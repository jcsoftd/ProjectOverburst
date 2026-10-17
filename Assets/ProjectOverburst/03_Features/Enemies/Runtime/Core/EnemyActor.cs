using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyActor : MonoBehaviour
{
    private static readonly Unity.Profiling.ProfilerMarker PrepareReferencesMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.References");
    private static readonly Unity.Profiling.ProfilerMarker PrepareIdentityMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.Identity");
    private static readonly Unity.Profiling.ProfilerMarker PrepareBodyMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.Body");
    private static readonly Unity.Profiling.ProfilerMarker PrepareTintMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.Tint");
    private static readonly Unity.Profiling.ProfilerMarker PrepareAnimatorMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.Animator");
    private static readonly Unity.Profiling.ProfilerMarker PrepareHealthMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.Health");
    private static readonly Unity.Profiling.ProfilerMarker PrepareMovementMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.Movement");
    private static readonly Unity.Profiling.ProfilerMarker PrepareAbilitiesMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.Abilities");
    private static readonly Unity.Profiling.ProfilerMarker PrepareAIMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Actor.AI");
    private static readonly Unity.Profiling.ProfilerMarker TintCreateMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Tint.CreateBlock");
    private static readonly Unity.Profiling.ProfilerMarker TintMaterialsMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Tint.Materials");
    private static readonly Unity.Profiling.ProfilerMarker TintGetBlockMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Tint.GetBlock");
    private static readonly Unity.Profiling.ProfilerMarker TintColorMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Tint.Color");
    private static readonly Unity.Profiling.ProfilerMarker TintSetBlockMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Tint.SetBlock");
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
    private readonly List<Material> materialBuffer = new List<Material>();
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
    float progressBaseMaxHp;
    public float NaturalProgressMultiplier { get; private set; } = 1f;
    public EnemyRuntimeStats RuntimeStats => NaturalProgressMultiplier == 1f ? runtimeStats : new EnemyRuntimeStats(
        runtimeStats.MaxHealth * NaturalProgressMultiplier, runtimeStats.DamageMultiplier * NaturalProgressMultiplier,
        runtimeStats.MoveSpeedMultiplier, runtimeStats.AttackSpeedMultiplier, runtimeStats.VisualScale,
        runtimeStats.CollisionScale, runtimeStats.AnchorScale, runtimeStats.Tint, runtimeStats.AttackInterval);

    public void ApplyNaturalProgress(float multiplier)
    {
        if (!health || health.IsDead) return;
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 1f || multiplier > 1.15001f)
            throw new System.ArgumentOutOfRangeException(nameof(multiplier));
        if (progressBaseMaxHp <= 0) progressBaseMaxHp = health.MaxHp;
        if (NaturalProgressMultiplier == multiplier) return;
        float ratio = health.CurrentHp / health.MaxHp;
        NaturalProgressMultiplier = multiplier;
        health.SetMaxHp(progressBaseMaxHp * multiplier, false);
        health.RestoreLivingHealth(ratio * health.MaxHp);
    }
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
        if (Application.isPlaying && !TryGetComponent<EnemyAmbientLight>(out _))
            gameObject.AddComponent<EnemyAmbientLight>();
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
        using (PrepareReferencesMarker.Auto())
        {
            CaptureAuthoredState();
            ResolveOptionalComponents();
            if (!IsAuthoringValid || enemyDefinition == null || !enemyDefinition.IsValid)
                return false;
        }
        using (PrepareIdentityMarker.Auto())
        {
            leased = true;
            LeaseVersion++;
            definition = enemyDefinition;
            runtimeStats = stats;
            NaturalProgressMultiplier = 1f; progressBaseMaxHp = 0f;
            identity?.SetDefinition(enemyDefinition);
            rank?.ConfigureEncounter(request.Encounter);
            rank?.ConfigureFromDefinition(enemyDefinition);
            lootDropper?.ConfigureEncounter(request.Encounter);
        }
        using (PrepareBodyMarker.Auto())
        {
            transform.localScale = Vector3.one; // ActorRoot 크기는 변형에 사용하지 않음
            visualRoot.localScale = stats.VisualScale;
            collisionRoot.localScale = stats.CollisionScale;
            anchors.localScale = stats.AnchorScale;
            RestoreColliderStates();
            if (TryGetComponent(out CombatTarget combatTarget))
            {
                combatTarget.RefreshVolumeFromCollider(
                    collisionRoot.GetComponentInChildren<CapsuleCollider>(true));
                // 몸 부피는 커진 충돌체에서 다시 재지만, 전용 피격 판정과 오라 기준 부피는 크기 1 기준 값이라 겉모습 배율을 따로 넣는다.
                combatTarget.SetVariantHurtScale(stats.VisualScale);
            }
            if (TryGetComponent(out CombatTargetVfxPlacement vfxPlacement))
                vfxPlacement.SetVariantScale(stats.VisualScale);
        }
        using (PrepareTintMarker.Auto())
        {
            ApplyTint(stats.Tint);
        }
        using (PrepareAnimatorMarker.Auto())
        {
            EnemyAnimationProfile animationProfile = enemyDefinition.AnimationProfile;
            if (animator != null && animationProfile != null)
            {
                animator.runtimeAnimatorController = animationProfile.RuntimeController;
                animator.applyRootMotion = false;
            }
        }
        using (PrepareHealthMarker.Auto())
        {
            health.SetMaxHp(stats.MaxHealth, true);
            rank?.ApplyLevelToHealth(stats.MaxHealth);
            health.ResetHealth();
        }
        using (PrepareMovementMarker.Auto())
        {
            movement.enabled = true;
            movement.SetProfile(enemyDefinition.MovementProfile);
            float levelProgress = (OverburstGrowthRules.ClampLevel(rank != null ? rank.Level : 1) - 1) / 99f;
            var balance = OverburstBalanceTable.Current;
            movement.SetRuntimeSpeedMultiplier(Mathf.Min(balance.EnemyMoveCap, stats.MoveSpeedMultiplier * (1f + balance.EnemyMoveGrowth * levelProgress)));
            movement.ResolveReferences();
            movement.StopMovement();
        }
        using (PrepareAbilitiesMarker.Auto())
        {
            animationBridge.enabled = true;
            melee.enabled = true;
            abilityController.enabled = true;
            abilityController.ResetForReuse();
            abilityController.Configure(
                enemyDefinition.AbilitySet,
                stats.DamageMultiplier,
                MapOptionPolicy.AttackSpeedMultiplier(stats, request.Encounter.Map, rank != null ? rank.Level : 1,
                    enemyDefinition.Grade != null && enemyDefinition.Grade.GradeType == EnemyGradeType.Boss),
                enemyDefinition.Grade != null && enemyDefinition.Grade.GradeType == EnemyGradeType.Boss ? 0f : stats.AttackInterval);
            bossOutcomeController?.ResetForPool();
            if (bossPhaseController != null
                && !bossPhaseController.PrepareForLease(this))
            {
                return false;
            }
        }
        using (PrepareAIMarker.Auto())
        {
            ai.enabled = true;
            ai.RefreshCombatRangesFromAbilities();
            ai.SetBehaviorProfile(enemyDefinition.BehaviorProfile);
            ai.SetTacticalProfile(enemyDefinition.TacticalProfile);
            ai.SetSquadPursuitProfile(
                enemyDefinition.SquadPursuitPreset,
                enemyDefinition.SquadParticipationMode);
            ai.SetHomePosition(request.Position);
            ai.SetTarget(request.Target);
            ai.SetSquadEncounter(request.EncounterOwner, request.EncounterAnchor, request.SpawnPackId);
            return true;
        }
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
        NaturalProgressMultiplier = 1f; progressBaseMaxHp = 0f;
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
            using (TintCreateMarker.Auto()) propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer targetRenderer = visualRenderers[i];
            if (targetRenderer == null)
                continue;

            using (TintMaterialsMarker.Auto()) targetRenderer.GetSharedMaterials(materialBuffer);
            for (int materialIndex = 0; materialIndex < materialBuffer.Count; materialIndex++)
            {
                Material material = materialBuffer[materialIndex];
                if (material == null)
                    continue;

                using (TintGetBlockMarker.Auto()) targetRenderer.GetPropertyBlock(propertyBlock, materialIndex);
                using (TintColorMarker.Auto())
                {
                    if (material.HasProperty(BaseColorId))
                        propertyBlock.SetColor(BaseColorId, material.GetColor(BaseColorId) * tint);
                    else if (material.HasProperty(ColorId))
                        propertyBlock.SetColor(ColorId, material.GetColor(ColorId) * tint);
                    else
                    {
                        propertyBlock.Clear();
                        continue;
                    }
                }
                using (TintSetBlockMarker.Auto()) targetRenderer.SetPropertyBlock(propertyBlock, materialIndex);
                propertyBlock.Clear();
            }
        }
        materialBuffer.Clear();
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

            targetRenderer.GetSharedMaterials(materialBuffer);
            for (int materialIndex = 0; materialIndex < materialBuffer.Count; materialIndex++)
                targetRenderer.SetPropertyBlock(null, materialIndex);
            targetRenderer.SetPropertyBlock(null);
        }
        materialBuffer.Clear();
    }
}
