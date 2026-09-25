using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum WeaponGradeStatType
{
    Damage,
    Rpm,
    MagazineSize,
    ReloadDuration,
    Range,
    Recoil,
    RecoilRecovery,
    CritChance,
    CritDamage,
    AttackSpeed,
    AttackRange
}

public enum WeaponGradeStarType // 무기 등급 별 종류
{
    White,
    Green,
    Yellow,
    Red
}

public enum MeleeStarDistributionProfile // 밀리 별 배분 성향
{
    None,
    Focused,
    DualCore,
    Spread
}

[System.Serializable]
public class WeaponGradeStarRoll // 개별 별 결과
{
    public WeaponGradeStarType starType;

    public bool IsNegative => starType == WeaponGradeStarType.Red;

    public float ValueMultiplier
    {
        get
        {
            switch (starType)
            {
                case WeaponGradeStarType.Green: return 1.5f;
                case WeaponGradeStarType.Yellow: return 2f;
                default: return 1f;
            }
        }
    }
}

[System.Serializable]
public class WeaponGradeStatRoll // 등급 별 결과
{
    public WeaponGradeStatType statType; // 대상 스탯
    public int positiveStarCount; // 긍정 별
    public int negativeStarCount; // 부정 별
    public float positiveTotalValue; // 긍정 합계
    public float negativeTotalValue; // 부정 합계
    public List<WeaponGradeStarRoll> starRolls; // 개별 별 저장

    public bool HasStars
    {
        get { return positiveStarCount > 0 || negativeStarCount > 0; }
    }

    public int TotalStarCount
    {
        get { return Mathf.Max(0, positiveStarCount) + Mathf.Max(0, negativeStarCount); }
    }

    public List<WeaponGradeStarRoll> GetDisplayStars()
    {
        if (starRolls != null && starRolls.Count > 0)
            return starRolls;

        int positiveCount = Mathf.Max(0, positiveStarCount);
        int negativeCount = Mathf.Max(0, negativeStarCount);
        List<WeaponGradeStarRoll> legacyStars = new List<WeaponGradeStarRoll>(positiveCount + negativeCount);

        for (int i = 0; i < positiveCount; i++)
            legacyStars.Add(new WeaponGradeStarRoll { starType = WeaponGradeStarType.White });

        for (int i = 0; i < negativeCount; i++)
            legacyStars.Add(new WeaponGradeStarRoll { starType = WeaponGradeStarType.Red });

        return legacyStars;
    }
}

public static class WeaponGradeStatRoller // 등급 별 롤러
{
    public const float MaximumMeleeAttackSpeedMultiplier = MeleeAttackSpeedPolicy.MaximumDisplayedAttackSpeedMultiplier;
    public const float MaximumMeleeAttackRangeMultiplier = 2f;
    public const float MaximumMeleeCriticalChance = 60f;
    public const float MaximumMeleeCriticalDamageMultiplier = 2f;
    public const int ConcentrationDiminishingStartCount = 10;
    public const float FocusedProfileChance = 0.30f;
    public const float DualCoreProfileChance = 0.45f;
    public const float SpreadProfileChance = 0.25f;
    public const float CursedPrimaryProtectionChance = 0.70f;

    private const float CursedPrimaryFlawAffinity = 2f;
    private const float CursedSecondaryFlawAffinity = 1.4f;
    private const float CursedOtherFlawAffinity = 0.75f;
    private const float CursedMomentumPerStar = 0.08f;
    private const float CursedMaxMomentumBonus = 0.4f;

    private readonly struct MeleeDistributionSettings
    {
        public readonly MeleeStarDistributionProfile Profile;
        public readonly int SeedCount;
        public readonly float FirstAffinity;
        public readonly float SecondAffinity;
        public readonly float ThirdAffinity;
        public readonly float OtherAffinity;
        public readonly float MomentumPerStar;
        public readonly float MaxMomentumBonus;

        public MeleeDistributionSettings(
            MeleeStarDistributionProfile profile,
            int seedCount,
            float firstAffinity,
            float secondAffinity,
            float thirdAffinity,
            float otherAffinity,
            float momentumPerStar,
            float maxMomentumBonus)
        {
            Profile = profile;
            SeedCount = seedCount;
            FirstAffinity = firstAffinity;
            SecondAffinity = secondAffinity;
            ThirdAffinity = thirdAffinity;
            OtherAffinity = otherAffinity;
            MomentumPerStar = momentumPerStar;
            MaxMomentumBonus = maxMomentumBonus;
        }

        public float GetSeedAffinity(int seedOrder)
        {
            switch (seedOrder)
            {
                case 0: return FirstAffinity;
                case 1: return SecondAffinity;
                case 2: return ThirdAffinity;
                default: return OtherAffinity;
            }
        }
    }

    private sealed class MeleeDistributionContext
    {
        public MeleeDistributionSettings Settings;
        public float[] Affinities;
        public int PrimaryStatIndex = -1;
        public int[] GuaranteedPositiveStarCounts;
    }

    private static readonly WeaponGradeStatType[] RollableStats =
    {
        WeaponGradeStatType.Damage,
        WeaponGradeStatType.Rpm,
        WeaponGradeStatType.MagazineSize,
        WeaponGradeStatType.ReloadDuration,
        WeaponGradeStatType.Range,
        WeaponGradeStatType.Recoil,
        WeaponGradeStatType.RecoilRecovery,
        WeaponGradeStatType.CritChance,
        WeaponGradeStatType.CritDamage
    };

    private static readonly WeaponGradeStatType[] MeleeRollableStats =
    {
        WeaponGradeStatType.Damage,
        WeaponGradeStatType.AttackSpeed,
        WeaponGradeStatType.AttackRange,
        WeaponGradeStatType.CritChance,
        WeaponGradeStatType.CritDamage
    };

    public static List<WeaponGradeStatRoll> Roll(
        ItemGrade grade,
        WeaponItemData weaponData,
        out MeleeStarDistributionProfile distributionProfile)
    {
        distributionProfile = MeleeStarDistributionProfile.None;
        if (IsMeleeWeapon(weaponData))
        {
            List<WeaponGradeStatRoll> meleeRolls = CreateEmptyRolls(MeleeRollableStats);
            int positiveStarCount = GetMeleePositiveStarCount(grade);
            int negativeStarCount = GetMeleeNegativeStarCount(grade);
            int[] guaranteedStarCounts = CreateGuaranteedMeleeStarCounts(grade);
            int guaranteedStarTotal = GetGuaranteedMeleePositiveStarTotal(grade);
            ApplyGuaranteedMeleeStars(meleeRolls, guaranteedStarCounts, grade, weaponData);
            MeleeDistributionContext context = ApplyMeleePositiveStars(
                meleeRolls,
                positiveStarCount - guaranteedStarTotal,
                grade,
                weaponData,
                guaranteedStarCounts);
            distributionProfile = context.Settings.Profile;
            ApplyMeleeNegativeStars(meleeRolls, negativeStarCount, context.PrimaryStatIndex);
            return meleeRolls;
        }

        List<WeaponGradeStatRoll> rolls = CreateEmptyRolls(RollableStats); // 전체 스탯
        ApplyStars(rolls, GetPositiveStarCount(grade), true); // 긍정 별
        ApplyStars(rolls, GetNegativeStarCount(grade), false); // 부정 별
        return rolls;
    }

    public static bool IsMeleeWeapon(WeaponItemData weaponData)
    {
        return weaponData != null && weaponData.CombatFamily == WeaponCombatFamily.Melee;
    }

    public static bool IsMeleeStat(WeaponGradeStatType statType)
    {
        for (int i = 0; i < MeleeRollableStats.Length; i++)
        {
            if (MeleeRollableStats[i] == statType)
                return true;
        }

        return false;
    }

    public static IReadOnlyList<WeaponGradeStatType> GetPlannedMeleeRollableStats()
    {
        return MeleeRollableStats;
    }

    public static int GetMeleePositiveStarCount(ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon: return 4;
            case ItemGrade.Rare: return 8;
            case ItemGrade.Epic: return 12;
            case ItemGrade.Legendary: return 15;
            case ItemGrade.Artifact: return 20;
            case ItemGrade.Mythic: return 20;
            case ItemGrade.Cursed: return 15;
            case ItemGrade.Common:
            default:
                return 0;
        }
    }

    public static int GetMeleeNegativeStarCount(ItemGrade grade)
    {
        return grade == ItemGrade.Cursed ? 5 : 0;
    }

    public static int GetGuaranteedMeleeDamageStarCount(ItemGrade grade)
    {
        return GetGuaranteedMeleePositiveStarCount(grade, WeaponGradeStatType.Damage);
    }

    public static int GetGuaranteedMeleePositiveStarCount(
        ItemGrade grade,
        WeaponGradeStatType statType)
    {
        if (!IsMeleeStat(statType))
            return 0;

        switch (grade)
        {
            case ItemGrade.Uncommon:
                return statType == WeaponGradeStatType.Damage ? 1 : 0;
            case ItemGrade.Rare:
                return statType == WeaponGradeStatType.Damage ? 1 : 0;
            case ItemGrade.Epic:
                if (statType == WeaponGradeStatType.Damage) return 2;
                if (statType == WeaponGradeStatType.AttackSpeed) return 1;
                return 0;
            case ItemGrade.Legendary:
                if (statType == WeaponGradeStatType.Damage) return 3;
                if (statType == WeaponGradeStatType.AttackSpeed
                    || statType == WeaponGradeStatType.AttackRange) return 1;
                return 0;
            case ItemGrade.Artifact:
                if (statType == WeaponGradeStatType.Damage
                    || statType == WeaponGradeStatType.AttackSpeed) return 3;
                return 2;
            case ItemGrade.Mythic:
                if (statType == WeaponGradeStatType.Damage) return 4;
                if (statType == WeaponGradeStatType.AttackSpeed) return 3;
                return 2;
            case ItemGrade.Cursed:
                return statType == WeaponGradeStatType.Damage ? 7 : 0;
            case ItemGrade.Common:
            default:
                return 0;
        }
    }

    public static int GetGuaranteedMeleePositiveStarTotal(ItemGrade grade)
    {
        int total = 0;
        for (int i = 0; i < MeleeRollableStats.Length; i++)
            total += GetGuaranteedMeleePositiveStarCount(grade, MeleeRollableStats[i]);

        return total;
    }

    public static void GetMeleePositiveStarChances(
        ItemGrade grade,
        out float whiteChance,
        out float greenChance,
        out float yellowChance)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon:
                whiteChance = 0.85f;
                greenChance = 0.12f;
                yellowChance = 0.03f;
                return;
            case ItemGrade.Rare:
                whiteChance = 0.78f;
                greenChance = 0.17f;
                yellowChance = 0.05f;
                return;
            case ItemGrade.Epic:
                whiteChance = 0.70f;
                greenChance = 0.22f;
                yellowChance = 0.08f;
                return;
            case ItemGrade.Legendary:
                whiteChance = 0.60f;
                greenChance = 0.27f;
                yellowChance = 0.13f;
                return;
            case ItemGrade.Artifact:
                whiteChance = 0.50f;
                greenChance = 0.32f;
                yellowChance = 0.18f;
                return;
            case ItemGrade.Mythic:
                whiteChance = 0.40f;
                greenChance = 0.36f;
                yellowChance = 0.24f;
                return;
            case ItemGrade.Cursed:
                whiteChance = 0.10f;
                greenChance = 0.40f;
                yellowChance = 0.50f;
                return;
            case ItemGrade.Common:
            default:
                whiteChance = 1f;
                greenChance = 0f;
                yellowChance = 0f;
                return;
        }
    }

    public static float GetConcentrationWeightForResultingCount(int resultingCount)
    {
        if (resultingCount < ConcentrationDiminishingStartCount)
            return 1f;

        int halvingCount = resultingCount - ConcentrationDiminishingStartCount + 1;
        return Mathf.Pow(0.5f, halvingCount);
    }

    public static float GetMeleeBaseStarValue(WeaponGradeStatType statType)
    {
        switch (statType)
        {
            case WeaponGradeStatType.Damage: return 0.25f;
            case WeaponGradeStatType.AttackSpeed: return 0.02f;
            case WeaponGradeStatType.AttackRange: return 0.03f;
            case WeaponGradeStatType.CritChance: return 3f;
            case WeaponGradeStatType.CritDamage: return 0.06f;
            default: return 0f;
        }
    }

    public static float GetMeleeMaximumPositiveGradeValue(
        WeaponItemData weaponData,
        WeaponGradeStatType statType)
    {
        if (weaponData == null)
            return 0f;

        switch (statType)
        {
            case WeaponGradeStatType.Damage:
                return float.PositiveInfinity;
            case WeaponGradeStatType.AttackSpeed:
                MeleeWeaponDefinition meleeDefinition = weaponData.GetMeleeDefinition();
                float baseAttackSpeed = meleeDefinition != null
                    ? meleeDefinition.baseSettings.SafeAttackSpeedMultiplier
                    : 1f;
                return Mathf.Max(0f, MaximumMeleeAttackSpeedMultiplier - baseAttackSpeed);
            case WeaponGradeStatType.AttackRange:
                return MaximumMeleeAttackRangeMultiplier - 1f;
            case WeaponGradeStatType.CritChance:
                return Mathf.Max(0f, MaximumMeleeCriticalChance - weaponData.baseStats.criticalChance);
            case WeaponGradeStatType.CritDamage:
                return Mathf.Max(0f, MaximumMeleeCriticalDamageMultiplier - weaponData.baseStats.criticalDamageMultiplier);
            default:
                return 0f;
        }
    }

    public static bool HasFormalMeleeGradeRolls(
        WeaponItemData weaponData,
        ItemGrade grade,
        IList<WeaponGradeStatRoll> rolls,
        MeleeStarDistributionProfile distributionProfile)
    {
        if (!IsMeleeWeapon(weaponData) || rolls == null || rolls.Count != MeleeRollableStats.Length)
            return false;

        HashSet<WeaponGradeStatType> foundStats = new HashSet<WeaponGradeStatType>();
        int totalPositive = 0;
        int totalNegative = 0;

        for (int rollIndex = 0; rollIndex < rolls.Count; rollIndex++)
        {
            WeaponGradeStatRoll roll = rolls[rollIndex];
            if (roll == null || !IsMeleeStat(roll.statType) || !foundStats.Add(roll.statType))
                return false;
            if (roll.positiveStarCount < 0 || roll.negativeStarCount < 0)
                return false;
            if (roll.starRolls == null || roll.starRolls.Count != roll.TotalStarCount)
                return false;

            float expectedPositiveValue = 0f;
            float expectedNegativeValue = 0f;
            for (int starIndex = 0; starIndex < roll.starRolls.Count; starIndex++)
            {
                WeaponGradeStarRoll star = roll.starRolls[starIndex];
                if (star == null)
                    return false;

                bool shouldBeNegative = starIndex >= roll.positiveStarCount;
                if (star.IsNegative != shouldBeNegative)
                    return false;

                float baseValue = GetMeleeBaseStarValue(roll.statType);
                if (shouldBeNegative)
                    expectedNegativeValue += baseValue;
                else
                    expectedPositiveValue += baseValue * star.ValueMultiplier;
            }

            if (Mathf.Abs(roll.positiveTotalValue - expectedPositiveValue) > 0.0001f
                || Mathf.Abs(roll.negativeTotalValue - expectedNegativeValue) > 0.0001f)
            {
                return false;
            }

            float positiveCap = GetMeleeMaximumPositiveGradeValue(weaponData, roll.statType);
            if (!float.IsPositiveInfinity(positiveCap) && roll.positiveTotalValue > positiveCap + 0.0001f)
                return false;

            totalPositive += roll.positiveStarCount;
            totalNegative += roll.negativeStarCount;
        }

        if (totalPositive != GetMeleePositiveStarCount(grade)
            || totalNegative != GetMeleeNegativeStarCount(grade))
        {
            return false;
        }

        for (int i = 0; i < MeleeRollableStats.Length; i++)
        {
            WeaponGradeStatType statType = MeleeRollableStats[i];
            WeaponGradeStatRoll guaranteedRoll = GetRoll(rolls, statType);
            if (guaranteedRoll == null
                || guaranteedRoll.positiveStarCount < GetGuaranteedMeleePositiveStarCount(grade, statType))
            {
                return false;
            }
        }

        int randomPositiveCount = totalPositive - GetGuaranteedMeleePositiveStarTotal(grade);
        return randomPositiveCount > 0
            ? distributionProfile != MeleeStarDistributionProfile.None
            : distributionProfile == MeleeStarDistributionProfile.None;
    }

    public static bool TryRefreshMeleeGradeRollValues(IList<WeaponGradeStatRoll> rolls)
    {
        if (rolls == null)
            return false;

        for (int rollIndex = 0; rollIndex < rolls.Count; rollIndex++)
        {
            WeaponGradeStatRoll roll = rolls[rollIndex];
            if (roll == null
                || !IsMeleeStat(roll.statType)
                || roll.starRolls == null
                || roll.starRolls.Count != roll.TotalStarCount)
            {
                return false;
            }

            float positiveTotalValue = 0f;
            float negativeTotalValue = 0f;
            float baseValue = GetMeleeBaseStarValue(roll.statType);
            for (int starIndex = 0; starIndex < roll.starRolls.Count; starIndex++)
            {
                WeaponGradeStarRoll star = roll.starRolls[starIndex];
                if (star == null)
                    return false;

                bool shouldBeNegative = starIndex >= roll.positiveStarCount;
                if (star.IsNegative != shouldBeNegative)
                    return false;

                if (shouldBeNegative)
                    negativeTotalValue += baseValue;
                else
                    positiveTotalValue += baseValue * star.ValueMultiplier;
            }

            roll.positiveTotalValue = positiveTotalValue;
            roll.negativeTotalValue = negativeTotalValue;
        }

        return true;
    }

    public static WeaponGradeStatRoll GetRoll(IList<WeaponGradeStatRoll> rolls, WeaponGradeStatType statType)
    {
        if (rolls == null)
            return null;

        for (int i = 0; i < rolls.Count; i++)
        {
            WeaponGradeStatRoll roll = rolls[i]; // 후보
            if (roll != null && roll.statType == statType)
                return roll;
        }

        return null;
    }

    private static List<WeaponGradeStatRoll> CreateEmptyRolls(
        IReadOnlyList<WeaponGradeStatType> rollableStats)
    {
        int count = rollableStats != null ? rollableStats.Count : 0;
        List<WeaponGradeStatRoll> rolls = new List<WeaponGradeStatRoll>(count);

        for (int i = 0; i < count; i++)
        {
            rolls.Add(new WeaponGradeStatRoll
            {
                statType = rollableStats[i],
                starRolls = new List<WeaponGradeStarRoll>()
            });
        }

        return rolls;
    }

    private static MeleeDistributionContext ApplyMeleePositiveStars(
        List<WeaponGradeStatRoll> rolls,
        int count,
        ItemGrade grade,
        WeaponItemData weaponData,
        int[] guaranteedStarCounts)
    {
        MeleeDistributionSettings settings = count > 0
            ? RollMeleeDistributionSettings()
            : GetMeleeDistributionSettings(MeleeStarDistributionProfile.None);
        MeleeDistributionContext context = new MeleeDistributionContext
        {
            Settings = settings,
            Affinities = CreateFilledFloatArray(rolls != null ? rolls.Count : 0, settings.OtherAffinity),
            GuaranteedPositiveStarCounts = guaranteedStarCounts
        };

        if (rolls == null || rolls.Count == 0 || count <= 0)
            return context;

        List<int> availableIndices = CreateIndexList(rolls.Count);
        RemoveAboveMinimumGuaranteedStatsFromSeedCandidates(availableIndices, guaranteedStarCounts);
        int seedCount = Mathf.Min(count, Mathf.Min(settings.SeedCount, rolls.Count));
        seedCount = Mathf.Min(seedCount, availableIndices.Count);
        for (int seedOrder = 0; seedOrder < seedCount; seedOrder++)
        {
            int availableIndex = Random.Range(0, availableIndices.Count);
            int statIndex = availableIndices[availableIndex];
            availableIndices.RemoveAt(availableIndex);
            context.Affinities[statIndex] = settings.GetSeedAffinity(seedOrder);
            if (seedOrder == 0)
                context.PrimaryStatIndex = statIndex;

            WeaponGradeStarType starType = RollPositiveStarType(grade);
            WeaponGradeStatRoll target = RedirectPositiveStarToDamageIfCapped(
                rolls,
                rolls[statIndex],
                starType,
                weaponData);
            AddMeleeStar(target, true, starType);
        }

        for (int starIndex = seedCount; starIndex < count; starIndex++)
        {
            WeaponGradeStatRoll target = PickMeleePositiveTarget(rolls, context);
            if (target == null)
                continue;

            WeaponGradeStarType starType = RollPositiveStarType(grade);
            target = RedirectPositiveStarToDamageIfCapped(rolls, target, starType, weaponData);
            AddMeleeStar(target, true, starType);
        }

        return context;
    }

    private static void ApplyMeleeNegativeStars(
        List<WeaponGradeStatRoll> rolls,
        int count,
        int positivePrimaryIndex)
    {
        if (rolls == null || rolls.Count == 0 || count <= 0)
            return;

        float[] flawAffinities = CreateFilledFloatArray(rolls.Count, CursedOtherFlawAffinity);
        List<int> flawCandidates = CreateIndexList(rolls.Count);
        bool protectPositivePrimary = positivePrimaryIndex >= 0
            && rolls.Count > 2
            && Random.value < CursedPrimaryProtectionChance;
        if (protectPositivePrimary)
            flawCandidates.Remove(positivePrimaryIndex);

        int flawSeedCount = Mathf.Min(2, Mathf.Min(count, flawCandidates.Count));
        for (int seedOrder = 0; seedOrder < flawSeedCount; seedOrder++)
        {
            int availableIndex = Random.Range(0, flawCandidates.Count);
            int statIndex = flawCandidates[availableIndex];
            flawCandidates.RemoveAt(availableIndex);
            flawAffinities[statIndex] = seedOrder == 0
                ? CursedPrimaryFlawAffinity
                : CursedSecondaryFlawAffinity;
            AddMeleeStar(rolls[statIndex], false, WeaponGradeStarType.Red);
        }

        for (int starIndex = flawSeedCount; starIndex < count; starIndex++)
        {
            WeaponGradeStatRoll target = PickMeleeNegativeTarget(rolls, flawAffinities);
            if (target == null)
                continue;

            AddMeleeStar(target, false, WeaponGradeStarType.Red);
        }
    }

    private static void AddMeleeStar(
        WeaponGradeStatRoll target,
        bool positive,
        WeaponGradeStarType starType)
    {
        if (target == null)
            return;

        if (target.starRolls == null)
            target.starRolls = new List<WeaponGradeStarRoll>();

        if (!positive)
            starType = WeaponGradeStarType.Red;

        WeaponGradeStarRoll star = new WeaponGradeStarRoll { starType = starType };
        target.starRolls.Add(star);

        float baseValue = GetMeleeBaseStarValue(target.statType);
        if (positive)
        {
            target.positiveStarCount++;
            target.positiveTotalValue += baseValue * star.ValueMultiplier;
        }
        else
        {
            target.negativeStarCount++;
            target.negativeTotalValue += baseValue;
        }
    }

    private static void ApplyGuaranteedMeleeStars(
        List<WeaponGradeStatRoll> rolls,
        int[] guaranteedStarCounts,
        ItemGrade grade,
        WeaponItemData weaponData)
    {
        if (rolls == null || guaranteedStarCounts == null)
            return;

        int count = Mathf.Min(rolls.Count, guaranteedStarCounts.Length);
        for (int rollIndex = 0; rollIndex < count; rollIndex++)
        {
            WeaponGradeStatRoll target = rolls[rollIndex];
            int guaranteedCount = Mathf.Max(0, guaranteedStarCounts[rollIndex]);
            for (int starIndex = 0; starIndex < guaranteedCount; starIndex++)
            {
                WeaponGradeStarType starType = RollPositiveStarType(grade);
                starType = DowngradeGuaranteedStarToFitCap(target, starType, weaponData);
                AddMeleeStar(target, true, starType);
            }
        }
    }

    private static WeaponGradeStarType DowngradeGuaranteedStarToFitCap(
        WeaponGradeStatRoll target,
        WeaponGradeStarType starType,
        WeaponItemData weaponData)
    {
        if (target == null || target.statType == WeaponGradeStatType.Damage)
            return starType;

        float maximumPositiveValue = GetMeleeMaximumPositiveGradeValue(weaponData, target.statType);
        float baseValue = GetMeleeBaseStarValue(target.statType);
        while (starType != WeaponGradeStarType.White
            && target.positiveTotalValue + baseValue * GetStarValueMultiplier(starType) > maximumPositiveValue + 0.0001f)
        {
            starType = starType == WeaponGradeStarType.Yellow
                ? WeaponGradeStarType.Green
                : WeaponGradeStarType.White;
        }

        return starType;
    }

    private static WeaponGradeStatRoll RedirectPositiveStarToDamageIfCapped(
        List<WeaponGradeStatRoll> rolls,
        WeaponGradeStatRoll selectedTarget,
        WeaponGradeStarType starType,
        WeaponItemData weaponData)
    {
        if (selectedTarget == null || selectedTarget.statType == WeaponGradeStatType.Damage)
            return selectedTarget;

        float maximumPositiveValue = GetMeleeMaximumPositiveGradeValue(weaponData, selectedTarget.statType);
        float nextValue = selectedTarget.positiveTotalValue
            + GetMeleeBaseStarValue(selectedTarget.statType) * GetStarValueMultiplier(starType);
        if (nextValue <= maximumPositiveValue + 0.0001f)
            return selectedTarget;

        return GetRoll(rolls, WeaponGradeStatType.Damage) ?? selectedTarget;
    }

    private static float GetStarValueMultiplier(WeaponGradeStarType starType)
    {
        switch (starType)
        {
            case WeaponGradeStarType.Green: return 1.5f;
            case WeaponGradeStarType.Yellow: return 2f;
            default: return 1f;
        }
    }

    private static WeaponGradeStatRoll PickMeleePositiveTarget(
        List<WeaponGradeStatRoll> rolls,
        MeleeDistributionContext context)
    {
        float[] weights = new float[rolls.Count];
        for (int i = 0; i < rolls.Count; i++)
        {
            WeaponGradeStatRoll roll = rolls[i];
            if (roll != null)
            {
                float momentumBonus = Mathf.Min(
                    GetRandomPositiveStarCount(roll, context) * context.Settings.MomentumPerStar,
                    context.Settings.MaxMomentumBonus);
                float momentum = 1f + momentumBonus;
                weights[i] = context.Affinities[i]
                    * momentum
                    * GetConcentrationWeightForResultingCount(roll.TotalStarCount + 1);
            }
        }

        return PickWeightedMeleeTarget(rolls, weights);
    }

    private static float GetRandomPositiveStarCount(
        WeaponGradeStatRoll roll,
        MeleeDistributionContext context)
    {
        if (roll == null)
            return 0f;

        int rollIndex = FindRollIndex(context != null ? context.GuaranteedPositiveStarCounts : null, roll.statType);
        int guaranteedCount = rollIndex >= 0 && context.GuaranteedPositiveStarCounts != null
            ? context.GuaranteedPositiveStarCounts[rollIndex]
            : 0;
        return Mathf.Max(0, roll.positiveStarCount - guaranteedCount);
    }

    private static int[] CreateGuaranteedMeleeStarCounts(ItemGrade grade)
    {
        int[] counts = new int[MeleeRollableStats.Length];
        for (int i = 0; i < counts.Length; i++)
            counts[i] = GetGuaranteedMeleePositiveStarCount(grade, MeleeRollableStats[i]);

        return counts;
    }

    private static void RemoveAboveMinimumGuaranteedStatsFromSeedCandidates(
        List<int> availableIndices,
        int[] guaranteedStarCounts)
    {
        if (availableIndices == null || guaranteedStarCounts == null || availableIndices.Count <= 1)
            return;

        int minimumGuaranteed = int.MaxValue;
        for (int i = 0; i < guaranteedStarCounts.Length; i++)
            minimumGuaranteed = Mathf.Min(minimumGuaranteed, Mathf.Max(0, guaranteedStarCounts[i]));

        for (int i = availableIndices.Count - 1; i >= 0; i--)
        {
            int statIndex = availableIndices[i];
            if (statIndex >= 0
                && statIndex < guaranteedStarCounts.Length
                && guaranteedStarCounts[statIndex] > minimumGuaranteed
                && availableIndices.Count > 1)
            {
                availableIndices.RemoveAt(i);
            }
        }
    }

    private static int FindRollIndex(int[] guaranteedStarCounts, WeaponGradeStatType statType)
    {
        if (guaranteedStarCounts == null)
            return -1;

        for (int i = 0; i < MeleeRollableStats.Length && i < guaranteedStarCounts.Length; i++)
        {
            if (MeleeRollableStats[i] == statType)
                return i;
        }

        return -1;
    }

    private static WeaponGradeStatRoll PickMeleeNegativeTarget(
        List<WeaponGradeStatRoll> rolls,
        float[] flawAffinities)
    {
        float[] weights = new float[rolls.Count];
        for (int i = 0; i < rolls.Count; i++)
        {
            WeaponGradeStatRoll roll = rolls[i];
            if (roll != null)
            {
                float momentumBonus = Mathf.Min(
                    Mathf.Max(0f, roll.negativeStarCount) * CursedMomentumPerStar,
                    CursedMaxMomentumBonus);
                float momentum = 1f + momentumBonus;
                weights[i] = flawAffinities[i]
                    * momentum
                    * GetConcentrationWeightForResultingCount(roll.TotalStarCount + 1);
            }
        }

        return PickWeightedMeleeTarget(rolls, weights);
    }

    private static WeaponGradeStatRoll PickWeightedMeleeTarget(
        List<WeaponGradeStatRoll> rolls,
        float[] weights)
    {
        float totalWeight = 0f;
        for (int i = 0; i < weights.Length; i++)
            totalWeight += Mathf.Max(0f, weights[i]);

        if (totalWeight <= 0f)
            return rolls[Random.Range(0, rolls.Count)];

        float selectedWeight = Random.value * totalWeight;
        WeaponGradeStatRoll fallback = null;
        for (int i = 0; i < rolls.Count; i++)
        {
            WeaponGradeStatRoll roll = rolls[i];
            if (roll == null)
                continue;

            fallback = roll;
            selectedWeight -= Mathf.Max(0f, weights[i]);
            if (selectedWeight <= 0f)
                return roll;
        }

        return fallback;
    }

    private static MeleeDistributionSettings RollMeleeDistributionSettings()
    {
        float profileRoll = Random.value;
        if (profileRoll < FocusedProfileChance)
            return GetMeleeDistributionSettings(MeleeStarDistributionProfile.Focused);
        if (profileRoll < FocusedProfileChance + DualCoreProfileChance)
            return GetMeleeDistributionSettings(MeleeStarDistributionProfile.DualCore);

        return GetMeleeDistributionSettings(MeleeStarDistributionProfile.Spread);
    }

    private static MeleeDistributionSettings GetMeleeDistributionSettings(
        MeleeStarDistributionProfile profile)
    {
        switch (profile)
        {
            case MeleeStarDistributionProfile.Focused:
                return new MeleeDistributionSettings(profile, 2, 2.4f, 1.45f, 0f, 0.75f, 0.12f, 0.60f);
            case MeleeStarDistributionProfile.DualCore:
                return new MeleeDistributionSettings(profile, 2, 2f, 2f, 0f, 0.85f, 0.08f, 0.40f);
            case MeleeStarDistributionProfile.Spread:
                return new MeleeDistributionSettings(profile, 3, 1.5f, 1.3f, 1.15f, 1f, 0.03f, 0.20f);
            default:
                return new MeleeDistributionSettings(MeleeStarDistributionProfile.None, 0, 1f, 1f, 1f, 1f, 0f, 0f);
        }
    }

    private static List<int> CreateIndexList(int count)
    {
        List<int> indices = new List<int>(Mathf.Max(0, count));
        for (int i = 0; i < count; i++)
            indices.Add(i);
        return indices;
    }

    private static float[] CreateFilledFloatArray(int count, float value)
    {
        float[] values = new float[Mathf.Max(0, count)];
        for (int i = 0; i < values.Length; i++)
            values[i] = value;
        return values;
    }

    private static WeaponGradeStarType RollPositiveStarType(ItemGrade grade)
    {
        GetMeleePositiveStarChances(grade, out float whiteChance, out float greenChance, out _);
        float roll = Random.value;
        if (roll < whiteChance)
            return WeaponGradeStarType.White;
        if (roll < whiteChance + greenChance)
            return WeaponGradeStarType.Green;

        return WeaponGradeStarType.Yellow;
    }

    private static void ApplyStars(List<WeaponGradeStatRoll> rolls, int count, bool positive)
    {
        for (int i = 0; i < count; i++)
        {
            WeaponGradeStatRoll roll = rolls[Random.Range(0, rolls.Count)]; // 랜덤 스탯
            float value = RollStarValue(roll.statType, positive); // 별 수치

            if (positive)
            {
                roll.positiveStarCount++;
                roll.positiveTotalValue += value;
            }
            else
            {
                roll.negativeStarCount++;
                roll.negativeTotalValue += value;
            }
        }
    }

    private static int GetPositiveStarCount(ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon: return 2;
            case ItemGrade.Rare: return 4;
            case ItemGrade.Epic: return 6;
            case ItemGrade.Legendary: return 10;
            case ItemGrade.Artifact: return 14;
            case ItemGrade.Mythic: return 18;
            case ItemGrade.Cursed: return 30;
            case ItemGrade.Common:
            default:
                return 0;
        }
    }

    private static int GetNegativeStarCount(ItemGrade grade)
    {
        return grade == ItemGrade.Cursed ? 10 : 0;
    }

    private static float RollStarValue(WeaponGradeStatType statType, bool positive)
    {
        switch (statType)
        {
            case WeaponGradeStatType.Damage:
                return positive ? Random.Range(1, 4) : Random.Range(1, 3);
            case WeaponGradeStatType.Rpm:
                return Random.Range(10, 16);
            case WeaponGradeStatType.MagazineSize:
                return positive ? Random.Range(1, 3) : 1f;
            case WeaponGradeStatType.ReloadDuration:
                return positive ? Random.Range(0.05f, 0.10f) : Random.Range(0.05f, 0.12f);
            case WeaponGradeStatType.Range:
                return Random.Range(0.5f, 1.0f);
            case WeaponGradeStatType.Recoil:
                return positive ? Random.Range(2, 5) : Random.Range(2, 6);
            case WeaponGradeStatType.RecoilRecovery:
                return Random.Range(0.2f, 0.5f);
            case WeaponGradeStatType.CritChance:
                return positive ? Random.Range(1, 4) : Random.Range(1, 3);
            case WeaponGradeStatType.CritDamage:
                return Random.Range(0.05f, 0.10f);
            default:
                return 0f;
        }
    }
}

[System.Serializable]
public class ItemData // 런타임 아이템
{
    private static long nextAcquisitionOrder = 1; // 획득 순번

    public BaseItemData baseData; // 원본 에셋
    public FlaskInstanceState flaskState;
    public string runtimeInstanceId; // 고유 id
    public long acquisitionOrder; // 획득 순서
    public int level; // 레벨
    public ItemGrade grade; // 등급
    public int stackCount; // 스택 수
    public string originRunId;
    public Overburst.Persistence.MapInstanceState mapState;
    [System.NonSerialized] private bool restoredFromValidatedSnapshot;
    public bool HasInstanceElement => hasInstanceElement;

    [UnityEngine.SerializeField] private WeaponElement instanceElement;
    [UnityEngine.SerializeField] private bool hasInstanceElement;
    public WeaponElement ResolvedElement
    {
        get
        {
            if (!(baseData is WeaponItemData weapon)) return WeaponElement.None;
            WeaponElement element = hasInstanceElement ? instanceElement : weapon.defaultElement;
            return OverburstElementRules.IsActive(element) ? element : WeaponElement.None;
        }
    }
    // Explicit authoring/test assignment; never called by read/ensure/pickup paths.
    public bool TryAssignElementOnce(WeaponElement element)
    {
        if (hasInstanceElement || !(baseData is WeaponItemData) || !OverburstElementRules.IsActive(element)) return false;
        instanceElement = element; hasInstanceElement = true; return true;
    }
    public List<WeaponGradeStatRoll> weaponGradeStatRolls; // 무기 별
    public List<GearStatRoll> gearRolls; // 방어구·장신구 고정 주능력치와 보조 3종
    public MeleeStarDistributionProfile meleeStarDistributionProfile; // 밀리 별 배분 성향
    public List<BagRandomOptionRoll> bagOptions; // 가방 랜덤 옵션


    public bool HasValidBaseData { get { return baseData != null; } }
    public List<WeaponGradeStatRoll> weaponGradeStats { get { return weaponGradeStatRolls; } }

    public string itemName
    {
        get
        {
            string name = baseData != null ? baseData.itemName : string.Empty;
            string element = OverburstElementRules.Label(ResolvedElement);
            return element.Length > 0 ? name + " (" + element + ")" : name;
        }
    }
    public Sprite icon
    {
        get
        {

            if (baseData is MapItemData mapData)
                return mapData.ResolveIconForLevel(mapState != null ? mapState.level : level);

            if (baseData is CurrencyItemData currencyData)
                return currencyData.GetDisplayIcon(stackCount);

            return baseData != null ? baseData.icon : null;
        }
    }

    public Color iconColor
    {
        get
        {
            if (icon != null) // 아이콘 원본색 사용, 등급색은 효과 레이어 담당
                return Color.white;

            return new Color(color.r, color.g, color.b, 0.65f);
        }
    }

    public Color color { get { return baseData != null ? baseData.color : Color.white; } }

    public string itemType
    {
        get
        {
            if (baseData == null) return "Unknown";
            if (baseData is WeaponItemData) return "Weapon";
            if (baseData is GearItemData) return "Gear";
            if (baseData is EquipmentItemData) return "Equipment";
            if (baseData is ConsumableItemData) return "Consumable";
            if (baseData is CurrencyItemData) return "Currency";
            if (baseData is BagItemData) return "Bag";
            if (baseData is MapItemData) return "Map";
            if (baseData is JunkItemData) return "Junk";
            if (baseData is QuestItemData) return "QuestItem";
            return "Unknown";
        }
    }

    public bool ShouldDisplayStackCount
    {
        get
        {
            if (stackCount <= 1)
                return false;

            if (baseData is ConsumableItemData consumableData && consumableData.IsPermanentSingleItem)
                return false;

            return true;
        }
    }

    public string weaponClassName
    {
        get
        {
            if (baseData is WeaponItemData weaponData)
                return weaponData.weaponClass.ToString();

            return string.Empty;
        }
    }

    public ItemData(BaseItemData data, int lv, ItemGrade itemGrade, int stack = 1, WeaponElement? element = null)
    {
        EnsureRuntimeInstanceId(); // id 보장
        baseData = data;
        level = data is WeaponItemData || data is GearItemData || data is FlaskItemData
            ? OverburstGrowthRules.ClampLevel(lv) : lv;
        grade = itemGrade;
        stackCount = stack;
        weaponGradeStatRolls = new List<WeaponGradeStatRoll>();
        gearRolls = new List<GearStatRoll>();
        bagOptions = new List<BagRandomOptionRoll>();

        if (baseData == null)
            return;

        if (baseData is WeaponItemData)
            RollWeaponGradeStats(); // 무기 별
        else if (baseData is GearItemData gear)
            gearRolls = GearQuality.Roll(gear, grade, GearSeed(runtimeInstanceId));
        else if (baseData is BagItemData)
            RollBagOptions(); // 가방 옵션

        if (baseData is WeaponItemData weapon)
        {
            instanceElement = element.HasValue ? element.Value : OverburstElementRules.RollNewWeapon(weapon);
            if (!OverburstElementRules.IsActive(instanceElement)) instanceElement = WeaponElement.None;
            hasInstanceElement = true;
        }
        if (baseData is FlaskItemData) FlaskRuntime.State(this);
    }

    private ItemData() { }

    public ItemData CopyStack(int count, bool newIdentity)
    {
        if (count <= 0) throw new System.ArgumentOutOfRangeException(nameof(count));
        var copy = (ItemData)MemberwiseClone();
        copy.stackCount = count;
        copy.weaponGradeStatRolls = Overburst.Persistence.ItemSnapshotCodec.CopyValues(weaponGradeStatRolls);
        copy.gearRolls = Overburst.Persistence.ItemSnapshotCodec.CopyValues(gearRolls);
        copy.bagOptions = Overburst.Persistence.ItemSnapshotCodec.CopyValues(bagOptions);
        copy.flaskState = Overburst.Persistence.ItemSnapshotCodec.CopyValues(flaskState);
        copy.mapState = Overburst.Persistence.ItemSnapshotCodec.CopyValues(mapState);
        if (newIdentity)
        {
            copy.runtimeInstanceId = System.Guid.NewGuid().ToString("N");
            copy.acquisitionOrder = nextAcquisitionOrder++;
        }
        return copy;
    }

    internal static ItemData RestoreSaved(Overburst.Persistence.ItemSnapshot value, BaseItemData data)
    {
        var item = new ItemData
        {
            baseData = data, runtimeInstanceId = value.instanceId, acquisitionOrder = value.acquisitionOrder,
            level = value.level, grade = value.grade, stackCount = value.count,
            originRunId = value.originRunId, instanceElement = value.element, hasInstanceElement = value.hasElement,
            meleeStarDistributionProfile = value.qualityProfile, weaponGradeStatRolls = value.weaponRolls,
            gearRolls = value.gearRolls, bagOptions = value.bagRolls, flaskState = value.flask,
            mapState = value.map, restoredFromValidatedSnapshot = true
        };
        nextAcquisitionOrder = System.Math.Max(nextAcquisitionOrder, checked(value.acquisitionOrder + 1));
        return item;
    }

    public void EnsureRuntimeInstanceId()
    {
        if (!string.IsNullOrEmpty(runtimeInstanceId))
            return;

        runtimeInstanceId = System.Guid.NewGuid().ToString("N");
    }

    public void EnsureAcquisitionOrder()
    {
        if (acquisitionOrder > 0)
            return;

        acquisitionOrder = nextAcquisitionOrder++;
    }

    public void EnsureRuntimeState()
    {
        if (restoredFromValidatedSnapshot) return;
        EnsureRuntimeInstanceId(); // id 보장

        if (baseData is WeaponItemData || baseData is GearItemData || baseData is FlaskItemData)
            level = OverburstGrowthRules.ClampLevel(level);

        if (baseData is FlaskItemData) FlaskRuntime.State(this);

        bool bagOptionsMissing = bagOptions == null; // 구 데이터

        if (weaponGradeStatRolls == null)
            weaponGradeStatRolls = new List<WeaponGradeStatRoll>(); // 구 데이터

        if (baseData is GearItemData gear && !GearQuality.IsValid(gear, grade, gearRolls))
            gearRolls = GearQuality.Roll(gear, grade, GearSeed(runtimeInstanceId));

        if (bagOptionsMissing)
            bagOptions = new List<BagRandomOptionRoll>(); // 구 데이터

        if (baseData is WeaponItemData)
        {
            EnsureWeaponGradeStatRolls(); // 별 보정
        }

        else if (baseData is BagItemData)
        {
            EnsureBagOptions(); // 누락 옵션
        }
    }

    private static int GearSeed(string id)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char value in id) { hash ^= value; hash *= 16777619; }
            return (int)hash;
        }
    }

    public void EnsureWeaponGradeStatRolls()
    {
        if (restoredFromValidatedSnapshot) return;
        if (!(baseData is WeaponItemData weaponData))
            return;

        if (WeaponGradeStatRoller.IsMeleeWeapon(weaponData))
        {
            WeaponGradeStatRoller.TryRefreshMeleeGradeRollValues(weaponGradeStatRolls);
            if (WeaponGradeStatRoller.HasFormalMeleeGradeRolls(
                weaponData,
                grade,
                weaponGradeStatRolls,
                meleeStarDistributionProfile))
            {
                return;
            }

            RollWeaponGradeStats(); // 현재 확정 계약과 다른 구 밀리 데이터는 1회 재생성
            return;
        }

        if (weaponGradeStatRolls != null && weaponGradeStatRolls.Count > 0)
        {
            return;
        }

        RollWeaponGradeStats(); // 누락 별
    }

    public bool IsSameRuntimeItem(ItemData other)
    {
        if (other == null)
            return false;

        EnsureRuntimeInstanceId(); // 내 id
        other.EnsureRuntimeInstanceId(); // 상대 id
        return runtimeInstanceId == other.runtimeInstanceId;
    }

    public bool TryGetWeaponComboAttackId(int comboStepIndex, out string attackId)
    {
        attackId = null;
        if (!TryGetStableWeaponComboAttackIds(out string[] attackIds, out _)
            || comboStepIndex < 0
            || comboStepIndex >= attackIds.Length)
        {
            return false;
        }

        attackId = attackIds[comboStepIndex];
        return true;
    }

    private bool TryGetStableWeaponComboAttackIds(out string[] attackIds, out string error)
    {
        attackIds = null;
        if (!(baseData is WeaponItemData weaponData))
        {
            error = "Item is not a weapon.";
            return false;
        }

        MeleeComboDefinition comboDefinition = weaponData.GetMeleeComboDefinition();
        if (comboDefinition == null)
        {
            error = "Melee combo definition is missing.";
            return false;
        }

        return comboDefinition.TryGetStableAttackIds(out attackIds, out error);
    }

    private void RollWeaponGradeStats()
    {
        WeaponItemData weaponData = baseData as WeaponItemData;
        weaponGradeStatRolls = WeaponGradeStatRoller.Roll(
            grade,
            weaponData,
            out MeleeStarDistributionProfile distributionProfile); // 별 롤
        meleeStarDistributionProfile = distributionProfile;
    }

    private void EnsureBagOptions()
    {
        if (!(baseData is BagItemData))
            return;

        int expectedCount = BagRandomOptionRoller.GetOptionCount(grade);
        if (expectedCount <= 0)
        {
            if (bagOptions != null && bagOptions.Count > 0)
                bagOptions.Clear();

            return;
        }

        if (bagOptions == null || bagOptions.Count != expectedCount)
            RollBagOptions();
    }

    private void RollBagOptions()
    {
        if (!(baseData is BagItemData))
            return;

        bagOptions = BagRandomOptionRoller.Roll(grade); // 가방 옵션 롤
    }
}
