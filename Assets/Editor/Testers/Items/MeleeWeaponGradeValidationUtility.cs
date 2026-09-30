using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MeleeWeaponGradeValidationUtility
{
    private const int SamplesPerGrade = 64;
    private const float FloatTolerance = 0.0001f;

    private static readonly ItemGrade[] Grades =
    {
        ItemGrade.Common,
        ItemGrade.Uncommon,
        ItemGrade.Rare,
        ItemGrade.Epic,
        ItemGrade.Legendary,
        ItemGrade.Artifact,
        ItemGrade.Mythic,
        ItemGrade.Cursed
    };

    [MenuItem("OVERBURST/Codex/Validate/Items/Validate Formal Melee Weapon Grade")]
    public static void ValidateFromMenu()
    {
        ValidateOrThrow();
        Debug.Log("[MeleeWeaponGradeValidation] PASS");
    }

    public static void RunFromCommandLine()
    {
        try
        {
            ValidateOrThrow();
            Debug.Log("[MeleeWeaponGradeValidation] PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void ValidateOrThrow()
    {
        WeaponItemData weaponData = LoadMeleeWeapon();
        if (weaponData == null)
            throw new InvalidOperationException("No active melee WeaponItemData was found.");

        UnityEngine.Random.State previousState = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(20260713);
            ValidateConcentrationWeights();
            ValidateGradeContracts();
            ValidateGradeRolls(weaponData);
            ValidatePercentApplication();
            ValidateFinalCaps(weaponData);
        }
        finally
        {
            UnityEngine.Random.state = previousState;
        }
    }

    private static void ValidateGradeContracts()
    {
        int[] expectedPositive = { 0, 4, 8, 12, 15, 20, 20, 15 };
        int[] expectedNegative = { 0, 0, 0, 0, 0, 0, 0, 5 };
        int[,] expectedGuaranteed =
        {
            { 0, 0, 0, 0, 0 },
            { 1, 0, 0, 0, 0 },
            { 1, 0, 0, 0, 0 },
            { 2, 1, 0, 0, 0 },
            { 3, 1, 1, 0, 0 },
            { 3, 3, 2, 2, 2 },
            { 4, 3, 2, 2, 2 },
            { 7, 0, 0, 0, 0 }
        };
        float[,] expectedChances =
        {
            { 1f, 0f, 0f },
            { 0.85f, 0.12f, 0.03f },
            { 0.78f, 0.17f, 0.05f },
            { 0.70f, 0.22f, 0.08f },
            { 0.60f, 0.27f, 0.13f },
            { 0.50f, 0.32f, 0.18f },
            { 0.40f, 0.36f, 0.24f },
            { 0.10f, 0.40f, 0.50f }
        };

        for (int gradeIndex = 0; gradeIndex < Grades.Length; gradeIndex++)
        {
            ItemGrade grade = Grades[gradeIndex];
            if (WeaponGradeStatRoller.GetMeleePositiveStarCount(grade) != expectedPositive[gradeIndex])
                throw new InvalidOperationException(grade + " positive star contract mismatch.");
            if (WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade) != expectedNegative[gradeIndex])
                throw new InvalidOperationException(grade + " negative star contract mismatch.");
            IReadOnlyList<WeaponGradeStatType> statTypes = WeaponGradeStatRoller.GetPlannedMeleeRollableStats();
            for (int statIndex = 0; statIndex < statTypes.Count; statIndex++)
            {
                int actualGuaranteed = WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarCount(grade, statTypes[statIndex]);
                if (actualGuaranteed != expectedGuaranteed[gradeIndex, statIndex])
                    throw new InvalidOperationException(grade + " " + statTypes[statIndex] + " guaranteed star contract mismatch.");
            }

            WeaponGradeStatRoller.GetMeleePositiveStarChances(
                grade,
                out float whiteChance,
                out float greenChance,
                out float yellowChance);
            AssertApproximately(expectedChances[gradeIndex, 0], whiteChance, grade + " white chance");
            AssertApproximately(expectedChances[gradeIndex, 1], greenChance, grade + " green chance");
            AssertApproximately(expectedChances[gradeIndex, 2], yellowChance, grade + " yellow chance");
            AssertApproximately(1f, whiteChance + greenChance + yellowChance, grade + " chance total");
        }
    }

    private static void ValidateConcentrationWeights()
    {
        AssertApproximately(1f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(9), "9th star weight");
        AssertApproximately(0.5f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(10), "10th star weight");
        AssertApproximately(0.25f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(11), "11th star weight");
        AssertApproximately(0.125f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(12), "12th star weight");
        AssertApproximately(0.0625f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(13), "13th star weight");
    }

    private static void ValidateGradeRolls(WeaponItemData weaponData)
    {
        for (int gradeIndex = 0; gradeIndex < Grades.Length; gradeIndex++)
        {
            ItemGrade grade = Grades[gradeIndex];
            int expectedPositive = WeaponGradeStatRoller.GetMeleePositiveStarCount(grade);
            int expectedNegative = WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade);

            for (int sample = 0; sample < SamplesPerGrade; sample++)
            {
                ItemData item = new ItemData(weaponData, 1, grade);
                if (!WeaponGradeStatRoller.HasFormalMeleeGradeRolls(
                    weaponData,
                    grade,
                    item.weaponGradeStatRolls,
                    item.meleeStarDistributionProfile))
                {
                    throw new InvalidOperationException("Melee item did not use the formal grade contract. grade=" + grade);
                }

                ValidateItemRolls(
                    item,
                    expectedPositive,
                    expectedNegative,
                    grade);
            }
        }
    }

    private static void ValidateItemRolls(
        ItemData item,
        int expectedPositive,
        int expectedNegative,
        ItemGrade grade)
    {
        if (item.weaponGradeStatRolls == null || item.weaponGradeStatRolls.Count != 5)
            throw new InvalidOperationException("Current melee grade roll must contain exactly 5 stat roll records.");

        int totalPositive = 0;
        int totalNegative = 0;

        for (int i = 0; i < item.weaponGradeStatRolls.Count; i++)
        {
            WeaponGradeStatRoll roll = item.weaponGradeStatRolls[i];
            if (roll == null || !WeaponGradeStatRoller.IsMeleeStat(roll.statType))
                throw new InvalidOperationException("Current melee grade roll contains an invalid stat type.");

            if (roll.starRolls == null || roll.starRolls.Count != roll.TotalStarCount)
                throw new InvalidOperationException("Stored star list count does not match stat star counts.");

            bool redStarted = false;
            float expectedPositiveValue = 0f;
            float expectedNegativeValue = 0f;
            int countedPositive = 0;
            int countedNegative = 0;
            float baseValue = WeaponGradeStatRoller.GetMeleeBaseStarValue(roll.statType);

            for (int starIndex = 0; starIndex < roll.starRolls.Count; starIndex++)
            {
                WeaponGradeStarRoll star = roll.starRolls[starIndex];
                if (star == null)
                    throw new InvalidOperationException("A stored star entry is null.");

                if (star.IsNegative)
                {
                    redStarted = true;
                    countedNegative++;
                    expectedNegativeValue += baseValue;
                }
                else
                {
                    if (redStarted)
                        throw new InvalidOperationException("A positive star was stored after a red star.");

                    countedPositive++;
                    expectedPositiveValue += baseValue * star.ValueMultiplier;
                }
            }

            if (countedPositive != roll.positiveStarCount || countedNegative != roll.negativeStarCount)
                throw new InvalidOperationException("Stored star colors do not match positive/negative counts.");

            AssertApproximately(expectedPositiveValue, roll.positiveTotalValue, roll.statType + " positive total");
            AssertApproximately(expectedNegativeValue, roll.negativeTotalValue, roll.statType + " negative total");
            totalPositive += roll.positiveStarCount;
            totalNegative += roll.negativeStarCount;
            int guaranteedMinimum = WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarCount(grade, roll.statType);
            if (roll.positiveStarCount < guaranteedMinimum)
            {
                throw new InvalidOperationException(
                    "Guaranteed star mismatch. stat=" + roll.statType
                    + " actual=" + roll.positiveStarCount
                    + " minimum=" + guaranteedMinimum);
            }
        }

        if (totalPositive != expectedPositive || totalNegative != expectedNegative)
        {
            throw new InvalidOperationException(
                "Grade star total mismatch. positive=" + totalPositive + "/" + expectedPositive
                + " negative=" + totalNegative + "/" + expectedNegative);
        }

    }

    private static void ValidatePercentApplication()
    {
        AssertApproximately(0.25f, WeaponGradeStatRoller.GetMeleeBaseStarValue(WeaponGradeStatType.Damage), "damage star value");
        AssertApproximately(0.03f, WeaponGradeStatRoller.GetMeleeBaseStarValue(WeaponGradeStatType.AttackSpeed), "attack speed star value");
        AssertApproximately(0.03f, WeaponGradeStatRoller.GetMeleeBaseStarValue(WeaponGradeStatType.AttackRange), "attack range star value");
        AssertApproximately(3f, WeaponGradeStatRoller.GetMeleeBaseStarValue(WeaponGradeStatType.CritChance), "critical chance star value");
        AssertApproximately(0.06f, WeaponGradeStatRoller.GetMeleeBaseStarValue(WeaponGradeStatType.CritDamage), "critical damage star value");
    }

    private static void ValidateFinalCaps(WeaponItemData weaponData)
    {
        AssertApproximately(1.30f, MeleeAttackSpeedPolicy.BaselineAnimationSpeedMultiplier, "melee playback baseline");
        AssertApproximately(1.50f, WeaponGradeStatRoller.MaximumMeleeAttackSpeedMultiplier, "displayed attack speed cap");

        ItemGrade[] capGrades = { ItemGrade.Mythic, ItemGrade.Cursed };
        for (int gradeIndex = 0; gradeIndex < capGrades.Length; gradeIndex++)
        {
            for (int sampleIndex = 0; sampleIndex < 256; sampleIndex++)
            {
                ItemData item = new ItemData(weaponData, 1, capGrades[gradeIndex]);
                WeaponFinalStats stats = WeaponStatCalculator.CalculateWeaponBase(item);
                if (stats.meleeAttackSpeedMultiplier > WeaponGradeStatRoller.MaximumMeleeAttackSpeedMultiplier + FloatTolerance)
                    throw new InvalidOperationException("attack speed cap exceeded");
                if (stats.range > weaponData.baseStats.range * WeaponGradeStatRoller.MaximumMeleeAttackRangeMultiplier + FloatTolerance)
                    throw new InvalidOperationException("attack range cap exceeded");
                if (stats.critChance > WeaponGradeStatRoller.MaximumMeleeCriticalChance + FloatTolerance)
                    throw new InvalidOperationException("critical chance cap exceeded");
                if (stats.critDamageMultiplier > WeaponGradeStatRoller.MaximumMeleeCriticalDamageMultiplier + FloatTolerance)
                    throw new InvalidOperationException("critical damage cap exceeded");
            }
        }
    }

    private static void AssertApproximately(float expected, float actual, string label)
    {
        if (Mathf.Abs(expected - actual) > FloatTolerance)
            throw new InvalidOperationException(label + " mismatch. expected=" + expected + " actual=" + actual);
    }

    private static WeaponItemData LoadMeleeWeapon()
    {
        string[] guids = AssetDatabase.FindAssets("t:WeaponItemData", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            WeaponItemData weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(path);
            if (weapon != null && WeaponGradeStatRoller.IsMeleeWeapon(weapon) && WeaponContentPolicy.IsActiveWeapon(weapon))
                return weapon;
        }

        return null;
    }
}
