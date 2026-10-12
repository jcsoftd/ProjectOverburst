using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GraveHuntTuningBuilder
{
    const string Root = "Assets/ProjectOverburst/Resources/Enemies/Themes/";
    static readonly (string id, float walk, float chase, float rate, int stage)[] Targets =
    {
        ("GraveHunt_GiantRat", 1.40f, 3.00f, 1.16f, 1),
        ("GraveHunt_DarknessSpider", 1.30f, 2.75f, 1.12f, 2),
        ("GraveHunt_GiantBeetle", 1.05f, 1.85f, 0.92f, 1),
        ("GraveHunt_Barghest", 1.50f, 3.40f, 1.20f, 3),
        ("GraveHunt_UndeadSword", 1.10f, 2.00f, 1.08f, 1),
        ("GraveHunt_UndeadAxe", 1.10f, 1.85f, 0.96f, 3),
        ("GraveHunt_UndeadMace", 1.05f, 1.75f, 0.88f, 4),
        ("GraveHunt_Undead", 1.15f, 1.85f, 0.96f, 1),
        ("GraveHunt_UndeadShield", 1.10f, 1.65f, 0.92f, 2),
        ("GraveHunt_Akanthasaur", 1.30f, 2.25f, 1.00f, 3),
        ("V3_Ghoul", 1.25f, 2.60f, 1.08f, 4),
        ("DeathHarvest_RakeBrute", 1.15f, 1.75f, 0.92f, 5),
        ("GraveHunt_Minotaur", 1.05f, 1.55f, 0.88f, 2),
        ("DeathHarvest_Reaper", 1.15f, 1.95f, 1.04f, 5),
    };

    [MenuItem("OVERBURST/Monsters/GraveHunt/Apply approved tempo and outdoor unlocks")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Require idle EditMode.");
        var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(Root + "Tables/GraveHunt.asset");
        var definitions = table.Entries.Select(e => e.definition).ToDictionary(d => d.EnemyId);
        var all = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Definitions" })
            .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        foreach (var target in Targets)
        {
            var definition = definitions[target.id];
            if (all.Any(d => d != definition && (d.MovementProfile == definition.MovementProfile
                || Enumerable.Range(0, d.AbilitySet.Count).Any(i => Enumerable.Range(0, definition.AbilitySet.Count)
                    .Any(j => d.AbilitySet.GetAbility(i) == definition.AbilitySet.GetAbility(j))))))
                throw new InvalidOperationException("Shared movement/ability authoring: " + target.id);
        }
        var owned = new HashSet<Object>();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var target in Targets)
            {
                var definition = definitions[target.id];
                var current = definition.Variant;
                float ratio = current.AttackSpeedMultiplier / target.rate;
                float damage = current.DamageMultiplier * ratio, interval = current.AttackInterval * ratio;
                string path = Root + "Variants/EVP_" + target.id + ".asset";
                var variant = AssetDatabase.LoadAssetAtPath<EnemyVariantProfile>(path);
                if (!variant)
                {
                    variant = Object.Instantiate(current);
                    variant.name = "EVP_" + target.id;
                    AssetDatabase.CreateAsset(variant, path);
                }
                var serialized = new SerializedObject(variant);
                serialized.FindProperty("variantId").stringValue = target.id + "_Tempo";
                serialized.FindProperty("displayName").stringValue = definition.DisplayName;
                serialized.FindProperty("attackSpeedMultiplier").floatValue = target.rate;
                serialized.FindProperty("damageMultiplier").floatValue = damage;
                serialized.FindProperty("attackInterval").floatValue = interval;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var authored = new SerializedObject(definition);
                authored.FindProperty("variant").objectReferenceValue = variant;
                authored.ApplyModifiedPropertiesWithoutUndo();
                var movement = new SerializedObject(definition.MovementProfile);
                movement.FindProperty("moveSpeed").floatValue = target.walk / definition.ResolveRuntimeStats().MoveSpeedMultiplier;
                movement.FindProperty("runSpeedMultiplier").floatValue = target.chase / target.walk;
                movement.ApplyModifiedPropertiesWithoutUndo();
                owned.Add(variant); owned.Add(definition); owned.Add(definition.MovementProfile);
                for (int i = 0; i < definition.AbilitySet.Count; i++)
                {
                    var ability = definition.AbilitySet.GetAbility(i);
                    var attack = new SerializedObject(ability);
                    attack.FindProperty("cooldown").floatValue = ability.Cooldown * ratio;
                    attack.ApplyModifiedPropertiesWithoutUndo(); owned.Add(ability);
                }
            }
            var entries = table.Entries.ToArray();
            for (int i = 0; i < entries.Length; i++)
                entries[i].minimumOutdoorWorldLevel = Targets.Single(t => t.id == entries[i].definition.EnemyId).stage;
            table.ConfigureApproved(table.ThemeId, table.DisplayName, table.Catalog, table.Accent, entries);
            EditorUtility.SetDirty(table); owned.Add(table);
        }
        finally { AssetDatabase.StopAssetEditing(); }
        foreach (var asset in owned) AssetDatabase.SaveAssetIfDirty(asset);
    }
}
