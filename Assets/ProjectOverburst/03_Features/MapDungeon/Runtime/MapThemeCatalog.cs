using System;
using System.Collections.Generic;
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
        foreach (var table in Tables)
            if (string.Equals(table.ThemeId, themeId, StringComparison.Ordinal)) return table;
        return null;
    }

    public static string DisplayName(string themeId)
    {
        var table = Resolve(themeId);
        return table != null ? table.DisplayName : "미지정";
    }

    public static string RollThemeId()
    {
        var available = Tables;
        if (available.Count == 0) throw new InvalidOperationException("사용 가능한 몬스터 테마가 없습니다.");
        return available[UnityEngine.Random.Range(0, available.Count)].ThemeId;
    }
}
