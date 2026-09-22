using UnityEngine;

public static class MeleeEvadeCancelPolicy
{
    public const bool ResetComboOnCancel = true;

    public static bool CanCancel(bool isAttackInProgress, bool usesCombo)
    {
        return isAttackInProgress && usesCombo; // 실제 근접 콤보만 전 구간 허용
    }
}

public class MeleeRuntime : MonoBehaviour, IWeaponActionPort // 근접 런타임
{
    private const float DamageOverTimeTickInterval = 1f; // 지속 피해 주기
    private const float MinAttackDuration = 0.2f; // 공격 액션 최소 길이
    private const float MeleeCombatStanceCritChanceBonus = 10f; // 자세 치명 보너스
    private const float MeleeCombatStanceKnockbackMultiplier = 1.5f; // 자세 넉백 배율

    [Header("References")]
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerAnimation playerAnimatorController;
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private Camera attackCamera;
    [SerializeField] private CombatTarget combatTarget;
    [SerializeField] private PlayerActorRuntime playerActorRuntime;

    private WeaponFinalStats activeStats; // 공격 스탯
    private WeaponItemData activeWeaponData; // 공격 무기
    private MeleeComboDefinition activeComboDefinition; // 근접 콤보 정의
    private bool isAttacking; // 공격 중
    private float attackStartTime; // 시작 시간
    private float attackDuration; // 공격 시간
    private int comboStepIndex = -1;
    private float lastComboWindowTime = -999f;
    private bool activeAttackUsesCombo;
    private AnimationClip activeAttackAnimationClip; // 공격 애니
    private float activeAttackAnimationSpeed = 1f; // 공격 애니 속도
    private float activeAttackTransitionDuration; // 공격 전환 보간
    private AttackPhaseData[] activeAttackPhases;
    private readonly AttackPhaseExecutor attackPhaseExecutor = new AttackPhaseExecutor();
    private readonly AttackMovementExecutor attackMovementExecutor = new AttackMovementExecutor();
    private readonly ComboMovementCollisionPusher comboMovementCollisionPusher = new ComboMovementCollisionPusher();
    private readonly AttackTrailExecutor attackTrailExecutor = new AttackTrailExecutor();
    private AttackPatternDebugRenderer attackPatternDebugRenderer;
    private Vector3 activeAttackDirection; // 공격 방향
    private IWeaponTrailController activeAttackTrail;
    private MeleeComboStepData activeAttackStep;
    private WeaponComboGemAttackModifierSnapshot activeComboAttackModifierSnapshot; // 현재 타수 보석값
    private bool hasActiveComboAttackModifierSnapshot; // 현재 타수 조회 완료
    private bool activeAttackUsedMeleeCombatStance; // 전투 자세
    private ItemData activeAttackWeaponItem; // 공격 아이템
    private bool manualInputEnabled = true; // 파티 전환 중 기존 공격은 유지하고 신규 입력만 막는다.
    private bool suppressHandoffMoveCancelUntilRelease; // 인계 전 이동키 무시
    private bool bufferedHandoffComboContinuation; // 교체 프레임 콤보 입력 보존
    private int nextActionId = 1; // 행동 식별자
    private int nextHitFeedbackSequenceId = 1; // 타수별 피드백 식별자
    private int activeHitFeedbackSequenceId; // 현재 타수 피드백 식별자
    private int activeActionId; // 진행 행동
    private int terminalActionId; // 마지막 종료 행동
    private WeaponActionSource activeActionSource; // 행동 요청 원본
    private CombatTarget activeRequestedTarget; // AI 고정 타깃
    private WeaponActionState terminalActionState; // 마지막 종료 결과
    private PlayerInputFacade inputFacade; // GOAL A2 파사드 캐시
    private PlayerStateCoordinator stateCoordinator; // GOAL A2 상태 보고
    private PlayerEvadeController inputEvadeController;

    public WeaponRuntimeKind RuntimeKind => WeaponRuntimeKind.Melee;
    public bool CanUseCurrentWeapon => playerEquipment != null && playerEquipment.CanCurrentWeaponUseMeleeSlash;
    public bool IsBusy => IsAttackInProgress;
    public bool IsReady => IsAttackReady;
    public float CooldownRemaining => 0f;
    public float CooldownProgress01 => 1f;

    public bool IsAttackReady => CanUseCurrentWeapon && !isAttacking;

    public bool IsAttackInProgress
    {
        get { return isAttacking; }
    }

    public bool CanCancelActiveComboForEvade
    {
        get { return MeleeEvadeCancelPolicy.CanCancel(isAttacking, activeAttackUsesCombo); }
    }

    public void SetManualInputEnabled(bool enabledValue)
    {
        manualInputEnabled = enabledValue;
        if (!enabledValue) ResolveFacade()?.CombatInputs?.Invalidate();
    }

    public void CancelCurrentAttackState()
    {
        CancelActiveAttack(WeaponActionCompletionReason.CancelledByRequest, true);
    }

    public void CancelCurrentAction()
    {
        CancelCurrentAction(WeaponActionCancelReason.Request);
    }

    public void CancelCurrentAction(WeaponActionCancelReason reason)
    {
        CancelActiveAttack(ResolveCompletionReason(reason), true);
    }

    public bool TryGetAttackWindow(out WeaponAttackWindow window)
    {
        ResolveReferences();
        window = default;
        if (!CanUseCurrentWeapon || playerEquipment == null)
            return false;

        WeaponItemData weaponData = playerEquipment.CurrentWeaponData;
        MeleeWeaponDefinition meleeDefinition = weaponData != null
            ? weaponData.GetMeleeDefinition()
            : null;
        if (meleeDefinition == null)
            return false;

        WeaponFinalStats stats = FlaskCombatModifiers.Apply(playerEquipment.CurrentWeaponStats, gameObject);
        float maxDistance = Mathf.Max(0.1f, stats.range);
        float verticalTolerance = 3f;
        MeleeComboDefinition comboDefinition = weaponData.GetMeleeComboDefinition();
        if (comboDefinition != null && comboDefinition.HasSteps)
        {
            AttackPhaseData[] phases = comboDefinition.GetStep(0).attackPhases;
            if (phases != null)
            {
                for (int i = 0; i < phases.Length; i++)
                {
                    if (phases[i].attackPattern == null)
                        continue;

                    AttackPatternRuntimeData pattern = phases[i].ResolvePattern(
                        stats.range,
                        stats.meleeSlashAngle,
                        meleeDefinition.baseSettings.hitWidth);
                    maxDistance = Mathf.Max(
                        maxDistance,
                        pattern.Range + Mathf.Max(0f, pattern.ForwardOffset));
                    verticalTolerance = Mathf.Max(verticalTolerance, pattern.VerticalTolerance);
                }
            }
        }

        window = new WeaponAttackWindow(
            0f,
            maxDistance * 0.72f,
            maxDistance,
            verticalTolerance);
        return true;
    }

    public WeaponActionResult TryStartAction(
        in WeaponActionRequest request,
        out WeaponActionHandle handle)
    {
        ResolveReferences();
        handle = WeaponActionHandle.Invalid;
        if (isAttacking || activeActionId > 0)
            return WeaponActionResult.RejectedBusy;

        if (!CanUseCurrentWeapon)
            return WeaponActionResult.RejectedUnsupported;

        if (request.Source == WeaponActionSource.PlayerInput && !manualInputEnabled)
            return WeaponActionResult.RejectedNotReady;

        if (IsPlayerEvading() || !CanAttackFromCurrentMovementState())
            return WeaponActionResult.RejectedNotReady;

        WeaponActionResult validation = ValidateActionRequest(request);
        if (validation != WeaponActionResult.Accepted)
            return validation;

        suppressHandoffMoveCancelUntilRelease = false;
        int actionId = AllocateActionId();
        activeActionId = actionId;
        activeActionSource = request.Source;
        activeRequestedTarget = request.Target;
        Vector3 attackDirection = ResolveRequestDirection(request);
        if (!TryStartAttackStep(false, attackDirection))
            return WeaponActionResult.RejectedNotReady;

        handle = new WeaponActionHandle(actionId);
        if (request.Source == WeaponActionSource.PlayerInput)
            ResolveFacade()?.CombatInputs?.ConsumeAttack();
        NotifyAcceptedMeleeAction();
        return WeaponActionResult.Accepted;
    }

    public WeaponActionResult TryContinue(
        in WeaponActionHandle handle,
        in WeaponActionRequest request)
    {
        if (!handle.IsValid
            || handle.ActionId != activeActionId
            || !isAttacking)
        {
            return WeaponActionResult.RejectedNotReady;
        }

        if (request.Source != activeActionSource)
            return WeaponActionResult.RejectedInvalidTarget;

        if (activeActionSource == WeaponActionSource.CompanionAI
            && request.Target != activeRequestedTarget)
        {
            return WeaponActionResult.RejectedInvalidTarget;
        }

        if (!CanContinueCombo() || !IsContinuationWindowOpen())
            return WeaponActionResult.RejectedNotReady;

        WeaponActionResult validation = ValidateActionRequest(request);
        if (validation != WeaponActionResult.Accepted)
            return validation;

        lastComboWindowTime = Time.time;
        Vector3 attackDirection = ResolveRequestDirection(request);
        if (!TryStartAttackStep(true, attackDirection))
            return WeaponActionResult.RejectedNotReady;

        if (request.Source == WeaponActionSource.PlayerInput)
            ResolveFacade()?.CombatInputs?.ConsumeAttack();
        NotifyAcceptedMeleeAction();
        return WeaponActionResult.Accepted;
    }

    public bool TryTransferActiveActionToPlayerInput(
        in WeaponActionHandle handle)
    {
        if (!handle.IsValid
            || handle.ActionId != activeActionId
            || !isAttacking
            || activeActionSource != WeaponActionSource.CompanionAI
            || !activeAttackUsesCombo
            || !CanContinueCombo())
        {
            return false;
        }

        activeActionSource = WeaponActionSource.PlayerInput;
        activeRequestedTarget = null;
        bufferedHandoffComboContinuation = true;
        return true;
    }

    public bool TryArmPlayerComboHandoff()
    {
        if (activeActionId <= 0
            || !isAttacking
            || activeActionSource != WeaponActionSource.PlayerInput
            || !activeAttackUsesCombo
            || !CanContinueCombo())
        {
            return false;
        }

        suppressHandoffMoveCancelUntilRelease = HasRawMoveInput();
        bufferedHandoffComboContinuation = true;
        return true;
    }

    public bool IsActivePlayerInputAction(int actionRevision)
    {
        return actionRevision > 0
            && actionRevision == activeActionId
            && isAttacking
            && activeActionSource == WeaponActionSource.PlayerInput;
    }

    public bool TryGetActionState(
        in WeaponActionHandle handle,
        out WeaponActionState state)
    {
        if (!handle.IsValid)
        {
            state = default;
            return false;
        }

        if (handle.ActionId == activeActionId)
        {
            state = WeaponActionState.Running(
                IsContinuationWindowOpen(),
                activeAttackDirection);
            return true;
        }

        if (handle.ActionId == terminalActionId)
        {
            state = terminalActionState;
            return true;
        }

        state = default;
        return false;
    }

    public void CancelAction(
        in WeaponActionHandle handle,
        WeaponActionCancelReason reason)
    {
        if (!handle.IsValid || handle.ActionId != activeActionId)
            return;

        CancelActiveAttack(ResolveCompletionReason(reason), true);
    }

    public void CancelActiveComboForEvade()
    {
        if (!CanCancelActiveComboForEvade)
            return;

        CancelActiveAttack(
            WeaponActionCompletionReason.CancelledByEvade,
            MeleeEvadeCancelPolicy.ResetComboOnCancel); // 구르기 뒤에는 항상 1타부터 시작
        playerAnimatorController?.CancelWeaponRuntimeState(); // 회피 시작 시 콤보 후반 공격 애니메이션 종료
        playerController?.CancelWeaponActionLocks();
    }

    public WeaponRuntimeStatus GetRuntimeStatus()
    {
        return new WeaponRuntimeStatus(RuntimeKind, CanUseCurrentWeapon, IsBusy, IsReady, CooldownRemaining, CooldownProgress01);
    }

    private void Awake()
    {
        ResolveReferences();
        ResolveAttackPatternDebugRenderer();
    }

    private void OnDisable()
    {
        ReleaseAttackStates();
        if (Application.isPlaying)
            CancelActiveAttack(WeaponActionCompletionReason.RuntimeDisabled, true);
        else
            attackPatternDebugRenderer?.Hide();
    }

    private void Update()
    {
        ResolveReferences();
        ResetStateIfWeaponChanged();

        // Resolve an executable evade before accepting an attack, independent of
        // MonoBehaviour Update order. Cooldown/locks/cost remain owned by evade.
        if (manualInputEnabled && inputEvadeController != null
            && inputEvadeController.TryExecuteBufferedEvade())
            return;

        if (!CombatDebugSettings.ShowAttackPatternDebug)
            attackPatternDebugRenderer?.Hide();

        if (IsPlayerEvading())
        {
            if (CanCancelActiveComboForEvade)
                CancelActiveComboForEvade(); // 입력 순서와 무관하게 같은 회피 취소 계약 적용

            return;
        }

        if (ShouldStartAttack())
        {
            WeaponActionRequest request = new WeaponActionRequest(
                WeaponActionSource.PlayerInput,
                null,
                CaptureAttackStartDirection());
            TryStartAction(request, out _);
        }

        UpdateAttack();
    }

    private void ResolveReferences()
    {
        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        if (playerAnimatorController == null)
            playerAnimatorController = GetComponent<PlayerAnimation>();

        if (playerController == null)
            playerController = GetComponent<PlayerMovement>();

        if (attackCamera == null)
            attackCamera = Camera.main;

        if (combatTarget == null)
            combatTarget = GetComponent<CombatTarget>();

        if (playerActorRuntime == null)
            playerActorRuntime = GetComponent<PlayerActorRuntime>();

        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();

        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (inputEvadeController == null)
            inputEvadeController = GetComponent<PlayerEvadeController>();
    }

    private int AllocateActionId()
    {
        if (nextActionId <= 0)
            nextActionId = 1;

        int actionId = nextActionId;
        nextActionId++;
        return actionId;
    }

    private int AllocateHitFeedbackSequenceId()
    {
        if (nextHitFeedbackSequenceId <= 0)
            nextHitFeedbackSequenceId = 1;

        int sequenceId = nextHitFeedbackSequenceId;
        nextHitFeedbackSequenceId++;
        return sequenceId;
    }

    private WeaponActionResult ValidateActionRequest(in WeaponActionRequest request)
    {
        if (request.Source == WeaponActionSource.PlayerInput)
            return WeaponActionResult.Accepted;

        CombatTarget target = request.Target;
        if (combatTarget == null
            || target == null
            || target.Team != CombatTeam.Enemy
            || !CombatTargetFilter.CanDamage(combatTarget, target))
        {
            return WeaponActionResult.RejectedInvalidTarget;
        }

        if (!TryGetAttackWindow(out WeaponAttackWindow window))
            return WeaponActionResult.RejectedUnsupported;

        CombatTargetVolume ownerVolume = combatTarget.CurrentVolume;
        CombatTargetVolume targetVolume = target.CurrentHurtVolume;
        Vector3 offset = targetVolume.Center - ownerVolume.Center;
        float planarCenterDistance = new Vector2(offset.x, offset.z).magnitude;
        float surfaceDistance = Mathf.Max(
            0f,
            planarCenterDistance - ownerVolume.Radius - targetVolume.Radius);
        float verticalGap = Mathf.Max(
            0f,
            Mathf.Abs(offset.y) - ownerVolume.HalfHeight - targetVolume.HalfHeight);

        if (surfaceDistance < window.MinStartDistance
            || surfaceDistance > window.MaxStartDistance
            || verticalGap > window.VerticalTolerance)
        {
            return WeaponActionResult.RejectedOutOfRange;
        }

        return WeaponActionResult.Accepted;
    }

    private Vector3 ResolveRequestDirection(in WeaponActionRequest request)
    {
        Vector3 direction = request.AimDirection;
        if (direction.sqrMagnitude <= 0.0001f && request.Target != null)
            direction = request.Target.WorldCenter - transform.position;

        return ResolvePlanarDirection(direction);
    }

    private Vector3 ResolvePlanarDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        return direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.forward;
    }

    private static WeaponActionCompletionReason ResolveCompletionReason(
        WeaponActionCancelReason reason)
    {
        switch (reason)
        {
            case WeaponActionCancelReason.WeaponSwitch:
                return WeaponActionCompletionReason.CancelledByWeaponSwitch;
            case WeaponActionCancelReason.Evade:
                return WeaponActionCompletionReason.CancelledByEvade;
            case WeaponActionCancelReason.Movement:
                return WeaponActionCompletionReason.CancelledByMovement;
            case WeaponActionCancelReason.Recovery:
                return WeaponActionCompletionReason.CancelledByRecovery;
            case WeaponActionCancelReason.Death:
                return WeaponActionCompletionReason.CancelledByDeath;
            case WeaponActionCancelReason.RuntimeDisabled:
                return WeaponActionCompletionReason.RuntimeDisabled;
            case WeaponActionCancelReason.InvalidConfiguration:
                return WeaponActionCompletionReason.InvalidConfiguration;
            default:
                return WeaponActionCompletionReason.CancelledByRequest;
        }
    }

    private bool ShouldStartAttack()
    {
        if (isAttacking)
            return false;

        if (!manualInputEnabled)
            return false;

        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        if (IsPlayerEvading())
            return false;

        if (playerEquipment == null || !playerEquipment.CanCurrentWeaponUseMeleeSlash)
            return false;

        if (!CanAttackFromCurrentMovementState())
            return false;

        if (bufferedHandoffComboContinuation)
            return true;

        return IsPrimaryAttackInputPressed();
    }

    private bool IsPlayerEvading()
    {
        return playerController != null && playerController.IsEvading;
    }

    private bool TryStartAttackStep(bool isDirectComboContinuation, Vector3 requestedDirection)
    {
        if (isAttacking)
            StopActiveAttackStep();

        comboMovementCollisionPusher.BeginComboStep(); // 새 타수의 이동 충돌 기록 초기화
        activeHitFeedbackSequenceId = AllocateHitFeedbackSequenceId(); // 홀드 콤보도 타수별 분리
        activeStats = FlaskCombatModifiers.Apply(playerEquipment.CurrentWeaponStats, gameObject);
        activeWeaponData = playerEquipment.CurrentWeaponData;
        activeComboDefinition = activeWeaponData != null
            ? activeWeaponData.GetMeleeComboDefinition()
            : null;
        activeAttackWeaponItem = playerEquipment.CurrentWeaponItem;
        activeAttackUsedMeleeCombatStance = playerController != null && playerController.IsMeleeCombatStance;
        activeAttackUsesCombo = ShouldUseCombo(activeWeaponData);
        ResolveAttackAnimation(isDirectComboContinuation);
        if (!TryResolveActiveComboAttackModifier())
        {
            CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
            return false;
        }

        attackDuration = ResolveAttackDuration();
        ResolveAttackTrail(activeAttackStep);
        ResolveAttackPhases();

        playerEquipment.RefreshCurrentWeaponReferences();
        activeAttackDirection = ResolvePlanarDirection(requestedDirection);
        if (!TryBeginAttackPhases())
        {
            CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
            return false;
        }

        if (!attackMovementExecutor.Begin(
                activeAttackStep.movementPhases,
                activeAttackDirection,
                ApplyAttackDisplacement)
            || !attackTrailExecutor.Begin(
                activeAttackStep.trailPhases,
                StartAttackTrail,
                StopAttackTrail))
        {
            CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
            return false;
        }

        RotateOwnerToAttackDirection();
        isAttacking = true;
        attackStartTime = Time.time;

        playerEquipment.CurrentWeaponPose?.BeginActivePose(attackDuration);

        MeleeAttackLock.Begin(
            playerController,
            attackDuration,
            activeAttackDirection);

        // GOAL A2: 공격 이동 ControlledMove와 melee action Attack을 명시 요청한다.
        RequestAttackStates();

        bool animationStarted = playerAnimatorController != null
            && playerAnimatorController.PlayMeleeCombatAttack(
                comboStepIndex,
                activeAttackAnimationClip,
                activeAttackAnimationSpeed,
                attackDuration,
                activeAttackTransitionDuration,
                activeActionSource == WeaponActionSource.PlayerInput);
        if (!animationStarted)
        {
            Debug.LogError("[MeleeRuntime] Formal melee combat animation could not start.", this);
            CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
            return false;
        }

        return true;
    }

    private void NotifyAcceptedMeleeAction()
    {
        if (activeActionSource == WeaponActionSource.PlayerInput)
        {
            PlayerCombatModeController.NotifyPlayerMeleeAccepted(
                playerActorRuntime,
                activeActionId);
            return;
        }

    }

    private void ResolveAttackTrail(MeleeComboStepData step)
    {
        activeAttackTrail = step.trailPhases != null && step.trailPhases.Length > 0
            ? ResolveCurrentWeaponTrail()
            : null;
    }

    private void StartAttackTrail()
    {
        if (activeAttackTrail == null)
            activeAttackTrail = ResolveCurrentWeaponTrail();

        activeAttackTrail?.BeginTrail();
    }

    private void StopAttackTrail()
    {
        activeAttackTrail?.EndTrail();
    }

    private void ClearAttackTrail()
    {
        if (activeAttackTrail == null)
            return;

        activeAttackTrail.ClearTrail();
    }

    private void ResetActiveTrailState()
    {
        activeAttackTrail = null;
    }

    private IWeaponTrailController ResolveCurrentWeaponTrail()
    {
        Transform weaponRoot = playerEquipment != null ? playerEquipment.CurrentWeaponRoot : null;
        if (weaponRoot == null)
            return null;

        MonoBehaviour[] components = weaponRoot.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IWeaponTrailController trailController)
                return trailController;
        }

        return null;
    }

    private void ResolveAttackAnimation(bool isDirectComboContinuation)
    {
        WeaponAnimationSettings animation = activeWeaponData != null && activeWeaponData.combatDefinition != null
            ? activeWeaponData.combatDefinition.animation
            : default;
        activeAttackAnimationClip = animation.primaryAttackClip;
        activeAttackAnimationSpeed = Mathf.Max(0.01f, animation.primaryAttackSpeed);
        activeAttackTransitionDuration = 0f;
        activeAttackStep = default;
        ResetActiveTrailState();

        if (!activeAttackUsesCombo)
            return;

        int stepIndex = ResolveNextComboStepIndex(activeComboDefinition);
        MeleeComboStepData step = activeComboDefinition.GetStep(stepIndex);
        activeAttackStep = step;
        activeAttackAnimationClip = step.animationClip != null ? step.animationClip : animation.primaryAttackClip;
        activeAttackAnimationSpeed = CalculateComboAnimationSpeed(activeComboDefinition, step);
        activeAttackTransitionDuration = ResolveComboTransitionDuration(
            activeComboDefinition,
            step,
            isDirectComboContinuation);
    }

    private static bool ShouldUseCombo(WeaponItemData weaponData)
    {
        MeleeComboDefinition comboDefinition = weaponData != null
            ? weaponData.GetMeleeComboDefinition()
            : null;
        return comboDefinition != null && comboDefinition.HasSteps;
    }

    private int ResolveNextComboStepIndex(MeleeComboDefinition comboDefinition)
    {
        int stepCount = comboDefinition.StepCount;
        float resetDelay = Mathf.Max(0.01f, comboDefinition.resetDelay);
        bool resetCombo = comboStepIndex < 0 || Time.time - lastComboWindowTime > resetDelay;
        int nextStepIndex = resetCombo ? 0 : (comboStepIndex + 1) % stepCount;

        comboStepIndex = nextStepIndex;
        return nextStepIndex;
    }

    private static float ResolveComboTransitionDuration(
        MeleeComboDefinition comboDefinition,
        MeleeComboStepData step,
        bool isDirectComboContinuation)
    {
        if (isDirectComboContinuation)
            return Mathf.Max(0f, step.transitionDuration);

        return Mathf.Max(0f, comboDefinition.entryTransitionDuration);
    }

    private float CalculateComboAnimationSpeed(MeleeComboDefinition comboDefinition, MeleeComboStepData step)
    {
        float attackSpeedMultiplier = Mathf.Max(0.01f, activeStats.meleeAttackSpeedMultiplier);
        float baseSpeed = comboDefinition != null ? comboDefinition.baseAnimationSpeed : 1f;
        float playbackAttackSpeed = MeleeAttackSpeedPolicy.ToPlaybackMultiplier(attackSpeedMultiplier);
        return Mathf.Max(0.01f, baseSpeed * playbackAttackSpeed * Mathf.Max(0.01f, step.animationSpeedMultiplier));
    }

    private void ResolveAttackPhases()
    {
        activeAttackPhases = activeAttackStep.attackPhases;
    }

    private bool TryResolveActiveComboAttackModifier()
    {
        ResetActiveComboAttackModifier();
        if (!activeAttackUsesCombo)
            return true; // 비콤보는 기존 무기값 유지

        if (!WeaponComboGemAttackModifierResolver.TryResolve(
                activeAttackWeaponItem,
                activeAttackStep.attackId,
                out WeaponComboGemAttackModifierSnapshot snapshot,
                out WeaponComboGemAttackModifierResolveFailureReason failureReason))
        {
            Debug.LogError(
                $"[MeleeRuntime] Combo attack modifier resolve failed. AttackId={activeAttackStep.attackId}, Reason={failureReason}",
                this);
            return false;
        }

        if (!string.Equals(snapshot.AttackId, activeAttackStep.attackId, System.StringComparison.Ordinal)
            || float.IsNaN(snapshot.DamageMultiplier)
            || float.IsInfinity(snapshot.DamageMultiplier)
            || snapshot.DamageMultiplier <= 0f)
        {
            Debug.LogError(
                $"[MeleeRuntime] Combo attack modifier snapshot is invalid. AttackId={activeAttackStep.attackId}",
                this);
            return false;
        }

        activeComboAttackModifierSnapshot = snapshot; // 타격 종료까지 고정
        hasActiveComboAttackModifierSnapshot = true;
        return true;
    }

    private WeaponElement ResolveActiveAttackElement()
    {
        if (!activeAttackUsesCombo)
            return activeAttackWeaponItem != null ? activeAttackWeaponItem.ResolvedElement : WeaponElement.None;

        return hasActiveComboAttackModifierSnapshot
            ? activeComboAttackModifierSnapshot.Element
            : WeaponElement.None;
    }

    private float ResolveActiveAttackDamageMultiplier()
    {
        return activeAttackUsesCombo && hasActiveComboAttackModifierSnapshot
            ? activeComboAttackModifierSnapshot.DamageMultiplier
            : 1f;
    }

    private void ResetActiveComboAttackModifier()
    {
        activeComboAttackModifierSnapshot = default;
        hasActiveComboAttackModifierSnapshot = false;
    }

    private bool TryBeginAttackPhases()
    {
        if (activeAttackPhases == null || activeAttackPhases.Length == 0)
        {
            Debug.LogError(
                "[MeleeRuntime] The melee attack has no configured AttackPhase. The attack was cancelled.",
                this);
            return false;
        }

        ResolveAttackPatternDebugRenderer();
        MeleeWeaponDefinition meleeDefinition = activeWeaponData != null
            ? activeWeaponData.GetMeleeDefinition()
            : null;
        if (meleeDefinition == null)
            return false;

        MeleeAttackStepTrajectoryBakeData bakedTrajectoryStep = null;
        string trajectoryError = "콤보 정의가 없습니다.";
        if (RequiresBakedAttackTrajectory(activeAttackPhases)
            && (activeComboDefinition == null
                || !activeComboDefinition.TryGetAttackTrajectoryStep(
                    comboStepIndex,
                    out bakedTrajectoryStep,
                    out trajectoryError)))
        {
            Debug.LogError(
                "[MeleeRuntime] WeaponTip 공격 궤적 베이크가 유효하지 않습니다. " + trajectoryError,
                this);
            return false;
        }

        bool started = attackPhaseExecutor.Begin(
            activeAttackPhases,
            transform,
            combatTarget,
            activeAttackDirection,
            activeStats,
            meleeDefinition.baseSettings,
            ResolveActiveAttackElement(),
            ResolveActiveAttackDamageMultiplier(),
            bakedTrajectoryStep,
            playerEquipment.CurrentWeaponTraceBinding,
            attackPatternDebugRenderer,
            DealPatternDamage,
            activeHitFeedbackSequenceId,
            activeAttackWeaponItem != null ? activeAttackWeaponItem.runtimeInstanceId : string.Empty,
            EarthElementZoneRuntimeService.ReportAttackRange);

        if (!started)
        {
            Debug.LogError(
                "[MeleeRuntime] AttackPhase execution failed. The attack was cancelled.",
                this);
        }

        return started;
    }

    private static bool RequiresBakedAttackTrajectory(AttackPhaseData[] phases)
    {
        if (phases == null)
            return false;

        for (int i = 0; i < phases.Length; i++)
        {
            if (phases[i].progressSource != AttackProgressSource.NormalizedTime)
                return true;
        }

        return false;
    }

    private void ResolveAttackPatternDebugRenderer()
    {
        if (attackPatternDebugRenderer == null)
            attackPatternDebugRenderer = GetComponent<AttackPatternDebugRenderer>();
    }

    private bool CanAttackFromCurrentMovementState()
    {
        if (playerController == null)
            return true;

        if (!playerController.IsGrounded)
            return false;

        // GOAL A2: Space 직접 읽기 대신 Gameplay Jump 눌림으로 공격 시작을 차단한다(점프 우선). 부정 로직 보존.
        PlayerInputFacade facade = ResolveFacade();
        return (stateCoordinator == null || stateCoordinator.CurrentCondition == PlayerConditionState.Normal)
            && !GameplayInputBlocker.IsGameplayInputBlocked
            && (facade == null || !facade.JumpPressedThisFrame);
    }

    private Vector3 CaptureAttackStartDirection()
    {
        if (!MeleeAimCalculator.TryGetMouseDirectionFromPlayer(
                transform,
                attackCamera,
                out Vector3 attackDirection))
        {
            attackDirection = transform.forward; // 방향 fallback
        }

        attackDirection.y = 0f; // XZ 평면

        if (attackDirection.sqrMagnitude <= 0.0001f)
            attackDirection = Vector3.forward;

        return attackDirection.normalized;
    }

    private void RotateOwnerToAttackDirection()
    {
        Vector3 attackDirection = activeAttackDirection;
        attackDirection.y = 0f;

        if (attackDirection.sqrMagnitude <= 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(attackDirection, Vector3.up);
    }

    private float ResolveAttackDuration()
    {
        if (activeAttackAnimationClip != null)
            return Mathf.Max(MinAttackDuration, activeAttackAnimationClip.length / Mathf.Max(0.01f, activeAttackAnimationSpeed));

        return MinAttackDuration;
    }

    private void UpdateAttack()
    {
        if (!isAttacking)
            return;

        float elapsed = Time.time - attackStartTime;
        float normalizedTime = attackDuration > 0f ? Mathf.Clamp01(elapsed / attackDuration) : 1f;
        bool shouldContinueCombo = ShouldContinueActiveCombo(normalizedTime);
        bool shouldCancelByMoveInput = !shouldContinueCombo
            && ShouldCancelActiveComboByMoveInput(normalizedTime);

        if (!shouldContinueCombo && !shouldCancelByMoveInput)
        {
            attackMovementExecutor.Tick(normalizedTime);
            attackTrailExecutor.Tick(normalizedTime);
        }

        attackPhaseExecutor.Tick(normalizedTime); // 전환·취소 프레임의 마지막 검끝 표본까지 먼저 판정

        if (shouldContinueCombo)
        {
            ContinueActiveCombo();
            return;
        }

        if (shouldCancelByMoveInput)
        {
            CancelActiveAttackByMoveInput();
            return;
        }

        if (elapsed >= attackDuration)
            FinishActiveAttack();
    }

    private bool ShouldContinueActiveCombo(float normalizedTime)
    {
        if (!manualInputEnabled
            || !activeAttackUsesCombo
            || !CanContinueCombo()
            || !activeAttackStep.comboInputWindow.Contains(normalizedTime)
            || GameplayInputBlocker.IsGameplayInputBlocked)
        {
            return false;
        }

        if (bufferedHandoffComboContinuation)
            return true;

        return IsPrimaryAttackInputPressed();
    }

    private bool IsPrimaryAttackInputPressed()
    {
        if (PlayerPickupInteractor.IsPrimaryAttackSuppressed)
            return false; // 월드 라벨 클릭 release까지 공격 누출 차단

        // GOAL A2: 좌클릭 홀드 직접 읽기 대신 Gameplay Attack 유지를 사용한다. 콤보 계속 의미 유지.
        PlayerInputFacade facade = ResolveFacade();
        return facade != null && facade.CombatInputs != null
            && (facade.CombatInputs.HasAttack || facade.CombatInputs.AllowsHeldAttack);
    }

    private void ContinueActiveCombo()
    {
        bufferedHandoffComboContinuation = false;
        WeaponActionRequest request = new WeaponActionRequest(
            activeActionSource,
            activeRequestedTarget,
            CaptureAttackStartDirection());
        TryContinue(new WeaponActionHandle(activeActionId), request);
    }

    private bool IsContinuationWindowOpen()
    {
        if (!isAttacking
            || !activeAttackUsesCombo
            || !CanContinueCombo()
            || attackDuration <= 0f)
        {
            return false;
        }

        float normalizedTime = Mathf.Clamp01((Time.time - attackStartTime) / attackDuration);
        return activeAttackStep.comboInputWindow.Contains(normalizedTime);
    }

    private bool IsActiveComboActionOpen()
    {
        if (!activeAttackUsesCombo || attackDuration <= 0f)
            return false;

        float normalizedTime = Mathf.Clamp01((Time.time - attackStartTime) / attackDuration);
        return normalizedTime >= Mathf.Clamp01(activeAttackStep.actionCancelStartNormalized);
    }

    private bool ShouldCancelActiveComboByMoveInput(float normalizedTime)
    {
        if (!manualInputEnabled
            || !activeAttackUsesCombo
            || normalizedTime < Mathf.Clamp01(activeAttackStep.actionCancelStartNormalized))
        {
            return false;
        }

        bool hasRawMoveInput = HasRawMoveInput();
        if (suppressHandoffMoveCancelUntilRelease)
        {
            if (!hasRawMoveInput)
                suppressHandoffMoveCancelUntilRelease = false;

            return false;
        }

        return hasRawMoveInput;
    }

    private bool HasRawMoveInput()
    {
        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        // GOAL A2: WASD 직접 읽기 대신 Gameplay Move 벡터를 사용한다. 후반 콤보 이동 취소 의미 유지.
        PlayerInputFacade facade = ResolveFacade();
        return facade != null && facade.MoveValue.sqrMagnitude > 0.001f;
    }

    private void CancelActiveAttackByMoveInput()
    {
        KeepComboWindowForCancel();
        CancelActiveAttack(WeaponActionCompletionReason.CancelledByMovement, false);
        playerAnimatorController?.CancelWeaponRuntimeState(); // 후반 이동 취소 시 공격 애니메이션도 종료
    }

    private void KeepComboWindowForCancel()
    {
        if (activeAttackUsesCombo && IsActiveComboActionOpen())
            lastComboWindowTime = Time.time;
    }

    private void ResetStateIfWeaponChanged()
    {
        ItemData currentWeaponItem = playerEquipment != null ? playerEquipment.CurrentWeaponItem : null;

        if (!isAttacking)
            return;

        if (playerEquipment == null || !playerEquipment.CanCurrentWeaponUseMeleeSlash || !IsSameRuntimeItem(activeAttackWeaponItem, currentWeaponItem))
            CancelActiveAttack(WeaponActionCompletionReason.CancelledByWeaponSwitch, true);
    }

    private void CancelActiveAttack(
        WeaponActionCompletionReason completionReason,
        bool resetCombo)
    {
        ResolveFacade()?.CombatInputs?.ClearAttack();
        Vector3 committedDirection = activeAttackDirection;
        StopActiveAttackStep();
        if (resetCombo)
            ResetComboState();

        CompleteActiveAction(false, completionReason, committedDirection);
    }

    private void StopActiveAttackStep()
    {
        isAttacking = false;
        ReleaseAttackStates();
        activeAttackWeaponItem = null;
        activeAttackUsesCombo = false;
        activeAttackAnimationClip = null;
        activeAttackAnimationSpeed = 1f;
        activeAttackTransitionDuration = 0f;
        activeAttackPhases = null;
        ResetActiveComboAttackModifier();
        attackPhaseExecutor.Cancel();
        attackMovementExecutor.Cancel();
        attackTrailExecutor.Cancel();
        activeAttackStep = default;
        ClearAttackTrail();
        ResetActiveTrailState();
        playerController?.CancelWeaponActionLocks();

        attackPatternDebugRenderer?.Hide();
    }

    private void ResetComboState()
    {
        comboStepIndex = -1;
        lastComboWindowTime = -999f;
    }

    private void FinishActiveAttack()
    {
        bool keepManualCombo = activeAttackUsesCombo
            && activeActionSource == WeaponActionSource.PlayerInput
            && manualInputEnabled;
        Vector3 committedDirection = activeAttackDirection;
        if (keepManualCombo)
            lastComboWindowTime = Time.time;
        else if (activeAttackUsesCombo)
            ResetComboState();

        FinishActiveAttackStep();
        CompleteActiveAction(
            true,
            WeaponActionCompletionReason.Completed,
            committedDirection);
    }

    private void FinishActiveAttackStep()
    {
        ReleaseAttackStates();
        activeAttackStep = default;
        activeAttackPhases = null;
        ResetActiveComboAttackModifier();
        attackPhaseExecutor.Cancel();
        attackMovementExecutor.Cancel();
        attackTrailExecutor.Cancel();
        ResetActiveTrailState();
        isAttacking = false;
    }

    private void CompleteActiveAction(
        bool completed,
        WeaponActionCompletionReason completionReason,
        Vector3 committedDirection)
    {
        if (activeActionId <= 0)
            return;

        terminalActionId = activeActionId;
        terminalActionState = WeaponActionState.Terminal(
            completed,
            committedDirection,
            completionReason);
        activeActionId = 0;
        activeActionSource = default;
        activeRequestedTarget = null;
        suppressHandoffMoveCancelUntilRelease = false;
        bufferedHandoffComboContinuation = false;
    }

    private bool CanContinueCombo()
    {
        return activeComboDefinition != null
            && activeComboDefinition.StepCount > 1
            && comboStepIndex >= 0;
    }

    private void ApplyAttackDisplacement(Vector3 displacement)
    {
        comboMovementCollisionPusher.PushBeforeMove(
            playerController,
            combatTarget,
            displacement); // CharacterController 이동 전에 선행 밀어내기
        playerController?.ApplyWeaponRootMotionDisplacement(displacement);
    }

    private bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }

    private PlayerInputFacade ResolveFacade()
    {
        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();
        if (inputFacade == null)
            inputFacade = PlayerInputFacade.Current;
        return inputFacade;
    }

    private PlayerStateCoordinator ResolveStateCoordinator()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        return stateCoordinator;
    }

    private void RequestAttackStates()
    {
        PlayerStateCoordinator coordinator = ResolveStateCoordinator();
        if (coordinator == null)
            return;
        coordinator.RequestLocomotion(this, PlayerLocomotionState.ControlledMove);
        coordinator.RequestAction(this, PlayerActionState.Attack);
    }

    private void ReleaseAttackStates()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        if (stateCoordinator == null)
            return;
        stateCoordinator.ReleaseLocomotion(this);
        stateCoordinator.ReleaseAction(this);
    }

    private void DealPatternDamage(AttackPhaseHit hit)
    {
        if (hit.Damageable == null)
            return;

        AttackPhaseData phase = hit.Phase;
        AttackImpactData impact = phase.impact;
        MeleeAttackRuntimeData runtimeData = hit.RuntimeData;
        WeaponElement attackElement = ResolveActiveAttackElement();
        bool useElementHitVfx = MeleeElementHitVfxService.CanPlay(attackElement)
            || (hit.TargetHealth != null && hit.TargetHealth.GetComponent<EnemyDeathPresentation>() != null);
        MeleeDamageResult result = MeleeDamageResolver.Apply(new MeleeDamageRequest(
            hit.Damageable,
            runtimeData.Damage,
            impact,
            runtimeData.HitStunDuration,
            GetActiveCritChance(),
            activeStats.critDamageMultiplier,
            hit.HitPoint,
            gameObject,
            hit.Direction,
            ApplyCombatStanceKnockback(runtimeData.Knockback),
            useElementHitVfx,
            attackElement,
            activeAttackWeaponItem != null ? activeAttackWeaponItem.runtimeInstanceId : string.Empty,
            activeHitFeedbackSequenceId));

        ApplyAirborne(
            result.TargetHealth,
            result.ActualDamage,
            impact.airborneImpulse,
            impact.airborneStunDuration);

        if (result.ActualDamage > 0f
            && impact.triggersOnHitEffects)
        {
            CombatHitFeedbackService.Request(new CombatHitFeedbackRequest(
                this,
                activeHitFeedbackSequenceId,
                impact.hitFeedbackProfile,
                result.IsCritical,
                attackElement,
                hit.HitPoint,
                playerActorRuntime != null && PlayerContext.GetOrCreate()?.CurrentActor == playerActorRuntime,
                CombatCameraRequestKind.AttackHit,
                hit.Direction,
                impact.hitFeedbackProfile != null ? impact.hitFeedbackProfile.CameraPriority : 1f,
                isLethal: result.TargetHealth != null && result.TargetHealth.IsDead,
                phaseIndex: hit.PhaseIndex,
                target: result.TargetHealth,
                impactShape: runtimeData.Pattern.IsThrust ? CombatImpactShape.Thrust
                    : phase.vfxSwingSettings.orientation == AttackVfxSwingOrientation.Vertical
                        ? CombatImpactShape.Downward : CombatImpactShape.Sweep,
                impactDirection: runtimeData.Pattern.IsThrust || phase.vfxSwingSettings.orientation == AttackVfxSwingOrientation.Vertical
                    ? hit.Direction : Vector3.Cross(Vector3.up, hit.Direction)
                        * (phase.vfxSwingSettings.reverseDirection ? -1f : 1f)));
        }

        if (impact.triggersOnHitEffects)
            ApplyOnHitEffects(result.TargetHealth, result.ActualDamage, hit.Direction);
    }

    private void ApplyAirborne(
        CombatHealth targetHealth,
        float actualDamage,
        float airborneImpulse,
        float airborneStunDuration)
    {
        if (targetHealth == null || targetHealth.IsDead || actualDamage <= 0f || airborneImpulse <= 0f)
            return;

        Rigidbody targetBody = targetHealth.GetComponentInParent<Rigidbody>();
        if (targetBody != null && !targetBody.isKinematic)
            targetBody.AddForce(Vector3.up * airborneImpulse, ForceMode.Impulse);

        EnemyMovementReaction movementReaction = targetHealth.GetComponentInParent<EnemyMovementReaction>();
        if (movementReaction != null)
            movementReaction.ExtendKnockbackReaction(airborneStunDuration > 0f ? airborneStunDuration : 0.35f);
    }

    private float GetActiveCritChance()
    {
        float bonus = activeAttackUsedMeleeCombatStance ? MeleeCombatStanceCritChanceBonus : 0f; // 자세 보너스
        return activeStats.critChance + bonus;
    }

    private float ApplyCombatStanceKnockback(float knockback)
    {
        float multiplier = activeAttackUsedMeleeCombatStance ? MeleeCombatStanceKnockbackMultiplier : 1f; // 자세 배율
        return knockback * multiplier;
    }

    private void ApplyOnHitEffects(CombatHealth targetHealth, float actualDamage, Vector3 direction)
    {
        if (actualDamage <= 0f)
            return;

        ApplyDamageOverTime(targetHealth, actualDamage, direction);
        HealSourceOnHit(actualDamage);
    }

    private void ApplyDamageOverTime(CombatHealth targetHealth, float actualDamage, Vector3 direction)
    {
        if (targetHealth == null || targetHealth.IsDead)
            return;

        float damagePerTick = activeStats.dotDamageFlat + actualDamage * activeStats.dotDamagePercent / 100f; // DoT 피해
        if (damagePerTick <= 0f)
            return;

        targetHealth.ApplyDamageOverTime(damagePerTick, activeStats.duration, DamageOverTimeTickInterval, gameObject, direction);
    }

    private void HealSourceOnHit(float actualDamage)
    {
        float healAmount = activeStats.onHitHeal + actualDamage * activeStats.lifeStealPercent / 100f; // 회복량
        if (healAmount <= 0f)
            return;

        CombatHealth sourceHealth = GetComponentInParent<CombatHealth>();
        if (sourceHealth == null)
            return;

        sourceHealth.Heal(healAmount);
    }

}
