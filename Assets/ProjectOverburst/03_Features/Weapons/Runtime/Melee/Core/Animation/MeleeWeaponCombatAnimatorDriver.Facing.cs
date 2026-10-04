using UnityEngine;

public partial class MeleeWeaponCombatAnimatorDriver
{
    private enum FacingMotion { None, Idle, Start, Loop, Stop, Turn }
    private FacingMotion facingMotion;
    private CombatMoveMotion activeMoveMotion;
    private CombatTurnMotion activeFacingTurn;
    private float facingMotionEnd, facingTurnStart;
    private float facingStartDuration;
    private PlayerCombatFacingController facingController;

    public CombatLocomotionSet FacingSet => activeProfile != null ? activeProfile.combatLocomotionSet : null;
    public bool CanUseCombatFacing => FacingSet != null && combatRequested && !legacySuppressed
        && activeAction == DriverAction.None && playerMovement != null && !playerMovement.IsKnockedDown;
    public bool CanStartFacingTurn => CanUseCombatFacing && facingMotion == FacingMotion.Idle
        && !targetAnimator.IsInTransition(layerIndex);
    public CombatTurnMotion ActiveFacingTurn => activeFacingTurn;
    public bool IsStationaryFacingPose => facingMotion == FacingMotion.Idle || facingMotion == FacingMotion.Turn;
    public float FacingTurnElapsed => Mathf.Clamp(Time.time - facingTurnStart, 0f, activeFacingTurn?.Duration ?? 0f);

    private void InitializeFacing()
    {
        if (!Application.isPlaying || playerMovement == null || targetAnimator == null) return;
        if (facingController == null) facingController = playerMovement.GetComponent<PlayerCombatFacingController>();
        facingController?.Bind(playerMovement, this, targetAnimator);
    }
    public bool TryStartFacingTurn(CombatTurnMotion motion)
    {
        if (!CanStartFacingTurn || motion == null || motion.clip == null || motion.yaw == null
            || !PlayState(layerIndex, layerName, motion.stateName, FacingSet.turnBlendSeconds, 0, motion.Duration))
            return false;
        activeFacingTurn = motion;
        facingTurnStart = Time.time;
        facingMotionEnd = Time.time + motion.Duration;
        facingMotion = FacingMotion.Turn;
        return true;
    }
    public void CancelFacingTurn()
    {
        activeFacingTurn = null;
        if (facingMotion == FacingMotion.Turn) facingMotion = FacingMotion.None;
    }
    private void ResetFacingMotion()
    {
        CancelFacingTurn();
        facingMotion = FacingMotion.None;
        activeMoveMotion = null;
        facingStartDuration = 0f;
    }
    private float FacingMoveDuration(CombatMoveMotion motion, AnimationClip clip)
    {
        var set = FacingSet;
        if (set == null || !set.scaleStartStopWithLocomotionSpeed || activeProfile == null)
            return clip.length;
        int sector = System.Array.IndexOf(set.directions, motion);
        if (sector < 0 || motion.authoredSpeed <= .05f) return clip.length;
        Vector3 direction = Quaternion.Euler(0, sector * 45f, 0) * Vector3.forward;
        float speed = activeProfile.locomotionReferenceSpeeds.GetSpeed(direction)
            * ResolvePositiveOrDefault(activeProfile.locomotionAnimationSpeedMultiplier, 1f);
        return clip.length / Mathf.Max(.01f, speed / motion.authoredSpeed);
    }
    private bool TryPlayDetailedLocomotion(float blend)
    {
        var set = FacingSet;
        if (set == null || !CanUseCombatFacing || playerMovement.IsMeleeGuarding) return false;
        bool moving = IsMovingInputActive();
        if (activeFacingTurn != null)
        {
            if (!moving && Time.time < facingMotionEnd) return true;
            activeFacingTurn = null;
            facingMotion = FacingMotion.None;
            blend = set.moveBlendSeconds;
        }
        Vector3 local = Quaternion.Inverse(Quaternion.Euler(0, facingController != null
            ? facingController.LowerYaw : transform.eulerAngles.y, 0)) * playerMovement.MoveDirection;
        var direction = set.GetDirection(local);
        if (moving && direction != null)
        {
            if (facingMotion == FacingMotion.Idle || facingMotion == FacingMotion.None)
            {
                activeMoveMotion = direction;
                if (!PlayState(layerIndex, layerName, direction.startState, set.moveBlendSeconds, 0, direction.start.length)) return false;
                facingMotion = FacingMotion.Start;
                facingStartDuration = FacingMoveDuration(direction, direction.start);
                facingMotionEnd = Time.time + facingStartDuration;
            }
            else if (facingMotion == FacingMotion.Start && Time.time < facingMotionEnd && direction != activeMoveMotion)
            {
                // Lower-body alignment can cross a sector during the first step. Carry that
                // step into the new directional Start instead of dropping straight into Run.
                float progress = activeMoveMotion != null
                    ? Mathf.Clamp01(1 - (facingMotionEnd - Time.time) / Mathf.Max(.01f, facingStartDuration)) : 0;
                PlayState(layerIndex, layerName, direction.startState, set.moveBlendSeconds, progress, direction.start.length);
                facingStartDuration = FacingMoveDuration(direction, direction.start);
                facingMotionEnd = Time.time + (1 - progress) * facingStartDuration;
                activeMoveMotion = direction;
            }
            else if (facingMotion == FacingMotion.Stop
                || (facingMotion == FacingMotion.Start && Time.time >= facingMotionEnd))
            {
                float phase = activeMoveMotion != null ? Mathf.Repeat(activeMoveMotion.startLoopPhase - activeMoveMotion.loopCycleOffset, 1) : 0;
                PlayState(layerIndex, layerName, locomotionStateName, set.moveBlendSeconds, phase, direction.loop.length);
                facingMotion = FacingMotion.Loop;
                activeMoveMotion = direction;
            }
            else if (facingMotion == FacingMotion.Loop) activeMoveMotion = direction;
        }
        else if (facingMotion == FacingMotion.Loop || facingMotion == FacingMotion.Start)
        {
            if (activeMoveMotion == null) { ResetFacingMotion(); return true; }
            PlayState(layerIndex, layerName, activeMoveMotion.stopState, set.moveBlendSeconds, 0, activeMoveMotion.stop.length);
            facingMotion = FacingMotion.Stop;
            facingMotionEnd = Time.time + FacingMoveDuration(activeMoveMotion, activeMoveMotion.stop);
        }
        else if (facingMotion == FacingMotion.None || (facingMotion == FacingMotion.Stop && Time.time >= facingMotionEnd))
        {
            PlayState(set.idleState, Mathf.Max(set.moveBlendSeconds, blend), 0);
            facingMotion = FacingMotion.Idle;
        }
        return true;
    }
}
