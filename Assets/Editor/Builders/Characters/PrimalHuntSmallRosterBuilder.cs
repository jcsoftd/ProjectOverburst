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
            var template = MonsterThemeTemplate.LoadActor(); // 2026-10-01: 옛 Protofactor 템플릿 삭제, 새 템플릿은 미정
            var normal = AssetDatabase.LoadAssetAtPath<EnemyGradeProfile>(Root + "/Grades/Normal.asset");
            var variant = MonsterThemeTemplate.LoadDefaultVariant();
            var preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(Root + "/Presets/PrimalHunt.asset");
            var signal = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/PrimalHunt.mat");
            var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(Root + "/Tables/PrimalHunt.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(Root + "/Catalog.asset");
            if (baseline == null || normal == null || variant == null || preset == null
                || signal == null || table == null || catalog == null) throw new InvalidOperationException("Primal baseline missing");
            // 2026-10-01: 이미 있는 종은 보존한다. 새 종만 만들고, 기존 테이블 가중치(Caniathrox 등)와 프리셋 로스터는 그대로 둔다.
            // 새 종이 필요한데 템플릿이 없으면 쓰기 전에 멈춘다.
            MonsterThemeAuthoringPolicy.RequireTemplateBeforeWrites(Specs.Select(s => Theme + "_" + s.Name), template, baseline);
            var added = new List<EnemyDefinition>(); int preserved = 0;
            foreach (var spec in Specs)
            {
                string id = Theme + "_" + spec.Name;
                if (MonsterThemeAuthoringPolicy.FindExisting(id) != null) { preserved++; continue; }
                MonsterThemeAuthoringPolicy.RequireTemplate(template, baseline, id);
                added.Add(BuildSpecies(gallery.actors[spec.Number - 1], spec, baseline, template, normal, variant, preset, signal));
            }
            if (added.Count == 0)
            {
                Debug.Log("[PrimalHuntSmallRoster] created=0 preserved=" + preserved + "; catalog, table weights and preset unchanged.");
                return;
            }
            var touched = new List<Object>();
            MonsterThemeAuthoringPolicy.MergeCatalog(catalog, added, touched);
            MonsterThemeAuthoringPolicy.MergeTable(table, false, table.ThemeId, table.DisplayName, catalog, table.Accent,
                added.Select(d => new EnemyThemeTable.Entry { definition = d, tier = EnemyThemeTier.Small, weight = 10f }), touched);
            MonsterThemeAuthoringPolicy.AppendPresetRoster(preset, added.Select(d => d.ActorPrefab.gameObject), touched);
            MonsterThemeAuthoringPolicy.Save(touched); AssetDatabase.SaveAssets();
            MonsterThemeLocomotionBuilder.ApplyCreated(added.Select(d => d.EnemyId).ToArray());
            if (!table.Validate(out string error)) throw new InvalidOperationException("Primal table: " + error);
            Debug.Log("[PrimalHuntSmallRoster] created=" + added.Count + " preserved=" + preserved + "; existing weights kept.");
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
