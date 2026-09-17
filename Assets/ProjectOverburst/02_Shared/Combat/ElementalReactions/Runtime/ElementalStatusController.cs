using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class ElementalStatusController : MonoBehaviour, IElementalStatusReceiver, IElementalReactionStateOwner
{
    private sealed class StatusSlot
    {
        public readonly WeaponElement Element;
        public bool IsActive;
        public int StackCount;
        public float ExpiresAt;
        public float NextTickAt;
        public ElementalStatusOwnerSnapshot Owner;

        public StatusSlot(WeaponElement element) { Element = element; }

        public void Clear()
        {
            IsActive = false;
            StackCount = 0;
            ExpiresAt = 0f;
            NextTickAt = 0f;
            Owner = default;
        }
    }

    private sealed class ReactionStateSlot
    {
        public readonly ElementalReactionType ReactionType;
        public bool IsActive;
        public float ExpiresAt;
        public float IncomingDamageMultiplier = 1f;
        public float StoredDamage;
        public float MoveSpeedMultiplier = 1f;
        public float ActionSpeedMultiplier = 1f;
        public ElementalReactionOwnerSnapshot Owner;

        public ReactionStateSlot(ElementalReactionType reactionType) { ReactionType = reactionType; }

        public void Clear()
        {
            IsActive = false;
            ExpiresAt = 0f;
            IncomingDamageMultiplier = 1f;
            StoredDamage = 0f;
            MoveSpeedMultiplier = 1f;
            ActionSpeedMultiplier = 1f;
            Owner = default;
        }
    }

    [SerializeField] private CombatHealth combatHealth;
    [SerializeField] private EnemyRank enemyRank;
    [SerializeField] private EnemyMovement enemyMovement;
    [SerializeField] private EnemyMeleeAttackController enemyAttackController;
    [SerializeField] private MeleeElementStatusAuraController auraController;

    private readonly StatusSlot burning = new StatusSlot(WeaponElement.Fire);
    private readonly StatusSlot wet = new StatusSlot(WeaponElement.Water);
    private readonly StatusSlot chilled = new StatusSlot(WeaponElement.Ice);
    private readonly StatusSlot shocked = new StatusSlot(WeaponElement.Electric);
    private readonly ReactionStateSlot plasma = new ReactionStateSlot(ElementalReactionType.Plasma);
    private readonly ReactionStateSlot freeze = new ReactionStateSlot(ElementalReactionType.Freeze);
    private readonly ReactionStateSlot coldCharge = new ReactionStateSlot(ElementalReactionType.ColdCharge);
    private float lastAdvancedTime = float.NegativeInfinity;
    private bool hasDeferredTickWork;

    public event Action<ElementalStatusSnapshot, ElementalStatusChangeReason> StatusChanged;
    public event Action<WeaponElement, ElementalStatusRemoveReason> StatusRemoved;
    public event Action<ElementalStatusClearReason> StatusesCleared;
    public event Action<ElementalReactionStateSnapshot, ElementalReactionStateChangeReason> ReactionStateChanged;
    public event Action<ElementalReactionType, ElementalReactionStateRemoveReason> ReactionStateRemoved;
    public event Action<ElementalStatusClearReason> ReactionStatesCleared;

    public float MoveSpeedMultiplier { get; private set; } = 1f;
    public float ActionSpeedMultiplier { get; private set; } = 1f;
    public float BasicMoveSpeedMultiplier { get; private set; } = 1f;
    public float BasicActionSpeedMultiplier { get; private set; } = 1f;
    public float ReactionMoveSpeedMultiplier { get; private set; } = 1f;
    public float ReactionActionSpeedMultiplier { get; private set; } = 1f;

    private void Awake()
    {
        ResetTimeSynchronization();
        ResolveReferences();
        ClearRuntimeState(false, ElementalStatusClearReason.Disabled); // 첫 활성 잔존 차단
        ClearReactionRuntimeState(false, ElementalStatusClearReason.Disabled);
    }

    private void OnEnable()
    {
        ResetTimeSynchronization();
        ResolveReferences();
        SubscribeHealthEvents();
        ClearRuntimeState(false, ElementalStatusClearReason.Disabled); // 풀 재사용 초기화
        ClearReactionRuntimeState(false, ElementalStatusClearReason.Disabled);
    }

    private void OnDisable()
    {
        UnsubscribeHealthEvents();
        ClearAllStatuses(ElementalStatusClearReason.Disabled);
        ClearAllReactionStates(ElementalStatusClearReason.Disabled);
    }

    internal bool AdvanceScheduledStates(float now, ref int remainingTickBudget)
    {
        if (combatHealth == null || combatHealth.IsDead || combatHealth.CurrentHp <= 0f)
            return false;

        SynchronizeTime(now, ref remainingTickBudget);
        return HasScheduledTimeState();
    }

    private void SynchronizeTime(float now)
    {
        int unlimitedTickBudget = int.MaxValue;
        SynchronizeTime(now, ref unlimitedTickBudget);
    }

    private void SynchronizeTime(float now, ref int remainingTickBudget)
    {
        if (now < lastAdvancedTime)
        {
            lastAdvancedTime = float.NegativeInfinity; // 검증·재진입의 시간 원점 변경 허용
            hasDeferredTickWork = false;
        }
        if (now <= lastAdvancedTime && !hasDeferredTickWork)
            return;

        lastAdvancedTime = now; // DoT 재진입 전에 같은 시각의 중복 진행 차단
        hasDeferredTickWork = false;
        UpdateSlot(burning, now, ref remainingTickBudget);
        UpdateSlot(wet, now, ref remainingTickBudget);
        UpdateSlot(chilled, now, ref remainingTickBudget);
        UpdateSlot(shocked, now, ref remainingTickBudget);
        UpdateReactionState(freeze, now);
        UpdateReactionState(coldCharge, now);
    }

    public bool TryApplyDirectHit(ElementalStatusApplication application)
    {
        if (combatHealth == null || combatHealth.IsDead || combatHealth.CurrentHp <= 0f)
            return false;

        if (application.ActualDirectDamage <= 0f
            || !application.TriggersOnHitEffects
            || application.IsDamageOverTime
            || !ElementalStatusRules.TryGetRule(application.Element, out ElementalStatusRule rule))
        {
            return false;
        }

        float now = Time.time;
        SynchronizeTime(now); // 타격보다 먼저 만료·밀린 tick 정산
        if (combatHealth == null || combatHealth.IsDead || combatHealth.CurrentHp <= 0f)
            return false;

        if (coldCharge.IsActive)
            return false; // 냉전하 동안 기본 원소 상태 차단

        StatusSlot slot = GetSlot(application.Element);
        if (slot == null)
            return false;

        bool wasActive = slot.IsActive;
        int previousStacks = slot.StackCount;
        slot.IsActive = true;
        slot.StackCount = Mathf.Min(rule.MaxStacks, previousStacks + 1);
        slot.ExpiresAt = now + rule.Duration; // 상태 전체 시간 갱신
        slot.Owner = new ElementalStatusOwnerSnapshot(application);

        if (!wasActive && rule.HasDamageTicks)
            slot.NextTickAt = now + rule.TickInterval; // 새 상태만 tick 위상 시작

        UpdateAuraForStatusApplication(slot.Element, wasActive);
        ApplySpeedMultipliers();

        ElementalStatusChangeReason reason = !wasActive
            ? ElementalStatusChangeReason.Applied
            : slot.StackCount > previousStacks
                ? ElementalStatusChangeReason.StackIncreased
                : ElementalStatusChangeReason.Refreshed;
        StatusChanged?.Invoke(CreateSnapshot(slot, now), reason);
        RefreshScheduling();
        return true;
    }

    public bool TryGetStatus(WeaponElement element, out ElementalStatusSnapshot snapshot)
    {
        float now = Time.time;
        SynchronizeTime(now); // 반응 snapshot보다 먼저 만료 상태 제거
        StatusSlot slot = GetSlot(element);
        if (slot == null || !slot.IsActive)
        {
            snapshot = default;
            return false;
        }

        snapshot = CreateSnapshot(slot, now);
        return true;
    }

    public bool HasStatus(WeaponElement element)
    {
        SynchronizeTime(Time.time);
        StatusSlot slot = GetSlot(element);
        return slot != null && slot.IsActive;
    }

    public int GetStackCount(WeaponElement element)
    {
        SynchronizeTime(Time.time);
        StatusSlot slot = GetSlot(element);
        return slot != null && slot.IsActive ? slot.StackCount : 0;
    }

    public void ClearAllStatuses(ElementalStatusClearReason reason = ElementalStatusClearReason.Explicit)
    {
        ClearRuntimeState(true, reason);
    }

    public bool TryApplyReactionState(ElementalReactionStateApplication application)
    {
        ResolveReferences();
        if (combatHealth == null
            || combatHealth.IsDead
            || combatHealth.CurrentHp <= 0f
            || (application.Duration <= 0f && application.ReactionType != ElementalReactionType.Plasma))
        {
            return false;
        }

        ReactionStateSlot slot = GetReactionStateSlot(application.ReactionType);
        if (slot == null)
            return false;

        float now = Time.time;
        SynchronizeTime(now);
        if (combatHealth == null || combatHealth.IsDead || combatHealth.CurrentHp <= 0f)
            return false;

        bool wasActive = slot.IsActive;
        slot.IsActive = true;
        slot.ExpiresAt = now + application.Duration; // 비중첩 전체 시간 갱신
        if (application.ReactionType == ElementalReactionType.Plasma)
            slot.ExpiresAt = 0f; // 다음 직접 Hit까지 무기한
        slot.IncomingDamageMultiplier = application.IncomingDamageMultiplier;
        slot.StoredDamage = application.StoredDamage;
        slot.MoveSpeedMultiplier = application.MoveSpeedMultiplier;
        slot.ActionSpeedMultiplier = application.ActionSpeedMultiplier;
        slot.Owner = application.Owner;
        ApplySpeedMultipliers();
        ElementalReactionStateSnapshot changedSnapshot = CreateReactionStateSnapshot(slot, now);
        ElementalReactionStateChangeReason changeReason = wasActive
            ? ElementalReactionStateChangeReason.Refreshed
            : ElementalReactionStateChangeReason.Applied;
        ReactionStateChanged?.Invoke(changedSnapshot, changeReason);
        ElementalReactionStateEvents.RaiseStateChanged(this, changedSnapshot, changeReason);
        RefreshScheduling();
        return true;
    }

    public void ResolvePendingReactionResults(ElementalApplicationContext triggerContext)
    {
        if (combatHealth == null
            || combatHealth.IsDead
            || combatHealth.CurrentHp <= 0f
            || triggerContext.ActualDirectDamage <= 0f
            || !triggerContext.TriggersOnHitEffects
            || triggerContext.IsDamageOverTime)
        {
            return;
        }

        SynchronizeTime(Time.time); // 저장 proc보다 만료·tick 우선
        if (combatHealth == null || combatHealth.IsDead || combatHealth.CurrentHp <= 0f)
            return;

        if (freeze.IsActive)
        {
            ElementalReactionOwnerSnapshot owner = freeze.Owner;
            RemoveReactionState(freeze, ElementalReactionStateRemoveReason.Consumed); // 쇄빙을 먼저 정산
            ElementalReactionProcExecutor.ExecuteShatter(combatHealth, triggerContext.Target, owner, triggerContext);
        }

        if (plasma.IsActive && triggerContext.Target != null)
        {
            ElementalReactionOwnerSnapshot owner = plasma.Owner;
            float storedDamage = plasma.StoredDamage;
            RemoveReactionState(plasma, ElementalReactionStateRemoveReason.Consumed);
            ElementalReactionProcExecutor.ExecutePlasma(triggerContext.Target, owner, storedDamage, triggerContext);
        }
        if (coldCharge.IsActive && triggerContext.Target != null)
            ElementalReactionProcExecutor.ExecuteColdCharge(triggerContext.Target, coldCharge.Owner, triggerContext);
    }

    public bool TryGetReactionState(
        ElementalReactionType reactionType,
        out ElementalReactionStateSnapshot snapshot)
    {
        ReactionStateSlot slot = GetReactionStateSlot(reactionType);
        if (slot == null)
        {
            snapshot = default;
            return false;
        }

        float now = Time.time;
        SynchronizeTime(now);
        if (!slot.IsActive)
        {
            snapshot = default;
            return false;
        }

        snapshot = CreateReactionStateSnapshot(slot, now);
        return true;
    }

    public void ClearAllReactionStates(ElementalStatusClearReason reason = ElementalStatusClearReason.Explicit)
    {
        ClearReactionRuntimeState(true, reason);
    }

#if UNITY_EDITOR
    public void AdvanceReactionStatesForValidation(float now)
    {
        SynchronizeTime(now);
    }
#endif

    private void UpdateSlot(StatusSlot slot, float now, ref int remainingTickBudget)
    {
        if (!slot.IsActive || !ElementalStatusRules.TryGetRule(slot.Element, out ElementalStatusRule rule))
            return;

        if (rule.HasDamageTicks)
        {
            while (slot.IsActive
                && remainingTickBudget > 0
                && slot.NextTickAt <= now
                && slot.NextTickAt <= slot.ExpiresAt)
            {
                slot.NextTickAt += rule.TickInterval; // 재진입 전 시계 선진행
                remainingTickBudget--;
                ApplyDamageTick(slot, rule);
            }

            if (slot.IsActive
                && slot.NextTickAt <= now
                && slot.NextTickAt <= slot.ExpiresAt)
            {
                hasDeferredTickWork = true; // 남은 총피해를 버리지 않고 다음 scheduler 방문으로 이월
                return;
            }
        }

        if (slot.IsActive && now >= slot.ExpiresAt)
            RemoveStatus(slot, ElementalStatusRemoveReason.Expired);
    }

    private void UpdateReactionState(ReactionStateSlot slot, float now)
    {
        if (slot.IsActive && slot.ExpiresAt > 0f && now >= slot.ExpiresAt)
            RemoveReactionState(slot, ElementalReactionStateRemoveReason.Expired);
    }

    private void ApplyDamageTick(StatusSlot slot, ElementalStatusRule rule)
    {
        if (combatHealth == null || combatHealth.IsDead)
            return;

        float damage = ElementalStatusRules.ResolveTickDamage(
            slot.Owner.ActualDirectDamage,
            rule.TickDamageCoefficient,
            slot.StackCount);
        if (damage <= 0f)
            return;

        DamageInfo tickInfo = new DamageInfo(
            damage,
            combatHealth.transform.position,
            slot.Owner.SourceActor,
            Vector3.zero,
            0f,
            false,
            false,
            true,
            default,
            true,
            slot.Element,
            slot.Owner.SourceWeaponRuntimeInstanceId);
        combatHealth.TakeDamage(tickInfo); // 공용 피해 경로 유지
    }

    private void RemoveStatus(StatusSlot slot, ElementalStatusRemoveReason reason)
    {
        WeaponElement element = slot.Element;
        slot.Clear();
        ClearAura(element);
        ApplySpeedMultipliers();
        StatusRemoved?.Invoke(element, reason);
        RefreshScheduling();
    }

    private void RemoveReactionState(ReactionStateSlot slot, ElementalReactionStateRemoveReason reason)
    {
        ElementalReactionType reactionType = slot.ReactionType;
        slot.Clear();
        ApplySpeedMultipliers();
        ReactionStateRemoved?.Invoke(reactionType, reason);
        ElementalReactionStateEvents.RaiseStateRemoved(this, reactionType, reason);
        RefreshScheduling();
    }

    private void ClearRuntimeState(bool notify, ElementalStatusClearReason reason)
    {
        burning.Clear();
        wet.Clear();
        chilled.Clear();
        shocked.Clear();
        BasicMoveSpeedMultiplier = 1f;
        BasicActionSpeedMultiplier = 1f;
        ApplyCombinedSpeedMultipliers();
        auraController?.ClearAllAuras();

        if (notify)
            StatusesCleared?.Invoke(reason);

        RefreshScheduling();
    }

    private void ClearReactionRuntimeState(bool notify, ElementalStatusClearReason reason)
    {
        bool wasPlasmaActive = plasma.IsActive;
        bool wasFreezeActive = freeze.IsActive;
        bool wasColdChargeActive = coldCharge.IsActive;
        plasma.Clear();
        freeze.Clear();
        coldCharge.Clear();
        ApplySpeedMultipliers();
        RefreshScheduling();

        if (!notify)
            return;

        if (wasPlasmaActive)
        {
            ReactionStateRemoved?.Invoke(ElementalReactionType.Plasma, ElementalReactionStateRemoveReason.Cleared);
            ElementalReactionStateEvents.RaiseStateRemoved(
                this,
                ElementalReactionType.Plasma,
                ElementalReactionStateRemoveReason.Cleared);
        }
        if (wasFreezeActive)
        {
            ReactionStateRemoved?.Invoke(ElementalReactionType.Freeze, ElementalReactionStateRemoveReason.Cleared);
            ElementalReactionStateEvents.RaiseStateRemoved(
                this,
                ElementalReactionType.Freeze,
                ElementalReactionStateRemoveReason.Cleared);
        }
        if (wasColdChargeActive)
        {
            ReactionStateRemoved?.Invoke(ElementalReactionType.ColdCharge, ElementalReactionStateRemoveReason.Cleared);
            ElementalReactionStateEvents.RaiseStateRemoved(
                this,
                ElementalReactionType.ColdCharge,
                ElementalReactionStateRemoveReason.Cleared);
        }

        ReactionStatesCleared?.Invoke(reason);
        ElementalReactionStateEvents.RaiseStatesCleared(this, reason);
    }

    private bool HasScheduledTimeState()
    {
        return burning.IsActive
            || wet.IsActive
            || chilled.IsActive
            || shocked.IsActive
            || freeze.IsActive
            || coldCharge.IsActive;
    }

    private void RefreshScheduling()
    {
        if (isActiveAndEnabled && HasScheduledTimeState())
            ElementalStatusScheduler.Register(this);
        else
            ElementalStatusScheduler.Unregister(this);
    }

    private void ResetTimeSynchronization()
    {
        lastAdvancedTime = float.NegativeInfinity;
        hasDeferredTickWork = false;
    }

    private void ApplySpeedMultipliers()
    {
        float resistance = ElementalStatusRules.ResolveControlEffectMultiplier(enemyRank);
        ElementalStatusRules.ResolveSpeedMultipliers(
            wet.IsActive,
            chilled.IsActive ? chilled.StackCount : 0,
            resistance,
            out float moveMultiplier,
            out float actionMultiplier);

        BasicMoveSpeedMultiplier = moveMultiplier;
        BasicActionSpeedMultiplier = actionMultiplier;
        ReactionMoveSpeedMultiplier = freeze.IsActive ? freeze.MoveSpeedMultiplier : 1f;
        ReactionActionSpeedMultiplier = freeze.IsActive ? freeze.ActionSpeedMultiplier : 1f;
        ApplyCombinedSpeedMultipliers();
    }

    private void ApplyCombinedSpeedMultipliers()
    {
        MoveSpeedMultiplier = Mathf.Clamp01(BasicMoveSpeedMultiplier * ReactionMoveSpeedMultiplier);
        ActionSpeedMultiplier = Mathf.Clamp01(BasicActionSpeedMultiplier * ReactionActionSpeedMultiplier);
        enemyMovement?.SetStatusMoveSpeedMultiplier(MoveSpeedMultiplier);
        enemyAttackController?.SetStatusActionSpeedMultiplier(ActionSpeedMultiplier);
    }

    private ElementalStatusSnapshot CreateSnapshot(StatusSlot slot, float now)
    {
        return new ElementalStatusSnapshot(
            slot.Element,
            slot.IsActive,
            slot.StackCount,
            slot.ExpiresAt - now,
            MoveSpeedMultiplier,
            ActionSpeedMultiplier,
            slot.Owner);
    }

    private static ElementalReactionStateSnapshot CreateReactionStateSnapshot(
        ReactionStateSlot slot,
        float now)
    {
        return new ElementalReactionStateSnapshot(
            slot.ReactionType,
            slot.IsActive,
            slot.ExpiresAt - now,
            slot.IncomingDamageMultiplier,
            slot.Owner,
            slot.StoredDamage,
            slot.MoveSpeedMultiplier,
            slot.ActionSpeedMultiplier);
    }

    private StatusSlot GetSlot(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return burning;
            case WeaponElement.Water: return wet;
            case WeaponElement.Ice: return chilled;
            case WeaponElement.Electric: return shocked;
            default: return null;
        }
    }

    private ReactionStateSlot GetReactionStateSlot(ElementalReactionType reactionType)
    {
        switch (reactionType)
        {
            case ElementalReactionType.Plasma: return plasma;
            case ElementalReactionType.Freeze: return freeze;
            case ElementalReactionType.ColdCharge: return coldCharge;
            default: return null;
        }
    }

    private void UpdateAuraForStatusApplication(WeaponElement element, bool wasActive)
    {
        if (element != WeaponElement.Electric
            || auraController == null
            || !TryResolveAuraType(element, out MeleeElementStatusAuraType auraType))
            return;

        if (wasActive)
            auraController.RefreshAuraLifetime(auraType); // 동일 상태 갱신은 현재 재생 유지
        else
            auraController.StartAura(auraType); // 신규 상태만 Stop·Clear·Play
    }

    private void ClearAura(WeaponElement element)
    {
        if (auraController != null && TryResolveAuraType(element, out MeleeElementStatusAuraType auraType))
            auraController.ClearAura(auraType);
    }

    private static bool TryResolveAuraType(WeaponElement element, out MeleeElementStatusAuraType auraType)
    {
        switch (element)
        {
            case WeaponElement.Fire: auraType = MeleeElementStatusAuraType.Burning; return true;
            case WeaponElement.Water: auraType = MeleeElementStatusAuraType.Wet; return true;
            case WeaponElement.Ice: auraType = MeleeElementStatusAuraType.Chilled; return true;
            case WeaponElement.Electric: auraType = MeleeElementStatusAuraType.Shocked; return true;
            default: auraType = default; return false;
        }
    }

    private void ResolveReferences()
    {
        if (combatHealth == null)
            combatHealth = GetComponent<CombatHealth>();
        if (enemyRank == null)
            enemyRank = GetComponent<EnemyRank>();
        if (enemyMovement == null)
            enemyMovement = GetComponent<EnemyMovement>();
        if (enemyAttackController == null)
            enemyAttackController = GetComponent<EnemyMeleeAttackController>();
        if (auraController == null)
            auraController = GetComponent<MeleeElementStatusAuraController>();
    }

    private void SubscribeHealthEvents()
    {
        if (combatHealth == null)
            return;

        combatHealth.OnDead -= HandleDeath;
        combatHealth.OnDead += HandleDeath;
        combatHealth.OnReset -= HandleReset;
        combatHealth.OnReset += HandleReset;
    }

    private void UnsubscribeHealthEvents()
    {
        if (combatHealth == null)
            return;

        combatHealth.OnDead -= HandleDeath;
        combatHealth.OnReset -= HandleReset;
    }

    private void HandleDeath(CombatHealth _, DamageInfo __)
    {
        ClearAllStatuses(ElementalStatusClearReason.Death);
        ClearAllReactionStates(ElementalStatusClearReason.Death);
    }

    private void HandleReset(CombatHealth _)
    {
        ClearAllStatuses(ElementalStatusClearReason.Reset);
        ClearAllReactionStates(ElementalStatusClearReason.Reset);
    }
}
