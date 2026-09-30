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

// 무기 툴팁·장착 비교의 '단일 DPS': 약공 콤보를 끊지 않고 계속 이어 칠 때 한 순환의 기대 피해 ÷ 순환 시간.
// 피해는 MeleeRuntime과 같은 약공 배율(CombatBalanceFormulas.AttackDamageMultiplier, 대검 0.35)·치명 상한·반올림을,
// 시간은 MeleeRuntime과 같은 재생 가속(MeleePlaybackAcceleration)과 이어 치기 진입 진행률을 쓴다(2026-10-01 수정).
// 무기만의 비교값이라 장비 옵션·전투 자세 치명·원소 에너지·강공은 넣지 않는다.
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
        float criticalChance01 = Mathf.Clamp01(CombatBalanceFormulas.EffectiveCriticalChance(stats.critChance, false) / 100f);
        float weakDamageMultiplier = CombatBalanceFormulas.AttackDamageMultiplier(weaponData, null, false, false);
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
            // 순환 중에는 모든 타가 이어 치기 진입점에서 시작하고, 다음 입력 창이 열리면 다음 타로 넘어간다.
            float entry = Mathf.Clamp(step.continuationStartNormalizedTime, 0f, 0.95f);
            float chainStart = Mathf.Clamp(step.comboInputWindow.SafeStart, 0.01f, 1f);
            MeleePlaybackAcceleration acceleration = step.playbackAcceleration;
            cycleDuration += fullStepDuration
                * (acceleration.ToElapsed(Mathf.Max(chainStart, entry)) - acceleration.ToElapsed(entry));

            AttackPhaseData[] phases = step.attackPhases;
            if (phases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                float start = phases[phaseIndex].SafeStart;
                if (start < entry || start >= chainStart)
                    continue; // 진입 전에 지나간 판정, 다음 타 전환 뒤 시작하는 판정 제외

                float phaseDamage = Mathf.Max(0f, stats.damage)
                    * phases[phaseIndex].impact.SafeDamageMultiplier
                    * weakDamageMultiplier;
                int normalDamage = CombatBalanceFormulas.RoundedHitDamage(phaseDamage, false, stats.critDamageMultiplier);
                int criticalDamage = CombatBalanceFormulas.RoundedHitDamage(phaseDamage, true, stats.critDamageMultiplier);
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
