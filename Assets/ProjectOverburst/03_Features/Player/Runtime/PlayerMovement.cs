using UnityEngine;

[DefaultExecutionOrder(300)]
[RequireComponent(typeof(CharacterController))]
public partial class PlayerMovement : MonoBehaviour, IActorMotor // 공용 이동 실행
{
    private const float DefaultCharacterControllerSkinWidthRadiusRatio = 0.1f;
    public const float GreatswordLocomotionSpeedMultiplier = 1.1f;

    [Header("Move")]
    [SerializeField] private float walkSpeed = 4.5f;
    [SerializeField] private float runSpeed = 7.8f;
    [SerializeField] private float aimMoveSpeedMultiplier = 0.6f;
    [SerializeField] private float acceleration = 55f;
    [SerializeField] private float deceleration = 70f;
    [SerializeField] private float airControl = 0.35f;
    [SerializeField] private float rotationSpeed = 22f;
    [SerializeField, Min(0f)] private float explorationFacingSharpness = 16f;
    [SerializeField] private bool useCameraRelativeMovement = true;
    [SerializeField] private Transform movementCamera;

    [Header("Input")]
    [SerializeField] private PlayerMovementInputSource movementInputSource;
    [SerializeField] private OverburstCharacterMotor3D characterMotor;
    [SerializeField] private PlayerLocomotion locomotion;
    private PlayerCombatFacingController combatFacingController;
    [SerializeField] private CombatMotionDriver combatMotion;

    [Header("Character Controller")]
    [SerializeField, Min(0.01f)] private float characterControllerSkinWidthRadiusRatio = DefaultCharacterControllerSkinWidthRadiusRatio;
    [SerializeField] private float characterControllerSlopeLimit = 45f;
    [SerializeField] private float characterControllerStepOffset = 0.3f;
    [SerializeField] private float characterControllerMinMoveDistance = 0f;
    [SerializeField, Min(0f)] private float externalVelocityDecay = 8f;
    [SerializeField, Min(0f)] private float maxExternalSpeed = 20f;

    [Header("Aim")]
    [SerializeField] private bool debugForceAiming;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerBuffController playerBuffController;

    [Header("Evade")]
    [SerializeField] private PlayerEvadeController playerEvadeController;

    [Header("Melee")]
    [SerializeField] private Camera meleeAimCamera;
    [SerializeField] private float meleeFacingRotationSpeed = 60f;

    [Header("Melee Combat Move")]
    [SerializeField] private float meleeCombatMoveSpeed = 3f;
    [SerializeField] private float meleeCombatGuardMoveSpeed = 1.8f;
    [SerializeField, Min(0f)] private float weaponRootMotionEnemyClearance = 0.03f;

    [Header("Jump")]
    [SerializeField] private float jumpHeight = 1.6f;
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float groundStickVelocity = -2f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.22f;
    [SerializeField] private LayerMask groundLayer = ~0;

    [Header("Grounding")]
    [SerializeField, Min(0.01f)] private float groundProbeStartOffset = 0.08f;
    [SerializeField, Min(0.05f)] private float groundSnapDistance = 0.32f;
    [SerializeField, Min(0f)] private float groundNormalSharpness = 22f;

    [Header("Landing")]
    [SerializeField] private float landingSlowDuration = 0.22f;
    [SerializeField] private float landingSpeedMultiplier = 0.6f;
    [SerializeField] private float landingAnimationMultiplier = 0.7f;
    [SerializeField] private float landingMinFallSpeed = 1.5f;

    private Vector2 moveInput; // 이동 입력
    private Vector3 moveDirection; // 이동 방향
    private float pendingJumpVelocity; // 모터 전달용 점프 속도
    private bool isRunning; // 기본 달리기 이동
    private bool isWalkMode; // 걷기 토글
    private bool isAiming; // 조준
    private bool isMeleeCombatStance; // 근접 자세
    private bool aimBlockedUntilRelease; // 조준 재입력
    private bool jumpRequested; // 점프 예약
    private bool jumpAnimationRequested; // 점프 애니
    private float meleeAttackMoveLockUntil; // 근접 이동 잠금
    private bool meleeAttackRotationLocked; // 근접 회전 잠금
    private Vector3 meleeAttackLockedDirection; // 근접 방향
    private float swordGuardStartedTime = -999f; // Sword 막기 시작
    private bool swordGuardParryConsumed; // 패링 소모
    private float landingSlowTimer; // 착지 감속
    private ActorControlAuthority controlAuthority = ActorControlAuthority.Player; // 이동 권한
    private ActorMovementIntent activeMovementIntent; // 현재 실행 의도
    private ActorMovementIntent submittedAIIntent; // AI 제출 의도
    private int submittedAIIntentFrame = -1; // 제출 프레임
    private bool lootAutoMoveActive; // 월드 아이템 전용 자동 접근
    private Vector3 lootAutoMoveDestination; // 자동 접근 목적지
    private CombatTarget combatTarget; // 이동 충돌 진영
    private PlayerInputFacade inputFacade; // GOAL A2 파사드 캐시
    private PlayerStateCoordinator stateCoordinator; // GOAL A2 상태 보고
    private PlayerKnockdownController knockdownController;
    public bool IsKnockedDown => knockdownController != null && knockdownController.IsActive;
    public bool IsConditionMovementBlocked => ResolveStateCoordinator() != null
        && ResolveStateCoordinator().CurrentCondition != PlayerConditionState.Normal;

    public Vector2 MoveInput
    {
        get { return moveInput; }
    }

    public float BaseMoveSpeed
    {
        get { return ResolveCurrentBaseMoveSpeed(); }
    }

    public float WalkMoveSpeed => ResolveAuthoredMoveSpeed(walkSpeed);
    public float RunMoveSpeed => ResolveAuthoredMoveSpeed(runSpeed);

    public bool HasGreatswordEquipped => playerEquipment != null
        && playerEquipment.HasCurrentWeapon
        && playerEquipment.CurrentWeaponContext.Class == WeaponClass.Greatsword;

    public float MoveAcceleration => Mathf.Max(0f, acceleration);
    public float MoveDeceleration => Mathf.Max(0f, deceleration);

    public ActorControlAuthority ControlAuthority => controlAuthority;
    public ActorMovementIntent ActiveMovementIntent => activeMovementIntent;
    public bool IsLootAutoMoveActive => controlAuthority == ActorControlAuthority.Player && lootAutoMoveActive;

    public OverburstCharacterMotor3D Motor => characterMotor;
    public PlayerLocomotion Locomotion => locomotion;
    public CombatMotionDriver CombatMotion => combatMotion;

    public Vector3 MoveDirection
    {
        get { return moveDirection; }
    }

    public bool IsGrounded
    {
        get { return characterMotor != null && characterMotor.IsGrounded; }
    }

    public Vector3 GroundNormal => characterMotor != null ? characterMotor.GroundNormal : Vector3.up;

    public bool IsRunning
    {
        get { return isRunning; }
    }

    public bool IsWalkMode
    {
        get { return controlAuthority == ActorControlAuthority.Player && isWalkMode; }
    }

    public ActorMovementGait ExplorationGait
    {
        get { return IsWalkMode ? ActorMovementGait.Walk : ActorMovementGait.Run; }
    }

    public bool IsAiming
    {
        get { return isAiming; }
    }

    public bool IsMeleeCombatStance
    {
        get { return isMeleeCombatStance; }
    }

    public bool IsMeleeGuarding
    {
        get { return IsMeleeCombatLocomotionMode && isMeleeCombatStance && CanUseMeleeGuardWithCurrentWeapon(); }
    }

    public float MeleeGuardElapsedTime
    {
        get { return IsMeleeGuarding ? Mathf.Max(0f, Time.time - swordGuardStartedTime) : 0f; }
    }

    public bool IsWeaponAimInputActive
    {
        get { return IsAiming || isMeleeCombatStance; }
    }

    public bool IsMeleeCombatLocomotionMode
    {
        get { return PlayerCombatModeController.IsSharedCombatModeActive() && CanUseMeleeCombatStanceWithCurrentWeapon(); }
    }

    public bool IsCombatWalkLocomotionMode
    {
        get
        {
            return !lootAutoMoveActive
                && (IsMeleeCombatLocomotionMode
                    || controlAuthority == ActorControlAuthority.AI
                    && PlayerCombatModeController.IsSharedCombatModeActive()
                    && activeMovementIntent.Gait == ActorMovementGait.Walk); // 자동 접근은 Run 우선
        }
    }

    public bool IsRightClickCombatPoseActive
    {
        get { return isAiming || isMeleeCombatStance; }
    }

    public bool IsWeaponAimPoseActive
    {
        get { return IsWeaponAimInputActive && CanUseAimPoseWithCurrentWeapon(); }
    }

    public float ActiveAimIncomingDamageMultiplier
    {
        get
        {
            if (!IsWeaponAimInputActive || playerEquipment == null)
                return 1f;

            return playerEquipment.CurrentAimIncomingDamageMultiplier;
        }
    }

    public bool IsQuickFiring
    {
        get { return false; } // 옛 총기·마법 빠른 사격. 시작 경로가 없어 늘 false다. 조준 커서 UI가 읽어서 남긴다.
    }

    public bool IsCombatMoveMode
    {
        get { return !lootAutoMoveActive && (IsCombatWalkLocomotionMode || IsAimCombatMoveActive); } // 자동 접근은 Run 우선
    }

    private bool IsAimCombatMoveActive
    {
        get { return IsWeaponAimInputActive && !IsMeleeCombatStance && CanUseAimCombatMoveWithCurrentWeapon(); }
    }

    public bool IsMeleeAttackMoveLocked
    {
        get { return Time.time < meleeAttackMoveLockUntil && !GameplayInputBlocker.IsGameplayInputBlocked; }
    }

    public bool IsLandingRecovering
    {
        get { return landingSlowTimer > 0f; }
    }

    public bool IsEvading
    {
        get { return playerEvadeController != null && playerEvadeController.IsEvading; }
    }

    public float EvadeRotationSpeedMultiplier
    {
        get { return playerEvadeController != null ? playerEvadeController.RotationRecoverySpeedMultiplier : 1f; }
    }

    public float VerticalVelocity
    {
        get { return characterMotor != null ? characterMotor.VerticalVelocity : 0f; }
    }

    public LayerMask GroundLayerMask
    {
        get { return groundLayer; }
    }

    public float AnimationMoveAmount
    {
        get
        {
            if (moveInput.sqrMagnitude <= 0.001f)
                return 0f;

            if (IsMeleeAttackMoveLocked)
                return 0f;

            float amount = IsCombatWalkLocomotionMode ? (IsMeleeGuarding ? 0.55f : 1f) : isRunning ? 1f : 0.55f;

            if (landingSlowTimer > 0f)
                amount *= landingAnimationMultiplier;

            return Mathf.Clamp01(amount);
        }
    }

    private void Awake()
    {
        combatFacingController = GetComponent<PlayerCombatFacingController>();
        if (combatFacingController == null) combatFacingController = gameObject.AddComponent<PlayerCombatFacingController>();
        knockdownController = GetComponent<PlayerKnockdownController>();
        if (characterMotor == null)
            characterMotor = GetComponent<OverburstCharacterMotor3D>();
        if (characterMotor == null)
            characterMotor = gameObject.AddComponent<OverburstCharacterMotor3D>(); // 모터 런타임 보장
        characterMotor.BindMovement(this);

        if (locomotion == null)
            locomotion = GetComponent<PlayerLocomotion>();
        if (locomotion == null)
            locomotion = gameObject.AddComponent<PlayerLocomotion>();
        locomotion.Bind(this, characterMotor);

        if (combatMotion == null)
            combatMotion = GetComponent<CombatMotionDriver>();
        if (combatMotion == null)
            combatMotion = gameObject.AddComponent<CombatMotionDriver>();

        if (groundCheck == null)
        {
            Transform foundGroundCheck = transform.Find("GroundCheck");

            if (foundGroundCheck != null)
                groundCheck = foundGroundCheck;
        }

        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        if (playerBuffController == null)
            playerBuffController = ResolveBuffController();

        if (playerEvadeController == null)
            playerEvadeController = GetComponent<PlayerEvadeController>();

        if (playerEvadeController == null)
            playerEvadeController = gameObject.AddComponent<PlayerEvadeController>(); // 회피 입력 런타임 보장

        if (movementCamera == null && Camera.main != null)
            movementCamera = Camera.main.transform;

        if (movementInputSource == null)
            movementInputSource = GetComponent<PlayerMovementInputSource>();

        if (meleeAimCamera == null)
            meleeAimCamera = Camera.main;

        if (combatTarget == null)
            combatTarget = GetComponent<CombatTarget>();
        combatMotion.Bind(this, characterMotor, combatTarget, weaponRootMotionEnemyClearance);

        ConfigureGroundLayerMask();
        characterMotor.Configure(BuildMotorSettings());
    }

    private CharacterMotorSettings BuildMotorSettings()
    {
        return new CharacterMotorSettings
        {
            skinWidthRadiusRatio = characterControllerSkinWidthRadiusRatio,
            slopeLimit = characterControllerSlopeLimit,
            stepOffset = characterControllerStepOffset,
            minMoveDistance = characterControllerMinMoveDistance,
            groundCheckRadius = groundCheckRadius,
            groundLayer = groundLayer,
            groundProbeStartOffset = groundProbeStartOffset,
            groundSnapDistance = groundSnapDistance,
            groundNormalSharpness = groundNormalSharpness,
            gravity = gravity,
            groundStickVelocity = groundStickVelocity,
            maxFallSpeed = OverburstCharacterMotor3D.DefaultMaxFallSpeed,
            externalVelocityDecay = externalVelocityDecay,
            maxExternalSpeed = maxExternalSpeed,
        };
    }

    private void ConfigureGroundLayerMask()
    {
        int playerBoundaryLayer = LayerMask.NameToLayer("PlayerBoundary"); // 투명벽 layer
        if (playerBoundaryLayer >= 0)
            groundLayer = groundLayer.value & ~(1 << playerBoundaryLayer);

        int actorLayer = gameObject.layer;
        if (actorLayer >= 0)
            groundLayer = groundLayer.value & ~(1 << actorLayer); // 자기 몸체 제외
    }

    private void Update()
    {
        ProbeMotorGround();
        UpdateLandingRecovery();
        UpdateMeleeAttackMoveLock();

        if (IsConditionMovementBlocked)
        {
            PrepareKnockdownMotion();
            Move(Time.deltaTime); // 중력·지면·이동 발판은 계속 갱신한다.
            UpdatePlayerStateReport();
            return;
        }

        if (controlAuthority == ActorControlAuthority.Player)
        {
            ReadJumpInput();
            bool wasSwordGuarding = IsMeleeGuarding;
            ReadAimInput();
            UpdateSwordGuardTiming(wasSwordGuarding);
            ReadMoveInput();
            activeMovementIntent = lootAutoMoveActive
                ? CreateLootAutoMoveIntent()
                : CreatePlayerMovementIntent();
        }
        else
        {
            ReadAIMovementIntent();
        }

        RotatePlayer();
        ApplyJump();
        Move(Time.deltaTime);
        UpdatePlayerStateReport();
    }

    private void OnDisable()
    {
        Stop();
        ReleasePlayerStateReport();
        if (characterMotor != null)
            characterMotor.ResetMotion();
    }

    private PlayerStateCoordinator ResolveStateCoordinator()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        return stateCoordinator;
    }

    // GOAL A2: Idle/Moving/Airborne와 조준 GuardOrAim을 명시 보고한다.
    // Evading/ControlledMove/Attack은 각 소유자가 우선순위로 보고하며 이 보고를 덮어쓰지 않는다.
    private void UpdatePlayerStateReport()
    {
        PlayerStateCoordinator coordinator = ResolveStateCoordinator();
        if (coordinator == null || !isActiveAndEnabled)
            return;

        PlayerLocomotionState locomotion = !IsGrounded
            ? PlayerLocomotionState.Airborne
            : moveInput.sqrMagnitude > 0.001f ? PlayerLocomotionState.Moving : PlayerLocomotionState.Idle;
        coordinator.RequestLocomotion(this, locomotion);

        if (IsWeaponAimInputActive)
            coordinator.RequestAction(this, PlayerActionState.GuardOrAim);
        else
            coordinator.ReleaseAction(this);
    }

    private void ReleasePlayerStateReport()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        if (stateCoordinator != null)
        {
            stateCoordinator.ReleaseLocomotion(this);
            stateCoordinator.ReleaseAction(this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
