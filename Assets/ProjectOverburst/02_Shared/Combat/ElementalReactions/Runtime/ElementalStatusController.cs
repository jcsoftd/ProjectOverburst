using System;
using System.Collections.Generic;
using UnityEngine;

// Keeps the existing prefab GUID/UI contract, with single-element buildup replacing combination reactions.
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class ElementalStatusController : MonoBehaviour, IElementalStatusReceiver, IElementalReactionStateOwner
{
    [SerializeField] private CombatHealth combatHealth;
    [SerializeField] private EnemyRank enemyRank;
    [SerializeField] private EnemyMovement enemyMovement;
    [SerializeField] private EnemyMeleeAttackController enemyAttackController;
    [SerializeField] private bool freezeControlImmune;
    private readonly OverburstElementState state = new OverburstElementState();
    private readonly ElementalStatusOwnerSnapshot[] owners = new ElementalStatusOwnerSnapshot[OverburstElementRules.Count];
    private readonly HashSet<(int, int)> hits = new HashSet<(int, int)>();
    private readonly Queue<(int, int)> hitOrder = new Queue<(int, int)>();
    private bool frozenPresentation;
    private bool advancing;
    private float staggerUntil;
    private MeleeElementStatusAuraController aura;
    private HitFlashFeedback tint;
    private EnemyAbilityController enemyAbilities;
    public int DeliveredTickCount { get; private set; }
    public bool IsShockStaggered => staggerUntil > Time.time;
    public int LifecycleVersion { get; private set; }
    public event Action<ElementalStatusSnapshot, ElementalStatusChangeReason> StatusChanged;
    public event Action<WeaponElement, ElementalStatusRemoveReason> StatusRemoved;
    public event Action<ElementalStatusClearReason> StatusesCleared;
    public event Action<ElementalReactionStateSnapshot, ElementalReactionStateChangeReason> ReactionStateChanged;
    public event Action<ElementalReactionType, ElementalReactionStateRemoveReason> ReactionStateRemoved;
    public event Action<ElementalStatusClearReason> ReactionStatesCleared;
    public float MoveSpeedMultiplier { get; private set; } = 1f;
    public float ActionSpeedMultiplier => MoveSpeedMultiplier;
    public float BasicMoveSpeedMultiplier => 1f;
    public float BasicActionSpeedMultiplier => 1f;
    public float ReactionMoveSpeedMultiplier => MoveSpeedMultiplier;
    public float ReactionActionSpeedMultiplier => MoveSpeedMultiplier;
    public bool IsFrozen { get { Advance(Time.time); return state.IsFrozen(Time.time); } }

    private void Awake() => ResolveReferences();
    private void OnEnable()
    {
        ResolveReferences();
        combatHealth.OnDead += Died;
        combatHealth.OnReset += ResetHealth;
        ClearAllStatuses(ElementalStatusClearReason.Reset);
    }
    private void OnDisable()
    {
        if (combatHealth != null) { combatHealth.OnDead -= Died; combatHealth.OnReset -= ResetHealth; }
        ClearAllStatuses(ElementalStatusClearReason.Disabled);
    }
    private void ResolveReferences()
    {
        if (combatHealth == null) combatHealth = GetComponent<CombatHealth>();
        if (enemyRank == null) enemyRank = GetComponent<EnemyRank>();
        if (enemyMovement == null) enemyMovement = GetComponent<EnemyMovement>();
        if (enemyAttackController == null) enemyAttackController = GetComponent<EnemyMeleeAttackController>();
        if (enemyAbilities == null) enemyAbilities = GetComponent<EnemyAbilityController>();
    }
    private void Died(CombatHealth _, DamageInfo info) => ClearAllStatuses(ElementalStatusClearReason.Death);
    private void ResetHealth(CombatHealth _) => ClearAllStatuses(ElementalStatusClearReason.Reset);
    public bool ApplyConfirmedHit(DamageInfo info, float actualDamage)
    {
        if (!isActiveAndEnabled || info.source == null || info.sourceAttackSequenceId <= 0
            || !OverburstElementTuning.IsFinitePositive(actualDamage) || !info.triggersOnHitEffects || info.isDamageOverTime
            || info.elementalReactionType != ElementalReactionType.None || !OverburstElementRules.IsActive(info.element)) return false;
        var key = (info.source.GetInstanceID(), info.sourceAttackSequenceId);
        if (!hits.Add(key)) return false;
        hitOrder.Enqueue(key);
        while (hitOrder.Count > 128) hits.Remove(hitOrder.Dequeue());
        return TryApplyDirectHit(new ElementalStatusApplication(info.element, actualDamage, info.source,
            info.sourceWeaponRuntimeInstanceId, true, false, info.hitPoint, info.direction));
    }
    public bool TryApplyDirectHit(ElementalStatusApplication application)
    {
        using var costScope = ElementCombatCostMarkers.Status_Apply.Auto();
        if (!isActiveAndEnabled || combatHealth == null || combatHealth.IsDead || combatHealth.CurrentHp <= 0f
            || !OverburstElementTuning.IsFinitePositive(application.ActualDirectDamage)
            || !application.TriggersOnHitEffects || application.IsDamageOverTime
            || !ElementalStatusRules.TryGetRule(application.Element, out _)) return false;
        Advance(Time.time);
        if (combatHealth.IsDead || !isActiveAndEnabled) return false;
        float freezeMultiplier = enemyRank != null && enemyRank.GradeType != EnemyGradeType.Normal ? 1f
            : 1f + FlaskCombatModifiers.Bonus(application.SourceActor, FlaskEffect.FreezeDuration);
        if (!state.Add(application.Element, Time.time, OverburstElementTuning.Current, freezeMultiplier)) return false;
        owners[OverburstElementRules.Index(application.Element)] = new ElementalStatusOwnerSnapshot(application);
        if (Application.isPlaying && (application.Element == WeaponElement.Fire || application.Element == WeaponElement.Electric) && aura == null)
            aura = GetComponent<MeleeElementStatusAuraController>() ?? gameObject.AddComponent<MeleeElementStatusAuraController>();
        if (application.Element == WeaponElement.Ice && tint == null)
            tint = GetComponent<HitFlashFeedback>() ?? gameObject.AddComponent<HitFlashFeedback>();
        RefreshControl();
        TryGetStatus(application.Element, out ElementalStatusSnapshot snapshot);
        StatusChanged?.Invoke(snapshot, snapshot.StackCount == 1 ? ElementalStatusChangeReason.Applied : ElementalStatusChangeReason.StackIncreased);
        ElementalStatusScheduler.Register(this);
        return true;
    }
    public bool TryGetStatus(WeaponElement element, out ElementalStatusSnapshot snapshot)
    {
        Advance(Time.time);
        int count = state.Count(element, Time.time);
        snapshot = count > 0 ? new ElementalStatusSnapshot(element, true, count, state.Remaining(element, Time.time),
            MoveSpeedMultiplier, ActionSpeedMultiplier, owners[OverburstElementRules.Index(element)]) : default;
        return count > 0;
    }
    public bool HasStatus(WeaponElement element) => TryGetStatus(element, out _);
    public int GetStackCount(WeaponElement element) => TryGetStatus(element, out ElementalStatusSnapshot snapshot) ? snapshot.StackCount : 0;
    public int ConsumeForDischarge(WeaponElement element, out bool shattered)
    {
        using var costScope = ElementCombatCostMarkers.Status_Consume.Auto();
        Advance(Time.time);
        int count = state.Consume(element, Time.time, out shattered);
        if (count <= 0) return 0;
        owners[OverburstElementRules.Index(element)] = default;
        RefreshControl();
        StatusRemoved?.Invoke(element, ElementalStatusRemoveReason.Cleared);
        if (!state.HasAny && !IsShockStaggered) ElementalStatusScheduler.Unregister(this);
        return count;
    }
    public void ClearAllStatuses(ElementalStatusClearReason reason = ElementalStatusClearReason.Explicit)
    {
        if (reason == ElementalStatusClearReason.Reset || reason == ElementalStatusClearReason.Disabled) LifecycleVersion++;
        state.Clear(); Array.Clear(owners, 0, owners.Length); hits.Clear(); hitOrder.Clear();
        staggerUntil = 0f;
        RefreshControl();
        ElementalStatusScheduler.Unregister(this);
        StatusesCleared?.Invoke(reason);
        ReactionStatesCleared?.Invoke(reason);
        ElementalReactionStateEvents.RaiseStatesCleared(this, reason);
    }
    internal bool AdvanceScheduledStates(float now, ref int remainingTickBudget)
    {
        using var costScope = ElementCombatCostMarkers.Status_Advance.Auto();
        if (advancing) return state.HasAny || staggerUntil > now;
        advancing = true;
        try
        {
            while (remainingTickBudget > 0 && state.TryTakeTick(now, out WeaponElement element, out int count))
            {
                remainingTickBudget--;
                var owner = owners[OverburstElementRules.Index(element)];
                float damage = owner.ActualDirectDamage * count * OverburstElementTuning.Current.TickCoefficient(element);
                float before = combatHealth.CurrentHp;
                int life = LifecycleVersion;
                combatHealth.TakeDamage(new DamageInfo(damage, transform.position, owner.SourceActor,
                    triggersOnHitEffects: false, isDamageOverTime: true, suppressDefaultHitVfx: true,
                    element: element, sourceWeaponRuntimeInstanceId: owner.SourceWeaponRuntimeInstanceId,
                    playerAttackKind: PlayerAttackKind.Elemental, usesResolvedTickDamage: true));
                DeliveredTickCount++;
                if (life != LifecycleVersion || combatHealth.IsDead || !isActiveAndEnabled) break;
                if (element == WeaponElement.Electric && combatHealth.CurrentHp < before)
                {
                    EnemyGradeType grade = enemyRank != null ? enemyRank.GradeType : EnemyGradeType.Normal;
                    float factor = grade == EnemyGradeType.Boss ? 0f : grade == EnemyGradeType.GreaterElite ? .25f : grade == EnemyGradeType.Elite ? .5f : 1f;
                    var tuning = OverburstElementTuning.Current;
                    staggerUntil = Mathf.Max(staggerUntil, now + (tuning.shockStaggerBase + tuning.shockStaggerPerStack * (count - 1)) * factor);
                    aura?.SetAuraActive(MeleeElementStatusAuraType.Shocked, true, true);
                }
            }
            ExpireAndRefresh(now);
        }
        finally { advancing = false; }
        return state.HasAny || staggerUntil > now;
    }
    private void Advance(float now)
    {
        if (advancing) return;
        int budget = 64;
        AdvanceScheduledStates(now, ref budget);
    }
    private void ExpireAndRefresh(float now)
    {
        state.Expire(now, out int expiredMask);
        RefreshControl(now);
        for (int i = 0; i < OverburstElementRules.Count; i++)
            if ((expiredMask & (1 << i)) != 0)
            {
                owners[i] = default;
                StatusRemoved?.Invoke(OverburstElementRules.At(i), ElementalStatusRemoveReason.Expired);
            }
        if (!state.HasAny && staggerUntil <= now) ElementalStatusScheduler.Unregister(this);
    }
    private void RefreshControl() => RefreshControl(Time.time);
    private void RefreshControl(float now)
    {
        using var costScope = ElementCombatCostMarkers.Status_Control.Auto();
        bool frozen = state.IsFrozen(now);
        EnemyGradeType grade = enemyRank != null ? enemyRank.GradeType : EnemyGradeType.Normal;
        bool immune = freezeControlImmune || grade != EnemyGradeType.Normal;
        int cold = state.RawCount(WeaponElement.Ice);
        float slow = cold * (grade == EnemyGradeType.Normal ? .04f : grade == EnemyGradeType.Boss ? .01f : .02f);
        float speed = (frozen && !immune) || staggerUntil > now ? 0f : 1f - slow;
        if (!Mathf.Approximately(MoveSpeedMultiplier, speed))
        {
            MoveSpeedMultiplier = speed;
            enemyMovement?.SetStatusMoveSpeedMultiplier(speed);
            enemyAttackController?.SetStatusActionSpeedMultiplier(speed);
            if (speed <= 0f) enemyAbilities?.Cancel();
        }
        tint?.SetElementColdStacks(cold);
        if (aura != null)
        {
            aura.SetStackCount(MeleeElementStatusAuraType.Burning,state.RawCount(WeaponElement.Fire));
            aura.SetStackCount(MeleeElementStatusAuraType.Shocked,state.RawCount(WeaponElement.Electric));
        }
        if (frozenPresentation == frozen) return;
        frozenPresentation = frozen;
        if (frozen)
        {
            var snapshot = FreezeSnapshot();
            ReactionStateChanged?.Invoke(snapshot, ElementalReactionStateChangeReason.Applied);
            ElementalReactionStateEvents.RaiseStateChanged(this, snapshot, ElementalReactionStateChangeReason.Applied);
        }
        else
        {
            ReactionStateRemoved?.Invoke(ElementalReactionType.Freeze, ElementalReactionStateRemoveReason.Cleared);
            ElementalReactionStateEvents.RaiseStateRemoved(this, ElementalReactionType.Freeze, ElementalReactionStateRemoveReason.Cleared);
        }
    }
    private ElementalReactionStateSnapshot FreezeSnapshot() => new ElementalReactionStateSnapshot(
        ElementalReactionType.Freeze, true, state.FrozenRemaining(Time.time), 1f, default, 0f, MoveSpeedMultiplier, ActionSpeedMultiplier);
    // Retired combination API remains inert for serialized/editor compatibility. Freeze is produced only by cold buildup.
    public bool TryApplyReactionState(ElementalReactionStateApplication application) => false;
    public void ResolvePendingReactionResults(ElementalApplicationContext context) { }
    public bool TryGetReactionState(ElementalReactionType type, out ElementalReactionStateSnapshot snapshot)
    {
        Advance(Time.time);
        bool active = type == ElementalReactionType.Freeze && state.IsFrozen(Time.time);
        snapshot = active ? FreezeSnapshot() : default;
        return active;
    }
    public void ClearAllReactionStates(ElementalStatusClearReason reason = ElementalStatusClearReason.Explicit)
    { ConsumeForDischarge(WeaponElement.Ice, out _); ReactionStatesCleared?.Invoke(reason); }
#if UNITY_EDITOR
    public void AdvanceReactionStatesForValidation(float now) => Advance(now);
#endif
}
