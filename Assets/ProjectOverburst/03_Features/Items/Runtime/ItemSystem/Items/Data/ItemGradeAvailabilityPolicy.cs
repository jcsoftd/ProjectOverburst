using UnityEngine;

public static class ItemGradeAvailabilityPolicy
{
    private static readonly bool cursedEnabled = false; // 재활성화 스위치
    private const float CursedTailThreshold = 0.995f; // 재활성화 준비용 임시 확률

    private static readonly ItemGrade[] enabledGrades = BuildEnabledGrades();

    private static ItemGrade[] BuildEnabledGrades()
    {
        ItemGrade[] grades = cursedEnabled
            ? new ItemGrade[8]
            : new ItemGrade[7];
        grades[0] = ItemGrade.Common;
        grades[1] = ItemGrade.Uncommon;
        grades[2] = ItemGrade.Rare;
        grades[3] = ItemGrade.Epic;
        grades[4] = ItemGrade.Legendary;
        grades[5] = ItemGrade.Artifact;
        grades[6] = ItemGrade.Mythic;
        if (cursedEnabled)
            grades[7] = ItemGrade.Cursed;
        return grades;
    }

    public static int EnabledGradeCount => enabledGrades.Length;

    public static bool IsEnabled(ItemGrade grade)
    {
        for (int i = 0; i < enabledGrades.Length; i++)
        {
            if (enabledGrades[i] == grade)
                return true;
        }

        return false;
    }

    public static ItemGrade[] GetEnabledGrades()
    {
        return (ItemGrade[])enabledGrades.Clone();
    }

    public static ItemGrade GetEnabledGrade(int index)
    {
        return enabledGrades[Mathf.Clamp(index, 0, enabledGrades.Length - 1)];
    }

    public static ItemGrade RollWeightedGrade()
    {
        return ResolveWeightedGrade(Random.value, cursedEnabled);
    }

    public static ItemGrade ResolveWeightedGrade(float roll, bool cursedActive)
    {
        roll = Mathf.Clamp01(roll);
        if (roll < 0.40f)
            return ItemGrade.Common;
        if (roll < 0.65f)
            return ItemGrade.Uncommon;
        if (roll < 0.80f)
            return ItemGrade.Rare;
        if (roll < 0.90f)
            return ItemGrade.Epic;
        if (roll < 0.95f)
            return ItemGrade.Legendary;
        if (roll < 0.98f)
            return ItemGrade.Artifact;

        if (cursedActive && roll >= CursedTailThreshold)
            return ItemGrade.Cursed;

        return ItemGrade.Mythic;
    }

    public static bool TryRollInRange(ItemGrade minGrade, ItemGrade maxGrade, out ItemGrade grade)
    {
        int min = Mathf.Min((int)minGrade, (int)maxGrade);
        int max = Mathf.Max((int)minGrade, (int)maxGrade);
        int eligibleCount = 0;
        for (int i = 0; i < enabledGrades.Length; i++)
        {
            int value = (int)enabledGrades[i];
            if (value >= min && value <= max)
                eligibleCount++;
        }

        if (eligibleCount == 0)
        {
            grade = default;
            return false; // 비활성 등급만 남은 범위는 드랍 제외
        }

        int selected = Random.Range(0, eligibleCount);
        for (int i = 0; i < enabledGrades.Length; i++)
        {
            int value = (int)enabledGrades[i];
            if (value < min || value > max)
                continue;
            if (selected-- == 0)
            {
                grade = enabledGrades[i];
                return true;
            }
        }

        grade = default;
        return false;
    }
}

#if UNITY_EDITOR
public static class ItemGradeAvailabilityPolicyFixture
{
    public static void Run()
    {
        if (!Validate())
            throw new System.InvalidOperationException("Item grade availability fixture failed.");
    }

    public static bool Validate()
    {
        if (ItemGradeAvailabilityPolicy.EnabledGradeCount != 7
            || !ItemGradeAvailabilityPolicy.IsEnabled(ItemGrade.Common)
            || !ItemGradeAvailabilityPolicy.IsEnabled(ItemGrade.Mythic)
            || ItemGradeAvailabilityPolicy.IsEnabled(ItemGrade.Cursed))
        {
            return false; // Common~Mythic만 활성
        }

        ItemGrade[] grades = ItemGradeAvailabilityPolicy.GetEnabledGrades();
        for (int i = 0; i < grades.Length; i++)
        {
            if (grades[i] != (ItemGrade)i)
                return false;
        }

        if (ItemGradeAvailabilityPolicy.TryRollInRange(ItemGrade.Cursed, ItemGrade.Cursed, out _))
            return false;

        return ItemGradeAvailabilityPolicy.ResolveWeightedGrade(0.99f, true) == ItemGrade.Mythic
            && ItemGradeAvailabilityPolicy.ResolveWeightedGrade(0.999f, true) == ItemGrade.Cursed
            && ItemGradeAvailabilityPolicy.ResolveWeightedGrade(0.999f, false) == ItemGrade.Mythic;
    }
}
#endif
