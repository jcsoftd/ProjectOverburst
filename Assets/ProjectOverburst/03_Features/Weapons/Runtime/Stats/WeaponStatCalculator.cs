using UnityEngine;

public static class WeaponStatCalculator // 무기 스탯 계산
{
    private const float MinActionCooldown = 0.2f; // 액션 하한
    private const float MinReloadDuration = 0.2f; // 재장전 하한
    private const float MinRecoil = 0f; // 반동 최소
    private const float MaxRecoil = 100f; // 반동 최대

    public static WeaponFinalStats Calculate(ItemData item)
    {
        if (item == null)
            return WeaponFinalStats.Empty;

        item.EnsureRuntimeState(); // 런타임 보정
        return CalculateInternal(item.baseData as WeaponItemData, item);
    }

    public static WeaponFinalStats Calculate(WeaponItemData weaponData, ItemData item = null)
    {
        return CalculateInternal(weaponData, item);
    }

    public static WeaponFinalStats CalculateWeaponBase(ItemData item)
    {
        if (item == null)
            return WeaponFinalStats.Empty;

        item.EnsureRuntimeState(); // 런타임 보정
        return CalculateInternal(item.baseData as WeaponItemData, item);
    }

    private static WeaponFinalStats CalculateInternal(WeaponItemData weaponData, ItemData item)
    {
        if (weaponData == null)
            return WeaponFinalStats.Empty;

        WeaponFinalStats stats = CreateBaseStats(weaponData); // 원본 스탯
        float originalMeleeRange = stats.range;
        ApplyWeaponGradeStats(ref stats, item); // 등급 보정
        ProjectileRangeBehavior rangeBehavior = ResolveRangeBehavior(weaponData);
        ClampStats(ref stats, rangeBehavior); // 최종 보정
        ClampMeleeStatCaps(ref stats, weaponData, originalMeleeRange);
        UpdateMeleeRangeScale(ref stats, weaponData.CombatFamily, originalMeleeRange);

        return stats;
    }

    private static WeaponFinalStats CreateBaseStats(WeaponItemData weaponData)
    {
        WeaponBaseStats baseStats = weaponData.baseStats;
        WeaponCombatDefinition combatDefinition = weaponData.combatDefinition;
        WeaponAimSettings aim = combatDefinition != null ? combatDefinition.aim : default;
        MagicWeaponDefinition magicDefinition = weaponData.GetMagicDefinition();
        MagicWeaponSettings magic = magicDefinition != null ? magicDefinition.magic : default;
        MeleeWeaponDefinition meleeDefinition = weaponData.GetMeleeDefinition();

        return new WeaponFinalStats
        {
            damage = baseStats.damage,
            attackInterval = magic.actionInterval,
            meleeAttackSpeedMultiplier = meleeDefinition != null
                ? meleeDefinition.baseSettings.SafeAttackSpeedMultiplier
                : 1f,
            meleeAttackRangeScale = 1f,
            critChance = baseStats.criticalChance,
            critDamageMultiplier = baseStats.criticalDamageMultiplier,
            range = baseStats.range,
            maxTravelDistance = magic.maxTravelDistance,
            outOfRangeDamageMultiplier = magic.outOfRangeDamageMultiplier,
            projectileSpeed = magic.projectileSpeed,
            projectileSize = magic.projectileSize,
            projectileCount = magic.projectileCount,
            spreadAngle = magic.spreadAngle,
            pierceCount = magic.pierceCount,
            radius = magic.radius,
            explosionRadius = magic.explosionRadius,
            duration = magic.duration,
            knockback = baseStats.knockback,
            recoil = magic.recoil,
            recoilAmount = magic.recoilAmount,
            recoilPerShot = magic.recoilPerShot,
            recoilYawMin = magic.recoilYawMin,
            recoilYawMax = magic.recoilYawMax,
            recoilPitchMin = magic.recoilPitchMin,
            recoilPitchMax = magic.recoilPitchMax,
            maxRecoilAngle = magic.maxRecoilAngle,
            recoilRampShots = magic.recoilRampShots,
            recoilRecoverySpeed = magic.recoilRecoverySpeed,
            aimedRecoilMultiplier = magic.aimedRecoilMultiplier,
            hipFireRecoilMultiplier = magic.hipFireRecoilMultiplier,
            quickFireHoldTime = magic.quickFireHoldTime,
            quickFireMoveSpeedMultiplier = magic.quickFireMoveSpeedMultiplier,
            aimMoveSpeedMultiplier = aim.moveSpeedMultiplier,
            meleeSlashAngle = meleeDefinition != null ? meleeDefinition.baseSettings.slashAngle : 0f,
            magazineSize = magic.magazineSize,
            reloadDuration = magic.reloadDuration,
            chainRange = magic.chainRange,
            maxChainDepth = magic.maxChainDepth,
            chainBranchCount = magic.chainBranchCount,
            chainDelay = magic.chainDelay,
            chainDamageFalloff = magic.chainDamageFalloff
        };
    }

    private static void ApplyWeaponGradeStats(ref WeaponFinalStats stats, ItemData item)
    {
        if (item == null || !(item.baseData is WeaponItemData weaponData))
            return;

        item.EnsureWeaponGradeStatRolls(); // 별 보장

        if (item.weaponGradeStatRolls == null)
            return;

        bool useFormalMeleeStats = WeaponGradeStatRoller.IsMeleeWeapon(weaponData);

        for (int i = 0; i < item.weaponGradeStatRolls.Count; i++)
            ApplyWeaponGradeRoll(ref stats, item.weaponGradeStatRolls[i], useFormalMeleeStats);
    }

    private static void ApplyWeaponGradeRoll(
        ref WeaponFinalStats stats,
        WeaponGradeStatRoll roll,
        bool useFormalMeleeStats)
    {
        if (roll == null)
            return;

        if (useFormalMeleeStats)
        {
            ApplyMeleeGradeRoll(ref stats, roll);
            return;
        }

        float positive = roll.positiveTotalValue; // 긍정 합계
        float negative = roll.negativeTotalValue; // 부정 합계

        switch (roll.statType)
        {
            case WeaponGradeStatType.Damage:
                stats.damage += positive - negative;
                break;
            case WeaponGradeStatType.Rpm:
                float rpm = Mathf.Max(1f, ToRpm(stats.attackInterval) + positive - negative); // RPM 보정
                stats.attackInterval = 60f / rpm;
                break;
            case WeaponGradeStatType.MagazineSize:
                stats.magazineSize += Mathf.RoundToInt(positive - negative);
                break;
            case WeaponGradeStatType.ReloadDuration:
                stats.reloadDuration += negative - positive;
                break;
            case WeaponGradeStatType.Range:
                stats.range += positive - negative;
                break;
            case WeaponGradeStatType.Recoil:
                stats.recoil += negative - positive;
                break;
            case WeaponGradeStatType.RecoilRecovery:
                stats.recoilRecoverySpeed += positive - negative;
                break;
            case WeaponGradeStatType.CritChance:
                stats.critChance += positive - negative;
                break;
            case WeaponGradeStatType.CritDamage:
                stats.critDamageMultiplier += positive - negative;
                break;
            case WeaponGradeStatType.AttackSpeed:
                stats.meleeAttackSpeedMultiplier += positive - negative;
                break;
            case WeaponGradeStatType.AttackRange:
                stats.range += positive - negative;
                break;
        }
    }

    private static void ApplyMeleeGradeRoll(ref WeaponFinalStats stats, WeaponGradeStatRoll roll)
    {
        float delta = roll.positiveTotalValue - roll.negativeTotalValue;

        switch (roll.statType)
        {
            case WeaponGradeStatType.Damage:
                stats.damage *= 1f + delta;
                break;
            case WeaponGradeStatType.AttackSpeed:
                stats.meleeAttackSpeedMultiplier += delta;
                break;
            case WeaponGradeStatType.AttackRange:
                stats.range *= 1f + delta;
                break;
            case WeaponGradeStatType.CritChance:
                stats.critChance += delta;
                break;
            case WeaponGradeStatType.CritDamage:
                stats.critDamageMultiplier += delta;
                break;
        }
    }

    private static void ClampStats(ref WeaponFinalStats stats, ProjectileRangeBehavior rangeBehavior)
    {
        stats.damage = Mathf.Max(1f, stats.damage);
        stats.attackInterval = Mathf.Max(MinActionCooldown, stats.attackInterval);
        stats.meleeAttackSpeedMultiplier = Mathf.Max(0.01f, stats.meleeAttackSpeedMultiplier);
        stats.meleeAttackRangeScale = Mathf.Max(0.01f, stats.meleeAttackRangeScale);
        stats.critChance = Mathf.Clamp(stats.critChance, 0f, 100f);
        stats.critDamageMultiplier = Mathf.Max(1f, stats.critDamageMultiplier);
        stats.range = Mathf.Max(1f, stats.range);
        stats.maxTravelDistance = ResolveMaxTravelDistance(stats.range, stats.maxTravelDistance, rangeBehavior);
        stats.outOfRangeDamageMultiplier = Mathf.Clamp01(stats.outOfRangeDamageMultiplier);
        stats.projectileSpeed = Mathf.Max(0f, stats.projectileSpeed);
        stats.projectileSize = Mathf.Max(0.01f, stats.projectileSize);
        stats.projectileCount = Mathf.Max(1, stats.projectileCount);
        stats.pierceCount = Mathf.Max(0, stats.pierceCount);
        stats.radius = Mathf.Max(0f, stats.radius);
        stats.explosionRadius = Mathf.Max(0f, stats.explosionRadius);
        stats.duration = Mathf.Max(0f, stats.duration);
        stats.knockback = Mathf.Max(0f, stats.knockback);
        stats.recoil = Mathf.Clamp(stats.recoil, MinRecoil, MaxRecoil);
        CalculateRecoilAngles(stats.recoil, out stats.recoilPerShot, out stats.maxRecoilAngle);
        stats.recoilAmount = stats.recoil;
        if (stats.recoilYawMin > stats.recoilYawMax)
        {
            float yaw = stats.recoilYawMin; // swap 임시
            stats.recoilYawMin = stats.recoilYawMax;
            stats.recoilYawMax = yaw;
        }
        if (stats.recoilPitchMin > stats.recoilPitchMax)
        {
            float pitch = stats.recoilPitchMin; // swap 임시
            stats.recoilPitchMin = stats.recoilPitchMax;
            stats.recoilPitchMax = pitch;
        }
        stats.maxRecoilAngle = Mathf.Max(0f, stats.maxRecoilAngle);
        stats.recoilRampShots = Mathf.Max(1, stats.recoilRampShots);
        stats.recoilRecoverySpeed = Mathf.Max(0.1f, stats.recoilRecoverySpeed);
        stats.aimedRecoilMultiplier = Mathf.Max(0f, stats.aimedRecoilMultiplier);
        stats.hipFireRecoilMultiplier = Mathf.Max(0f, stats.hipFireRecoilMultiplier);
        stats.quickFireHoldTime = Mathf.Max(0f, stats.quickFireHoldTime);
        stats.quickFireMoveSpeedMultiplier = Mathf.Max(0f, stats.quickFireMoveSpeedMultiplier);
        stats.aimMoveSpeedMultiplier = Mathf.Max(0f, stats.aimMoveSpeedMultiplier);
        stats.meleeSlashAngle = Mathf.Clamp(stats.meleeSlashAngle, 1f, 180f);
        stats.magazineSize = Mathf.Max(1, stats.magazineSize);
        stats.reloadDuration = Mathf.Max(MinReloadDuration, stats.reloadDuration);
        stats.dotDamageFlat = Mathf.Max(0f, stats.dotDamageFlat);
        stats.dotDamagePercent = Mathf.Max(0f, stats.dotDamagePercent);
        stats.onHitHeal = Mathf.Max(0f, stats.onHitHeal);
        stats.lifeStealPercent = Mathf.Max(0f, stats.lifeStealPercent);
        stats.doubleShotChance = Mathf.Clamp(stats.doubleShotChance, 0f, 100f);
        stats.onKillExplosion = Mathf.Max(0f, stats.onKillExplosion);
        stats.onKillHeal = Mathf.Max(0f, stats.onKillHeal);
        stats.chainRange = Mathf.Max(0.1f, stats.chainRange);
        stats.maxChainDepth = Mathf.Max(0, stats.maxChainDepth);
        stats.chainBranchCount = Mathf.Max(1, stats.chainBranchCount);
        stats.chainDelay = Mathf.Max(0f, stats.chainDelay);
        stats.chainDamageFalloff = Mathf.Clamp01(stats.chainDamageFalloff);
    }

    private static void ClampMeleeStatCaps(
        ref WeaponFinalStats stats,
        WeaponItemData weaponData,
        float originalMeleeRange)
    {
        if (weaponData == null || weaponData.CombatFamily != WeaponCombatFamily.Melee)
            return;

        stats.meleeAttackSpeedMultiplier = Mathf.Min(
            stats.meleeAttackSpeedMultiplier,
            WeaponGradeStatRoller.MaximumMeleeAttackSpeedMultiplier);
        stats.range = Mathf.Min(
            stats.range,
            Mathf.Max(0.01f, originalMeleeRange) * WeaponGradeStatRoller.MaximumMeleeAttackRangeMultiplier);
        stats.critChance = Mathf.Min(stats.critChance, WeaponGradeStatRoller.MaximumMeleeCriticalChance);
        stats.critDamageMultiplier = Mathf.Min(
            stats.critDamageMultiplier,
            WeaponGradeStatRoller.MaximumMeleeCriticalDamageMultiplier);
    }

    private static ProjectileRangeBehavior ResolveRangeBehavior(WeaponItemData weaponData)
    {
        MagicWeaponDefinition definition = weaponData != null ? weaponData.GetMagicDefinition() : null;
        return definition != null
            ? definition.magic.rangeBehavior
            : ProjectileRangeBehavior.DestroyAtRange;
    }

    private static void UpdateMeleeRangeScale(
        ref WeaponFinalStats stats,
        WeaponCombatFamily combatFamily,
        float originalMeleeRange)
    {
        stats.meleeAttackRangeScale = combatFamily == WeaponCombatFamily.Melee
            ? stats.range / Mathf.Max(0.1f, originalMeleeRange)
            : 1f;
    }

    private static void CalculateRecoilAngles(float recoil, out float perShotAngle, out float maxRecoilAngle)
    {
        float t = Mathf.Clamp01(recoil / MaxRecoil); // 반동 비율
        float curved = t * t; // 곡선 보정
        perShotAngle = Mathf.Lerp(0.3f, 5f, curved);
        maxRecoilAngle = Mathf.Lerp(2f, 18f, curved);
    }

    private static float ResolveMaxTravelDistance(float range, float maxTravelDistance, ProjectileRangeBehavior rangeBehavior)
    {
        if (range <= 0f)
            return Mathf.Max(0f, maxTravelDistance);

        if (maxTravelDistance <= 0f)
        {
            if (rangeBehavior == ProjectileRangeBehavior.ContinueWithDamageFalloff)
                return range * 2.5f;

            return range;
        }

        return Mathf.Max(range, maxTravelDistance);
    }

    private static float ToRpm(float attackInterval)
    {
        if (attackInterval <= 0f)
            return 0f;

        return 60f / attackInterval;
    }
}

