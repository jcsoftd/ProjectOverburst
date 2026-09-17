using UnityEngine;

[DefaultExecutionOrder(300)]
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour, IActorMotor // 공용 이동 실행
{
    private const float DefaultCharacterControllerSkinWidthRadiusRatio = 0.1f;

    [Header("Move")]
    [SerializeField] private float walkSpeed = 4.5f;
    [SerializeField] private float runSpeed = 7.8f;
    [SerializeField] private float aimMoveSpeedMultiplier = 0.6f;
    [SerializeField] private float quickFireMoveSpeedMultiplier = 0.8f;
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

    [Header("Stamina")]
    [SerializeField] private PlayerStaminaController playerStaminaController;

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
    private float quickFireUntil; // QuickFire 유지
    private float externalAimUntil; // 외부 조준
    private float activeQuickFireMoveSpeedMultiplier = 0.8f; // QuickFire 속도
    private float bagMoveSpeedMultiplier = 1f; // 가방 이동속도
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

    public float MoveAcceleration => Mathf.Max(0f, acceleration);
    public float MoveDeceleration => Mathf.Max(0f, deceleration);

    public ActorControlAuthority ControlAuthority => controlAuthority;
    public ActorMovementIntent ActiveMovementIntent => activeMovementIntent;
    public bool IsLootAutoMoveActive => controlAuthority == ActorControlAuthority.Player && lootAutoMoveActive;

    public void BindInputSource(PlayerMovementInputSource inputSource)
    {
        movementInputSource = inputSource != null
            ? inputSource
            : GetComponent<PlayerMovementInputSource>();
    }

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
        get { return isAiming || IsExternalAiming; }
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
        get { return Time.time < quickFireUntil && !GameplayInputBlocker.IsGameplayInputBlocked; } // QuickFire 상태
    }

    public bool IsCombatMoveMode
    {
        get { return !lootAutoMoveActive && (IsCombatWalkLocomotionMode || IsAimCombatMoveActive || IsQuickFiring); } // 자동 접근은 Run 우선
    }

    private bool IsAimCombatMoveActive
    {
        get { return IsWeaponAimInputActive && !IsMeleeCombatStance && CanUseAimCombatMoveWithCurrentWeapon(); }
    }

    private bool IsExternalAiming
    {
        get { return Time.time < externalAimUntil && !GameplayInputBlocker.IsGameplayInputBlocked; }
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

        if (playerStaminaController == null)
            playerStaminaController = GetComponent<PlayerStaminaController>();

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

    public bool ConsumeJumpAnimationRequest()
    {
        if (!jumpAnimationRequested)
            return false;

        jumpAnimationRequested = false;
        return true;
    }

    private void ProbeMotorGround()
    {
        characterMotor.ProbeGround(Time.deltaTime);
        if (characterMotor.DidLandThisStep && characterMotor.LandingFallSpeed < -landingMinFallSpeed)
            landingSlowTimer = landingSlowDuration; // 착지 감속
    }

    private void UpdateLandingRecovery()
    {
        if (landingSlowTimer <= 0f)
            return;

        landingSlowTimer -= Time.deltaTime; // 감속 시간

        if (landingSlowTimer < 0f)
            landingSlowTimer = 0f;
    }

    private void UpdateMeleeAttackMoveLock()
    {
        if (!meleeAttackRotationLocked)
            return;

        if (IsMeleeAttackMoveLocked)
            return;

        meleeAttackRotationLocked = false; // 회전 잠금 해제
        meleeAttackLockedDirection = Vector3.zero; // 방향 초기화
    }

    private void ReadMoveInput()
    {
        if (movementInputSource == null)
        {
            moveInput = Vector2.zero; // 입력 초기화
            moveDirection = Vector3.zero; // 방향 초기화
            isRunning = false; // 달리기 해제
            return;
        }

        ReadWalkToggleInput();

        if (GameplayInputBlocker.IsGameplayInputBlocked
            || IsEvading
            || IsMeleeAttackMoveLocked
            || (isMeleeCombatStance && !IsMeleeCombatLocomotionMode)
            || (IsMeleeGuarding
                && playerEquipment != null
                && !playerEquipment.CanCurrentWeaponMoveWhileGuarding))
        {
            moveInput = Vector2.zero; // 이동 차단
            moveDirection = Vector3.zero; // 방향 초기화
            isRunning = false; // 달리기 해제
            return;
        }

        moveInput = movementInputSource.RawMoveInput; // 원시 입력 소비
        moveDirection = GetMoveDirection(moveInput); // 카메라 기준
        isRunning = moveInput.sqrMagnitude > 0.001f && !isWalkMode && !IsCombatMoveMode; // 회피 입력은 별도 제어
    }

    private void ReadWalkToggleInput()
    {
        bool requested = movementInputSource != null
            && movementInputSource.ConsumeWalkToggleRequest();
        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return;

        if (requested)
            isWalkMode = !isWalkMode; // C 걷기 토글
    }

    private Vector3 GetMoveDirection(Vector2 input)
    {
        return locomotion != null
            ? locomotion.ResolveMoveDirection(input, useCameraRelativeMovement, movementCamera)
            : Vector3.zero;
    }

    public Vector3 ResolveMoveDirection(Vector2 input)
    {
        return GetMoveDirection(input);
    }

    private void ReadJumpInput()
    {
        bool requested = movementInputSource != null
            && movementInputSource.ConsumeJumpRequest();
        if (!requested || GameplayInputBlocker.IsGameplayInputBlocked)
            return;

        if (IsEvading || IsMeleeAttackMoveLocked)
            return;

        if (IsGrounded)
        {
            jumpRequested = true; // 점프 예약
            jumpAnimationRequested = true; // 점프 애니
            CancelAimUntilRelease(); // 조준 차단
        }
    }

    private void ReadAimInput()
    {
        if (GameplayInputBlocker.IsGameplayInputBlocked)
        {
            isAiming = false; // 조준 해제
            isMeleeCombatStance = false; // 근접 자세 해제
            ResetSwordGuardTiming();
            meleeAttackMoveLockUntil = 0f; // 이동 잠금 해제
            meleeAttackRotationLocked = false; // 회전 잠금 해제
            meleeAttackLockedDirection = Vector3.zero; // 방향 초기화
            quickFireUntil = 0f; // QuickFire 해제
            externalAimUntil = 0f; // 외부 조준 해제
            aimBlockedUntilRelease = true; // 재입력 대기
            return;
        }

        if (IsEvading)
        {
            isAiming = false; // 회피 중 조준 차단
            isMeleeCombatStance = false; // 회피 중 자세 차단
            ResetSwordGuardTiming();
            quickFireUntil = 0f;
            externalAimUntil = 0f;
            return;
        }

        if (debugForceAiming)
        {
            isAiming = CanAimWithCurrentWeapon(); // 디버그 조준
            isMeleeCombatStance = false; // 근접 제외
            ResetSwordGuardTiming();
            aimBlockedUntilRelease = false; // 차단 해제
            return;
        }

        // GOAL A2: 우클릭 홀드 직접 읽기 대신 Gameplay Aim 유지를 사용한다.
        // aimBlockedUntilRelease 래치와 무기별 CanAim/CanStance 분기 감각을 유지.
        PlayerInputFacade facade = ResolveInputFacade();
        if (facade == null)
        {
            isAiming = false;
            isMeleeCombatStance = false;
            ResetSwordGuardTiming();
            return;
        }

        bool rightPressed = facade.AimHeld;

        if (!rightPressed)
        {
            aimBlockedUntilRelease = false; // 재입력 해제
            isAiming = false; // 조준 해제
            isMeleeCombatStance = false; // 근접 자세 해제
            ResetSwordGuardTiming();
            return;
        }

        if (!IsGrounded || jumpRequested)
        {
            CancelAimUntilRelease();
            return;
        }

        if (CanUseMeleeCombatStanceWithCurrentWeapon())
        {
            if (!PlayerCombatModeController.IsSharedCombatModeActive())
            {
                isAiming = false;
                isMeleeCombatStance = false;
                ResetSwordGuardTiming();
                return;
            }

            if (aimBlockedUntilRelease)
            {
                isMeleeCombatStance = false; // 재입력 대기
                return;
            }

            isAiming = false; // 총기 조준 해제
            isMeleeCombatStance = true; // 근접 자세
            return;
        }

        isMeleeCombatStance = false; // 근접 자세 해제
        ResetSwordGuardTiming();

        if (!CanAimWithCurrentWeapon())
        {
            isAiming = false; // 조준 불가
            return;
        }

        if (aimBlockedUntilRelease)
        {
            isAiming = false; // 재입력 대기
            return;
        }

        isAiming = true; // 정조준
    }

    private bool CanAimWithCurrentWeapon()
    {
        return playerEquipment == null
            || playerEquipment.CanCurrentWeaponUseMagicAim; // 우클릭 직접 조준
    }

    private bool CanUseMeleeCombatStanceWithCurrentWeapon()
    {
        return playerEquipment != null && playerEquipment.CanCurrentWeaponUseMeleeCombatStance; // 근접 자세 가능
    }

    private bool CanUseMeleeGuardWithCurrentWeapon()
    {
        return playerEquipment != null && playerEquipment.CanCurrentWeaponUseMeleeGuard;
    }

    private bool CanUseMagicAimWithCurrentWeapon()
    {
        return playerEquipment != null && playerEquipment.CanCurrentWeaponUseMagicAim;
    }

    private bool CanUseAimCombatMoveWithCurrentWeapon()
    {
        return playerEquipment == null || playerEquipment.CanCurrentWeaponUseAimCombatMove;
    }

    private bool CanUseAimPoseWithCurrentWeapon()
    {
        return playerEquipment == null || playerEquipment.CanCurrentWeaponUseAimPose;
    }

    private bool CanRotateAimWithCurrentWeapon()
    {
        return playerEquipment == null || playerEquipment.CanCurrentWeaponRotateToAim;
    }

    private void CancelAimUntilRelease()
    {
        isAiming = false; // 조준 해제
        isMeleeCombatStance = false; // 근접 해제
        ResetSwordGuardTiming();
        aimBlockedUntilRelease = true; // 재입력 대기
    }

    private void UpdateSwordGuardTiming(bool wasSwordGuarding)
    {
        bool isSwordGuarding = IsMeleeGuarding;
        if (!isSwordGuarding)
        {
            ResetSwordGuardTiming();
            return;
        }

        if (wasSwordGuarding)
            return;

        swordGuardStartedTime = Time.time; // false -> true 시점
        swordGuardParryConsumed = false;
    }

    private void ResetSwordGuardTiming()
    {
        swordGuardStartedTime = -999f;
        swordGuardParryConsumed = false;
    }

    public bool TryConsumeMeleeGuardParry()
    {
        if (!IsMeleeGuarding || swordGuardParryConsumed || playerEquipment == null)
            return false;

        float parryWindow = playerEquipment.CurrentMeleeGuardParryWindow;
        if (parryWindow <= 0f || Time.time - swordGuardStartedTime > parryWindow)
            return false;

        swordGuardParryConsumed = true;
        return true;
    }

    private void RotatePlayer()
    {
        if (IsMeleeAttackMoveLocked)
        {
            RotateToLockedMeleeAttackDirection();
            return;
        }

        if (controlAuthority == ActorControlAuthority.AI)
        {
            Vector3 facingDirection = activeMovementIntent.FacingDirection.sqrMagnitude > 0.001f
                ? activeMovementIntent.FacingDirection
                : moveDirection;
            RotateToDirection(facingDirection, rotationSpeed);
            return;
        }

        if (lootAutoMoveActive)
        {
            RotateToMoveDirection(); // 아이템 접근 방향 우선
            return;
        }

        if (IsEvading)
            return;

        if (IsMeleeCombatLocomotionMode)
        {
            RotateToMeleeAimDirection();
            return;
        }

        if (isMeleeCombatStance)
        {
            if (CanRotateAimWithCurrentWeapon())
                RotateToMeleeAimDirection();
            return;
        }

        if (IsAiming && CanUseMagicAimWithCurrentWeapon() && CanRotateAimWithCurrentWeapon())
        {
            RotateToMagicAimDirection();
            return;
        }

        if (IsCombatMoveMode)
            return;

        RotateToExplorationMoveDirection();
    }

    private void RotateToMeleeAimDirection()
    {
        if (!MeleeAimCalculator.TryGetMouseDirectionFromPlayer(transform, meleeAimCamera, out Vector3 direction))
            return;

        RotateToDirection(direction, meleeFacingRotationSpeed);
    }

    private void RotateToMagicAimDirection()
    {
        if (!MeleeAimCalculator.TryGetMouseDirectionFromPlayer(transform, meleeAimCamera, out Vector3 direction))
            return;

        RotateToDirection(direction, meleeFacingRotationSpeed);
    }

    private void RotateToLockedMeleeAttackDirection()
    {
        Vector3 direction = meleeAttackLockedDirection;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            direction = transform.forward;

        if (direction.sqrMagnitude <= 0.001f)
            return;

        locomotion?.FaceImmediately(direction);
    }

    private void RotateToMoveDirection()
    {
        if (moveDirection.sqrMagnitude <= 0.001f)
            return;

        RotateToDirection(moveDirection, rotationSpeed);
    }

    private void RotateToExplorationMoveDirection()
    {
        if (moveDirection.sqrMagnitude <= 0.001f)
            return;

        locomotion?.RotateSmooth(moveDirection, explorationFacingSharpness, Time.deltaTime);
    }

    private void RotateToDirection(Vector3 direction, float rotateSpeed)
    {
        locomotion?.RotateTowards(
            direction,
            rotateSpeed,
            EvadeRotationSpeedMultiplier,
            Time.deltaTime);
    }

    private void Move(float deltaTime)
    {
        if (locomotion == null)
            return;

        float jumpVelocity = pendingJumpVelocity;
        pendingJumpVelocity = 0f; // 모터 소비
        MotorStepResult result = locomotion.Step(
            activeMovementIntent,
            BaseMoveSpeed,
            acceleration,
            deceleration,
            airControl,
            jumpVelocity,
            Mathf.Max(0f, deltaTime));
        if (result.didLand && result.landingFallSpeed < -landingMinFallSpeed)
            landingSlowTimer = landingSlowDuration; // 같은 Move에서 발생한 착지를 즉시 소비
    }

    private float GetTargetMoveSpeed()
    {
        if (IsEvading || IsMeleeAttackMoveLocked)
            return 0f;

        if (IsCombatWalkLocomotionMode)
            return GetMeleeCombatMoveSpeed() * bagMoveSpeedMultiplier * GetActiveBuffMoveSpeedMultiplier();

        float baseMoveSpeed = isWalkMode ? walkSpeed : runSpeed; // 기본 이동은 달리기
        float speed = IsAimCombatMoveActive ? walkSpeed * GetActiveAimMoveSpeedMultiplier() : IsQuickFiring ? walkSpeed * activeQuickFireMoveSpeedMultiplier : baseMoveSpeed; // 상태별 속도

        if (landingSlowTimer > 0f)
            speed *= landingSpeedMultiplier; // 착지 감속

        return speed * bagMoveSpeedMultiplier * GetActiveBuffMoveSpeedMultiplier();
    }

    public void SetControlAuthority(ActorControlAuthority authority)
    {
        if (controlAuthority == authority)
            return;

        controlAuthority = authority;
        CancelLootAutoMove(); // 권한 전환 시 목적지 폐기
        movementInputSource?.Clear();
        Stop();
        jumpRequested = false;
        jumpAnimationRequested = false;
        isAiming = false;
        isMeleeCombatStance = false;
        ResetSwordGuardTiming();
    }

    public void ApplyMovementIntent(ActorMovementIntent intent, float deltaTime)
    {
        if (controlAuthority != ActorControlAuthority.AI)
            return;

        submittedAIIntent = intent;
        submittedAIIntentFrame = Time.frameCount;
    }

    public void Stop()
    {
        lootAutoMoveActive = false;
        submittedAIIntent = ActorMovementIntent.Hold(
            transform.position,
            transform.forward,
            ResolveStoppedMovementGait());
        submittedAIIntentFrame = -1;
        activeMovementIntent = submittedAIIntent;
        moveInput = Vector2.zero;
        moveDirection = Vector3.zero;
        locomotion?.Stop();
        isRunning = false;
    }

    public bool BeginLootAutoMove(Vector3 destination)
    {
        if (controlAuthority != ActorControlAuthority.Player || !isActiveAndEnabled)
            return false;

        lootAutoMoveDestination = destination;
        lootAutoMoveDestination.y = transform.position.y; // 평면 접근
        lootAutoMoveActive = true;
        jumpRequested = false;
        jumpAnimationRequested = false;
        return true;
    }

    public void UpdateLootAutoMoveDestination(Vector3 destination)
    {
        if (!lootAutoMoveActive || controlAuthority != ActorControlAuthority.Player)
            return;

        lootAutoMoveDestination = destination;
        lootAutoMoveDestination.y = transform.position.y; // 이동 대상 갱신
    }

    public void CancelLootAutoMove()
    {
        if (!lootAutoMoveActive)
            return;

        lootAutoMoveActive = false;
        moveInput = Vector2.zero;
        moveDirection = Vector3.zero;
        locomotion?.Stop();
        isRunning = false;
        activeMovementIntent = ActorMovementIntent.Hold(
            transform.position,
            transform.forward,
            ResolveStoppedMovementGait());
    }

    private ActorMovementIntent CreatePlayerMovementIntent()
    {
        bool shouldMove = moveInput.sqrMagnitude > 0.001f
            && moveDirection.sqrMagnitude > 0.001f;
        ActorMovementGait gait = IsCombatMoveMode || isWalkMode
            ? ActorMovementGait.Walk
            : ActorMovementGait.Run;
        float baseSpeed = ResolveBaseMoveSpeed(gait, IsMeleeCombatLocomotionMode);
        float speedMultiplier = baseSpeed > 0.001f
            ? GetTargetMoveSpeed() / baseSpeed
            : 0f;
        return new ActorMovementIntent(
            transform.position + moveDirection,
            shouldMove ? moveDirection.normalized : Vector3.zero,
            shouldMove ? moveDirection.normalized : transform.forward,
            gait,
            speedMultiplier,
            ActorMovementPriority.PlayerControl,
            shouldMove);
    }

    private ActorMovementIntent CreateLootAutoMoveIntent()
    {
        Vector3 direction = lootAutoMoveDestination - transform.position;
        direction.y = 0f;
        bool shouldMove = direction.sqrMagnitude > 0.0001f;
        moveDirection = shouldMove ? direction.normalized : Vector3.zero;
        moveInput = shouldMove ? Vector2.up : Vector2.zero;
        isRunning = shouldMove; // 전용 접근도 기존 Run 출력 사용
        return new ActorMovementIntent(
            lootAutoMoveDestination,
            moveDirection,
            shouldMove ? moveDirection : transform.forward,
            ActorMovementGait.Run,
            1f,
            ActorMovementPriority.PlayerControl,
            shouldMove);
    }

    private void ReadAIMovementIntent()
    {
        bool hasCurrentIntent = submittedAIIntentFrame == Time.frameCount;
        activeMovementIntent = hasCurrentIntent
            ? submittedAIIntent
            : ActorMovementIntent.Hold(
                transform.position,
                transform.forward,
                activeMovementIntent.Gait);
        moveDirection = activeMovementIntent.ShouldMove
            ? activeMovementIntent.DesiredDirection
            : Vector3.zero;
        moveDirection.y = 0f;
        moveDirection = moveDirection.sqrMagnitude > 0.001f
            ? moveDirection.normalized
            : Vector3.zero;
        moveInput = activeMovementIntent.ShouldMove ? Vector2.up : Vector2.zero;
        isRunning = activeMovementIntent.ShouldMove
            && activeMovementIntent.Gait == ActorMovementGait.Run; // 보행 종류와 과속 분리
        jumpRequested = false;
        jumpAnimationRequested = false;
        submittedAIIntentFrame = -1; // 매 프레임 재제출
    }

    private float GetMeleeCombatMoveSpeed()
    {
        if (IsMeleeGuarding
            && playerEquipment != null
            && !playerEquipment.CanCurrentWeaponMoveWhileGuarding)
        {
            return 0f;
        }

        return Mathf.Max(0f, IsMeleeGuarding ? meleeCombatGuardMoveSpeed : meleeCombatMoveSpeed);
    }

    public void SetBagMoveSpeedBonusPercent(float percent)
    {
        bagMoveSpeedMultiplier = Mathf.Clamp(1f + Mathf.Max(0f, percent) * 0.01f, 0.05f, 10f);
    }

    private float GetActiveBuffMoveSpeedMultiplier()
    {
        if (playerBuffController == null)
            playerBuffController = ResolveBuffController();

        return playerBuffController != null ? playerBuffController.ActiveMoveSpeedMultiplier : 1f;
    }

    private PlayerBuffController ResolveBuffController()
    {
        PlayerBuffController controller = GetComponent<PlayerBuffController>();
        if (controller != null)
            return controller;

        controller = GetComponentInParent<PlayerBuffController>();
        if (controller != null)
            return controller;

        return GetComponentInChildren<PlayerBuffController>();
    }

    private float GetActiveAimMoveSpeedMultiplier()
    {
        if (playerEquipment != null
            && playerEquipment.HasCurrentWeapon
            && playerEquipment.CurrentWeaponContext.Aim.moveSpeedMultiplier > 0f)
        {
            return playerEquipment.CurrentWeaponContext.Aim.moveSpeedMultiplier;
        }

        return aimMoveSpeedMultiplier;
    }

    private void ApplyJump()
    {
        pendingJumpVelocity = 0f;
        if (!jumpRequested)
            return;

        jumpRequested = false; // 점프 소비

        pendingJumpVelocity = Mathf.Sqrt(Mathf.Max(0f, jumpHeight) * -2f * gravity); // 점프 속도
    }

    public void PrepareEvadeMotion()
    {
        Stop();
        jumpRequested = false;
        jumpAnimationRequested = false;
    }

    public Vector3 ResolveSafeEvadeDisplacement(Vector3 displacement)
    {
        displacement.y = 0f;
        return combatMotion != null
            ? combatMotion.ResolveSafeDisplacement(displacement)
            : displacement;
    }

    public void ResetMotionAfterTeleport()
    {
        Stop();
        if (characterMotor != null)
            characterMotor.ResetMotion();
        locomotion?.ResetMotion();
        isRunning = false;
        jumpRequested = false;
        jumpAnimationRequested = false;
        landingSlowTimer = 0f;
    }

    public void BeginQuickFireCombatMove(float holdTime, float moveSpeedMultiplier)
    {
        quickFireUntil = Mathf.Max(quickFireUntil, Time.time + Mathf.Max(0f, holdTime)); // QuickFire 시간
        activeQuickFireMoveSpeedMultiplier = moveSpeedMultiplier > 0f ? moveSpeedMultiplier : quickFireMoveSpeedMultiplier; // QuickFire 속도
        isRunning = false; // 달리기 차단
    }

    public void BeginExternalAim(float holdTime)
    {
        externalAimUntil = Mathf.Max(externalAimUntil, Time.time + Mathf.Max(0f, holdTime));
        isRunning = false;
    }

    public void BeginMeleeAttackMoveLock(float duration)
    {
        BeginMeleeAttackMoveLock(duration, transform.forward);
    }

    public void BeginMeleeAttackMoveLock(float duration, Vector3 lockedDirection)
    {
        meleeAttackMoveLockUntil = Time.time + Mathf.Max(0f, duration); // 이동 잠금
        lockedDirection.y = 0f;

        if (lockedDirection.sqrMagnitude <= 0.001f)
            lockedDirection = transform.forward;

        meleeAttackLockedDirection = lockedDirection.sqrMagnitude > 0.001f ? lockedDirection.normalized : Vector3.zero;
        meleeAttackRotationLocked = meleeAttackLockedDirection.sqrMagnitude > 0.001f;
        moveInput = Vector2.zero; // 이동 차단
        moveDirection = Vector3.zero; // 회전 차단
        isRunning = false; // 달리기 차단
        jumpRequested = false; // 점프 제거
        jumpAnimationRequested = false; // 점프 애니 제거
    }

    public void ApplyWeaponRootMotionDisplacement(Vector3 displacement)
    {
        if (combatMotion == null)
            return;
        Vector3 controllerVelocity = combatMotion.ApplyWeaponRootMotion(displacement);
        locomotion?.SetHorizontalVelocity(controllerVelocity);
    }

    private float ResolveCurrentBaseMoveSpeed()
    {
        return ResolveBaseMoveSpeed(
            activeMovementIntent.Gait,
            IsCombatWalkLocomotionMode);
    }

    private float ResolveBaseMoveSpeed(ActorMovementGait gait, bool useCombatSpeed)
    {
        if (useCombatSpeed)
            return ResolveAuthoredMoveSpeed(GetMeleeCombatMoveSpeed()); // 전투 전용 Walk 속도

        return gait == ActorMovementGait.Walk ? WalkMoveSpeed : RunMoveSpeed;
    }

    private ActorMovementGait ResolveStoppedMovementGait()
    {
        if (controlAuthority == ActorControlAuthority.Player)
        {
            return IsCombatMoveMode || isWalkMode
                ? ActorMovementGait.Walk
                : ActorMovementGait.Run;
        }

        return activeMovementIntent.Gait;
    }

    private float ResolveAuthoredMoveSpeed(float authoredSpeed)
    {
        return Mathf.Max(0f, authoredSpeed)
            * bagMoveSpeedMultiplier
            * GetActiveBuffMoveSpeedMultiplier();
    }

    public void CancelWeaponActionLocks()
    {
        meleeAttackMoveLockUntil = 0f;
        meleeAttackRotationLocked = false;
        meleeAttackLockedDirection = Vector3.zero;
        moveInput = Vector2.zero;
        moveDirection = Vector3.zero;
        isRunning = false;
        jumpRequested = false;
        jumpAnimationRequested = false;
        quickFireUntil = 0f;
        externalAimUntil = 0f;
    }

    public void CancelWeaponAimStateForSwitch()
    {
        isAiming = false;
        isMeleeCombatStance = false;
        externalAimUntil = 0f;
        aimBlockedUntilRelease = true;
        ResetSwordGuardTiming();
    }

    private PlayerInputFacade ResolveInputFacade()
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
