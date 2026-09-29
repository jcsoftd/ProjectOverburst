using UnityEngine;

// Keeps one leased monster at a Hideout training station. The normal AI is
// disabled, while the original attack executor and parry contract stay active.
[DisallowMultipleComponent]
public sealed class HideoutParryPracticeStation : MonoBehaviour
{
    [SerializeField] private EnemyDefinition definition;
    [SerializeField] private EnemyAbilityDefinition strongAbility;
    [SerializeField] private EnemyPoolService pool;
    [SerializeField] private Transform spawnPoint;
    [SerializeField, Min(1f)] private float trainingHealth = 10000000f;
    [SerializeField, Range(.01f, 1f)] private float damageMultiplier = .1f;

    private EnemyAbilitySet practiceSet;
    private EnemyActor actor;
    private float nextAttemptAt;
    private float nextSpawnAt;
    private float deathObservedAt = -1f;

    public EnemyActor ActiveActor => actor != null && actor.IsLeased ? actor : null;
    public EnemyAbilityDefinition StrongAbility => strongAbility;
    public int StartedAttackCount { get; private set; }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (!IsConfigured())
        {
            Debug.LogError("[HideoutParryPracticeStation] 훈련용 몬스터 설정이 유효하지 않습니다.", this);
            enabled = false;
            return;
        }

        practiceSet = ScriptableObject.CreateInstance<EnemyAbilitySet>();
        practiceSet.name = "HideoutPractice_" + definition.EnemyId;
        practiceSet.hideFlags = HideFlags.DontSave;
        practiceSet.Configure(practiceSet.name, new[] { strongAbility });
        StartedAttackCount = 0;
        nextAttemptAt = 0f;
        nextSpawnAt = 0f;
        deathObservedAt = -1f;
    }

    private void OnDisable()
    {
        if (actor != null && actor.IsLeased && pool != null)
            pool.Release(actor);
        actor = null;
        if (practiceSet != null)
            Destroy(practiceSet);
        practiceSet = null;
    }

    private bool IsConfigured()
    {
        if (definition == null || !definition.IsValid || strongAbility == null
            || !strongAbility.IsValid || !strongAbility.IsTelegraphedStrongAttack
            || !strongAbility.IsParryable || pool == null || !pool.IsAuthoringValid
            || spawnPoint == null)
            return false;

        EnemyAbilitySet original = definition.AbilitySet;
        for (int i = 0; original != null && i < original.Count; i++)
            if (original.GetAbility(i) == strongAbility) return true;
        return false;
    }

    private void Update()
    {
        if (practiceSet == null || Time.time < nextAttemptAt) return;
        nextAttemptAt = Time.time + .12f;

        PlayerActorRuntime player = PlayerContext.Instance != null
            ? PlayerContext.Instance.CurrentActor : null;
        if (player == null || !player.isActiveAndEnabled) return;
        CombatTarget playerTarget = player.GetComponent<CombatTarget>();
        if (playerTarget == null || !playerTarget.IsAlive) return;

        if (actor == null || !actor.IsLeased || !actor.gameObject.activeInHierarchy)
        {
            if (Time.time >= nextSpawnAt) TrySpawn(player.transform);
            return;
        }

        if (actor.Health == null || actor.Health.IsDead)
        {
            if (deathObservedAt < 0f) deathObservedAt = Time.time;
            if (Time.time - deathObservedAt >= 2f)
            {
                pool.Release(actor);
                actor = null;
                nextSpawnAt = Time.time + 1f;
                deathObservedAt = -1f;
            }
            return;
        }
        deathObservedAt = -1f;

        Vector3 delta = player.transform.position - actor.transform.position;
        delta.y = 0f;
        float turnRange = strongAbility.Range + .5f;
        if (delta.sqrMagnitude > turnRange * turnRange || actor.AbilityController.IsExecuting)
            return;

        actor.Movement.FacePosition(player.transform.position);
        if (delta.sqrMagnitude <= strongAbility.Range * strongAbility.Range
            && actor.AbilityController.HasAvailableAbility(player.transform)
            && actor.AbilityController.TryStart(player.transform))
            StartedAttackCount++;
    }

    private void TrySpawn(Transform player)
    {
        nextSpawnAt = Time.time + 2f;
        EnemyActor spawned = pool.Acquire(definition);
        if (spawned == null) return;

        Vector3 position = spawnPoint.position;
        Quaternion rotation = spawnPoint.rotation;
        spawned.transform.SetParent(transform, false);
        spawned.transform.SetPositionAndRotation(position, rotation);
        spawned.transform.localScale = Vector3.one;

        var request = new EnemySpawnRequest(definition, position, rotation,
            player, gameObject, spawnPoint, transform);
        if (!spawned.PrepareForLease(definition, definition.ResolveRuntimeStats(), request))
        {
            pool.Release(spawned);
            return;
        }

        spawned.ConfigureRunFallGuard(position);
        spawned.gameObject.SetActive(true);
        if (!spawned.FinalizeLeaseAfterActivation())
        {
            pool.Release(spawned);
            return;
        }

        spawned.AI.enabled = false;
        Disable<EnemySensor>(spawned);
        Disable<EnemyCrowdAgent>(spawned);
        Disable<EnemyThemeSpecialExecutor>(spawned);
        Disable<EnemyLootDropper>(spawned);
        spawned.Movement.StopMovement();
        spawned.Health.SetMaxHp(trainingHealth, true);
        spawned.Health.ResetHealth();
        spawned.AbilityController.Configure(practiceSet, damageMultiplier, 1f);
        spawned.name = "ParryPractice_" + definition.EnemyId;
        actor = spawned;
    }

    private static void Disable<T>(EnemyActor owner) where T : Behaviour
    {
        T component = owner.GetComponent<T>();
        if (component != null) component.enabled = false;
    }
}
