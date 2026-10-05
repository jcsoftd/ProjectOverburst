using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.BossMaker
{
    [Serializable]
    internal sealed class BossMakerRecovery
    {
        public string collectionGuid, guid, materialJson, abilityJson, sourceMaterialJson, sourceAbilityJson;
    }

    internal sealed class BossMakerDraft : IDisposable
    {
        public EnemyBossAttackMaterial Source { get; }
        public EnemyAbilityDefinition AbilitySource { get; }
        public EnemyBossAttackMaterial Material { get; }
        public EnemyAbilityDefinition Ability { get; }
        public string SourceMaterialJson { get; private set; }
        public string SourceAbilityJson { get; private set; }
        string materialBaseline, abilityBaseline;
        public bool Dirty => Material != null && (EditorJsonUtility.ToJson(Material) != materialBaseline
            || EditorJsonUtility.ToJson(Ability) != abilityBaseline);
        public bool Conflict => EditorJsonUtility.ToJson(Source) != SourceMaterialJson
            || EditorJsonUtility.ToJson(AbilitySource) != SourceAbilityJson;
        public float FrameCount => Material.runtimeClip != null ? Material.runtimeClip.length * Material.runtimeClip.frameRate : 1f;

        public BossMakerDraft(EnemyBossAttackMaterial source, BossMakerRecovery recovery = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            AbilitySource = source.ability ?? throw new InvalidOperationException("공격의 능력 연결이 없습니다.");
            SourceMaterialJson = EditorJsonUtility.ToJson(source);
            SourceAbilityJson = EditorJsonUtility.ToJson(AbilitySource);
            Material = Object.Instantiate(source); Material.name = source.name; Material.hideFlags = HideFlags.HideAndDontSave;
            Ability = Object.Instantiate(AbilitySource); Ability.name = AbilitySource.name; Ability.hideFlags = HideFlags.HideAndDontSave;
            Material.ability = Ability;
            EnsureParries(); MarkBaseline();
            if (recovery != null)
            {
                EditorJsonUtility.FromJsonOverwrite(recovery.materialJson, Material);
                EditorJsonUtility.FromJsonOverwrite(recovery.abilityJson, Ability);
                Material.ability = Ability;
                Material.hideFlags = Ability.hideFlags = HideFlags.HideAndDontSave;
                SourceMaterialJson = recovery.sourceMaterialJson; SourceAbilityJson = recovery.sourceAbilityJson;
                EnsureParries();
            }
        }
        void MarkBaseline() { materialBaseline = EditorJsonUtility.ToJson(Material); abilityBaseline = EditorJsonUtility.ToJson(Ability); }
        public void EnsureParries()
        {
            if (Material.tuning == null) Material.tuning = new EnemyBossAttackTuning();
            int count = Material.strikes?.Length ?? 0;
            if (Material.tuning.parries != null && Material.tuning.parries.Length == count) return;
            var previous = Material.tuning.parries;
            Material.tuning.parries = new EnemyBossStrikeParryTuning[count];
            for (int i = 0; i < count; i++)
                Material.tuning.parries[i] = previous != null && i < previous.Length && previous[i] != null
                    ? previous[i] : new EnemyBossStrikeParryTuning();
        }
        public void Edit(string label, Action edit)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObjects(new Object[] { Material, Ability }, label);
            edit(); CanonicalizeGeometry(); SyncHits();
            Undo.FlushUndoRecordObjects();
        }
        public void SyncHits()
        {
            using (var serialized = new SerializedObject(Ability))
            {
                serialized.FindProperty("hitNormalizedTime").floatValue = Material.strikes[0].impact;
                var extra = serialized.FindProperty("additionalHitNormalizedTimes"); extra.arraySize = Material.strikes.Length - 1;
                for (int i = 1; i < Material.strikes.Length; i++) extra.GetArrayElementAtIndex(i - 1).floatValue = Material.strikes[i].impact;
                serialized.FindProperty("attackAnimationDuration").floatValue = Material.runtimeClip.length;
                float extent = Material.strikes.Max(s => new Vector2(s.localOrigin.x, s.localOrigin.z).magnitude
                    + (s.shape == GroundIndicatorShape.Rectangle ? Mathf.Sqrt(s.length * s.length + s.width * s.width * .25f) : s.radius));
                // Preserve a deliberately larger selection range; never leave a new shape outside its gate.
                var range = serialized.FindProperty("range"); range.floatValue = Mathf.Max(range.floatValue, extent + .5f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        void CanonicalizeGeometry()
        {
            foreach (var s in Material.strikes)
            {
                if (s.shape == GroundIndicatorShape.Circle) { s.angle = 360f; s.innerRadius = 0f; }
                if (s.shape == GroundIndicatorShape.Donut) s.angle = 360f;
                if (s.shape == GroundIndicatorShape.Sector) s.innerRadius = Mathf.Max(s.innerRadius, s.radius * .05f);
            }
        }
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Material.runtimeClip == null || Material.runtimeClip.isLooping || Material.runtimeClip.name.EndsWith("_RM", StringComparison.Ordinal)) errors.Add("루프가 없는 RM 제외 게임용 공격 클립이 필요합니다.");
            if (!Material.IsValid) errors.Add("타격 순서·접촉 창·형태·클립 길이와 능력 연결을 확인하세요.");
            if (!Material.tuning.Validate(Material.strikes)) errors.Add("피해/속도 배율과 패링 구간을 확인하세요. 패링 끝은 타격보다 늦을 수 없습니다.");
            for (int i = 0; i < Material.strikes.Length; i++)
            {
                var s = Material.strikes[i];
                if (s.impact < .05f || s.impact > .95f) errors.Add($"{i + 1}타 시점은 클립의 5~95% 안에 있어야 합니다.");
                if (s.shape == GroundIndicatorShape.Sector && s.innerRadius + .000001f < s.radius * .05f) errors.Add($"{i + 1}타 부채꼴 내부 반경은 예고와 같은 바깥 반경의 5% 이상이어야 합니다.");
            }
            foreach (string path in new[] { "damage", "referencePatternDamagePercent", "cooldown", "range", "minimumRange", "minimumWarningTime", "minimumRecoveryTime", "preparationDuration", "releaseDuration", "recoveryDuration" })
                using (var serialized = new SerializedObject(Ability))
                { float value = serialized.FindProperty(path).floatValue; if (!EnemyBossMaterialStrike.Finite(value) || value < 0f) errors.Add(path + " 값은 유한한 0 이상의 수여야 합니다."); }
            using (var serialized = new SerializedObject(Ability))
            {
                if (serialized.FindProperty("minimumRange").floatValue > serialized.FindProperty("range").floatValue) errors.Add("선택 최소 거리는 최대 거리보다 클 수 없습니다.");
                if (serialized.FindProperty("referencePatternDamagePercent").floatValue > 100f) errors.Add("기준 체력 비율은 0~100% 안에서 지정하세요.");
                float weight = serialized.FindProperty("weight").floatValue;
                if (!EnemyBossMaterialStrike.Finite(weight) || weight < 0) errors.Add("선택 가중치는 유한한 0 이상의 수여야 합니다.");
            }
            if (!EnemyBossMaterialStrike.Finite(Material.projectileSpeed) || Material.projectileSpeed <= 0f
                || !EnemyBossMaterialStrike.Finite(Material.projectileRadius) || Material.projectileRadius <= 0f
                || !EnemyBossMaterialStrike.Finite(Material.flightSeconds) || Material.flightSeconds <= 0f
                || !EnemyBossMaterialStrike.Finite(Material.arcHeight) || Material.arcHeight < 0f
                || !EnemyBossMaterialStrike.Finite(Material.advanceDistance) || Material.advanceDistance < 0f) errors.Add("비행체와 이동 값은 유한한 유효 범위여야 합니다.");
            if (!EnemyBossMaterialStrike.Finite(Material.aimLockLeadSeconds) || Material.aimLockLeadSeconds < 0f
                || !EnemyBossMaterialStrike.Finite(Material.muzzleOffset.x) || !EnemyBossMaterialStrike.Finite(Material.muzzleOffset.y)
                || !EnemyBossMaterialStrike.Finite(Material.muzzleOffset.z)) errors.Add("조준 고정 시간과 발사 오프셋을 확인하세요.");
            if (Conflict) errors.Add("원본이 다른 곳에서 변경됐습니다. 다시 불러온 후 편집하세요.");
            return errors;
        }
        public Vector2 ParryWindow(int phase, float baseSpeed = 1f)
        {
            var p = Material.tuning.parries[phase];
            if (p.overrideWindow) return new Vector2(p.startNormalized, p.endNormalized);
            float impact = Material.strikes[phase].impact;
            float seconds = Mathf.Max(0f, Ability.ResolvePacedTime(impact, Material.AnimationSpeedMultiplier * baseSpeed) - EnemyAbilityController.ParryLeadSeconds);
            return new Vector2(ProgressAtTime(seconds, baseSpeed), impact);
        }
        public float ProgressAtTime(float seconds, float baseSpeed = 1f)
        {
            float low = 0f, high = 1f, speed = Material.AnimationSpeedMultiplier * baseSpeed;
            for (int i = 0; i < 24; i++) { float mid = (low + high) * .5f; if (Ability.ResolvePacedTime(mid, speed) < seconds) low = mid; else high = mid; }
            return (low + high) * .5f;
        }
        public BossMakerRecovery Capture() => new BossMakerRecovery { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Source)),
            materialJson = EditorJsonUtility.ToJson(Material), abilityJson = EditorJsonUtility.ToJson(Ability),
            sourceMaterialJson = SourceMaterialJson, sourceAbilityJson = SourceAbilityJson };
        public void AcceptSaved()
        {
            SourceMaterialJson = EditorJsonUtility.ToJson(Source); SourceAbilityJson = EditorJsonUtility.ToJson(AbilitySource);
            MarkBaseline();
        }
        public bool RefreshCleanSource()
        {
            if (Dirty || !Conflict) return false;
            EditorUtility.CopySerialized(Source, Material); EditorUtility.CopySerialized(AbilitySource, Ability);
            Material.ability = Ability; Material.hideFlags = Ability.hideFlags = HideFlags.HideAndDontSave;
            EnsureParries(); AcceptSaved(); return true;
        }
        public void Dispose()
        {
            if (Material != null) { Undo.ClearUndo(Material); Object.DestroyImmediate(Material); }
            if (Ability != null) { Undo.ClearUndo(Ability); Object.DestroyImmediate(Ability); }
        }
    }

    internal sealed class BossMakerSession : IDisposable
    {
        public EnemyBossMaterialCollection Collection { get; }
        public List<BossMakerDraft> Drafts { get; } = new List<BossMakerDraft>();
        readonly List<EnemyBossCompositePatternSet> composites = new List<EnemyBossCompositePatternSet>();
        public List<CrustaspikanEncounterSettings> Encounters { get; } = new List<CrustaspikanEncounterSettings>();
        readonly HashSet<string> bones;
        public bool Dirty => Drafts.Any(d => d.Dirty);
        public BossMakerSession(EnemyBossMaterialCollection collection, IEnumerable<BossMakerRecovery> recovery = null)
        {
            Collection = collection;
            bones = new HashSet<string>((collection.actorDefinition?.ActorPrefab != null
                ? collection.actorDefinition.ActorPrefab.GetComponentsInChildren<Transform>(true).Select(t => t.name)
                : Enumerable.Empty<string>()));
            try
            {
                foreach (var m in collection.attacks.Where(a => a != null))
                    Drafts.Add(new BossMakerDraft(m, recovery?.FirstOrDefault(r => r.guid == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m)))));
                RefreshRuntimeLinks();
            }
            catch { Dispose(); throw; }
        }
        public List<string> Validate(bool onlyDirty = false)
        {
            var errors = new List<string>();
            foreach (var d in Drafts.Where(d => !onlyDirty || d.Dirty))
            {
                errors.AddRange(d.Validate().Select(e => d.Source.displayName + ": " + e));
                if (d.Material.delivery == EnemyBossMaterialDelivery.Spit && !bones.Contains(d.Material.muzzleBone ?? ""))
                    errors.Add(d.Source.displayName + ": 모델에 발사 소켓이 없습니다. 뼈 이름을 다시 확인하세요.");
                if (d.Material.delivery == EnemyBossMaterialDelivery.Boulder
                    && (!bones.Contains(Collection.boulderLeftHandBone ?? "") || !bones.Contains(Collection.boulderRightHandBone ?? "")))
                    errors.Add(d.Source.displayName + ": 바위 투척 양손 뼈 연결을 확인하세요.");
            }
            ValidateComposites(errors, onlyDirty);
            return errors;
        }
        public EnemyBossCompositePatternSet CompositeFor(EnemyBossAttackMaterial material)
            => composites.FirstOrDefault(s => s != null && (s.throwMaterial == material
                || s.spitPatterns.Any(p => p != null && p.material == material)));
        void RefreshRuntimeLinks()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:EnemyBossCompositePatternSet"))
            {
                var source = AssetDatabase.LoadAssetAtPath<EnemyBossCompositePatternSet>(AssetDatabase.GUIDToAssetPath(guid));
                if (source == null || composites.Contains(source) || !Drafts.Any(d => source.throwMaterial == d.Source
                    || source.spitPatterns.Any(p => p != null && p.material == d.Source))) continue;
                composites.Add(source);
            }
            Encounters.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:CrustaspikanEncounterSettings"))
            {
                var settings = AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings != null && settings.materials == Collection) Encounters.Add(settings);
            }
        }
        void ValidateComposites(List<string> errors, bool onlyDirty)
        {
            foreach (var set in composites)
            {
                if (set == null) continue;
                foreach (var pattern in set.spitPatterns)
                {
                    if (pattern == null) continue;
                    var draft = Drafts.FirstOrDefault(d => d.Source == pattern.material && (!onlyDirty || d.Dirty));
                    if (draft == null) continue;
                    foreach (var emission in pattern.emissions)
                    {
                        if (emission == null || emission.phase < 0 || emission.phase >= draft.Material.strikes.Length)
                        { errors.Add(draft.Material.displayName + ": 복합 토출의 타격 연결을 확인하세요."); continue; }
                        var strike = draft.Material.strikes[emission.phase];
                        if (emission.normalizedTime >= strike.contactStart && emission.normalizedTime <= strike.contactEnd) continue;
                        errors.Add($"{draft.Material.displayName}: {emission.normalizedTime * draft.FrameCount:0.##}F 토출이 "
                            + $"{strike.contactStart * draft.FrameCount:0.##}~{strike.contactEnd * draft.FrameCount:0.##}F 판정 창 밖에 있습니다. 복합 공격 설정을 함께 조절하세요.");
                    }
                }
            }
        }
        public void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("유휴 EditMode에서 저장하세요.");
            var changed = Drafts.Where(d => d.Dirty).ToArray();
            RefreshRuntimeLinks();
            var errors = Validate(true);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (changed.GroupBy(d => d.AbilitySource).Any(g => g.Count() > 1)) throw new InvalidOperationException("변경 공격들이 같은 능력을 공유합니다. 한 공격씩 저장하거나 능력을 분리하세요.");
            if (changed.Length == 0) return;
            var originals = changed.SelectMany(d => new Object[] { d.Source, d.AbilitySource }).Distinct().ToArray();
            var snapshots = originals.Select(o => EditorJsonUtility.ToJson(o)).ToArray();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("보스 메이커 저장");
            Undo.RegisterCompleteObjectUndo(originals, "보스 메이커 저장");
            try
            {
                foreach (var draft in changed)
                {
                    EditorUtility.CopySerialized(draft.Ability, draft.AbilitySource); draft.AbilitySource.hideFlags = HideFlags.None;
                    draft.Material.ability = draft.AbilitySource;
                    try { EditorUtility.CopySerialized(draft.Material, draft.Source); draft.Source.hideFlags = HideFlags.None; }
                    finally { draft.Material.ability = draft.Ability; }
                }
                foreach (var asset in originals) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
                foreach (var draft in changed) draft.AcceptSaved();
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                for (int i = 0; i < originals.Length; i++) { EditorJsonUtility.FromJsonOverwrite(snapshots[i], originals[i]); EditorUtility.SetDirty(originals[i]); AssetDatabase.SaveAssetIfDirty(originals[i]); }
                throw;
            }
        }
        public BossMakerRecovery[] Capture() => Drafts.Where(d => d.Dirty).Select(d => d.Capture()).ToArray();
        public void Dispose()
        {
            foreach (var draft in Drafts) draft.Dispose(); Drafts.Clear();
            composites.Clear(); Encounters.Clear();
        }
    }
}
