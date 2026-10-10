using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.MonsterTuner
{
    internal static partial class MonsterTunerWriter
    {
        internal static Action<string> FailureHook;
        internal static string FixtureRoot;
        internal sealed class Result
        {
            public bool Success, RolledBack;
            public string Message, Backup;
            public readonly List<string> Changed = new List<string>();
        }
        public static List<string> Validate(MonsterTunerSession session, MonsterTunerCatalog catalog)
        {
            var errors = new List<string>();
            if (session?.Definition == null) { errors.Add("몬스터 정의가 없습니다."); return errors; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) errors.Add("Play 또는 컴파일 중에는 저장할 수 없습니다.");
            if (session.Definition.ActorPrefab == null || session.Definition.Variant == null) errors.Add("프리팹 또는 크기 프로필 연결이 없습니다.");
            foreach (var stamp in session.stamps) if (!stamp.Matches()) errors.Add("원본이 바뀌었거나 다른 편집이 있습니다: " + stamp.path);
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.assetPath == AssetDatabase.GetAssetPath(session.Definition.ActorPrefab)) errors.Add("같은 몬스터의 Prefab Mode를 닫은 뒤 저장하세요.");
            foreach (var edit in session.edits)
            {
                var value = edit.after;
                if (value.type == SerializedPropertyType.Float && !Finite(value.number)) errors.Add(edit.label + ": 유한한 숫자를 입력하세요.");
                if (value.type == SerializedPropertyType.Vector3 && !Finite(value.vector)) errors.Add(edit.label + ": XYZ에 유한한 숫자를 입력하세요.");
                string name = edit.property.ToLowerInvariant();
                if (value.type == SerializedPropertyType.Float && (name.Contains("radius") || name.Contains("height") || name.Contains("scale") || name == "attackanimationduration") && value.number <= 0f) errors.Add(edit.label + ": 0보다 커야 합니다.");
                if (edit.target == "variant" && value.type == SerializedPropertyType.Vector3 && (value.vector.x <= 0f || value.vector.y <= 0f || value.vector.z <= 0f)) errors.Add(edit.label + ": 크기 XYZ는 모두 양수여야 합니다.");
                if (edit.target == "variant" && (name == "attackspeedmultiplier" || name == "movespeedmultiplier" || name == "healthmultiplier") && value.number <= 0f) errors.Add(edit.label + ": 0보다 커야 합니다.");
                if (name == "attackinterval" && value.number < 0f) errors.Add("공격간격은 0초 이상입니다.");
                if (name == "damagemultiplier" && value.number < 0f) errors.Add("피해 배율은 0 이상입니다.");
                if (name == "parrycuescale" && value.number <= 0f) errors.Add("패링 예고 크기는 0보다 커야 합니다.");
                if (name == "burnscale" && (value.number < .3f || value.number > 2f)) errors.Add("화상 크기 보정은 현재 게임 범위 0.3~2 안에서 입력하세요.");
                if (name == "hitnormalizedtime" && (value.number < .05f || value.number > .95f)) errors.Add("첫 타격 비율은 0.05~0.95입니다.");
                if (name == "hitangle" && (value.number < 0f || value.number > 360f)) errors.Add("공격 각도는 0~360도입니다.");
                if (value.type == SerializedPropertyType.ObjectReference && !string.IsNullOrEmpty(value.text) && value.text.StartsWith("actor:", StringComparison.Ordinal) && MonsterTunerAddress.Find(session.Definition.ActorPrefab.transform, value.text.Substring(6)) == null) errors.Add(edit.label + ": 몬스터 내부 부착점이 없습니다.");
                if (value.numbers != null && value.numbers.Any(n => !Finite(n))) errors.Add(edit.label + ": 타격 시점에 유한한 숫자를 입력하세요.");
                if (value.muzzles != null)
                {
                    foreach (var muzzle in value.muzzles)
                        if (muzzle.ability.Resolve() == null || string.IsNullOrEmpty(muzzle.socket.text) || !Finite(muzzle.offset)) errors.Add("머즐에는 공격·몬스터 내부 소켓·유효한 보정이 필요합니다.");
                }
            }
            var replacements = MonsterTunerAnimationBindings.Replacements(session);
            var bindings = MonsterTunerAnimationBindings.Read(session.Definition.AnimationProfile);
            foreach (var replacement in replacements)
            {
                if (replacement.Value == null) { errors.Add("모션 슬롯을 비울 수 없습니다: " + replacement.Key); continue; }
                string compatibility = MonsterTunerAnimationBindings.Compatibility(session.Definition, replacement.Value);
                if (!string.IsNullOrEmpty(compatibility)) errors.Add(compatibility);
                if (bindings.All(b => b.Key != replacement.Key)) errors.Add("모션 상태가 바뀌었습니다: " + replacement.Key);
                if (replacement.Key.Contains("Stunned_Loop") && !replacement.Value.isLooping) errors.Add("기절 모션은 반복 클립이어야 합니다.");
            }
            for (int i = 0; session.Definition.AbilitySet != null && i < session.Definition.AbilitySet.Count; i++)
            {
                // 기존 공격의 타이밍은 오라 등 다른 항목의 저장을 막지 않는다.
                if (!session.edits.Any(e => e.target == "ability:" + i)) continue;
                var original = session.Definition.AbilitySet.GetAbility(i);
                if (original == null) continue;
                var ability = Object.Instantiate(original);
                try
                {
                    session.Apply(ability, "ability:" + i);
                    if (ability.HasAttackCue && ability.UsesPacedTimeline
                        && ability.TryGetParryMotionWindow(0, out var firstWindow)
                        && (firstWindow.x <= ability.PreparationEnd || firstWindow.y > ability.HitNormalizedTime + .00001f))
                        errors.Add(ability.AbilityId + ": 패링 예고는 휘두르기 시작 뒤, 첫 타격 전에 있어야 합니다.");
                    float previous = ability.HitNormalizedTime;
                    for (int hit = 1; hit < ability.HitCount; hit++)
                    {
                        float time = ability.GetHitNormalizedTime(hit);
                        if (!Finite(time) || time <= previous || time > .95f) errors.Add(ability.AbilityId + ": 추가 타격은 첫 타격 뒤 오름차순이며 0.95 이하여야 합니다.");
                        previous = time;
                    }
                }
                finally { Object.DestroyImmediate(ability); }
            }
            if (errors.Count == 0 && session.edits.Any(e => e.target.StartsWith("component:", StringComparison.Ordinal)))
            {
                GameObject actor = null;
                try { actor = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(session.Definition.ActorPrefab)); session.ApplyActor(actor); ValidatePrefab(actor); }
                catch (Exception e) { errors.Add(e.Message); }
                finally { if (actor != null) PrefabUtility.UnloadPrefabContents(actor); }
            }
            return errors.Distinct().ToList();
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        public static List<string> Plan(MonsterTunerSession session, MonsterTunerCatalog catalog)
        {
            var result = new List<string>(); var definition = session.Definition;
            if (session.edits.Any(e => e.target == "variant")) result.Add("몬스터 능력치·크기 프로필" + (catalog.Uses(definition.Variant) > 1 ? " · 공유 " + catalog.Uses(definition.Variant) + "종 → 이 몬스터용 분리" : " · 현재 프로필"));
            if (session.edits.Any(e => e.target.StartsWith("component:", StringComparison.Ordinal))) result.Add("Actor 프리팹 · 기준점/판정/오라/예고/머즐의 변경 필드");
            if (session.edits.Any(e => e.target.StartsWith("ability:", StringComparison.Ordinal))) result.Add("공격 정의 · 공유 공격과 공격 세트는 이 몬스터용 분리");
            if (session.edits.Any(e => e.target == "animation")) result.Add("애니메이션 프로필 + 실제 Controller 모션 연결 · FBX/클립 원본 유지");
            result.Add("선택 몬스터: " + definition.DisplayName + " · 변경 " + session.edits.Count + "개");
            result.Add("정의: " + AssetDatabase.GetAssetPath(definition));
            foreach (var target in session.edits.Select(e => e.target).Distinct())
            {
                string path = AssetDatabase.GetAssetPath(session.Source(target));
                if (!string.IsNullOrEmpty(path)) result.Add(path);
            }
            foreach (var edit in session.edits) result.Add(edit.label + ": " + edit.before.Display + " → " + edit.after.Display);
            result.Add("분리 자산 폴더: Assets/ProjectOverburst/Resources/Enemies/Tuning/" + SafeName(definition.EnemyId));
            return result;
        }

        public static Result Save(MonsterTunerSession session, MonsterTunerCatalog catalog)
        {
            var result = new Result(); var errors = Validate(session, catalog);
            if (errors.Count > 0) { result.Message = string.Join("\n", errors); return result; }
            if (!session.Dirty) { result.Success = true; result.Message = "저장할 변경이 없습니다."; return result; }
            var definition = session.Definition;
            var historyChanges = session.edits.Select(e => JsonUtility.FromJson<MonsterTunerPatch>(JsonUtility.ToJson(e))).ToList();
            if (FixtureRoot != null && !AssetDatabase.GetAssetPath(definition).StartsWith(FixtureRoot + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("검증 저장 범위 밖의 정의입니다.");
            var snapshots = new Dictionary<Object, Object>(); var created = new List<string>();
            GameObject snapshotPrefab = null, workingPrefab = null;
            string sourcePrefabPath = AssetDatabase.GetAssetPath(definition.ActorPrefab), finalPrefabPath = sourcePrefabPath;
            bool prefabSaved = false;
            string folder = DestinationFolder(definition);
            result.Backup = Path.Combine(MonsterTunerSession.OutputRoot, "Saves", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + SafeName(definition.EnemyId));
            Directory.CreateDirectory(result.Backup);
            File.WriteAllText(Path.Combine(result.Backup, "changes.json"), JsonUtility.ToJson(session, true));
            foreach (var stamp in session.stamps)
            {
                if (stamp.fileOnly) continue;
                if (!File.Exists(stamp.path)) continue;
                string backupFile = Path.Combine(result.Backup, stamp.path.Replace('/', '_'));
                File.Copy(stamp.path, backupFile, false);
                if (File.Exists(stamp.path + ".meta")) File.Copy(stamp.path + ".meta", backupFile + ".meta", false);
            }
            try
            {
                Snapshot(definition); EnsureFolder(folder);
                var remappedAbilities = new Dictionary<EnemyAbilityDefinition, EnemyAbilityDefinition>();
                if (session.edits.Any(e => e.target == "variant"))
                {
                    var variant = definition.Variant;
                    bool shared = catalog.Uses(variant) > 1;
                    var destination = shared ? Clone(variant, folder + "/EVP_Tuning.asset") : variant;
                    if (!shared) Snapshot(destination);
                    session.Apply(destination, "variant"); SaveAsset(destination);
                    SetReference(definition, "variant", destination);
                }
                FailureHook?.Invoke("after-variant");
                var originalSet = definition.AbilitySet;
                if (session.edits.Any(e => e.target.StartsWith("ability:", StringComparison.Ordinal)))
                {
                    var setSerialized = new SerializedObject(definition);
                    bool inherited = setSerialized.FindProperty("abilitySet").objectReferenceValue == null;
                    var destinationSet = catalog.Uses(originalSet) > 1 || inherited ? Clone(originalSet, folder + "/EAS_Tuning.asset") : originalSet;
                    if (destinationSet == originalSet) Snapshot(destinationSet);
                    var setFields = new SerializedObject(destinationSet); var array = setFields.FindProperty("abilities");
                    for (int i = 0; i < originalSet.Count; i++)
                    {
                        if (!session.edits.Any(e => e.target == "ability:" + i)) continue;
                        var original = originalSet.GetAbility(i);
                        var destination = catalog.Uses(original) > 1 ? Clone(original, folder + "/EAD_Tuning_" + i + ".asset") : original;
                        if (destination == original) Snapshot(destination);
                        session.Apply(destination, "ability:" + i); SaveAsset(destination);
                        array.GetArrayElementAtIndex(i).objectReferenceValue = destination;
                        if (destination != original) remappedAbilities[original] = destination;
                    }
                    setFields.ApplyModifiedPropertiesWithoutUndo(); SaveAsset(destinationSet);
                    SetReference(definition, "abilitySet", destinationSet);
                }
                FailureHook?.Invoke("after-abilities");
                if (session.edits.Any(e => e.target == "animation"))
                {
                    var original = definition.AnimationProfile;
                    string controllerPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/AC_Tuning"
                        + (MonsterTunerAnimationBindings.CanUseOverrides(MonsterTunerAnimationBindings.Read(original), MonsterTunerAnimationBindings.Replacements(session)) ? ".overrideController" : ".controller"));
                    // Register the proposed path before creation so failures inside the controller writer remain recoverable.
                    created.Add(controllerPath);
                    var controller = MonsterTunerAnimationBindings.SaveController(session, controllerPath);
                    var destination = Clone(original, folder + "/EAP_Tuning.asset");
                    session.Apply(destination, "animation"); MonsterTunerAnimationBindings.SynchronizeProfile(session, destination, controller); SaveAsset(destination);
                    SetReference(definition, "animationProfile", destination);
                }
                FailureHook?.Invoke("after-animation");
                bool actorEdits = session.edits.Any(e => e.target.StartsWith("component:", StringComparison.Ordinal)) || remappedAbilities.Count > 0;
                if (actorEdits)
                {
                    snapshotPrefab = PrefabUtility.LoadPrefabContents(sourcePrefabPath);
                    if (catalog.Uses(definition.ActorPrefab) > 1)
                    {
                        finalPrefabPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/PF_Tuning.prefab");
                        if (!AssetDatabase.CopyAsset(sourcePrefabPath, finalPrefabPath)) throw new InvalidOperationException("공유 프리팹을 분리하지 못했습니다.");
                        created.Add(finalPrefabPath);
                    }
                    workingPrefab = PrefabUtility.LoadPrefabContents(finalPrefabPath);
                    session.ApplyActor(workingPrefab);
                    foreach (var component in workingPrefab.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) throw new InvalidOperationException("프리팹에 Missing Script가 있습니다.");
                        var serialized = new SerializedObject(component); var iterator = serialized.GetIterator();
                        bool changed = false;
                        while (iterator.Next(true)) if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue is EnemyAbilityDefinition ability && remappedAbilities.TryGetValue(ability, out var replacement))
                        { iterator.objectReferenceValue = replacement; changed = true; }
                        if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
                        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    }
                    ValidatePrefab(workingPrefab);
                    FailureHook?.Invoke("before-prefab-save");
                    PrefabUtility.SaveAsPrefabAsset(workingPrefab, finalPrefabPath, out bool saved);
                    if (!saved) throw new InvalidOperationException("프리팹을 저장하지 못했습니다.");
                    prefabSaved = true; result.Changed.Add(finalPrefabPath);
                    if (finalPrefabPath != sourcePrefabPath) SetReference(definition, "actorPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(finalPrefabPath).GetComponent<EnemyActor>());
                }
                FailureHook?.Invoke("before-definition-save");
                SaveAsset(definition);
                FailureHook?.Invoke("after-definition-save");
                // Read persisted assets, not the preview objects, before accepting the new baseline.
                if (definition.ActorPrefab == null || definition.Variant == null || definition.AnimationProfile?.RuntimeController == null) throw new InvalidOperationException("저장 후 필수 연결이 누락됐습니다.");
                if (prefabSaved)
                {
                    var check = PrefabUtility.LoadPrefabContents(finalPrefabPath);
                    try { ValidatePrefab(check); VerifyActorEdits(session, check, remappedAbilities); } finally { PrefabUtility.UnloadPrefabContents(check); }
                }
                File.WriteAllText(Path.Combine(result.Backup, "saved-paths.json"), Newtonsoft.Json.JsonConvert.SerializeObject(result.Changed.Concat(created).Distinct().ToArray(), Newtonsoft.Json.Formatting.Indented));
                MonsterTunerHistory.Record(session, historyChanges, result.Backup); session.Saved();
                catalog.Refresh(); result.Success = true; result.Message = definition.DisplayName + " 저장·재로드 확인 완료";
            }
            catch (Exception e)
            {
                result.Message = "저장 실패: " + e.Message;
                try
                {
                    foreach (var pair in snapshots) { EditorUtility.CopySerialized(pair.Value, pair.Key); AssetDatabase.SaveAssetIfDirty(pair.Key); }
                    if (prefabSaved && finalPrefabPath == sourcePrefabPath && snapshotPrefab != null) PrefabUtility.SaveAsPrefabAsset(snapshotPrefab, sourcePrefabPath);
                    foreach (string path in created.AsEnumerable().Reverse()) if (AssetDatabase.LoadMainAssetAtPath(path) != null && !AssetDatabase.MoveAssetToTrash(path)) throw new IOException("저장 복구 자산을 휴지통으로 이동하지 못했습니다: " + path);
                    session.Persist(); result.RolledBack = true; result.Message += "\n이번 저장을 복구했습니다. 편집 사본은 유지합니다.";
                }
                catch (Exception recovery) { result.Message += "\n복구 실패: " + recovery.Message + "\n백업: " + result.Backup; }
                File.WriteAllText(Path.Combine(result.Backup, "failure.txt"), result.Message);
            }
            finally
            {
                if (workingPrefab != null) PrefabUtility.UnloadPrefabContents(workingPrefab);
                if (snapshotPrefab != null) PrefabUtility.UnloadPrefabContents(snapshotPrefab);
                foreach (var snapshot in snapshots.Values) Object.DestroyImmediate(snapshot);
            }
            return result;
            void Snapshot(Object asset) { if (!snapshots.ContainsKey(asset)) { var copy = Object.Instantiate(asset); copy.name = asset.name; copy.hideFlags = asset.hideFlags; snapshots.Add(asset, copy); } }
            T Clone<T>(T asset, string candidate) where T : Object
            {
                var copy = Object.Instantiate(asset); copy.name = asset.name + "_Tuning";
                string path = AssetDatabase.GenerateUniqueAssetPath(candidate); created.Add(path); AssetDatabase.CreateAsset(copy, path); return copy;
            }
            void SaveAsset(Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); result.Changed.Add(AssetDatabase.GetAssetPath(asset)); }
        }
        private static void SetReference(Object asset, string field, Object value)
        {
            var serialized = new SerializedObject(asset); serialized.FindProperty(field).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void VerifyActorEdits(MonsterTunerSession session, GameObject actor, Dictionary<EnemyAbilityDefinition, EnemyAbilityDefinition> remapped)
        {
            foreach (var edit in session.edits.Where(e => e.target.StartsWith("component:", StringComparison.Ordinal)))
            {
                var target = MonsterTunerAddress.ResolveComponent(actor, edit.target);
                if (target == null) throw new InvalidOperationException("저장 후 대상을 찾을 수 없습니다: " + edit.label);
                var actual = MonsterTunerValue.Read(new SerializedObject(target).FindProperty(edit.property), actor);
                var expected = JsonUtility.FromJson<MonsterTunerValue>(JsonUtility.ToJson(edit.after));
                if (expected.muzzles != null) foreach (var muzzle in expected.muzzles)
                    if (muzzle.ability.Resolve() is EnemyAbilityDefinition original && remapped.TryGetValue(original, out var replacement))
                        muzzle.ability.text = GlobalObjectId.GetGlobalObjectIdSlow(replacement).ToString();
                if (!actual.EqualsValue(expected)) throw new InvalidOperationException("저장 후 값이 다릅니다: " + edit.label + "\n실제 " + JsonUtility.ToJson(actual) + "\n예상 " + JsonUtility.ToJson(expected));
            }
        }
        private static void ValidatePrefab(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true)) if (component == null) throw new InvalidOperationException("Missing Script가 있습니다.");
            foreach (var capsule in root.GetComponentsInChildren<CapsuleCollider>(true))
                if (!Finite(capsule.radius) || !Finite(capsule.height) || capsule.radius <= 0f || capsule.height < capsule.radius * 2f || capsule.direction < 0 || capsule.direction > 2) throw new InvalidOperationException("Capsule 높이는 반경의 두 배 이상이어야 합니다.");
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true)) if (!Finite(box.size) || box.size.x <= 0f || box.size.y <= 0f || box.size.z <= 0f) throw new InvalidOperationException("Box 크기는 모두 양수여야 합니다.");
            foreach (var sphere in root.GetComponentsInChildren<SphereCollider>(true)) if (!Finite(sphere.radius) || sphere.radius <= 0f) throw new InvalidOperationException("Sphere 반경은 양수여야 합니다.");
            var executor = root.GetComponent<EnemyThemeSpecialExecutor>();
            var warning = root.GetComponent<EnemyStrongAttackWarning>();
            if (warning != null)
            {
                var socket = new SerializedObject(warning).FindProperty("cueSocket").objectReferenceValue as Transform;
                if (socket != null && socket != root.transform && !socket.IsChildOf(root.transform)) throw new InvalidOperationException("패링 부착점은 몬스터 내부여야 합니다.");
            }
            if (executor != null)
            {
                var muzzles = new SerializedObject(executor).FindProperty("muzzleOverrides");
                var keys = new HashSet<Object>();
                for (int i = 0; i < muzzles.arraySize; i++)
                {
                    var entry = muzzles.GetArrayElementAtIndex(i); var ability = entry.FindPropertyRelative("ability").objectReferenceValue;
                    var socket = entry.FindPropertyRelative("socket").objectReferenceValue as Transform;
                    if (ability == null || !keys.Add(ability) || socket == null || (socket != root.transform && !socket.IsChildOf(root.transform))) throw new InvalidOperationException("머즐의 공격 또는 몬스터 내부 소켓이 누락/중복됐습니다.");
                }
            }
        }
        private static string SafeName(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
        private static void EnsureFolder(string path)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
