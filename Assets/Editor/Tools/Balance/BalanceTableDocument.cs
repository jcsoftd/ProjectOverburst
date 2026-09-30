using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorBalance
{
    // A working copy keeps typing separate from the live ScriptableObject and from disk.
    public sealed class BalanceTableDocument : IDisposable
    {
        public readonly Object Source;
        public readonly Object Draft;
        public readonly SerializedObject Serialized;
        private string baseline;
        public readonly bool SourceWasDirty;
        private readonly Dictionary<string, BalanceField> fields = new Dictionary<string, BalanceField>();
        public IEnumerable<BalanceField> Fields => fields.Values;
        public bool IsChanged => fields.Values.Any(f => f.Changed);
        public bool HasConflict => EditorJsonUtility.ToJson(Source) != baseline;
        public BalanceTableDocument(Object source)
        {
            Source = source;
            SourceWasDirty = EditorUtility.IsDirty(source);
            baseline = EditorJsonUtility.ToJson(source);
            Draft = Object.Instantiate(source);
            Draft.name = source.name;
            Draft.hideFlags = HideFlags.HideAndDontSave;
            Serialized = new SerializedObject(Draft);
        }
        // 소형(일반 등급 + 가벼운 피격 체급) 몬스터의 공격 목록에 든 공격. 도메인 리로드마다 한 번만 모은다.
        private static HashSet<Object> smallEnemyAbilities;
        private static HashSet<Object> SmallEnemyAbilities()
        {
            if (smallEnemyAbilities != null) return smallEnemyAbilities;
            smallEnemyAbilities = new HashSet<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/ProjectOverburst/Resources/Enemies" }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null || definition.AbilitySet == null || definition.Grade == null
                    || definition.Grade.GradeType != EnemyGradeType.Normal
                    || definition.MovementProfile == null || definition.MovementProfile.HitWeightProfile == null
                    || definition.MovementProfile.HitWeightProfile.Weight != EnemyHitWeight.Light) continue;
                for (int i = 0; i < definition.AbilitySet.Count; i++)
                    if (definition.AbilitySet.GetAbility(i) != null) smallEnemyAbilities.Add(definition.AbilitySet.GetAbility(i));
            }
            return smallEnemyAbilities;
        }
        public BalanceField Field(string path, float min, float max, float displayFactor = 1f)
        {
            if (fields.TryGetValue(path, out var existing)) return existing;
            var property = Serialized.FindProperty(path);
            if (property == null) throw new InvalidOperationException(Source.name + ": 없는 필드 " + path);
            var field = new BalanceField(this, path, min, max, displayFactor);
            fields.Add(path, field);
            return field;
        }
        public IEnumerable<string> Validate()
        {
            if (IsChanged && SourceWasDirty) yield return Source.name + ": 툴을 열기 전의 미저장 변경이 있습니다. 해당 작업을 먼저 정리하세요.";
            if (IsChanged && HasConflict) yield return Source.name + ": 원본이 외부에서 변경됨. 새로고침 후 다시 편집하세요.";
            foreach (var f in Fields)
                if (f.Changed && (!float.IsFinite(f.Value) || f.Value < f.Minimum || f.Value > f.Maximum))
                    yield return Source.name + "/" + f.Path + $": 허용 범위 {f.Minimum}~{f.Maximum}";
            if (Draft is EnemyAbilityDefinition && IsChanged)
            {
                bool strong = Serialized.FindProperty("telegraphedStrongAttack").boolValue;
                if (strong && Serialized.FindProperty("minimumWarningTime").floatValue < .30f)
                    yield return Source.name + ": 강공 전조는 마지막 대응 신호 0.30초 이상이어야 합니다.";
                // 2026-10-01: 패링은 중형·대형의 근접 강공(휘두르기·돌진·내려찍기)에만 둔다. 평타·원거리는 패링 불가, 소형은 강공 없음.
                bool melee = EnemyAbilityDefinition.IsMeleeExecution(
                    (EnemyAbilityExecutionMode)Serialized.FindProperty("executionMode").intValue);
                if (Serialized.FindProperty("parryable").boolValue && !(strong && melee))
                    yield return Source.name + ": 패링은 근접 강공에만 켭니다(평타·원거리는 패링되지 않음).";
                if (strong && !melee)
                    yield return Source.name + ": 원거리·기타 공격은 강공으로 두지 않습니다(강공은 근접 공격만).";
                if (strong && SmallEnemyAbilities().Contains(Source))
                    yield return Source.name + ": 소형 몬스터에는 강공을 두지 않습니다.";
            }
            if (Draft is GearItemData gear && IsChanged && gear.catalogMinLevel > gear.catalogMaxLevel)
                yield return Source.name + ": 등장 최소 레벨이 최대 레벨보다 큽니다.";
        }
        public void Apply()
        {
            if (!IsChanged) return;
            var errors = Validate().ToArray();
            if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
            Undo.RecordObject(Source, "밸런스 표 적용");
            using (var target = new SerializedObject(Source))
            {
                foreach (var f in Fields.Where(f => f.Changed)) f.Write(target.FindProperty(f.Path), f.Value);
                target.ApplyModifiedProperties();
            }
            EditorUtility.SetDirty(Source);
            Rebase();
        }
        public void Rebase()
        {
            baseline = EditorJsonUtility.ToJson(Source);
            EditorJsonUtility.FromJsonOverwrite(baseline, Draft);
            Draft.hideFlags = HideFlags.HideAndDontSave;
            Serialized.Update();
            foreach (var f in Fields) f.MarkBaseline();
        }
        public void Dispose() { Serialized.Dispose(); if (Draft != null) Object.DestroyImmediate(Draft); }
    }

    public sealed class BalanceField
    {
        public readonly BalanceTableDocument Document;
        public readonly string Path;
        public readonly float Minimum, Maximum, DisplayFactor;
        private float original;
        public BalanceField(BalanceTableDocument document, string path, float min, float max, float factor)
        { Document = document; Path = path; Minimum = min; Maximum = max; DisplayFactor = factor; MarkBaseline(); }
        public SerializedPropertyType Type => Document.Serialized.FindProperty(Path).propertyType;
        public bool Changed => !Value.Equals(original);
        public float Value
        {
            get
            {
                var p = Document.Serialized.FindProperty(Path);
                return p.propertyType == SerializedPropertyType.Boolean ? (p.boolValue ? 1f : 0f)
                    : p.propertyType == SerializedPropertyType.Integer ? p.intValue : p.floatValue;
            }
            set
            {
                Write(Document.Serialized.FindProperty(Path), value);
                Document.Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        public void Write(SerializedProperty p, float value)
        {
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value > .5f;
            else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(value);
            else p.floatValue = value;
        }
        public void MarkBaseline() => original = Value;
    }
}
