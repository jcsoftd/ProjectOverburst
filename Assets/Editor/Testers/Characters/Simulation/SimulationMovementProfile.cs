using UnityEngine;
using UnityEditor;

[CreateAssetMenu(menuName = "OVERBURST/Validation/Simulation Movement Profile")]
public sealed class SimulationMovementProfile : ScriptableObject
{
    public const string DefaultAssetPath = "Assets/Editor/Testers/Characters/Simulation/Data/EnemyPursuitMovementProfile.asset";


    [Header("Target fixture movement captured before companion removal")]
    [SerializeField] private float formationArrivalDistance = 0.3f;
    public float FormationArrivalDistance => formationArrivalDistance;
    [SerializeField] private float catchUpEnterDistance = 3.25f;
    public float CatchUpEnterDistance => catchUpEnterDistance;
    [SerializeField] private float catchUpExitDistance = 2.25f;
    public float CatchUpExitDistance => catchUpExitDistance;
    [SerializeField] private float catchUpSpeedMultiplier = 1.15f;
    public float CatchUpSpeedMultiplier => catchUpSpeedMultiplier;

    [Header("Body Metrics")]
    [SerializeField] private float fallbackBodyRadius = 0.35f;
    [SerializeField] private float clearance = 0.15f;
    [SerializeField] private float companionClearance = 0.25f;

    [Header("Leader Exclusion")]
    [SerializeField] private float softMargin = 0.55f;
    [SerializeField] private float escapePadding = 0.15f;
    [SerializeField] private float lookAheadTime = 0.20f;
    [SerializeField, Range(0f, 1f)] private float softAvoidanceWeight = 0.65f;

    [Header("Yield")]
    [SerializeField] private float leaderPathLookAheadTime = 0.35f;
    [SerializeField] private float yieldSideDistance = 0.75f;
    [SerializeField] private float yieldReleaseDuration = 0.20f;

    [Header("Formation")]
    [SerializeField] private float narrowEnterDuration = 0.15f;
    [SerializeField] private float wideExitDuration = 0.35f;
    [SerializeField] private float pathSampleDistance = 0.40f;
    [SerializeField] private float followTrailRetentionDistance = 4f;
    [SerializeField] private float corridorProbeDistance = 2.25f;
    [SerializeField] private float minimumCompressedLateralOffset = 0.35f;
    [SerializeField] private float wallClearance = 0.15f;
    [SerializeField] private float leaderMotionHoldDuration = 0.12f;

    [Header("Recovery")]
    [SerializeField] private float warpDistance = 15f;
    [SerializeField] private float stuckObservationDuration = 1f;
    [SerializeField] private float forcedRecoveryDuration = 2.5f;
    [SerializeField] private float minimumProgressDistance = 0.10f;
    [SerializeField] private float recoveryCandidateStep = 0.50f;
    [SerializeField] private int recoveryCandidateCount = 8;

    [Header("Environment")]
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float groundProbeHeight = 1.5f;
    [SerializeField] private float groundProbeDistance = 3f;

    public float FallbackBodyRadius => Mathf.Max(0.01f, fallbackBodyRadius);
    public float Clearance => Mathf.Max(0f, clearance);
    public float CompanionClearance => Mathf.Max(0f, companionClearance);
    public float SoftMargin => Mathf.Max(0.01f, softMargin);
    public float EscapePadding => Mathf.Max(0.01f, escapePadding);
    public float LookAheadTime => Mathf.Max(0f, lookAheadTime);
    public float SoftAvoidanceWeight => Mathf.Clamp01(softAvoidanceWeight);
    public float LeaderPathLookAheadTime => Mathf.Max(0f, leaderPathLookAheadTime);
    public float YieldSideDistance => Mathf.Max(0.1f, yieldSideDistance);
    public float YieldReleaseDuration => Mathf.Max(0f, yieldReleaseDuration);
    public float NarrowEnterDuration => Mathf.Max(0f, narrowEnterDuration);
    public float WideExitDuration => Mathf.Max(0f, wideExitDuration);
    public float PathSampleDistance => Mathf.Max(0.05f, pathSampleDistance);
    public float FollowTrailRetentionDistance => Mathf.Max(PathSampleDistance, followTrailRetentionDistance);
    public float CorridorProbeDistance => Mathf.Max(0.5f, corridorProbeDistance);
    public float MinimumCompressedLateralOffset => Mathf.Max(0f, minimumCompressedLateralOffset);
    public float WallClearance => Mathf.Max(0f, wallClearance);
    public float LeaderMotionHoldDuration => Mathf.Max(0.01f, leaderMotionHoldDuration);
    public float WarpDistance => Mathf.Max(1f, warpDistance);
    public float StuckObservationDuration => Mathf.Max(0.1f, stuckObservationDuration);
    public float ForcedRecoveryDuration => Mathf.Max(StuckObservationDuration, forcedRecoveryDuration);
    public float MinimumProgressDistance => Mathf.Max(0.01f, minimumProgressDistance);
    public float RecoveryCandidateStep => Mathf.Max(0.1f, recoveryCandidateStep);
    public int RecoveryCandidateCount => Mathf.Clamp(recoveryCandidateCount, 1, 24);
    public float GroundProbeHeight => Mathf.Max(0.1f, groundProbeHeight);
    public float GroundProbeDistance => Mathf.Max(0.1f, groundProbeDistance);
    public int GroundMask => groundMask.value != 0 ? groundMask.value : ResolveLayerMask("Ground");
    public int ObstacleMask => obstacleMask.value != 0
        ? obstacleMask.value
        : ResolveLayerMask("Default", "PlayerBoundary");

    public static SimulationMovementProfile LoadDefault()
    {
        return AssetDatabase.LoadAssetAtPath<SimulationMovementProfile>(DefaultAssetPath);
    }

    public static SimulationMovementProfile CreateRuntimeFallback()
    {
        SimulationMovementProfile profile = CreateInstance<SimulationMovementProfile>();
        profile.name = "PartyMovementProfile_RuntimeFallback";
        return profile;
    }

    private static int ResolveLayerMask(params string[] layerNames)
    {
        int mask = 0;
        for (int i = 0; i < layerNames.Length; i++)
        {
            int layer = LayerMask.NameToLayer(layerNames[i]);
            if (layer >= 0)
                mask |= 1 << layer;
        }

        return mask;
    }
}
