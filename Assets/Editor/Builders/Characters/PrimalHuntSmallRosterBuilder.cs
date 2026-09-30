using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static MonsterThemeCombatBuilder;

// Local authoring tool: uses the saved showcase and existing actor/squad runtime.
// Supplier prefabs and animation importers are never modified.
public static class PrimalHuntSmallRosterBuilder
{
    private const string Theme = "PrimalHunt";
    private const string GalleryPath = "Assets/ProjectOverburst/00_Scenes/MonsterVol2_Showcase.unity";
    private sealed class Spec
    {
        public int Number;
        public string Name;
        public string Walk;
        public string Hit;
        public string[] Attacks;
        public float Footprint;
        public float WalkSpeed;
        public float RunSpeed;
        public Spec(int number, string name, string walk, string hit, float footprint, float walkSpeed, float runSpeed, params string[] attacks)
        { Number = number; Name = name; Walk = walk; Hit = hit; Footprint = footprint; WalkSpeed = walkSpeed; RunSpeed = runSpeed; Attacks = attacks; }
    }

    private static readonly Spec[] Specs =
    {
        new Spec(7, "CrustaspikanLarvae", "WalkForward", "GetHitFront", 1.20f, 1.30f, 1.82f,
            "Bite", "BiteForward")
    };

    [MenuItem("OVERBURST/Enemies/Themes/Build Primal Small Roster")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene first");
        var scene = EditorSceneManager.OpenScene(GalleryPath, OpenSceneMode.Additive);
        try
        {
            var gallery = Object.FindObjectsByType<MonsterShowcaseGallery>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(g => g.gameObject.scene == scene);
            if (gallery.actors.Length != 47) throw new InvalidOperationException("Expected saved 47-actor showcase");
            var baseline = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "/Definitions/PrimalHunt_Caniathrox.asset");
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(ProtofactorEnemyPilotBuilder.PrefabPaths[0]);
            var normal = AssetDatabase.LoadAssetAtPath<EnemyGradeProfile>(Root + "/Grades/Normal.asset");
            var variant = AssetDatabase.LoadAssetAtPath<EnemyVariantProfile>(ProtofactorEnemyPilotBuilder.DefaultVariantPath);
            var preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(Root + "/Presets/PrimalHunt.asset");
            var signal = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/PrimalHunt.mat");
            var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(Root + "/Tables/PrimalHunt.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(Root + "/Catalog.asset");
            if (baseline == null || template == null || normal == null || variant == null || preset == null
                || signal == null || table == null || catalog == null) throw new InvalidOperationException("Primal baseline missing");
            var added = new List<EnemyDefinition>();
            foreach (var spec in Specs)
                added.Add(BuildSpecies(gallery.actors[spec.Number - 1], spec, baseline, template, normal, variant, preset, signal));

            var definitions = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition)
                .Where(d => d != null && d.EnemyId != "PrimalHunt_Pistriptere" && d.EnemyId != "PrimalHunt_Lacercharias").ToList();
            foreach (var definition in added)
                if (!definitions.Any(d => d.EnemyId == definition.EnemyId)) definitions.Add(definition);
            catalog.Configure(definitions.ToArray()); EditorUtility.SetDirty(catalog);
            var roster = table.Entries.Where(e => e.definition != null && !added.Any(d => d.EnemyId == e.definition.EnemyId)
                && e.definition.EnemyId != "PrimalHunt_Pistriptere" && e.definition.EnemyId != "PrimalHunt_Lacercharias").ToList();
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].definition.EnemyId == "PrimalHunt_Caniathrox")
                { var entry = roster[i]; entry.weight = 30f; roster[i] = entry; }
            foreach (var definition in added)
                roster.Add(new EnemyThemeTable.Entry
                { definition = definition, tier = EnemyThemeTier.Small, weight = 10f });
            table.Configure(table.ThemeId, table.DisplayName, catalog, table.Accent, roster.ToArray());
            preset.ConfigureIdentity("Theme_PrimalHunt", table.DisplayName,
                roster.Where(e => e.tier != EnemyThemeTier.Elite).Select(e => e.definition.ActorPrefab.gameObject).ToArray());
            EditorUtility.SetDirty(table); EditorUtility.SetDirty(preset); AssetDatabase.SaveAssets();
            foreach (var definition in added) MonsterThemeLocomotionBuilder.ApplySpecies(definition.EnemyId);
            if (!table.Validate(out string error)) throw new InvalidOperationException("Primal table: " + error);
            var counts = table.BuildRoster(40, 9, 1, 731).GroupBy(d => d.EnemyId).ToDictionary(g => g.Key, g => g.Count());
            if (counts["PrimalHunt_Caniathrox"] != 30 || counts["PrimalHunt_CrustaspikanLarvae"] != 10)
                throw new InvalidOperationException("Unexpected 50-actor mix");
            AssetDatabase.SaveAssets();
            Debug.Log("[PrimalHuntSmallRoster] PASS Caniathrox=30 CrustaspikanLarvae=10 medium=9 elite=1");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static EnemyDefinition BuildSpecies(MonsterShowcaseActor display, Spec spec, EnemyDefinition baseline,
        GameObject template, EnemyGradeProfile normal, EnemyVariantProfile variant, EnemyAiPreset preset, Material signal)
    {
        if (display == null || display.displayName != spec.Name) throw new InvalidOperationException("Showcase species mismatch: " + spec.Name);
        string id = Theme + "_" + spec.Name;
        AnimationClip Clip(string name) => display.clips.FirstOrDefault(c => c.name == name)
            ?? throw new InvalidOperationException(id + " missing clip " + name);
        var idle = Clip("Idle"); var walk = Clip(spec.Walk); var hit = Clip(spec.Hit); var death = Clip("Death");
        var attacks = spec.Attacks.Select(Clip).ToArray();
        var optional = display.clips.Where(c => !c.name.EndsWith("_RM") && !c.name.StartsWith("Fly")
            && !c.name.StartsWith("InAir") && !c.name.StartsWith("TakeOff") && !c.name.StartsWith("Landing")
            && !attacks.Contains(c) && c != idle && c != walk && c != hit && c != death).ToArray();
        float scale = spec.Footprint / Mathf.Max(display.displaySize.x, display.displaySize.z);
        Vector3 size = display.displaySize * scale;
        float radius = Mathf.Clamp(Mathf.Max(size.x, size.z) * .22f, .22f, .42f);
        float bodyHeight = Mathf.Max(radius * 2f, size.y * .88f);
        var controller = Controller(id, idle, walk, walk, hit, death, attacks, display.clips);
        var animation = Asset<EnemyAnimationProfile>("Animations/" + id);
        animation.Configure(id, controller, idle, walk, walk, attacks, hit, death, optional,
            display.clips.Where(c => c.name.EndsWith("_RM")).Select(AssetDatabase.GetAssetPath).Distinct().ToArray());
        var movement = Clone(baseline.MovementProfile, "Movement/" + id);
        movement.Configure(id, spec.WalkSpeed, 420f, spec.WalkSpeed, spec.RunSpeed / spec.WalkSpeed, 2.2f);
        movement.ConfigureHitWeight(MonsterThemeWeightBuilder.Resolve(EnemyThemeTier.Small));
        movement.ConfigureCrowdWeight(1f);
        var behavior = Clone(baseline.BehaviorProfile, "Behavior/" + id);
        Set(behavior, "profileId", id);
        Set(behavior, "preferredApproachDistance", radius + .55f);
        Set(behavior, "preferredMinDistance", radius + .30f);
        behavior.ConfigureRunApproach(3.1f);
        var abilities = new List<EnemyAbilityDefinition>();
        for (int i = 0; i < attacks.Length; i++)
        {
            var clip = attacks[i]; bool roll = clip.name == "RollToBiteAttack";
            bool combo = clip.name.StartsWith("2Hit", StringComparison.Ordinal);
            float timing = roll ? .68f : combo ? .31f : clip.name.Contains("Claws") ? .40f : .43f;
            var ability = Asset<EnemyAbilityDefinition>("Abilities/" + id + "_" + clip.name);
            ability.Configure(id + "_" + clip.name, "Attack" + (i + 1), 5f,
                roll ? 3.5f : radius + 1.1f, radius + .65f, roll ? 95f : 125f,
                roll ? 7f : combo ? 2.0f : 1.35f, clip.length * timing, timing,
                clip.length * .95f, roll ? .25f : combo ? .65f : 1f, false,
                roll ? EnemyAbilityExecutionMode.Charge : EnemyAbilityExecutionMode.MeleeArc,
                1.25f, true, clip.length);
            ability.ConfigureUsePolicy(roll ? 1.8f : 0f, 0);
            ability.ConfigureAdditionalHits(combo ? new[] { .66f } : Array.Empty<float>());
            EditorUtility.SetDirty(ability); abilities.Add(ability);
        }
        var set = Asset<EnemyAbilitySet>("Abilities/" + id + "_Set"); set.Configure(id, abilities.ToArray());
        var species = Asset<EnemySpeciesDefinition>("Species/" + id);
        string displayName = EnemyDisplayNames.Resolve(id, display.displayName);
        species.Configure(id, displayName, AssetDatabase.LoadAssetAtPath<GameObject>(display.sourcePath), animation, set);
        species.ConfigureRuntime(EnemyCombatRole.Swarm, 38, movement, behavior, 1);
        var definition = Asset<EnemyDefinition>("Definitions/" + id); definition.ConfigureIdentity(id, displayName);
        definition.ConfigureRuntime(animation, set, behavior, movement, preset, EnemySquadParticipationMode.SquadMember);
        var actor = BuildActor(template, display, id, definition, scale, size, radius, bodyHeight, 1f,
            controller, set, movement, behavior, preset, EnemySquadParticipationMode.SquadMember, signal, new Color(1f, .3f, .15f));
        definition.ConfigureComposition(species, normal, variant, actor);
        MonsterMixedSquadBuilder.ApplyDefinition(definition, EnemyTacticalRole.MeleePressure);
        var prefabPath = AssetDatabase.GetAssetPath(actor);
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var target = prefab.GetComponent<CombatTarget>();
            target.ConfigureHurtVolume(Vector3.up * (bodyHeight * .50f), .46f, .64f);
            EditorUtility.SetDirty(target);
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var item in new Object[] { animation, movement, behavior, set, species, definition }) EditorUtility.SetDirty(item);
        return definition;
    }
}
