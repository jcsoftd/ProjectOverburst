using UnityEngine;

public sealed partial class EnemyLocomotionAnimator
{
    private EnemyMotionHandle facingHandle, locomotionHandle;
    private EnemyMotionPlaybackProfile.Binding facingBinding;
    private float settleBegan = -1f;
    private EnemyMotionResult facingResult;
    public float FacingBudget { get; private set; }
    public EnemyMotionResult FacingResult => facingResult;
    public EnemyMotionHandle FacingHandle => facingHandle;
    public EnemyMotionHandle LocomotionHandle => locomotionHandle;

    private bool BeginOwnedFacingTurn(Vector3 direction)
    {
        if (IsTurning || Time.frameCount == completedTurnFrame || movement == null || animationBridge.HasOwnedBlockingMotion || animationBridge.IsFrozen) return false;
        direction.y = 0f; if (direction.sqrMagnitude < .0001f) return false;
        float angle = Vector3.SignedAngle(movement.PhysicalRotation * Vector3.forward, direction, Vector3.up);
        if (Mathf.Abs(angle) <= animationBridge.PlaybackProfile.FacingTolerance) return false;
        bool halfTurn = Mathf.Abs(angle) > 90f && !Mathf.Approximately(Mathf.Abs(angle), 90f);
        string id = halfTurn ? (angle < 0f ? "Turn180Left" : "Turn180Right") : (angle < 0f ? "Turn90Left" : "Turn90Right");
        facingBinding = animationBridge.PlaybackProfile.Find(id);
        if (facingBinding == null || facingBinding.turnProgress == null) return false;
        turnStart = movement.PhysicalRotation; turnDirection = angle;
        turnEnd = Quaternion.AngleAxis(angle, Vector3.up) * turnStart;
        float rate = facingBinding.rate * Mathf.Clamp(facingBinding.authoredYaw / Mathf.Abs(angle), 1f, 2f);
        FacingBudget = animationBridge.PlaybackProfile.DurationBudget(facingBinding, rate);
        var request = new EnemyMotionRequest { Owner = this, Role = EnemyMotionRole.Turn, MotionId = id,
            Rate = rate, Magnitude = Mathf.Abs(angle) / facingBinding.authoredYaw,
            BudgetSeconds = FacingBudget, ExternalCompletion = true,
            OnInvalidated = OnFacingInvalidated, OnTerminated = OnFacingTerminated };
        if (!animationBridge.TryBeginMotion(request, out facingHandle, out _)) return false;
        movement.AcquireMotionLock(facingHandle, FacingBudget);
        IsTurning = true; turnEntered = false; settleBegan = -1f; turnBegan = Time.time;
        facingResult = new EnemyMotionResult(facingHandle, EnemyMotionState.Entering);
        return true;
    }
    private void OnFacingInvalidated(EnemyMotionResult result)
    { if (result.Handle != facingHandle) return; movement?.ReleaseMotionLock(result.Handle); IsTurning = false; turnEntered = false; }
    private void OnFacingTerminated(EnemyMotionResult result)
    { if (result.Handle != facingHandle) return; facingResult = result; if (result.State == EnemyMotionState.Completed) { completedTurnFrame = Time.frameCount; ai?.NotifyFacingTurnCompleted(); } }
    private bool TickOwnedFacingTurn(EnemyMotor motor)
    {
        if (!IsTurning || motor == null) return false;
        motor.HoldPosition();
        if (animationBridge.CurrentMotionState == EnemyMotionState.BlendingOut) return true;
        if (!animationBridge.TryReadMotion(facingHandle, out var sample))
        { if (Time.time - turnBegan > animationBridge.PlaybackProfile.EntryTimeout) animationBridge.FailMotion(facingHandle, EnemyMotionReason.EntryTimeout); return true; }
        turnEntered = true;
        float progress = Mathf.Clamp01(facingBinding.turnProgress.Evaluate(Mathf.Clamp01(sample.Normalized)));
        motor.ApplyFacingRotation(Quaternion.AngleAxis(turnDirection * progress, Vector3.up) * turnStart);
        bool aligned = Quaternion.Angle(motor.Rotation, turnEnd) <= .25f;
        if (sample.Normalized >= facingBinding.completeNormalized && aligned)
        {
            if (settleBegan < 0f) settleBegan = Time.time;
            if (Time.time - settleBegan >= facingBinding.settleSeconds) animationBridge.TryCompleteOwnedMotion(facingHandle);
        }
        else settleBegan = -1f;
        if (IsTurning && Time.time - turnBegan > FacingBudget) animationBridge.FailMotion(facingHandle, EnemyMotionReason.DeadlineExceeded);
        return true;
    }
    private void UpdateOwnedLocomotion(Vector3 displacement)
    {
        if (animationBridge == null || !animationBridge.UsesOwnedMotion || animationBridge.HasOwnedBlockingMotion || animationBridge.IsFrozen
            || movement == null || movement.IsStatusMovementLocked) return;
        if (!animationBridge.OwnsMotion(locomotionHandle))
        {
            var request = new EnemyMotionRequest { Owner = this, Role = EnemyMotionRole.Locomotion, MotionId = "Locomotion", Rate = 1f };
            if (!animationBridge.TryBeginMotion(request, out locomotionHandle, out _)) return;
        }
        float dt = Time.deltaTime;
        if (dt <= .00001f) return;
        bool teleport = displacement.magnitude > Mathf.Max(.75f, movement.ActiveMoveSpeed * dt * 4f);
        float actualSpeed = teleport ? 0f : displacement.magnitude / dt;
        observedSpeed = Mathf.Lerp(observedSpeed, actualSpeed, 1f - Mathf.Exp(-dt / .06f));
        bool moving = !teleport && actualSpeed > .04f && movement.HasDestination && !movement.IsActionLocked;
        Vector3 local = Quaternion.Inverse(movement.PhysicalRotation) * displacement;
        Vector2 axis = moving ? new Vector2(local.x, local.z).normalized : Vector2.zero;
        string id = Mathf.Abs(axis.x) > Mathf.Abs(axis.y) ? (axis.x < 0f ? "WalkLeft" : "WalkRight") : (axis.y < 0f ? "WalkBackwards" : "WalkForward");
        var binding = animationBridge.PlaybackProfile.Find(id);
        float stride = binding != null ? binding.strideSpeed : referenceSpeed;
        float rate = moving ? Mathf.Clamp(observedSpeed / Mathf.Max(.01f, stride), .01f, 8f) : 1f;
        animationBridge.TryUpdateOwnedMotionIntent(locomotionHandle, new EnemyMotionIntent(axis, rate));
    }
}
