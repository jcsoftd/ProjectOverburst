using UnityEngine;

public sealed partial class CrustaspikanTemporaryReaction
{
    public const string ParryDazedStateName = "Material_ParryDazed";
    public const string ParryDazedEnterStateName = "Material_ParryDazedEnter";
    public const string ParryDazedRecoverStateName = "Material_ParryDazedRecover";
    [SerializeField] private AnimationClip parryDazedEnterClip;
    [SerializeField] private AnimationClip parryDazedRecoverClip;
    [SerializeField] private AnimationClip parryDazedClip;
    [SerializeField, Min(.05f)] private float dazedBlendSeconds = .22f;
    [SerializeField, Min(.05f)] private float dazedRecoverySeconds = .38f;
    [SerializeField, Min(1f)] private float minimumDazedCycles = 1f;
    private float dazedSeconds;
    public AnimationClip ParryDazedClip => parryDazedClip;
    public AnimationClip ParryDazedEnterClip => parryDazedEnterClip;
    public AnimationClip ParryDazedRecoverClip => parryDazedRecoverClip;
    private int DazedEnterState => Animator.StringToHash("Base Layer." + ParryDazedEnterStateName);
    private int DazedRecoverState => Animator.StringToHash("Base Layer." + ParryDazedRecoverStateName);
    private bool HasDazedTransitions => parryDazedEnterClip != null && parryDazedRecoverClip != null
        && animator != null && animator.HasState(0, DazedEnterState) && animator.HasState(0, DazedRecoverState);
    private float DazedEnterDuration => HasDazedTransitions ? parryDazedEnterClip.length : dazedBlendSeconds;
    private float DazedRecoverDuration => HasDazedTransitions ? parryDazedRecoverClip.length : dazedRecoverySeconds;
    public int DazedCount { get; private set; }
    public float LastDazedCycles { get; private set; }
    private int DazedState => Animator.StringToHash("Base Layer." + ParryDazedStateName);
    private bool HasDazedMotion => parryDazedClip != null && parryDazedClip.isLooping
        && animator != null && animator.HasState(0, DazedState);

    public bool TryPlayParryDaze(float stunSeconds)
    {
        if (!ResolveMotions() || !HasDazedMotion || groggy && BlocksActions) return false;
        dazedSeconds = Mathf.Max(stunSeconds, parryDazedClip.length * minimumDazedCycles);
        if (!TakeOwnership()) return false;
        groggy = false; BeginDazed(); applyingLock = true;
        try { reaction?.ApplyBossStun(dazedSeconds + DazedEnterDuration + DazedRecoverDuration); }
        finally { applyingLock = false; }
        return true;
    }

    private void CaptureBlendPose()
    {
        if (blendBones == null)
        {
            blendBones = animator.GetComponentsInChildren<Transform>(true);
            blendPositions = new Vector3[blendBones.Length]; blendScales = new Vector3[blendBones.Length];
            blendRotations = new Quaternion[blendBones.Length];
        }
        for (int i = 0; i < blendBones.Length; i++)
        { blendPositions[i] = blendBones[i].localPosition; blendRotations[i] = blendBones[i].localRotation; blendScales[i] = blendBones[i].localScale; }
    }

    private void BlendCapturedPose(float amount)
    {
        if (amount >= 1f || UsesMotion && !actor.AnimationBridge.OwnsMotion(reactionHandle)) return;
        for (int i = 0; i < blendBones.Length; i++)
        {
            var bone = blendBones[i]; if (bone == null || bone == animator.transform) continue;
            bone.localPosition = Vector3.Lerp(blendPositions[i], bone.localPosition, amount);
            bone.localRotation = Quaternion.Slerp(blendRotations[i], bone.localRotation, amount);
            bone.localScale = Vector3.Lerp(blendScales[i], bone.localScale, amount);
        }
    }

    private void BeginDazed(bool fromAuthoredRecoil = false)
    {
        if (!HasDazedMotion) { FinishStandingReaction(); return; }
        dazedSeconds = Mathf.Max(dazedSeconds, parryDazedClip.length * minimumDazedCycles);
        CaptureBlendPose(); Phase = fromAuthoredRecoil ? ReactionPhase.Dazed : ReactionPhase.DazedEnter; elapsed = 0f; stageStartFrame = Time.frameCount;
        DazedCount++; LastDazedCycles = 0f;
        Sample(fromAuthoredRecoil ? DazedState : HasDazedTransitions ? DazedEnterState : DazedState, 0f);
        if (!fromAuthoredRecoil) BlendCapturedPose(0f);
    }

    private void TickDazed()
    {
        if (Phase == ReactionPhase.DazedEnter)
        {
            Sample(HasDazedTransitions ? DazedEnterState : DazedState,
                HasDazedTransitions ? Mathf.Clamp01(elapsed / DazedEnterDuration) : 0f);
            BlendCapturedPose(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dazedBlendSeconds)));
            if (elapsed >= DazedEnterDuration) { Phase = ReactionPhase.Dazed; elapsed = 0f; stageStartFrame = Time.frameCount; }
        }
        else if (Phase == ReactionPhase.Dazed)
        {
            Sample(DazedState, Mathf.Repeat(elapsed / parryDazedClip.length, 1f));
            LastDazedCycles = elapsed / parryDazedClip.length;
            if (elapsed >= dazedSeconds)
            { CaptureBlendPose(); Phase = ReactionPhase.DazedRecover; elapsed = 0f; stageStartFrame = Time.frameCount; }
        }
        else
        {
            if (HasDazedTransitions)
            {
                Sample(DazedRecoverState, Mathf.Clamp01(elapsed / DazedRecoverDuration));
                BlendCapturedPose(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dazedBlendSeconds)));
            }
            else
            {
                Sample(Animator.StringToHash("Base Layer." + idle.state), Mathf.Repeat(elapsed / idle.runtime.length, 1f));
                BlendCapturedPose(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dazedRecoverySeconds)));
            }
            if (elapsed >= DazedRecoverDuration) FinishStandingReaction();
        }
    }

    private void FinishStandingReaction()
    {
        RecoveryCount++; ReleaseAnimator(); Phase = ReactionPhase.Ready; groggy = false;
        if (!UsesMotion && animator != null) animator.CrossFadeInFixedTime("Locomotion", .12f, 0, 0f);
    }
}
