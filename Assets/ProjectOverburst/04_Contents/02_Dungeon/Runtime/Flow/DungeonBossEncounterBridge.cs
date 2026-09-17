using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonBossEncounterBridge : MonoBehaviour
{
    [SerializeField] private DungeonRunFlow runFlow;

    private EnemyBossPhaseController activeBoss;

    public event Action<
        DungeonBossEncounterBridge,
        EnemyBossPhaseController,
        string> RewardRequested;
    public event Action<
        DungeonBossEncounterBridge,
        EnemyBossPhaseController> RoomCleared;
    public event Action<DungeonBossEncounterBridge> ExitUnlocked;

    public DungeonRunFlow RunFlow => runFlow;
    public EnemyBossPhaseController ActiveBoss => activeBoss;
    public bool IsBossEncounterActive { get; private set; }
    public bool IsRewardRequested { get; private set; }
    public bool IsRoomCleared { get; private set; }
    public bool IsExitUnlocked { get; private set; } = true;

    private void Awake()
    {
        ResolveRunFlow();
    }

    private void OnEnable()
    {
        ResolveRunFlow();
        EnemyBossEncounterRegistry.EncounterStarted += HandleEncounterStarted;
        EnemyBossEncounterRegistry.EncounterEnded += HandleEncounterEnded;
        EnemyBossOutcomeController.RewardRequested += HandleRewardRequested;
        EnemyBossOutcomeController.EncounterCleared += HandleEncounterCleared;
        if (runFlow != null)
            runFlow.ExitPortalCreated += HandleExitPortalCreated;

        EnemyBossPhaseController current =
            EnemyBossEncounterRegistry.Current;
        if (current != null)
            HandleEncounterStarted(current);
    }

    private void OnDisable()
    {
        EnemyBossEncounterRegistry.EncounterStarted -= HandleEncounterStarted;
        EnemyBossEncounterRegistry.EncounterEnded -= HandleEncounterEnded;
        EnemyBossOutcomeController.RewardRequested -= HandleRewardRequested;
        EnemyBossOutcomeController.EncounterCleared -= HandleEncounterCleared;
        if (runFlow != null)
            runFlow.ExitPortalCreated -= HandleExitPortalCreated;
    }

    public void Configure(DungeonRunFlow configuredRunFlow)
    {
        if (isActiveAndEnabled && runFlow != null)
            runFlow.ExitPortalCreated -= HandleExitPortalCreated;

        runFlow = configuredRunFlow;

        if (isActiveAndEnabled && runFlow != null)
            runFlow.ExitPortalCreated += HandleExitPortalCreated;
    }

    private void HandleEncounterStarted(EnemyBossPhaseController boss)
    {
        if (boss == null)
            return;

        activeBoss = boss;
        IsBossEncounterActive = true;
        IsRewardRequested = false;
        IsRoomCleared = false;
        SetExitUnlocked(false);
    }

    private void HandleRewardRequested(
        EnemyBossOutcomeController outcome,
        EnemyBossPhaseController boss,
        string rewardProfileId)
    {
        if (boss == null || boss != activeBoss || IsRewardRequested)
            return;

        IsRewardRequested = true;
        RewardRequested?.Invoke(this, boss, rewardProfileId);
    }

    private void HandleEncounterCleared(
        EnemyBossOutcomeController outcome,
        EnemyBossPhaseController boss)
    {
        if (boss == null || boss != activeBoss || IsRoomCleared)
            return;

        IsRoomCleared = true;
        RoomCleared?.Invoke(this, boss);
        SetExitUnlocked(true);
    }

    private void HandleEncounterEnded(EnemyBossPhaseController boss)
    {
        if (boss != activeBoss)
            return;

        IsBossEncounterActive = false;
        activeBoss = null;
    }

    private void HandleExitPortalCreated(DungeonPortalExit portal)
    {
        ApplyExitState(portal);
    }

    private void SetExitUnlocked(bool unlocked)
    {
        bool changed = IsExitUnlocked != unlocked;
        IsExitUnlocked = unlocked;
        ApplyExitState(runFlow != null ? runFlow.ActiveExitPortal : null);
        if (changed && unlocked)
            ExitUnlocked?.Invoke(this);
    }

    private void ApplyExitState(DungeonPortalExit portal)
    {
        if (portal != null)
            portal.SetInteractionEnabled(IsExitUnlocked);
    }

    private void ResolveRunFlow()
    {
        if (runFlow == null)
            runFlow = GetComponent<DungeonRunFlow>();
    }
}
