using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerFootstepPlaybackMode { Disabled, AnimationContacts, LegacyDistance }

public struct PlayerFootstepContact
{
    public AnimationClip Clip;
    public int StateHash;
    public int Foot;
    public int Frame;
    public int OccurrenceLoop;
    public float NormalizedTime;
    public float EventSeconds;
    public float EventWeight;
    public float SelectedWeight;
    public FootstepMotionKind Kind;
}

[DefaultExecutionOrder(1500)]
[DisallowMultipleComponent]
public sealed class FootstepEmitter : MonoBehaviour
{
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private OverburstCharacterMotor3D motor;
    [SerializeField] private SurfaceResolver surfaceResolver;
    [SerializeField] private AudioSource audioSource;
    [SerializeField, Min(0.1f)] private float walkStrideDistance = 1.35f;
    [SerializeField, Min(0.1f)] private float runStrideDistance = 1.8f;
    [SerializeField, Min(0f)] private float minimumMoveSpeed = 0.2f;
    [SerializeField, Min(0f)] private float landingMinimumFallSpeed = 2f;
    [SerializeField, Min(0f)] private float minimumEmissionInterval = 0.08f;
    [SerializeField, Min(0f)] private float minimumSameFootInterval = 0.12f;
    [SerializeField, Min(0f)] private float maximumContactHeight = 0.1f;
    [SerializeField] private AnimationClip[] animationContactClips = Array.Empty<AnimationClip>();
    [SerializeField] private float[] animationContactCycleOffsets = Array.Empty<float>();
    [SerializeField] private AnimationClip[] legacyLocomotionClips = Array.Empty<AnimationClip>();
    [SerializeField] private string[] animationContactStates = Array.Empty<string>();
    [SerializeField] private string[] legacyDistanceStates = Array.Empty<string>();

    private const int CandidateCapacity = 32;
    private readonly PlayerFootstepContact[] candidates = new PlayerFootstepContact[CandidateCapacity];
    private readonly List<AnimatorClipInfo> currentClips = new List<AnimatorClipInfo>(16);
    private readonly List<AnimatorClipInfo> nextClips = new List<AnimatorClipInfo>(16);
    private readonly float[] lastFootTimes = { float.NegativeInfinity, float.NegativeInfinity };
    private readonly int[] lastFootLoops = { int.MinValue, int.MinValue };
    private readonly int[] lastFootStates = new int[2];
    private readonly AnimationClip[] lastFootClips = new AnimationClip[2];
    private readonly float[] lastFootEventSeconds = new float[2];
    private PlayerStateCoordinator state;
    private MeleeRuntime melee;
    private Animator animator;
    private PlayerFootstepAnimationRelay relay;
    private RuntimeAnimatorController boundController;
    private Transform leftHeel, leftToe, rightHeel, rightToe, leftFoot, rightFoot;
    private int[] contactStateHashes = Array.Empty<int>(), legacyStateHashes = Array.Empty<int>();
    private int combatLayer = -1, lowerLayer = -1;
    private int candidateCount, candidateFrame = -1, lastLandingFrame = -1;
    private Vector3 previousPosition;
    private bool hasPreviousPosition, legacyNotification;
    private float accumulatedDistance, lastEmissionTime = float.NegativeInfinity;
    private AnimationClip selectedClip;
    private int selectedStateHash;
    private float selectedWeight;

    public int StepEmissionCount { get; private set; }
    public int LandingEmissionCount { get; private set; }
    public int AnimationEventCount { get; private set; }
    public int AnimationStepCount { get; private set; }
    public int LegacyStepCount { get; private set; }
    public int RejectedEventCount { get; private set; }
    public int QueueOverflowCount { get; private set; }
    public int CatchUpDiscardCount { get; private set; }
    public int ElevatedContactDiscardCount { get; private set; }
    public int MaximumFrameCandidates { get; private set; }
    public int LastFoot { get; private set; } = -1;
    public int LastEmissionFrame { get; private set; } = -1;
    public AnimationClip LastContactClip { get; private set; }
    public Vector3 LastSamplePosition { get; private set; }
    public PlayerFootstepPlaybackMode PlaybackMode { get; private set; }
    public SurfaceProfile LastProfile { get; private set; }
    public FootstepMotionKind LastMotionKind { get; private set; }
    public event Action<SurfaceProfile, FootstepMotionKind> Emitted;
    public event Action<PlayerFootstepContact> ContactAccepted;

    public void Configure(PlayerMovement configuredMovement, OverburstCharacterMotor3D configuredMotor,
        SurfaceResolver configuredResolver, AudioSource configuredAudioSource,
        float configuredWalkStride, float configuredRunStride, float configuredLandingMinimumFallSpeed)
    {
        movement = configuredMovement; motor = configuredMotor;
        surfaceResolver = configuredResolver; audioSource = configuredAudioSource;
        walkStrideDistance = Mathf.Max(.1f, configuredWalkStride);
        runStrideDistance = Mathf.Max(.1f, configuredRunStride);
        landingMinimumFallSpeed = Mathf.Max(0f, configuredLandingMinimumFallSpeed);
        ResolveReferences();
    }

    public void ConfigureAnimationContacts(AnimationClip[] clips, string[] states,
        AnimationClip[] legacyClips, string[] legacyStates, float[] cycleOffsets = null)
    {
        animationContactClips = clips ?? Array.Empty<AnimationClip>();
        animationContactCycleOffsets = cycleOffsets ?? new float[animationContactClips.Length];
        animationContactStates = states ?? Array.Empty<string>();
        legacyLocomotionClips = legacyClips ?? Array.Empty<AnimationClip>();
        legacyDistanceStates = legacyStates ?? Array.Empty<string>();
        CacheStateHashes(); ResetTracking();
    }

    public void BindAnimation(Animator target, HumanoidFootContactRig rig)
    {
        if (target == null) return;
        if (animator != target)
        {
            if (relay != null) relay.Unbind(this);
            animator = target;
            relay = target.GetComponent<PlayerFootstepAnimationRelay>();
            if (relay == null) relay = target.gameObject.AddComponent<PlayerFootstepAnimationRelay>();
            ResetTracking();
        }
        if (relay == null)
        {
            relay = target.GetComponent<PlayerFootstepAnimationRelay>();
            if (relay == null) relay = target.gameObject.AddComponent<PlayerFootstepAnimationRelay>();
        }
        relay.Bind(target, this);
        leftHeel = rig != null ? rig.LeftHeel : null; leftToe = rig != null ? rig.LeftToe : null;
        rightHeel = rig != null ? rig.RightHeel : null; rightToe = rig != null ? rig.RightToe : null;
        leftFoot = target.isHuman ? target.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
        rightFoot = target.isHuman ? target.GetBoneTransform(HumanBodyBones.RightFoot) : null;
        RefreshLayers();
    }

    private void Awake() { ResolveReferences(); CacheStateHashes(); ResetTracking(); }
    private void OnEnable()
    {
        ResetTracking();
        if (relay != null && animator != null) relay.Bind(animator, this);
    }
    private void OnDisable()
    {
        if (relay != null) relay.Unbind(this);
        ResetTracking();
    }

    public void QueueAnimationContact(Animator source, AnimationClip clip, int stateHash,
        float normalizedTime, float weight, float eventSeconds, int foot, FootstepMotionKind kind)
    {
        AnimationEventCount++;
        if (!isActiveAndEnabled || source != animator || clip == null || (foot != 0 && foot != 1)
            || !Contains(animationContactClips, clip) || Time.timeScale <= 0f
            || GameplayInputBlocker.IsGameplayInputBlocked)
        { RejectedEventCount++; return; }
        if (candidateFrame != Time.frameCount) { candidateCount = 0; candidateFrame = Time.frameCount; }
        if (candidateCount == CandidateCapacity) { QueueOverflowCount++; return; }
        candidates[candidateCount++] = new PlayerFootstepContact { Clip = clip, StateHash = stateHash,
            NormalizedTime = normalizedTime, EventSeconds = eventSeconds, EventWeight = weight,
            Foot = foot, Kind = kind, Frame = Time.frameCount };
        MaximumFrameCandidates = Mathf.Max(MaximumFrameCandidates, candidateCount);
    }

    private void LateUpdate()
    {
        Vector3 now = transform.position;
        Vector3 delta = hasPreviousPosition ? now - previousPosition : Vector3.zero;
        previousPosition = now; hasPreviousPosition = true; delta.y = 0f;
        SelectAnimationSource();
        PlaybackMode = ResolvePlaybackMode();
        bool audioAllowed = Time.timeScale > 0f && !GameplayInputBlocker.IsGameplayInputBlocked
            && (movement == null || !movement.IsEvading)
            && (state == null || state.CurrentCondition == PlayerConditionState.Normal);
        if (!audioAllowed || delta.sqrMagnitude > 9f)
        { ClearCandidates(); accumulatedDistance = 0f; ClearFootHistory(); return; }
        if (motor != null && motor.DidLandThisStep && motor.LandingFallSpeed <= -landingMinimumFallSpeed
            && lastLandingFrame != Time.frameCount)
        {
            lastLandingFrame = Time.frameCount;
            Emit(FootstepMotionKind.Land, transform.position, true);
            ClearCandidates(); accumulatedDistance = 0f; return;
        }
        Vector3 relativeDelta = delta;
        if (motor != null) relativeDelta -= Vector3.ProjectOnPlane(motor.PlatformVelocity, Vector3.up) * Time.deltaTime;
        float speed = relativeDelta.magnitude / Mathf.Max(Time.deltaTime, .0001f);
        bool moving = movement != null && movement.MoveInput.sqrMagnitude > .001f
            && motor != null && motor.IsGrounded && !motor.StandingOnEnemy
            && !movement.IsEvading && !movement.IsKnockedDown && !movement.IsMeleeAttackMoveLocked
            && (melee == null || !melee.IsAttackInProgress)
            && (state == null || (state.CurrentAction != PlayerActionState.Attack
                && state.CurrentAction != PlayerActionState.Interacting
                && state.CurrentLocomotion != PlayerLocomotionState.Evading
                && state.CurrentLocomotion != PlayerLocomotionState.ControlledMove))
            && speed >= minimumMoveSpeed;
        if (!moving)
        {
            RejectedEventCount += candidateCount; ClearCandidates(); accumulatedDistance = 0f;
            ClearFootHistory(); return;
        }

        if (PlaybackMode == PlayerFootstepPlaybackMode.AnimationContacts)
        {
            accumulatedDistance = 0f;
            ProcessAnimationContacts();
        }
        else if (PlaybackMode == PlayerFootstepPlaybackMode.LegacyDistance)
        {
            accumulatedDistance += relativeDelta.magnitude;
            bool running = movement.IsRunning;
            float stride = running ? runStrideDistance : walkStrideDistance;
            if (legacyNotification || accumulatedDistance >= stride)
            {
                accumulatedDistance = legacyNotification ? 0f : accumulatedDistance % stride;
                if (Emit(running ? FootstepMotionKind.Run : FootstepMotionKind.Walk, transform.position)) LegacyStepCount++;
            }
        }
        else accumulatedDistance = 0f;
        ClearCandidates();
    }

    private void ProcessAnimationContacts()
    {
        if (candidateFrame != Time.frameCount) return;
        int newest = -1, valid = 0;
        float smallestAge = float.PositiveInfinity;
        for (int i = 0; i < candidateCount; i++)
        {
            var candidate = candidates[i];
            if (candidate.Clip != selectedClip || candidate.StateHash != selectedStateHash)
            { RejectedEventCount++; continue; }
            float clipPhase = ContactClipPhase(candidate);
            int foot = candidate.Foot, loop = ContactOccurrenceLoop(candidate);
            if (lastFootClips[foot] == candidate.Clip && lastFootStates[foot] == candidate.StateHash
                && lastFootLoops[foot] == loop && Mathf.Abs(lastFootEventSeconds[foot] - candidate.EventSeconds) < .0001f)
            { RejectedEventCount++; continue; }
            float age = Mathf.Repeat(clipPhase - candidate.EventSeconds / candidate.Clip.length, 1f);
            valid++;
            if (age <= smallestAge) { smallestAge = age; newest = i; }
        }
        if (newest < 0) return;
        CatchUpDiscardCount += Mathf.Max(0, valid - 1);
        var contact = candidates[newest];
        contact.OccurrenceLoop = ContactOccurrenceLoop(contact);
        if (IsFootAboveCachedGround(contact.Foot))
        { ElevatedContactDiscardCount++; return; }
        if (Time.time - lastFootTimes[contact.Foot] < minimumSameFootInterval)
        { RejectedEventCount++; return; }
        if (!Emit(contact.Kind, FootSample(contact.Foot))) return;
        lastFootTimes[contact.Foot] = Time.time;
        lastFootLoops[contact.Foot] = contact.OccurrenceLoop;
        lastFootStates[contact.Foot] = contact.StateHash;
        lastFootClips[contact.Foot] = contact.Clip;
        lastFootEventSeconds[contact.Foot] = contact.EventSeconds;
        LastFoot = contact.Foot; LastContactClip = contact.Clip; AnimationStepCount++;
        contact.SelectedWeight = selectedWeight;
        ContactAccepted?.Invoke(contact);
    }

    private void SelectAnimationSource()
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
        { selectedClip = null; selectedStateHash = 0; return; }
        if (boundController != animator.runtimeAnimatorController) RefreshLayers();
        float combat = combatLayer >= 0 ? animator.GetLayerWeight(combatLayer) : 0f;
        float lower = lowerLayer >= 0 ? animator.GetLayerWeight(lowerLayer) : 0f;
        int layer = 0;
        float contribution = (1f - combat) * (1f - lower);
        if (combatLayer >= 0 && combat * (1f - lower) >= contribution)
        { layer = combatLayer; contribution = combat * (1f - lower); }
        if (lowerLayer >= 0 && lower >= contribution) layer = lowerLayer;
        AnimationClip previousClip = selectedClip;
        int previousState = selectedStateHash;
        selectedClip = null; selectedStateHash = 0; selectedWeight = 0f;
        animator.GetCurrentAnimatorClipInfo(layer, currentClips);
        animator.GetNextAnimatorClipInfo(layer, nextClips);
        SelectClip(currentClips, animator.GetCurrentAnimatorStateInfo(layer).fullPathHash, previousClip, previousState);
        SelectClip(nextClips, animator.GetNextAnimatorStateInfo(layer).fullPathHash, previousClip, previousState);
    }

    private void SelectClip(List<AnimatorClipInfo> clips, int stateHash, AnimationClip previousClip, int previousState)
    {
        for (int i = 0; i < clips.Count; i++)
        {
            var item = clips[i];
            if (item.clip == null || item.weight <= .00001f) continue;
            bool tied = Mathf.Abs(item.weight - selectedWeight) <= .00001f;
            if (selectedClip != null && item.weight < selectedWeight - .00001f) continue;
            if (tied && !(item.clip == previousClip && stateHash == previousState)) continue;
            selectedClip = item.clip; selectedStateHash = stateHash; selectedWeight = item.weight;
        }
    }

    private PlayerFootstepPlaybackMode ResolvePlaybackMode()
    {
        if (Contains(contactStateHashes, selectedStateHash))
            return Contains(legacyLocomotionClips, selectedClip)
                ? PlayerFootstepPlaybackMode.LegacyDistance : PlayerFootstepPlaybackMode.AnimationContacts;
        return Contains(legacyStateHashes, selectedStateHash)
            ? PlayerFootstepPlaybackMode.LegacyDistance : PlayerFootstepPlaybackMode.Disabled;
    }
    private Vector3 FootSample(int foot)
    {
        Transform heel = foot == 0 ? leftHeel : rightHeel, toe = foot == 0 ? leftToe : rightToe;
        if (heel != null && toe != null) return (heel.position + toe.position) * .5f;
        Transform bone = foot == 0 ? leftFoot : rightFoot;
        return bone != null ? bone.position : transform.position;
    }
    private bool IsFootAboveCachedGround(int foot)
    {
        Transform heel = foot == 0 ? leftHeel : rightHeel, toe = foot == 0 ? leftToe : rightToe;
        if (motor == null || heel == null || toe == null || float.IsInfinity(motor.GroundSurfaceHeight)) return false;
        Vector3 normal = motor.GroundContactNormal;
        if (normal.y <= .001f) return false;
        Vector3 groundOrigin = transform.position; groundOrigin.y = motor.GroundSurfaceHeight;
        // A long frame can deliver an old event after the foot has already lifted.
        // Use the motor's existing ground plane; this performs no additional physics query.
        float heelHeight = Vector3.Dot(normal, heel.position - groundOrigin) / normal.y;
        float toeHeight = Vector3.Dot(normal, toe.position - groundOrigin) / normal.y;
        return Mathf.Min(heelHeight, toeHeight) > maximumContactHeight;
    }
    private int ContactOccurrenceLoop(PlayerFootstepContact contact)
    {
        // A contact near the clip end can arrive after the wrap. Identify the
        // event's occurrence so the next real contact does not share its key.
        return Mathf.FloorToInt(ContactClipPhase(contact) - contact.EventSeconds / contact.Clip.length);
    }
    private float ContactClipPhase(PlayerFootstepContact contact)
    {
        for (int i = 0; i < animationContactClips.Length; i++)
            if (animationContactClips[i] == contact.Clip)
                return contact.NormalizedTime + (i < animationContactCycleOffsets.Length ? animationContactCycleOffsets[i] : 0f);
        return contact.NormalizedTime;
    }
    private static bool Contains<T>(T[] values, T value)
    {
        if (values == null) return false;
        for (int i = 0; i < values.Length; i++) if (EqualityComparer<T>.Default.Equals(values[i], value)) return true;
        return false;
    }

    // Compatibility for explicitly declared legacy states; supported states use typed contact events.
    public void NotifyFootstep() => legacyNotification = true;
    public void EmitValidationStep(bool running) => Emit(running ? FootstepMotionKind.Run : FootstepMotionKind.Walk, transform.position, true);

    private bool Emit(FootstepMotionKind kind, Vector3 samplePosition, bool ignoreRateLimit = false)
    {
        if (!ignoreRateLimit && Time.time - lastEmissionTime < minimumEmissionInterval) return false;
        SurfaceProfile profile = surfaceResolver != null ? surfaceResolver.Resolve(samplePosition) : null;
        if (profile == null) return false;
        lastEmissionTime = Time.time; LastProfile = profile; LastMotionKind = kind;
        LastSamplePosition = samplePosition; LastEmissionFrame = Time.frameCount;
        if (kind == FootstepMotionKind.Land) LandingEmissionCount++; else StepEmissionCount++;
        AudioClip clip = profile.PickClip(kind);
        if (audioSource != null && clip != null)
        {
            audioSource.pitch = UnityEngine.Random.Range(profile.PitchMin, profile.PitchMax);
            audioSource.PlayOneShot(clip, UnityEngine.Random.Range(profile.VolumeMin, profile.VolumeMax));
        }
        Emitted?.Invoke(profile, kind);
        return true;
    }
    private void ResolveReferences()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (motor == null) motor = GetComponent<OverburstCharacterMotor3D>();
        if (surfaceResolver == null) surfaceResolver = GetComponent<SurfaceResolver>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        state = GetComponent<PlayerStateCoordinator>(); melee = GetComponent<MeleeRuntime>();
    }
    private void CacheStateHashes()
    {
        contactStateHashes = new int[animationContactStates.Length];
        for (int i = 0; i < contactStateHashes.Length; i++) contactStateHashes[i] = Animator.StringToHash(animationContactStates[i]);
        legacyStateHashes = new int[legacyDistanceStates.Length];
        for (int i = 0; i < legacyStateHashes.Length; i++) legacyStateHashes[i] = Animator.StringToHash(legacyDistanceStates[i]);
    }
    private void RefreshLayers()
    {
        boundController = animator.runtimeAnimatorController;
        combatLayer = animator.GetLayerIndex("Combat_MeleeWeapon");
        lowerLayer = animator.GetLayerIndex("Combat_MeleeWeapon_TransitionLower");
    }
    private void ClearCandidates() { candidateCount = 0; candidateFrame = -1; legacyNotification = false; }
    private void ClearFootHistory()
    {
        for (int i = 0; i < 2; i++)
        { lastFootTimes[i] = float.NegativeInfinity; lastFootLoops[i] = int.MinValue; lastFootStates[i] = 0; lastFootClips[i] = null; }
    }
    private void ResetTracking()
    {
        previousPosition = transform.position; hasPreviousPosition = true; accumulatedDistance = 0f;
        ClearCandidates(); ClearFootHistory(); lastLandingFrame = -1; lastEmissionTime = float.NegativeInfinity;
        selectedClip = null; selectedStateHash = 0; PlaybackMode = PlayerFootstepPlaybackMode.Disabled;
    }
}
