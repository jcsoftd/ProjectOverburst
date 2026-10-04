using UnityEngine;

// PlayerMovement partial: 이동 실행·정지·점프·회피 준비·순간이동 초기화·줍기 자동 이동·지면 확인. 필드와 Unity 수명주기는 PlayerMovement.cs에 있다.
public partial class PlayerMovement
{
    private bool combatStopWasMoving;
    private float combatStopDeceleration;
    private float combatMoveSeconds;
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
            ResolveCombatStopDeceleration(jumpVelocity, Mathf.Max(0f, deltaTime)),
            airControl,
            jumpVelocity,
            Mathf.Max(0f, deltaTime));
        if (result.didLand && result.landingFallSpeed < -landingMinFallSpeed)
            landingSlowTimer = landingSlowDuration; // 같은 Move에서 발생한 착지를 즉시 소비
    }

    private float ResolveCombatStopDeceleration(float jumpVelocity, float deltaTime)
    {
        var set = combatFacingController != null ? combatFacingController.Set : null;
        bool eligible = set != null && combatFacingController.IsPoseActive
            && IsMeleeCombatLocomotionMode && !IsLootAutoMoveActive
            && !IsEvading && !IsMeleeAttackMoveLocked && !IsConditionMovementBlocked && jumpVelocity <= 0f
            && !GameplayInputBlocker.IsGameplayInputBlocked
            && set.stopBrakeSeconds > 0f && set.stopBrakeMaxDistance > 0f;
        if (!eligible || activeMovementIntent.ShouldMove)
        {
            // A fresh input must not inherit the old Stop's velocity, including reversals.
            CancelCombatStopCurve(true);
            combatMoveSeconds = eligible && activeMovementIntent.ShouldMove
                ? combatMoveSeconds + deltaTime : 0f;
            combatStopWasMoving = eligible && activeMovementIntent.ShouldMove;
            combatStopDeceleration = 0f;
            return deceleration;
        }
        if (StepCombatStopCurve(set, deltaTime)) return 0f;
        float speed = locomotion.HorizontalVelocity.magnitude;
        if (combatStopWasMoving)
        {
            // Capture once on release; recomputing every frame creates a long tail.
            // The existing collision motor owns all displacement.
            combatStopDeceleration = Mathf.Max(speed / set.stopBrakeSeconds,
                speed * speed / (2f * set.stopBrakeMaxDistance));
            combatStopWasMoving = false;
            combatMoveSeconds = 0f;
        }
        if (speed <= .001f) combatStopDeceleration = 0f;
        return combatStopDeceleration > 0f ? combatStopDeceleration : deceleration;
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
        CancelCombatStopCurve(false);
        combatStopWasMoving = false;
        combatStopDeceleration = 0f;
        combatMoveSeconds = 0f;
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
        if (controlAuthority != ActorControlAuthority.Player || !isActiveAndEnabled || IsConditionMovementBlocked)
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
        combatFacingController?.ResetAfterTeleport();
        GetComponent<PlayerKnockdownController>()?.ResetReaction();
        Stop();
        if (characterMotor != null)
            characterMotor.ResetMotion();
        locomotion?.ResetMotion();
        isRunning = false;
        jumpRequested = false;
        jumpAnimationRequested = false;
        landingSlowTimer = 0f;
    }

    public void ApplyWeaponRootMotionDisplacement(Vector3 displacement, bool inheritLocomotionVelocity = true)
    {
        if (IsKnockedDown) return;
        if (combatMotion == null)
            return;
        Vector3 controllerVelocity = combatMotion.ApplyWeaponRootMotion(displacement);
        locomotion?.SetHorizontalVelocity(inheritLocomotionVelocity ? controllerVelocity : Vector3.zero);
    }

    public void PrepareKnockdownMotion()
    {
        movementInputSource?.ConsumeJumpRequest();
        movementInputSource?.ConsumeWalkToggleRequest();
        Stop();
        CancelWeaponActionLocks();
        isAiming = false;
        isMeleeCombatStance = false;
        aimBlockedUntilRelease = true;
        ResetSwordGuardTiming();
    }
}
