using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CombatHealth : MonoBehaviour, IDamageable // 체력 처리
{
    [Header("Health")]
    [SerializeField] private float maxHp = 100f;
    [SerializeField] private float currentHp = 100f;
    [SerializeField] private bool resetOnEnable = true;
    [SerializeField] private bool destroyOnDeath;
    [SerializeField] private bool showDamageNumbers = true;

    [Header("Combat Gates")]
    [SerializeField] private bool enableSwordParry;

    private Coroutine activeDamageOverTimeRoutine; // DoT 루틴
    private readonly HashSet<Behaviour> damageDeathPreventionOwners = new HashSet<Behaviour>();

    // Runtime leases: inactive or destroyed owners cannot leave protection behind.
    public bool IsDeathFromDamagePrevented
    {
        get
        {
            foreach (var owner in damageDeathPreventionOwners)
                if (owner != null && owner.isActiveAndEnabled) return true;
            return false;
        }
    }

    public void SetDamageDeathPrevention(Behaviour owner, bool enabled)
    {
        if (owner == null) return;
        if (enabled) damageDeathPreventionOwners.Add(owner);
        else damageDeathPreventionOwners.Remove(owner);
    }

    public event Action<CombatHealth, DamageInfo> OnDamaged;
    public event Action<CombatHealth, float, float> OnHealthChanged;
    public event Action<CombatHealth, DamageInfo> OnDead;
    public event Action<CombatHealth> OnReset;

    public float MaxHp { get { return maxHp; } }
    public float CurrentHp { get { return currentHp; } }
    public float NormalizedHp { get { return maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f; } }
    public bool IsDead { get; private set; }

    private void Awake()
    {
        currentHp = Mathf.Clamp(currentHp, 0f, maxHp); // 초기 HP 범위 보정
        IsDead = currentHp <= 0f; // 시작 사망 상태 계산
    }

    private void OnEnable()
    {
        if (resetOnEnable)
            ResetHealth(); // 재사용 대상 HP 초기화
        else
            RaiseHealthChanged(); // 현재 HP UI 갱신
    }

    public void TakeDamage(DamageInfo info)
    {
        if (IsDead)
            return; // 사망 후 중복 피해 차단

        float damage = Mathf.Max(0f, info.damage); // 음수 피해 방지
        if (damage <= 0f)
            return; // 0 피해 무시

        if (CombatTeamUtility.IsFriendlyPlayerActorDamage(this, info))
            return; // 파티원 friendly fire 차단

        if (TryCancelDamageByEvade(info))
            return; // 회피 무적

        ApplyAimDamageModifier(ref info, ref damage); // 조준/자세 피해 보정
        ApplyEnemyDefenseModifier(ref info, ref damage); // 몬스터 방패 방어
        ApplyPlayerDamageReductionDebug(ref info, ref damage);
        FlaskCombatModifiers.Incoming(this, ref info, ref damage);
        float hpBeforeDamage = currentHp; // 실제 감소량 계산
        currentHp = Mathf.Max(IsDeathFromDamagePrevented ? Mathf.Min(1f, currentHp) : 0f, currentHp - damage); // 시험 보호 중 최소 생존 HP
        float actualDamage = Mathf.Max(0f, hpBeforeDamage - currentHp);
        FlaskCombatModifiers.ConfirmedHit(this, info, actualDamage);
        OverburstElementCombat.ReportConfirmedHit(this, info, actualDamage); // 적중 에너지·독립 상태 축적
        ApplyKnockback(info); // 넉백 적용

        if (actualDamage > 0f
            && CombatTeamUtility.IsPlayerActorHealth(this)
            && CanOpenPlayerCombatMode())
        {
            PlayerCombatModeController.EnterSharedCombatMode(PlayerCombatModeReason.Damaged);
            PlayCombatDamagedHitAnimation(info);
        }

        OnDamaged?.Invoke(this, info); // 모든 실제 피해 통지
        RaiseHealthChanged(); // HP바 갱신

        if (showDamageNumbers)
            SpawnDamageNumber(info, damage, actualDamage); // 데미지 숫자 표시

        if (currentHp <= 0f)
            Die(info); // HP 0 사망 처리
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || IsDead)
            return;

        currentHp = Mathf.Min(maxHp, currentHp + amount);
        RaiseHealthChanged();
    }

    private void SpawnDamageNumber(DamageInfo info, float displayDamage, float actualDamage)
    {
        if (CombatTeamUtility.IsPlayerActorHealth(this))
        {
            DamageNumberSpawner.SpawnPlayerDamage(transform.position, actualDamage);
            return;
        }

        DamageNumberSpawner.Spawn(info, displayDamage);
    }

    public void ApplyDamageOverTime(float damagePerTick, float duration, float tickInterval, GameObject damageSource, Vector3 damageDirection)
    {
        if (IsDead)
            return;

        damagePerTick = Mathf.Max(0f, damagePerTick);
        if (damagePerTick <= 0f)
            return;

        float resolvedDuration = duration > 0f ? duration : 3f; // 지속 시간
        float resolvedTickInterval = Mathf.Max(0.05f, tickInterval); // tick 간격
        int tickCount = Mathf.Max(1, Mathf.FloorToInt(resolvedDuration / resolvedTickInterval)); // tick 수

        if (activeDamageOverTimeRoutine != null)
            StopCoroutine(activeDamageOverTimeRoutine);

        activeDamageOverTimeRoutine = StartCoroutine(DamageOverTimeRoutine(damagePerTick, tickCount, resolvedTickInterval, damageSource, damageDirection));
    }

    [ContextMenu("Reset Health")]
    public void ResetHealth()
    {
        maxHp = Mathf.Max(1f, maxHp); // 최대 HP 최소값 보장
        currentHp = maxHp; // HP 완전 회복
        IsDead = false; // 사망 상태 해제
        RaiseHealthChanged(); // HP UI 갱신
        OnReset?.Invoke(this); // 상태·풀 재사용 정리
    }

    public void SetMaxHp(float value, bool refill)
    {
        maxHp = Mathf.Max(1f, value);

        if (refill)
            currentHp = maxHp;
        else
            currentHp = Mathf.Clamp(currentHp, 0f, maxHp);

        IsDead = currentHp <= 0f;
        RaiseHealthChanged();
    }

    private void Die(DamageInfo info)
    {
        if (IsDead)
            return; // 사망 이벤트 중복 방지

        if (activeDamageOverTimeRoutine != null)
        {
            StopCoroutine(activeDamageOverTimeRoutine);
            activeDamageOverTimeRoutine = null;
        }

        IsDead = true; // 사망 상태 고정
        OnDead?.Invoke(this, info); // 사망 이벤트

        if (destroyOnDeath)
            Destroy(gameObject); // 옵션형 즉시 제거
    }

    private void ApplyKnockback(DamageInfo info)
    {
        if (info.knockback <= 0f)
            return; // 넉백 없음

        Vector3 knockbackDirection = info.direction;
        knockbackDirection.y = 0f; // XZ 평면 넉백

        if (knockbackDirection.sqrMagnitude <= 0.0001f && info.source != null)
        {
            knockbackDirection = transform.position - info.source.transform.position; // 출처 fallback
            knockbackDirection.y = 0f; // XZ 평면 유지
        }

        if (knockbackDirection.sqrMagnitude <= 0.0001f)
            return; // 방향 없음

        EnemyMovementReaction movementReaction = GetComponentInParent<EnemyMovementReaction>();
        if (movementReaction != null)
        {
            // Weighted enemies resolve physical and visual feedback together after shield/freeze checks.
            if (movementReaction.HitWeightProfile != null) return;
            movementReaction.ApplyKnockback(knockbackDirection.normalized, info.knockback); // 적 제어형 넉백
            return;
        }

        Rigidbody targetRigidbody = GetComponentInParent<Rigidbody>();
        if (targetRigidbody == null || targetRigidbody.isKinematic)
            return; // 물리 대상 아님

        targetRigidbody.AddForce(knockbackDirection.normalized * info.knockback, ForceMode.Impulse); // 넉백 힘 적용
    }

    private void ApplyAimDamageModifier(ref DamageInfo info, ref float damage)
    {
        PlayerMovement playerMovement = GetComponentInParent<PlayerMovement>();
        if (playerMovement == null)
            return;

        if (TryApplySwordParry(ref info, ref damage, playerMovement))
            return;

        float multiplier = playerMovement.ActiveAimIncomingDamageMultiplier;
        if (multiplier >= 0.999f)
            return;

        PlayMeleeGuardBlockAnimation(playerMovement);
        damage *= Mathf.Clamp01(multiplier);
        info.damage = damage; // 이벤트/표시 피해량 동기화
    }

    private void ApplyEnemyDefenseModifier(ref DamageInfo info, ref float damage)
    {
        EnemyDefenseController defenseController = GetComponentInParent<EnemyDefenseController>();
        defenseController?.TryModifyIncomingDamage(ref info, ref damage);
    }

    private void ApplyPlayerDamageReductionDebug(ref DamageInfo info, ref float damage)
    {
        if (!CombatTeamUtility.IsPlayerActorHealth(this)
            || !CombatDebugSettings.ReduceIncomingPlayerDamageBy99_9Percent)
        {
            return;
        }

        damage = CombatDebugSettings.ApplyPlayerDamageReductionDebug(damage);
        info.damage = damage;
    }

    private bool TryCancelDamageByEvade(DamageInfo info)
    {
        PlayerEvadeController evadeController = GetComponentInParent<PlayerEvadeController>();
        return evadeController != null && evadeController.TryCancelDamageByEvade(info);
    }

    private bool TryApplySwordParry(ref DamageInfo info, ref float damage, PlayerMovement playerMovement)
    {
        if (!enableSwordParry)
            return false;

        if (info.isDamageOverTime || !info.triggersOnHitEffects)
            return false;

        if (!playerMovement.TryConsumeMeleeGuardParry())
            return false;

        if (CanOpenPlayerCombatMode())
            PlayerCombatModeController.EnterSharedCombatMode(PlayerCombatModeReason.Damaged);
        PlayMeleeGuardBlockAnimation(playerMovement);

        PlayerEquipment equipment = playerMovement.GetComponent<PlayerEquipment>();
        float parryDamage = equipment != null ? equipment.CurrentMeleeGuardParryDamage : 1f;
        damage = Mathf.Max(1f, parryDamage);
        info.damage = damage;

        return true;
    }

    private bool CanOpenPlayerCombatMode()
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        return context != null && context.CurrentActorHealth == this;
    }

    private static void PlayMeleeGuardBlockAnimation(PlayerMovement playerMovement)
    {
        if (playerMovement == null || !playerMovement.IsMeleeGuarding)
            return;

        PlayerAnimation playerAnimation = playerMovement.GetComponent<PlayerAnimation>();
        playerAnimation?.NotifyMeleeGuardBlockedHit();
    }

    private void PlayCombatDamagedHitAnimation(DamageInfo info)
    {
        if (info.isDamageOverTime || !info.triggersOnHitEffects)
            return;

        PlayerMovement playerMovement = GetComponentInParent<PlayerMovement>();
        if (playerMovement == null
            || !playerMovement.IsMeleeCombatLocomotionMode
            || playerMovement.IsMeleeGuarding
            || playerMovement.IsEvading)
        {
            return;
        }

        MeleeRuntime meleeRuntime = playerMovement.GetComponent<MeleeRuntime>();
        if (meleeRuntime != null && meleeRuntime.IsAttackInProgress)
            return;

        PlayerAnimation playerAnimation = playerMovement.GetComponent<PlayerAnimation>();
        playerAnimation?.NotifyCombatDamagedHit();
    }

    private void RaiseHealthChanged()
    {
        OnHealthChanged?.Invoke(this, currentHp, maxHp);
    }

    private IEnumerator DamageOverTimeRoutine(float damagePerTick, int tickCount, float tickInterval, GameObject damageSource, Vector3 damageDirection)
    {
        for (int i = 0; i < tickCount; i++)
        {
            yield return new WaitForSeconds(tickInterval);

            if (IsDead)
                break;

            DamageInfo tickInfo = new DamageInfo(
                damagePerTick,
                transform.position,
                damageSource,
                damageDirection,
                0f,
                false,
                false,
                true);

            TakeDamage(tickInfo);
        }

        activeDamageOverTimeRoutine = null;
    }
}
