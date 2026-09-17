using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

public static class MeleeGradeRollSimulatorValidationUtility
{
    private const string OneHandSwordPath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const string GreatswordPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";

    [MenuItem("OVERBURST/Codex/Validate/Melee Grade Roll Simulator")]
    public static void RunFromMenu()
    {
        RunValidation();
        Debug.Log("[MeleeGradeRollSimulatorValidation] PASS");
    }

    public static void RunFromCommandLine()
    {
        RunValidation();
        Debug.Log("[MeleeGradeRollSimulatorValidation] PASS");
    }

    private static void RunValidation()
    {
        WeaponItemData oneHandSword = LoadWeapon(OneHandSwordPath);
        WeaponItemData greatsword = LoadWeapon(GreatswordPath);

        ValidateMeleeBalanceContracts(oneHandSword, greatsword);
        ValidateConcentrationWeights();
        ValidateGradeContracts();
        ValidateWeapon(oneHandSword, 14001);
        ValidateWeapon(greatsword, 24001);
        ValidateProfileDistribution(oneHandSword);
        ValidateLegacyMeleeMigrates(oneHandSword);
    }

    private static void ValidateMeleeBalanceContracts(
        WeaponItemData oneHandSword,
        WeaponItemData greatsword)
    {
        ValidateBaseStats(oneHandSword, 16f, 2.6f, 10f, 1.4f, 0.1f);
        ValidateBaseStats(greatsword, 20f, 3f, 15f, 1.6f, 0.3f);

        ValidateComboRangesAndVfx(
            oneHandSword,
            new[] { 1f, 1f, 1.1f },
            new[] { 0.94545454f, 0.94545454f, 1.04f });
        ValidateComboRangesAndVfx(
            greatsword,
            new[] { 1f, 1f, 2.3f / 3f },
            new[] { 1.0909091f, 1.0909091f, 1f });
        ValidateComboImpacts(
            oneHandSword,
            new[] { 0.9f, 0.95f, 1.15f },
            new[] { 1f, 1f, 1f });
        ValidateComboImpacts(
            greatsword,
            new[] { 1f, 1f, 1.15f },
            new[] { 1f, 1f, 1f });

        MeleeComboDefinition greatswordCombo = greatsword.GetMeleeComboDefinition();
        AttackPhaseData groundSlam = greatswordCombo.steps[2].attackPhases[0];
        RequireApproximately(2.5f, groundSlam.geometry.forwardOffset, "대검 3타 중심 전방 거리");
        for (int i = 0; i < greatswordCombo.steps.Length; i++)
        {
            AttackPhaseData phase = greatswordCombo.steps[i].attackPhases[0];
            float resolvedHitStun = Mathf.Max(
                    0f,
                    greatsword.GetMeleeDefinition().baseSettings.hitStunDuration)
                * phase.impact.SafeHitStunMultiplier;
            RequireApproximately(0.3f, resolvedHitStun, "대검 " + (i + 1) + "타 히트 경직");
        }
    }

    private static void ValidateBaseStats(
        WeaponItemData weapon,
        float damage,
        float range,
        float criticalChance,
        float criticalDamageMultiplier,
        float hitStunDuration)
    {
        RequireApproximately(damage, weapon.baseStats.damage, weapon.name + " 기본 공격력");
        RequireApproximately(range, weapon.baseStats.range, weapon.name + " 기본 사거리");
        RequireApproximately(criticalChance, weapon.baseStats.criticalChance, weapon.name + " 치명타 확률");
        RequireApproximately(
            criticalDamageMultiplier,
            weapon.baseStats.criticalDamageMultiplier,
            weapon.name + " 치명타 피해");
        RequireApproximately(
            hitStunDuration,
            Mathf.Max(0f, weapon.GetMeleeDefinition().baseSettings.hitStunDuration),
            weapon.name + " 기본 히트 경직");
    }

    private static void ValidateComboRangesAndVfx(
        WeaponItemData weapon,
        float[] expectedRangeMultipliers,
        float[] expectedVfxMultipliers)
    {
        MeleeComboDefinition combo = weapon.GetMeleeComboDefinition();
        Require(combo != null && combo.steps != null && combo.steps.Length == 3, weapon.name + " 3타 콤보 누락");
        for (int i = 0; i < combo.steps.Length; i++)
        {
            Require(
                combo.steps[i].attackPhases != null && combo.steps[i].attackPhases.Length == 1,
                weapon.name + " " + (i + 1) + "타 Phase 누락");
            AttackGeometryData geometry = combo.steps[i].attackPhases[0].geometry;
            RequireApproximately(
                expectedRangeMultipliers[i],
                geometry.SafeRangeMultiplier,
                weapon.name + " " + (i + 1) + "타 사거리 배율");
            RequireApproximately(
                expectedVfxMultipliers[i],
                geometry.SafeVfxScaleMultiplier,
                weapon.name + " " + (i + 1) + "타 VFX 배율");
        }
    }

    private static void ValidateComboImpacts(
        WeaponItemData weapon,
        float[] expectedDamageMultipliers,
        float[] expectedKnockbackMultipliers)
    {
        MeleeComboDefinition combo = weapon.GetMeleeComboDefinition();
        Require(combo != null && combo.steps != null && combo.steps.Length == 3, weapon.name + " 3타 콤보 누락");
        Require(expectedDamageMultipliers != null && expectedDamageMultipliers.Length == 3, weapon.name + " 피해 배율 검증값 오류");
        Require(expectedKnockbackMultipliers != null && expectedKnockbackMultipliers.Length == 3, weapon.name + " 넉백 배율 검증값 오류");

        float damageMultiplierSum = 0f;
        for (int i = 0; i < combo.steps.Length; i++)
        {
            AttackImpactData impact = combo.steps[i].attackPhases[0].impact;
            RequireApproximately(expectedDamageMultipliers[i], impact.SafeDamageMultiplier, weapon.name + " " + (i + 1) + "타 피해 배율");
            RequireApproximately(expectedKnockbackMultipliers[i], impact.SafeKnockbackMultiplier, weapon.name + " " + (i + 1) + "타 넉백 배율");
            damageMultiplierSum += impact.SafeDamageMultiplier;
        }

        float expectedSum = 0f;
        for (int i = 0; i < expectedDamageMultipliers.Length; i++)
            expectedSum += expectedDamageMultipliers[i];
        RequireApproximately(expectedSum, damageMultiplierSum, weapon.name + " 콤보 피해 배율 합계");
    }

    private static void ValidateGradeContracts()
    {
        RequireApproximately(0.02f, WeaponGradeStatRoller.GetMeleeBaseStarValue(WeaponGradeStatType.AttackSpeed), "공격속도 흰별 %p 값");
        RequireApproximately(1f, new WeaponGradeStarRoll { starType = WeaponGradeStarType.White }.ValueMultiplier, "흰별 배율");
        RequireApproximately(1.5f, new WeaponGradeStarRoll { starType = WeaponGradeStarType.Green }.ValueMultiplier, "초록별 배율");
        RequireApproximately(2f, new WeaponGradeStarRoll { starType = WeaponGradeStarType.Yellow }.ValueMultiplier, "노란별 배율");
        ValidateGradeContract(ItemGrade.Common, 0, 0, new[] { 0, 0, 0, 0, 0 }, 1f, 0f, 0f);
        ValidateGradeContract(ItemGrade.Uncommon, 4, 0, new[] { 1, 0, 0, 0, 0 }, 0.85f, 0.12f, 0.03f);
        ValidateGradeContract(ItemGrade.Rare, 8, 0, new[] { 1, 0, 0, 0, 0 }, 0.78f, 0.17f, 0.05f);
        ValidateGradeContract(ItemGrade.Epic, 12, 0, new[] { 2, 1, 0, 0, 0 }, 0.70f, 0.22f, 0.08f);
        ValidateGradeContract(ItemGrade.Legendary, 15, 0, new[] { 3, 1, 1, 0, 0 }, 0.60f, 0.27f, 0.13f);
        ValidateGradeContract(ItemGrade.Artifact, 20, 0, new[] { 3, 3, 2, 2, 2 }, 0.50f, 0.32f, 0.18f);
        ValidateGradeContract(ItemGrade.Mythic, 20, 0, new[] { 4, 3, 2, 2, 2 }, 0.40f, 0.36f, 0.24f);
        ValidateGradeContract(ItemGrade.Cursed, 15, 5, new[] { 7, 0, 0, 0, 0 }, 0.10f, 0.40f, 0.50f);
    }

    private static void ValidateGradeContract(
        ItemGrade grade,
        int positiveCount,
        int negativeCount,
        int[] guaranteedCounts,
        float whiteChance,
        float greenChance,
        float yellowChance)
    {
        Require(WeaponGradeStatRoller.GetMeleePositiveStarCount(grade) == positiveCount, grade + " 긍정 별 계약 오류");
        Require(WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade) == negativeCount, grade + " 붉은별 계약 오류");
        IReadOnlyList<WeaponGradeStatType> statTypes = WeaponGradeStatRoller.GetPlannedMeleeRollableStats();
        Require(guaranteedCounts != null && guaranteedCounts.Length == statTypes.Count, grade + " 기본 지급 계약 배열 오류");
        for (int i = 0; i < statTypes.Count; i++)
        {
            Require(
                WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarCount(grade, statTypes[i]) == guaranteedCounts[i],
                grade + " " + statTypes[i] + " 기본 지급 별 계약 오류");
        }
        WeaponGradeStatRoller.GetMeleePositiveStarChances(
            grade,
            out float actualWhite,
            out float actualGreen,
            out float actualYellow);
        RequireApproximately(whiteChance, actualWhite, grade + " 흰별 확률");
        RequireApproximately(greenChance, actualGreen, grade + " 초록별 확률");
        RequireApproximately(yellowChance, actualYellow, grade + " 노란별 확률");
        RequireApproximately(1f, actualWhite + actualGreen + actualYellow, grade + " 별 확률 합계");
    }

    private static WeaponItemData LoadWeapon(string path)
    {
        WeaponItemData weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(path);
        Require(weapon != null, "무기 에셋을 찾을 수 없습니다: " + path);
        Require(WeaponGradeStatRoller.IsMeleeWeapon(weapon), "정식 밀리 무기가 아닙니다: " + path);
        return weapon;
    }

    private static void ValidateConcentrationWeights()
    {
        RequireApproximately(1f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(9), "9번째 별 가중치");
        RequireApproximately(0.5f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(10), "10번째 별 가중치");
        RequireApproximately(0.25f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(11), "11번째 별 가중치");
        RequireApproximately(0.125f, WeaponGradeStatRoller.GetConcentrationWeightForResultingCount(12), "12번째 별 가중치");
    }

    private static void ValidateWeapon(WeaponItemData weapon, int baseSeed)
    {
        Array grades = Enum.GetValues(typeof(ItemGrade));
        Random.State previousState = Random.state;
        try
        {
            for (int gradeIndex = 0; gradeIndex < grades.Length; gradeIndex++)
            {
                ItemGrade grade = (ItemGrade)grades.GetValue(gradeIndex);
                Random.InitState(baseSeed + gradeIndex);
                ItemData item = new ItemData(weapon, 1, grade);
                ValidateRoll(item, grade);

                WeaponFinalStats finalStats = WeaponStatCalculator.Calculate(item);
                Require(finalStats.meleeAttackSpeedMultiplier <= WeaponGradeStatRoller.MaximumMeleeAttackSpeedMultiplier + 0.0001f, weapon.name + " " + grade + " 공격속도 상한 초과");
                Require(finalStats.range <= weapon.baseStats.range * WeaponGradeStatRoller.MaximumMeleeAttackRangeMultiplier + 0.0001f, weapon.name + " " + grade + " 사거리 상한 초과");
                Require(finalStats.critChance <= WeaponGradeStatRoller.MaximumMeleeCriticalChance + 0.0001f, weapon.name + " " + grade + " 치명타 확률 상한 초과");
                Require(finalStats.critDamageMultiplier <= WeaponGradeStatRoller.MaximumMeleeCriticalDamageMultiplier + 0.0001f, weapon.name + " " + grade + " 치명타 피해 상한 초과");
                MeleeSingleTargetDpsEstimate dps = MeleeSingleTargetDpsCalculator.Estimate(weapon, finalStats);
                Require(dps.IsValid && dps.Dps > 0f, weapon.name + " " + grade + " DPS 계산 실패");
                Require(dps.HitCount > 0 && dps.CycleDuration > 0f, weapon.name + " " + grade + " DPS 세부값 오류");

                string tooltip = SimpleItemTooltipBuilder.Build(item);
                Require(tooltip.Contains("단일 DPS"), weapon.name + " " + grade + " 단순 툴팁 DPS 행 누락");
            }
        }
        finally
        {
            Random.state = previousState;
        }
    }

    private static void ValidateRoll(ItemData item, ItemGrade grade)
    {
        WeaponItemData weaponData = item.baseData as WeaponItemData;
        Require(WeaponGradeStatRoller.HasFormalMeleeGradeRolls(
            weaponData,
            grade,
            item.weaponGradeStats,
            item.meleeStarDistributionProfile), grade + " 정식 별 계약 오류");
        int randomPositiveCount = WeaponGradeStatRoller.GetMeleePositiveStarCount(grade)
            - WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarTotal(grade);
        if (randomPositiveCount > 0)
            Require(item.meleeStarDistributionProfile != MeleeStarDistributionProfile.None, grade + " 성향 누락");
        else
            Require(item.meleeStarDistributionProfile == MeleeStarDistributionProfile.None, grade + " 랜덤 배분 별이 없는데 성향이 생성됨");

        int positiveCount = 0;
        int negativeCount = 0;
        int damagePositiveCount = 0;
        HashSet<WeaponGradeStatType> foundStats = new HashSet<WeaponGradeStatType>();
        IReadOnlyList<WeaponGradeStatType> expectedStats = WeaponGradeStatRoller.GetPlannedMeleeRollableStats();

        Require(item.weaponGradeStats != null, grade + " 별 데이터가 없습니다.");
        for (int rollIndex = 0; rollIndex < item.weaponGradeStats.Count; rollIndex++)
        {
            WeaponGradeStatRoll roll = item.weaponGradeStats[rollIndex];
            Require(roll != null, grade + " null 능력치 롤");
            Require(WeaponGradeStatRoller.IsMeleeStat(roll.statType), grade + " 밀리 외 능력치가 생성됨: " + roll.statType);
            Require(foundStats.Add(roll.statType), grade + " 중복 능력치 롤: " + roll.statType);
            positiveCount += Mathf.Max(0, roll.positiveStarCount);
            negativeCount += Mathf.Max(0, roll.negativeStarCount);
            if (roll.statType == WeaponGradeStatType.Damage)
                damagePositiveCount = Mathf.Max(0, roll.positiveStarCount);
            Require(
                roll.positiveStarCount >= WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarCount(grade, roll.statType),
                grade + " " + roll.statType + " 기본 지급 별 누락");

            float positiveCap = WeaponGradeStatRoller.GetMeleeMaximumPositiveGradeValue(weaponData, roll.statType);
            Require(float.IsPositiveInfinity(positiveCap) || roll.positiveTotalValue <= positiveCap + 0.0001f,
                grade + " 상한 초과 별이 데미지로 이동하지 않음: " + roll.statType);

            List<WeaponGradeStarRoll> stars = roll.GetDisplayStars();
            Require(stars.Count == roll.TotalStarCount, grade + " 표시 별 개수 불일치: " + roll.statType);
            for (int starIndex = 0; starIndex < stars.Count; starIndex++)
            {
                bool shouldBeRed = starIndex >= roll.positiveStarCount;
                Require(stars[starIndex] != null, grade + " null 별 데이터");
                Require(stars[starIndex].IsNegative == shouldBeRed, grade + " 긍정/붉은별 표시 순서 오류");
            }
        }

        Require(foundStats.Count == expectedStats.Count, grade + " 밀리 능력치 롤 개수 불일치");
        Require(positiveCount == WeaponGradeStatRoller.GetMeleePositiveStarCount(grade), grade + " 긍정 별 총합 불일치");
        Require(negativeCount == WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade), grade + " 붉은별 총합 불일치");
        Require(damagePositiveCount >= WeaponGradeStatRoller.GetGuaranteedMeleeDamageStarCount(grade), grade + " 보장 데미지 별 누락");
    }

    private static void ValidateProfileDistribution(WeaponItemData weapon)
    {
        const int sampleCount = 4000;
        int focusedCount = 0;
        int dualCoreCount = 0;
        int spreadCount = 0;
        float focusedMaxSum = 0f;
        float dualCoreMaxSum = 0f;
        float spreadMaxSum = 0f;

        Random.State previousState = Random.state;
        try
        {
            Random.InitState(20260714);
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                ItemData item = new ItemData(weapon, 1, ItemGrade.Rare);
                int maxStars = GetMaxRandomProfileStarCount(item);
                switch (item.meleeStarDistributionProfile)
                {
                    case MeleeStarDistributionProfile.Focused:
                        focusedCount++;
                        focusedMaxSum += maxStars;
                        break;
                    case MeleeStarDistributionProfile.DualCore:
                        dualCoreCount++;
                        dualCoreMaxSum += maxStars;
                        break;
                    case MeleeStarDistributionProfile.Spread:
                        spreadCount++;
                        spreadMaxSum += maxStars;
                        break;
                    default:
                        throw new InvalidOperationException("[MeleeGradeRollSimulatorValidation] 희귀 성향 누락");
                }
            }
        }
        finally
        {
            Random.state = previousState;
        }

        RequireRate(focusedCount, sampleCount, WeaponGradeStatRoller.FocusedProfileChance, "집중형 확률");
        RequireRate(dualCoreCount, sampleCount, WeaponGradeStatRoller.DualCoreProfileChance, "쌍축형 확률");
        RequireRate(spreadCount, sampleCount, WeaponGradeStatRoller.SpreadProfileChance, "분산형 확률");

        float focusedMaxAverage = focusedMaxSum / Mathf.Max(1, focusedCount);
        float dualCoreMaxAverage = dualCoreMaxSum / Mathf.Max(1, dualCoreCount);
        float spreadMaxAverage = spreadMaxSum / Mathf.Max(1, spreadCount);
        Require(focusedMaxAverage > dualCoreMaxAverage + 0.15f, "집중형 최대 집중도가 쌍축형과 구분되지 않음");
        Require(dualCoreMaxAverage > spreadMaxAverage + 0.15f, "쌍축형 최대 집중도가 분산형과 구분되지 않음");
        Require(focusedMaxAverage < 6.5f, "집중형 별 분포가 다시 극단적으로 몰림");
        Require(dualCoreMaxAverage < 5.5f, "쌍축형 별 분포가 다시 한 축으로 과도하게 몰림");
        Require(spreadMaxAverage < 4.8f, "분산형 별 분포가 다시 과도하게 몰림");
    }

    private static int GetMaxRandomProfileStarCount(ItemData item)
    {
        int maxStars = 0;
        if (item == null || item.weaponGradeStats == null)
            return maxStars;

        for (int i = 0; i < item.weaponGradeStats.Count; i++)
        {
            WeaponGradeStatRoll roll = item.weaponGradeStats[i];
            if (roll != null)
            {
                int guaranteed = WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarCount(item.grade, roll.statType);
                maxStars = Mathf.Max(maxStars, Mathf.Max(0, roll.positiveStarCount - guaranteed));
            }
        }

        return maxStars;
    }

    private static void ValidateLegacyMeleeMigrates(WeaponItemData weapon)
    {
        Random.State previousState = Random.state;
        try
        {
            Random.InitState(30303);
            ItemData item = new ItemData(weapon, 1, ItemGrade.Legendary);
            List<WeaponGradeStatRoll> legacyRolls = item.weaponGradeStatRolls;
            legacyRolls[0].positiveTotalValue = 0.02f;
            item.EnsureRuntimeState();

            Require(!ReferenceEquals(legacyRolls, item.weaponGradeStatRolls), "구 밀리 별 목록이 정식 계약으로 재생성되지 않음");
            Require(WeaponGradeStatRoller.HasFormalMeleeGradeRolls(
                weapon,
                item.grade,
                item.weaponGradeStatRolls,
                item.meleeStarDistributionProfile), "마이그레이션 결과가 정식 별 계약과 다름");
        }
        finally
        {
            Random.state = previousState;
        }
    }

    private static void RequireRate(int count, int total, float expectedRate, string label)
    {
        float actualRate = count / (float)Mathf.Max(1, total);
        Require(Mathf.Abs(actualRate - expectedRate) <= 0.05f, label + " 오차 초과: " + actualRate);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("[MeleeGradeRollSimulatorValidation] " + message);
    }

    private static void RequireApproximately(float expected, float actual, string label)
    {
        Require(Mathf.Approximately(expected, actual), label + " 불일치: expected=" + expected + ", actual=" + actual);
    }
}
