using UnityEngine;

public static class OverburstGrowthRules
{
    public const int MaximumLevel = 100;

    public static int ClampLevel(int level) => Mathf.Clamp(level, 1, MaximumLevel);

    public static float ItemFactor(int level) => 1f + .03f * (ClampLevel(level) - 1);

    public static float PlayerAttackFactor(int level) => 1f + .005f * (ClampLevel(level) - 1);

    public static float PlayerHealthBonus(int level) => 5f * (ClampLevel(level) - 1);

    public static float PlayerArmorBonus(int level) => .25f * (ClampLevel(level) - 1);

    public static float EnemyHealthFactor(int level) => 1f + .055f * (ClampLevel(level) - 1);

    public static float EnemyDamageFactor(int level) => 1f + .065f * (ClampLevel(level) - 1);

    public static int MonsterLevelForDifficulty(int difficulty)
    {
        int tier = Mathf.Clamp(difficulty, 1, 30);
        return Mathf.RoundToInt(1f + (tier - 1) * 99f / 29f);
    }

    public static int ExperienceToNext(int level)
    {
        int n = ClampLevel(level) - 1;
        return level >= MaximumLevel ? 0 : 100 + 25 * n + 2 * n * n;
    }

    public static int ExperienceForKill(int monsterLevel, EnemyGradeType grade, int playerLevel)
    {
        int baseExperience = 10 + 3 * ClampLevel(monsterLevel);
        int gradeFactor = grade == EnemyGradeType.Boss ? 15 : grade == EnemyGradeType.Elite ? 4 : 1;
        float differenceFactor = Mathf.Clamp(1f + .1f * (monsterLevel - playerLevel), .1f, 1.5f);
        return Mathf.Max(1, Mathf.RoundToInt(baseExperience * gradeFactor * differenceFactor));
    }

    public static int RollDropItemLevel(int monsterLevel, bool boss)
    {
        return ClampLevel(monsterLevel + Random.Range(-2, 3) + (boss ? 2 : 0));
    }
}
