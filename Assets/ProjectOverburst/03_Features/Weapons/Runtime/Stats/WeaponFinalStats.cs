using System;

[Serializable]
public struct WeaponFinalStats
{
    public float damage;
    public float attackInterval; // 오브 계열 기본공격 보류값, 밀리에서는 미사용
    public float meleeAttackSpeedMultiplier;
    public float meleeAttackRangeScale;
    public float critChance;
    public float critDamageMultiplier;
    public float range;
    public float maxTravelDistance;
    public float outOfRangeDamageMultiplier;
    public float projectileSpeed;
    public float projectileSize;
    public int projectileCount;
    public float spreadAngle;
    public int pierceCount;
    public float radius;
    public float explosionRadius;
    public float duration;
    public float knockback;
    public float recoil;
    public float recoilAmount;
    public float recoilPerShot;
    public float recoilYawMin;
    public float recoilYawMax;
    public float recoilPitchMin;
    public float recoilPitchMax;
    public float maxRecoilAngle;
    public int recoilRampShots;
    public float recoilRecoverySpeed;
    public float aimedRecoilMultiplier;
    public float hipFireRecoilMultiplier;
    public float quickFireHoldTime;
    public float quickFireMoveSpeedMultiplier;
    public float aimMoveSpeedMultiplier;
    public float meleeSlashAngle;
    public int magazineSize;
    public float reloadDuration;
    public float dotDamageFlat;
    public float dotDamagePercent;
    public float onHitHeal;
    public float lifeStealPercent;
    public float doubleShotChance;
    public float onKillExplosion;
    public float onKillHeal;
    public float chainRange;
    public int maxChainDepth;
    public int chainBranchCount;
    public float chainDelay;
    public float chainDamageFalloff;

    public static WeaponFinalStats Empty => default;
}
