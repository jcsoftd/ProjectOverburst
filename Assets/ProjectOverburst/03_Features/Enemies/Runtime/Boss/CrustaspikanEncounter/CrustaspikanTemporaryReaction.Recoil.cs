using UnityEngine;

public sealed partial class CrustaspikanTemporaryReaction
{
    [SerializeField] private CrustaspikanParryRecoilProfile parryRecoilProfile;
    private CrustaspikanParryRecoilProfile.Motion authoredRecoil;
    public CrustaspikanParryRecoilProfile ParryRecoilProfile => parryRecoilProfile;
    public int AuthoredRecoilCount { get; private set; }
    public AnimationClip LastAuthoredRecoilClip { get; private set; }
    public int LastAuthoredRecoilStrike { get; private set; }
    public float LastAuthoredRecoilRate { get; private set; }
    public float LastAuthoredRecoilDuration { get; private set; }
    private float AuthoredRecoilDuration => authoredRecoil != null
        ? authoredRecoil.clip.length / Mathf.Max(.1f, LastAuthoredRecoilRate) : 0f;

    private bool PrepareAuthoredRecoil()
    {
        authoredRecoil = parryRecoilProfile != null ? parryRecoilProfile.FindCancelled(cancelledMaterial, cancelledNormalized) : null;
        if (authoredRecoil == null || !HasDazedMotion || animator == null
            || !animator.HasState(0, Animator.StringToHash("Base Layer." + authoredRecoil.state)))
        { authoredRecoil = null; return false; }
        CaptureBlendPose(); return true;
    }
    private void BeginAuthoredRecoil()
    {
        LastAuthoredRecoilClip = authoredRecoil.clip; LastAuthoredRecoilStrike = authoredRecoil.strikeIndex;
        LastAuthoredRecoilRate = Mathf.Max(.1f, parryRecoilProfile.playbackSpeed);
        LastAuthoredRecoilDuration = AuthoredRecoilDuration; LastRewindStart = cancelledNormalized;
        AuthoredRecoilCount++; Phase = ReactionPhase.AuthoredRecoil; elapsed = 0f; stageStartFrame = Time.frameCount;
        SampleAuthoredRecoil();
    }
    private void SampleAuthoredRecoil()
    {
        Sample(Animator.StringToHash("Base Layer." + authoredRecoil.state), Mathf.Clamp01(elapsed / AuthoredRecoilDuration));
        float blend = parryRecoilProfile.poseBlendSeconds;
        BlendCapturedPose(blend > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / blend)) : 1f);
    }
    private void TickAuthoredRecoil()
    {
        SampleAuthoredRecoil();
        if (elapsed < AuthoredRecoilDuration) return;
        if (groggy) BeginCollapse();
        else BeginDazed(true); // Authored end pose is already the standing Loop's first pose.
        authoredRecoil = null;
    }
}
