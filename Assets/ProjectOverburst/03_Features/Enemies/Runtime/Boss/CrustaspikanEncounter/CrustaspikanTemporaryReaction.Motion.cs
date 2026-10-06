using UnityEngine;

public sealed partial class CrustaspikanTemporaryReaction
{
    private EnemyMotionHandle reactionHandle;
    private float cancelledConsumedEnd;
    public EnemyMotionHandle ReactionHandle => reactionHandle;
    public int MissingOwnedSnapshotCount { get; private set; }
    private bool UsesMotion => actor != null && actor.AnimationBridge != null && actor.AnimationBridge.UsesOwnedMotion;
    private void CaptureOwnedCancelledAttack(EnemyBossAttackMaterial material)
    {
        var snapshot = executor.LastCancelledSnapshot;
        if (!Alive || BlocksActions || material == null || !snapshot.Valid || snapshot.Sample.Handle.Lease != actor.LeaseVersion)
        { MissingOwnedSnapshotCount++; return; }
        animator = actor.Animator;
        cancelledState = snapshot.Sample.StateHash; cancelledMaterial = material;
        cancelledNormalized = Mathf.Clamp01(snapshot.Sample.Normalized); cancelledFrame = snapshot.Sample.Frame;
        cancelledTime = snapshot.Sample.UnscaledTime; capturedLease = snapshot.Sample.Handle.Lease;
        cancelledConsumedEnd = snapshot.ConsumedStrikeEnd;
    }
    private bool TakeOwnedReaction()
    {
        if (!actor.AnimationBridge.OwnsMotion(reactionHandle))
        {
            var request = new EnemyMotionRequest { Owner = this, Role = EnemyMotionRole.Reaction, MotionId = "ReactionPose", Rate = 1f,
                ExternalCompletion = true, HoldLastPose = true, OnInvalidated = OwnedReactionInvalidated };
            if (!actor.AnimationBridge.TryBeginMotion(request, out reactionHandle, out _)) return false;
        }
        if (!ownsAnimator)
        {
            ownedLease = actor.LeaseVersion; hurtTarget = actor.GetComponent<CombatTarget>();
            if (hurtTarget != null) { hurtBefore = hurtTarget.CurrentHurtVolume; hurtBeforeLocal = actor.transform.InverseTransformPoint(hurtBefore.Center); }
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            { if (bone.name == "Crustaspikan_ Spine2") chestBone = bone; if (bone.name == "Crustaspikan_ Pelvis") pelvisBone = bone; }
            ownsAnimator = true;
        }
        float budget = rewindSeconds + reboundHoldSeconds + collapseBlendSeconds + collapseSeconds + recoverySeconds
            + Mathf.Max(proneSeconds, dazedSeconds + dazedBlendSeconds + dazedRecoverySeconds, reaction != null ? reaction.ParryStunRemaining : 0f) + .65f;
        actor.Movement.AcquireMotionLock(reactionHandle, budget); actor.Movement.StopMovement(); return true;
    }
    private void OwnedReactionInvalidated(EnemyMotionResult result)
    {
        if (result.Handle != reactionHandle) return;
        actor.Movement.ReleaseMotionLock(result.Handle); reactionHandle = default;
        if (result.Reason == EnemyMotionReason.Frozen) return; // Keep stage/hurt context; reacquire a fresh handle after thaw.
        hurtTarget?.ClearTemporaryHurtVolume(this); ownsAnimator = false;
        Phase = ReactionPhase.Ready; groggy = false; elapsed = 0f;
    }
    private void ReleaseOwnedReaction()
    {
        if (actor.AnimationBridge.OwnsMotion(reactionHandle)) actor.AnimationBridge.CancelMotion(reactionHandle, EnemyMotionReason.OwnerCancelled);
        hurtTarget?.ClearTemporaryHurtVolume(this); ownsAnimator = false; reactionHandle = default;
    }
}
