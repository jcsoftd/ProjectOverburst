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
    [SerializeField] private MeleeElementStatusAuraController auraController;
    [SerializeField] private bool freezeControlImmune;
    private readonly OverburstElementState state = new OverburstElementState();
    private readonly ElementalStatusOwnerSnapshot[] owners = new ElementalStatusOwnerSnapshot[4];
    private readonly HashSet<(int, int)> hits = new HashSet<(int, int)>();
    private readonly Queue<(int, int)> hitOrder = new Queue<(int, int)>();
    private bool frozenPresentation;
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
        if (auraController == null) auraController = GetComponent<MeleeElementStatusAuraController>();
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
        if (!isActiveAndEnabled || combatHealth == null || combatHealth.IsDead || combatHealth.CurrentHp <= 0f
            || !OverburstElementTuning.IsFinitePositive(application.ActualDirectDamage)
            || !application.TriggersOnHitEffects || application.IsDamageOverTime) return false;
        Advance(Time.time);
        float freezeMultiplier = enemyRank != null && enemyRank.GradeType != EnemyGradeType.Normal ? 1f
            : 1f + FlaskCombatModifiers.Bonus(application.SourceActor, FlaskEffect.FreezeDuration);
        if (!state.Add(application.Element, Time.time, OverburstElementTuning.Current, freezeMultiplier)) return false;
        owners[OverburstElementRules.Index(application.Element)] = new ElementalStatusOwnerSnapshot(application);
        RefreshControl();
        RefreshAuras();
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
        Advance(Time.time);
        int count = state.Consume(element, Time.time, out shattered);
        if (count <= 0) return 0;
        owners[OverburstElementRules.Index(element)] = default;
        RefreshControl(); RefreshAuras();
        StatusRemoved?.Invoke(element, ElementalStatusRemoveReason.Cleared);
        if (!state.HasAny) ElementalStatusScheduler.Unregister(this);
        return count;
    }
    public void ClearAllStatuses(ElementalStatusClearReason reason = ElementalStatusClearReason.Explicit)
    {
        if (reason == ElementalStatusClearReason.Reset || reason == ElementalStatusClearReason.Disabled) LifecycleVersion++;
        state.Clear(); Array.Clear(owners, 0, owners.Length); hits.Clear(); hitOrder.Clear();
        RefreshControl(); auraController?.ClearAllAuras();
        ElementalStatusScheduler.Unregister(this);
        StatusesCleared?.Invoke(reason);
        ReactionStatesCleared?.Invoke(reason);
    }
    internal bool AdvanceScheduledStates(float now, ref int remainingTickBudget)
    { Advance(now); return state.HasAny; }
    private void Advance(float now)
    {
        if (!state.Expire(now)) return;
        RefreshControl(); RefreshAuras();
        for (int i = 0; i < 4; i++)
            if (state.Count(OverburstElementRules.At(i), now) == 0)
            {
                owners[i] = default;
                StatusRemoved?.Invoke(OverburstElementRules.At(i), ElementalStatusRemoveReason.Expired);
            }
        if (!state.HasAny) ElementalStatusScheduler.Unregister(this);
    }
    private void RefreshControl()
    {
        bool frozen = state.IsFrozen(Time.time);
        // Boss controllers can opt out of locomotion control while retaining shatter eligibility.
        bool immune = freezeControlImmune || (enemyRank != null && enemyRank.GradeType == EnemyGradeType.Boss);
        MoveSpeedMultiplier = frozen && !immune ? 0f : 1f;
        enemyMovement?.SetStatusMoveSpeedMultiplier(MoveSpeedMultiplier);
        enemyAttackController?.SetStatusActionSpeedMultiplier(MoveSpeedMultiplier);
        if (frozenPresentation == frozen) return;
        frozenPresentation = frozen;
        if (frozen)
        {
            var snapshot = FreezeSnapshot();
            ReactionStateChanged?.Invoke(snapshot, ElementalReactionStateChangeReason.Applied);
        }
        else ReactionStateRemoved?.Invoke(ElementalReactionType.Freeze, ElementalReactionStateRemoveReason.Cleared);
    }
    private void RefreshAuras()
    {
        if (auraController == null) return;
        for (int i = 0; i < 4; i++)
        {
            var aura = i == 0 ? MeleeElementStatusAuraType.Burning : i == 1 ? MeleeElementStatusAuraType.Chilled
                : i == 2 ? MeleeElementStatusAuraType.Shocked : MeleeElementStatusAuraType.Wet;
            if (state.Count(OverburstElementRules.At(i), Time.time) > 0) auraController.StartAura(aura);
            else auraController.ClearAura(aura);
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
