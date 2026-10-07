using UnityEngine;

// Owned temporary reactions: authored standing daze for parry, existing non-RM fall/get-up for groggy.
[DisallowMultipleComponent]
[DefaultExecutionOrder(11000)]
public sealed partial class CrustaspikanTemporaryReaction : MonoBehaviour
{
    public enum ReactionPhase { Ready, Rewind, ReboundHold, Collapse, Prone, Recover, DazedEnter, Dazed, DazedRecover, AuthoredRecoil }
    [SerializeField, Min(.03f)] private float rewindSeconds = .7f;
    [SerializeField, Min(0f)] private float reboundHoldSeconds = .14f;
    [SerializeField, Min(.05f)] private float collapseBlendSeconds = .32f;
    [SerializeField, Range(0f, 1f)] private float rewindKeep = .4f;
    [SerializeField, Min(.05f)] private float maximumRewindClipSeconds = .45f;
    [SerializeField, Range(0f, .9f)] private float deathFallStart = .42f;
    [SerializeField, Range(.5f, 1f)] private float deathProneStart = .78f;
    [SerializeField, Min(.05f)] private float collapseSeconds = 1.05f;
    [SerializeField, Min(.05f)] private float recoverySeconds = 2.6f;
    [SerializeField] private AnimationClip getUpClip;
    private EnemyActor actor;
    private EnemyBossMaterialExecutor executor;
    private EnemyBossCombatDirector director;
    private EnemyMovementReaction reaction;
    private Animator animator;
    private EnemyBossMaterialCollection.Motion death, idle;
    private float elapsed, proneSeconds, speedBefore, cancelledTime;
    private float cancelledNormalized;
    private int cancelledState, cancelledFrame, parrySeen, stageStartFrame;
    private uint capturedLease, ownedLease;
    private AnimatorUpdateMode updateBefore;
    private bool ownsAnimator, groggy;
    private bool applyingLock;
    private EnemyBossAttackMaterial cancelledMaterial;
    private Transform[] blendBones;
    private Vector3[] blendPositions, blendScales;
    private Quaternion[] blendRotations;
    private int[] triggerHashes;
    private float collapseFrom;
    private CombatTarget hurtTarget;
    private CombatTargetVolume hurtBefore;
    private Vector3 hurtBeforeLocal;
    private Transform chestBone, pelvisBone;
    public ReactionPhase Phase { get; private set; }
    public bool BlocksActions => Phase != ReactionPhase.Ready;
    public bool IsGroggyAnimating => groggy && BlocksActions;
    public float SampledNormalizedTime { get; private set; }
    public float LastRewindStart { get; private set; }
    public float LastRewindEnd { get; private set; }
    public int RewindCount { get; private set; }
    public int RecoveryCount { get; private set; }
    public AnimationClip GetUpClip => getUpClip;

    private void Awake()
    {
        actor = GetComponent<EnemyActor>(); executor = GetComponent<EnemyBossMaterialExecutor>();
        director = GetComponent<EnemyBossCombatDirector>(); reaction = GetComponent<EnemyMovementReaction>();
        animator = actor != null ? actor.Animator : GetComponentInChildren<Animator>(true);
    }
    private void OnEnable()
    {
        parrySeen = director != null ? director.ParryCount : 0;
        if (executor != null) executor.AttackCancelled += CaptureCancelledAttack;
        if (reaction != null) reaction.ReactionStarted += ReactionStarted;
        if (actor != null && actor.Health != null) actor.Health.OnDead += Died;
    }
    private void OnDisable()
    {
        if (executor != null) executor.AttackCancelled -= CaptureCancelledAttack;
        if (reaction != null) reaction.ReactionStarted -= ReactionStarted;
        if (actor != null && actor.Health != null) actor.Health.OnDead -= Died;
        Cancel();
    }
    private void Died(CombatHealth health, DamageInfo info) => Cancel();
    private void ReactionStarted()
    {
        if (applyingLock || !Alive || reaction == null) return;
        ObserveParry();
        // The existing encounter and common director both announce groggy through this forced boss stun.
        float shortRecoil = director != null && director.Profile != null ? director.Profile.parryRecoil : 1f;
        if (!groggy && (director != null && director.IsGroggy || !BlocksActions && reaction.ParryStunRemaining > shortRecoil + .1f)) TryPlayGroggy(reaction.ParryStunRemaining);
    }
    private bool Alive => actor != null && actor.IsLeased && actor.Health != null && !actor.Health.IsDead;
    private bool ResolveMotions()
    {
        animator = actor != null ? actor.Animator : null;
        death = executor != null ? executor.Collection?.FindMotion("Death") : null;
        idle = executor != null ? executor.Collection?.FindMotion("IdleBreathe") : null;
        return Alive && animator != null && animator.isActiveAndEnabled && !animator.applyRootMotion
            && death?.IsPlayable == true && idle?.IsPlayable == true
            && animator.HasState(0, Animator.StringToHash("Base Layer." + death.state));
    }
    private void CaptureCancelledAttack(EnemyBossAttackMaterial material)
    {
        if (UsesMotion) { CaptureOwnedCancelledAttack(material); return; }
        if (!Alive || BlocksActions || material == null || actor.AnimationBridge == null
            || !actor.AnimationBridge.TryGetAttackMotionTime(material.ability.AnimatorTrigger, material.runtimeClip, out float time)) return;
        animator = actor.Animator;
        if (animator == null || !material.ability.AnimatorTrigger.StartsWith("Attack", System.StringComparison.Ordinal)) return;
        var current = animator.GetCurrentAnimatorStateInfo(0);
        var next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : current;
        // The bridge prefers a matching incoming attack during a transition.
        int expected = Animator.StringToHash("Attack_" + material.ability.AnimatorTrigger.Substring("Attack".Length));
        cancelledState = next.shortNameHash == expected ? next.fullPathHash : current.fullPathHash;
        cancelledMaterial = material; cancelledNormalized = Mathf.Clamp01(time); cancelledFrame = Time.frameCount;
        cancelledTime = Time.unscaledTime; capturedLease = actor.LeaseVersion;
    }
    private void ObserveParry()
    {
        if (director == null || director.ParryCount == parrySeen) return;
        int previous = parrySeen; parrySeen = director.ParryCount;
        if (parrySeen < previous || BlocksActions || reaction == null || !reaction.IsParryStunned || !ResolveMotions()) return;
        bool captured = capturedLease == actor.LeaseVersion && Time.frameCount - cancelledFrame <= 2
            && Time.unscaledTime - cancelledTime < .25f && cancelledState != 0 && cancelledNormalized > .02f;
        if (!captured) return; // Ordinary cancellations and incomplete/normal parries never masquerade as a rewind.
        bool useAuthoredRecoil = PrepareAuthoredRecoil();
        if (!TakeOwnership()) return; groggy = false; dazedSeconds = Mathf.Max(director.Profile.parryRecoil, parryDazedClip != null ? parryDazedClip.length * minimumDazedCycles : 0f);
        if (useAuthoredRecoil) { BeginAuthoredRecoil(); return; }
        LastRewindStart = cancelledNormalized;
        LastRewindEnd = Mathf.Max(cancelledNormalized * rewindKeep, cancelledNormalized - maximumRewindClipSeconds / cancelledMaterial.runtimeClip.length);
        // A last-hit parry must not rewind through a preceding strike in a multi-hit clip.
        for (int i = 0; i < cancelledMaterial.strikes.Length - 1; i++)
            if (cancelledMaterial.strikes[i].contactEnd < cancelledNormalized)
                LastRewindEnd = Mathf.Max(LastRewindEnd, cancelledMaterial.strikes[i].contactEnd);
        if (UsesMotion) LastRewindEnd = Mathf.Max(LastRewindEnd, cancelledConsumedEnd);
        RewindCount++; Phase = ReactionPhase.Rewind; elapsed = 0f; stageStartFrame = Time.frameCount;
        Sample(cancelledState, LastRewindStart);
    }
    public bool TryPlayGroggy(float stunSeconds)
    {
        ObserveParry();
        if (!ResolveMotions()) return false;
        bool keepRewind = Phase == ReactionPhase.Rewind || Phase == ReactionPhase.ReboundHold || Phase == ReactionPhase.AuthoredRecoil;
        if (!TakeOwnership()) return false; groggy = true; proneSeconds = Mathf.Max(.25f, stunSeconds - collapseSeconds - collapseBlendSeconds);
        if (Phase == ReactionPhase.Prone) elapsed = 0f;
        else if (!keepRewind && Phase != ReactionPhase.Collapse) BeginCollapse();
        applyingLock = true;
        try { reaction?.ApplyBossStun(stunSeconds); }
        finally { applyingLock = false; }
        return true;
    }
    private bool TakeOwnership()
    {
        if (UsesMotion) return TakeOwnedReaction();
        if (!ownsAnimator)
        {
            ownedLease = actor.LeaseVersion; speedBefore = animator.speed; updateBefore = animator.updateMode;
            hurtTarget = actor.GetComponent<CombatTarget>();
            if (hurtTarget != null) { hurtBefore = hurtTarget.CurrentHurtVolume; hurtBeforeLocal = actor.transform.InverseTransformPoint(hurtBefore.Center); }
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            { if (bone.name == "Crustaspikan_ Spine2") chestBone = bone; if (bone.name == "Crustaspikan_ Pelvis") pelvisBone = bone; }
            ownsAnimator = true; animator.updateMode = AnimatorUpdateMode.Normal;
            var triggers = new System.Collections.Generic.List<int>();
            foreach (var p in animator.parameters) if (p.type == AnimatorControllerParameterType.Trigger && p.name != "Death") triggers.Add(p.nameHash);
            triggerHashes = triggers.ToArray();
        }
        animator.speed = 0f; actor.Movement?.StopMovement();
        return true;
    }
    private void ResetActionTriggers()
    {
        if (triggerHashes != null) foreach (int hash in triggerHashes) animator.ResetTrigger(hash);
    }
    private void Sample(int stateHash, float normalized)
    {
        if (UsesMotion)
        {
            if (actor.AnimationBridge.TrySampleOwnedPose(reactionHandle, stateHash, Mathf.Clamp01(normalized))) SampledNormalizedTime = Mathf.Clamp01(normalized);
            return;
        }
        ResetActionTriggers(); animator.speed = 0f;
        animator.Play(stateHash, 0, Mathf.Clamp01(normalized)); animator.Update(0f);
        SampledNormalizedTime = Mathf.Clamp01(normalized);
    }
    private int GetUpState => Animator.StringToHash("Base Layer.Material_TemporaryGetUp");
    private int DeathState => Animator.StringToHash("Base Layer." + death.state);
    private void BeginCollapse()
    {
        collapseFrom = Phase == ReactionPhase.Recover ? (getUpClip != null ? Mathf.Lerp(deathProneStart, deathFallStart, SampledNormalizedTime) : SampledNormalizedTime) : deathFallStart;
        CaptureBlendPose();
        Phase = ReactionPhase.Collapse; elapsed = 0f; stageStartFrame = Time.frameCount; SampleCollapse();
    }
    private void SampleCollapse()
    {
        // First finish the pose transfer; then let the original weighted fall play.
        float fall = Mathf.Clamp01((elapsed - collapseBlendSeconds) / collapseSeconds);
        float normalized = fall < .9f ? Mathf.Lerp(collapseFrom, deathProneStart, fall / .9f)
            : Mathf.Lerp(deathProneStart, 1f, (fall - .9f) / .1f);
        Sample(DeathState, normalized);
        float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / collapseBlendSeconds));
        BlendCapturedPose(blend);
    }
    private void LateUpdate()
    {
        ObserveParry();
        if (!BlocksActions) return;
        if (!Alive || actor.LeaseVersion != ownedLease) { Cancel(); return; }
        // Freeze and pause keep the stage clock and never fight the bridge's frozen pose.
        if (actor.AnimationBridge.IsFrozen || Time.timeScale <= 0f || Time.deltaTime <= 0f) return;
        if (UsesMotion && !actor.AnimationBridge.OwnsMotion(reactionHandle) && !TakeOwnedReaction()) return;
        actor.Movement?.StopMovement();
        applyingLock = true;
        try
        {
            if (Phase == ReactionPhase.Recover || Phase == ReactionPhase.DazedRecover) reaction?.ApplyHitStun(.08f, false);
            else if (reaction != null && reaction.ParryStunRemaining < .08f) reaction.ApplyBossStun(.08f);
        }
        finally { applyingLock = false; }
        if (stageStartFrame == Time.frameCount) return;
        elapsed += Phase == ReactionPhase.Rewind || Phase == ReactionPhase.ReboundHold ? Time.unscaledDeltaTime : Time.deltaTime;
        switch (Phase)
        {
            case ReactionPhase.AuthoredRecoil:
                TickAuthoredRecoil(); break;
            case ReactionPhase.Rewind:
                float rebound = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / rewindSeconds));
                Sample(cancelledState, Mathf.Lerp(LastRewindStart, LastRewindEnd, rebound));
                if (elapsed >= rewindSeconds) { Phase = ReactionPhase.ReboundHold; elapsed = 0f; stageStartFrame = Time.frameCount; }
                break;
            case ReactionPhase.ReboundHold:
                Sample(cancelledState, LastRewindEnd);
                if (elapsed >= reboundHoldSeconds) { if (groggy) BeginCollapse(); else BeginDazed(); }
                break;
            case ReactionPhase.DazedEnter:
            case ReactionPhase.Dazed:
            case ReactionPhase.DazedRecover:
                TickDazed(); break;
            case ReactionPhase.Collapse:
                SampleCollapse();
                if (elapsed >= collapseBlendSeconds + collapseSeconds) { Phase = ReactionPhase.Prone; elapsed = 0f; stageStartFrame = Time.frameCount; Sample(DeathState, 1f); }
                break;
            case ReactionPhase.Prone:
                Sample(DeathState, 1f);
                if (elapsed >= proneSeconds) { Phase = ReactionPhase.Recover; elapsed = 0f; stageStartFrame = Time.frameCount; }
                break;
            case ReactionPhase.Recover:
                float rise = Mathf.Clamp01(elapsed / recoverySeconds);
                // Only a brief transition through the nearly identical final prone frames.
                if (getUpClip != null && animator.HasState(0, GetUpState)) Sample(GetUpState, rise);
                else Sample(DeathState, rise < .1f ? Mathf.Lerp(1f, deathProneStart, rise / .1f)
                    : Mathf.Lerp(deathProneStart, deathFallStart, Mathf.SmoothStep(0f, 1f, (rise - .1f) / .9f)));
                if (elapsed >= recoverySeconds)
                {
                    RecoveryCount++; ReleaseAnimator(); Phase = ReactionPhase.Ready; groggy = false;
                    if (!UsesMotion) animator.CrossFadeInFixedTime("Locomotion", .12f, 0, 0f);
                }
                break;
        }
        FollowProneHurtVolume();
    }
    private void FollowProneHurtVolume()
    {
        if (!groggy || !ownsAnimator || Phase == ReactionPhase.Rewind || Phase == ReactionPhase.ReboundHold || Phase == ReactionPhase.AuthoredRecoil || hurtTarget == null || chestBone == null || pelvisBone == null) return;
        Vector3 center = (chestBone.position + pelvisBone.position) * .5f;
        float upright = Mathf.Abs(Vector3.Dot((chestBone.position - pelvisBone.position).normalized, actor.transform.up));
        float height = Mathf.Lerp(hurtBefore.Radius * 2, hurtBefore.HalfHeight * 2, upright);
        Vector3 original = actor.transform.TransformPoint(hurtBeforeLocal);
        float blend = Phase == ReactionPhase.Collapse ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / collapseBlendSeconds)) : 1f;
        if (Phase == ReactionPhase.Recover) blend *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.8f, 1f, SampledNormalizedTime));
        hurtTarget.TrySetTemporaryHurtVolume(this, new CombatTargetVolume(Vector3.Lerp(original, center, blend), hurtBefore.Radius, Mathf.Lerp(hurtBefore.HalfHeight, height * .5f, blend)));
    }
    private void ReleaseAnimator()
    {
        if (UsesMotion) { ReleaseOwnedReaction(); return; }
        if (ownsAnimator && animator != null) { animator.speed = speedBefore; animator.updateMode = updateBefore; }
        hurtTarget?.ClearTemporaryHurtVolume(this);
        ownsAnimator = false;
    }
    public void Cancel()
    {
        ReleaseAnimator(); Phase = ReactionPhase.Ready; groggy = false; elapsed = 0f;
        cancelledState = 0; capturedLease = 0; cancelledMaterial = null; authoredRecoil = null;
    }
}
