using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

public static class MapOptionPolicy
{
    public const string ExtraDrop = "extra_drop";
    public const string ExtraExperience = "extra_experience";
    public const string EnemyAttackSpeed = "enemy_attack_speed";
    public const string EnemyHealth = "enemy_health";
    public const string EnemyDamage = "enemy_damage";
    public const string EnemySpeed = "enemy_speed";
    public const string MoreMediumElite = "more_medium_elite";
    public const string MoreSmall = "more_small";
    public const string MoreMedium = "more_medium";
    public const string MoreElite = "more_elite";
    public const string PlayerHealth = "player_health";
    public const string PlayerHealing = "player_healing";

    private static readonly string[] OptionIds =
    {
        EnemyHealth, EnemyDamage, EnemySpeed, EnemyAttackSpeed, MoreSmall, MoreMedium, MoreElite,
        PlayerHealth, PlayerHealing, ExtraDrop, ExtraExperience
    };
    // Initial tuning; reward options compete in the same pool and have no reserved slot.
    private static readonly int[] Weights = { 16, 16, 8, 5, 16, 12, 8, 9, 10, 12, 12 };
    private static readonly int[,] Minimum =
    {
        {10,15,20,28,35,45}, {4,6,8,11,15,20}, {3,4,5,7,9,11}, {2,3,4,5,6,7},
        {15,20,30,40,55,70}, {10,15,20,30,40,55}, {15,25,40,55,75,100},
        {3,4,6,8,10,12}, {5,8,12,16,20,25}, {10,15,20,28,35,45}, {10,15,20,28,35,45}
    };
    private static readonly int[,] Maximum =
    {
        {15,22,30,40,50,60}, {6,9,12,16,21,28}, {4,5,7,9,11,13}, {3,4,5,6,7,8},
        {20,30,40,55,70,90}, {15,20,30,40,55,70}, {25,40,55,75,100,125},
        {5,7,9,11,13,15}, {8,12,16,20,25,30}, {15,22,30,40,50,60}, {15,22,30,40,50,60}
    };

    public static List<MapOptionRoll> Roll(ItemGrade grade)
    {
        int index = Mathf.Clamp((int)grade, 0, 6);
        int count = index == 0 ? 0 : Random.Range(index, index + 2);
        var pool = new List<int>();
        for (int i = 0; i < OptionIds.Length; i++) pool.Add(i);
        var result = new List<MapOptionRoll>(count);
        int dangerCount = 0;
        for (int i = 0; i < count; i++)
        {
            int sum = 0;
            foreach (int candidate in pool)
                if (dangerCount < 3 || !IsDanger(OptionIds[candidate])) sum += Weights[candidate];
            int roll = Random.Range(0, sum);
            for (int j = 0; j < pool.Count; j++)
            {
                int selected = pool[j];
                string id = OptionIds[selected];
                if (dangerCount >= 3 && IsDanger(id)) continue;
                if ((roll -= Weights[selected]) >= 0) continue;
                result.Add(new MapOptionRoll { optionId = id,
                    value = Random.Range(Minimum[selected, index - 1], Maximum[selected, index - 1] + 1) * .01f });
                if (IsDanger(id)) dangerCount++;
                pool.RemoveAt(j);
                break;
            }
        }
        return result;
    }

    private static bool IsDanger(string id)
        => id == EnemyDamage || id == EnemySpeed || id == EnemyAttackSpeed || id == PlayerHealth;
    public static bool IsReward(string id) => id == ExtraDrop || id == ExtraExperience;

    public static float Value(MapInstanceState map, string optionId)
    {
        if (map?.options == null) return 0f;
        foreach (var option in map.options)
            if (option != null && option.optionId == optionId) return Mathf.Max(0f, option.value);
        return 0f;
    }

    public static float ExperienceMultiplier(MapInstanceState map)
        => MapFarmingLoot.Applies(map) ? 1f + Value(map, ExtraExperience)
            : 1f + Mathf.Clamp((int)(map?.grade ?? ItemGrade.Common), 0, 6) * .07f;

    public static float ExperienceDensity(int mapLevel)
    {
        int level = OverburstGrowthRules.ClampLevel(mapLevel);
        if (level <= 10) return Mathf.Lerp(.40f, .20f, (level - 1) / 9f);
        if (level <= 50) return Mathf.Lerp(.20f, .08f, (level - 10) / 40f);
        return Mathf.Lerp(.08f, .04f, Mathf.Clamp01((level - 50) / 40f));
    }

    public static float AttackSpeedMultiplier(EnemyRuntimeStats stats, MapInstanceState map, int level, bool boss)
    {
        var balance = OverburstBalanceTable.Current;
        float progress = (OverburstGrowthRules.ClampLevel(level) - 1) / 99f;
        float growth = (1f + balance.EnemyAttackGrowth * progress) * (1f + Value(map, EnemyAttackSpeed));
        return boss ? Mathf.Min(balance.EnemyAttackCap, stats.AttackSpeedMultiplier * growth)
            : EnemyRuntimeStats.ResolveAuthoredAttackSpeed(stats.AttackSpeedMultiplier, growth, balance.EnemyAttackCap);
    }

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
            stats.AnchorScale, stats.Tint, stats.AttackInterval);
    }

    public static string Describe(MapOptionRoll option)
    {
        if (option == null) return string.Empty;
        if (option.optionId == MoreMediumElite)
            return "몬스터 무리의 중형 +1, 중·후반 정예 +1";
        int percent = Mathf.RoundToInt(option.value * 100f);
        switch (option.optionId)
        {
            case ExtraDrop: return "아이템·자연 지도 드랍 확률 +" + percent + "%";
            case ExtraExperience: return "몬스터 처치 경험치 +" + percent + "%";
            case EnemyAttackSpeed: return "몬스터 공격속도 +" + percent + "%";
            case MoreSmall: return "소형 몬스터 수량 +" + percent + "%";
            case MoreMedium: return "중형 몬스터 수량 +" + percent + "%";
            case MoreElite: return "정예 몬스터 수량 +" + percent + "%";
            case EnemyHealth: return "몬스터 최대 체력 +" + percent + "%";
            case EnemyDamage: return "몬스터 공격력 +" + percent + "%";
            case EnemySpeed: return "몬스터 이동속도 +" + percent + "%";
            case PlayerHealth: return "플레이어 최대 체력 -" + percent + "%";
            case PlayerHealing: return "플레이어 회복 효과 -" + percent + "%";
            default: return string.Empty;
        }
    }
}
