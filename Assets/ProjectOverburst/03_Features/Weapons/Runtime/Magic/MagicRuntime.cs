using UnityEngine;

public class MagicRuntime : MonoBehaviour, IWeaponRuntimeController // 마법 런타임
{
    private const float MinCastInterval = 0.2f; // 시전 하한
    private const float FixedSpreadStepDegrees = 5f; // 고정 분산

    [Header("References")]
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private PlayerAnimation playerAnimatorController;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private MagicLightningChainProjectile magicChainProjectilePrefab;

    [Header("Cast")]
    [SerializeField] private float fallbackCastHeight = 1.1f;
    [SerializeField] private float fallbackCastForwardOffset = 0.65f;
    [SerializeField] private float hipFireDamageMultiplier = 0.5f;
    [SerializeField] private float hipFireExplosionRadiusMultiplier = 0.5f;
    [SerializeField] private bool enableProjectileDebugLogs = true;

    private float nextCastTime; // 다음 시전
    private float lastCastInterval; // 최근 간격
    private float castBusyUntil; // busy 시간
    private ItemData cooldownWeaponItem; // 쿨타임 무기
    private string lastProjectileDebugSignature; // debug 중복
    private bool manualInputEnabled = true; // 수동 입력 권한

    public WeaponRuntimeKind RuntimeKind => WeaponRuntimeKind.Magic;
    public bool CanUseCurrentWeapon => CanUseCurrentMagicWeapon;
    public bool IsBusy => IsCastBusy;
    public bool IsReady => IsCastReady;
    public float CooldownRemaining => CastCooldownRemaining;
    public float CooldownProgress01 => CastCooldownProgress01;

    public float CastCooldownRemaining
    {
        get { return Mathf.Max(0f, nextCastTime - Time.time); }
    }

    public float CastCooldownDuration
    {
        get { return Mathf.Max(0f, lastCastInterval); }
    }

    public float CastCooldownProgress01
    {
        get
        {
            if (lastCastInterval <= 0f)
                return 1f;

            return Mathf.Clamp01(1f - CastCooldownRemaining / lastCastInterval);
        }
    }

    public bool IsCastReady
    {
        get { return CastCooldownRemaining <= 0f && CanUseCurrentMagicWeapon; }
    }

    public bool IsCastBusy
    {
        get { return CanUseCurrentMagicWeapon && Time.time < castBusyUntil; }
    }

    public void SetManualInputEnabled(bool enabledValue)
    {
        manualInputEnabled = enabledValue;
    }

    public bool CanUseCurrentMagicWeapon
    {
        get { return playerEquipment != null && playerEquipment.CanCurrentWeaponUseMagicCaster; }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();
        ResetCooldownIfWeaponChanged();

        if (!CanUseCurrentMagicWeapon)
            return;

        if (IsPlayerEvading())
            return; // 회피 중 시전 입력 차단

        WeaponFinalStats currentStats = playerEquipment.CurrentWeaponStats; // 최종 스탯
        LogProjectileDebugIfNeeded(currentStats);
        if (!ShouldCast(currentStats))
            return;

        TryCast(currentStats);
    }

    private void ResolveReferences()
    {
        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>(); // 장비

        if (playerController == null)
            playerController = GetComponent<PlayerMovement>(); // 이동

        if (playerAnimatorController == null)
            playerAnimatorController = GetComponent<PlayerAnimation>(); // 애니

        if (aimCamera == null)
            aimCamera = Camera.main; // 카메라
    }

    private bool ShouldCast(WeaponFinalStats currentStats)
    {
        if (!manualInputEnabled)
            return false;

        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        if (PlayerPickupInteractor.IsPrimaryAttackSuppressed)
            return false; // 월드 라벨 클릭 해제 전 발사 차단

        if (IsPlayerEvading())
            return false;

        // GOAL A2: 좌클릭 홀드 직접 읽기 대신 Gameplay Attack 유지를 사용한다. 연속 발사 의미를 유지.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null)
            return false;

        WeaponFireMode fireMode = playerEquipment != null
            ? playerEquipment.CurrentWeaponContext.Usage.fireMode
            : WeaponFireMode.None;
        switch (fireMode)
        {
            case WeaponFireMode.Cooldown:
                return facade.AttackHeld;

            default:
                return false;
        }
    }

    private bool IsPlayerEvading()
    {
        return playerController != null && playerController.IsEvading;
    }

    private void TryCast(WeaponFinalStats currentStats)
    {
        if (Time.time < nextCastTime)
            return;

        MonoBehaviour projectilePrefab = GetCurrentMagicProjectilePrefab(currentStats); // 발사체
        if (projectilePrefab == null)
            return;

        if (!TryGetCurrentAimLine(currentStats, out MagicAimLine aimLine))
            return;

        transform.rotation = Quaternion.LookRotation(aimLine.direction, Vector3.up); // 시전 방향

        bool isAimedCast = playerController != null && playerController.IsAiming; // 조준 시전
        SpawnCastVfx(currentStats, aimLine.origin, aimLine.direction);
        CastProjectiles(projectilePrefab, aimLine.origin, aimLine.direction, currentStats, isAimedCast);
        float actionDuration = PlayCastAnimation(currentStats); // 애니 시간
        castBusyUntil = Time.time + actionDuration; // busy 유지

        lastCastInterval = Mathf.Max(MinCastInterval, currentStats.attackInterval); // 쿨타임
        nextCastTime = Time.time + lastCastInterval; // 다음 시전
        cooldownWeaponItem = playerEquipment != null ? playerEquipment.CurrentWeaponItem : null; // 쿨타임 주인
    }

    public void CancelCurrentCastState()
    {
        castBusyUntil = 0f; // busy 해제
        nextCastTime = 0f; // 쿨타임 해제
        lastCastInterval = 0f; // 간격 초기화
        cooldownWeaponItem = null; // 주인 해제
    }

    public void CancelCurrentAction()
    {
        CancelCurrentCastState();
    }

    public WeaponRuntimeStatus GetRuntimeStatus()
    {
        return new WeaponRuntimeStatus(RuntimeKind, CanUseCurrentWeapon, IsBusy, IsReady, CooldownRemaining, CooldownProgress01);
    }

    private void ResetCooldownIfWeaponChanged()
    {
        ItemData currentWeaponItem = playerEquipment != null ? playerEquipment.CurrentWeaponItem : null; // 현재 무기

        if (cooldownWeaponItem == null || IsSameRuntimeItem(cooldownWeaponItem, currentWeaponItem))
            return;

        cooldownWeaponItem = currentWeaponItem; // 새 무기
        nextCastTime = 0f; // 쿨타임 초기화
        lastCastInterval = 0f; // 간격 초기화
        castBusyUntil = 0f; // busy 초기화
    }

    private bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }

    public bool TryGetCurrentAimLine(out MagicAimLine aimLine)
    {
        WeaponFinalStats currentStats = playerEquipment != null ? playerEquipment.CurrentWeaponStats : WeaponFinalStats.Empty; // 최종 스탯
        return TryGetCurrentAimLine(currentStats, out aimLine);
    }

    private bool TryGetCurrentAimLine(WeaponFinalStats currentStats, out MagicAimLine aimLine)
    {
        return MagicTargeting.TryGetDirectionalAimLine(
            transform,
            playerEquipment,
            aimCamera,
            currentStats,
            fallbackCastHeight,
            fallbackCastForwardOffset,
            out aimLine);
    }

    private void CastProjectiles(MonoBehaviour projectilePrefab, Vector3 castPosition, Vector3 castDirection, WeaponFinalStats currentStats, bool isAimedCast)
    {
        int projectileCount = Mathf.Max(1, currentStats.projectileCount); // 투사체 수

        for (int i = 0; i < projectileCount; i++)
        {
            Vector3 shotDirection = GetSpreadDirection(castDirection, projectileCount, i); // 분산 방향
            MagicProjectileSpawner.Spawn(CreateCastRequest(
                projectilePrefab,
                castPosition,
                shotDirection,
                currentStats,
                isAimedCast));
        }

        if (!RollDoubleShot(currentStats))
            return;

        for (int i = 0; i < projectileCount; i++)
        {
            Vector3 shotDirection = GetSpreadDirection(castDirection, projectileCount, i); // 추가 방향
            MagicProjectileSpawner.Spawn(CreateCastRequest(
                projectilePrefab,
                castPosition,
                shotDirection,
                currentStats,
                isAimedCast));
        }
    }

    private Vector3 GetSpreadDirection(Vector3 baseDirection, int projectileCount, int projectileIndex)
    {
        Vector3 direction = baseDirection.normalized; // 기준 방향
        float angle = GetSpreadAngle(projectileCount, projectileIndex); // 분산각
        return Quaternion.AngleAxis(angle, Vector3.up) * direction;
    }

    private float GetSpreadAngle(int projectileCount, int projectileIndex)
    {
        if (projectileCount <= 1)
            return 0f;

        float startAngle = -FixedSpreadStepDegrees * (projectileCount - 1) * 0.5f; // 좌측 시작
        return startAngle + FixedSpreadStepDegrees * projectileIndex;
    }

    private MagicCastRequest CreateCastRequest(MonoBehaviour projectilePrefab, Vector3 castPosition, Vector3 shotDirection, WeaponFinalStats currentStats, bool isAimedCast)
    {
        return new MagicCastRequest
        {
            projectilePrefab = projectilePrefab,
            origin = castPosition,
            direction = shotDirection,
            projectileScale = currentStats.projectileSize,
            isAimedCast = isAimedCast,
            appliesHipFirePenalty = !isAimedCast,
            projectileConfig = BuildProjectileConfig(currentStats, isAimedCast)
        };
    }

    private MagicProjectileConfig BuildProjectileConfig(WeaponFinalStats currentStats, bool isAimedCast)
    {
        bool isCritical = RollCritical(currentStats); // 치명타
        float damageMultiplier = 1f; // 피해 배율
        if (!isAimedCast)
            damageMultiplier *= Mathf.Clamp01(hipFireDamageMultiplier); // 비조준 패널티

        float baseDamage = currentStats.damage * damageMultiplier; // 기본 피해
        int damage = Mathf.Max(0, Mathf.RoundToInt(baseDamage * (isCritical ? Mathf.Max(1f, currentStats.critDamageMultiplier) : 1f))); // 최종 피해
        float explosionRadius = currentStats.explosionRadius; // 폭발 범위
        if (!isAimedCast)
            explosionRadius *= Mathf.Clamp01(hipFireExplosionRadiusMultiplier); // 비조준 범위

        return new MagicProjectileConfig
        {
            speed = currentStats.projectileSpeed,
            damage = damage,
            baseDamage = baseDamage,
            critChance = currentStats.critChance,
            critDamageMultiplier = currentStats.critDamageMultiplier,
            range = currentStats.range,
            explosionRadius = explosionRadius,
            source = gameObject,
            knockback = currentStats.knockback,
            isCritical = isCritical,
            dotDamageFlat = currentStats.dotDamageFlat,
            dotDamagePercent = currentStats.dotDamagePercent,
            dotDuration = currentStats.duration,
            hitHeal = currentStats.onHitHeal,
            lifeStealPercent = currentStats.lifeStealPercent,
            chainRange = currentStats.chainRange,
            maxChainDepth = currentStats.maxChainDepth,
            chainBranchCount = currentStats.chainBranchCount,
            chainDelay = currentStats.chainDelay,
            chainDamageFalloff = currentStats.chainDamageFalloff
        };
    }

    private void SpawnCastVfx(WeaponFinalStats currentStats, Vector3 castPosition, Vector3 castDirection)
    {
        MagicWeaponDefinition definition = playerEquipment != null
            ? playerEquipment.CurrentWeaponContext.Magic
            : null;
        GameObject fireVfxPrefab = definition != null ? definition.magic.fireVfxPrefab : null;
        if (fireVfxPrefab == null)
            return;

        Quaternion rotation = Quaternion.LookRotation(castDirection, Vector3.up); // VFX 방향
        VfxPrefabFactory.Spawn(fireVfxPrefab, castPosition, rotation);
    }

    private float PlayCastAnimation(WeaponFinalStats currentStats)
    {
        WeaponAnimationSettings animation = playerEquipment != null
            ? playerEquipment.CurrentWeaponContext.Animation
            : default;
        float animationSpeed = Mathf.Max(0.01f, animation.primaryAttackSpeed); // 애니 속도
        playerAnimatorController?.PlayWeaponFire(animation.primaryAttackClip, animationSpeed);

        float activePoseDuration = CalculateActionDuration(currentStats, animationSpeed); // 포즈 시간
        WeaponPose poseController = playerEquipment != null ? playerEquipment.CurrentWeaponPose : null; // 포즈 제어
        poseController?.BeginActivePose(activePoseDuration);
        return activePoseDuration;
    }

    private float CalculateActionDuration(WeaponFinalStats currentStats, float animationSpeed)
    {
        AnimationClip clip = playerEquipment != null
            ? playerEquipment.CurrentWeaponContext.Animation.primaryAttackClip
            : null;
        if (clip != null)
            return Mathf.Max(0.1f, clip.length / animationSpeed);

        return Mathf.Max(0.2f, currentStats.attackInterval);
    }

    private MonoBehaviour GetCurrentMagicProjectilePrefab(WeaponFinalStats currentStats)
    {
        MagicWeaponDefinition definition = playerEquipment != null
            ? playerEquipment.CurrentWeaponContext.Magic
            : null;
        if (definition != null && definition.magic.chainProjectilePrefab != null)
            return definition.magic.chainProjectilePrefab;

        return magicChainProjectilePrefab;
    }

    private bool RollDoubleShot(WeaponFinalStats currentStats)
    {
        float doubleShotChance = Mathf.Clamp(currentStats.doubleShotChance, 0f, 100f); // 확률 보정
        return doubleShotChance > 0f && Random.value * 100f < doubleShotChance;
    }

    private bool RollCritical(WeaponFinalStats currentStats)
    {
        float critChance = Mathf.Clamp(currentStats.critChance, 0f, 100f); // 확률 보정
        return critChance > 0f && Random.value * 100f < critChance;
    }

    private void LogProjectileDebugIfNeeded(WeaponFinalStats currentStats)
    {
        if (!enableProjectileDebugLogs || playerEquipment == null)
            return;

        ItemData weaponItem = playerEquipment.CurrentWeaponItem; // 현재 무기
        WeaponItemData weaponData = playerEquipment.CurrentWeaponData; // 무기 원본
        if (weaponItem == null || weaponData == null || weaponData.CombatFamily != WeaponCombatFamily.Magic)
            return;

        string signature = BuildProjectileDebugSignature(weaponItem, currentStats); // 디버그 키
        if (signature == lastProjectileDebugSignature)
            return;

        lastProjectileDebugSignature = signature;
        Debug.Log(BuildProjectileDebugMessage(weaponData, currentStats), this);
    }

    private string BuildProjectileDebugSignature(ItemData weaponItem, WeaponFinalStats currentStats)
    {
        return weaponItem.runtimeInstanceId + "|" + currentStats.projectileCount;
    }

    private string BuildProjectileDebugMessage(WeaponItemData weaponData, WeaponFinalStats currentStats)
    {
        return "[MagicProjectileDebug]"
            + " WeaponName=" + weaponData.itemName
            + " WeaponClass=" + weaponData.weaponClass
            + " BaseProjectileCount=" + (weaponData.GetMagicDefinition() != null ? weaponData.GetMagicDefinition().magic.projectileCount : 0)
            + " FinalProjectileCount=" + currentStats.projectileCount
            + " RuntimeControllerProjectileCount=" + Mathf.Max(1, currentStats.projectileCount);
    }
}

