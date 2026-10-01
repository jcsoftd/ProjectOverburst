using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.MonsterTuner
{
    [Serializable]
    internal sealed class MonsterTunerStamp
    {
        public string path, hash, memory;
        public bool fileOnly;
        public static MonsterTunerStamp Capture(string path)
        {
            return new MonsterTunerStamp { path = path, hash = FileHash(path), memory = Memory(path) };
        }
        public bool Matches() => hash == FileHash(path) && (fileOnly || memory == Memory(path))
            && !Objects(path).Any(EditorUtility.IsDirty);
        public static string FileHash(string path)
        {
            if (!File.Exists(path)) return string.Empty;
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return Convert.ToBase64String(sha.ComputeHash(stream));
        }
        private static IEnumerable<Object> Objects(string path)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset == null) continue;
                yield return asset;
                if (asset is GameObject root) foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component != null) yield return component;
            }
        }
        private static string Memory(string path) => string.Join("\n", Objects(path)
            .Where(o => o != null).Select(o => EditorJsonUtility.ToJson(o)).OrderBy(s => s, StringComparer.Ordinal));
    }

    internal sealed class MonsterTunerSession : ScriptableObject
    {
        public string definitionGuid;
        public List<MonsterTunerPatch> edits = new List<MonsterTunerPatch>();
        public List<MonsterTunerStamp> stamps = new List<MonsterTunerStamp>();
        [NonSerialized] private bool enableRecovery = true;
        public EnemyDefinition Definition => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(definitionGuid));
        public bool Dirty => edits.Count > 0;
        public string RecoveryPath => Path.Combine(OutputRoot, "Recovery", definitionGuid + ".json");
        public static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Authoring/MonsterTuner"));
        public static MonsterTunerSession Create(EnemyDefinition definition, bool persistRecovery = true)
        {
            var session = CreateInstance<MonsterTunerSession>();
            session.hideFlags = HideFlags.HideAndDontSave;
            session.enableRecovery = persistRecovery;
            session.definitionGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition));
            session.ReadBaseline();
            return session;
        }
        public Object Source(string target)
        {
            var definition = Definition;
            if (definition == null) return null;
            if (target == "variant") return definition.Variant;
            if (target == "animation") return definition.AnimationProfile;
            if (target.StartsWith("ability:", StringComparison.Ordinal))
                return int.TryParse(target.Substring(8), out int index) ? definition.AbilitySet?.GetAbility(index) : null;
            return MonsterTunerAddress.ResolveComponent(definition.ActorPrefab != null ? definition.ActorPrefab.gameObject : null, target);
        }
        public MonsterTunerValue Value(string target, string property)
        {
            var edit = edits.Find(e => e.target == target && e.property == property);
            if (edit != null) return JsonUtility.FromJson<MonsterTunerValue>(JsonUtility.ToJson(edit.after));
            if (target == "animation" && property.StartsWith("motion:", StringComparison.Ordinal))
            {
                var binding = MonsterTunerAnimationBindings.Read(Definition.AnimationProfile).Find(b => b.Key == property.Substring(7));
                if (binding == null) throw new InvalidOperationException("실제 모션 슬롯을 찾을 수 없습니다.");
                return new MonsterTunerValue { type = SerializedPropertyType.ObjectReference, text = binding.Actual != null ? GlobalObjectId.GetGlobalObjectIdSlow(binding.Actual).ToString() : string.Empty };
            }
            var source = Source(target);
            if (source == null && target.Contains(":EnemyStrongAttackWarning,"))
            {
                if (property == "cueOffset") return new MonsterTunerValue { type = SerializedPropertyType.Vector3, vector = Vector3.zero };
                if (property == "cueScale") return new MonsterTunerValue { type = SerializedPropertyType.Float, number = 1f };
                if (property == "cueSocket") return new MonsterTunerValue { type = SerializedPropertyType.ObjectReference, text = string.Empty };
            }
            if (source == null) throw new InvalidOperationException("편집 원본이 없습니다: " + target);
            return MonsterTunerValue.Read(new SerializedObject(source).FindProperty(property), Definition.ActorPrefab.gameObject);
        }
        public void Set(string target, string property, MonsterTunerValue value, string label, bool recordUndo = true)
        {
            if (recordUndo) Undo.RecordObject(this, label);
            var edit = edits.Find(e => e.target == target && e.property == property);
            var before = edit != null ? edit.before : Value(target, property);
            if (value.EqualsValue(before)) { if (edit != null) edits.Remove(edit); }
            else if (edit == null) edits.Add(new MonsterTunerPatch { target = target, property = property, before = before, after = value, label = label });
            else edit.after = value;
            EditorUtility.SetDirty(this);
            Persist();
        }
        public void ResetField(string target, string property)
        {
            Undo.RecordObject(this, "항목 되돌리기");
            edits.RemoveAll(e => e.target == target && e.property == property);
            Persist();
        }
        public void ReadBaseline()
        {
            stamps.Clear();
            var definition = Definition;
            if (definition == null) return;
            var assets = new List<Object> { definition, definition.ActorPrefab, definition.Variant,
                definition.AnimationProfile, definition.AbilitySet, definition.AnimationProfile?.RuntimeController };
            for (int i = 0; definition.AbilitySet != null && i < definition.AbilitySet.Count; i++) assets.Add(definition.AbilitySet.GetAbility(i));
            foreach (string path in assets.Where(o => o != null).Select(AssetDatabase.GetAssetPath).Distinct())
                if (!string.IsNullOrEmpty(path)) stamps.Add(MonsterTunerStamp.Capture(path));
            foreach (string path in MonsterTunerAnimationBindings.Read(definition.AnimationProfile).Select(b => AssetDatabase.GetAssetPath(b.Actual)).Where(p => !string.IsNullOrEmpty(p)).Distinct())
                if (stamps.All(s => s.path != path)) stamps.Add(new MonsterTunerStamp { path = path, hash = MonsterTunerStamp.FileHash(path), memory = string.Empty, fileOnly = true });
        }
        public void Apply(Object destination, string target, GameObject actor = null)
        {
            var relevant = edits.Where(e => e.target == target && !e.property.StartsWith("motion:", StringComparison.Ordinal)).ToArray();
            if (relevant.Length == 0 || destination == null) return;
            var serialized = new SerializedObject(destination);
            foreach (var edit in relevant) edit.after.Write(serialized.FindProperty(edit.property), actor);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        public void ApplyActor(GameObject actor)
        {
            foreach (string target in edits.Where(e => e.target.StartsWith("component:", StringComparison.Ordinal)).Select(e => e.target).Distinct())
            {
                var destination = MonsterTunerAddress.ResolveComponent(actor, target);
                if (destination == null && target.StartsWith("component::EnemyStrongAttackWarning,", StringComparison.Ordinal))
                    destination = actor.AddComponent<EnemyStrongAttackWarning>();
                if (destination == null) throw new InvalidOperationException("편집 대상 컴포넌트를 찾을 수 없습니다: " + target);
                Apply(destination, target, actor);
            }
        }
        public void Persist()
        {
            if (!enableRecovery) return;
            Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath));
            if (Dirty) File.WriteAllText(RecoveryPath, JsonUtility.ToJson(this, true), new System.Text.UTF8Encoding(false));
            else if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath);
        }
        public bool Restore()
        {
            if (!File.Exists(RecoveryPath)) return false;
            JsonUtility.FromJsonOverwrite(File.ReadAllText(RecoveryPath), this);
            foreach (var patch in edits)
            {
                string kind = patch.property == "muzzleOverrides" ? "muzzles" : patch.property == "additionalHitNormalizedTimes" ? "numbers" : null;
                if (kind != null) { patch.before.arrayKind = kind; patch.after.arrayKind = kind; }
            }
            return Dirty;
        }
        public void Saved()
        {
            edits.Clear(); ReadBaseline(); Persist(); Undo.ClearUndo(this);
        }
        public void Discard()
        {
            Undo.RecordObject(this, "몬스터 편집 폐기");
            edits.Clear(); ReadBaseline(); Persist();
        }
    }
}
