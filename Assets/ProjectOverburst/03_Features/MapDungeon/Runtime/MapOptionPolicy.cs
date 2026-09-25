using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

public static class MapOptionPolicy
{
    public const string EnemyHealth = "enemy_health";
    public const string EnemyDamage = "enemy_damage";
    public const string EnemySpeed = "enemy_speed";
    public const string MoreMediumElite = "more_medium_elite";
    public const string PlayerHealth = "player_health";
    public const string PlayerHealing = "player_healing";

    private static readonly string[] OptionIds =
    {
        EnemyHealth, EnemyDamage, EnemySpeed, MoreMediumElite, PlayerHealth, PlayerHealing
    };
    private static readonly int[] CountsByGrade = { 0, 1, 1, 2, 2, 3, 3 };

    public static List<MapOptionRoll> Roll(ItemGrade grade)
    {
        int index = Mathf.Clamp((int)grade, 0, CountsByGrade.Length - 1);
        int count = CountsByGrade[index];
        var pool = (string[])OptionIds.Clone();
        var result = new List<MapOptionRoll>(count);
        for (int i = 0; i < count; i++)
        {
            int selected = Random.Range(i, pool.Length);
            (pool[i], pool[selected]) = (pool[selected], pool[i]);
            float strength = 0.08f + index * 0.018f + Random.Range(0f, 0.035f);
            result.Add(new MapOptionRoll
            {
                optionId = pool[i], value = pool[i] == MoreMediumElite ? 1f : strength
            });
        }
        return result;
    }

    public static float Value(MapInstanceState map, string optionId)
    {
        if (map?.options == null) return 0f;
        foreach (var option in map.options)
            if (option != null && option.optionId == optionId) return Mathf.Max(0f, option.value);
        return 0f;
    }

    public static float ExperienceMultiplier(MapInstanceState map)
        => 1f + Mathf.Clamp((int)(map?.grade ?? ItemGrade.Common), 0, 6) * .07f;

    public static float HighGradeRollBias(ItemGrade mapGrade)
        => Mathf.Clamp((int)mapGrade, 0, 6) * .025f;

    public static EnemyRuntimeStats ApplyEnemyStats(EnemyRuntimeStats stats, MapInstanceState map)
    {
        if (map == null) return stats;
        return new EnemyRuntimeStats(
            stats.MaxHealth * (1f + Value(map, EnemyHealth)),
            stats.DamageMultiplier * (1f + Value(map, EnemyDamage)),
            stats.MoveSpeedMultiplier * (1f + Value(map, EnemySpeed)),
            stats.AttackSpeedMultiplier, stats.VisualScale, stats.CollisionScale,
            stats.AnchorScale, stats.Tint);
    }

    public static string Describe(MapOptionRoll option)
    {
        if (option == null) return string.Empty;
        if (option.optionId == MoreMediumElite)
            return "몬스터 무리의 중형 +1, 중·후반 정예 +1";
        int percent = Mathf.RoundToInt(option.value * 100f);
        switch (option.optionId)
        {
            case EnemyHealth: return "몬스터 최대 체력 +" + percent + "%";
            case EnemyDamage: return "몬스터 공격력 +" + percent + "%";
            case EnemySpeed: return "몬스터 이동속도 +" + percent + "%";
            case PlayerHealth: return "플레이어 최대 체력 -" + percent + "%";
            case PlayerHealing: return "플레이어 회복 효과 -" + percent + "%";
            default: return string.Empty;
        }
    }
}
