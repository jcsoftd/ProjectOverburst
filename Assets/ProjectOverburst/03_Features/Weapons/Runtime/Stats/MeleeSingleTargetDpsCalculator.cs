using UnityEngine;

public readonly struct MeleeSingleTargetDpsEstimate
{
    public readonly bool IsValid;
    public readonly float Dps;
    public readonly float ExpectedCycleDamage;
    public readonly float CycleDuration;
    public readonly int HitCount;

    public MeleeSingleTargetDpsEstimate(
        bool isValid,
        float dps,
        float expectedCycleDamage,
        float cycleDuration,
        int hitCount)
    {
        IsValid = isValid;
        Dps = dps;
        ExpectedCycleDamage = expectedCycleDamage;
        CycleDuration = cycleDuration;
        HitCount = hitCount;
    }
}

public static class MeleeSingleTargetDpsCalculator
{
    private const float MinAttackDuration = 0.2f;

    public static MeleeSingleTargetDpsEstimate Estimate(
        WeaponItemData weaponData,
        WeaponFinalStats stats)
    {
        MeleeComboDefinition combo = weaponData != null
            ? weaponData.GetMeleeComboDefinition()
            : null;
        if (combo == null || !combo.HasSteps)
            return default;

        float expectedCycleDamage = 0f;
        float cycleDuration = 0f;
        int hitCount = 0;
        float criticalChance01 = Mathf.Clamp01(stats.critChance / 100f);
        float criticalDamageMultiplier = Mathf.Max(1f, stats.critDamageMultiplier);
        MeleeWeaponDefinition melee = weaponData.GetMeleeDefinition();
        float baseline = melee != null
            ? melee.baseSettings.SafeAnimationPlaybackBaseline
            : MeleeAttackSpeedPolicy.BaselineAnimationSpeedMultiplier;
        float attackSpeedMultiplier = MeleeAttackSpeedPolicy.ToPlaybackMultiplier(
            Mathf.Max(0.01f, stats.meleeAttackSpeedMultiplier), baseline);
        float comboBaseSpeed = Mathf.Max(0.01f, combo.baseAnimationSpeed);

        for (int stepIndex = 0; stepIndex < combo.StepCount; stepIndex++)
        {
            MeleeComboStepData step = combo.GetStep(stepIndex);
            AnimationClip clip = ResolveAnimationClip(weaponData, step);
            if (clip == null)
                continue;

            float stepSpeed = comboBaseSpeed
                * attackSpeedMultiplier
                * Mathf.Max(0.01f, step.animationSpeedMultiplier);
            float fullStepDuration = Mathf.Max(MinAttackDuration, clip.length / stepSpeed);
            float chainStart = Mathf.Clamp(step.comboInputWindow.startNormalizedTime, 0.01f, 1f);
            cycleDuration += fullStepDuration * chainStart;

            AttackPhaseData[] phases = step.attackPhases;
            if (phases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                if (phases[phaseIndex].SafeStart >= chainStart)
                    continue; // 다음 타 전환 뒤 시작하는 판정 제외

                float phaseDamage = Mathf.Max(0f, stats.damage)
                    * phases[phaseIndex].impact.SafeDamageMultiplier;
                int normalDamage = Mathf.Max(1, Mathf.RoundToInt(phaseDamage));
                int criticalDamage = Mathf.Max(1, Mathf.RoundToInt(phaseDamage * criticalDamageMultiplier));
                expectedCycleDamage += Mathf.Lerp(normalDamage, criticalDamage, criticalChance01);
                hitCount++;
            }
        }

        if (cycleDuration <= 0f || hitCount <= 0)
            return default;

        return new MeleeSingleTargetDpsEstimate(
            true,
            expectedCycleDamage / cycleDuration,
            expectedCycleDamage,
            cycleDuration,
            hitCount);
    }

    private static AnimationClip ResolveAnimationClip(
        WeaponItemData weaponData,
        MeleeComboStepData step)
    {
        if (step.animationClip != null)
            return step.animationClip;

        return weaponData != null && weaponData.combatDefinition != null
            ? weaponData.combatDefinition.animation.primaryAttackClip
            : null;
    }
}
