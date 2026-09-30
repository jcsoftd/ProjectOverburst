using System;
using UnityEditor;
using UnityEngine;

public static class MonsterMixedSquadBuilder
{
    [MenuItem("OVERBURST/Enemies/Themes/Apply Mixed Squad Tactics")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode");
        string root = MonsterThemeCombatBuilder.Root;
        string folder = root + "/Tactics";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(root, "Tactics");
        var melee = Profile(folder, "MeleePressure", EnemyTacticalRole.MeleePressure);
        var ranged = Profile(folder, "RangedHold", EnemyTacticalRole.RangedHold);
        var skirmisher = Profile(folder, "Skirmisher", EnemyTacticalRole.Skirmisher);
        int count = 0, rangedCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { root + "/Definitions" }))
        {
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (!IsOriginalTheme(definition.EnemyId)) continue;
            var profile = definition.EnemyId == "VenomBrood_Kupolojuve_Tint_Orange" ? skirmisher
                : definition.EnemyId == "VenomBrood_Arathrox" || definition.EnemyId == "VenomBrood_Venodonte_Tint3" ? ranged : melee;
            if (profile.UsesRangedPositioning)
            {
                bool hasShot = false;
                for (int i = 0; i < definition.AbilitySet.Count; i++)
                    hasShot |= definition.AbilitySet.GetAbility(i).ExecutionMode == EnemyAbilityExecutionMode.Projectile;
                if (!hasShot) throw new InvalidOperationException("Missing ranged ability: " + definition.EnemyId);
                rangedCount++;
            }
            definition.SetTacticalProfile(profile);
            EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition); count++;
        }
        if (count != 14 || rangedCount != 3) throw new InvalidOperationException("Unexpected roster: " + count + "/" + rangedCount);
        Debug.Log("[MixedSquad] PASS profiles=3 definitions=14 rangedSpecies=3; prefabs/abilities preserved");
    }
    public static bool IsOriginalTheme(string id) => id.StartsWith("SpiderBrood_") || id.StartsWith("VenomBrood_") || id.StartsWith("PrimalHunt_");
    // Additive theme builders explicitly choose a role; species IDs are not runtime AI policy.
    public static void ApplyDefinition(EnemyDefinition definition, EnemyTacticalRole role)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode");
        string folder = MonsterThemeCombatBuilder.Root + "/Tactics";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(MonsterThemeCombatBuilder.Root, "Tactics");
        definition.SetTacticalProfile(Profile(folder, role.ToString(), role));
        EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition);
    }
    private static EnemyTacticalProfile Profile(string folder, string name, EnemyTacticalRole role)
    {
        string path = folder + "/" + name + ".asset";
        var profile = AssetDatabase.LoadAssetAtPath<EnemyTacticalProfile>(path);
        if (profile != null) return profile; // Preserve tuning on repeated generation.
        profile = ScriptableObject.CreateInstance<EnemyTacticalProfile>();
        profile.Configure(role, 3f, 5.5f); AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssetIfDirty(profile); return profile;
    }
}
