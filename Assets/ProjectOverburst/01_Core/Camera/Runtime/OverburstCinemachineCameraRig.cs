using Unity.Cinemachine;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OverburstCinemachineCameraRig : MonoBehaviour
{
    public const int CombatImpulseChannel = 1 << 4;

    [Header("Output")]
    [SerializeField] private Camera outputCamera;
    [SerializeField] private CinemachineBrain brain;

    [Header("Virtual Camera")]
    [SerializeField] private CinemachineCamera virtualCamera;
    [SerializeField] private Transform focusTarget;
    [SerializeField] private CinemachineFollow follow;
    [SerializeField] private CinemachineRotationComposer rotationComposer;
    [SerializeField] private CinemachineDeoccluder deoccluder;
    [SerializeField] private CinemachineConfiner3D confiner;

    [Header("Combat Impact")]
    [SerializeField] private CinemachineImpulseSource impulseSource;
    [SerializeField] private CinemachineImpulseListener impulseListener;

    [Header("Projection")]
    [SerializeField, Min(0.01f)] private float referenceDistance = 20f;
    [SerializeField, Min(0.01f)] private float referenceOrthographicSize = 6.887f;

    private CinemachineImpulseManager.ImpulseEvent activeImpactEvent;
    private CombatImpactSignal activeImpactSignal;
    private int emittedImpactCount;
    private CombatCameraRequestKind lastImpactKind;

    public Camera OutputCamera => outputCamera;
    public CinemachineBrain Brain => brain;
    public CinemachineCamera VirtualCamera => virtualCamera;
    public Transform FocusTarget => focusTarget;
    public CinemachineFollow Follow => follow;
    public CinemachineRotationComposer RotationComposer => rotationComposer;
    public CinemachineDeoccluder Deoccluder => deoccluder;
    public CinemachineConfiner3D Confiner => confiner;
    public CinemachineImpulseSource ImpulseSource => impulseSource;
    public CinemachineImpulseListener ImpulseListener => impulseListener;
    public int EmittedImpactCount => emittedImpactCount;
    public CombatCameraRequestKind LastImpactKind => lastImpactKind;
    public float ReferenceDistance => referenceDistance;
    public float ReferenceOrthographicSize => referenceOrthographicSize;
    public float CurrentOrthographicSize => virtualCamera != null
        ? virtualCamera.Lens.OrthographicSize
        : referenceOrthographicSize;
    public bool IsConfigured => outputCamera != null
        && brain != null
        && virtualCamera != null
        && focusTarget != null
        && follow != null
        && rotationComposer != null
        && deoccluder != null
        && confiner != null
        && impulseSource != null
        && impulseListener != null;

    public void Configure(
        Camera configuredOutputCamera,
        CinemachineBrain configuredBrain,
        CinemachineCamera configuredVirtualCamera,
        Transform configuredFocusTarget,
        CinemachineFollow configuredFollow,
        CinemachineRotationComposer configuredRotationComposer,
        CinemachineDeoccluder configuredDeoccluder,
        CinemachineConfiner3D configuredConfiner,
        CinemachineImpulseSource configuredImpulseSource,
        CinemachineImpulseListener configuredImpulseListener,
        float configuredReferenceDistance,
        float configuredReferenceOrthographicSize)
    {
        outputCamera = configuredOutputCamera;
        brain = configuredBrain;
        virtualCamera = configuredVirtualCamera;
        focusTarget = configuredFocusTarget;
        follow = configuredFollow;
        rotationComposer = configuredRotationComposer;
        deoccluder = configuredDeoccluder;
        confiner = configuredConfiner;
        impulseSource = configuredImpulseSource;
        impulseListener = configuredImpulseListener;
        referenceDistance = Mathf.Max(0.01f, configuredReferenceDistance);
        referenceOrthographicSize = Mathf.Max(0.01f, configuredReferenceOrthographicSize);
    }

    public void SynchronizeView(Vector3 focusPosition, float pitch, float yaw, float distance, bool cut)
    {
        SynchronizeView(focusPosition, pitch, yaw, distance, cut, 1f);
    }

    public void SynchronizeView(Vector3 focusPosition, float pitch, float yaw, float distance, bool cut, float frameScale)
    {
        if (!IsConfigured)
            return;

        focusTarget.position = focusPosition;
        CameraTarget targets = virtualCamera.Target;
        targets.TrackingTarget = focusTarget;
        targets.LookAtTarget = focusTarget;
        virtualCamera.Target = targets;

        Quaternion viewRotation = Quaternion.Euler(pitch, yaw, 0f);
        follow.FollowOffset = viewRotation * Vector3.back * Mathf.Max(0.01f, distance);

        LensSettings lens = virtualCamera.Lens;
        lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
        lens.OrthographicSize = referenceOrthographicSize
            * Mathf.Max(0.01f, distance)
            / referenceDistance
            * Mathf.Max(0.05f, frameScale);
        virtualCamera.Lens = lens;

        if (cut)
            virtualCamera.PreviousStateIsValid = false;
    }

    public void SetCombatDutch(float dutch)
    {
        if (virtualCamera == null)
            return;

        LensSettings lens = virtualCamera.Lens;
        lens.Dutch = dutch;
        virtualCamera.Lens = lens;
    }

    public void RenderNow()
    {
        if (!IsConfigured || !Application.isPlaying)
            return;

        brain.ManualUpdate();
    }

    public void SetConfinerVolume(Collider volume, float slowingDistance = 0f)
    {
        if (confiner == null)
            return;

        confiner.BoundingVolume = volume;
        confiner.SlowingDistance = Mathf.Max(0f, slowingDistance);
    }

    public void SetPriority(int priority)
    {
        if (virtualCamera != null)
            virtualCamera.Priority = priority;
    }

    public void EmitCombatImpact(
        CombatCameraRequestKind requestKind,
        Vector2 screenDirection,
        float duration,
        float positionAmplitude,
        float rollAmplitude,
        float returnRatio,
        float microShakeDuration,
        float microShakeAmplitude,
        float positionSafetyLimit,
        float rollSafetyLimit)
    {
        if (!IsConfigured || duration <= 0f)
            return;

        if (activeImpactEvent != null && !activeImpactEvent.Expired)
            activeImpactEvent.Cancel(CinemachineCore.CurrentTime, true);

        Vector2 direction = screenDirection.sqrMagnitude > 0.0001f
            ? screenDirection.normalized
            : Vector2.right;
        float screenScale = Mathf.Max(0.01f, CurrentOrthographicSize) * 0.1f;
        float resolvedPosition = Mathf.Min(
            Mathf.Max(0f, positionAmplitude),
            Mathf.Max(0f, positionSafetyLimit)) * screenScale;
        float resolvedMicro = Mathf.Min(
            Mathf.Max(0f, positionAmplitude) * Mathf.Clamp01(microShakeAmplitude),
            Mathf.Max(0f, positionSafetyLimit)) * screenScale;
        float resolvedRoll = Mathf.Min(
            Mathf.Max(0f, rollAmplitude),
            Mathf.Max(0f, rollSafetyLimit));

        CinemachineImpulseDefinition definition = impulseSource.ImpulseDefinition;
        if (definition == null)
        {
            definition = new CinemachineImpulseDefinition();
            impulseSource.ImpulseDefinition = definition;
        }
        definition.ImpulseChannel = CombatImpulseChannel;
        definition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Custom;
        definition.CustomImpulseShape ??= AnimationCurve.Linear(0f, 1f, 1f, 0f);
        definition.ImpulseDuration = duration;
        definition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        definition.DissipationDistance = 100f;
        definition.DissipationRate = 0f;
        definition.PropagationSpeed = 343f;

        activeImpactEvent = definition.CreateAndReturnEvent(transform.position, Vector3.one);
        if (activeImpactEvent == null)
            return;

        activeImpactSignal = new CombatImpactSignal(
            duration,
            direction,
            resolvedPosition,
            resolvedRoll,
            Mathf.Clamp01(returnRatio),
            Mathf.Max(0f, microShakeDuration),
            resolvedMicro);
        activeImpactEvent.SignalSource = activeImpactSignal;
        emittedImpactCount++;
        lastImpactKind = requestKind;
    }

    public void BoostCombatMicroShake(float positionAmplitude, float microShakeAmplitude, float positionSafetyLimit)
    {
        if (activeImpactSignal == null || activeImpactEvent == null || activeImpactEvent.Expired)
            return;

        float screenScale = Mathf.Max(0.01f, CurrentOrthographicSize) * 0.1f;
        float resolvedMicro = Mathf.Min(
            Mathf.Max(0f, positionAmplitude) * Mathf.Clamp01(microShakeAmplitude),
            Mathf.Max(0f, positionSafetyLimit)) * screenScale;
        activeImpactSignal.BoostMicroShake(resolvedMicro);
    }

    public void CancelCombatImpact()
    {
        if (activeImpactEvent != null && !activeImpactEvent.Expired)
            activeImpactEvent.Cancel(CinemachineCore.CurrentTime, true);
        activeImpactEvent = null;
        activeImpactSignal = null;
        SetCombatDutch(0f);
    }

    private void OnDisable()
    {
        CancelCombatImpact();
    }

    private sealed class CombatImpactSignal : ISignalSource6D
    {
        private readonly float duration;
        private readonly Vector2 direction;
        private readonly Vector2 perpendicular;
        private readonly float positionAmplitude;
        private readonly float rollAmplitude;
        private readonly float kickDuration;
        private readonly float microStart;
        private readonly float microDuration;
        private float microAmplitude;

        public float SignalDuration => duration;

        public CombatImpactSignal(
            float duration,
            Vector2 direction,
            float positionAmplitude,
            float rollAmplitude,
            float returnRatio,
            float microDuration,
            float microAmplitude)
        {
            this.duration = Mathf.Max(0.0001f, duration);
            this.direction = direction;
            perpendicular = new Vector2(direction.y, -direction.x);
            this.positionAmplitude = positionAmplitude;
            this.rollAmplitude = rollAmplitude;
            kickDuration = this.duration * returnRatio;
            this.microDuration = Mathf.Min(this.duration, microDuration);
            microStart = this.duration - this.microDuration;
            this.microAmplitude = microAmplitude;
        }

        public void BoostMicroShake(float amplitude)
        {
            microAmplitude = Mathf.Max(microAmplitude, Mathf.Max(0f, amplitude));
        }

        public void GetSignal(float timeSinceSignalStart, out Vector3 pos, out Quaternion rot)
        {
            float time = Mathf.Clamp(timeSinceSignalStart, 0f, duration);
            float kickProgress = kickDuration <= 0f ? 1f : Mathf.Clamp01(time / kickDuration);
            float kickEnvelope = 1f - Mathf.SmoothStep(0f, 1f, kickProgress);
            Vector2 offset = direction * positionAmplitude * kickEnvelope;

            if (microDuration > 0f && time >= microStart && time < duration)
            {
                float microProgress = Mathf.Clamp01((time - microStart) / microDuration);
                float microEnvelope = (1f - microProgress) * microAmplitude;
                offset += perpendicular * Mathf.Sin(time * 90f) * microEnvelope;
            }

            pos = new Vector3(offset.x, offset.y, 0f);
            rot = Quaternion.AngleAxis(rollAmplitude * direction.x * kickEnvelope, Vector3.forward);
        }
    }
}
