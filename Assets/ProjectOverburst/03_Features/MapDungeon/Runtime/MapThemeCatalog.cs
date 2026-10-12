using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

public static class MapThemeCatalog
{
    private const string ResourcePath = "Enemies/Themes/Tables";
    private static EnemyThemeTable[] tables;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => tables = null;

    public static IReadOnlyList<EnemyThemeTable> Tables
    {
        get
        {
            if (tables != null) return tables;
            var valid = new List<EnemyThemeTable>();
            foreach (var table in Resources.LoadAll<EnemyThemeTable>(ResourcePath))
                if (table != null && table.Validate(out _)) valid.Add(table);
            valid.Sort((left, right) => string.CompareOrdinal(left.ThemeId, right.ThemeId));
            tables = valid.ToArray();
            return tables;
        }
    }

    public static EnemyThemeTable Resolve(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId)) return null;
        // Saved maps retain their seed and options when their retired theme is replaced.
        if (themeId == "DeathHarvest") themeId = "PrimalHunt";
        foreach (var table in Tables)
            if (string.Equals(table.ThemeId, themeId, StringComparison.Ordinal)) return table;
        return null;
    }

    public static string DisplayName(string themeId)
    {
        var table = Resolve(themeId);
        return table != null ? table.DisplayName : "미지정";
    }

    public static bool IsEnabledForRuns(string themeId) => themeId != "DeathHarvest" && Resolve(themeId) != null;

    public static EnemyThemeTable ResolveForRun(string themeId)
    {
        var resolved = Resolve(themeId);
        if (resolved != null) return resolved;
        // Existing saved maps remain usable while this theme is temporarily disabled.
        foreach (var table in Tables) if (IsEnabledForRuns(table.ThemeId)) return table;
        return null;
    }

    public static string RollThemeId()
    {
        var available = new List<EnemyThemeTable>();
        foreach (var table in Tables) if (IsEnabledForRuns(table.ThemeId)) available.Add(table);
        if (available.Count == 0) throw new InvalidOperationException("사용 가능한 몬스터 테마가 없습니다.");
        return available[UnityEngine.Random.Range(0, available.Count)].ThemeId;
    }

    // One theme per newly acquired map. Regional weights are initial playtest tuning.
    public static string RollThemeId(MapTerrainKind terrain, System.Random random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        int total = 0;
        foreach (var table in Tables) total += RegionalWeight(terrain, table.ThemeId);
        if (total == 0) throw new InvalidOperationException("이 지형에서 사용할 몬스터 테마가 없습니다.");
        int pick = random.Next(total);
        foreach (var table in Tables)
        {
            pick -= RegionalWeight(terrain, table.ThemeId);
            if (pick < 0) return table.ThemeId;
        }
        throw new InvalidOperationException("지역 몬스터 테마 추첨에 실패했습니다.");
    }

    private static int RegionalWeight(MapTerrainKind terrain, string themeId)
    {
        if (terrain == MapTerrainKind.Desert)
        {
            switch (themeId)
            {
                case "PrimalHunt": return 45;
                case "VenomBrood": return 35;
            }
        }
        else if (terrain == MapTerrainKind.Cave)
        {
            switch (themeId)
            {
                case "CavernMutants": return 35;
                case "SpiderBrood": return 30;
                case "RotsporeMarsh": return 20;
                case "AlienContainment": return 15;
            }
        }
        return 0;
    }
}
