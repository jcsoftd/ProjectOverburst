using System;
using System.Collections.Generic;
using UnityEngine;

// Optional playback ownership on the existing component. Null profiles retain the legacy path.
public partial class EnemyAnimationBridge
{
    [SerializeField] private EnemyMotionPlaybackProfile motionPlaybackProfile;
    private EnemyActor motionActor;
    private EnemyMotionRequest ownedRequest;
    private EnemyMotionPlaybackProfile.Binding ownedBinding;
    private EnemyMotionHandle ownedHandle;
    private EnemyMotionState ownedState;
    private EnemyMotionResult lastMotionResult;
    private EnemyMotionSample lastOwnedSample;
    private int motionGeneration, motionRequestId, ownedStateHash;
    private float ownedStartedAt, ownedDeadline, ownedLastProgressAt, ownedLastNormalized;
    private bool motionTransitionGate, ownedEntered, motionBindingsValid, poseSampleValid;
    private int motionNotificationDepth;
    private AnimatorUpdateMode normalMotionClock;
    private float normalMotionSpeed = 1f;
    private readonly List<AnimatorClipInfo> motionClipBuffer = new List<AnimatorClipInfo>(8);
    private readonly Dictionary<int, EnemyMotionPlaybackProfile.Binding> motionStates = new Dictionary<int, EnemyMotionPlaybackProfile.Binding>();
    private readonly Dictionary<AnimationClip, EnemyMotionPlaybackProfile.Binding> contactBindings = new Dictionary<AnimationClip, EnemyMotionPlaybackProfile.Binding>();
    private EnemyMotionSample contactPrevious;
    private bool contactTracked;
    private Vector3 contactPreviousPosition;
    public event Action<EnemyMotionContact> MotionContact;
    public EnemyMotionPlaybackProfile PlaybackProfile => motionPlaybackProfile;
    public bool UsesOwnedMotion => motionPlaybackProfile != null && motionBindingsValid;
    public bool HasInvalidMotionProfile => motionPlaybackProfile != null && !motionBindingsValid;
    public string MotionConfigurationError { get; private set; }
    public EnemyMotionHandle CurrentMotionHandle => ownedHandle;
    public EnemyMotionRole CurrentMotionRole => ownedRequest.Role;
    public EnemyMotionState CurrentMotionState => ownedState;
    public bool HasOwnedBlockingMotion => UsesOwnedMotion && ownedHandle.IsValid && ownedRequest.Role != EnemyMotionRole.Locomotion;
    public bool IsMotionTransitioning => motionTransitionGate;
    public int InvalidSnapshotCount { get; private set; }

    public bool ConfigureMotionPlayback(EnemyMotionPlaybackProfile profile)
    {
        InvalidateOwnedMotion(EnemyMotionReason.Reused, false);
        motionPlaybackProfile = profile;
        return CacheMotionBindings();
    }

    private bool CacheMotionBindings()
    {
        motionStates.Clear(); contactBindings.Clear(); motionBindingsValid = false;
        SetOwnedControllerFlag(false);
        MotionConfigurationError = null;
        if (motionActor == null) motionActor = GetComponent<EnemyActor>();
        if (animator != null) { normalMotionClock = animator.updateMode; normalMotionSpeed = animator.speed > 0f ? animator.speed : 1f; }
        if (motionPlaybackProfile == null)
        { SetOwnedControllerFlag(false); return true; }
        string error = null;
        if (animator == null || !motionPlaybackProfile.Validate(out error))
        { MotionConfigurationError = animator == null ? "No Animator." : error; return false; }
        foreach (string parameter in new[] { "BossMoveX", "BossMoveZ", "BossMotionRate", "TurnMagnitude", "AttackAnimSpeed" })
            if (!HasFloatParameter(parameter)) { MotionConfigurationError = "Missing Float parameter: " + parameter; return false; }
        bool hasOwnedFlag = false;
        foreach (var parameter in animator.parameters) if (parameter.name == "OwnedMotionPlayback" && parameter.type == AnimatorControllerParameterType.Bool) hasOwnedFlag = true;
        if (!hasOwnedFlag || motionActor == null || motionActor.Movement == null || motionActor.AbilityController == null
            || GetComponent<EnemyLocomotionAnimator>() == null || GetComponent<EnemyBossMaterialExecutor>() == null || GetComponent<EnemyBossCompositePatternExecutor>() == null)
        { MotionConfigurationError = "Required playback consumer or opt-in parameter missing."; return false; }
        foreach (var binding in motionPlaybackProfile.Bindings)
        {
            int hash = MotionStateHash(binding.state);
            if (!animator.HasState(0, hash)) { MotionConfigurationError = "Missing Animator state: " + binding.state; return false; }
            if (!string.IsNullOrEmpty(binding.rateParameter) && !HasFloatParameter(binding.rateParameter))
            { MotionConfigurationError = "Missing Float parameter: " + binding.rateParameter; return false; }
            motionStates[hash] = binding;
            var clip = motionPlaybackProfile.ResolveClip(binding);
            // Individual walk bindings carry marker/stride metadata for the BlendTree.
            if (binding.contacts != null && binding.contacts.Length != 0) contactBindings[clip] = binding;
        }
        motionBindingsValid = true;
        SetOwnedControllerFlag(true);
        return true;
    }

    private void SetOwnedControllerFlag(bool value)
    { if (animator != null) foreach (var p in animator.parameters) if (p.name == "OwnedMotionPlayback" && p.type == AnimatorControllerParameterType.Bool) { animator.SetBool(p.nameHash, value); break; } }

    private bool HasFloatParameter(string name)
    { foreach (var parameter in animator.parameters) if (parameter.name == name) return parameter.type == AnimatorControllerParameterType.Float; return false; }
    private static int MotionStateHash(string state) => Animator.StringToHash(state.StartsWith("Base Layer.", StringComparison.Ordinal) ? state : "Base Layer." + state);
    private static int Priority(EnemyMotionRole role)
    {
        switch (role)
        {
            case EnemyMotionRole.Death: return 100;
            case EnemyMotionRole.Frozen: return 90;
            case EnemyMotionRole.Reaction: return 80;
            case EnemyMotionRole.Introduction: return 70;
            case EnemyMotionRole.Attack: return 50;
            case EnemyMotionRole.Carry: case EnemyMotionRole.Support: return 40;
            case EnemyMotionRole.Turn: return 30;
            default: return 10;
        }
    }

    public bool OwnsMotion(in EnemyMotionHandle handle) => UsesOwnedMotion && !motionTransitionGate && motionNotificationDepth == 0 && handle.IsValid
        && ownedHandle == handle && (motionActor == null || motionActor.LeaseVersion == handle.Lease);
    private bool IsCurrentHandle(in EnemyMotionHandle handle) => handle.IsValid && ownedHandle == handle
        && (motionActor == null || motionActor.LeaseVersion == handle.Lease);

    public bool CanCommitPreparedAttack(in EnemyAbilityStartContext context) => UsesOwnedMotion && !motionTransitionGate && context.IsPrepared
        && IsCurrentHandle(context.Preparation) && ownedState == EnemyMotionState.Holding
        && ownedRequest.GroupId == context.Preparation.GroupId && ownedHandle.StepId == context.Preparation.StepId
        && ownedRequest.Role == EnemyMotionRole.Support && context.PayloadLease == context.Preparation.Lease;

    public bool TryBeginMotion(in EnemyMotionRequest request, out EnemyMotionHandle handle, out EnemyMotionReason reason)
        => TryInstallMotion(default, request, false, out handle, out reason);
    public bool TryHandoff(in EnemyMotionHandle from, in EnemyMotionRequest next, out EnemyMotionHandle handle, out EnemyMotionReason reason)
        => TryInstallMotion(from, next, true, out handle, out reason);

    private bool TryInstallMotion(in EnemyMotionHandle from, in EnemyMotionRequest request, bool handoff, out EnemyMotionHandle handle, out EnemyMotionReason reason)
    {
        handle = default; reason = EnemyMotionReason.None;
        if (motionTransitionGate || motionNotificationDepth != 0) { reason = EnemyMotionReason.Transitioning; return false; }
        if (!UsesOwnedMotion || request.Owner == null) { reason = EnemyMotionReason.InvalidBinding; return false; }
        var binding = motionPlaybackProfile.Find(request.MotionId);
        if (binding == null) { reason = EnemyMotionReason.InvalidBinding; return false; }
        if (!EnemyMotionPlaybackProfile.FinitePositive(request.Rate)) { reason = EnemyMotionReason.InvalidRate; return false; }
        if ((isDead || health != null && health.IsDead) && request.Role != EnemyMotionRole.Death) { reason = EnemyMotionReason.ActorDead; return false; }
        if (isFrozen && request.Role != EnemyMotionRole.Frozen && request.Role != EnemyMotionRole.Death) { reason = EnemyMotionReason.Frozen; return false; }
        if (handoff)
        {
            // Introduction's early recovery handoff is a distinct, same-owner group contract.
            bool introduction = ownedRequest.Role == EnemyMotionRole.Introduction && request.Role == EnemyMotionRole.Introduction
                && request.Owner == ownedRequest.Owner && TryReadMotion(ownedHandle, out var introductionSample) && introductionSample.Normalized >= .82f;
            bool carryStop = ownedRequest.Role == EnemyMotionRole.Carry && request.Role == EnemyMotionRole.Support
                && request.Owner == ownedRequest.Owner && ownedBinding.lifetime == EnemyMotionLifetime.Continuous;
            if (!IsCurrentHandle(from) || from.GroupId == 0 || from.GroupId != ownedRequest.GroupId || from.GroupId != request.GroupId
                || !introduction && !carryStop && ownedState != EnemyMotionState.Holding)
            { reason = EnemyMotionReason.StaleHandle; return false; }
        }
        else if (ownedHandle.IsValid && Priority(request.Role) <= Priority(ownedRequest.Role))
        { reason = EnemyMotionReason.Busy; return false; }

        var oldRequest = ownedRequest;
        var old = CaptureTerminal(EnemyMotionState.Cancelled, handoff ? EnemyMotionReason.HandoffAccepted
            : request.Role == EnemyMotionRole.Frozen ? EnemyMotionReason.Frozen : request.Role == EnemyMotionRole.Death ? EnemyMotionReason.ActorDead : EnemyMotionReason.Preempted);
        motionTransitionGate = true;
        try
        {
            // The invalidated generation cannot release locks or write the new clock.
            ownedHandle = default; ++motionGeneration;
            contactTracked = poseSampleValid = false;
            if (old.Handle.IsValid) { lastMotionResult = old; oldRequest.OnInvalidated?.Invoke(old); }
            ClearBlockingAction(); ResetActionTriggers();
            ownedRequest = request; ownedBinding = binding;
            ownedHandle = handle = new EnemyMotionHandle(motionActor != null ? motionActor.LeaseVersion : 0u, ++motionGeneration, ++motionRequestId, request.GroupId, request.StepId);
            ownedStateHash = MotionStateHash(binding.state);
            ownedState = EnemyMotionState.Entering; ownedEntered = false;
            ownedStartedAt = ownedLastProgressAt = Time.time; ownedLastNormalized = 0f;
            float budget = request.BudgetSeconds > 0f ? request.BudgetSeconds : motionPlaybackProfile.DurationBudget(binding, request.Rate);
            ownedDeadline = binding.lifetime == EnemyMotionLifetime.Continuous || request.HoldLastPose
                ? float.PositiveInfinity : Time.time + Mathf.Max(motionPlaybackProfile.EntryTimeout, budget);
            animator.speed = normalMotionSpeed; animator.updateMode = binding.updateMode;
            SetOwnedRateInternal(request.Rate);
            if (request.Role == EnemyMotionRole.Turn) animator.SetFloat("TurnMagnitude", Mathf.Clamp01(request.Magnitude));
            animator.SetFloat("BossMoveX", 0f); animator.SetFloat("BossMoveZ", 0f);
            float fixedOffset = Mathf.Clamp01(request.StartNormalized) * motionPlaybackProfile.ResolveClip(binding).length;
            animator.CrossFadeInFixedTime(ownedStateHash, request.BlendInOverride > 0f ? request.BlendInOverride : binding.blendIn, 0, fixedOffset);
            // Readbacks immediately after begin can identify a pending incoming state.
            animator.Update(0f);
        }
        finally { motionTransitionGate = false; }
        if (old.Handle.IsValid) NotifyMotionTerminated(oldRequest, old);
        return true;
    }

    public EnemyMotionResult GetMotionResult(in EnemyMotionHandle handle)
    {
        if (IsCurrentHandle(handle)) return new EnemyMotionResult(handle, ownedState);
        if (lastMotionResult.Handle == handle && handle.IsValid) return lastMotionResult;
        return new EnemyMotionResult(handle, EnemyMotionState.Unknown, EnemyMotionReason.StaleHandle);
    }

    public bool TryReadMotion(in EnemyMotionHandle handle, out EnemyMotionSample sample)
    {
        sample = default;
        if (!OwnsMotion(handle) || animator == null || !animator.isActiveAndEnabled) return false;
        if (ownedBinding.ratePolicy == EnemyMotionRatePolicy.OwnedPose && poseSampleValid)
        { sample = lastOwnedSample; return true; }
        bool transition = animator.IsInTransition(0);
        if (transition && ReadMotionState(animator.GetNextAnimatorStateInfo(0), true, transition, out sample)) return true;
        return ReadMotionState(animator.GetCurrentAnimatorStateInfo(0), false, transition, out sample);
    }

    private bool ReadMotionState(AnimatorStateInfo state, bool next, bool transition, out EnemyMotionSample sample)
    {
        sample = default;
        if (state.fullPathHash != ownedStateHash) return false;
        motionClipBuffer.Clear();
        if (next) animator.GetNextAnimatorClipInfo(0, motionClipBuffer); else animator.GetCurrentAnimatorClipInfo(0, motionClipBuffer);
        AnimationClip expected = motionPlaybackProfile.ResolveClip(ownedBinding), clip = null; float weight = 0f;
        bool blend = ownedBinding.lifetime == EnemyMotionLifetime.Continuous;
        for (int i = 0; i < motionClipBuffer.Count; i++)
        {
            var info = motionClipBuffer[i];
            if (info.weight > weight && (info.clip == expected || blend)) { clip = info.clip; weight = info.weight; }
        }
        if (clip == null || weight <= .001f) return false;
        sample = new EnemyMotionSample(ownedHandle, ownedRequest.MotionId, clip, state.fullPathHash, state.normalizedTime, weight, transition);
        return true;
    }

    public bool SetOwnedPlaybackRate(in EnemyMotionHandle handle, float rate)
    {
        if (!OwnsMotion(handle) || !EnemyMotionPlaybackProfile.FinitePositive(rate)
            || ownedBinding.ratePolicy == EnemyMotionRatePolicy.SnapshotAtStart || ownedBinding.ratePolicy == EnemyMotionRatePolicy.OwnedPose
            || ownedState == EnemyMotionState.Holding) return false;
        ownedRequest.Rate = rate; SetOwnedRateInternal(rate); return true;
    }
    private void SetOwnedRateInternal(float rate)
    { if (!string.IsNullOrEmpty(ownedBinding.rateParameter)) animator.SetFloat(ownedBinding.rateParameter, rate); }

    public bool TryUpdateOwnedMotionIntent(in EnemyMotionHandle handle, in EnemyMotionIntent intent)
    {
        if (!OwnsMotion(handle) || ownedBinding.lifetime != EnemyMotionLifetime.Continuous
            || !EnemyMotionPlaybackProfile.FinitePositive(intent.Rate)) return false;
        var direction = Vector2.ClampMagnitude(intent.Direction, 1f);
        animator.SetFloat("BossMoveX", direction.x, LocomotionDampTime, Mathf.Max(.001f, Time.deltaTime));
        animator.SetFloat("BossMoveZ", direction.y, LocomotionDampTime, Mathf.Max(.001f, Time.deltaTime));
        return SetOwnedPlaybackRate(handle, intent.Rate);
    }

    public bool TrySampleOwnedPose(in EnemyMotionHandle handle, int stateHash, float normalized)
    {
        if (!OwnsMotion(handle) || ownedBinding.ratePolicy != EnemyMotionRatePolicy.OwnedPose
            || !animator.HasState(0, stateHash) || !EnemyMotionPlaybackProfile.FiniteNonNegative(normalized) || normalized > 1f) return false;
        animator.speed = 0f; animator.updateMode = AnimatorUpdateMode.Normal;
        animator.Play(stateHash, 0, normalized); animator.Update(0f);
        motionClipBuffer.Clear(); animator.GetCurrentAnimatorClipInfo(0, motionClipBuffer);
        // Interrupting an incoming transition can install the state before its clip weights are evaluated.
        // Evaluate once more without advancing either the animation clock or its gameplay crossings.
        if (motionClipBuffer.Count == 0 && animator.GetCurrentAnimatorStateInfo(0).fullPathHash == stateHash)
        { animator.Update(0f); animator.GetCurrentAnimatorClipInfo(0, motionClipBuffer); }
        AnimationClip clip = null; float weight = 0f;
        for (int i = 0; i < motionClipBuffer.Count; i++) if (motionClipBuffer[i].weight > weight) { clip = motionClipBuffer[i].clip; weight = motionClipBuffer[i].weight; }
        if (clip == null || animator.GetCurrentAnimatorStateInfo(0).fullPathHash != stateHash) { poseSampleValid = false; return false; }
        lastOwnedSample = new EnemyMotionSample(handle, ownedRequest.MotionId, clip, stateHash, normalized, weight, false);
        poseSampleValid = ownedEntered = true; ownedState = EnemyMotionState.Holding; contactTracked = false;
        return true;
    }

    public bool UpdateOwnedAttackContext(in EnemyMotionHandle handle, int phase, float consumedEnd)
    { if (!OwnsMotion(handle) || ownedRequest.Role != EnemyMotionRole.Attack) return false; ownedRequest.Phase = phase; ownedRequest.ConsumedStrikeEnd = consumedEnd; return true; }
    public bool TryCompleteOwnedMotion(in EnemyMotionHandle handle)
    {
        if (!OwnsMotion(handle) || ownedBinding.lifetime == EnemyMotionLifetime.HeldPose || ownedBinding.lifetime == EnemyMotionLifetime.Terminal) return false;
        if (ownedState == EnemyMotionState.BlendingOut) return true;
        ownedState = EnemyMotionState.BlendingOut;
        ownedDeadline = Time.time + ownedBinding.blendOut + motionPlaybackProfile.EntryTimeout;
        animator.SetFloat("BossMotionRate", 1f);
        animator.CrossFadeInFixedTime(MotionStateHash(motionPlaybackProfile.Find("Locomotion").state), ownedBinding.blendOut, 0, 0f);
        return true;
    }
    public bool TryEndContinuousMotion(in EnemyMotionHandle handle)
    { return OwnsMotion(handle) && ownedBinding.lifetime == EnemyMotionLifetime.Continuous && FinishOwnedMotion(handle, EnemyMotionState.Completed, EnemyMotionReason.None, true); }
    public bool CancelMotion(in EnemyMotionHandle handle, EnemyMotionReason reason)
    { return OwnsMotion(handle) && FinishOwnedMotion(handle, EnemyMotionState.Cancelled, reason, reason != EnemyMotionReason.Disabled && reason != EnemyMotionReason.Reused); }
    public bool FailMotion(in EnemyMotionHandle handle, EnemyMotionReason reason)
    { return OwnsMotion(handle) && FinishOwnedMotion(handle, EnemyMotionState.Failed, reason, true); }

    private EnemyMotionResult CaptureTerminal(EnemyMotionState state, EnemyMotionReason reason)
    {
        if (!ownedHandle.IsValid) return default;
        EnemyMotionCancelSnapshot snapshot = default;
        if (state != EnemyMotionState.Completed && ownedRequest.Role == EnemyMotionRole.Attack)
        {
            if (TryReadMotion(ownedHandle, out var sample)) snapshot = new EnemyMotionCancelSnapshot(sample, ownedRequest.AttackSequence, ownedRequest.Phase, ownedRequest.ConsumedStrikeEnd, reason);
            if (!snapshot.Valid) InvalidSnapshotCount++;
        }
        return new EnemyMotionResult(ownedHandle, state, reason, snapshot);
    }

    private bool FinishOwnedMotion(in EnemyMotionHandle handle, EnemyMotionState state, EnemyMotionReason reason, bool returnIdle, bool resetStaleLease = false)
    {
        if (motionTransitionGate || (!resetStaleLease && !IsCurrentHandle(handle)) || resetStaleLease && ownedHandle != handle) return false;
        bool staleLease = motionActor != null && motionActor.LeaseVersion != handle.Lease;
        var result = CaptureTerminal(state, reason); var request = ownedRequest; var binding = ownedBinding;
        motionTransitionGate = true;
        try
        {
            ownedHandle = default; ++motionGeneration; lastMotionResult = result;
            ownedState = state; contactTracked = poseSampleValid = false;
            request.OnInvalidated?.Invoke(result);
            if (!staleLease) { animator.speed = normalMotionSpeed; animator.updateMode = normalMotionClock; }
            if (returnIdle && !staleLease && !isDead && !isFrozen && animator.isActiveAndEnabled)
                animator.CrossFadeInFixedTime(MotionStateHash(motionPlaybackProfile.Find("Locomotion").state), binding.blendOut, 0, 0f);
        }
        finally { motionTransitionGate = false; }
        NotifyMotionTerminated(request, result); return true;
    }

    private void NotifyMotionTerminated(in EnemyMotionRequest request, in EnemyMotionResult result)
    {
        // Notifications can observe the installed owner, but cannot issue a synchronous replacement or write its clock.
        motionNotificationDepth++;
        try { request.OnTerminated?.Invoke(result); }
        finally { motionNotificationDepth--; }
    }

    private void InvalidateOwnedMotion(EnemyMotionReason reason, bool returnIdle)
    {
        if (!ownedHandle.IsValid) return;
        FinishOwnedMotion(ownedHandle, EnemyMotionState.Cancelled, reason, returnIdle, true);
    }

    private void Update()
    {
        if (!UsesOwnedMotion || !ownedHandle.IsValid || motionTransitionGate) return;
        if (motionActor != null && motionActor.LeaseVersion != ownedHandle.Lease) { InvalidateOwnedMotion(EnemyMotionReason.Reused, false); return; }
        if (ownedState == EnemyMotionState.BlendingOut)
        {
            if (!animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).fullPathHash == MotionStateHash(motionPlaybackProfile.Find("Locomotion").state))
                FinishOwnedMotion(ownedHandle, EnemyMotionState.Completed, EnemyMotionReason.None, false);
            else if (Time.time > ownedDeadline) FailMotion(ownedHandle, EnemyMotionReason.DeadlineExceeded);
            return;
        }
        if (ownedBinding.ratePolicy == EnemyMotionRatePolicy.OwnedPose || ownedState == EnemyMotionState.Holding) return;
        if (!TryReadMotion(ownedHandle, out var sample))
        {
            if (Time.time - ownedStartedAt > motionPlaybackProfile.EntryTimeout)
                FailMotion(ownedHandle, ownedEntered ? EnemyMotionReason.UnexpectedExit : EnemyMotionReason.EntryTimeout);
            return;
        }
        if (!EnemyMotionPlaybackProfile.FiniteNonNegative(sample.Normalized) || ownedEntered && sample.Normalized + .0001f < ownedLastNormalized)
        { FailMotion(ownedHandle, EnemyMotionReason.InvalidClock); return; }
        ownedEntered = true; ownedState = EnemyMotionState.Playing; lastOwnedSample = sample;
        if (sample.Normalized > ownedLastNormalized + .00001f) ownedLastProgressAt = Time.time;
        ownedLastNormalized = sample.Normalized;
        ObserveContacts(sample);
        if (ownedBinding.lifetime == EnemyMotionLifetime.Continuous) return;
        if (sample.Normalized >= ownedBinding.completeNormalized)
        {
            if (ownedBinding.lifetime == EnemyMotionLifetime.HeldPose || ownedRequest.HoldLastPose || ownedBinding.lifetime == EnemyMotionLifetime.Terminal)
            {
                if (sample.Transitioning) return; // Finish the incoming blend before freezing its last pose.
                animator.Play(ownedStateHash, 0, ownedBinding.completeNormalized); animator.Update(0f); animator.speed = 0f;
                ownedState = EnemyMotionState.Holding; return;
            }
            if (!ownedRequest.ExternalCompletion) TryCompleteOwnedMotion(ownedHandle);
        }
        else if (Time.time > ownedDeadline && !ownedRequest.ExternalCompletion) FailMotion(ownedHandle, EnemyMotionReason.DeadlineExceeded);
        // Attack deadlines and changing phase rates are owned by their execution context.
    }

    private void ObserveContacts(in EnemyMotionSample sample)
    {
        bool role = ownedRequest.Role == EnemyMotionRole.Locomotion || ownedRequest.Role == EnemyMotionRole.Turn || ownedRequest.Role == EnemyMotionRole.Carry;
        Vector3 position = motionActor != null ? motionActor.transform.position : transform.position;
        bool teleport = contactTracked && (position - contactPreviousPosition).sqrMagnitude > .75f * .75f;
        contactPreviousPosition = position;
        if (!role || sample.Transitioning || teleport || !contactBindings.TryGetValue(sample.Clip, out var binding)) { contactTracked = false; return; }
        if (!contactTracked || contactPrevious.Handle != sample.Handle || contactPrevious.Clip != sample.Clip || sample.Normalized < contactPrevious.Normalized)
        { contactPrevious = sample; contactTracked = true; return; }
        // One presentation contact per frame; physical attack crossings keep their separate rules.
        bool emitted = false;
        if (ownedRequest.Role == EnemyMotionRole.Turn && sample.Normalized > 1f) { contactPrevious = sample; return; }
        int cycle = Mathf.FloorToInt(sample.Normalized);
        for (int i = 0; i < binding.contacts.Length && !emitted; i++)
        {
            var marker = binding.contacts[i]; float at = cycle + marker.normalized;
            if (at > sample.Normalized) at -= 1f;
            if (at > contactPrevious.Normalized && at <= sample.Normalized && sample.Normalized - contactPrevious.Normalized < 1f)
            { MotionContact?.Invoke(new EnemyMotionContact(sample.Handle, binding.motionId, marker.bone, Mathf.FloorToInt(at), i, marker.strength, marker.localSoleOffset)); emitted = true; }
        }
        contactPrevious = sample;
    }

    public bool TryPlayOwnedPresentation(string id, EnemyMotionRole role, UnityEngine.Object owner, bool hold, out EnemyMotionHandle handle)
    {
        var binding = motionPlaybackProfile != null ? motionPlaybackProfile.Find(id) : null;
        var request = new EnemyMotionRequest { Owner = owner, Role = role, MotionId = id, Rate = binding != null ? binding.rate : 1f, HoldLastPose = hold };
        return TryBeginMotion(request, out handle, out _);
    }

    public void ReturnOwnedToLocomotion(UnityEngine.Object owner)
    { if (ownedHandle.IsValid && ownedRequest.Owner == owner) CancelMotion(ownedHandle, EnemyMotionReason.OwnerCancelled); }
    public bool TryPlayOwnedStatePresentation(string state, UnityEngine.Object owner)
    {
        if (state == "Locomotion" || state == "BossLocomotion") { ReturnOwnedToLocomotion(owner); return true; }
        if (!motionStates.TryGetValue(MotionStateHash(state), out var binding)) return false;
        return TryPlayOwnedPresentation(binding.motionId, EnemyMotionRole.Support, owner, false, out _);
    }
}
