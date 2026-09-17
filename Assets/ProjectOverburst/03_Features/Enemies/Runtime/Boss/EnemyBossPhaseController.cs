using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyBossPhaseController : MonoBehaviour
{
    [SerializeField] private EnemyBossDefinition bossDefinition;
    [SerializeField] private EnemyActor actor;
    [SerializeField] private CombatHealth health;
    [SerializeField] private EnemyAbilityController abilityController;

    private bool subscribed;

    public event Action<EnemyBossPhaseController> EncounterStarted;
    public event Action<EnemyBossPhaseController, int, int> PhaseChanged;
    public event Action<EnemyBossPhaseController, DamageInfo> Defeated;

    public EnemyBossDefinition BossDefinition => bossDefinition;
    public CombatHealth Health => health;
    public EnemyActor Actor => actor;
    public bool IsEncounterActive { get; private set; }
    public bool IsDefeated { get; private set; }
    public int CurrentPhaseIndex { get; private set; } = -1;
    public EnemyBossPhaseDefinition CurrentPhase =>
        bossDefinition != null
            ? bossDefinition.GetPhase(CurrentPhaseIndex)
            : null;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        EndEncounter();
    }

    public void Configure(
        EnemyBossDefinition definition,
        EnemyActor owner,
        CombatHealth combatHealth,
        EnemyAbilityController abilities)
    {
        bossDefinition = definition;
        actor = owner;
        health = combatHealth;
        abilityController = abilities;
        ResolveReferences();
    }

    public bool PrepareForLease(EnemyActor owner)
    {
        if (owner != null)
            actor = owner;
        ResolveReferences();
        EndEncounter();
        IsDefeated = false;
        CurrentPhaseIndex = -1;
        return bossDefinition != null
            && bossDefinition.IsValid
            && actor != null
            && health != null
            && abilityController != null;
    }

    public bool BeginEncounterAfterActivation()
    {
        ResolveReferences();
        Subscribe();
        if (IsEncounterActive
            || bossDefinition == null
            || !bossDefinition.IsValid
            || actor == null
            || health == null
            || abilityController == null
            || health.IsDead)
        {
            return false;
        }

        if (!ApplyResolvedPhase(true))
            return false;

        IsEncounterActive = true;
        EnemyBossEncounterRegistry.Register(this);
        EncounterStarted?.Invoke(this);
        return true;
    }

    public void ResetForPool()
    {
        EndEncounter();
        IsDefeated = false;
        CurrentPhaseIndex = -1;
    }

    private void HandleHealthChanged(
        CombatHealth source,
        float currentHealth,
        float maximumHealth)
    {
        if (!IsEncounterActive || IsDefeated || source != health)
            return;

        ApplyResolvedPhase(false);
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (!IsEncounterActive || IsDefeated || source != health)
            return;

        IsDefeated = true;
        abilityController?.Cancel();
        Defeated?.Invoke(this, info);
        EnemyBossEncounterRegistry.NotifyDefeated(this);
    }

    private bool ApplyResolvedPhase(bool force)
    {
        int nextPhase = bossDefinition.ResolvePhaseIndex(
            health != null ? health.NormalizedHp : 1f);
        if (nextPhase < 0)
            return false;
        if (!force && nextPhase == CurrentPhaseIndex)
            return true;

        EnemyBossPhaseDefinition phase =
            bossDefinition.GetPhase(nextPhase);
        if (phase == null || !phase.IsValid)
            return false;

        int previousPhase = CurrentPhaseIndex;
        CurrentPhaseIndex = nextPhase;
        EnemyRuntimeStats stats = actor.RuntimeStats;
        abilityController.Configure(
            phase.AbilitySet,
            stats.DamageMultiplier,
            stats.AttackSpeedMultiplier);
        if (previousPhase >= 0)
        {
            PhaseChanged?.Invoke(this, previousPhase, nextPhase);
            EnemyBossEncounterRegistry.NotifyPhaseChanged(
                this,
                previousPhase,
                nextPhase);
        }

        return true;
    }

    private void EndEncounter()
    {
        if (!IsEncounterActive)
            return;

        IsEncounterActive = false;
        EnemyBossEncounterRegistry.Unregister(this);
    }

    private void ResolveReferences()
    {
        if (actor == null)
            actor = GetComponent<EnemyActor>();
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (abilityController == null)
            abilityController = GetComponent<EnemyAbilityController>();
    }

    private void Subscribe()
    {
        if (subscribed || health == null)
            return;

        health.OnHealthChanged += HandleHealthChanged;
        health.OnDead += HandleDead;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || health == null)
            return;

        health.OnHealthChanged -= HandleHealthChanged;
        health.OnDead -= HandleDead;
        subscribed = false;
    }
}
