using UnityEngine;

public sealed partial class EnemyMovement
{
    private EnemyAnimationBridge motionBridge;
    private EnemyMotionHandle motionLockOwner, motionFacingOwner;
    private float motionLockUntil;
    private Quaternion committedMotionFacing;
    private bool keepMoveFacing;
    private Quaternion keptMoveFacing;
    public Quaternion PhysicalRotation => motor != null ? motor.Rotation : transform.rotation;
    public bool UsesMotionFacing => motionBridge != null && motionBridge.UsesOwnedMotion;
    public bool HasCommittedMotionFacing => motionFacingOwner.IsValid;
    public bool IsOwnedTurning => UsesMotionFacing && locomotionAnimator != null && locomotionAnimator.IsTurning;
    public float FacingPreparationBudget => locomotionAnimator != null ? locomotionAnimator.FacingBudget : 0f;

    public bool IsFacingForAttack(Vector3 position, float tolerance)
    {
        if (!UsesMotionFacing) return IsFacingForAttack(position);
        if (IsOwnedTurning || motionBridge.HasOwnedBlockingMotion || motionBridge.HasInvalidMotionProfile || IsStatusMovementLocked) return false;
        Vector3 direction = position - transform.position; direction.y = 0f;
        return direction.sqrMagnitude < .0001f || Vector3.Angle(PhysicalRotation * Vector3.forward, direction) <= tolerance;
    }

    public bool AcquireMotionLock(in EnemyMotionHandle handle, float seconds, bool commitFacing = false, Quaternion facing = default)
    {
        if (motionBridge == null || !motionBridge.OwnsMotion(handle) || !EnemyMotionPlaybackProfile.FinitePositive(seconds)) return false;
        motionLockOwner = handle; motionLockUntil = Time.time + seconds;
        if (commitFacing) { motionFacingOwner = handle; committedMotionFacing = facing; }
        return true;
    }
    public bool RefreshMotionLock(in EnemyMotionHandle handle, float seconds)
    {
        if (motionBridge == null || !motionBridge.OwnsMotion(handle) || motionLockOwner != handle || !EnemyMotionPlaybackProfile.FinitePositive(seconds)) return false;
        motionLockUntil = Mathf.Max(motionLockUntil, Time.time + seconds); return true;
    }
    public bool AcquireMotionFacing(in EnemyMotionHandle handle, Quaternion facing)
    { if (motionBridge == null || !motionBridge.OwnsMotion(handle)) return false; motionFacingOwner = handle; committedMotionFacing = facing; return true; }
    public void ReleaseMotionLock(in EnemyMotionHandle handle, bool releaseFacing = true)
    {
        if (motionLockOwner == handle) { motionLockOwner = default; motionLockUntil = 0f; ClearAttackDisplacement(); }
        if (releaseFacing && motionFacingOwner == handle) motionFacingOwner = default;
    }
    public bool RequestOwnedAttackDisplacement(in EnemyMotionHandle handle, Vector3 displacement)
    { return motionBridge != null && motionBridge.OwnsMotion(handle) && motionLockOwner == handle && RequestAttackDisplacement(displacement); }
    public void ClearOwnedAttackDisplacement(in EnemyMotionHandle handle)
    { if (motionLockOwner == handle) ClearAttackDisplacement(); }

    public void SetMoveFacingPolicy(bool keepStart)
    { keepMoveFacing = keepStart; keptMoveFacing = PhysicalRotation; }
    private void ResetMotionMovement()
    { motionLockOwner = motionFacingOwner = default; motionLockUntil = 0f; keepMoveFacing = false; }
    private bool TickOwnedFacing()
    {
        if (!UsesMotionFacing || motor == null) return false;
        if (motionFacingOwner.IsValid) motor.ApplyFacingRotation(committedMotionFacing);
        if (locomotionAnimator.IsTurning) return locomotionAnimator.TickFacingTurn(motor);
        if (IsActionLocked || IsStatusMovementLocked || motionBridge.HasOwnedBlockingMotion || reaction != null && reaction.IsHitStunActive) return false;
        Vector3 direction = Vector3.zero;
        if (!hasDestination && Time.time < facingRequestUntil) direction = requestedFacingPosition - motor.Position;
        else if (hasDestination && !keepMoveFacing && !motionFacingOwner.IsValid)
        {
            direction = hasFacingPosition ? facingPosition - motor.Position : destination - motor.Position;
            direction.y = 0f;
            if (Vector3.Angle(PhysicalRotation * Vector3.forward, direction) < motionBridge.PlaybackProfile.MovingTurnThreshold) return false;
        }
        direction.y = 0f;
        if (direction.sqrMagnitude < .0001f || !locomotionAnimator.BeginFacingTurn(direction)) return false;
        return locomotionAnimator.TickFacingTurn(motor);
    }
    private void ResolveOwnedMoveFacing(ref float turnSpeed, ref Vector3 direction)
    {
        if (!UsesMotionFacing) return;
        // Deliberate strafing/backsteps keep the accepted body direction. Ordinary travel turns via the turn owner.
        turnSpeed = 0f;
        direction = motionFacingOwner.IsValid ? committedMotionFacing * Vector3.forward
            : keepMoveFacing ? keptMoveFacing * Vector3.forward : PhysicalRotation * Vector3.forward;
    }
}
