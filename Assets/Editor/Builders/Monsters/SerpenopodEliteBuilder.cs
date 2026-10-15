using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Scale the authored model and measured contacts together; keep runtime variant scale at one.
public static class SerpenopodEliteBuilder
{
    public const string Id = "V3_Serpenopod";
    const string Root = "Assets/ProjectOverburst/Resources/Enemies/Themes/";
    public const string DefinitionPath = Root + "Definitions/" + Id + ".asset";
    const string VariantPath = Root + "Variants/EVP_" + Id + ".asset";
    const string FootfallPath = Root + "Footfalls/" + Id + ".asset";
    public const float HealthCoefficient = 18.8f;
    public const float DamageMultiplier = 1.25f;
    public const float AuthoredSize = 1.4f;

    static T Load<T>(string path) where T : UnityEngine.Object
        => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing asset: " + path);
    static void Save(UnityEngine.Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }

    public static string Apply(string outputDirectory)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string project = Directory.GetParent(Application.dataPath).FullName;
        string allowed = Path.GetFullPath(Path.Combine(project, "../개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (!outputDirectory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private monster output required.");
        Directory.CreateDirectory(outputDirectory);
        var definition = Load<EnemyDefinition>(DefinitionPath);
        var table = Load<EnemyThemeTable>(Root + "Tables/PrimalHunt.asset");
        var heavy = Load<EnemyHitWeightProfile>(Root + "HitWeights/Heavy.asset");
        var elite = Load<EnemyGradeProfile>(Root + "Grades/Elite.asset");
        if (!definition.IsValid || definition.AbilitySet.Count != 4
            || Enumerable.Range(0, 4).Count(i => definition.AbilitySet.GetAbility(i).IsTelegraphedStrongAttack) != 1
            || table.Entries.Count(e => e.definition == definition) != 1)
            throw new InvalidOperationException("Expected the existing four-attack primal actor.");
        var variant = AssetDatabase.LoadAssetAtPath<EnemyVariantProfile>(VariantPath);
        if (variant == null) {
            variant = ScriptableObject.CreateInstance<EnemyVariantProfile>();
            AssetDatabase.CreateAsset(variant, VariantPath);
        }
        variant.Configure(Id + "_Elite", "세발 긴턱룡 엘리트", Vector3.one, Color.white);
        variant.ConfigureRuntimeModifiers(Vector3.one, Vector3.one, 1f, DamageMultiplier, 1f, 1f);
        var variantData = new SerializedObject(variant);
        variantData.FindProperty("attackInterval").floatValue = 1.15f;
        variantData.ApplyModifiedPropertiesWithoutUndo(); Save(variant);

        definition.ConfigureComposition(definition.Species, elite, variant, definition.ActorPrefab);
        definition.ConfigureRuntime(definition.AnimationProfile, definition.AbilitySet, definition.BehaviorProfile,
            definition.MovementProfile, definition.AiPreset, EnemySquadParticipationMode.Independent);
        var data = new SerializedObject(definition);
        data.FindProperty("referenceHealthCoefficient").floatValue = HealthCoefficient;
        data.ApplyModifiedPropertiesWithoutUndo(); Save(definition);

        var movement = new SerializedObject(definition.MovementProfile);
        movement.FindProperty("crowdWeight").floatValue = 4f;
        movement.FindProperty("knockbackReductionPercent").floatValue = 55f;
        movement.FindProperty("hitWeightProfile").objectReferenceValue = heavy;
        movement.ApplyModifiedPropertiesWithoutUndo(); Save(definition.MovementProfile);

        var entries = table.Entries.ToArray();
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].definition == definition) entries[i].tier = EnemyThemeTier.Elite;
        table.ConfigureApproved(table.ThemeId, table.DisplayName, Load<EnemyCatalog>(Root + "Catalog.asset"), table.Accent, entries); Save(table);
        var preset = definition.AiPreset;
        var presetData = new SerializedObject(preset);
        var prefabs = presetData.FindProperty("defaultMonsterPrefabs");
        var kept = Enumerable.Range(0, prefabs.arraySize).Select(i => prefabs.GetArrayElementAtIndex(i).objectReferenceValue)
            .Where(p => p != definition.ActorPrefab.gameObject).ToArray();
        prefabs.arraySize = kept.Length;
        for (int i = 0; i < kept.Length; i++) prefabs.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];
        presetData.ApplyModifiedPropertiesWithoutUndo(); Save(preset);

        var profile = AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(FootfallPath);
        if (profile == null) {
            MonsterV3FootfallBuilder.Create(DefinitionPath, new[] { "Serpenopod_ L Foot", "Serpenopod_ R Foot" },
                Path.Combine(outputDirectory, "footfall-measurement.json"));
            profile = Load<EnemyFootfallProfile>(FootfallPath);
        }
        var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(definition.ActorPrefab));
        try {
            var emitter = root.GetComponent<EnemyEliteFootstepEmitter>();
            if (emitter == null) emitter = root.AddComponent<EnemyEliteFootstepEmitter>();
            if (emitter.Profile != profile) {
                emitter.Configure(root.GetComponent<EnemyActor>(), profile);
                PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GetAssetPath(definition.ActorPrefab), out bool saved);
                if (!saved) throw new InvalidOperationException("Elite emitter save failed.");
            }
        } finally { PrefabUtility.UnloadPrefabContents(root); }
        float authoredSize = definition.ActorPrefab.Animator.transform.localScale.x;
        if (!Mathf.Approximately(authoredSize, AuthoredSize)) {
            if (!Mathf.Approximately(authoredSize, 1f))
                throw new InvalidOperationException("Unexpected authored scale; preserve the later size edit.");
            string planPath = Path.Combine(outputDirectory, "size-plan.json");
            File.WriteAllText(planPath, new JObject { ["entries"] = new JArray(new JObject {
                ["id"] = Id, ["definition"] = DefinitionPath, ["factor"] = AuthoredSize / authoredSize, ["offset"] = 0f, ["airborne"] = false }) }.ToString());
            MonsterPresentationCalibrationBuilder.Apply(planPath, Path.Combine(outputDirectory, "SizeCalibration"));
        }
        var report = Verify();
        File.WriteAllText(Path.Combine(outputDirectory, "native-verification.json"), report.ToString());
        return "APPLIED_ELITE_NATIVE_PASS";
    }

    public static JObject Verify()
    {
        var d = Load<EnemyDefinition>(DefinitionPath);
        var table = Load<EnemyThemeTable>(Root + "Tables/PrimalHunt.asset");
        var checks = new JObject();
        void Check(string name, bool ok) { checks[name] = ok; }
        Check("validActor", d.IsValid && d.ActorPrefab.IsAuthoringValid);
        Check("eliteGradeAndSpawnTier", d.Grade.GradeType == EnemyGradeType.Elite
            && table.Entries.Single(e => e.definition == d).tier == EnemyThemeTier.Elite);
        Check("independentAi", d.SquadParticipationMode == EnemySquadParticipationMode.Independent);
        Check("healthCoefficient", Mathf.Approximately(d.ReferenceHealthCoefficient, HealthCoefficient));
        Check("authoredEliteSize", Mathf.Approximately(d.ActorPrefab.Animator.transform.localScale.x, AuthoredSize)
            && d.ResolveRuntimeStats().VisualScale == Vector3.one
            && d.ResolveRuntimeStats().CollisionScale == Vector3.one && d.ResolveRuntimeStats().AnchorScale == Vector3.one);
        Check("dedicatedDamageVariant", AssetDatabase.GetAssetPath(d.Variant) == VariantPath
            && Mathf.Approximately(d.ResolveRuntimeStats().DamageMultiplier, DamageMultiplier));
        Check("heavyCrowdAndHitWeight", d.MovementProfile.HitWeightProfile.Weight == EnemyHitWeight.Heavy
            && Mathf.Approximately(new SerializedObject(d.MovementProfile).FindProperty("crowdWeight").floatValue, 4f));
        Check("threeWeakOneStrong", d.AbilitySet.Count == 4
            && Enumerable.Range(0, 4).Count(i => d.AbilitySet.GetAbility(i).IsTelegraphedStrongAttack) == 1
            && Enumerable.Range(0, 4).Where(i => !d.AbilitySet.GetAbility(i).IsTelegraphedStrongAttack)
                .All(i => d.AbilitySet.GetAbility(i).WeakAttackExecution.ValidateAuthoring(out _)));
        Check("approvedParryRoles", d.AnimationProfile.ParryCollapse != null && d.AnimationProfile.StunnedLoop != null
            && d.AnimationProfile.StunnedLoop.isLooping && d.AnimationProfile.StunRecover != null);
        var emitter = d.ActorPrefab.GetComponent<EnemyEliteFootstepEmitter>();
        var profile = emitter != null ? emitter.Profile : null;
        Check("separateMeasuredGaits", profile != null && profile.IsValid && profile.GroundStepTier == EnemyGroundStepTier.Elite
            && profile.LocomotionClip == d.AnimationProfile.Walk && profile.RunClip == d.AnimationProfile.Run
            && profile.GetContactCount(false) >= 2 && profile.GetContactCount(true) >= 2);
        var defaults = new SerializedObject(d.AiPreset).FindProperty("defaultMonsterPrefabs");
        Check("excludedFromSquadDefaults", !Enumerable.Range(0, defaults.arraySize)
            .Any(i => defaults.GetArrayElementAtIndex(i).objectReferenceValue == d.ActorPrefab.gameObject));
        Check("primalMembership", table.Validate(out _) && table.Entries.Count == 9
            && table.Entries.Count(e => e.tier == EnemyThemeTier.Small) == 4
            && table.Entries.Count(e => e.tier == EnemyThemeTier.Medium) == 3
            && table.Entries.Count(e => e.tier == EnemyThemeTier.Elite) == 2);
        bool rosters = true; int samples = 0;
        foreach (var tableGuid in AssetDatabase.FindAssets("t:EnemyThemeTable", new[] { Root + "Tables" })) {
            var t = Load<EnemyThemeTable>(AssetDatabase.GUIDToAssetPath(tableGuid));
            for (int seed = 0; seed < 32; seed++) {
                var roster = t.BuildRoster(40, 9, 1, seed);
                rosters &= roster.Count == 50 && roster.Count(x => t.Entries.Single(e => e.definition == x).tier == EnemyThemeTier.Elite) == 1;
                samples++;
            }
        }
        Check("allThemeRosterCounts", rosters);
        Check("missingScriptsZero", d.ActorPrefab.GetComponentsInChildren<Transform>(true)
            .All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0));
        bool pass = checks.Properties().All(p => (bool)p.Value);
        var result = new JObject { ["status"] = pass ? "PASS_NATIVE" : "FAIL_NATIVE", ["checks"] = checks,
            ["rosterSamples"] = samples, ["healthCoefficient"] = d.ReferenceHealthCoefficient,
            ["damageMultiplier"] = d.ResolveRuntimeStats().DamageMultiplier, ["playVerified"] = false };
        if (!pass) throw new InvalidOperationException(result.ToString());
        return result;
    }
}
