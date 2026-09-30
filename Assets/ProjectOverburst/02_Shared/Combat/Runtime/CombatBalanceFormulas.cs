using UnityEngine;

// 실제 전투와 밸런스 분석 도구(Assets/Editor/Testers/Balance)가 함께 쓰는 순수 계산식.
// 식을 새로 만든 것이 아니라 아래 호출 지점에 있던 식을 그대로 옮겼다. 호출 지점은 이 함수만 부른다.
// CombatHealth · PlayerEquipment · PlayerProgression · MeleeRuntime · MeleeDamageResolver · EnemyRank
// · ElementDischargeBatch(지연 경로) · UpperElementCombatUtility
// 에너지·방출(OverburstElementEnergy)과 상태 틱(ElementalStatusController)의 호출 전환은 두 파일을 수정 중인
// 다른 작업이 끝난 뒤 이어서 한다. 그 전까지 이 두 묶음은 분석 도구만 쓰며, Play 측정으로 실제 값과 대조한다.
public static class CombatBalanceFormulas
{
    public const float CombatStanceCritChanceBonus = 10f;      // MeleeRuntime 전투 자세 치명 보너스(%p)
    public const float ParryStunDamageMultiplier = 1.4f;       // CombatHealth 패링 기절 중 받는 피해
    public const float PlayerArmorMultiplierFloor = .20f;      // CombatHealth 방어 감소 최대 80%
    public const float PlayerIncomingDamageFloor = .10f;       // CombatHealth 최종 피해 감소 최대 90%
    public const float LightningHopFalloff = .8f;              // ElementDischargeBatch 번개 다음 홉 배율
    public const int LightningMaxHopLimit = 7;

    // ── 플레이어 무기 최종 수치: PlayerEquipment.RefreshCurrentWeaponStats
    public static WeaponFinalStats ComposePlayerWeaponStats(WeaponFinalStats stats, GearStatTotals gear, int playerLevel,
        float runAttackSpeed)
    {
        stats.damage = OverburstCombatBalance.RoundStat((stats.damage + gear.Attack) * OverburstGrowthRules.PlayerAttackFactor(playerLevel));
        stats.critChance = Mathf.Min(65f, stats.critChance + gear.CriticalChance);
        stats.meleeAttackSpeedMultiplier = Mathf.Min(WeaponGradeStatRoller.MaximumMeleeAttackSpeedMultiplier,
            stats.meleeAttackSpeedMultiplier + gear.AttackSpeed / 100f);
        stats.critDamageMultiplier = Mathf.Min(OverburstCombatBalance.FinalCriticalDamage, stats.critDamageMultiplier + gear.CriticalDamage / 100f);
        stats.meleeAttackSpeedMultiplier = Mathf.Min(WeaponGradeStatRoller.MaximumMeleeAttackSpeedMultiplier,
            stats.meleeAttackSpeedMultiplier + runAttackSpeed);
        if (stats.attackInterval > 0f) stats.attackInterval /= 1f + runAttackSpeed;
        return stats;
    }

    // ── 플레이어 방어·체력 보너스: PlayerProgression.Armor / RefreshStats
    public static float PlayerArmor(int level, GearStatTotals gear, float runArmor)
        => OverburstGrowthRules.PlayerArmorBonus(level) + gear.Armor + runArmor;

    public static float PlayerPermanentHealthBonus(int level, GearStatTotals gear)
        => OverburstGrowthRules.PlayerHealthBonus(level) + gear.MaxHealth;

    // ── 적에게 주는 플레이어 피해 보정: CombatHealth.ApplyProgressionDamageModifiers(플레이어 → 적)
    public static float ApplyPlayerOutgoing(float damage, GearStatTotals stats, bool hasTargetRank, EnemyGradeType targetGrade,
        PlayerAttackKind kind, float runAttack, float runElemental)
    {
        float bonus = hasTargetRank ? stats.TargetDamage(targetGrade) : 0f;
        if ((kind & PlayerAttackKind.Weak) != 0) bonus += stats.WeakDamage;
        if ((kind & PlayerAttackKind.Heavy) != 0) bonus += stats.HeavyDamage;
        if ((kind & PlayerAttackKind.Elemental) != 0) bonus += stats.ElementalDamage;
        damage *= Mathf.Max(.1f, 1f + bonus / 100f);
        damage *= 1f + runAttack;
        if ((kind & PlayerAttackKind.Elemental) != 0)
            damage *= 1f + runElemental;
        return damage;
    }

    // ── 플레이어가 받는 피해: CombatHealth.ApplyProgressionDamageModifiers(적 → 플레이어)와 최종 하한
    public static bool UsesLegacyEnemyDamageGrowth(EnemyAbilityDefinition ability)
        => ability == null || !ability.UsesLevelDamageBudget;

    public static float PlayerArmorMultiplier(float armor)
        => Mathf.Max(PlayerArmorMultiplierFloor, 100f / (100f + Mathf.Max(0f, armor)));

    // ── 근접 공격 배율과 치명: MeleeRuntime.BeginAttack / GetActiveCritChance, MeleeDamageResolver.Apply
    public static float AttackDamageMultiplier(WeaponItemData weapon, MeleeHeavyAttackDefinition heavy, bool isHeavy, bool hasEnergy)
    {
        float multiplier = isHeavy ? heavy.GetDamageMultiplier(hasEnergy) : 1f;
        if (weapon != null && weapon.weaponClass == WeaponClass.Greatsword)
            multiplier = isHeavy ? OverburstCombatBalance.EmptyHeavyDamage : OverburstCombatBalance.GreatswordWeakDamage;
        return multiplier;
    }

    public static float EffectiveCriticalChance(float statCritChance, bool combatStance)
        => Mathf.Min(OverburstCombatBalance.FinalCriticalChance, statCritChance + (combatStance ? CombatStanceCritChanceBonus : 0f));

    public static int RoundedHitDamage(float damage, bool isCritical, float criticalDamageMultiplier)
    {
        damage = Mathf.Max(0f, damage);
        if (isCritical)
            damage *= Mathf.Max(1f, criticalDamageMultiplier);
        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    // ── 원소 에너지 충전: OverburstElementEnergy.RecordConfirmedHit (적중 Phase당 1회)
    public static float PhaseEnergyGain(OverburstElementTuning tuning, bool isCritical, float energyGainBonus)
    {
        float baseGain = isCritical ? Mathf.Max(1f, tuning.maximumEnergy) * Mathf.Clamp01(tuning.criticalEnergyFraction)
            : Mathf.Max(0f, tuning.energyPerAttack);
        return baseGain * (1f + energyGainBonus);
    }

    // ── 강공 방출: OverburstElementDischarge
    public static float DischargeEnergyCoefficient(OverburstElementTuning tuning, float normalizedEnergy, float elementBonus,
        float energyDischargeBonus)
        => Mathf.Max(0f, tuning.dischargeDamageAtFullEnergy) * normalizedEnergy * (1f + elementBonus + energyDischargeBonus);

    public static float HeavyFirstBlastDamage(float attackDamage, float normalizedEnergy, float energyCoefficient, float dischargePower)
        => attackDamage * Mathf.Lerp(OverburstCombatBalance.EmptyHeavyDamage, OverburstCombatBalance.FullHeavyDamage, normalizedEnergy)
            * (1f + energyCoefficient) + dischargePower * normalizedEnergy;

    public static float DischargeRadius(OverburstElementTuning tuning, float normalizedEnergy)
        => Mathf.Lerp(Mathf.Max(0f, tuning.minimumRadius), Mathf.Max(0f, tuning.maximumRadius), normalizedEnergy);

    public static int DischargeEnergyChainBonus(float normalizedEnergy)
        => normalizedEnergy >= .99999f ? 2 : normalizedEnergy >= .5f ? 1 : 0;

    public static float IceShatterDamage(float firstBlastDamage, float shatterCoefficient) => firstBlastDamage * shatterCoefficient;

    public static float LightTripleHitScale(OverburstElementTuning tuning, int hit, int radianceStacks, float overcharge)
        => hit == 0
            ? tuning.SafeLightTripleHit1Base + tuning.SafeLightTripleHit1PerStack * radianceStacks / (float)tuning.SafeLightRadianceMaxStacks
            : hit == 1 ? tuning.SafeLightTripleHit2Base + tuning.SafeLightTripleHit2PerOvercharge * overcharge
            : tuning.SafeLightTripleHit3Scale;

    // ── 적 상태 틱: ElementalStatusController.AdvanceScheduledStates
    public static float StatusTickDamage(OverburstElementTuning tuning, WeaponElement element, float ownerDirectDamage, int stacks)
        => ownerDirectDamage * stacks * tuning.TickCoefficient(element);

    public static float ShockStaggerGradeFactor(EnemyGradeType grade)
        => grade == EnemyGradeType.Boss ? 0f : grade == EnemyGradeType.GreaterElite ? .25f : grade == EnemyGradeType.Elite ? .5f : 1f;

    public static float ShockStaggerSeconds(OverburstElementTuning tuning, int stacks, EnemyGradeType grade)
        => (tuning.shockStaggerBase + tuning.shockStaggerPerStack * (stacks - 1)) * ShockStaggerGradeFactor(grade);

    // ── 강공 파생 연쇄: ElementDischargeBatch.AdvanceDelayed (제품 경로), DarkGatherBurstScheduler.Burst
    public static float FireChainDamage(float blastDamage, int stack) => blastDamage * (.1f + .1f * stack);

    public static float FireChainRadius(int stack) => 1 + .2f * (stack - 1);

    public static float LightningLinkRadius(float normalizedEnergy) => Mathf.Lerp(2.5f, 4f, normalizedEnergy);

    public static int LightningMaxHops(int originStacks, float normalizedEnergy)
        => Mathf.Min(LightningMaxHopLimit, originStacks + DischargeEnergyChainBonus(normalizedEnergy));

    public static float LightningHopDamage(OverburstElementTuning tuning, float blastDamage, int originStacks, int hop)
        => blastDamage * tuning.LightningChainFraction(originStacks) * Mathf.Pow(LightningHopFalloff, hop);

    // ── 빛 광휘 공속: UpperElementCombatUtility.ApplyRadianceAttackSpeed
    public static WeaponFinalStats ApplyRadianceAttackSpeed(WeaponFinalStats stats, OverburstElementTuning tuning, int radianceStacks)
    {
        if (radianceStacks <= 0) return stats;
        float multiplier = 1f + tuning.SafeLightRadianceAttackSpeedPerStack * radianceStacks;
        float boosted = Mathf.Min(tuning.SafeLightFinalAttackSpeedCap, stats.meleeAttackSpeedMultiplier * multiplier);
        stats.meleeAttackSpeedMultiplier = Mathf.Max(stats.meleeAttackSpeedMultiplier, boosted);
        return stats;
    }

    // ── 몬스터 체력: EnemyRank.ApplyLevelToHealth (기준 곡선을 쓰는 종)
    public static float EnemyReferenceHealth(float referenceHealthCoefficient, int level)
        => OverburstCombatBalance.RoundStat(referenceHealthCoefficient * OverburstCombatBalance.ReferenceExpectedHit(level));
}
