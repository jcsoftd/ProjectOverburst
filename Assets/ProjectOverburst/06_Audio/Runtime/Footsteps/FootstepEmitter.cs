using System;
using UnityEngine;

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
    [SerializeField, Min(0f)] private float animationEventGraceSeconds = 0.3f;
    [SerializeField, Min(0f)] private float minimumEmissionInterval = 0.08f;

    private Vector3 previousPosition;
    private float accumulatedDistance;
    private float animationDrivenUntil;
    private float lastEmissionTime = float.NegativeInfinity;
    private int lastLandingFrame = -1;
    private bool hasPreviousPosition;

    public int StepEmissionCount { get; private set; }
    public int LandingEmissionCount { get; private set; }
    public SurfaceProfile LastProfile { get; private set; }
    public FootstepMotionKind LastMotionKind { get; private set; }
    public event Action<SurfaceProfile, FootstepMotionKind> Emitted;

    public void Configure(
        PlayerMovement configuredMovement,
        OverburstCharacterMotor3D configuredMotor,
        SurfaceResolver configuredResolver,
        AudioSource configuredAudioSource,
        float configuredWalkStride,
        float configuredRunStride,
        float configuredLandingMinimumFallSpeed)
    {
        movement = configuredMovement;
        motor = configuredMotor;
        surfaceResolver = configuredResolver;
        audioSource = configuredAudioSource;
        walkStrideDistance = Mathf.Max(0.1f, configuredWalkStride);
        runStrideDistance = Mathf.Max(0.1f, configuredRunStride);
        landingMinimumFallSpeed = Mathf.Max(0f, configuredLandingMinimumFallSpeed);
    }

    private void Awake()
    {
        ResolveReferences();
        ResetTracking();
    }

    private void OnEnable() => ResetTracking();

    private void OnDisable()
    {
        accumulatedDistance = 0f;
        hasPreviousPosition = false;
    }

    private void Update()
    {
        ResolveReferences();
        Vector3 currentPosition = transform.position;
        if (!hasPreviousPosition)
        {
            previousPosition = currentPosition;
            hasPreviousPosition = true;
            return;
        }

        Vector3 delta = currentPosition - previousPosition;
        previousPosition = currentPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude > 9f)
        {
            accumulatedDistance = 0f;
            return;
        }

        if (motor != null
            && motor.DidLandThisStep
            && motor.LandingFallSpeed <= -landingMinimumFallSpeed
            && lastLandingFrame != Time.frameCount)
        {
            lastLandingFrame = Time.frameCount;
            Emit(FootstepMotionKind.Land);
            accumulatedDistance = 0f;
        }

        if (motor == null || !motor.IsGrounded)
        {
            accumulatedDistance = 0f;
            return;
        }

        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        if (speed < minimumMoveSpeed)
            return;

        if (Time.unscaledTime < animationDrivenUntil)
            return;

        accumulatedDistance += delta.magnitude;
        bool running = movement != null && movement.IsRunning;
        float stride = running ? runStrideDistance : walkStrideDistance;
        while (accumulatedDistance >= stride)
        {
            accumulatedDistance -= stride;
            Emit(running ? FootstepMotionKind.Run : FootstepMotionKind.Walk);
        }
    }

    // Animator 이벤트가 연결되면 거리 주기를 잠시 대체해 같은 발걸음의 이중 재생을 막는다.
    public void NotifyFootstep()
    {
        animationDrivenUntil = Time.unscaledTime + animationEventGraceSeconds;
        accumulatedDistance = 0f;
        bool running = movement != null && movement.IsRunning;
        Emit(running ? FootstepMotionKind.Run : FootstepMotionKind.Walk);
    }

    public void EmitValidationStep(bool running)
    {
        Emit(running ? FootstepMotionKind.Run : FootstepMotionKind.Walk, true);
    }

    private void Emit(FootstepMotionKind kind, bool ignoreRateLimit = false)
    {
        if (!ignoreRateLimit && Time.unscaledTime - lastEmissionTime < minimumEmissionInterval)
            return;

        SurfaceProfile profile = surfaceResolver != null ? surfaceResolver.Resolve() : null;
        if (profile == null)
            return;

        lastEmissionTime = Time.unscaledTime;
        LastProfile = profile;
        LastMotionKind = kind;
        if (kind == FootstepMotionKind.Land)
            LandingEmissionCount++;
        else
            StepEmissionCount++;

        AudioClip clip = profile.PickClip(kind);
        if (audioSource != null && clip != null)
        {
            audioSource.pitch = UnityEngine.Random.Range(profile.PitchMin, profile.PitchMax);
            audioSource.PlayOneShot(clip, UnityEngine.Random.Range(profile.VolumeMin, profile.VolumeMax));
        }
        Emitted?.Invoke(profile, kind);
    }

    private void ResolveReferences()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
        if (motor == null)
            motor = GetComponent<OverburstCharacterMotor3D>();
        if (surfaceResolver == null)
            surfaceResolver = GetComponent<SurfaceResolver>();
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void ResetTracking()
    {
        previousPosition = transform.position;
        hasPreviousPosition = true;
        accumulatedDistance = 0f;
        animationDrivenUntil = 0f;
        lastLandingFrame = -1;
    }
}
