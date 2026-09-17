using System;
using UnityEngine;

[Serializable]
public struct MagicWeaponSettings
{
    [Header("오브 기본공격 보류 데이터")]
    [Min(0.01f)] public float actionInterval;

    [Header("투사체")]
    public float maxTravelDistance;
    public ProjectileRangeBehavior rangeBehavior;
    [Range(0f, 1f)] public float outOfRangeDamageMultiplier;
    public float projectileSpeed;
    public float projectileSize;
    public int projectileCount;
    public float spreadAngle;
    public int pierceCount;
    public float radius;
    public float explosionRadius;
    public float duration;

    [Header("반동 및 탄약")]
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
    public int magazineSize;
    public float reloadDuration;

    [Header("연쇄")]
    public MagicLightningChainProjectile chainProjectilePrefab;
    public GameObject fireVfxPrefab;
    public float chainRange;
    public int maxChainDepth;
    public int chainBranchCount;
    public float chainDelay;
    [Range(0f, 1f)] public float chainDamageFalloff;
}
