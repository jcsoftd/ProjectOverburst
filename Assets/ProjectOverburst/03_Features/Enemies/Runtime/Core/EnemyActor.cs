using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyActor : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Definition")]
    [SerializeField] private EnemyDefinition definition;
    [SerializeField] private EnemyIdentity identity;

    [Header("Roots")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform collisionRoot;
    [SerializeField] private Transform anchors;

    [Header("Runtime")]
    [SerializeField] private CombatHealth health;
    [SerializeField] private EnemyMovement movement;
    [SerializeField] private EnemyAIController ai;
    [SerializeField] private EnemyAbilityController abilityController;
    [SerializeField] private EnemyMeleeAttackController melee;
    [SerializeField] private EnemyAnimationBridge animationBridge;
    [SerializeField] private Animator animator;
    [SerializeField] private RunFallGuard runFallGuard;
    [SerializeField] private EnemyBossPhaseController bossPhaseController;
    [SerializeField] private EnemyBossOutcomeController bossOutcomeController;

    private MaterialPropertyBlock propertyBlock;
    private Renderer[] visualRenderers;
    private Collider[] authoredColliders;
    private bool[] authoredColliderEnabled;
    private EnemyPoolService poolOwner;
    private EnemyActor poolPrefabKey;
    private EnemyRuntimeStats runtimeStats;
    private bool authoredStateCaptured;
    private bool leased;
    private EnemyRank rank;
    private EnemyLootDropper lootDropper;
    private CombatVfx combatVfx;

    public EnemyDefinition Definition => definition;
    public EnemyRuntimeStats RuntimeStats => runtimeStats;
    public EnemyIdentity Identity => identity;
    public Transform VisualRoot => visualRoot;
    public Transform CollisionRoot => collisionRoot;
    public Transform Anchors => anchors;
    public CombatHealth Health => health;
    public EnemyMovement Movement => movement;
    public EnemyAIController AI => ai;
    public EnemyAbilityController AbilityController => abilityController;
    public EnemyMeleeAttackController Melee => melee;
    public EnemyAnimationBridge AnimationBridge => animationBridge;
    public Animator Animator => animator;
    public RunFallGuard RunFallGuard => runFallGuard;
    public EnemyBossPhaseController BossPhaseController => bossPhaseController;
    public EnemyBossOutcomeController BossOutcomeController =>
        bossOutcomeController;
    public bool IsLeased => leased;
    public uint LeaseVersion { get; private set; }
    public bool IsAuthoringValid => transform.localScale == Vector3.one
        && visualRoot != null
        && collisionRoot != null
        && anchors != null
        && health != null
        && movement != null
        && ai != null
        && abilityController != null
        && melee != null
        && animationBridge != null
        && animator != null
        && runFallGuard != null;

    private void Awake()
    {
        CaptureAuthoredState();
    }

    private void OnDisable()
    {
        EnemyFootfallRuntime.Unregister(this);
        if (leased && poolOwner != null)
            poolOwner.NotifyActorDisabled(this);
    }

    public void ConfigureReferences(
        EnemyIdentity enemyIdentity,
        Transform enemyVisualRoot,
        Transform enemyCollisionRoot,
        Transform enemyAnchors,
        CombatHealth combatHealth,
        EnemyMovement enemyMovement,
        EnemyAIController aiController,
        EnemyAbilityController enemyAbilityController,
        EnemyMeleeAttackController meleeController,
        EnemyAnimationBridge bridge,
        Animator enemyAnimator,
        RunFallGuard fallGuard)
    {
        identity = enemyIdentity;
        visualRoot = enemyVisualRoot;
        collisionRoot = enemyCollisionRoot;
        anchors = enemyAnchors;
        health = combatHealth;
        movement = enemyMovement;
        ai = aiController;
        abilityController = enemyAbilityController;
        melee = meleeController;
        animationBridge = bridge;
        animator = enemyAnimator;
        runFallGuard = fallGuard;
        authoredStateCaptured = false;
        CaptureAuthoredState();
    }

    public bool Validate(out string message)
    {
        if (!IsAuthoringValid)
        {
            message = $"{name}: EnemyActor 필수 참조 또는 Root Scale 계약이 유효하지 않습니다.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    internal bool PrepareForLease(
        EnemyDefinition enemyDefinition,
        EnemyRuntimeStats stats,
        in EnemySpawnRequest request)
    {
        CaptureAuthoredState();
        ResolveOptionalComponents();
        if (!IsAuthoringValid || enemyDefinition == null || !enemyDefinition.IsValid)
            return false;

        leased = true;
        LeaseVersion++;
        definition = enemyDefinition;
        runtimeStats = stats;
        identity?.SetDefinition(enemyDefinition);
        rank?.ConfigureEncounter(request.Encounter);
        rank?.ConfigureFromDefinition(enemyDefinition);
        lootDropper?.ConfigureEncounter(request.Encounter);

        transform.localScale = Vector3.one; // ActorRoot 크기는 변형에 사용하지 않음
        visualRoot.localScale = stats.VisualScale;
        collisionRoot.localScale = stats.CollisionScale;
        anchors.localScale = stats.AnchorScale;
        RestoreColliderStates();
        GetComponent<CombatTarget>()?.RefreshVolumeFromCollider(
            collisionRoot.GetComponentInChildren<CapsuleCollider>(true));
        ApplyTint(stats.Tint);

        EnemyAnimationProfile animationProfile = enemyDefinition.AnimationProfile;
        if (animator != null && animationProfile != null)
        {
            animator.runtimeAnimatorController = animationProfile.RuntimeController;
            animator.applyRootMotion = false;
        }

        health.SetMaxHp(stats.MaxHealth, true);
        rank?.ApplyLevelToHealth(stats.MaxHealth);
        health.ResetHealth();
        movement.enabled = true;
        movement.SetProfile(enemyDefinition.MovementProfile);
        movement.SetRuntimeSpeedMultiplier(stats.MoveSpeedMultiplier);
        movement.ResolveReferences();
        movement.StopMovement();

        animationBridge.enabled = true;
        melee.enabled = true;
        abilityController.enabled = true;
        abilityController.ResetForReuse();
        abilityController.Configure(
            enemyDefinition.AbilitySet,
            stats.DamageMultiplier,
            stats.AttackSpeedMultiplier);
        bossOutcomeController?.ResetForPool();
        if (bossPhaseController != null
            && !bossPhaseController.PrepareForLease(this))
        {
            return false;
        }

        ai.enabled = true;
        ai.RefreshCombatRangesFromAbilities();
        ai.SetBehaviorProfile(enemyDefinition.BehaviorProfile);
        ai.SetTacticalProfile(enemyDefinition.TacticalProfile);
        ai.SetSquadPursuitProfile(
            enemyDefinition.SquadPursuitPreset,
            enemyDefinition.SquadParticipationMode);
        ai.SetHomePosition(request.Position);
        ai.SetTarget(request.Target);
        ai.SetSquadEncounter(request.EncounterOwner, request.EncounterAnchor);
        return true;
    }

    internal bool FinalizeLeaseAfterActivation()
    {
        if (!gameObject.activeInHierarchy || animator == null || !animator.isActiveAndEnabled)
            return false;

        animationBridge.ResetForReuse();
        if (bossPhaseController != null
            && !bossPhaseController.BeginEncounterAfterActivation())
        {
            return false;
        }
        EnemyFootfallRuntime.Register(this);
        return true;
    }

    public void ResetForPool()
    {
        EnemyFootfallRuntime.Unregister(this);
        CaptureAuthoredState();
        ResolveOptionalComponents();
        ai?.SetTarget(null);
        ai?.SetSquadEncounter(null, null);
        bossPhaseController?.ResetForPool();
        bossOutcomeController?.ResetForPool();
        abilityController?.ResetForReuse();
        movement?.CancelActionLock();
        movement?.StopMovement();
        movement?.SetStatusMoveSpeedMultiplier(1f);
        movement?.SetEarthZoneMoveSpeedMultiplier(1f);
        movement?.SetRuntimeSpeedMultiplier(1f);
        animationBridge?.ResetForReuse();
        health?.ResetHealth();
        ResetRunFallGuard();
        lootDropper?.ResetForPool();
        combatVfx?.ResetForPool();
        rank?.ResetForPool();

        if (visualRoot != null)
            visualRoot.localScale = Vector3.one;
        if (collisionRoot != null)
            collisionRoot.localScale = Vector3.one;
        if (anchors != null)
            anchors.localScale = Vector3.one;

        RestoreColliderStates();
        ClearTint();
        transform.localScale = Vector3.one;
        definition = null;
        identity?.ClearDefinition();
        runtimeStats = default;
        leased = false;
    }

    internal void ConfigureRunFallGuard(Vector3 spawnPosition)
    {
        if (runFallGuard == null)
            return;

        RunWalkableContext.TryConfigureExistingFallGuard(
            runFallGuard,
            RunFallGuardMode.KillOnExit,
            spawnPosition);
    }

    internal void AttachPool(EnemyPoolService owner, EnemyActor prefabKey)
    {
        poolOwner = owner;
        poolPrefabKey = prefabKey;
    }

    public bool RequestPoolRelease()
    {
        if (!leased || poolOwner == null)
            return false;

        poolOwner.Release(this);
        return !leased;
    }

    internal EnemyActor PoolPrefabKey => poolPrefabKey;

    private void ResetRunFallGuard()
    {
        if (runFallGuard == null)
            return;

        runFallGuard.Configure(null, RunFallGuardMode.KillOnExit, Vector3.zero, 0f);
        runFallGuard.enabled = false;
    }

    private void CaptureAuthoredState()
    {
        if (authoredStateCaptured)
            return;

        ResolveOptionalComponents();

        visualRenderers = visualRoot != null
            ? visualRoot.GetComponentsInChildren<Renderer>(true)
            : new Renderer[0];
        authoredColliders = collisionRoot != null
            ? collisionRoot.GetComponentsInChildren<Collider>(true)
            : new Collider[0];
        authoredColliderEnabled = new bool[authoredColliders.Length];
        for (int i = 0; i < authoredColliders.Length; i++)
            authoredColliderEnabled[i] = authoredColliders[i] != null && authoredColliders[i].enabled;

        authoredStateCaptured = true;
    }

    private void ResolveOptionalComponents()
    {
        if (rank == null)
            rank = GetComponent<EnemyRank>();
        if (lootDropper == null)
            lootDropper = GetComponent<EnemyLootDropper>();
        if (combatVfx == null)
            combatVfx = GetComponent<CombatVfx>();
        if (bossPhaseController == null)
            bossPhaseController = GetComponent<EnemyBossPhaseController>();
        if (bossOutcomeController == null)
        {
            bossOutcomeController =
                GetComponent<EnemyBossOutcomeController>();
        }
    }

    private void RestoreColliderStates()
    {
        if (authoredColliders == null || authoredColliderEnabled == null)
            return;

        int count = Mathf.Min(authoredColliders.Length, authoredColliderEnabled.Length);
        for (int i = 0; i < count; i++)
        {
            if (authoredColliders[i] != null)
                authoredColliders[i].enabled = authoredColliderEnabled[i];
        }
    }

    private void ApplyTint(Color tint)
    {
        if (visualRenderers == null)
            return;
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer targetRenderer = visualRenderers[i];
            if (targetRenderer == null)
                continue;

            Material[] materials = targetRenderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                    continue;

                targetRenderer.GetPropertyBlock(propertyBlock, materialIndex);
                if (material.HasProperty(BaseColorId))
                    propertyBlock.SetColor(BaseColorId, material.GetColor(BaseColorId) * tint);
                else if (material.HasProperty(ColorId))
                    propertyBlock.SetColor(ColorId, material.GetColor(ColorId) * tint);
                else
                {
                    propertyBlock.Clear();
                    continue;
                }

                targetRenderer.SetPropertyBlock(propertyBlock, materialIndex);
                propertyBlock.Clear();
            }
        }
    }

    private void ClearTint()
    {
        if (visualRenderers == null)
            return;

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer targetRenderer = visualRenderers[i];
            if (targetRenderer == null)
                continue;

            Material[] materials = targetRenderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                targetRenderer.SetPropertyBlock(null, materialIndex);
            targetRenderer.SetPropertyBlock(null);
        }
    }
}
