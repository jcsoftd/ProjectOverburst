using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MonsterMixedSquadBuilder
{
    // 2026-10-01: 원래 3테마의 역할 기준. 원거리·교란 역할은 투사체 능력이 있어야 한다.
    public static readonly string[] OriginalThemeIds = { "SpiderBrood", "VenomBrood", "PrimalHunt" };
    private const string SkirmisherId = "VenomBrood_Kupolojuve_Tint_Orange";
    private static readonly string[] RangedHoldIds = { "VenomBrood_Arathrox", "VenomBrood_Venodonte_Tint3" };

    public struct Assignment
    {
        public EnemyDefinition definition;
        public EnemyTacticalRole role;
    }

    [MenuItem("OVERBURST/Enemies/Themes/Apply Mixed Squad Tactics")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode");
        string root = MonsterThemeCombatBuilder.Root;
        // 2026-10-01: 로스터·능력·누락 참조를 모두 먼저 검사한다. 검사가 끝나기 전에는 폴더·프로필·정의를 만들거나 저장하지 않는다.
        var definitions = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { root + "/Definitions" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
        var tables = OriginalThemeIds.Select(id => AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(root + "/Tables/" + id + ".asset")).ToArray();
        var plan = Plan(definitions, tables, out int rosterCount);
        if (plan.Count == 0)
        {
            Debug.Log("[MixedSquad] NO_CHANGE definitions=" + rosterCount + "; existing tactical profiles kept");
            return;
        }
        string folder = root + "/Tactics";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(root, "Tactics");
        foreach (var assignment in plan)
        {
            assignment.definition.SetTacticalProfile(Profile(folder, assignment.role.ToString(), assignment.role));
            EditorUtility.SetDirty(assignment.definition);
            AssetDatabase.SaveAssetIfDirty(assignment.definition);
        }
        Debug.Log("[MixedSquad] APPLIED definitions=" + rosterCount + " assigned=" + plan.Count + "; existing tactical profiles kept");
    }

    // 쓰기 없이 검사만 한다. 원래 3테마 정의가 테마 테이블과 같은 집합인지, 능력 세트와 능력 참조가 모두 있는지,
    // 원거리·교란 역할에 투사체 능력이 있는지 본다. 하나라도 틀리면 예외를 던지고, 통과하면 전술 프로필이 비어 있는 정의만 돌려준다.
    public static List<Assignment> Plan(IEnumerable<EnemyDefinition> definitions, IEnumerable<EnemyThemeTable> tables, out int rosterCount)
    {
        var errors = new List<string>();
        var roster = new List<EnemyDefinition>();
        foreach (var definition in definitions)
        {
            if (definition == null) errors.Add("unloadable definition");
            else if (IsOriginalTheme(definition.EnemyId)) roster.Add(definition);
        }
        rosterCount = roster.Count;

        var tableList = tables.ToList();
        if (tableList.Count != OriginalThemeIds.Length || tableList.Any(t => t == null))
            errors.Add("theme table missing");
        else
        {
            if (tableList.SelectMany(t => t.Entries).Any(e => e.definition == null)) errors.Add("theme table has an empty entry");
            var registered = new HashSet<string>(tableList.SelectMany(t => t.Entries)
                .Where(e => e.definition != null && IsOriginalTheme(e.definition.EnemyId)).Select(e => e.definition.EnemyId));
            var found = new HashSet<string>(roster.Select(d => d.EnemyId));
            foreach (string id in registered.Except(found)) errors.Add("table entry without definition: " + id);
            foreach (string id in found.Except(registered)) errors.Add("definition not in its theme table: " + id);
        }
        foreach (string id in RangedHoldIds.Concat(new[] { SkirmisherId }))
            if (!roster.Any(d => d.EnemyId == id)) errors.Add("role species missing: " + id);

        var plan = new List<Assignment>();
        foreach (var definition in roster)
        {
            var role = RoleOf(definition.EnemyId);
            var set = definition.AbilitySet;
            if (set == null || set.Count == 0) { errors.Add("ability set missing: " + definition.EnemyId); continue; }
            bool hasShot = false, broken = false;
            for (int i = 0; i < set.Count; i++)
            {
                var ability = set.GetAbility(i);
                if (ability == null) broken = true;
                else hasShot |= ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile;
            }
            if (broken) errors.Add("ability reference missing: " + definition.EnemyId);
            if (role != EnemyTacticalRole.MeleePressure && !hasShot) errors.Add("missing ranged ability: " + definition.EnemyId);
            if (definition.TacticalProfile == null) plan.Add(new Assignment { definition = definition, role = role });
        }
        if (errors.Count > 0)
            throw new InvalidOperationException("Apply Mixed Squad Tactics: nothing was written. " + string.Join("; ", errors));
        return plan;
    }

    public static EnemyTacticalRole RoleOf(string enemyId) =>
        enemyId == SkirmisherId ? EnemyTacticalRole.Skirmisher
        : RangedHoldIds.Contains(enemyId) ? EnemyTacticalRole.RangedHold
        : EnemyTacticalRole.MeleePressure;

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
