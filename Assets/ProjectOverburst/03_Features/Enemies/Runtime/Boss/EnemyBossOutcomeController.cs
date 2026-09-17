using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyBossPhaseController))]
public sealed class EnemyBossOutcomeController : MonoBehaviour
{
    [SerializeField] private EnemyBossPhaseController boss;
    [SerializeField] private string rewardProfileId;

    private bool subscribed;

    public static event Action<
        EnemyBossOutcomeController,
        EnemyBossPhaseController,
        string> RewardRequested;
    public static event Action<
        EnemyBossOutcomeController,
        EnemyBossPhaseController> EncounterCleared;

    public EnemyBossPhaseController Boss => boss;
    public string RewardProfileId => rewardProfileId;
    public bool IsOutcomeDispatched { get; private set; }

    private void Awake()
    {
        ResolveBoss();
    }

    private void OnEnable()
    {
        IsOutcomeDispatched = false;
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void Configure(
        EnemyBossPhaseController configuredBoss,
        string configuredRewardProfileId)
    {
        Unsubscribe();
        boss = configuredBoss;
        rewardProfileId = configuredRewardProfileId != null
            ? configuredRewardProfileId.Trim()
            : string.Empty;
        ResolveBoss();
        Subscribe();
    }

    public void ResetForPool()
    {
        IsOutcomeDispatched = false;
    }

    private void HandleDefeated(
        EnemyBossPhaseController defeatedBoss,
        DamageInfo info)
    {
        if (IsOutcomeDispatched || defeatedBoss == null || defeatedBoss != boss)
            return;

        IsOutcomeDispatched = true;
        RewardRequested?.Invoke(this, defeatedBoss, rewardProfileId);
        EncounterCleared?.Invoke(this, defeatedBoss);
    }

    private void ResolveBoss()
    {
        if (boss == null)
            boss = GetComponent<EnemyBossPhaseController>();
    }

    private void Subscribe()
    {
        ResolveBoss();
        if (subscribed || boss == null)
            return;

        boss.Defeated += HandleDefeated;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || boss == null)
            return;

        boss.Defeated -= HandleDefeated;
        subscribed = false;
    }
}
