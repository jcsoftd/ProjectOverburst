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
                if (!strong && Serialized.FindProperty("parryable").boolValue)
                    yield return Source.name + ": 패링 가능 공격은 강공 예고가 필요합니다.";
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
