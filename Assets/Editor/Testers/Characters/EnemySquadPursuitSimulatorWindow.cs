using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class EnemySquadPursuitSimulatorWindow : EditorWindow
{
    private const string WindowTitle = "몬스터 부대 추격 시뮬레이터";
    private const string MenuPath = "JC Tool/AI/몬스터 부대 추격 시뮬레이터";
    private const string MurlocAiPresetAssetPath = "Assets/ProjectOverburst/Resources/Enemies/AiPresets/AIP_MurlocSquad.asset";
    private const string ActiveAiPresetId = "ProtofactorSquad";
    private const string PlayerActorPrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    private const int SimulationTargetCount = 3; // Optional multi-target test fixture, independent of gameplay actors.
    private const string PartyMovementProfileAssetPath = "Assets/Editor/Testers/Characters/Simulation/Data/EnemyPursuitMovementProfile.asset";
    private const float LeftPanelWidth = 268f;
    private const float RightPanelWidth = 278f;
    private const float PanelGap = 8f;
    private const float MinimumCanvasWidth = 460f;
    private const string SettingsKeyPrefix = "ProjectVTP3D.EnemySquadPursuitSimulator.Settings.v2.";
    private const float DefaultBaseMonsterSpeed = 3f;
    private const float LegacyMonsterSpeed = 2.4f;
    private const float NearCombatSpeed = 2.65f;
    private const float MonsterBodyRadius = 0.18f;
    private const float DefaultMonsterSeparationRadius = 0.72f;
    private const float DefaultMonsterSeparationStrength = 1.4f;
    private const float DefaultSlotArrivalDistance = 1.6f;
    private const float FormationOffsetScale = 0.52f;
    private const float MinimumPursuitForwardProgress = 0.2f;
    private const float RuntimeHardOverlapCorrection = EnemyCrowdService.MaximumCentralCorrection;
    private const float HigherPrioritySeparationScale = 0.15f;
    private const float LowerPrioritySeparationScale = 1.35f;
    private const float DefaultReserveOrbitAngularSpeed = 6f;
    private const float DefaultAggroDetectionRange = 10f;
    private const float DefaultSupportCallRange = 12f;
    private const float DefaultAggroReleaseDelay = 2f;
    private const float ReturnArriveDistance = 0.3f;
    private const float FarReentryDelay = 0.65f;
    private const float RouteRefreshMoveThreshold = EnemySquadPursuitPlanner.DefaultRouteRefreshMoveThreshold;
    private const float RouteRefreshMinimumInterval = EnemySquadPursuitPlanner.DefaultRouteRefreshMinimumInterval;
    private const float RouteRefreshHeadingThreshold = EnemySquadPursuitPlanner.DefaultRouteRefreshHeadingThreshold;
    private const int MaximumRouteRefreshPerFrame = EnemySquadPursuitPlanner.DefaultMaximumRouteRefreshPerFrame;
    private const float FullReformationInterval = EnemySquadPursuitPlanner.DefaultFullReformationInterval;
    private const float FullReformationMoveTrigger = EnemySquadPursuitPlanner.DefaultFullReformationMoveTrigger;
    private const float SlotLeaseDuration = 2.5f;
    private const float CenterSmoothSpeed = 5f;
    private const float DefaultMapHalfExtent = 50f;
    private const float MinimumViewZoom = 0.35f;
    private const float MaximumViewZoom = 4f;
    private const float DefaultSpawnInterval = 2f;
    private const int DefaultSpawnBatchCount = 8;
    private const float DefaultSpawnRadius = 21f;
    private const float TrailRecordInterval = 0.18f;
    private const int MaximumTrailPoints = 48;
    private const int MaximumEventLines = 9;
    private const float FixedSimulationStep = 1f / 60f;
    private const float DefaultPartyWalkSpeed = 4.5f;
    private const float DefaultPartyRunSpeed = 7.8f;
    private const float DefaultPartyAcceleration = 55f;
    private const float DefaultPartyDeceleration = 70f;
    private const float DefaultPartyArrivalDistance = 0.3f;
    private const float DefaultPartyCatchUpEnterDistance = 3.25f;
    private const float DefaultPartyCatchUpExitDistance = 2.25f;
    private const float DefaultPartyCatchUpSpeedMultiplier = 1.15f;
    private const float DefaultPartyTargetRadius = 0.45f;

    private static readonly Vector2 MinimumWindowSize = new Vector2(1080f, 690f);
    private static readonly Color CanvasBackground = new Color(0.105f, 0.12f, 0.14f, 1f);
    private static readonly Color GridMinorColor = new Color(0.25f, 0.28f, 0.31f, 0.24f);
    private static readonly Color GridMajorColor = new Color(0.38f, 0.42f, 0.46f, 0.34f);
    private static readonly Color PlayerColor = new Color(0.96f, 0.9f, 0.3f, 1f);
    private static readonly Color PartyFollowerTwoColor = new Color(0.2f, 0.78f, 1f, 1f);
    private static readonly Color PartyFollowerThreeColor = new Color(0.72f, 0.42f, 1f, 1f);
    private static readonly Color PartyTrailColor = new Color(0.5f, 0.82f, 1f, 0.42f);
    private static readonly Color NearRangeColor = EnemySquadPursuitDebugPalette.NearRangeColor;
    private static readonly Color CommitRangeColor = EnemySquadPursuitDebugPalette.CommitRangeColor;
    private static readonly Color FarRangeColor = EnemySquadPursuitDebugPalette.FarRangeColor;
    private static readonly Color ReserveOrbitRangeColor = new Color(0.72f, 0.46f, 0.96f, 0.72f);
    private static readonly Color AggroDetectionRangeColor = new Color(0.38f, 0.95f, 0.48f, 0.72f);
    private static readonly Color SupportCallRangeColor = new Color(1f, 0.72f, 0.22f, 0.66f);
    private static readonly Color AggroRangeColor = new Color(1f, 0.28f, 0.22f, 0.68f);
    private static readonly Color MemberEngageRangeColor = new Color(1f, 0.48f, 0.08f, 0.92f);
    private static readonly Color RoamColor = new Color(0.34f, 0.72f, 0.48f, 0.9f);
    private static readonly Color ReturnColor = new Color(0.42f, 0.46f, 0.5f, 0.82f);
    private static readonly Color SpawnRangeColor = new Color(0.72f, 0.88f, 0.42f, 0.52f);
    private static readonly Color ObstacleColor = new Color(0.3f, 0.32f, 0.35f, 0.9f);
    private static readonly Color DeadColor = new Color(0.42f, 0.42f, 0.42f, 0.5f);
    private static readonly Color BlockedSlotColor = new Color(0.96f, 0.24f, 0.2f, 0.95f);
    private static readonly Color UnassignedSquadColor = new Color(0.42f, 0.46f, 0.52f, 1f);

    private static readonly string[] CoreMurlocPrefabPaths =
    {
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Scout.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Spearling.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Guard.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Brute.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Warlord.prefab"
    };

    private enum SpawnPreset
    {
        한쪽밀집,
        부채꼴,
        원형포위,
        긴대열,
        좁은통로
    }

    private enum SimulationDataMode
    {
        균일테스트,
        AI프리셋데이터
    }

    private enum PartySimulationMode
    {
        단일리더,
        고정리더3인파티
    }

    private enum SquadMode
    {
        Legacy,
        Pursuit,
        Reserve,
        Rush,
        NearCombat,
        Remnant,
        Dead
    }

    private sealed class SimEnemy
    {
        public int Id;
        public Vector3 Position;
        public Vector3 HomePosition;
        public Vector3 SnapshotPosition;
        public Vector3 DesiredPosition;
        public Vector3 Velocity;
        public Vector3 LocalOffset;
        public string ArchetypeName = "Uniform";
        public bool SquadParticipant = true;
        public float WalkSpeed = DefaultBaseMonsterSpeed;
        public float PursuitSpeed = DefaultBaseMonsterSpeed;
        public float BodyRadius = MonsterBodyRadius;
        public float CrowdWeight = 1f;
        public float SeparationRadius = DefaultMonsterSeparationRadius;
        public float SeparationWeight = 1f;
        public int MovePriority;
        public int SquadId = -1;
        public float DetectionRange = DefaultAggroDetectionRange;
        public float SupportCallRange = DefaultSupportCallRange;
        public float AlertDuration;
        public float AlertRemaining;
        public float AggroOutsideTime;
        public Vector3 YieldDirection;
        public float YieldUntilTime;
        public float YieldDirectionLockUntilTime;
        public float YieldPressure;
        public bool AggroActive;
        public bool Returning;
        public bool SkipRoamDetectionOnce;
        public bool HasMoveIntent;
        public bool Alive = true;
        public EnemyPartyTargetPhase TargetPhase;
        public int TargetPartyIndex = -1;
        public float MemberEngageRange = EnemyBehaviorProfile.DefaultMemberEngageRange;
    }

    private sealed class SimPartyFollower
    {
        public readonly SimulationFollowTrail Trail = new SimulationFollowTrail();
        public int MemberIndex;
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 TargetPosition;
        public Vector3 FacingDirection = Vector3.forward;
        public ActorMovementGait Gait = ActorMovementGait.Run;
        public bool CatchingUp;
    }

    private sealed class EnemyMinimumData
    {
        public bool SquadParticipant = true;
        public string Name;
        public float WalkSpeed;
        public float RunSpeed;
        public float BodyRadius;
        public float CrowdWeight;
        public float SeparationRadius;
        public float SeparationWeight;
        public float DetectionRange;
        public float SupportCallRange;
        public float AlertDuration;
        public float MemberEngageRange;
    }

    private sealed class SimSquad
    {
        public int Id;
        public readonly List<SimEnemy> Members = new List<SimEnemy>(12);
        public readonly List<Vector3> Trail = new List<Vector3>(MaximumTrailPoints);
        public int InitialCount;
        public int SlotIndex = -1;
        public int RouteWaypointIndex;
        public EnemySquadPursuitRoute Route;
        public Vector3 RouteSlotDirection;
        public bool DirectCommitted;
        public bool HasRushed;
        public bool SlotEligibilityRevoked;
        public SquadMode Mode = SquadMode.Reserve;
        public Vector3 RawCenter;
        public Vector3 SmoothedCenter;
        public Vector3 MoveDirection;
        public float FarEligibleTime;
        public float NextTrailTime;
        public float SlotLeaseUntil;

        public int AliveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Members.Count; i++)
                {
                    if (Members[i].Alive)
                        count++;
                }

                return count;
            }
        }

        public int ParticipantCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Members.Count; i++)
                {
                    if (Members[i].Alive && Members[i].AggroActive)
                        count++;
                }

                return count;
            }
        }
    }

    [Serializable]
    private sealed class SimulatorSettings // 프로젝트별 EditorPrefs 자동 저장 데이터
    {
        public string aiPresetId;
        public string themeTableGuid;
        public int spawnPreset;
        public int simulationDataMode;
        public int partySimulationMode;
        public int partyLeaderGait;
        public int requestedMonsterCount;
        public int minimumSquadSize;
        public int maximumSquadSize;
        public int stableSeed;
        public float slotRadius;
        public float directCommitRadius;
        public float nearReleaseDistance;
        public float farActivationDistance;
        public float reserveSpeedMultiplier;
        public float remnantRatio;
        public float baseMonsterSpeed;
        public float monsterSeparationRadius;
        public float monsterSeparationStrength;
        public float slotArrivalDistance;
        public float reserveOrbitRadius;
        public float reserveOrbitAngularSpeed;
        public float aggroDetectionRange;
        public float supportCallRange;
        public float mapHalfExtent;
        public float viewZoom;
        public float viewCenterX;
        public float viewCenterZ;
        public float baseAggroReleaseDistance;
        public float aggroReleaseDelay;
        public float curveRadiusMultiplier;
        public float spawnInterval;
        public float spawnRadius;
        public int spawnBatchCount;
        public float simulationSpeed;
        public float automaticKillRate;
        public bool continuousSpawnerEnabled;
        public bool spawnWithAggro;
        public bool overridePrefabAggroRanges;
        public bool automaticCombatDeaths;
        public bool showRoutes;
        public bool showTrails;
        public bool showMonsterIds;
    }

    private readonly struct SimObstacle
    {
        public SimObstacle(Rect xzRect)
        {
            XzRect = xzRect;
        }

        public Rect XzRect { get; }
    }

    private readonly List<SimEnemy> enemies = new List<SimEnemy>(220);
    private readonly List<SimSquad> squads = new List<SimSquad>(32);
    private readonly List<EnemySquadPursuitSlot> slots = new List<EnemySquadPursuitSlot>(8);
    private readonly List<int> squadSizes = new List<int>(32);
    private readonly List<SimEnemy> unassignedBuffer = new List<SimEnemy>(220);
    private readonly List<SimSquad> reserveSquadBuffer = new List<SimSquad>(32);
    private readonly List<EnemySquadPursuitSlot> assignableSlotBuffer = new List<EnemySquadPursuitSlot>(7);
    private readonly List<int> balancedSlotBuffer = new List<int>(7);
    private readonly List<int> vacantSlotBuffer = new List<int>(7);
    private readonly List<Vector3> reserveCenterBuffer = new List<Vector3>(32);
    private readonly List<Vector3> vacantSlotPositionBuffer = new List<Vector3>(7);
    private readonly List<int> squadIndexBySlotBuffer = new List<int>(7);
    private readonly List<SimObstacle> obstacles = new List<SimObstacle>(8);
    private readonly List<string> eventLines = new List<string>(MaximumEventLines);
    private readonly List<EnemyAiPreset> aiPresets = new List<EnemyAiPreset>(4);
    private readonly List<EnemyMinimumData> aiMinimumData = new List<EnemyMinimumData>(8);
    private readonly List<SimEnemy> aggroDiscoveryBuffer = new List<SimEnemy>(64);
    private readonly List<SimPartyFollower> simulatedPartyFollowers = new List<SimPartyFollower>(2);
    private readonly List<EnemyPartyTargetCandidate> partyTargetCandidates =
        new List<EnemyPartyTargetCandidate>(SimulationTargetCount);
    private readonly SimulationFollowTrail simulatedLeaderTrail = new SimulationFollowTrail();
    private readonly SimulationFormationDefinition simulatedPartyFormation = new SimulationFormationDefinition();
    private readonly List<SimEnemy> centralSolverEnemies = new List<SimEnemy>(220);
    private readonly List<EnemyCrowdPriorityBody> centralSolverBodies =
        new List<EnemyCrowdPriorityBody>(220);
    private readonly List<Vector3> centralSolverCorrections = new List<Vector3>(220);
    private readonly List<Vector3> centralSolverYieldCorrections = new List<Vector3>(220);
    private readonly List<float> centralSolverYieldPressures = new List<float>(220);

    private SpawnPreset spawnPreset = SpawnPreset.한쪽밀집;
    private SimulationDataMode simulationDataMode = SimulationDataMode.AI프리셋데이터;
    private PartySimulationMode partySimulationMode = PartySimulationMode.고정리더3인파티;
    [SerializeField] private EnemyThemeTable selectedThemeTable;
    private EnemyAiPreset selectedAiPreset;
    private string selectedAiPresetId = ActiveAiPresetId;
    private int selectedAiPresetIndex;
    private int requestedMonsterCount = 50;
    private int minimumSquadSize = EnemySquadPursuitPlanner.DefaultMinimumSquadSize;
    private int maximumSquadSize = EnemySquadPursuitPlanner.DefaultMaximumSquadSize;
    private float slotRadius = EnemySquadPursuitPlanner.DefaultSlotRadius;
    private float directCommitRadius = EnemySquadPursuitPlanner.DefaultDirectCommitRadius;
    private float nearReleaseDistance = EnemySquadPursuitPlanner.DefaultNearReleaseDistance;
    private float farActivationDistance = EnemySquadPursuitPlanner.DefaultFarActivationDistance;
    private float reserveSpeedMultiplier = EnemySquadPursuitPlanner.DefaultReserveSpeedMultiplier;
    private float remnantRatio = EnemySquadPursuitPlanner.DefaultRemnantRatio;
    private float baseMonsterSpeed = DefaultBaseMonsterSpeed;
    private float monsterSeparationRadius = DefaultMonsterSeparationRadius;
    private float monsterSeparationStrength = DefaultMonsterSeparationStrength;
    private float slotArrivalDistance = DefaultSlotArrivalDistance;
    private float reserveOrbitRadius = EnemySquadPursuitPlanner.DefaultReserveOrbitRadius;
    private float reserveOrbitAngularSpeed = DefaultReserveOrbitAngularSpeed;
    private float aggroDetectionRange = DefaultAggroDetectionRange;
    private float supportCallRange = DefaultSupportCallRange;
    private float mapHalfExtent = DefaultMapHalfExtent;
    private float viewZoom = 1f;
    private float baseAggroReleaseDistance = EnemyAIController.BaseCombatLoseTargetRange;
    private float aggroReleaseDelay = DefaultAggroReleaseDelay;
    private float curveRadiusMultiplier = 1.25f;
    private float spawnInterval = DefaultSpawnInterval;
    private float spawnRadius = DefaultSpawnRadius;
    private int spawnBatchCount = DefaultSpawnBatchCount;
    private float simulationSpeed = 1f;
    private float automaticKillRate = 2f;
    private float continuousSpawnAccumulator;
    private bool continuousSpawnerEnabled = true;
    private bool spawnWithAggro;
    private bool overridePrefabAggroRanges;
    private bool automaticCombatDeaths = true;
    private bool showRoutes = true;
    private bool showTrails = true;
    private bool showMonsterIds;
    private bool isPlaying = true;
    private bool encounterActive;
    private bool pursuitFrameInitialized;
    private bool draggingPlayer;
    private bool draggingCanvas;
    private int stableSeed = 20260715;
    private int nextEnemyId = 1;
    private int nextSquadId = 1;
    private int spawnDirectionCursor;
    private int selectedEnemyId = -1;
    private int selectedSquadId = -1;
    private float elapsedSimulationTime;
    private float simulationAccumulator;
    private float automaticKillAccumulator;
    private int sessionMonsterDeathCount;
    private float nextRouteRefreshTime;
    private float lastFullReformationTime;
    private float nextFullReformationTime;
    private float playerSpeed;
    private ActorMovementGait partyLeaderGait = ActorMovementGait.Run;
    private float partyWalkSpeed = DefaultPartyWalkSpeed;
    private float partyRunSpeed = DefaultPartyRunSpeed;
    private float partyAcceleration = DefaultPartyAcceleration;
    private float partyDeceleration = DefaultPartyDeceleration;
    private float partyArrivalDistance = DefaultPartyArrivalDistance;
    private float partyCatchUpEnterDistance = DefaultPartyCatchUpEnterDistance;
    private float partyCatchUpExitDistance = DefaultPartyCatchUpExitDistance;
    private float partyCatchUpSpeedMultiplier = DefaultPartyCatchUpSpeedMultiplier;
    private float partyTargetRadius = DefaultPartyTargetRadius;
    private int routeRefreshCursor;
    private int routeRefreshCount;
    private int fullReformationCount;
    private Vector3 playerPosition;
    private Vector3 simulatedLeaderFacing = Vector3.forward;
    private Vector3 viewCenter;
    private Vector2 lastCanvasMousePosition;
    private Vector3 pursuitFramePlayerPosition;
    private Vector3 lastRouteTranslationPlayerPosition;
    private Vector3 lastPlayerSamplePosition;
    private Vector3 lastFullReformationPosition;
    private Vector3 directOutward = Vector3.right;
    private float simulatedLeaderMotionHoldRemaining;
    private bool routeRefreshPending;
    private bool playerSampleInitialized;
    private bool suppressSettingsSave;
    private SimulationMovementProfile partyMovementProfile;
    private Vector2 leftScroll;
    private Vector2 rightScroll;
    private double lastEditorTime;
    private System.Random random;

    private GUIStyle headerStyle;
    private GUIStyle headerSubtitleStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle canvasLabelStyle;
    private GUIStyle canvasSmallLabelStyle;
    private GUIStyle centeredCanvasLabelStyle;
    private GUIStyle statusStyle;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        EnemySquadPursuitSimulatorWindow window = GetWindow<EnemySquadPursuitSimulatorWindow>(WindowTitle);
        window.minSize = MinimumWindowSize;
        window.Show();
    }

    public static void CreateMurlocAiPresetFromSavedSettings() // 배치 검증에서도 현재 EditorPrefs 튜닝값으로 에셋 생성
    {
        EnemySquadPursuitSimulatorWindow window = CreateInstance<EnemySquadPursuitSimulatorWindow>();
        window.LoadSettings();
        window.EnsureMurlocAiPreset();
        DestroyImmediate(window);

        EnemyAiPreset preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(MurlocAiPresetAssetPath);
        if (preset == null
            || !string.Equals(preset.PresetId, "MurlocSquad", StringComparison.Ordinal)
            || preset.DefaultMonsterCount != CoreMurlocPrefabPaths.Length)
        {
            throw new InvalidOperationException("Murloc AI preset creation or roster contract failed");
        }

        Debug.Log(
            "[EnemySquadPursuitSimulatorWindow] Verified AI preset=" + preset.PresetId
            + " roster=" + preset.DefaultMonsterCount
            + " squad=" + preset.MinimumSquadSize + "~" + preset.MaximumSquadSize);
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Enemy Squad Three-Party Simulator")]
    public static void VerifyThreePartySimulationContract()
    {
        EnemySquadPursuitSimulatorWindow window = CreateInstance<EnemySquadPursuitSimulatorWindow>();
        window.suppressSettingsSave = true;
        try
        {
            window.isPlaying = false;
            window.partySimulationMode = PartySimulationMode.고정리더3인파티;
            window.simulationDataMode = SimulationDataMode.균일테스트;
            window.spawnWithAggro = true;
            window.random = new System.Random(20260716);
            window.enemies.Clear();
            window.squads.Clear();
            window.obstacles.Clear();
            window.nextEnemyId = 1;
            window.playerPosition = Vector3.zero;
            window.simulatedLeaderFacing = Vector3.forward;
            window.simulatedLeaderMotionHoldRemaining = 0f;
            window.partyLeaderGait = ActorMovementGait.Run;
            window.lastPlayerSamplePosition = Vector3.zero;
            window.playerSampleInitialized = true;
            window.LoadPartySimulationRuntimeData();
            window.VerifyMaximumFirstSquadLifecycleContract();
            GameObject runtimeSource = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerActorPrefabPath);
            PlayerMovement sourceMovement = runtimeSource != null
                ? runtimeSource.GetComponent<PlayerMovement>()
                : null;
            SimulationMovementProfile sourceBrain = window.partyMovementProfile;
            if (window.partyMovementProfile == null
                || sourceMovement == null
                || sourceBrain == null
                || !Mathf.Approximately(window.partyWalkSpeed, sourceMovement.WalkMoveSpeed)
                || !Mathf.Approximately(window.partyRunSpeed, sourceMovement.RunMoveSpeed)
                || !Mathf.Approximately(window.ActivePartyMoveSpeed, sourceMovement.RunMoveSpeed)
                || !Mathf.Approximately(window.partyAcceleration, sourceMovement.MoveAcceleration)
                || !Mathf.Approximately(window.partyDeceleration, sourceMovement.MoveDeceleration)
                || !Mathf.Approximately(window.partyArrivalDistance, sourceBrain.FormationArrivalDistance)
                || !Mathf.Approximately(window.partyCatchUpEnterDistance, sourceBrain.CatchUpEnterDistance)
                || !Mathf.Approximately(window.partyCatchUpExitDistance, sourceBrain.CatchUpExitDistance)
                || !Mathf.Approximately(window.partyCatchUpSpeedMultiplier, sourceBrain.CatchUpSpeedMultiplier))
            {
                throw new InvalidOperationException("Three-party simulator did not load the in-game movement source");
            }
            window.ResetSimulatedParty();

            const float deltaTime = FixedSimulationStep;
            for (int step = 1; step <= 40; step++)
            {
                window.playerPosition = Vector3.forward * (step * 0.1f);
                window.UpdatePlayerMotion(deltaTime);
            }
            for (int step = 39; step >= 0; step--)
            {
                window.playerPosition = Vector3.forward * (step * 0.1f);
                window.UpdatePlayerMotion(deltaTime);
            }

            if (window.simulatedPartyFollowers.Count != SimulationTargetCount - 1)
                throw new InvalidOperationException("Three-party simulator follower count mismatch");
            SimPartyFollower followerTwo = window.simulatedPartyFollowers[0];
            SimPartyFollower followerThree = window.simulatedPartyFollowers[1];
            if (followerTwo.MemberIndex != 1
                || followerThree.MemberIndex != 2
                || followerTwo.TargetPosition.z <= 1f
                || followerThree.TargetPosition.z <= followerTwo.TargetPosition.z + 1f
                || Mathf.Abs(followerTwo.TargetPosition.x) > 0.05f
                || Mathf.Abs(followerThree.TargetPosition.x) > 0.05f
                || window.simulatedLeaderFacing.z >= -0.99f)
            {
                throw new InvalidOperationException(
                    "Three-party simulator did not preserve reversed single-file trail formation"
                    + " P2=" + followerTwo.TargetPosition.ToString("F3")
                    + " P3=" + followerThree.TargetPosition.ToString("F3")
                    + " facing=" + window.simulatedLeaderFacing.ToString("F3")
                    + " hold=" + window.simulatedLeaderMotionHoldRemaining.ToString("F3"));
            }

            if (followerTwo.Gait != ActorMovementGait.Run
                || followerThree.Gait != ActorMovementGait.Run)
            {
                throw new InvalidOperationException("Three-party simulator did not start with P1 Run gait");
            }

            window.partyLeaderGait = ActorMovementGait.Walk;
            window.playerPosition += Vector3.back * 0.2f;
            window.UpdatePlayerMotion(deltaTime);
            if (!Mathf.Approximately(window.ActivePartyMoveSpeed, sourceMovement.WalkMoveSpeed)
                || followerTwo.Gait != ActorMovementGait.Walk
                || followerThree.Gait != ActorMovementGait.Walk)
            {
                throw new InvalidOperationException("P2/P3 did not mirror the simulator P1 Walk gait");
            }

            window.partyLeaderGait = ActorMovementGait.Run;
            window.playerPosition += Vector3.back * 0.2f;
            window.UpdatePlayerMotion(deltaTime);

            int stopSteps = Mathf.CeilToInt(
                window.partyMovementProfile.LeaderMotionHoldDuration / deltaTime) + 1;
            for (int step = 0; step < stopSteps; step++)
                window.UpdatePlayerMotion(deltaTime);

            Vector3 stoppedLeaderForward = window.simulatedLeaderFacing.normalized;
            Vector3 stoppedLeaderRight = Vector3.Cross(Vector3.up, stoppedLeaderForward).normalized;
            Vector3 stoppedTwoOffset = followerTwo.TargetPosition - window.playerPosition;
            Vector3 stoppedThreeOffset = followerThree.TargetPosition - window.playerPosition;
            if (Mathf.Abs(stoppedTwoOffset.magnitude - window.simulatedPartyFormation.StoppedSlotRadius) > 0.02f
                || Mathf.Abs(stoppedThreeOffset.magnitude - window.simulatedPartyFormation.StoppedSlotRadius) > 0.02f
                || Vector3.Dot(stoppedTwoOffset.normalized, stoppedLeaderForward) >= -0.5f
                || Vector3.Dot(stoppedThreeOffset.normalized, stoppedLeaderForward) >= -0.5f
                || Mathf.Sign(Vector3.Dot(stoppedTwoOffset, stoppedLeaderRight))
                    == Mathf.Sign(Vector3.Dot(stoppedThreeOffset, stoppedLeaderRight)))
            {
                throw new InvalidOperationException(
                    "Three-party simulator stopped formation did not match the rear arc contract");
            }

            followerTwo.Position = Vector3.left * 10f;
            followerThree.Position = Vector3.right * 10f;

            const int enemyCount = 12;
            for (int i = 0; i < enemyCount; i++)
            {
                float angle = i * Mathf.PI * 2f / enemyCount;
                Vector3 spawnPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 30f;
                SimEnemy enemy = window.CreateEnemy(spawnPosition);
                window.enemies.Add(enemy);
            }
            window.RefreshAllEnemyPartyTargetPhases();

            if (window.CountEnemiesInTargetPhase(EnemyPartyTargetPhase.LeaderApproach) != enemyCount
                || window.CountTargetsInPhase(EnemyPartyTargetPhase.LeaderApproach, 0) != enemyCount
                || window.CountEnemiesInTargetPhase(EnemyPartyTargetPhase.MemberEngaged) != 0)
            {
                throw new InvalidOperationException("Far three-party targets did not remain in P1 LeaderApproach");
            }

            SimEnemy lockedEnemy = window.enemies[0];
            Vector3 memberTwoPosition = window.ResolvePartyMemberPosition(1);
            float engageBoundary = EnemyCombatCoordinator.ResolveMemberEngageCenterRadius(
                lockedEnemy.BodyRadius,
                window.partyTargetRadius,
                lockedEnemy.MemberEngageRange);
            float engageOffset = engageBoundary
                - Mathf.Max(0.05f, lockedEnemy.MemberEngageRange * 0.5f);
            lockedEnemy.Position = memberTwoPosition + Vector3.forward * engageOffset;
            window.RefreshAllEnemyPartyTargetPhases();
            if (lockedEnemy.TargetPhase != EnemyPartyTargetPhase.MemberEngaged
                || lockedEnemy.TargetPartyIndex != 1
                || window.CountMemberEngagedTargets(1) != 1)
            {
                throw new InvalidOperationException("Near P2 target did not enter MemberEngaged");
            }

            lockedEnemy.Position = window.ResolvePartyMemberPosition(2);
            window.RefreshAllEnemyPartyTargetPhases();
            if (lockedEnemy.TargetPhase != EnemyPartyTargetPhase.MemberEngaged
                || lockedEnemy.TargetPartyIndex != 1)
            {
                throw new InvalidOperationException("Valid P2 MemberEngaged lock was not retained");
            }

            SimEnemy companionThreeEnemy = window.enemies[1];
            Vector3 memberThreePosition = window.ResolvePartyMemberPosition(2);
            float companionThreeBoundary = EnemyCombatCoordinator.ResolveMemberEngageCenterRadius(
                companionThreeEnemy.BodyRadius,
                window.partyTargetRadius,
                companionThreeEnemy.MemberEngageRange);
            companionThreeEnemy.Position = memberThreePosition
                + Vector3.left * (companionThreeBoundary - 0.05f);
            window.RefreshAllEnemyPartyTargetPhases();
            if (companionThreeEnemy.TargetPhase != EnemyPartyTargetPhase.MemberEngaged
                || companionThreeEnemy.TargetPartyIndex != 2
                || window.CountMemberEngagedTargets(2) != 1)
            {
                throw new InvalidOperationException("Near P3 target did not enter MemberEngaged");
            }

            const float groupAnchorRange = 6f;
            float groupAnchorRangeSqr = groupAnchorRange * groupAnchorRange;
            SimEnemy groupRequester = new SimEnemy
            {
                Position = Vector3.zero,
                AggroActive = true
            };
            SimEnemy groupCandidate = new SimEnemy
            {
                Position = Vector3.right * 5f,
                AggroActive = true
            };
            Vector3 groupCandidateTarget = groupCandidate.Position + Vector3.forward * 5f;
            if (!IsEngagedGroupAnchor(
                    groupRequester,
                    groupCandidate,
                    groupCandidateTarget,
                    groupAnchorRangeSqr))
            {
                throw new InvalidOperationException("Nearby engaged group anchor was not retained");
            }

            groupCandidate.Position = Vector3.right * 7f;
            groupCandidateTarget = groupCandidate.Position + Vector3.forward * 5f;
            if (IsEngagedGroupAnchor(
                    groupRequester,
                    groupCandidate,
                    groupCandidateTarget,
                    groupAnchorRangeSqr))
            {
                throw new InvalidOperationException("Far engaged group anchor incorrectly retained requester aggro");
            }

            window.obstacles.Add(new SimObstacle(Rect.MinMaxRect(-0.25f, -0.5f, 0.25f, 0.5f)));
            bool blockedLineClear = window.IsPartyApproachClear(Vector3.left, Vector3.right, 0.1f);
            bool openLineClear = window.IsPartyApproachClear(
                Vector3.left + Vector3.forward * 2f,
                Vector3.right + Vector3.forward * 2f,
                0.1f);
            if (blockedLineClear || !openLineClear)
            {
                throw new InvalidOperationException("Party target straight obstacle filter mismatch");
            }
            window.obstacles.Clear();

            lockedEnemy.Position = Vector3.right * 30f;
            window.partySimulationMode = PartySimulationMode.단일리더;
            window.ResetSimulatedParty();
            window.RefreshAllEnemyPartyTargetPhases();
            if (window.CountEnemiesInTargetPhase(EnemyPartyTargetPhase.LeaderApproach) != enemyCount
                || window.CountTargetsInPhase(EnemyPartyTargetPhase.LeaderApproach, 0) != enemyCount
                || window.CountEnemiesInTargetPhase(EnemyPartyTargetPhase.MemberEngaged) != 0)
            {
                throw new InvalidOperationException("Single-party targets did not fall back to P1 LeaderApproach");
            }

            window.enemies.Clear();
            window.squads.Clear();
            window.encounterActive = false;
            window.continuousSpawnerEnabled = false;
            window.automaticCombatDeaths = false;
            window.partySimulationMode = PartySimulationMode.고정리더3인파티;
            window.playerPosition = Vector3.zero;
            window.lastPlayerSamplePosition = window.playerPosition;
            window.playerSampleInitialized = true;
            window.simulatedLeaderFacing = Vector3.forward;
            window.ResetSimulatedParty();
            window.baseAggroReleaseDistance = 6f;
            window.aggroReleaseDelay = 0.1f;

            SimPartyFollower boundaryFollower = window.simulatedPartyFollowers[0];
            boundaryFollower.Position = Vector3.right * 8f;
            boundaryFollower.TargetPosition = boundaryFollower.Position;
            boundaryFollower.Velocity = Vector3.zero;

            SimEnemy boundaryEnemy = window.CreateEnemy(boundaryFollower.Position);
            boundaryEnemy.AggroActive = true;
            boundaryEnemy.Returning = false;
            boundaryEnemy.TargetPhase = EnemyPartyTargetPhase.LeaderApproach;
            boundaryEnemy.TargetPartyIndex = 0;
            boundaryEnemy.AggroOutsideTime = window.aggroReleaseDelay;
            window.enemies.Add(boundaryEnemy);

            window.StepSimulation(FixedSimulationStep);
            if (!boundaryEnemy.AggroActive
                || boundaryEnemy.Returning
                || boundaryEnemy.TargetPhase != EnemyPartyTargetPhase.MemberEngaged
                || boundaryEnemy.TargetPartyIndex != 1)
            {
                throw new InvalidOperationException(
                    "P2 boundary engagement was released using the previous P1 target distance");
            }

            Debug.Log(
                "[EnemySquadPursuitSimulatorWindow] PASS fixedLeader=P1 followers=2"
                + " runGait=1 walkGait=1 reversedTrail=1 farLeader=12 engagedP2=1 engagedP3=1 retained=1"
                + " groupNear=1 groupFar=0 singleFallback=12 obstacle=1 boundaryP2=1"
                + " maximumFirst=1 recruitToMax=1 reformation=1");
        }
        finally
        {
            DestroyImmediate(window);
        }
    }

    private void VerifyMaximumFirstSquadLifecycleContract()
    {
        minimumSquadSize = 4;
        maximumSquadSize = 8;
        farActivationDistance = 9f;
        playerPosition = Vector3.zero;
        spawnWithAggro = true;
        enemies.Clear();
        squads.Clear();
        nextEnemyId = 1;
        nextSquadId = 1;

        for (int i = 0; i < 41; i++)
        {
            float angle = i * Mathf.PI * 2f / 41f;
            enemies.Add(CreateEnemy(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 30f));
        }
        BuildInitialSquads();
        RequireSimulatorSquadSizes("initial 41", 1, 8, 8, 8, 8, 8);

        RebuildStrategicSquads();
        RequireSimulatorSquadSizes("reformation 41", 1, 8, 8, 8, 8, 8);

        enemies.Clear();
        squads.Clear();
        nextEnemyId = 1;
        nextSquadId = 1;
        for (int i = 0; i < 7; i++)
            enemies.Add(CreateEnemy(Vector3.right * 30f + Vector3.forward * i));
        BuildInitialSquads();
        RequireSimulatorSquadSizes("initial 7", 0, 7);

        enemies.Add(CreateEnemy(Vector3.right * 30f + Vector3.forward * 7f));
        RecruitUnassignedEnemies();
        RequireSimulatorSquadSizes("recruit 7+1", 0, 8);

        for (int i = 0; i < 4; i++)
            enemies.Add(CreateEnemy(Vector3.right * 32f + Vector3.forward * i));
        RecruitUnassignedEnemies();
        RequireSimulatorSquadSizes("recruit 8+4", 0, 8, 4);

        enemies.Clear();
        squads.Clear();
        nextEnemyId = 1;
        nextSquadId = 1;
    }

    private void RequireSimulatorSquadSizes(
        string phase,
        int expectedUnassigned,
        params int[] expectedSizes)
    {
        List<int> actualSizes = new List<int>();
        for (int i = 0; i < squads.Count; i++)
        {
            if (squads[i].AliveCount > 0)
                actualSizes.Add(squads[i].AliveCount);
        }
        actualSizes.Sort((left, right) => right.CompareTo(left));

        int unassignedCount = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Alive && enemies[i].AggroActive && enemies[i].SquadId < 0)
                unassignedCount++;
        }

        if (actualSizes.Count != expectedSizes.Length || unassignedCount != expectedUnassigned)
        {
            throw new InvalidOperationException(
                "Simulator maximum-first squad count mismatch phase=" + phase
                + " squads=" + actualSizes.Count + " expected=" + expectedSizes.Length
                + " unassigned=" + unassignedCount + " expectedUnassigned=" + expectedUnassigned);
        }
        for (int i = 0; i < expectedSizes.Length; i++)
        {
            if (actualSizes[i] != expectedSizes[i])
            {
                throw new InvalidOperationException(
                    "Simulator maximum-first squad size mismatch phase=" + phase
                    + " index=" + i + " actual=" + actualSizes[i]
                    + " expected=" + expectedSizes[i]);
            }
        }
    }

    public static void ApplySavedSettingsToMurlocAiPreset() // 시뮬레이터 자동 저장값을 실제 게임 프리셋에 명시 반영
    {
        EnemySquadPursuitSimulatorWindow window = CreateInstance<EnemySquadPursuitSimulatorWindow>();
        try
        {
            window.LoadSettings();
            window.EnsureMurlocAiPreset();
            EnemyAiPreset preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(MurlocAiPresetAssetPath);
            if (preset == null)
                throw new InvalidOperationException("Murloc AI preset was not found");

            preset.ConfigureSquadPursuit(
                preset.ActivationCount,
                window.minimumSquadSize,
                window.maximumSquadSize,
                window.slotRadius,
                window.directCommitRadius,
                window.nearReleaseDistance,
                window.farActivationDistance,
                window.reserveSpeedMultiplier,
                window.remnantRatio,
                window.slotArrivalDistance,
                window.reserveOrbitRadius,
                window.reserveOrbitAngularSpeed,
                window.curveRadiusMultiplier);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[EnemySquadPursuitSimulatorWindow] Applied saved settings"
                + " squad=" + preset.MinimumSquadSize + "~" + preset.MaximumSquadSize
                + " slot=" + preset.SlotRadius.ToString("0.###")
                + " commit=" + preset.DirectCommitRadius.ToString("0.###")
                + " near=" + preset.NearReleaseDistance.ToString("0.###")
                + " far=" + preset.FarActivationDistance.ToString("0.###")
                + " reserve=" + preset.ReserveSpeedMultiplier.ToString("0.###")
                + " remnant=" + preset.RemnantRatio.ToString("0.###")
                + " arrival=" + preset.SlotArrivalDistance.ToString("0.###")
                + " orbitRadius=" + preset.ReserveOrbitRadius.ToString("0.###")
                + " orbitSpeed=" + preset.ReserveOrbitAngularSpeed.ToString("0.###")
                + " curve=" + preset.CurveRadiusMultiplier.ToString("0.###"));
        }
        finally
        {
            DestroyImmediate(window);
        }
    }

    private void OnEnable()
    {
        minSize = MinimumWindowSize;
        LoadSettings();
        LoadAiPresets();
        LoadSelectedAiMinimumData();
        LoadPartySimulationRuntimeData();
        EditorApplication.update += TickEditor;
        lastEditorTime = EditorApplication.timeSinceStartup;
        if (enemies.Count == 0)
            ResetSimulation();
        else
        {
            ResetSimulatedParty();
            ResetAllEnemyPartyTargetsToLeader();
            ApplyEnemyDataToExisting();
        }
    }

    private void OnDisable()
    {
        if (!suppressSettingsSave)
            SaveSettings();
        EditorApplication.update -= TickEditor;
    }

    private bool IsThreeMemberPartySimulation => partySimulationMode == PartySimulationMode.고정리더3인파티;
    private int SimulatedPartySize => IsThreeMemberPartySimulation ? SimulationTargetCount : 1;
    private float ActivePartyMoveSpeed => partyLeaderGait == ActorMovementGait.Walk
        ? partyWalkSpeed
        : partyRunSpeed;

    private string ResolvePartyModeDisplayName()
    {
        return IsThreeMemberPartySimulation ? "3인 파티" : "단일 리더";
    }

    private void LoadPartySimulationRuntimeData()
    {
        partyMovementProfile = AssetDatabase.LoadAssetAtPath<SimulationMovementProfile>(PartyMovementProfileAssetPath);
        GameObject playerActor = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerActorPrefabPath);
        PlayerMovement movement = playerActor != null ? playerActor.GetComponent<PlayerMovement>() : null;
        SimulationMovementProfile brain = partyMovementProfile;
        CombatTarget combatTarget = playerActor != null ? playerActor.GetComponent<CombatTarget>() : null;

        partyWalkSpeed = movement != null && movement.WalkMoveSpeed > 0.01f
            ? movement.WalkMoveSpeed
            : DefaultPartyWalkSpeed;
        partyRunSpeed = movement != null && movement.RunMoveSpeed > 0.01f
            ? movement.RunMoveSpeed
            : DefaultPartyRunSpeed;
        partyAcceleration = movement != null
            ? movement.MoveAcceleration
            : DefaultPartyAcceleration;
        partyDeceleration = movement != null
            ? movement.MoveDeceleration
            : DefaultPartyDeceleration;
        partyArrivalDistance = brain != null
            ? brain.FormationArrivalDistance
            : DefaultPartyArrivalDistance;
        partyCatchUpEnterDistance = brain != null
            ? brain.CatchUpEnterDistance
            : DefaultPartyCatchUpEnterDistance;
        partyCatchUpExitDistance = brain != null
            ? brain.CatchUpExitDistance
            : DefaultPartyCatchUpExitDistance;
        partyCatchUpSpeedMultiplier = brain != null
            ? brain.CatchUpSpeedMultiplier
            : DefaultPartyCatchUpSpeedMultiplier;
        partyTargetRadius = combatTarget != null
            ? combatTarget.CurrentVolume.Radius
            : partyMovementProfile != null
                ? partyMovementProfile.FallbackBodyRadius
                : DefaultPartyTargetRadius;
    }

    private static string SettingsKey => SettingsKeyPrefix + Hash128.Compute(Application.dataPath);

    private void LoadSettings()
    {
        SimulatorSettings settings = CreateDefaultSettings();
        string json = EditorPrefs.GetString(SettingsKey, string.Empty);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                JsonUtility.FromJsonOverwrite(json, settings);
            }
            catch (ArgumentException)
            {
                EditorPrefs.DeleteKey(SettingsKey);
            }
        }

        ApplySettings(settings);
        NormalizeSettings();
    }

    private void SaveSettings()
    {
        if (suppressSettingsSave) return;
        NormalizeSettings();
        EditorPrefs.SetString(SettingsKey, JsonUtility.ToJson(CaptureSettings()));
    }

    private void ApplyChangedSettings()
    {
        NormalizeSettings();
        SaveSettings();
        playerPosition.x = Mathf.Clamp(playerPosition.x, -mapHalfExtent + 2f, mapHalfExtent - 2f);
        playerPosition.z = Mathf.Clamp(playerPosition.z, -mapHalfExtent + 2f, mapHalfExtent - 2f);
        viewCenter.x = Mathf.Clamp(viewCenter.x, -mapHalfExtent, mapHalfExtent);
        viewCenter.z = Mathf.Clamp(viewCenter.z, -mapHalfExtent, mapHalfExtent);
        ApplyEnemyDataToExisting();
        if (encounterActive && pursuitFrameInitialized)
            RefreshFixedPointRoles(true); // 수치 변경 즉시 점과 경로 갱신
        Repaint();
    }

    private void RestoreDefaultSettings()
    {
        ApplySettings(CreateDefaultSettings());
        NormalizeSettings();
        LoadAiPresets();
        LoadSelectedAiMinimumData();
        EditorPrefs.DeleteKey(SettingsKey);
        SaveSettings();
        ResetSimulation();
    }

    private static SimulatorSettings CreateDefaultSettings()
    {
        return new SimulatorSettings
        {
            aiPresetId = ActiveAiPresetId,
            spawnPreset = (int)SpawnPreset.한쪽밀집,
            simulationDataMode = (int)SimulationDataMode.AI프리셋데이터,
            partySimulationMode = (int)PartySimulationMode.고정리더3인파티,
            partyLeaderGait = (int)ActorMovementGait.Run,
            requestedMonsterCount = 50,
            minimumSquadSize = EnemySquadPursuitPlanner.DefaultMinimumSquadSize,
            maximumSquadSize = EnemySquadPursuitPlanner.DefaultMaximumSquadSize,
            stableSeed = 20260715,
            slotRadius = EnemySquadPursuitPlanner.DefaultSlotRadius,
            directCommitRadius = EnemySquadPursuitPlanner.DefaultDirectCommitRadius,
            nearReleaseDistance = EnemySquadPursuitPlanner.DefaultNearReleaseDistance,
            farActivationDistance = EnemySquadPursuitPlanner.DefaultFarActivationDistance,
            reserveSpeedMultiplier = EnemySquadPursuitPlanner.DefaultReserveSpeedMultiplier,
            remnantRatio = EnemySquadPursuitPlanner.DefaultRemnantRatio,
            baseMonsterSpeed = DefaultBaseMonsterSpeed,
            monsterSeparationRadius = DefaultMonsterSeparationRadius,
            monsterSeparationStrength = DefaultMonsterSeparationStrength,
            slotArrivalDistance = DefaultSlotArrivalDistance,
            reserveOrbitRadius = EnemySquadPursuitPlanner.DefaultReserveOrbitRadius,
            reserveOrbitAngularSpeed = DefaultReserveOrbitAngularSpeed,
            aggroDetectionRange = DefaultAggroDetectionRange,
            supportCallRange = DefaultSupportCallRange,
            mapHalfExtent = DefaultMapHalfExtent,
            viewZoom = 1f,
            viewCenterX = 0f,
            viewCenterZ = 0f,
            baseAggroReleaseDistance = EnemyAIController.BaseCombatLoseTargetRange,
            aggroReleaseDelay = DefaultAggroReleaseDelay,
            curveRadiusMultiplier = 1.25f,
            spawnInterval = DefaultSpawnInterval,
            spawnRadius = DefaultSpawnRadius,
            spawnBatchCount = DefaultSpawnBatchCount,
            simulationSpeed = 1f,
            automaticKillRate = 2f,
            continuousSpawnerEnabled = true,
            spawnWithAggro = false,
            overridePrefabAggroRanges = false,
            automaticCombatDeaths = true,
            showRoutes = true,
            showTrails = true,
            showMonsterIds = false
        };
    }

    private SimulatorSettings CaptureSettings()
    {
        return new SimulatorSettings
        {
            themeTableGuid = selectedThemeTable != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedThemeTable)) : "",
            aiPresetId = selectedAiPreset != null ? selectedAiPreset.PresetId : selectedAiPresetId,
            spawnPreset = (int)spawnPreset,
            simulationDataMode = (int)simulationDataMode,
            partySimulationMode = (int)partySimulationMode,
            partyLeaderGait = (int)partyLeaderGait,
            requestedMonsterCount = requestedMonsterCount,
            minimumSquadSize = minimumSquadSize,
            maximumSquadSize = maximumSquadSize,
            stableSeed = stableSeed,
            slotRadius = slotRadius,
            directCommitRadius = directCommitRadius,
            nearReleaseDistance = nearReleaseDistance,
            farActivationDistance = farActivationDistance,
            reserveSpeedMultiplier = reserveSpeedMultiplier,
            remnantRatio = remnantRatio,
            baseMonsterSpeed = baseMonsterSpeed,
            monsterSeparationRadius = monsterSeparationRadius,
            monsterSeparationStrength = monsterSeparationStrength,
            slotArrivalDistance = slotArrivalDistance,
            reserveOrbitRadius = reserveOrbitRadius,
            reserveOrbitAngularSpeed = reserveOrbitAngularSpeed,
            aggroDetectionRange = aggroDetectionRange,
            supportCallRange = supportCallRange,
            mapHalfExtent = mapHalfExtent,
            viewZoom = viewZoom,
            viewCenterX = viewCenter.x,
            viewCenterZ = viewCenter.z,
            baseAggroReleaseDistance = baseAggroReleaseDistance,
            aggroReleaseDelay = aggroReleaseDelay,
            curveRadiusMultiplier = curveRadiusMultiplier,
            spawnInterval = spawnInterval,
            spawnRadius = spawnRadius,
            spawnBatchCount = spawnBatchCount,
            simulationSpeed = simulationSpeed,
            automaticKillRate = automaticKillRate,
            continuousSpawnerEnabled = continuousSpawnerEnabled,
            spawnWithAggro = spawnWithAggro,
            overridePrefabAggroRanges = overridePrefabAggroRanges,
            automaticCombatDeaths = automaticCombatDeaths,
            showRoutes = showRoutes,
            showTrails = showTrails,
            showMonsterIds = showMonsterIds
        };
    }

    private void ApplySettings(SimulatorSettings settings)
    {
        selectedThemeTable = string.IsNullOrEmpty(settings.themeTableGuid) ? null : AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(AssetDatabase.GUIDToAssetPath(settings.themeTableGuid));
        selectedAiPresetId = string.IsNullOrWhiteSpace(settings.aiPresetId)
            || string.Equals(settings.aiPresetId, "MurlocSquad", StringComparison.Ordinal)
            ? ActiveAiPresetId
            : settings.aiPresetId;
        spawnPreset = (SpawnPreset)settings.spawnPreset;
        simulationDataMode = (SimulationDataMode)settings.simulationDataMode;
        partySimulationMode = (PartySimulationMode)settings.partySimulationMode;
        partyLeaderGait = (ActorMovementGait)settings.partyLeaderGait;
        requestedMonsterCount = settings.requestedMonsterCount;
        minimumSquadSize = settings.minimumSquadSize;
        maximumSquadSize = settings.maximumSquadSize;
        stableSeed = settings.stableSeed;
        slotRadius = settings.slotRadius;
        directCommitRadius = settings.directCommitRadius;
        nearReleaseDistance = settings.nearReleaseDistance;
        farActivationDistance = settings.farActivationDistance;
        reserveSpeedMultiplier = settings.reserveSpeedMultiplier;
        remnantRatio = settings.remnantRatio;
        baseMonsterSpeed = settings.baseMonsterSpeed;
        monsterSeparationRadius = settings.monsterSeparationRadius;
        monsterSeparationStrength = settings.monsterSeparationStrength;
        slotArrivalDistance = settings.slotArrivalDistance;
        reserveOrbitRadius = settings.reserveOrbitRadius > 0.01f
            ? settings.reserveOrbitRadius
            : EnemySquadPursuitPlanner.ResolveLegacyReserveOrbitBaseRadius(slotRadius, farActivationDistance);
        reserveOrbitAngularSpeed = settings.reserveOrbitAngularSpeed;
        aggroDetectionRange = settings.aggroDetectionRange > 0.01f
            ? settings.aggroDetectionRange
            : DefaultAggroDetectionRange;
        supportCallRange = settings.supportCallRange > 0.01f
            ? settings.supportCallRange
            : DefaultSupportCallRange;
        mapHalfExtent = settings.mapHalfExtent > 0.01f
            ? settings.mapHalfExtent
            : DefaultMapHalfExtent;
        viewZoom = settings.viewZoom > 0.01f ? settings.viewZoom : 1f;
        viewCenter = new Vector3(settings.viewCenterX, 0f, settings.viewCenterZ);
        baseAggroReleaseDistance = settings.baseAggroReleaseDistance > 0.01f
            ? settings.baseAggroReleaseDistance
            : EnemyAIController.BaseCombatLoseTargetRange;
        aggroReleaseDelay = settings.aggroReleaseDelay > 0.01f
            ? settings.aggroReleaseDelay
            : DefaultAggroReleaseDelay;
        curveRadiusMultiplier = settings.curveRadiusMultiplier;
        spawnInterval = settings.spawnInterval;
        spawnRadius = settings.spawnRadius;
        spawnBatchCount = settings.spawnBatchCount;
        simulationSpeed = settings.simulationSpeed;
        automaticKillRate = settings.automaticKillRate;
        continuousSpawnerEnabled = settings.continuousSpawnerEnabled;
        spawnWithAggro = settings.spawnWithAggro;
        overridePrefabAggroRanges = settings.overridePrefabAggroRanges;
        automaticCombatDeaths = settings.automaticCombatDeaths;
        showRoutes = settings.showRoutes;
        showTrails = settings.showTrails;
        showMonsterIds = settings.showMonsterIds;
    }

    private void NormalizeSettings()
    {
        spawnPreset = (SpawnPreset)Mathf.Clamp((int)spawnPreset, 0, Enum.GetValues(typeof(SpawnPreset)).Length - 1);
        simulationDataMode = (SimulationDataMode)Mathf.Clamp(
            (int)simulationDataMode,
            0,
            Enum.GetValues(typeof(SimulationDataMode)).Length - 1);
        partySimulationMode = (PartySimulationMode)Mathf.Clamp(
            (int)partySimulationMode,
            0,
            Enum.GetValues(typeof(PartySimulationMode)).Length - 1);
        partyLeaderGait = (ActorMovementGait)Mathf.Clamp(
            (int)partyLeaderGait,
            0,
            Enum.GetValues(typeof(ActorMovementGait)).Length - 1);
        requestedMonsterCount = Mathf.Clamp(requestedMonsterCount, 0, 200);
        minimumSquadSize = Mathf.Max(1, minimumSquadSize);
        maximumSquadSize = Mathf.Max(minimumSquadSize, maximumSquadSize);
        remnantRatio = Mathf.Clamp(remnantRatio, 0.1f, 0.8f);
        slotRadius = Mathf.Clamp(slotRadius, 3.5f, 8f);
        directCommitRadius = Mathf.Clamp(directCommitRadius, slotRadius + 0.5f, 14f);
        nearReleaseDistance = Mathf.Clamp(nearReleaseDistance, 2.5f, Mathf.Max(2.5f, slotRadius - 0.25f));
        farActivationDistance = Mathf.Clamp(farActivationDistance, directCommitRadius + 1f, 18f);
        reserveSpeedMultiplier = Mathf.Clamp(reserveSpeedMultiplier, 0.4f, 1.2f);
        baseMonsterSpeed = Mathf.Clamp(baseMonsterSpeed, 0.5f, 6f);
        monsterSeparationRadius = Mathf.Clamp(monsterSeparationRadius, MonsterBodyRadius * 2f, 3f);
        monsterSeparationStrength = Mathf.Clamp(monsterSeparationStrength, 0f, 3f);
        slotArrivalDistance = Mathf.Clamp(slotArrivalDistance, 0.4f, 3f);
        reserveOrbitRadius = Mathf.Clamp(reserveOrbitRadius, farActivationDistance + 0.5f, 25f);
        reserveOrbitAngularSpeed = Mathf.Clamp(reserveOrbitAngularSpeed, 0f, 20f);
        aggroDetectionRange = Mathf.Clamp(aggroDetectionRange, 1f, 30f);
        supportCallRange = Mathf.Clamp(supportCallRange, 0f, 30f);
        mapHalfExtent = Mathf.Clamp(mapHalfExtent, 30f, 100f);
        viewZoom = Mathf.Clamp(viewZoom, MinimumViewZoom, MaximumViewZoom);
        viewCenter.x = Mathf.Clamp(viewCenter.x, -mapHalfExtent, mapHalfExtent);
        viewCenter.z = Mathf.Clamp(viewCenter.z, -mapHalfExtent, mapHalfExtent);
        baseAggroReleaseDistance = Mathf.Clamp(baseAggroReleaseDistance, 5f, 40f);
        aggroReleaseDelay = Mathf.Clamp(aggroReleaseDelay, 0.1f, 10f);
        curveRadiusMultiplier = Mathf.Clamp(curveRadiusMultiplier, 0.65f, 1.25f);
        spawnInterval = Mathf.Clamp(spawnInterval, 0.25f, 10f);
        spawnRadius = Mathf.Clamp(spawnRadius, 8f, mapHalfExtent - 2f);
        spawnBatchCount = Mathf.Clamp(spawnBatchCount, 1, 200);
        simulationSpeed = Mathf.Clamp(simulationSpeed, 0.25f, 4f);
        automaticKillRate = Mathf.Clamp(automaticKillRate, 0.25f, 10f);
    }

    private void LoadAiPresets()
    {
        aiPresets.Clear();
        string[] presetGuids = AssetDatabase.FindAssets("t:EnemyAiPreset");
        for (int index = 0; index < presetGuids.Length; index++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(presetGuids[index]);
            EnemyAiPreset preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(assetPath);
            if (preset != null
                && !string.Equals(
                    preset.PresetId,
                    "MurlocSquad",
                    StringComparison.Ordinal))
            {
                aiPresets.Add(preset);
            }
        }

        aiPresets.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal));
        selectedAiPresetIndex = 0;
        for (int index = 0; index < aiPresets.Count; index++)
        {
            if (!string.Equals(aiPresets[index].PresetId, selectedAiPresetId, StringComparison.Ordinal))
                continue;

            selectedAiPresetIndex = index;
            break;
        }

        selectedAiPreset = aiPresets.Count > 0 ? aiPresets[selectedAiPresetIndex] : null;
        if (selectedAiPreset != null)
            selectedAiPresetId = selectedAiPreset.PresetId;
    }

    private void EnsureMurlocAiPreset()
    {
        if (AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(MurlocAiPresetAssetPath) != null)
            return;

        const string presetFolder = "Assets/ProjectOverburst/Resources/Enemies/AiPresets";
        if (!AssetDatabase.IsValidFolder(presetFolder))
            AssetDatabase.CreateFolder("Assets/ProjectOverburst/Resources/Enemies", "AiPresets");

        GameObject[] prefabs = new GameObject[CoreMurlocPrefabPaths.Length];
        for (int index = 0; index < CoreMurlocPrefabPaths.Length; index++)
            prefabs[index] = AssetDatabase.LoadAssetAtPath<GameObject>(CoreMurlocPrefabPaths[index]);

        EnemyAiPreset preset = CreateInstance<EnemyAiPreset>();
        preset.ConfigureIdentity("MurlocSquad", "멀록 부대 AI", prefabs);
        preset.ConfigureSquadPursuit(
            EnemySquadPursuitPlanner.DefaultActivationCount,
            minimumSquadSize,
            maximumSquadSize,
            slotRadius,
            directCommitRadius,
            nearReleaseDistance,
            farActivationDistance,
            reserveSpeedMultiplier,
            remnantRatio,
            slotArrivalDistance,
            reserveOrbitRadius,
            reserveOrbitAngularSpeed,
            curveRadiusMultiplier);
        AssetDatabase.CreateAsset(preset, MurlocAiPresetAssetPath);
        AssetDatabase.SaveAssets();
    }

    public void ApplyThemeTable(EnemyThemeTable table)
    {
        if (table == null || !table.Validate(out _)) throw new InvalidOperationException("유효한 테마 테이블이 필요합니다.");
        selectedThemeTable = table;
        selectedAiPreset = table.Entries.First(e => e.tier == EnemyThemeTier.Small).definition.AiPreset;
        selectedAiPresetId = selectedAiPreset.PresetId;
        selectedAiPresetIndex = aiPresets.IndexOf(selectedAiPreset);
        requestedMonsterCount = 50; continuousSpawnerEnabled = false; spawnWithAggro = true;
        partySimulationMode = PartySimulationMode.단일리더;
        ApplySelectedAiPresetValues();
        AddEvent(table.DisplayName + " · 40/9/1 · 정예 독립");
    }

    [MenuItem("OVERBURST/Enemies/Themes/Validate Squad Simulator")]
    public static void ValidateThemeSimulator()
    {
        var window = CreateInstance<EnemySquadPursuitSimulatorWindow>();
        window.suppressSettingsSave = true;
        try
        {
            foreach (string id in new[] { "SpiderBrood", "VenomBrood", "PrimalHunt" })
            {
                var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(MonsterThemeCombatBuilder.Root + "/Tables/" + id + ".asset");
                window.ApplyThemeTable(table);
                if (window.enemies.Count != 50 || window.enemies.Count(e => e.SquadParticipant) != 49)
                    throw new InvalidOperationException("Simulator roster mismatch: " + id);
                if (window.enemies.Any(e => !e.SquadParticipant && e.SquadId >= 0))
                    throw new InvalidOperationException("Elite joined a squad");
                if (window.CountLivingSquads() != 6)
                    throw new InvalidOperationException("Expected six squads from 49 participants: " + id);
                var roster = table.BuildRoster(40,9,1,window.stableSeed);
                for (int i=0;i<roster.Count;i++)
                {
                    float radius = EnemyCrowdAgent.EstimateBodyRadius(roster[i].ActorPrefab.gameObject);
                    if (Mathf.Abs(window.enemies[i].BodyRadius-radius) > .001f)
                        throw new InvalidOperationException("Runtime body radius mismatch");
                }
                for (int frame=0;frame<120;frame++) window.StepSimulation(1f/60f);
                if (window.enemies.Any(e => float.IsNaN(e.Position.x) || float.IsNaN(e.Position.z)))
                    throw new InvalidOperationException("Invalid simulator position");
            }
            Debug.Log("[ThemeSquadSimulator] PASS 3 tables / 50 total / 49 participants / 1 independent / 6 squads / body radii / 120 steps each");
        }
        finally { DestroyImmediate(window); }
    }

    private void LoadSelectedAiMinimumData()
    {
        aiMinimumData.Clear();
        if (selectedThemeTable != null && selectedThemeTable.Entries.First(e => e.tier == EnemyThemeTier.Small).definition.AiPreset != selectedAiPreset)
            selectedThemeTable = null;
        List<EnemyDefinition> themeRoster = selectedThemeTable != null ? selectedThemeTable.BuildRoster(40, 9, 1, stableSeed) : null;
        int prefabCount = themeRoster != null ? themeRoster.Count : selectedAiPreset != null ? selectedAiPreset.DefaultMonsterCount : 0;
        for (int index = 0; index < prefabCount; index++)
        {
            GameObject prefab = themeRoster != null ? themeRoster[index].ActorPrefab.gameObject : selectedAiPreset.GetDefaultMonsterPrefab(index);
            if (prefab == null)
                continue;

            EnemyMovement movement = prefab.GetComponent<EnemyMovement>();
            if (movement == null)
                movement = prefab.GetComponentInChildren<EnemyMovement>(true);
            EnemyMovementProfile movementProfile = movement != null ? movement.Profile : null;
            EnemyAIController controller = prefab.GetComponent<EnemyAIController>();
            if (controller == null)
                controller = prefab.GetComponentInChildren<EnemyAIController>(true);
            EnemyBehaviorProfile behaviorProfile = controller != null ? controller.BehaviorProfile : null;
            float walkSpeed = movement != null ? movement.MoveSpeed : EnemyMovementProfile.MinimumMoveSpeed;
            float runMultiplier = movementProfile != null
                ? movementProfile.RunSpeedMultiplier
                : EnemyMovementProfile.DefaultRunSpeedMultiplier;
            string archetypeName = behaviorProfile != null ? behaviorProfile.ProfileId : prefab.name;
            if (archetypeName.StartsWith("Murloc_", StringComparison.Ordinal))
                archetypeName = archetypeName.Substring("Murloc_".Length);
            aiMinimumData.Add(new EnemyMinimumData
            {
                Name = archetypeName,
                SquadParticipant = themeRoster == null || themeRoster[index].SquadParticipationMode != EnemySquadParticipationMode.Independent,
                WalkSpeed = walkSpeed,
                RunSpeed = walkSpeed * runMultiplier,
                BodyRadius = EnemyCrowdAgent.EstimateBodyRadius(prefab),
                CrowdWeight = movementProfile != null ? movementProfile.CrowdWeight : 1f,
                SeparationRadius = behaviorProfile != null
                    ? behaviorProfile.SeparationRadius
                    : DefaultMonsterSeparationRadius,
                SeparationWeight = behaviorProfile != null ? behaviorProfile.SeparationWeight : 1f,
                DetectionRange = controller != null ? controller.DetectionRange : DefaultAggroDetectionRange,
                SupportCallRange = behaviorProfile != null
                    ? behaviorProfile.SupportCallRange
                    : DefaultSupportCallRange,
                AlertDuration = behaviorProfile != null ? behaviorProfile.AlertDuration : 0f,
                MemberEngageRange = behaviorProfile != null
                    ? behaviorProfile.MemberEngageRange
                    : EnemyBehaviorProfile.DefaultMemberEngageRange
            });
        }
    }

    private void ApplySelectedAiPresetValues()
    {
        if (selectedAiPreset == null)
            return;

        minimumSquadSize = selectedAiPreset.MinimumSquadSize;
        maximumSquadSize = selectedAiPreset.MaximumSquadSize;
        slotRadius = selectedAiPreset.SlotRadius;
        directCommitRadius = selectedAiPreset.DirectCommitRadius;
        nearReleaseDistance = selectedAiPreset.NearReleaseDistance;
        farActivationDistance = selectedAiPreset.FarActivationDistance;
        reserveSpeedMultiplier = selectedAiPreset.ReserveSpeedMultiplier;
        remnantRatio = selectedAiPreset.RemnantRatio;
        slotArrivalDistance = selectedAiPreset.SlotArrivalDistance;
        reserveOrbitRadius = selectedAiPreset.ReserveOrbitRadius;
        reserveOrbitAngularSpeed = selectedAiPreset.ReserveOrbitAngularSpeed;
        curveRadiusMultiplier = selectedAiPreset.CurveRadiusMultiplier;
        simulationDataMode = SimulationDataMode.AI프리셋데이터;
        LoadSelectedAiMinimumData();
        SaveSettings();
        ResetSimulation();
    }

    private void SaveCurrentValuesToSelectedAiPreset()
    {
        if (selectedAiPreset == null)
            return;

        Undo.RecordObject(selectedAiPreset, "멀록 AI 프리셋 튜닝값 저장");
        selectedAiPreset.ConfigureSquadPursuit(
            selectedAiPreset.ActivationCount,
            minimumSquadSize,
            maximumSquadSize,
            slotRadius,
            directCommitRadius,
            nearReleaseDistance,
            farActivationDistance,
            reserveSpeedMultiplier,
            remnantRatio,
            slotArrivalDistance,
            reserveOrbitRadius,
            reserveOrbitAngularSpeed,
            curveRadiusMultiplier);
        EditorUtility.SetDirty(selectedAiPreset);
        AssetDatabase.SaveAssets();
        SaveSettings();
    }

    private void ApplyEnemyDataToExisting()
    {
        for (int index = 0; index < enemies.Count; index++)
            ApplyEnemyData(enemies[index], Mathf.Max(0, enemies[index].Id - 1));
    }

    private void ApplyEnemyData(SimEnemy enemy, int stableIndex)
    {
        if (enemy == null)
            return;
        if (simulationDataMode == SimulationDataMode.AI프리셋데이터 && aiMinimumData.Count > 0)
        {
            EnemyMinimumData data = aiMinimumData[stableIndex % aiMinimumData.Count];
            enemy.ArchetypeName = data.Name;
            enemy.SquadParticipant = data.SquadParticipant;
            enemy.WalkSpeed = data.WalkSpeed;
            enemy.PursuitSpeed = data.RunSpeed;
            enemy.BodyRadius = data.BodyRadius;
            enemy.CrowdWeight = data.CrowdWeight;
            enemy.SeparationRadius = data.SeparationRadius;
            enemy.SeparationWeight = data.SeparationWeight;
            enemy.DetectionRange = overridePrefabAggroRanges ? aggroDetectionRange : data.DetectionRange;
            enemy.SupportCallRange = overridePrefabAggroRanges ? supportCallRange : data.SupportCallRange;
            enemy.AlertDuration = data.AlertDuration;
            enemy.MemberEngageRange = data.MemberEngageRange;
            return;
        }

        enemy.ArchetypeName = "Uniform";
        enemy.SquadParticipant = true;
        enemy.WalkSpeed = baseMonsterSpeed;
        enemy.PursuitSpeed = baseMonsterSpeed;
        enemy.BodyRadius = MonsterBodyRadius;
        enemy.CrowdWeight = 1f;
        enemy.SeparationRadius = monsterSeparationRadius;
        enemy.SeparationWeight = monsterSeparationStrength;
        enemy.DetectionRange = aggroDetectionRange;
        enemy.SupportCallRange = supportCallRange;
        enemy.AlertDuration = 0f;
        enemy.MemberEngageRange = EnemyBehaviorProfile.DefaultMemberEngageRange;
    }

    private void TickEditor()
    {
        double now = EditorApplication.timeSinceStartup;
        float editorDelta = Mathf.Clamp((float)(now - lastEditorTime), 0f, 0.05f);
        lastEditorTime = now;
        if (!isPlaying)
            return;

        simulationAccumulator += editorDelta * simulationSpeed;
        int safety = 0;
        while (simulationAccumulator >= FixedSimulationStep && safety++ < 8)
        {
            StepSimulation(FixedSimulationStep);
            simulationAccumulator -= FixedSimulationStep;
        }

        Repaint();
    }

    private void OnGUI()
    {
        EnsureStyles();
        EditorGUI.BeginChangeCheck();
        DrawHeader();
        DrawToolbar();

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawSettingsPanel();
            GUILayout.Space(PanelGap);
            Rect canvasRect = GUILayoutUtility.GetRect(
                MinimumCanvasWidth,
                10000f,
                460f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            DrawCanvas(canvasRect);
            HandleCanvasInput(canvasRect);
            GUILayout.Space(PanelGap);
            DrawStatusPanel();
        }
        if (EditorGUI.EndChangeCheck())
            ApplyChangedSettings();
    }

    private void EnsureStyles()
    {
        if (headerStyle != null)
            return;

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
        headerSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.78f, 0.82f, 0.87f, 1f) }
        };
        subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12
        };
        canvasLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        canvasSmallLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.9f, 0.92f, 0.95f, 0.95f) }
        };
        centeredCanvasLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = new Color(0.86f, 0.88f, 0.92f, 1f) }
        };
        statusStyle = new GUIStyle(EditorStyles.helpBox)
        {
            wordWrap = true,
            richText = true
        };
    }

    private void DrawHeader()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, 48f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.11f, 0.13f, 0.16f, 1f));
        GUI.Label(new Rect(rect.x + 14f, rect.y + 5f, rect.width - 28f, 24f), WindowTitle, headerStyle);
        string mode = encounterActive ? "대량 웨이브 부대 AI" : "기존 개별 AI";
        GUI.Label(
            new Rect(rect.x + 14f, rect.y + 27f, rect.width - 28f, 17f),
            "XZ 탑뷰 · " + mode + " · " + ResolvePartyModeDisplayName()
                + " · 고정 8점 역할 교대 · 균일/AI 프리셋 데이터 비교",
            headerSubtitleStyle);
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button(isPlaying ? "일시정지" : "재생", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                isPlaying = !isPlaying;
            if (GUILayout.Button("1프레임", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                isPlaying = false;
                StepSimulation(FixedSimulationStep);
                Repaint();
            }
            if (GUILayout.Button("시뮬 초기화", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                ResetSimulation();

            GUILayout.Space(10f);
            GUILayout.Label("속도", GUILayout.Width(28f));
            simulationSpeed = GUILayout.HorizontalSlider(simulationSpeed, 0.25f, 4f, GUILayout.Width(100f));
            GUILayout.Label(simulationSpeed.ToString("0.00") + "x", GUILayout.Width(42f));
            GUILayout.Space(8f);
            GUILayout.Label("확대", GUILayout.Width(28f));
            viewZoom = GUILayout.HorizontalSlider(viewZoom, MinimumViewZoom, MaximumViewZoom, GUILayout.Width(100f));
            GUILayout.Label(viewZoom.ToString("0.00") + "x", GUILayout.Width(42f));
            if (GUILayout.Button("화면 맞춤", EditorStyles.toolbarButton, GUILayout.Width(68f)))
            {
                viewZoom = 1f;
                viewCenter = Vector3.zero;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "시간 " + elapsedSimulationTime.ToString("0.0")
                + "s  |  생존 " + CountAliveEnemies()
                + "  |  부대 " + CountLivingSquads(),
                EditorStyles.miniLabel);
        }
    }

    private void DrawSettingsPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(LeftPanelWidth), GUILayout.ExpandHeight(true)))
        {
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            EditorGUILayout.LabelField("AI 프리셋", subHeaderStyle);
            string[] presetNames = new string[aiPresets.Count];
            for (int index = 0; index < aiPresets.Count; index++)
                presetNames[index] = aiPresets[index].DisplayName;
            EditorGUI.BeginDisabledGroup(aiPresets.Count == 0);
            int newPresetIndex = EditorGUILayout.Popup("AI 종류", selectedAiPresetIndex, presetNames);
            if (newPresetIndex != selectedAiPresetIndex && newPresetIndex >= 0 && newPresetIndex < aiPresets.Count)
            {
                selectedAiPresetIndex = newPresetIndex;
                selectedAiPreset = aiPresets[selectedAiPresetIndex];
                selectedAiPresetId = selectedAiPreset.PresetId;
                ApplySelectedAiPresetValues();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("프리셋 불러오기"))
                    ApplySelectedAiPresetValues();
                if (GUILayout.Button("현재값 저장"))
                    SaveCurrentValuesToSelectedAiPreset();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.LabelField("AI 프리셋·파티 경로 계산을 게임 런타임과 공유", EditorStyles.miniLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("테마 테이블 · 실제 40/9/1 편성", subHeaderStyle);
            var changedTheme = (EnemyThemeTable)EditorGUILayout.ObjectField("테마", selectedThemeTable, typeof(EnemyThemeTable), false);
            if (changedTheme != selectedThemeTable)
            {
                selectedThemeTable = changedTheme;
                if (selectedThemeTable != null) ApplyThemeTable(selectedThemeTable);
                else { LoadSelectedAiMinimumData(); ResetSimulation(); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("거미")) ApplyThemeTable(AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(MonsterThemeCombatBuilder.Root + "/Tables/SpiderBrood.asset"));
                if (GUILayout.Button("독낭")) ApplyThemeTable(AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(MonsterThemeCombatBuilder.Root + "/Tables/VenomBrood.asset"));
                if (GUILayout.Button("포식")) ApplyThemeTable(AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(MonsterThemeCombatBuilder.Root + "/Tables/PrimalHunt.asset"));
            }
            if (selectedThemeTable != null)
                EditorGUILayout.HelpBox("50마리 기준 소형 40 / 중형 9 / 정예 1. 분대 참여 49, 정예 독립 1(분홍 테두리). 이동·편성 비교용이며 공격/애니메이션은 실제 Play에서 확인합니다.", MessageType.Info);

            EditorGUILayout.LabelField("시나리오", subHeaderStyle);
            spawnPreset = (SpawnPreset)EditorGUILayout.EnumPopup("배치", spawnPreset);
            simulationDataMode = (SimulationDataMode)EditorGUILayout.EnumPopup("개체 데이터", simulationDataMode);
            PartySimulationMode previousPartyMode = partySimulationMode;
            partySimulationMode = (PartySimulationMode)EditorGUILayout.EnumPopup("파티 구성", partySimulationMode);
            if (partySimulationMode != previousPartyMode)
            {
                ResetSimulatedParty();
                ResetAllEnemyPartyTargetsToLeader();
                AddEvent(ResolvePartyModeDisplayName() + " 전환 · 리더 P1 고정");
            }
            if (IsThreeMemberPartySimulation)
            {
                ActorMovementGait previousGait = partyLeaderGait;
                partyLeaderGait = (ActorMovementGait)EditorGUILayout.EnumPopup("P1 이동", partyLeaderGait);
                if (partyLeaderGait != previousGait)
                    AddEvent("P1 " + partyLeaderGait + " · P2/P3 동기화");
            }
            if (simulationDataMode == SimulationDataMode.AI프리셋데이터)
            {
                EditorGUILayout.LabelField(
                    selectedAiPreset != null
                        ? selectedAiPreset.DisplayName + " · 로스터 " + aiMinimumData.Count + "종"
                        : "선택된 AI 프리셋 없음",
                    EditorStyles.miniLabel);
                if (GUILayout.Button("AI 개체 데이터 새로고침"))
                {
                    LoadSelectedAiMinimumData();
                    ApplyEnemyDataToExisting();
                }
            }
            requestedMonsterCount = EditorGUILayout.IntSlider("초기 몬스터", requestedMonsterCount, 0, 200);
            stableSeed = EditorGUILayout.IntField("고정 시드", stableSeed);
            if (GUILayout.Button("현재 설정으로 다시 생성", GUILayout.Height(26f)))
                ResetSimulation();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("연속 스포너", subHeaderStyle);
            continuousSpawnerEnabled = EditorGUILayout.Toggle("계속 스폰", continuousSpawnerEnabled);
            spawnInterval = EditorGUILayout.Slider("스폰 간격", spawnInterval, 0.25f, 10f);
            spawnBatchCount = Mathf.Max(1, EditorGUILayout.DelayedIntField("회당 몬스터", spawnBatchCount));
            spawnRadius = EditorGUILayout.Slider("스폰 반경", spawnRadius, 8f, mapHalfExtent - 2f);
            if (GUILayout.Button("지금 1회 스폰"))
                SpawnContinuousBatch();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("맵과 화면", subHeaderStyle);
            mapHalfExtent = EditorGUILayout.Slider("맵 반크기", mapHalfExtent, 30f, 100f);
            EditorGUILayout.LabelField("전체 맵 크기", (mapHalfExtent * 2f).ToString("0") + "m × " + (mapHalfExtent * 2f).ToString("0") + "m");
            EditorGUILayout.LabelField("휠 확대·축소 · 중클릭 드래그 화면 이동", EditorStyles.miniLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("부대 편성", subHeaderStyle);
            minimumSquadSize = Mathf.Max(1, EditorGUILayout.DelayedIntField("최소 부대", minimumSquadSize));
            maximumSquadSize = Mathf.Max(
                minimumSquadSize,
                EditorGUILayout.DelayedIntField("최대 부대", maximumSquadSize));
            remnantRatio = EditorGUILayout.Slider("약화 기준", remnantRatio, 0.1f, 0.8f);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("추격 슬롯", subHeaderStyle);
            slotRadius = EditorGUILayout.Slider("슬롯 원 반경", slotRadius, 3.5f, 8f);
            directCommitRadius = EditorGUILayout.Slider(
                "직선 진입 반경",
                directCommitRadius,
                slotRadius + 0.5f,
                14f);
            nearReleaseDistance = EditorGUILayout.Slider(
                "근거리 강제 전환",
                nearReleaseDistance,
                2.5f,
                Mathf.Max(2.5f, slotRadius - 0.25f));
            farActivationDistance = EditorGUILayout.Slider(
                "원거리 재진입",
                farActivationDistance,
                directCommitRadius + 1f,
                18f);
            slotArrivalDistance = EditorGUILayout.Slider("슬롯 도착 판정", slotArrivalDistance, 0.4f, 3f);
            curveRadiusMultiplier = EditorGUILayout.Slider("우회 곡률 배율", curveRadiusMultiplier, 0.65f, 1.25f);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("이동과 군집", subHeaderStyle);
            if (simulationDataMode == SimulationDataMode.균일테스트)
            {
                baseMonsterSpeed = EditorGUILayout.Slider("균일 추격 속도", baseMonsterSpeed, 0.5f, 6f);
                monsterSeparationRadius = EditorGUILayout.Slider(
                    "분리 감지 반경",
                    monsterSeparationRadius,
                    MonsterBodyRadius * 2f,
                    3f);
                monsterSeparationStrength = EditorGUILayout.Slider("분리 강도", monsterSeparationStrength, 0f, 3f);
            }
            else
            {
                EditorGUILayout.LabelField("추격 속도", "AI 로스터별 Run 속도");
                EditorGUILayout.LabelField("군집 수치", "AI 로스터별 반경·강도·CrowdWeight");
            }
            reserveSpeedMultiplier = EditorGUILayout.Slider("예비 속도 배율", reserveSpeedMultiplier, 0.4f, 1.2f);
            reserveOrbitRadius = EditorGUILayout.Slider(
                "예비 궤도 기준 반경",
                reserveOrbitRadius,
                farActivationDistance + 0.5f,
                25f);
            reserveOrbitAngularSpeed = EditorGUILayout.Slider("예비 궤도 속도", reserveOrbitAngularSpeed, 0f, 20f);
            if (GUILayout.Button("추격 프레임 수동 재설정"))
                RefreshDynamicPursuitFrame(true);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("어그로", subHeaderStyle);
            spawnWithAggro = EditorGUILayout.Toggle("스폰 즉시 어그로", spawnWithAggro);
            if (simulationDataMode == SimulationDataMode.AI프리셋데이터)
                overridePrefabAggroRanges = EditorGUILayout.Toggle("어그로 거리 수동 조절", overridePrefabAggroRanges);
            bool usesPrefabAggroRanges = simulationDataMode == SimulationDataMode.AI프리셋데이터
                && !overridePrefabAggroRanges;
            EditorGUI.BeginDisabledGroup(usesPrefabAggroRanges);
            aggroDetectionRange = EditorGUILayout.Slider("탐지 거리", aggroDetectionRange, 1f, 30f);
            supportCallRange = EditorGUILayout.Slider("지원 요청 거리", supportCallRange, 0f, 30f);
            EditorGUI.EndDisabledGroup();
            if (usesPrefabAggroRanges)
                EditorGUILayout.LabelField("현재 멀록 프리팹별 원본 거리 사용 중", EditorStyles.miniLabel);
            baseAggroReleaseDistance = EditorGUILayout.Slider(
                "어그로 해제 거리",
                baseAggroReleaseDistance,
                5f,
                50f);
            aggroReleaseDelay = EditorGUILayout.Slider("해제 유지시간", aggroReleaseDelay, 0.1f, 10f);
            if (GUILayout.Button("생존 몬스터 전체 어그로 재적용"))
                ApplyAggroToAllAliveEnemies();
            if (GUILayout.Button("생존 몬스터 전체 Roam 전환"))
                ApplyRoamToAllAliveEnemies();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("전투 이벤트", subHeaderStyle);
            automaticCombatDeaths = EditorGUILayout.Toggle("자동 처치", automaticCombatDeaths);
            automaticKillRate = EditorGUILayout.Slider("초당 처치", automaticKillRate, 0.25f, 10f);
            if (GUILayout.Button("무작위 1명 처치"))
                KillRandomAliveEnemy(false);

            EditorGUI.BeginDisabledGroup(selectedSquadId < 0);
            if (GUILayout.Button("선택 부대 전멸"))
                KillSelectedSquad();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("표시", subHeaderStyle);
            showRoutes = EditorGUILayout.Toggle("경로", showRoutes);
            showTrails = EditorGUILayout.Toggle("이동 궤적", showTrails);
            showMonsterIds = EditorGUILayout.Toggle("몬스터 ID", showMonsterIds);
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("조절값은 프로젝트별로 자동 저장됩니다.", EditorStyles.miniLabel);
            if (GUILayout.Button("모든 조절값 기본값 복원", GUILayout.Height(26f)))
                RestoreDefaultSettings();
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawStatusPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(RightPanelWidth), GUILayout.ExpandHeight(true)))
        {
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
            EditorGUILayout.LabelField("이동 갱신", subHeaderStyle);
            EditorGUILayout.LabelField("플레이어 속도", playerSpeed.ToString("0.00") + "m/s");
            EditorGUILayout.LabelField(
                "경로 갱신",
                (routeRefreshPending ? "분산 처리 중" : "대기")
                + " · 누적 " + routeRefreshCount + "부대");
            EditorGUILayout.LabelField(
                "전체 재편성 이동",
                HorizontalDistance(lastFullReformationPosition, playerPosition).ToString("0.0")
                + " / " + FullReformationMoveTrigger.ToString("0.0") + "m");
            EditorGUILayout.LabelField(
                "다음 전체 재편성",
                Mathf.Max(0f, nextFullReformationTime - elapsedSimulationTime).ToString("0.0") + "초");
            EditorGUILayout.LabelField("전체 재편성", fullReformationCount + "회");
            EditorGUI.BeginDisabledGroup(!encounterActive);
            if (GUILayout.Button("전체 재편성 즉시 실행"))
            {
                nextFullReformationTime = elapsedSimulationTime;
                TryRefreshFullReformation();
                RefreshDynamicPursuitFrame(false);
                AssignAvailableSlots();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.LabelField(
                "고정 규칙",
                "평행 추종 · 1m/15도 재생성 · 프레임당 2부대",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "부대 결속",
                "Pursuit·Reserve · 후열 따라잡기 최대 +12%",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("현재 상태", subHeaderStyle);
            EditorGUILayout.LabelField("AI 종류", selectedAiPreset != null ? selectedAiPreset.DisplayName : "없음");
            EditorGUILayout.LabelField("모드", encounterActive ? "Large Wave" : "Legacy");
            EditorGUILayout.LabelField("개체 데이터", simulationDataMode.ToString());
            EditorGUILayout.LabelField("파티", ResolvePartyModeDisplayName() + " · 리더 P1 고정");
            EditorGUILayout.LabelField(
                "타깃 단계",
                "LeaderApproach " + CountEnemiesInTargetPhase(EnemyPartyTargetPhase.LeaderApproach)
                    + " · MemberEngaged " + CountEnemiesInTargetPhase(EnemyPartyTargetPhase.MemberEngaged));
            if (IsThreeMemberPartySimulation)
            {
                EditorGUILayout.LabelField(
                    "근접 잠금",
                    "P1 " + CountMemberEngagedTargets(0)
                        + " · P2 " + CountMemberEngagedTargets(1)
                        + " · P3 " + CountMemberEngagedTargets(2));
                EditorGUILayout.LabelField(
                    "파티 이동 원본",
                    "PF_PlayerActor " + partyLeaderGait + " · 경로 추적 · CatchUp "
                        + partyCatchUpSpeedMultiplier.ToString("0.00") + "x",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.LabelField("맵", (mapHalfExtent * 2f).ToString("0") + "m × " + (mapHalfExtent * 2f).ToString("0") + "m");
            EditorGUILayout.LabelField("화면 확대", viewZoom.ToString("0.00") + "x");
            EditorGUILayout.LabelField("생존", CountAliveEnemies().ToString());
            EditorGUILayout.LabelField("Roam", CountRoamingEnemies().ToString());
            EditorGUILayout.LabelField("어그로", CountAggroEnemies().ToString());
            EditorGUILayout.LabelField("Return", CountReturningEnemies().ToString());
            EditorGUILayout.LabelField(
                "세션 처치",
                sessionMonsterDeathCount.ToString());
            EditorGUILayout.LabelField(
                "현재 해제 기준",
                ResolveCurrentAggroReleaseDistance().ToString("0.0") + "m · " + aggroReleaseDelay.ToString("0.0") + "초");
            EditorGUILayout.LabelField(
                "탐지·지원 거리",
                simulationDataMode == SimulationDataMode.AI프리셋데이터 && !overridePrefabAggroRanges
                    ? "멀록 프리팹별 원본"
                    : aggroDetectionRange.ToString("0.0") + "m · " + supportCallRange.ToString("0.0") + "m");
            EditorGUILayout.LabelField("부대", CountLivingSquads().ToString());
            EditorGUILayout.LabelField(
                "연속 스폰",
                continuousSpawnerEnabled
                    ? "ON · " + Mathf.Max(0f, spawnInterval - continuousSpawnAccumulator).ToString("0.0") + "초 후"
                    : "OFF");
            EditorGUILayout.LabelField("슬롯 사용", CountOccupiedSlots() + " / 8 · 후방 조건부");
            int directSlotIndex = FindSlotIndexByKind(EnemySquadPursuitSlotKind.Direct);
            SimSquad directOwner = FindSlotOwner(directSlotIndex);
            EditorGUILayout.LabelField("정면 기준", directOwner != null ? "S" + directOwner.Id : "없음");
            EditorGUILayout.LabelField(
                "정면 역할점",
                directSlotIndex >= 0 ? "P" + slots[directSlotIndex].PointIndex : "없음");
            EditorGUILayout.LabelField("예비 부대", CountSquadsInMode(SquadMode.Reserve).ToString());
            EditorGUILayout.LabelField("돌진 부대", CountSquadsInMode(SquadMode.Rush).ToString());
            EditorGUILayout.LabelField("근접 전투", CountSquadsInMode(SquadMode.NearCombat).ToString());

            if (simulationDataMode == SimulationDataMode.AI프리셋데이터)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("AI 로스터 원본 데이터", subHeaderStyle);
                if (aiMinimumData.Count == 0)
                    EditorGUILayout.LabelField("프리팹 데이터를 찾지 못했습니다.", EditorStyles.miniLabel);
                for (int dataIndex = 0; dataIndex < aiMinimumData.Count; dataIndex++)
                {
                    EnemyMinimumData data = aiMinimumData[dataIndex];
                    EditorGUILayout.LabelField(
                        data.Name,
                        "Walk " + data.WalkSpeed.ToString("0.00")
                        + " · Run " + data.RunSpeed.ToString("0.00")
                        + " · R " + data.BodyRadius.ToString("0.00"));
                    EditorGUILayout.LabelField(
                        string.Empty,
                        "Crowd " + data.CrowdWeight.ToString("0.00")
                        + " · Sep " + data.SeparationRadius.ToString("0.00")
                        + "/" + data.SeparationWeight.ToString("0.00"));
                    EditorGUILayout.LabelField(
                        string.Empty,
                        "Detect " + data.DetectionRange.ToString("0.0")
                        + " · Support " + data.SupportCallRange.ToString("0.0")
                        + " · Alert " + data.AlertDuration.ToString("0.00") + "s");
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("어그로 상태 색상", subHeaderStyle);
            DrawLegendLine(RoamColor, "Roam", "거리 탐지 대기");
            DrawLegendLine(ReturnColor, "Return", "Home 복귀");

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("추격 모드 색상", subHeaderStyle);
            DrawLegendLine(ResolveModeColor(SquadMode.Legacy), "Legacy", "기존 개별 이동");
            DrawLegendLine(ResolveModeColor(SquadMode.Pursuit), "Pursuit", "곡선 우회 → 직선 확정");
            DrawLegendLine(ResolveModeColor(SquadMode.Reserve), "Reserve", "외곽 대기 · 최단 승격");
            DrawLegendLine(ResolveModeColor(SquadMode.Rush), "Rush", "선두 도착 · 전원 직선 돌진");
            DrawLegendLine(ResolveModeColor(SquadMode.NearCombat), "Near", "근거리 단순 전투");
            DrawLegendLine(ResolveModeColor(SquadMode.Remnant), "Remnant", "기준미달 · 슬롯권 박탈 · Near 동일");
            EditorGUILayout.LabelField("채움=상태 · 슬롯 보유 테두리/경로=역할", EditorStyles.miniLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("슬롯 역할 고정 색상", subHeaderStyle);
            for (int priorityOrder = 0; priorityOrder < 8; priorityOrder++)
                DrawSlotRoleLegendLine(priorityOrder);
            DrawLegendLine(UnassignedSquadColor, "미배정", "예비·근접·약화");
            EditorGUILayout.LabelField("색은 P점이 아니라 현재 역할을 따라갑니다.", EditorStyles.miniLabel);

            SimSquad selectedSquad = FindSquad(selectedSquadId);
            if (selectedSquad != null)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("선택 부대", subHeaderStyle);
                EditorGUILayout.LabelField("ID", "S" + selectedSquad.Id);
                EditorGUILayout.LabelField(
                    "상태",
                    selectedSquad.DirectCommitted ? "Pursuit / 직선 확정" : selectedSquad.Mode.ToString());
                EditorGUILayout.LabelField("돌진 잠금", selectedSquad.HasRushed ? "ON" : "OFF");
                EditorGUILayout.LabelField(
                    "슬롯 자격",
                    selectedSquad.SlotEligibilityRevoked ? "영구 박탈" : "정상");
                EditorGUILayout.LabelField(
                    "참여/생존",
                    selectedSquad.ParticipantCount + " / " + selectedSquad.AliveCount + " / " + selectedSquad.InitialCount);
                EditorGUILayout.LabelField(
                    "슬롯",
                    selectedSquad.SlotIndex >= 0 && selectedSquad.SlotIndex < slots.Count
                        ? "P" + slots[selectedSquad.SlotIndex].PointIndex + " · "
                            + EnemySquadPursuitPlanner.ResolveSlotDisplayName(
                            slots[selectedSquad.SlotIndex].Kind)
                        : "없음");
                EditorGUI.BeginDisabledGroup(selectedSquad.AliveCount <= 0);
                if (GUILayout.Button("선택 부대 전멸"))
                    KillSelectedSquad();
                EditorGUI.EndDisabledGroup();
            }

            SimEnemy selectedEnemy = FindEnemy(selectedEnemyId);
            if (selectedEnemy != null)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("선택 몬스터", subHeaderStyle);
                EditorGUILayout.LabelField("ID", "M" + selectedEnemy.Id);
                EditorGUILayout.LabelField("부대", selectedEnemy.SquadId >= 0 ? "S" + selectedEnemy.SquadId : "비소속");
                EditorGUILayout.LabelField("종류", selectedEnemy.ArchetypeName);
                EditorGUILayout.LabelField("상태", ResolveAggroStateName(selectedEnemy));
                EditorGUILayout.LabelField(
                    "타깃 단계",
                    selectedEnemy.Alive && selectedEnemy.AggroActive
                        ? selectedEnemy.TargetPhase.ToString()
                        : EnemyPartyTargetPhase.None.ToString());
                EditorGUILayout.LabelField(
                    "개별 타깃",
                    selectedEnemy.Alive && selectedEnemy.AggroActive && selectedEnemy.TargetPartyIndex >= 0
                        ? "P" + (selectedEnemy.TargetPartyIndex + 1)
                        : "없음");
                EditorGUILayout.LabelField(
                    "탐지/지원",
                    selectedEnemy.DetectionRange.ToString("0.0")
                    + "m / " + selectedEnemy.SupportCallRange.ToString("0.0") + "m");
                EditorGUILayout.LabelField(
                    "근접 획득",
                    selectedEnemy.MemberEngageRange.ToString("0.00") + "m · Collider 표면");
                EditorGUILayout.LabelField(
                    "Alert 대기",
                    selectedEnemy.AlertRemaining > 0f
                        ? selectedEnemy.AlertRemaining.ToString("0.00") + " / "
                            + selectedEnemy.AlertDuration.ToString("0.00") + "초"
                        : "-");
                EditorGUILayout.LabelField(
                    "이탈 유지",
                    selectedEnemy.AggroActive && selectedEnemy.AggroOutsideTime > 0f
                        ? selectedEnemy.AggroOutsideTime.ToString("0.00") + " / " + aggroReleaseDelay.ToString("0.00") + "초"
                        : "-");
                EditorGUILayout.LabelField(
                    "이동",
                    "Walk " + selectedEnemy.WalkSpeed.ToString("0.00")
                    + " · Pursuit " + selectedEnemy.PursuitSpeed.ToString("0.00"));
                EditorGUILayout.LabelField("몸 반경", selectedEnemy.BodyRadius.ToString("0.00"));
                EditorGUILayout.LabelField("CrowdWeight", selectedEnemy.CrowdWeight.ToString("0.00"));
                EditorGUILayout.LabelField(
                    "Separation",
                    selectedEnemy.SeparationRadius.ToString("0.00")
                    + " / " + selectedEnemy.SeparationWeight.ToString("0.00"));
                EditorGUILayout.LabelField("이동 우선순위", ResolveMovePriorityName(selectedEnemy.MovePriority));
                EditorGUILayout.LabelField("생존", selectedEnemy.Alive ? "생존" : "사망");
                EditorGUI.BeginDisabledGroup(!selectedEnemy.Alive);
                if (GUILayout.Button("선택 몬스터 처치"))
                    KillEnemy(selectedEnemy);
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("최근 이벤트", subHeaderStyle);
            if (eventLines.Count == 0)
                EditorGUILayout.LabelField("이벤트 없음", EditorStyles.miniLabel);
            for (int i = 0; i < eventLines.Count; i++)
                EditorGUILayout.LabelField(eventLines[i], EditorStyles.miniLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "P1 리더만 드래그합니다. P2·P3은 인게임 경로 추종으로 자동 이동하며 리더 교체는 없습니다. 주황 Engage 영역은 선택 몬스터의 실제 Collider 표면 근접 잠금 범위를 P1/P2/P3에 표시합니다. Roam은 가장 가까운 파티원에게 탐지될 수 있지만 원거리 개체는 P1 LeaderApproach를 유지하고, 장애물 없는 Engage 영역에 들어온 개체만 해당 파티원을 MemberEngaged로 잠급니다. Rigidbody·실제 공격·애니메이션은 재현하지 않습니다.",
                statusStyle);
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawLegendLine(Color color, string label, string description)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            Rect swatch = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f), GUILayout.Height(12f));
            EditorGUI.DrawRect(swatch, color);
            GUILayout.Label(label, GUILayout.Width(58f));
            GUILayout.Label(description, EditorStyles.miniLabel);
        }
    }

    private void DrawSlotRoleLegendLine(int priorityOrder)
    {
        int roleIndex = EnemySquadPursuitPlanner.GetPreferredSlotIndex(priorityOrder);
        if (roleIndex < 0)
            return;

        EnemySquadPursuitSlotKind kind = (EnemySquadPursuitSlotKind)roleIndex;
        DrawLegendLine(
            ResolveSlotRoleColor(kind),
            (priorityOrder + 1) + "순위",
            EnemySquadPursuitPlanner.ResolveSlotDisplayName(kind)
                + (kind == EnemySquadPursuitSlotKind.Rear ? " · 후방 궤도 Reserve만" : string.Empty));
    }

    private void DrawCanvas(Rect rect)
    {
        EditorGUI.DrawRect(rect, CanvasBackground);
        GUI.BeginClip(rect);
        Rect canvasRect = new Rect(0f, 0f, rect.width, rect.height);
        DrawGrid(canvasRect);
        DrawMapBounds(canvasRect);
        DrawObstacles(canvasRect);

        Handles.BeginGUI();
        if (continuousSpawnerEnabled)
            DrawRangeCircle(canvasRect, playerPosition, spawnRadius, SpawnRangeColor, "Spawn " + spawnRadius.ToString("0.0"));
        DrawPartyAggroRanges(canvasRect);
        DrawPartyMemberEngagementRanges(canvasRect);
        DrawRangeCircle(canvasRect, playerPosition, nearReleaseDistance, NearRangeColor, "Near " + nearReleaseDistance.ToString("0.0"));
        DrawRangeCircle(canvasRect, playerPosition, directCommitRadius, CommitRangeColor, "Commit " + directCommitRadius.ToString("0.0"));
        DrawRangeCircle(canvasRect, playerPosition, farActivationDistance, FarRangeColor, "Far " + farActivationDistance.ToString("0.0"));
        DrawReserveOrbitCircles(canvasRect);
        DrawFullReformationMarker(canvasRect);

        SimEnemy selectedEnemy = FindEnemy(selectedEnemyId);
        if (selectedEnemy != null && selectedEnemy.Alive)
        {
            DrawRangeCircle(
                canvasRect,
                selectedEnemy.Position,
                selectedEnemy.SupportCallRange,
                SupportCallRangeColor,
                "Support " + selectedEnemy.SupportCallRange.ToString("0.0") + "m");
        }

        if (encounterActive && pursuitFrameInitialized)
        {
            DrawPursuitAxes(canvasRect);
            DrawSlots(canvasRect);
            if (showRoutes)
                DrawSquadRoutes(canvasRect);
            if (showTrails)
                DrawSquadTrails(canvasRect);
        }

        DrawEnemiesAndSquads(canvasRect);
        DrawParty(canvasRect);
        Handles.EndGUI();
        GUI.EndClip();

        if (enemies.Count == 0)
            GUI.Label(rect, "몬스터가 없습니다", centeredCanvasLabelStyle);
    }

    private void DrawGrid(Rect rect)
    {
        float scale = ResolveCanvasScale(rect);
        float minorStep = 2f * scale;
        float majorStep = 10f * scale;
        Vector2 origin = WorldToCanvas(rect, Vector3.zero);

        for (float x = rect.x + Mathf.Repeat(origin.x - rect.x, minorStep); x < rect.xMax; x += minorStep)
            EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), GridMinorColor);
        for (float y = rect.y + Mathf.Repeat(origin.y - rect.y, minorStep); y < rect.yMax; y += minorStep)
            EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1f), GridMinorColor);
        for (float x = rect.x + Mathf.Repeat(origin.x - rect.x, majorStep); x < rect.xMax; x += majorStep)
            EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), GridMajorColor);
        for (float y = rect.y + Mathf.Repeat(origin.y - rect.y, majorStep); y < rect.yMax; y += majorStep)
            EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1f), GridMajorColor);
    }

    private void DrawMapBounds(Rect rect)
    {
        Vector2 topLeft = WorldToCanvas(rect, new Vector3(-mapHalfExtent, 0f, mapHalfExtent));
        Vector2 bottomRight = WorldToCanvas(rect, new Vector3(mapHalfExtent, 0f, -mapHalfExtent));
        Rect bounds = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        Color color = new Color(0.88f, 0.9f, 0.94f, 0.72f);
        EditorGUI.DrawRect(new Rect(bounds.xMin, bounds.yMin, bounds.width, 2f), color);
        EditorGUI.DrawRect(new Rect(bounds.xMin, bounds.yMax - 2f, bounds.width, 2f), color);
        EditorGUI.DrawRect(new Rect(bounds.xMin, bounds.yMin, 2f, bounds.height), color);
        EditorGUI.DrawRect(new Rect(bounds.xMax - 2f, bounds.yMin, 2f, bounds.height), color);
    }

    private void DrawObstacles(Rect rect)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            Rect worldRect = obstacles[i].XzRect;
            Vector2 topLeft = WorldToCanvas(rect, new Vector3(worldRect.xMin, 0f, worldRect.yMax));
            Vector2 bottomRight = WorldToCanvas(rect, new Vector3(worldRect.xMax, 0f, worldRect.yMin));
            Rect canvasObstacle = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
            EditorGUI.DrawRect(canvasObstacle, ObstacleColor);
        }
    }

    private void DrawRangeCircle(Rect rect, Vector3 center, float radius, Color color, string label)
    {
        Vector2 canvasCenter = WorldToCanvas(rect, center);
        Handles.color = color;
        Handles.DrawWireDisc(canvasCenter, Vector3.forward, radius * ResolveCanvasScale(rect));
        Handles.Label(
            canvasCenter + new Vector2(0f, -radius * ResolveCanvasScale(rect) - 12f),
            label,
            canvasSmallLabelStyle);
    }

    private void DrawPartyAggroRanges(Rect rect)
    {
        float detectionRange = ResolveMaximumDetectionRange();
        float releaseDistance = ResolveCurrentAggroReleaseDistance();
        for (int memberIndex = 0; memberIndex < SimulatedPartySize; memberIndex++)
        {
            Vector3 memberPosition = ResolvePartyMemberPosition(memberIndex);
            string prefix = IsThreeMemberPartySimulation ? "P" + (memberIndex + 1) + " " : string.Empty;
            float alphaScale = memberIndex == 0 ? 1f : 0.48f;
            DrawRangeCircle(
                rect,
                memberPosition,
                detectionRange,
                WithAlpha(AggroDetectionRangeColor, AggroDetectionRangeColor.a * alphaScale),
                prefix + "Detect " + detectionRange.ToString("0.0") + "m");
            DrawRangeCircle(
                rect,
                memberPosition,
                releaseDistance,
                WithAlpha(AggroRangeColor, AggroRangeColor.a * alphaScale),
                prefix + "Aggro " + releaseDistance.ToString("0.0") + "m"
                    + (memberIndex == 0 ? " · " + sessionMonsterDeathCount + "킬" : string.Empty));
        }
    }

    private void DrawPartyMemberEngagementRanges(Rect rect)
    {
        SimEnemy referenceEnemy = FindEnemy(selectedEnemyId);
        bool usesSelectedEnemy = referenceEnemy != null && referenceEnemy.Alive;
        float enemyRadius = usesSelectedEnemy ? referenceEnemy.BodyRadius : MonsterBodyRadius;
        float engageRange = usesSelectedEnemy
            ? referenceEnemy.MemberEngageRange
            : EnemyBehaviorProfile.DefaultMemberEngageRange;
        float centerRadius = EnemyCombatCoordinator.ResolveMemberEngageCenterRadius(
            enemyRadius,
            partyTargetRadius,
            engageRange);
        float canvasRadius = centerRadius * ResolveCanvasScale(rect);

        for (int memberIndex = 0; memberIndex < SimulatedPartySize; memberIndex++)
        {
            Vector3 memberPosition = ResolvePartyMemberPosition(memberIndex);
            Vector2 canvasCenter = WorldToCanvas(rect, memberPosition);
            Handles.color = WithAlpha(MemberEngageRangeColor, 0.10f);
            Handles.DrawSolidDisc(canvasCenter, Vector3.forward, canvasRadius);
            Handles.color = MemberEngageRangeColor;
            Handles.DrawWireDisc(canvasCenter, Vector3.forward, canvasRadius);
            Handles.Label(
                canvasCenter + new Vector2(0f, -canvasRadius - 12f),
                "P" + (memberIndex + 1)
                    + " Engage " + engageRange.ToString("0.00") + "m"
                    + (usesSelectedEnemy ? " · M" + referenceEnemy.Id : " · Default"),
                canvasSmallLabelStyle);
        }
    }

    private void DrawReserveOrbitCircles(Rect rect) // 예비 부대가 실제 대기할 3개 원주를 항상 표시
    {
        for (int lane = 0; lane < EnemySquadPursuitPlanner.DefaultReserveRingCount; lane++)
        {
            float radius = EnemySquadPursuitPlanner.ResolveReserveOrbitRadius(reserveOrbitRadius, lane);
            Color color = ReserveOrbitRangeColor;
            color.a = Mathf.Lerp(0.72f, 0.38f, lane / (float)Mathf.Max(1, EnemySquadPursuitPlanner.DefaultReserveRingCount - 1));
            DrawRangeCircle(
                rect,
                playerPosition,
                radius,
                color,
                "Reserve " + (lane + 1) + " · " + radius.ToString("0.00") + "m");
        }
    }

    private void DrawFullReformationMarker(Rect rect)
    {
        if (!encounterActive)
            return;

        Vector2 center = WorldToCanvas(rect, lastFullReformationPosition);
        float markerRadius = Mathf.Max(5f, 0.35f * ResolveCanvasScale(rect));
        Color markerColor = new Color(1f, 0.72f, 0.16f, 0.95f);
        Handles.color = markerColor;
        Handles.DrawWireDisc(center, Vector3.forward, markerRadius);
        Handles.DrawLine(center - Vector2.right * markerRadius, center + Vector2.right * markerRadius);
        Handles.DrawLine(center - Vector2.up * markerRadius, center + Vector2.up * markerRadius);
        Handles.Label(center + new Vector2(markerRadius + 4f, -markerRadius - 4f), "전체 재편성 기준점", canvasSmallLabelStyle);
    }

    private void DrawPursuitAxes(Rect rect)
    {
        int directSlotIndex = FindSlotIndexByKind(EnemySquadPursuitSlotKind.Direct);
        Vector3 roleOutward = directSlotIndex >= 0 ? slots[directSlotIndex].OutwardDirection : directOutward;
        Vector2 origin = WorldToCanvas(rect, playerPosition);
        Vector2 direct = WorldToCanvas(rect, playerPosition + roleOutward * 4f);
        Vector3 right = Quaternion.AngleAxis(90f, Vector3.up) * roleOutward;
        Vector2 rightPoint = WorldToCanvas(rect, playerPosition + right * 3f);
        Vector2 leftPoint = WorldToCanvas(rect, playerPosition - right * 3f);
        Handles.color = ResolveSlotRoleColor(EnemySquadPursuitSlotKind.Direct);
        Handles.DrawAAPolyLine(3f, origin, direct);
        Handles.DrawAAPolyLine(2f, leftPoint, rightPoint);
        SimSquad directOwner = FindSlotOwner(directSlotIndex);
        string pointLabel = directSlotIndex >= 0 ? " → P" + slots[directSlotIndex].PointIndex : string.Empty;
        string directLabel = directOwner != null
            ? "정면축 S" + directOwner.Id + pointLabel
            : "정면축" + pointLabel;
        Handles.Label(direct + new Vector2(8f, -8f), directLabel, canvasSmallLabelStyle);
    }

    private void DrawSlots(Rect rect)
    {
        float scale = ResolveCanvasScale(rect);
        for (int i = 0; i < slots.Count; i++)
        {
            EnemySquadPursuitSlot slot = slots[i];
            Vector2 point = WorldToCanvas(rect, slot.Position);
            SimSquad owner = FindSlotOwner(i);
            bool walkable = IsSlotWalkable(i);
            bool assignable = IsSlotAssignable(i);
            Color roleColor = ResolveSlotRoleColor(slot.Kind);
            Handles.color = WithAlpha(
                roleColor,
                !walkable ? 0.3f : owner != null ? 1f : assignable ? 0.68f : 0.45f);
            Handles.DrawSolidDisc(point, Vector3.forward, Mathf.Clamp(0.3f * scale, 4f, 8f));
            Handles.color = !walkable ? BlockedSlotColor : assignable || owner != null ? roleColor : UnassignedSquadColor;
            Handles.DrawWireDisc(point, Vector3.forward, Mathf.Clamp(0.48f * scale, 7f, 11f));
            string ownerText = !walkable
                ? " · 막힘"
                : owner != null
                    ? " · S" + owner.Id
                    : !assignable ? " · 후방 궤도 예비만" : string.Empty;
            Handles.Label(
                point + new Vector2(0f, -18f),
                "P" + slot.PointIndex + " · "
                    + EnemySquadPursuitPlanner.ResolveSlotDisplayName(slot.Kind) + ownerText,
                canvasSmallLabelStyle);
        }
    }

    private void DrawSquadRoutes(Rect rect)
    {
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (squad.Mode == SquadMode.Reserve)
            {
                Handles.color = WithAlpha(ResolveSquadVisualColor(squad), 0.48f);
                Vector2 from = WorldToCanvas(rect, squad.SmoothedCenter);
                Vector2 next = WorldToCanvas(rect, ResolveSquadDestination(squad));
                Handles.DrawAAPolyLine(1.5f, from, next);
                Handles.DrawWireDisc(next, Vector3.forward, 3f);
                continue;
            }
            if (squad.Mode != SquadMode.Pursuit || squad.SlotIndex < 0)
                continue;

            Handles.color = WithAlpha(ResolveSquadVisualColor(squad), 0.82f);
            Vector2 previous = WorldToCanvas(rect, squad.SmoothedCenter);
            if (squad.DirectCommitted && squad.SlotIndex < slots.Count)
            {
                Vector2 committedTarget = WorldToCanvas(rect, slots[squad.SlotIndex].Position);
                Handles.DrawAAPolyLine(2.5f, previous, committedTarget);
                Handles.DrawSolidDisc(committedTarget, Vector3.forward, 3f);
                continue;
            }
            for (int waypointIndex = squad.RouteWaypointIndex;
                 waypointIndex < squad.Route.WaypointCount;
                 waypointIndex++)
            {
                Vector2 next = WorldToCanvas(rect, squad.Route.GetWaypoint(waypointIndex));
                Handles.DrawAAPolyLine(2.5f, previous, next);
                Handles.DrawSolidDisc(next, Vector3.forward, 3f);
                previous = next;
            }
        }
    }

    private void DrawSquadTrails(Rect rect)
    {
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (squad.Trail.Count < 2)
                continue;

            Vector3[] points = new Vector3[squad.Trail.Count];
            for (int pointIndex = 0; pointIndex < squad.Trail.Count; pointIndex++)
                points[pointIndex] = WorldToCanvas(rect, squad.Trail[pointIndex]);
            Handles.color = WithAlpha(ResolveSquadVisualColor(squad), 0.38f);
            Handles.DrawAAPolyLine(1.5f, points);
        }
    }

    private void DrawEnemiesAndSquads(Rect rect)
    {
        float scale = ResolveCanvasScale(rect);
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            Vector2 point = WorldToCanvas(rect, enemy.Position);
            float radius = Mathf.Clamp(enemy.BodyRadius * scale, 2f, 12f);
            SimSquad enemySquad = FindSquad(enemy.SquadId);
            Color color = enemy.Alive
                ? enemy.AggroActive
                    ? ResolveModeColor(enemySquad != null ? enemySquad.Mode : SquadMode.Legacy)
                    : enemy.Returning ? ReturnColor : RoamColor
                : DeadColor;
            Handles.color = color;
            if (enemy.Alive)
            {
                Handles.DrawSolidDisc(point, Vector3.forward, radius);
                if (!enemy.SquadParticipant)
                {
                    Handles.color = new Color(1f, .35f, .65f);
                    Handles.DrawWireDisc(point, Vector3.forward, radius + 3f);
                }
                if (enemySquad != null)
                {
                    Handles.color = WithAlpha(ResolveSquadVisualColor(enemySquad), 0.85f);
                    Handles.DrawWireDisc(point, Vector3.forward, radius + 1f);
                }
            }
            else
            {
                Handles.DrawAAPolyLine(2f, point + new Vector2(-radius, -radius), point + new Vector2(radius, radius));
                Handles.DrawAAPolyLine(2f, point + new Vector2(-radius, radius), point + new Vector2(radius, -radius));
            }

            if (enemy.Id == selectedEnemyId)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(point, Vector3.forward, radius + 4f);
                if (enemy.Alive && enemy.AggroActive && enemy.TargetPartyIndex >= 0)
                {
                    Vector2 targetPoint = WorldToCanvas(rect, ResolveEnemyPartyTargetPosition(enemy));
                    Handles.color = WithAlpha(ResolvePartyMemberColor(enemy.TargetPartyIndex), 0.82f);
                    Handles.DrawAAPolyLine(2f, point, targetPoint);
                    Handles.Label(
                        Vector2.Lerp(point, targetPoint, 0.5f),
                        ResolveTargetPhaseShortName(enemy.TargetPhase)
                            + " → P" + (enemy.TargetPartyIndex + 1),
                        canvasSmallLabelStyle);
                }
            }
            if (showMonsterIds)
                Handles.Label(
                    point + new Vector2(0f, -12f),
                    "M" + enemy.Id + (simulationDataMode == SimulationDataMode.AI프리셋데이터
                        ? " " + enemy.ArchetypeName
                        : string.Empty)
                        + (enemy.Alive ? " " + ResolveAggroStateName(enemy) : string.Empty)
                        + (enemy.Alive && enemy.AggroActive && enemy.TargetPartyIndex >= 0
                            ? " " + ResolveTargetPhaseShortName(enemy.TargetPhase)
                                + "→P" + (enemy.TargetPartyIndex + 1)
                            : string.Empty),
                    canvasSmallLabelStyle);
        }

        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (squad.AliveCount <= 0)
                continue;

            Vector2 center = WorldToCanvas(rect, squad.SmoothedCenter);
            Color color = ResolveSquadVisualColor(squad);
            Handles.color = ResolveModeColor(squad.Mode);
            Handles.DrawSolidDisc(center, Vector3.forward, 6f);
            Handles.color = WithAlpha(color, 0.9f);
            Handles.DrawWireDisc(center, Vector3.forward, 8f);
            if (squad.Mode == SquadMode.Reserve)
            {
                Vector3 facing = Flatten(playerPosition - squad.SmoothedCenter);
                if (facing.sqrMagnitude > 0.0001f)
                {
                    Vector2 facingPoint = WorldToCanvas(
                        rect,
                        squad.SmoothedCenter + facing.normalized * 0.9f);
                    Handles.color = Color.white;
                    Handles.DrawAAPolyLine(2f, center, facingPoint);
                }
            }
            if (squad.Id == selectedSquadId)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(center, Vector3.forward, 11f);
            }
            string slotText = squad.SlotIndex >= 0 && squad.SlotIndex < slots.Count
                ? " P" + slots[squad.SlotIndex].PointIndex
                : string.Empty;
            Handles.Label(
                center + new Vector2(0f, -19f),
                "S" + squad.Id + " " + (squad.DirectCommitted ? "C" : ResolveModeShortName(squad.Mode)) + slotText
                + " A" + squad.ParticipantCount + " · " + squad.AliveCount + "/" + squad.InitialCount,
                canvasLabelStyle);
        }
    }

    private void DrawParty(Rect rect)
    {
        if (IsThreeMemberPartySimulation)
        {
            for (int i = 0; i < simulatedPartyFollowers.Count; i++)
            {
                SimPartyFollower follower = simulatedPartyFollowers[i];
                if (showTrails)
                {
                    Vector2 followerPoint = WorldToCanvas(rect, follower.Position);
                    Vector2 targetPoint = WorldToCanvas(rect, follower.TargetPosition);
                    Handles.color = PartyTrailColor;
                    Handles.DrawAAPolyLine(1.5f, followerPoint, targetPoint);
                    Handles.DrawWireDisc(targetPoint, Vector3.forward, 3f);
                }

                DrawPartyMember(
                    rect,
                    follower.Position,
                    follower.FacingDirection,
                    ResolvePartyMemberColor(follower.MemberIndex),
                    "P" + (follower.MemberIndex + 1)
                        + " " + follower.Gait.ToString().ToUpperInvariant()
                        + (follower.CatchingUp ? " CATCH UP" : " FOLLOW"),
                    false);
            }
        }

        DrawPartyMember(
            rect,
            playerPosition,
            simulatedLeaderFacing,
            PlayerColor,
            IsThreeMemberPartySimulation
                ? "P1 LEADER " + partyLeaderGait.ToString().ToUpperInvariant()
                : "PLAYER P1",
            true);
    }

    private void DrawPartyMember(
        Rect rect,
        Vector3 position,
        Vector3 facingDirection,
        Color color,
        string label,
        bool isLeader)
    {
        Vector2 center = WorldToCanvas(rect, position);
        float radius = isLeader ? 9f : 8f;
        Handles.color = color;
        Handles.DrawSolidDisc(center, Vector3.forward, radius);
        Handles.color = isLeader ? Color.white : WithAlpha(color, 0.95f);
        Handles.DrawWireDisc(center, Vector3.forward, radius + 3f);

        Vector3 facing = Flatten(facingDirection);
        if (facing.sqrMagnitude > 0.0001f)
        {
            Vector2 facingPoint = WorldToCanvas(rect, position + facing.normalized * 1.15f);
            Handles.DrawAAPolyLine(2f, center, facingPoint);
        }

        Handles.Label(center + new Vector2(0f, 17f), label, canvasLabelStyle);
    }

    private static Color ResolvePartyMemberColor(int memberIndex)
    {
        switch (memberIndex)
        {
            case 1:
                return PartyFollowerTwoColor;
            case 2:
                return PartyFollowerThreeColor;
            default:
                return PlayerColor;
        }
    }

    private void HandleCanvasInput(Rect rect)
    {
        Event current = Event.current;
        if (!rect.Contains(current.mousePosition))
        {
            if (current.type == EventType.MouseUp)
            {
                draggingPlayer = false;
                if (draggingCanvas)
                    SaveSettings();
                draggingCanvas = false;
            }
            return;
        }

        if (current.type == EventType.ScrollWheel)
        {
            Vector3 cursorWorldBeforeZoom = CanvasToWorld(rect, current.mousePosition);
            viewZoom = Mathf.Clamp(
                viewZoom * Mathf.Exp(-current.delta.y * 0.1f),
                MinimumViewZoom,
                MaximumViewZoom);
            Vector3 cursorWorldAfterZoom = CanvasToWorld(rect, current.mousePosition);
            viewCenter += cursorWorldBeforeZoom - cursorWorldAfterZoom;
            viewCenter.x = Mathf.Clamp(viewCenter.x, -mapHalfExtent, mapHalfExtent);
            viewCenter.z = Mathf.Clamp(viewCenter.z, -mapHalfExtent, mapHalfExtent);
            SaveSettings();
            Repaint();
            current.Use();
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 2)
        {
            draggingCanvas = true;
            lastCanvasMousePosition = current.mousePosition;
            current.Use();
            return;
        }

        if (draggingCanvas && current.type == EventType.MouseDrag && current.button == 2)
        {
            Vector2 delta = current.mousePosition - lastCanvasMousePosition;
            float scale = Mathf.Max(0.0001f, ResolveCanvasScale(rect));
            viewCenter += new Vector3(-delta.x / scale, 0f, delta.y / scale);
            viewCenter.x = Mathf.Clamp(viewCenter.x, -mapHalfExtent, mapHalfExtent);
            viewCenter.z = Mathf.Clamp(viewCenter.z, -mapHalfExtent, mapHalfExtent);
            lastCanvasMousePosition = current.mousePosition;
            Repaint();
            current.Use();
            return;
        }

        Vector2 playerCanvas = WorldToCanvas(rect, playerPosition);
        if (current.type == EventType.MouseDown && current.button == 0)
        {
            if (Vector2.Distance(current.mousePosition, playerCanvas) <= 15f)
            {
                draggingPlayer = true;
                current.Use();
                return;
            }

            SimEnemy selected = FindEnemyAtCanvasPosition(rect, current.mousePosition, 9f);
            selectedEnemyId = selected != null ? selected.Id : -1;
            selectedSquadId = selected != null ? selected.SquadId : -1;
            Repaint();
            current.Use();
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 1)
        {
            SimEnemy selected = FindEnemyAtCanvasPosition(rect, current.mousePosition, 10f);
            if (selected != null && selected.Alive)
            {
                selectedEnemyId = selected.Id;
                selectedSquadId = selected.SquadId;
                KillEnemy(selected);
            }
            current.Use();
            return;
        }

        if (draggingPlayer && current.type == EventType.MouseDrag && current.button == 0)
        {
            Vector3 proposedPlayerPosition = CanvasToWorld(rect, current.mousePosition);
            proposedPlayerPosition.x = Mathf.Clamp(
                proposedPlayerPosition.x,
                -mapHalfExtent + 2f,
                mapHalfExtent - 2f);
            proposedPlayerPosition.z = Mathf.Clamp(
                proposedPlayerPosition.z,
                -mapHalfExtent + 2f,
                mapHalfExtent - 2f);
            if (IsWalkable(proposedPlayerPosition))
                playerPosition = proposedPlayerPosition;
            if (encounterActive)
                RefreshDynamicPursuitFrame(false); // 고정 8점을 평행 이동하고 확정 전 경로만 갱신
            Repaint();
            current.Use();
            return;
        }

        if (current.type == EventType.MouseUp)
        {
            draggingPlayer = false;
            if (draggingCanvas)
                SaveSettings();
            draggingCanvas = false;
        }
    }

    private void StepSimulation(float deltaTime)
    {
        elapsedSimulationTime += deltaTime;
        UpdatePlayerMotion(deltaTime);
        SimulateContinuousSpawner(deltaTime);
        RefreshAllEnemyPartyTargetPhases(); // 매 스텝 같은 공용 타깃 단계 판정
        UpdateAggroLifecycle(deltaTime);
        TryActivateEncounter();
        if (encounterActive)
        {
            RecruitUnassignedEnemies();
            RefreshSquadCenters(deltaTime);
            UpdateSquadModes(deltaTime);
            TryRefreshFullReformation();
            RefreshDynamicPursuitFrame(false);
            AssignAvailableSlots();
        }

        MoveEnemies(deltaTime);
        SimulateAutomaticCombatDeaths(deltaTime);
    }

    private void ResetSimulation()
    {
        if (selectedThemeTable != null) LoadSelectedAiMinimumData();
        random = new System.Random(stableSeed);
        enemies.Clear();
        squads.Clear();
        slots.Clear();
        squadSizes.Clear();
        unassignedBuffer.Clear();
        aggroDiscoveryBuffer.Clear();
        partyTargetCandidates.Clear();
        reserveSquadBuffer.Clear();
        assignableSlotBuffer.Clear();
        balancedSlotBuffer.Clear();
        vacantSlotBuffer.Clear();
        reserveCenterBuffer.Clear();
        vacantSlotPositionBuffer.Clear();
        squadIndexBySlotBuffer.Clear();
        obstacles.Clear();
        eventLines.Clear();
        playerPosition = Vector3.zero;
        simulatedLeaderFacing = Vector3.forward;
        simulatedLeaderMotionHoldRemaining = 0f;
        pursuitFramePlayerPosition = playerPosition;
        lastRouteTranslationPlayerPosition = playerPosition;
        lastPlayerSamplePosition = playerPosition;
        lastFullReformationPosition = playerPosition;
        directOutward = Vector3.right;
        encounterActive = false;
        pursuitFrameInitialized = false;
        draggingPlayer = false;
        draggingCanvas = false;
        selectedEnemyId = -1;
        selectedSquadId = -1;
        nextEnemyId = 1;
        nextSquadId = 1;
        elapsedSimulationTime = 0f;
        simulationAccumulator = 0f;
        automaticKillAccumulator = 0f;
        sessionMonsterDeathCount = 0;
        continuousSpawnAccumulator = 0f;
        nextRouteRefreshTime = 0f;
        lastFullReformationTime = 0f;
        nextFullReformationTime = FullReformationInterval;
        playerSpeed = 0f;
        routeRefreshCursor = 0;
        routeRefreshCount = 0;
        fullReformationCount = 0;
        routeRefreshPending = false;
        playerSampleInitialized = true;
        spawnDirectionCursor = 0;
        ResetSimulatedParty();

        BuildPresetObstacles();
        for (int i = 0; i < requestedMonsterCount; i++)
            enemies.Add(CreateEnemy(ResolveSpawnPosition(i, requestedMonsterCount)));
        AddEvent("몬스터 " + requestedMonsterCount + "마리 생성");
        TryActivateEncounter();
        Repaint();
    }

    private SimEnemy CreateEnemy(Vector3 position)
    {
        SimEnemy enemy = new SimEnemy
        {
            Id = nextEnemyId++,
            Position = position,
            HomePosition = position,
            Velocity = Vector3.zero,
            AggroActive = spawnWithAggro,
            Returning = false
        };
        ApplyEnemyData(enemy, Mathf.Max(0, enemy.Id - 1));
        enemy.AlertRemaining = spawnWithAggro ? enemy.AlertDuration : 0f;
        if (enemy.AggroActive)
            SetEnemyLeaderApproach(enemy);
        return enemy;
    }

    private Vector3 ResolveSpawnPosition(int index, int count)
    {
        float randomA = NextSignedFloat();
        float randomB = NextSignedFloat();
        switch (spawnPreset)
        {
            case SpawnPreset.부채꼴:
            {
                float ratio = count > 1 ? index / (float)(count - 1) : 0.5f;
                float angle = Mathf.Lerp(-55f, 55f, ratio) + randomA * 5f;
                float radius = 15f + NextFloat() * 8f;
                return Quaternion.AngleAxis(angle, Vector3.up) * Vector3.right * radius
                    + new Vector3(0f, 0f, randomB * 0.65f);
            }
            case SpawnPreset.원형포위:
            {
                float angle = index * (360f / Mathf.Max(1, count)) + randomA * 8f;
                float radius = 15f + NextFloat() * 6f;
                return Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius;
            }
            case SpawnPreset.긴대열:
                return new Vector3(11f + (index % 12) * 0.9f, 0f, (index / 12 - 3f) * 0.8f + randomB * 0.25f);
            case SpawnPreset.좁은통로:
                return new Vector3(13f + (index % 10) * 0.9f, 0f, -2.2f + (index / 10 % 6) * 0.88f + randomB * 0.16f);
            default:
            {
                float angle = randomA * 24f;
                float radius = 13f + NextFloat() * 9f;
                Vector3 centerBias = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.right * radius;
                return centerBias + new Vector3(randomB * 1.5f, 0f, NextSignedFloat() * 4.8f);
            }
        }
    }

    private void BuildPresetObstacles()
    {
        if (spawnPreset != SpawnPreset.좁은통로)
            return;

        obstacles.Add(new SimObstacle(Rect.MinMaxRect(-2f, 3.1f, 23f, 11f)));
        obstacles.Add(new SimObstacle(Rect.MinMaxRect(-2f, -11f, 23f, -3.1f)));
    }

    private void UpdateAggroLifecycle(float deltaTime) // Roam 탐지부터 Return 후 재탐지까지 실제 순환 재현
    {
        float releaseDistance = ResolveCurrentAggroReleaseDistance();
        float releaseDistanceSqr = releaseDistance * releaseDistance;
        int rejoinedCount = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (!enemy.Alive || !enemy.Returning || !HasEngagedGroupAnchor(enemy, releaseDistanceSqr))
                continue;
            if (ActivateAggro(enemy, 0f))
                rejoinedCount++;
        }

        int returnedHomeCount = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (enemy.Alive && enemy.AggroActive && enemy.AlertRemaining > 0f)
                enemy.AlertRemaining = Mathf.Max(0f, enemy.AlertRemaining - deltaTime);
            if (!enemy.Alive || !enemy.Returning)
                continue;
            if (HorizontalSqrDistance(enemy.Position, enemy.HomePosition)
                > ReturnArriveDistance * ReturnArriveDistance)
                continue;
            enemy.Position = enemy.HomePosition;
            enemy.Returning = false;
            enemy.SkipRoamDetectionOnce = false;
            enemy.AlertRemaining = 0f;
            ClearEnemyPartyTarget(enemy);
            enemy.Velocity = Vector3.zero;
            returnedHomeCount++;
        }

        aggroDiscoveryBuffer.Clear();
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (!enemy.Alive || enemy.AggroActive)
                continue;
            if (CanDiscoverPlayer(
                ResolveNearestPartyDistanceSqr(enemy.Position),
                enemy.DetectionRange))
            {
                aggroDiscoveryBuffer.Add(enemy);
            }
        }

        int discoveredCount = 0;
        int supportCount = 0;
        for (int discoveryIndex = 0; discoveryIndex < aggroDiscoveryBuffer.Count; discoveryIndex++)
        {
            SimEnemy caller = aggroDiscoveryBuffer[discoveryIndex];
            if (!ActivateAggro(caller, caller.AlertDuration))
                continue;
            discoveredCount++;

            float supportRangeSqr = caller.SupportCallRange * caller.SupportCallRange;
            for (int receiverIndex = 0; receiverIndex < enemies.Count; receiverIndex++)
            {
                SimEnemy receiver = enemies[receiverIndex];
                if (!receiver.Alive || receiver.AggroActive || receiver == caller)
                    continue;
                if (HorizontalSqrDistance(receiver.Position, caller.Position) > supportRangeSqr)
                    continue;
                if (ActivateAggro(receiver, 0f))
                    supportCount++;
            }
        }

        int releasedCount = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (!enemy.Alive || !enemy.AggroActive)
                continue;
            Vector3 assignedTargetPosition = ResolveEnemyPartyTargetPosition(enemy);
            float targetDistanceSqr = HorizontalSqrDistance(enemy.Position, assignedTargetPosition);
            if (targetDistanceSqr <= releaseDistanceSqr)
            {
                enemy.AggroOutsideTime = 0f;
                continue;
            }
            if (HasEngagedGroupAnchor(enemy, releaseDistanceSqr))
            {
                enemy.AggroOutsideTime = 0f;
                continue;
            }

            enemy.AggroOutsideTime += deltaTime;
            if (!ShouldReleaseAggro(
                targetDistanceSqr,
                releaseDistance,
                enemy.AggroOutsideTime,
                aggroReleaseDelay))
                continue;
            enemy.AggroActive = false;
            enemy.Returning = true;
            enemy.AlertRemaining = 0f;
            enemy.AggroOutsideTime = 0f;
            ClearEnemyPartyTarget(enemy);
            enemy.Velocity = Vector3.zero;
            releasedCount++;
        }

        if (returnedHomeCount > 0)
            AddEvent("Home 도착 · Roam " + returnedHomeCount + "명");
        if (rejoinedCount > 0)
            AddEvent("동료 전투 재합류 " + rejoinedCount + "명");
        if (discoveredCount > 0 || supportCount > 0)
            AddEvent((IsThreeMemberPartySimulation ? "파티" : "플레이어")
                + " 발견 " + discoveredCount + "명 · 지원 합류 " + supportCount + "명");
        if (releasedCount > 0)
            AddEvent("어그로 해제 " + releasedCount + "명 · Home 복귀");
    }

    private bool HasEngagedGroupAnchor(SimEnemy requester, float releaseDistanceSqr)
    {
        SimSquad squad = requester.SquadId >= 0 ? FindSquad(requester.SquadId) : null;
        if (squad != null)
        {
            for (int i = 0; i < squad.Members.Count; i++)
            {
                SimEnemy ally = squad.Members[i];
                if (IsEngagedGroupAnchor(
                    requester,
                    ally,
                    ResolveEnemyPartyTargetPosition(ally),
                    releaseDistanceSqr))
                    return true;
            }
            return false;
        }

        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy ally = enemies[i];
            if (IsEngagedGroupAnchor(
                requester,
                ally,
                ResolveEnemyPartyTargetPosition(ally),
                releaseDistanceSqr))
                return true;
        }
        return false;
    }

    private static bool IsEngagedGroupAnchor(
        SimEnemy requester,
        SimEnemy candidate,
        Vector3 targetPosition,
        float releaseDistanceSqr)
    {
        return candidate != null
            && candidate != requester
            && candidate.Alive
            && candidate.AggroActive
            && HorizontalSqrDistance(requester.Position, candidate.Position) <= releaseDistanceSqr
            && HorizontalSqrDistance(candidate.Position, targetPosition) <= releaseDistanceSqr;
    }

    internal static bool CanDiscoverPlayer(float targetDistanceSqr, float detectionRange)
    {
        float range = Mathf.Max(0f, detectionRange);
        return targetDistanceSqr <= range * range;
    }

    private bool ActivateAggro(SimEnemy enemy, float alertDuration)
    {
        if (enemy == null || !enemy.Alive || enemy.AggroActive)
            return false;
        enemy.AggroActive = true;
        enemy.Returning = false;
        enemy.SkipRoamDetectionOnce = false;
        enemy.AlertRemaining = Mathf.Max(0f, alertDuration);
        enemy.AggroOutsideTime = 0f;
        enemy.Velocity = Vector3.zero;
        SetEnemyLeaderApproach(enemy);
        return true;
    }

    private void ApplyAggroToAllAliveEnemies()
    {
        int appliedCount = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (!enemy.Alive)
                continue;
            bool wasAggroActive = enemy.AggroActive;
            enemy.AggroActive = true;
            enemy.Returning = false;
            enemy.SkipRoamDetectionOnce = false;
            enemy.AlertRemaining = enemy.AlertDuration;
            enemy.AggroOutsideTime = 0f;
            if (!wasAggroActive || enemy.TargetPhase == EnemyPartyTargetPhase.None)
                SetEnemyLeaderApproach(enemy);
            appliedCount++;
        }

        AddEvent("전체 어그로 재적용 · " + appliedCount + "명");
    }

    private void ApplyRoamToAllAliveEnemies()
    {
        int appliedCount = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (!enemy.Alive)
                continue;
            enemy.AggroActive = false;
            enemy.Returning = false;
            enemy.SkipRoamDetectionOnce = false;
            enemy.AlertRemaining = 0f;
            enemy.AggroOutsideTime = 0f;
            ClearEnemyPartyTarget(enemy);
            enemy.Velocity = Vector3.zero;
            appliedCount++;
        }

        AddEvent("전체 Roam 전환 · " + appliedCount + "명");
    }

    private void TryActivateEncounter()
    {
        int activationCount = selectedAiPreset != null
            ? selectedAiPreset.ActivationCount
            : EnemySquadPursuitPlanner.DefaultActivationCount;
        if (encounterActive || enemies.Count(e => e.Alive && e.AggroActive && e.SquadParticipant) < activationCount)
            return;

        encounterActive = true;
        BuildInitialSquads();
        InitializePursuitFrame();
        AssignAvailableSlots();
        pursuitFramePlayerPosition = playerPosition;
        lastRouteTranslationPlayerPosition = playerPosition;
        lastFullReformationPosition = playerPosition;
        lastFullReformationTime = elapsedSimulationTime;
        nextFullReformationTime = elapsedSimulationTime + FullReformationInterval;
        AddEvent("Large Wave 활성 · " + CountLivingSquads() + "개 부대");
    }

    private void BuildInitialSquads()
    {
        unassignedBuffer.Clear();
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (enemy.Alive && enemy.AggroActive && enemy.SquadParticipant && enemy.SquadId < 0)
                unassignedBuffer.Add(enemy);
        }

        EnemySquadPursuitPlanner.BuildMaximumFirstSquadSizes(
            unassignedBuffer.Count,
            minimumSquadSize,
            maximumSquadSize,
            squadSizes);
        for (int sizeIndex = 0; sizeIndex < squadSizes.Count; sizeIndex++)
            CreateNearestSquad(squadSizes[sizeIndex]);
    }

    private void CreateNearestSquad(int targetSize)
    {
        if (unassignedBuffer.Count <= 0 || targetSize <= 0)
            return;

        int seedIndex = 0;
        float nearestPlayerDistance = float.PositiveInfinity;
        for (int i = 0; i < unassignedBuffer.Count; i++)
        {
            float distance = HorizontalSqrDistance(unassignedBuffer[i].Position, playerPosition);
            if (distance < nearestPlayerDistance)
            {
                nearestPlayerDistance = distance;
                seedIndex = i;
            }
        }

        SimEnemy seed = unassignedBuffer[seedIndex];
        unassignedBuffer.RemoveAt(seedIndex);
        unassignedBuffer.Sort((left, right) => HorizontalSqrDistance(left.Position, seed.Position)
            .CompareTo(HorizontalSqrDistance(right.Position, seed.Position)));

        SimSquad squad = new SimSquad { Id = nextSquadId++ };
        squad.Members.Add(seed);
        int additionalCount = Mathf.Min(targetSize - 1, unassignedBuffer.Count);
        for (int i = 0; i < additionalCount; i++)
            squad.Members.Add(unassignedBuffer[i]);
        if (additionalCount > 0)
            unassignedBuffer.RemoveRange(0, additionalCount);

        squad.InitialCount = squad.Members.Count;
        squad.RawCenter = CalculateSquadCenter(squad);
        squad.SmoothedCenter = squad.RawCenter;
        squad.Mode = HorizontalDistance(squad.SmoothedCenter, playerPosition) >= farActivationDistance
            ? SquadMode.Reserve
            : SquadMode.NearCombat;
        squad.Trail.Add(squad.RawCenter);
        for (int i = 0; i < squad.Members.Count; i++)
        {
            SimEnemy member = squad.Members[i];
            member.SquadId = squad.Id;
            member.LocalOffset = member.Position - squad.RawCenter;
        }
        squads.Add(squad);
    }

    private void InitializePursuitFrame()
    {
        RefreshDynamicPursuitFrame(true);
    }

    private void UpdatePlayerMotion(float deltaTime)
    {
        if (!playerSampleInitialized)
        {
            lastPlayerSamplePosition = playerPosition;
            playerSampleInitialized = true;
            playerSpeed = 0f;
            ResetSimulatedParty();
            return;
        }

        Vector3 leaderDisplacement = Flatten(playerPosition - lastPlayerSamplePosition);
        playerSpeed = leaderDisplacement.magnitude
            / Mathf.Max(0.0001f, deltaTime);
        if (leaderDisplacement.sqrMagnitude > 0.0001f)
        {
            simulatedLeaderFacing = leaderDisplacement.normalized;
            simulatedLeaderMotionHoldRemaining = partyMovementProfile != null
                ? partyMovementProfile.LeaderMotionHoldDuration
                : 0.12f;
        }
        else
        {
            simulatedLeaderMotionHoldRemaining = Mathf.Max(
                0f,
                simulatedLeaderMotionHoldRemaining - Mathf.Max(0f, deltaTime));
        }
        simulatedLeaderTrail.Record(
            playerPosition,
            partyMovementProfile != null ? partyMovementProfile.PathSampleDistance : 0.4f,
            partyMovementProfile != null ? partyMovementProfile.FollowTrailRetentionDistance : 4f);
        UpdateSimulatedPartyFollowers(deltaTime);
        lastPlayerSamplePosition = playerPosition;
    }

    private void ResetSimulatedParty()
    {
        simulatedLeaderTrail.Reset(playerPosition);
        simulatedPartyFollowers.Clear();
        if (!IsThreeMemberPartySimulation)
            return;

        Vector3 sourcePosition = playerPosition;
        SimulationFollowTrail sourceTrail = simulatedLeaderTrail;
        for (int followerSlot = 0; followerSlot < SimulationTargetCount - 1; followerSlot++)
        {
            ResolveSimulatedPartyTarget(
                followerSlot,
                sourceTrail,
                sourcePosition,
                true,
                out Vector3 targetPosition,
                out Vector3 pathForward);
            SimPartyFollower follower = new SimPartyFollower
            {
                MemberIndex = followerSlot + 1,
                Position = targetPosition,
                TargetPosition = targetPosition,
                FacingDirection = pathForward,
                Gait = partyLeaderGait,
                Velocity = Vector3.zero
            };
            follower.Trail.Reset(targetPosition);
            simulatedPartyFollowers.Add(follower);
            sourcePosition = targetPosition;
            sourceTrail = follower.Trail;
        }
    }

    private void UpdateSimulatedPartyFollowers(float deltaTime)
    {
        if (!IsThreeMemberPartySimulation || simulatedPartyFollowers.Count != SimulationTargetCount - 1)
        {
            if (IsThreeMemberPartySimulation)
                ResetSimulatedParty();
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        Vector3 sourcePosition = playerPosition;
        SimulationFollowTrail sourceTrail = simulatedLeaderTrail;
        for (int followerSlot = 0; followerSlot < simulatedPartyFollowers.Count; followerSlot++)
        {
            SimPartyFollower follower = simulatedPartyFollowers[followerSlot];
            follower.Gait = partyLeaderGait;
            ResolveSimulatedPartyTarget(
                followerSlot,
                sourceTrail,
                sourcePosition,
                false,
                out Vector3 targetPosition,
                out Vector3 pathForward);
            follower.TargetPosition = targetPosition;

            Vector3 toTarget = Flatten(targetPosition - follower.Position);
            float targetDistance = toTarget.magnitude;
            if (follower.CatchingUp)
                follower.CatchingUp = targetDistance > partyCatchUpExitDistance;
            else if (targetDistance >= partyCatchUpEnterDistance)
                follower.CatchingUp = true;

            bool shouldMove = targetDistance > partyArrivalDistance;
            Vector3 desiredDirection = shouldMove && targetDistance > 0.0001f
                ? toTarget / targetDistance
                : Vector3.zero;
            float speedMultiplier = follower.CatchingUp ? partyCatchUpSpeedMultiplier : 1f;
            Vector3 targetVelocity = desiredDirection * ActivePartyMoveSpeed * speedMultiplier;
            float controlRate = shouldMove ? partyAcceleration : partyDeceleration;
            follower.Velocity = Vector3.MoveTowards(
                follower.Velocity,
                targetVelocity,
                controlRate * safeDeltaTime);

            Vector3 proposedPosition = follower.Position + follower.Velocity * safeDeltaTime;
            proposedPosition = ResolveObstacleMovement(follower.Position, proposedPosition);
            proposedPosition.x = Mathf.Clamp(proposedPosition.x, -mapHalfExtent + 1f, mapHalfExtent - 1f);
            proposedPosition.z = Mathf.Clamp(proposedPosition.z, -mapHalfExtent + 1f, mapHalfExtent - 1f);
            Vector3 actualMovement = Flatten(proposedPosition - follower.Position);
            follower.Position = proposedPosition;
            if (actualMovement.sqrMagnitude > 0.000001f)
                follower.FacingDirection = actualMovement.normalized;
            else if (pathForward.sqrMagnitude > 0.0001f)
                follower.FacingDirection = pathForward.normalized;

            follower.Trail.Record(
                follower.Position,
                partyMovementProfile != null ? partyMovementProfile.PathSampleDistance : 0.4f,
                partyMovementProfile != null ? partyMovementProfile.FollowTrailRetentionDistance : 4f);
            sourcePosition = follower.Position;
            sourceTrail = follower.Trail;
        }
    }

    private void ResolveSimulatedPartyTarget(
        int followerSlot,
        SimulationFollowTrail sourceTrail,
        Vector3 sourcePosition,
        bool forceMovingChain,
        out Vector3 targetPosition,
        out Vector3 pathForward)
    {
        if (!forceMovingChain && simulatedLeaderMotionHoldRemaining <= 0f)
        {
            Vector3 forward = simulatedLeaderFacing;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Quaternion anchorRotation = Quaternion.LookRotation(forward, Vector3.up);
            targetPosition = playerPosition
                + anchorRotation * simulatedPartyFormation.GetStoppedFollowerOffset(followerSlot);
            pathForward = forward;
            return;
        }

        Vector3 authoredOffset = simulatedPartyFormation.GetFollowerOffset(followerSlot);
        SimulationChainFormationSolver.ResolveFollowPose(
            sourceTrail,
            sourcePosition,
            simulatedLeaderFacing,
            ResolveSimulatedFollowSpacing(followerSlot),
            authoredOffset.y,
            out targetPosition,
            out pathForward);
    }

    private float ResolveSimulatedFollowSpacing(int followerSlot)
    {
        float radius = Mathf.Max(0.01f, partyTargetRadius);
        if (followerSlot == 0)
        {
            float authoredDistance = Mathf.Abs(simulatedPartyFormation.GetFollowerOffset(0).z);
            float clearance = partyMovementProfile != null ? partyMovementProfile.Clearance : 0.15f;
            float escapePadding = partyMovementProfile != null ? partyMovementProfile.EscapePadding : 0.15f;
            return Mathf.Max(authoredDistance, radius * 2f + clearance + escapePadding);
        }

        float companionClearance = partyMovementProfile != null
            ? partyMovementProfile.CompanionClearance
            : 0.25f;
        return radius * 2f + companionClearance;
    }

    private Vector3 ResolvePartyMemberPosition(int memberIndex)
    {
        if (memberIndex <= 0 || !IsThreeMemberPartySimulation)
            return playerPosition;

        for (int i = 0; i < simulatedPartyFollowers.Count; i++)
        {
            SimPartyFollower follower = simulatedPartyFollowers[i];
            if (follower.MemberIndex == memberIndex)
                return follower.Position;
        }

        return playerPosition;
    }

    private Vector3 ResolveEnemyPartyTargetPosition(SimEnemy enemy)
    {
        int targetIndex = enemy != null ? enemy.TargetPartyIndex : -1;
        return enemy != null
            && enemy.TargetPhase != EnemyPartyTargetPhase.None
            && targetIndex >= 0
            && targetIndex < SimulatedPartySize
            ? ResolvePartyMemberPosition(targetIndex)
            : playerPosition;
    }

    private float ResolveNearestPartyDistanceSqr(Vector3 position)
    {
        float bestDistanceSqr = float.PositiveInfinity;
        for (int memberIndex = 0; memberIndex < SimulatedPartySize; memberIndex++)
        {
            bestDistanceSqr = Mathf.Min(
                bestDistanceSqr,
                HorizontalSqrDistance(position, ResolvePartyMemberPosition(memberIndex)));
        }

        return bestDistanceSqr;
    }

    private void SetEnemyLeaderApproach(SimEnemy enemy)
    {
        if (enemy == null || !enemy.Alive || !enemy.AggroActive)
        {
            ClearEnemyPartyTarget(enemy);
            return;
        }

        enemy.TargetPhase = EnemyPartyTargetPhase.LeaderApproach;
        enemy.TargetPartyIndex = 0; // 시뮬레이터 리더는 항상 P1
    }

    private static void ClearEnemyPartyTarget(SimEnemy enemy)
    {
        if (enemy == null)
            return;

        enemy.TargetPhase = EnemyPartyTargetPhase.None;
        enemy.TargetPartyIndex = -1;
    }

    private void RefreshAllEnemyPartyTargetPhases()
    {
        for (int i = 0; i < enemies.Count; i++)
            RefreshEnemyPartyTargetPhase(enemies[i]);
    }

    private void RefreshEnemyPartyTargetPhase(SimEnemy enemy)
    {
        if (enemy == null || !enemy.Alive || !enemy.AggroActive)
        {
            ClearEnemyPartyTarget(enemy);
            return;
        }

        partyTargetCandidates.Clear();
        float engageRange = Mathf.Max(0f, enemy.MemberEngageRange);
        for (int memberIndex = 0; memberIndex < SimulatedPartySize; memberIndex++)
        {
            Vector3 memberPosition = ResolvePartyMemberPosition(memberIndex);
            float surfaceDistance = EnemyCombatCoordinator.ResolveHorizontalSurfaceDistance(
                enemy.Position,
                enemy.BodyRadius,
                memberPosition,
                partyTargetRadius);
            bool canEngage = EnemyCombatCoordinator.IsInsideMemberEngageRange(
                    enemy.Position,
                    enemy.BodyRadius,
                    memberPosition,
                    partyTargetRadius,
                    engageRange)
                && IsPartyApproachClear(enemy.Position, memberPosition, enemy.BodyRadius);
            partyTargetCandidates.Add(new EnemyPartyTargetCandidate(
                memberIndex,
                true,
                canEngage,
                false,
                surfaceDistance));
        }

        EnemyPartyTargetDecision current = new EnemyPartyTargetDecision(
            enemy.TargetPhase,
            enemy.TargetPartyIndex);
        EnemyPartyTargetDecision resolved = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            current,
            0,
            partyTargetCandidates);
        enemy.TargetPhase = resolved.Phase;
        enemy.TargetPartyIndex = resolved.TargetMemberIndex;
    }

    private bool IsPartyApproachClear(
        Vector3 sourcePosition,
        Vector3 targetPosition,
        float clearanceRadius)
    {
        float padding = Mathf.Max(0f, clearanceRadius);
        for (int i = 0; i < obstacles.Count; i++)
        {
            Rect blocked = obstacles[i].XzRect;
            blocked.xMin -= padding;
            blocked.xMax += padding;
            blocked.yMin -= padding;
            blocked.yMax += padding;
            if (DoesSegmentIntersectRect(sourcePosition, targetPosition, blocked))
                return false;
        }

        return true;
    }

    private static bool DoesSegmentIntersectRect(Vector3 from, Vector3 to, Rect rect)
    {
        Vector2 origin = new Vector2(from.x, from.z);
        Vector2 delta = new Vector2(to.x - from.x, to.z - from.z);
        float minimumTime = 0f;
        float maximumTime = 1f;
        return IntersectsAxis(origin.x, delta.x, rect.xMin, rect.xMax, ref minimumTime, ref maximumTime)
            && IntersectsAxis(origin.y, delta.y, rect.yMin, rect.yMax, ref minimumTime, ref maximumTime);
    }

    private static bool IntersectsAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float minimumTime,
        ref float maximumTime)
    {
        if (Mathf.Abs(direction) <= 0.000001f)
            return origin >= minimum && origin <= maximum;

        float first = (minimum - origin) / direction;
        float second = (maximum - origin) / direction;
        if (first > second)
        {
            float swap = first;
            first = second;
            second = swap;
        }

        minimumTime = Mathf.Max(minimumTime, first);
        maximumTime = Mathf.Min(maximumTime, second);
        return minimumTime <= maximumTime;
    }

    private void ResetAllEnemyPartyTargetsToLeader()
    {
        for (int i = 0; i < enemies.Count; i++)
            SetEnemyLeaderApproach(enemies[i]);
    }

    private int CountEnemiesInTargetPhase(EnemyPartyTargetPhase phase)
    {
        int count = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (enemy.Alive && enemy.AggroActive && enemy.TargetPhase == phase)
                count++;
        }

        return count;
    }

    private int CountMemberEngagedTargets(int memberIndex)
    {
        return CountTargetsInPhase(EnemyPartyTargetPhase.MemberEngaged, memberIndex);
    }

    private int CountTargetsInPhase(EnemyPartyTargetPhase phase, int memberIndex)
    {
        int count = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (enemy.Alive
                && enemy.AggroActive
                && enemy.TargetPhase == phase
                && enemy.TargetPartyIndex == memberIndex)
            {
                count++;
            }
        }

        return count;
    }

    private void TryRefreshFullReformation()
    {
        float movedDistance = HorizontalDistance(lastFullReformationPosition, playerPosition);
        if (!EnemySquadPursuitPlanner.ShouldRefreshFullReformation(
            movedDistance,
            elapsedSimulationTime,
            nextFullReformationTime,
            lastFullReformationTime))
        {
            return;
        }

        RebuildStrategicSquads();
        pursuitFrameInitialized = false;
        routeRefreshPending = false;
        routeRefreshCursor = 0;
        pursuitFramePlayerPosition = playerPosition;
        lastRouteTranslationPlayerPosition = playerPosition;
        lastFullReformationPosition = playerPosition;
        lastFullReformationTime = elapsedSimulationTime;
        nextFullReformationTime = elapsedSimulationTime + FullReformationInterval;
        fullReformationCount++;
        AddEvent("부대·슬롯 전체 재편성 · " + movedDistance.ToString("0.0") + "m");
    }

    private void RebuildStrategicSquads()
    {
        for (int squadIndex = squads.Count - 1; squadIndex >= 0; squadIndex--)
        {
            SimSquad squad = squads[squadIndex];
            if (squad.DirectCommitted)
            {
                BeginSquadRush(squad, "전체 재편성 중 진입 확정");
                continue;
            }
            if (!IsStrategicReformationSquad(squad))
                continue;

            ReleaseSlot(squad);
            for (int memberIndex = 0; memberIndex < squad.Members.Count; memberIndex++)
            {
                SimEnemy member = squad.Members[memberIndex];
                member.SquadId = -1;
                member.LocalOffset = Vector3.zero;
            }
            squads.RemoveAt(squadIndex);
        }

        unassignedBuffer.Clear();
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (enemy.Alive && enemy.AggroActive && enemy.SquadParticipant && enemy.SquadId < 0)
                unassignedBuffer.Add(enemy);
        }
        EnemySquadPursuitPlanner.BuildMaximumFirstSquadSizes(
            unassignedBuffer.Count,
            minimumSquadSize,
            maximumSquadSize,
            squadSizes);
        for (int i = 0; i < squadSizes.Count; i++)
            CreateNearestSquad(squadSizes[i]);
    }

    private static bool IsStrategicReformationSquad(SimSquad squad)
    {
        return squad != null
            && !squad.HasRushed
            && !squad.SlotEligibilityRevoked
            && (squad.Mode == SquadMode.Pursuit || squad.Mode == SquadMode.Reserve);
    }

    private void RefreshDynamicPursuitFrame(bool resetRoutes)
    {
        if (!encounterActive)
            return;

        bool hadPursuitFrame = pursuitFrameInitialized;
        bool hasActiveSlotOwners = CountOccupiedSlots() > 0;
        if (resetRoutes || !hadPursuitFrame || !hasActiveSlotOwners)
        {
            SimSquad nearest = FindNearestSlotEligibleSquad();
            if (nearest == null)
                return;
            directOutward = EnemySquadPursuitPlanner.ResolveDirectOutward(
                playerPosition,
                nearest.SmoothedCenter);
        }

        pursuitFrameInitialized = true;
        RefreshFixedPointRoles(resetRoutes || !hadPursuitFrame);
    }

    private void RefreshFixedPointRoles(bool forceRouteRefresh)
    {
        if (!encounterActive || !pursuitFrameInitialized)
            return;

        TranslatePursuitRoutes();
        EnemySquadPursuitPlanner.BuildSlots(
            playerPosition,
            directOutward,
            slotRadius,
            slots);
        float playerMovedDistance = HorizontalDistance(pursuitFramePlayerPosition, playerPosition);
        bool routeShapeStale = HasRouteHeadingDrift();
        bool routeInvalid = HasInvalidTranslatedRoute();
        bool requestRouteRefresh = forceRouteRefresh
            || routeInvalid
            || routeShapeStale && elapsedSimulationTime >= nextRouteRefreshTime
            || EnemySquadPursuitPlanner.ShouldRefreshRoute(
                playerMovedDistance,
                elapsedSimulationTime,
                nextRouteRefreshTime,
                routeRefreshPending);
        if (requestRouteRefresh && !routeRefreshPending)
        {
            routeRefreshPending = true;
            routeRefreshCursor = 0;
            nextRouteRefreshTime = elapsedSimulationTime + RouteRefreshMinimumInterval;
        }

        ProcessPendingRouteRefresh();
    }

    private void TranslatePursuitRoutes()
    {
        Vector3 delta = playerPosition - lastRouteTranslationPlayerPosition;
        delta.y = 0f;
        lastRouteTranslationPlayerPosition = playerPosition;
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (squad.Mode != SquadMode.Pursuit
                || squad.DirectCommitted
                || squad.Route.WaypointCount <= 0)
            {
                continue;
            }
            squad.Route = EnemySquadPursuitPlanner.TranslateRoute(squad.Route, delta);
        }
    }

    private bool HasRouteHeadingDrift()
    {
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (squad.Mode != SquadMode.Pursuit
                || squad.DirectCommitted
                || squad.SlotIndex < 0
                || squad.SlotIndex >= slots.Count)
            {
                continue;
            }
            Vector3 desired = Flatten(slots[squad.SlotIndex].Position - squad.SmoothedCenter);
            if (squad.RouteSlotDirection.sqrMagnitude > 0.0001f
                && desired.sqrMagnitude > 0.0001f
                && Vector3.Angle(squad.RouteSlotDirection, desired) >= RouteRefreshHeadingThreshold)
            {
                return true;
            }
        }
        return false;
    }

    private bool HasInvalidTranslatedRoute()
    {
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (squad.Mode != SquadMode.Pursuit
                || squad.DirectCommitted
                || squad.Route.WaypointCount <= 0)
            {
                continue;
            }
            int start = Mathf.Clamp(squad.RouteWaypointIndex, 0, squad.Route.WaypointCount - 1);
            for (int waypointIndex = start; waypointIndex < squad.Route.WaypointCount; waypointIndex++)
            {
                if (!IsWalkable(squad.Route.GetWaypoint(waypointIndex)))
                    return true;
            }
        }
        return false;
    }

    private void ProcessPendingRouteRefresh()
    {
        if (!routeRefreshPending)
            return;

        int refreshed = 0;
        while (routeRefreshCursor < squads.Count && refreshed < MaximumRouteRefreshPerFrame)
        {
            SimSquad squad = squads[routeRefreshCursor++];
            if (squad.SlotIndex < 0 || squad.SlotIndex >= slots.Count)
                continue;
            if (!IsSlotWalkable(squad.SlotIndex))
            {
                ReleaseSlot(squad);
                squad.Mode = SquadMode.Reserve;
                refreshed++;
                continue;
            }
            if (squad.DirectCommitted || squad.Mode != SquadMode.Pursuit)
                continue;
            squad.Route = EnemySquadPursuitPlanner.BuildRoute(
                squad.SmoothedCenter,
                playerPosition,
                directOutward,
                slots[squad.SlotIndex],
                slots,
                curveRadiusMultiplier);
            squad.RouteWaypointIndex = 0;
            squad.RouteSlotDirection = ResolveRouteSlotDirection(squad);
            refreshed++;
            routeRefreshCount++;
        }

        if (routeRefreshCursor < squads.Count)
            return;
        routeRefreshPending = false;
        pursuitFramePlayerPosition = playerPosition;
    }

    private void RecruitUnassignedEnemies()
    {
        unassignedBuffer.Clear();
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (enemy.Alive && enemy.AggroActive && enemy.SquadParticipant && enemy.SquadId < 0)
                unassignedBuffer.Add(enemy);
        }
        if (unassignedBuffer.Count == 0)
            return;

        while (unassignedBuffer.Count > 0)
        {
            SimSquad nearestSquad = null;
            int nearestEnemyIndex = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int squadIndex = 0; squadIndex < squads.Count; squadIndex++)
            {
                SimSquad candidateSquad = squads[squadIndex];
                if (candidateSquad.SlotEligibilityRevoked)
                    continue;
                int desired = maximumSquadSize; // 부분 부대도 후속 인원으로 최대치까지 먼저 충원
                if (candidateSquad.AliveCount <= 0 || candidateSquad.AliveCount >= desired)
                    continue;

                int enemyIndex = FindNearestEnemyIndex(unassignedBuffer, candidateSquad.SmoothedCenter);
                float distance = HorizontalSqrDistance(
                    unassignedBuffer[enemyIndex].Position,
                    candidateSquad.SmoothedCenter);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestSquad = candidateSquad;
                    nearestEnemyIndex = enemyIndex;
                }
            }

            if (nearestSquad == null || nearestEnemyIndex < 0)
                break;

            SimEnemy recruit = unassignedBuffer[nearestEnemyIndex];
            unassignedBuffer.RemoveAt(nearestEnemyIndex);
            recruit.SquadId = nearestSquad.Id;
            recruit.LocalOffset = recruit.Position - nearestSquad.SmoothedCenter;
            nearestSquad.Members.Add(recruit);
            nearestSquad.InitialCount = Mathf.Max(nearestSquad.InitialCount, nearestSquad.AliveCount);
            AddEvent("M" + recruit.Id + " → S" + nearestSquad.Id + " 충원");
        }

        if (unassignedBuffer.Count >= minimumSquadSize)
        {
            EnemySquadPursuitPlanner.BuildMaximumFirstSquadSizes(
                unassignedBuffer.Count,
                minimumSquadSize,
                maximumSquadSize,
                squadSizes);
            for (int i = 0; i < squadSizes.Count; i++)
                CreateNearestSquad(squadSizes[i]);
            AddEvent("증원으로 신규 부대 편성");
        }
    }

    private void RefreshSquadCenters(float deltaTime)
    {
        float blend = 1f - Mathf.Exp(-CenterSmoothSpeed * deltaTime);
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (squad.ParticipantCount <= 0)
                continue;
            squad.RawCenter = CalculateSquadCenter(squad);
            squad.SmoothedCenter = Vector3.Lerp(squad.SmoothedCenter, squad.RawCenter, blend);
            if (elapsedSimulationTime >= squad.NextTrailTime)
            {
                squad.Trail.Add(squad.SmoothedCenter);
                if (squad.Trail.Count > MaximumTrailPoints)
                    squad.Trail.RemoveAt(0);
                squad.NextTrailTime = elapsedSimulationTime + TrailRecordInterval;
            }
        }
    }

    private void UpdateSquadModes(float deltaTime)
    {
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            int aliveCount = squad.AliveCount;
            if (aliveCount <= 0)
            {
                if (squad.Mode != SquadMode.Dead)
                {
                    ReleaseSlot(squad);
                    squad.Mode = SquadMode.Dead;
                    AddEvent("S" + squad.Id + " 전멸");
                }
                continue;
            }

            if (ShouldUseRemnantPattern(
                squad.SlotEligibilityRevoked,
                aliveCount,
                squad.InitialCount,
                remnantRatio))
            {
                if (!squad.SlotEligibilityRevoked)
                {
                    squad.SlotEligibilityRevoked = true;
                    ReleaseSlot(squad);
                    squad.HasRushed = false;
                    squad.FarEligibleTime = 0f;
                    AddEvent("S" + squad.Id + " 기준미달 · 슬롯권 박탈");
                }
                squad.Mode = SquadMode.Remnant;
                continue;
            }

            if (squad.ParticipantCount <= 0)
            {
                ReleaseSlot(squad);
                squad.HasRushed = false;
                squad.FarEligibleTime = 0f;
                squad.Mode = SquadMode.Reserve;
                continue;
            }

            float playerDistance = HorizontalDistance(squad.SmoothedCenter, playerPosition);
            float nearMemberRatio = ResolveNearMemberRatio(squad);
            if (squad.HasRushed && AreAllMembersOutsidePlayerDistance(squad, farActivationDistance))
            {
                squad.FarEligibleTime += deltaTime;
                if (squad.FarEligibleTime >= FarReentryDelay)
                {
                    EndSquadRush(squad);
                    continue;
                }
            }
            else if (squad.HasRushed)
            {
                squad.FarEligibleTime = 0f;
            }
            if (squad.Mode == SquadMode.Rush)
            {
                if (nearMemberRatio >= 0.999f)
                {
                    squad.Mode = SquadMode.NearCombat;
                    AddEvent("S" + squad.Id + " 전원 근접 · 개별 전투");
                }
                continue;
            }
            if (squad.Mode == SquadMode.Reserve
                && (playerDistance <= nearReleaseDistance || nearMemberRatio >= 0.3f))
            {
                ReleaseSlot(squad);
                squad.Mode = SquadMode.NearCombat;
                squad.FarEligibleTime = 0f;
                AddEvent("S" + squad.Id + " 근거리 전투 전환");
                continue;
            }
            if (squad.Mode == SquadMode.Pursuit
                && HasAnyMemberWithinPlayerDistance(squad, nearReleaseDistance))
            {
                BeginSquadRush(squad, "선두 근거리 진입");
                continue;
            }
            if (squad.Mode == SquadMode.Pursuit
                && !squad.DirectCommitted
                && squad.SlotIndex >= 0
                && squad.SlotIndex < slots.Count
                && HasAnyMemberWithinPlayerDistance(squad, directCommitRadius))
            {
                squad.DirectCommitted = true;
                squad.RouteWaypointIndex = 0;
                squad.Route = default;
                AddEvent("S" + squad.Id + " 직선 진입 확정 · P" + slots[squad.SlotIndex].PointIndex);
            }

            if (squad.Mode == SquadMode.NearCombat && !squad.HasRushed)
            {
                if (playerDistance >= farActivationDistance && nearMemberRatio <= 0.01f)
                {
                    squad.FarEligibleTime += deltaTime;
                    if (squad.FarEligibleTime >= FarReentryDelay)
                    {
                        squad.Mode = SquadMode.Reserve;
                        squad.FarEligibleTime = 0f;
                        AddEvent("S" + squad.Id + " 원거리 슬롯 재신청");
                    }
                }
                else
                {
                    squad.FarEligibleTime = 0f;
                }
            }

            if (squad.Mode == SquadMode.Pursuit && squad.SlotIndex >= 0 && squad.SlotIndex < slots.Count)
            {
                if (squad.DirectCommitted)
                {
                    if (HasAnyMemberReachedFormationDestination(
                        squad,
                        slots[squad.SlotIndex].Position))
                    {
                        BeginSquadRush(squad, "선두 직선 슬롯 도착");
                    }
                    continue;
                }
                if (squad.Route.WaypointCount <= 0)
                    continue;
                int routeIndex = Mathf.Clamp(
                    squad.RouteWaypointIndex,
                    0,
                    squad.Route.WaypointCount - 1);
                Vector3 waypoint = squad.Route.GetWaypoint(routeIndex);
                if (HasAnyMemberReachedFormationDestination(squad, waypoint))
                {
                    squad.RouteWaypointIndex++;
                    if (squad.RouteWaypointIndex >= squad.Route.WaypointCount)
                        BeginSquadRush(squad, "선두 슬롯 도착");
                }
            }
        }
    }

    private void AssignAvailableSlots()
    {
        if (!encounterActive || !pursuitFrameInitialized || slots.Count < 8)
            return;

        EnsureDirectSlotOwner();
        AssignVacantSlotsByMinimumDistance();
        RefreshOpportunisticRearSlot();

        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (IsFarEligibleSquad(squad) && squad.SlotIndex < 0)
                squad.Mode = SquadMode.Reserve;
        }
    }

    private void RefreshOpportunisticRearSlot()
    {
        int rearSlotIndex = FindSlotIndexByKind(EnemySquadPursuitSlotKind.Rear);
        if (!IsSlotWalkable(rearSlotIndex))
            return;

        SimSquad owner = FindSlotOwner(rearSlotIndex);
        if (owner != null)
        {
            bool leaseExpired = elapsedSimulationTime >= owner.SlotLeaseUntil;
            bool outsideReleaseSector = !EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                playerPosition,
                owner.SmoothedCenter,
                slots[rearSlotIndex].OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotReleaseAngle);
            if (!owner.DirectCommitted && leaseExpired && outsideReleaseSector)
            {
                ReleaseSlot(owner);
                owner.Mode = SquadMode.Reserve;
                AddEvent("S" + owner.Id + " 후방 궤도 이탈 · 슬롯 반납");
                owner = null;
            }
        }
        if (owner != null)
            return;

        SimSquad candidate = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (!IsNearbyRearSlotCandidate(squad, rearSlotIndex))
                continue;
            float distance = HorizontalSqrDistance(
                squad.SmoothedCenter,
                slots[rearSlotIndex].Position);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            candidate = squad;
        }
        if (candidate == null)
            return;

        AssignSlot(candidate, rearSlotIndex);
        AddEvent("S" + candidate.Id + " 후방 궤도 예비 · P" + slots[rearSlotIndex].PointIndex + " 배정");
    }

    private bool IsNearbyRearSlotCandidate(SimSquad squad, int rearSlotIndex)
    {
        if (squad == null
            || squad.ParticipantCount <= 0
            || squad.Mode != SquadMode.Reserve
            || squad.SlotIndex >= 0
            || squad.HasRushed
            || squad.SlotEligibilityRevoked
            || !IsSlotWalkable(rearSlotIndex)
            || slots[rearSlotIndex].Kind != EnemySquadPursuitSlotKind.Rear)
        {
            return false;
        }

        return EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
            playerPosition,
            squad.SmoothedCenter,
            slots[rearSlotIndex].OutwardDirection,
            EnemySquadPursuitPlanner.DefaultRearSlotClaimAngle);
    }

    private void EnsureDirectSlotOwner()
    {
        int directSlotIndex = FindSlotIndexByKind(EnemySquadPursuitSlotKind.Direct);
        if (!IsSlotAssignable(directSlotIndex) || FindSlotOwner(directSlotIndex) != null)
            return;

        SimSquad candidate = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (!IsReserveCandidate(squad))
                continue;
            float distance = HorizontalSqrDistance(squad.SmoothedCenter, slots[directSlotIndex].Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                candidate = squad;
            }
        }
        if (candidate == null)
            return;

        int previousSlot = candidate.SlotIndex;
        AssignSlot(candidate, directSlotIndex);
        if (previousSlot >= 0)
            AddEvent("S" + candidate.Id + " 슬롯 " + previousSlot + " → 정면 승격");
    }

    private void AssignVacantSlotsByMinimumDistance()
    {
        reserveSquadBuffer.Clear();
        reserveCenterBuffer.Clear();
        int farEligibleCount = 0;
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (IsFarEligibleSquad(squad))
                farEligibleCount++;
            if (!IsReserveCandidate(squad))
                continue;
            reserveSquadBuffer.Add(squad);
            reserveCenterBuffer.Add(squad.SmoothedCenter);
        }
        if (reserveSquadBuffer.Count <= 0)
            return;

        assignableSlotBuffer.Clear();
        for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            if (IsSlotAssignable(slotIndex))
                assignableSlotBuffer.Add(slots[slotIndex]);
        }
        EnemySquadPursuitPlanner.BuildBalancedSlotIndices(
            assignableSlotBuffer,
            farEligibleCount,
            balancedSlotBuffer);

        vacantSlotBuffer.Clear();
        vacantSlotPositionBuffer.Clear();
        for (int selectedIndex = 0; selectedIndex < balancedSlotBuffer.Count; selectedIndex++)
        {
            int slotIndex = balancedSlotBuffer[selectedIndex];
            if (!IsSlotAssignable(slotIndex) || FindSlotOwner(slotIndex) != null)
                continue;
            vacantSlotBuffer.Add(slotIndex);
            vacantSlotPositionBuffer.Add(slots[slotIndex].Position);
        }
        if (vacantSlotBuffer.Count <= 0)
            return;

        EnemySquadPursuitPlanner.BuildMinimumDistanceAssignment(
            reserveCenterBuffer,
            vacantSlotPositionBuffer,
            squadIndexBySlotBuffer);
        for (int vacantIndex = 0; vacantIndex < vacantSlotBuffer.Count; vacantIndex++)
        {
            int squadIndex = squadIndexBySlotBuffer[vacantIndex];
            if (squadIndex >= 0 && squadIndex < reserveSquadBuffer.Count)
                AssignSlot(reserveSquadBuffer[squadIndex], vacantSlotBuffer[vacantIndex]);
        }
    }

    private void AssignSlot(SimSquad squad, int slotIndex)
    {
        if (squad == null
            || squad.SlotEligibilityRevoked
            || slotIndex < 0
            || slotIndex >= slots.Count)
            return;

        squad.SlotIndex = slotIndex;
        squad.Mode = SquadMode.Pursuit;
        squad.DirectCommitted = false;
        squad.SlotLeaseUntil = elapsedSimulationTime + SlotLeaseDuration;
        squad.RouteWaypointIndex = 0;
        squad.Route = EnemySquadPursuitPlanner.BuildRoute(
            squad.SmoothedCenter,
            playerPosition,
            directOutward,
            slots[slotIndex],
            slots,
            curveRadiusMultiplier);
        squad.RouteSlotDirection = ResolveRouteSlotDirection(squad);
    }

    private Vector3 ResolveRouteSlotDirection(SimSquad squad)
    {
        if (squad == null || squad.SlotIndex < 0 || squad.SlotIndex >= slots.Count)
            return Vector3.zero;
        Vector3 direction = Flatten(slots[squad.SlotIndex].Position - squad.SmoothedCenter);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private void ReleaseSlot(SimSquad squad)
    {
        if (squad == null)
            return;
        squad.SlotIndex = -1;
        squad.DirectCommitted = false;
        squad.SlotLeaseUntil = 0f;
        squad.RouteWaypointIndex = 0;
        squad.Route = default;
        squad.RouteSlotDirection = Vector3.zero;
    }

    private void BeginSquadRush(SimSquad squad, string reason)
    {
        if (squad == null || squad.Mode == SquadMode.Rush || squad.ParticipantCount <= 0)
            return;

        ReleaseSlot(squad);
        squad.HasRushed = true;
        squad.Mode = SquadMode.Rush;
        squad.FarEligibleTime = 0f;
        AddEvent("S" + squad.Id + " " + reason + " · 전원 돌진");
    }

    private void EndSquadRush(SimSquad squad)
    {
        if (squad == null || !squad.HasRushed)
            return;

        ReleaseSlot(squad);
        squad.HasRushed = false;
        squad.Mode = SquadMode.Reserve;
        squad.FarEligibleTime = 0f;
        AddEvent("S" + squad.Id + " 전원 Far 이탈 · 돌진 해제");
    }

    private bool IsFarEligibleSquad(SimSquad squad)
    {
        if (squad == null
            || squad.ParticipantCount <= 0
            || squad.SlotEligibilityRevoked
            || squad.HasRushed
            || squad.Mode == SquadMode.Remnant
            || squad.Mode == SquadMode.Dead)
            return false;
        if (squad.Mode == SquadMode.Rush || squad.Mode == SquadMode.NearCombat)
            return false;
        float distance = HorizontalDistance(squad.SmoothedCenter, playerPosition);
        return squad.Mode == SquadMode.Pursuit
            ? distance > nearReleaseDistance
            : distance >= farActivationDistance;
    }

    private bool IsReserveCandidate(SimSquad squad)
    {
        return IsFarEligibleSquad(squad) && squad.SlotIndex < 0;
    }

    private void MoveEnemies(float deltaTime)
    {
        for (int i = 0; i < squads.Count; i++)
            ResolveSquadMoveDirection(squads[i]);

        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            enemy.SnapshotPosition = enemy.Position;
            enemy.DesiredPosition = enemy.Position;
            enemy.HasMoveIntent = false;
            enemy.MovePriority = enemy.AggroActive
                ? ResolveMovePriority(FindSquad(enemy.SquadId))
                : 0;
        }

        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (!enemy.Alive)
                continue;
            if (!enemy.AggroActive && !enemy.Returning)
            {
                enemy.Velocity = Vector3.zero;
                continue;
            }
            if (enemy.AggroActive && enemy.AlertRemaining > 0f)
            {
                enemy.Velocity = Vector3.zero;
                continue;
            }

            SimSquad squad = enemy.AggroActive ? FindSquad(enemy.SquadId) : null;
            Vector3 target = enemy.AggroActive ? ResolveEnemyTarget(enemy, squad) : enemy.HomePosition;
            float speed = enemy.AggroActive ? ResolveEnemySpeed(enemy, squad) : enemy.WalkSpeed;
            Vector3 desired = Flatten(target - enemy.SnapshotPosition);
            if (enemy.Returning && desired.sqrMagnitude <= ReturnArriveDistance * ReturnArriveDistance)
            {
                enemy.DesiredPosition = enemy.HomePosition;
                enemy.HasMoveIntent = true;
                continue;
            }
            Vector3 direction = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
            Vector3 separation = ResolveSeparation(enemy);
            float separationWeight = simulationDataMode == SimulationDataMode.AI프리셋데이터
                ? enemy.SeparationWeight
                : monsterSeparationStrength;
            Vector3 combinedDirection = direction + separation * separationWeight;
            direction = squad != null
                && (squad.Mode == SquadMode.Pursuit
                    || squad.Mode == SquadMode.Rush
                    || squad.Mode == SquadMode.NearCombat && squad.HasRushed)
                ? PreserveMinimumForwardProgress(
                    direction,
                    combinedDirection,
                    MinimumPursuitForwardProgress)
                : Vector3.ClampMagnitude(combinedDirection, 1f);
            enemy.Velocity = direction * speed; // 실제 EnemyMovement와 동일하게 가속 보간 없이 후보 위치 계산
            Vector3 proposed = enemy.SnapshotPosition + enemy.Velocity * deltaTime;
            proposed = ApplySimulatorYieldBias(enemy, squad, proposed);
            proposed = ResolveObstacleMovement(enemy.SnapshotPosition, proposed);
            enemy.DesiredPosition = proposed;
            enemy.HasMoveIntent = HorizontalSqrDistance(proposed, enemy.SnapshotPosition) > 0.000001f;
            if (!enemy.HasMoveIntent)
                enemy.Velocity = Vector3.zero;
        }

        ResolveCentralHardOverlap(deltaTime);
    }

    private void ResolveSquadMoveDirection(SimSquad squad)
    {
        if (squad == null || squad.ParticipantCount <= 0)
            return;
        Vector3 destination = ResolveSquadDestination(squad);
        Vector3 direction = Flatten(destination - squad.SmoothedCenter);
        squad.MoveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private Vector3 ResolveSquadDestination(SimSquad squad)
    {
        if (squad == null)
            return playerPosition;
        if (squad.Mode == SquadMode.Pursuit
            && squad.DirectCommitted
            && squad.SlotIndex >= 0
            && squad.SlotIndex < slots.Count)
        {
            return slots[squad.SlotIndex].Position;
        }
        if (squad.Mode == SquadMode.Pursuit && squad.Route.WaypointCount > 0)
        {
            int index = Mathf.Clamp(squad.RouteWaypointIndex, 0, squad.Route.WaypointCount - 1);
            return squad.Route.GetWaypoint(index);
        }
        if (squad.Mode == SquadMode.Reserve)
        {
            Vector3 orbitDirection = EnemySquadPursuitPlanner.ResolveReserveOrbitDirection(
                stableSeed,
                squad.Id,
                elapsedSimulationTime * reserveOrbitAngularSpeed);
            float orbitRadius = EnemySquadPursuitPlanner.ResolveReserveOrbitRadius(
                reserveOrbitRadius,
                squad.Id);
            return EnemySquadPursuitPlanner.ResolveReserveOrbitDestination(
                playerPosition,
                squad.SmoothedCenter,
                orbitDirection,
                orbitRadius,
                squad.Id);
        }
        return playerPosition;
    }

    private Vector3 ResolveEnemyTarget(SimEnemy enemy, SimSquad squad)
    {
        Vector3 individualTarget = ResolveEnemyPartyTargetPosition(enemy);
        if (squad == null || squad.Mode == SquadMode.Legacy)
            return individualTarget;
        if (squad.Mode == SquadMode.Pursuit || squad.Mode == SquadMode.Reserve)
        {
            Vector3 squadDestination = ResolveSquadDestination(squad);
            return EnemySquadPursuitPlanner.ResolveCohesionDestination(
                enemy.Position,
                squad.SmoothedCenter,
                enemy.LocalOffset,
                squadDestination,
                FormationOffsetScale);
        }
        return individualTarget;
    }

    private float ResolveEnemySpeed(SimEnemy enemy, SimSquad squad)
    {
        float individualTargetDistance = enemy != null
            ? HorizontalDistance(enemy.Position, ResolveEnemyPartyTargetPosition(enemy))
            : 0f;
        if (simulationDataMode == SimulationDataMode.AI프리셋데이터 && enemy != null)
        {
            if (squad == null || !encounterActive)
                return enemy.WalkSpeed;
            switch (squad.Mode)
            {
                case SquadMode.Pursuit:
                    return enemy.PursuitSpeed
                        * ResolveCohesionSpeedMultiplier(enemy, squad);
                case SquadMode.Reserve:
                    return enemy.WalkSpeed * reserveSpeedMultiplier
                        * ResolveCohesionSpeedMultiplier(enemy, squad);
                case SquadMode.Rush:
                    return individualTargetDistance > nearReleaseDistance
                        ? enemy.PursuitSpeed
                        : enemy.WalkSpeed;
                case SquadMode.NearCombat:
                    if (squad.HasRushed
                        && individualTargetDistance > nearReleaseDistance)
                    {
                        return enemy.PursuitSpeed;
                    }
                    return enemy.WalkSpeed;
                case SquadMode.Remnant:
                    return enemy.WalkSpeed;
                default:
                    return enemy.WalkSpeed;
            }
        }

        if (squad == null || !encounterActive)
            return LegacyMonsterSpeed;
        switch (squad.Mode)
        {
            case SquadMode.Pursuit:
                return baseMonsterSpeed * ResolveCohesionSpeedMultiplier(enemy, squad);
            case SquadMode.Reserve:
                return baseMonsterSpeed * reserveSpeedMultiplier
                    * ResolveCohesionSpeedMultiplier(enemy, squad);
            case SquadMode.Rush:
                return individualTargetDistance > nearReleaseDistance
                    ? baseMonsterSpeed
                    : NearCombatSpeed;
            case SquadMode.NearCombat:
                if (squad.HasRushed
                    && individualTargetDistance > nearReleaseDistance)
                {
                    return baseMonsterSpeed;
                }
                return NearCombatSpeed;
            case SquadMode.Remnant:
                return NearCombatSpeed;
            default:
                return LegacyMonsterSpeed;
        }
    }

    private float ResolveCohesionSpeedMultiplier(SimEnemy enemy, SimSquad squad)
    {
        if (enemy == null || squad == null)
            return 1f;

        return EnemySquadPursuitPlanner.ResolveCohesionSpeedMultiplier(
            enemy.Position,
            squad.SmoothedCenter,
            ResolveSquadDestination(squad));
    }

    private Vector3 ResolveSeparation(SimEnemy enemy)
    {
        Vector3 separation = Vector3.zero;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy other = enemies[i];
            if (!other.Alive || other == enemy)
                continue;
            Vector3 delta = Flatten(enemy.SnapshotPosition - other.SnapshotPosition);
            float distanceSqr = delta.sqrMagnitude;
            float desiredDistance = simulationDataMode == SimulationDataMode.AI프리셋데이터
                ? Mathf.Max(enemy.SeparationRadius, enemy.BodyRadius + other.BodyRadius)
                : monsterSeparationRadius;
            if (distanceSqr <= 0.0001f || distanceSqr >= desiredDistance * desiredDistance)
                continue;
            float distance = Mathf.Sqrt(distanceSqr);
            separation += delta / distance
                * (1f - distance / desiredDistance)
                * ResolvePrioritySeparationScale(enemy.MovePriority, other.MovePriority);
        }
        return Vector3.ClampMagnitude(separation, 1f);
    }

    private Vector3 ApplySimulatorYieldBias(SimEnemy enemy, SimSquad squad, Vector3 desiredPosition)
    {
        if (enemy == null
            || squad == null
            || squad.Mode != SquadMode.Reserve
            || elapsedSimulationTime > enemy.YieldUntilTime
            || enemy.YieldDirection.sqrMagnitude <= 0.0001f)
        {
            return desiredPosition;
        }

        Vector3 travel = Flatten(desiredPosition - enemy.SnapshotPosition);
        if (travel.sqrMagnitude <= 0.000001f)
            return desiredPosition;
        Vector3 direction = (travel.normalized
            + enemy.YieldDirection * EnemyCrowdPrioritySolver.ReserveYieldSteeringWeight).normalized;
        Vector3 biased = enemy.SnapshotPosition + direction * travel.magnitude;
        biased.y = desiredPosition.y;
        return IsWalkable(biased) ? biased : desiredPosition;
    }

    private void ResolveCentralHardOverlap(float deltaTime)
    {
        centralSolverEnemies.Clear();
        centralSolverBodies.Clear();
        centralSolverCorrections.Clear();
        centralSolverYieldCorrections.Clear();
        centralSolverYieldPressures.Clear();

        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (!enemy.Alive)
                continue;

            Vector3 desiredPosition = enemy.HasMoveIntent
                ? enemy.DesiredPosition
                : enemy.Position;
            centralSolverEnemies.Add(enemy);
            centralSolverBodies.Add(new EnemyCrowdPriorityBody
            {
                StableId = enemy.Id,
                DesiredPosition = desiredPosition,
                ResolvedPosition = desiredPosition,
                BodyRadius = enemy.BodyRadius,
                CrowdWeight = enemy.CrowdWeight,
                MovePriority = enemy.MovePriority,
                CanMove = enemy.HasMoveIntent,
                IsForcedMotion = false,
                YieldCorrection = Vector3.zero,
                YieldPressure = 0f
            });
            centralSolverCorrections.Add(Vector3.zero);
            centralSolverYieldCorrections.Add(Vector3.zero);
            centralSolverYieldPressures.Add(0f);
        }

        for (int pass = 0; pass < EnemyCrowdService.CentralSolverPassCount; pass++)
        {
            for (int i = 0; i < centralSolverCorrections.Count; i++)
            {
                centralSolverCorrections[i] = Vector3.zero;
                centralSolverYieldCorrections[i] = Vector3.zero;
                centralSolverYieldPressures[i] = 0f;
            }

            bool hasCorrectableOverlap = false;
            for (int i = 0; i < centralSolverBodies.Count; i++)
            {
                EnemyCrowdPriorityBody body = centralSolverBodies[i];
                for (int otherIndex = i + 1; otherIndex < centralSolverBodies.Count; otherIndex++)
                {
                    if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                        body,
                        centralSolverBodies[otherIndex],
                        out EnemyCrowdPairCorrection pairCorrection))
                    {
                        continue;
                    }

                    centralSolverCorrections[i] += pairCorrection.CorrectionA;
                    centralSolverCorrections[otherIndex] += pairCorrection.CorrectionB;
                    centralSolverYieldCorrections[i] += pairCorrection.YieldCorrectionA;
                    centralSolverYieldCorrections[otherIndex] += pairCorrection.YieldCorrectionB;
                    centralSolverYieldPressures[i] = Mathf.Max(
                        centralSolverYieldPressures[i],
                        pairCorrection.Penetration);
                    centralSolverYieldPressures[otherIndex] = Mathf.Max(
                        centralSolverYieldPressures[otherIndex],
                        pairCorrection.Penetration);
                    hasCorrectableOverlap = true;
                }
            }

            if (!hasCorrectableOverlap)
                break;

            for (int i = 0; i < centralSolverBodies.Count; i++)
            {
                EnemyCrowdPriorityBody body = centralSolverBodies[i];
                Vector3 applied = EnemyCrowdPrioritySolver.ApplyAccumulatedCorrection(
                    ref body,
                    centralSolverCorrections[i],
                    RuntimeHardOverlapCorrection);
                if (applied.sqrMagnitude > 0.0000001f
                    && centralSolverYieldCorrections[i].sqrMagnitude > 0.0000001f)
                {
                    body.YieldCorrection += centralSolverYieldCorrections[i];
                    body.YieldPressure = Mathf.Max(body.YieldPressure, centralSolverYieldPressures[i]);
                }
                centralSolverBodies[i] = body;
            }
        }

        float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
        for (int i = 0; i < centralSolverBodies.Count; i++)
        {
            SimEnemy enemy = centralSolverEnemies[i];
            if (!enemy.HasMoveIntent)
            {
                enemy.Velocity = Vector3.zero;
                continue;
            }

            EnemyCrowdPriorityBody body = centralSolverBodies[i];
            Vector3 finalPosition = ResolveCentralSimulatorCandidate(
                enemy,
                body.DesiredPosition,
                body.ResolvedPosition);
            Vector3 actualCorrection = Flatten(finalPosition - body.DesiredPosition);
            SimSquad squad = FindSquad(enemy.SquadId);
            if (squad != null
                && squad.Mode == SquadMode.Reserve
                && body.YieldCorrection.sqrMagnitude > 0.0001f
                && Vector3.Dot(actualCorrection, body.YieldCorrection) > 0f)
            {
                RegisterSimulatorYield(enemy, actualCorrection, body.YieldPressure);
            }

            Vector3 actualMovement = Flatten(finalPosition - enemy.SnapshotPosition);
            enemy.Position = finalPosition;
            enemy.Velocity = actualMovement / safeDeltaTime;
        }
    }

    private Vector3 ResolveCentralSimulatorCandidate(
        SimEnemy enemy,
        Vector3 desiredPosition,
        Vector3 resolvedPosition)
    {
        if (IsWalkable(resolvedPosition))
            return resolvedPosition;

        Vector3 correction = Flatten(resolvedPosition - desiredPosition);
        float correctionDistance = correction.magnitude;
        if (correctionDistance <= 0.0001f)
            return desiredPosition;

        Vector3 tangent = new Vector3(-correction.z, 0f, correction.x).normalized;
        if ((enemy.Id & 1) != 0)
            tangent = -tangent;
        for (int i = 0; i < 2; i++)
        {
            Vector3 tangentCandidate = desiredPosition + tangent * correctionDistance;
            if (IsWalkable(tangentCandidate))
                return tangentCandidate;
            tangent = -tangent;
        }

        if (elapsedSimulationTime <= enemy.YieldUntilTime
            && enemy.YieldDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 yieldCandidate = desiredPosition + enemy.YieldDirection * correctionDistance;
            if (IsWalkable(yieldCandidate))
                return yieldCandidate;
        }
        return desiredPosition;
    }

    private void RegisterSimulatorYield(SimEnemy enemy, Vector3 correction, float pressure)
    {
        correction = Flatten(correction);
        float distance = correction.magnitude;
        if (distance < EnemyCrowdPrioritySolver.YieldActivationDistance)
            return;

        Vector3 direction = correction / distance;
        float now = elapsedSimulationTime;
        bool hasActiveDirection = now <= enemy.YieldUntilTime
            && enemy.YieldDirection.sqrMagnitude > 0.0001f;
        if (!hasActiveDirection)
        {
            enemy.YieldDirection = direction;
            enemy.YieldPressure = Mathf.Max(distance, pressure);
            enemy.YieldDirectionLockUntilTime = now
                + EnemyCrowdPrioritySolver.YieldDirectionLockDuration;
            enemy.YieldUntilTime = now + EnemyCrowdPrioritySolver.YieldDirectionHoldDuration;
            return;
        }

        float resolvedPressure = Mathf.Max(distance, pressure);
        float alignment = Vector3.Dot(enemy.YieldDirection, direction);
        if (alignment < 0f)
        {
            if (now < enemy.YieldDirectionLockUntilTime
                || resolvedPressure < enemy.YieldPressure
                    * EnemyCrowdPrioritySolver.YieldReversePressureMultiplier)
            {
                enemy.YieldUntilTime = now + EnemyCrowdPrioritySolver.YieldDirectionHoldDuration;
                return;
            }

            enemy.YieldDirection = direction;
            enemy.YieldDirectionLockUntilTime = now
                + EnemyCrowdPrioritySolver.YieldDirectionLockDuration;
        }
        else
        {
            enemy.YieldDirection = Vector3.Lerp(enemy.YieldDirection, direction, 0.35f).normalized;
        }

        enemy.YieldPressure = Mathf.Max(enemy.YieldPressure * 0.8f, resolvedPressure);
        enemy.YieldUntilTime = now + EnemyCrowdPrioritySolver.YieldDirectionHoldDuration;
    }

    internal static float ResolvePriorityCorrectionShare(
        float ownWeight,
        float otherWeight,
        int ownPriority,
        int otherPriority)
    {
        return EnemyCrowdPrioritySolver.ResolvePriorityCorrectionShare(
            ownWeight,
            otherWeight,
            ownPriority,
            otherPriority);
    }

    internal static float ResolvePrioritySeparationScale(int ownPriority, int otherPriority)
    {
        if (ownPriority > otherPriority)
            return HigherPrioritySeparationScale;
        if (ownPriority < otherPriority)
            return LowerPrioritySeparationScale;
        return 1f;
    }

    internal static Vector3 ResolveStablePairDirection(int ownId, int otherId)
    {
        return EnemyCrowdPrioritySolver.ResolveStablePairDirection(ownId, otherId);
    }

    private bool HasAnyMemberReachedFormationDestination(SimSquad squad, Vector3 destination)
    {
        if (squad == null)
            return false;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            SimEnemy member = squad.Members[i];
            if (!member.Alive || !member.AggroActive)
                continue;
            if (IsMemberAtFormationDestination(
                member.Position,
                destination,
                member.LocalOffset,
                FormationOffsetScale,
                slotArrivalDistance))
            {
                return true;
            }
        }
        return false;
    }

    private bool HasAnyMemberWithinPlayerDistance(SimSquad squad, float distance)
    {
        if (squad == null)
            return false;
        float distanceSqr = Mathf.Max(0f, distance) * Mathf.Max(0f, distance);
        for (int i = 0; i < squad.Members.Count; i++)
        {
            SimEnemy member = squad.Members[i];
            if (member.Alive
                && member.AggroActive
                && HorizontalSqrDistance(
                    member.Position,
                    ResolveEnemyPartyTargetPosition(member)) <= distanceSqr)
                return true;
        }
        return false;
    }

    private bool AreAllMembersOutsidePlayerDistance(SimSquad squad, float distance)
    {
        if (squad == null)
            return false;
        bool hasParticipant = false;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            SimEnemy member = squad.Members[i];
            if (!member.Alive || !member.AggroActive)
                continue;
            hasParticipant = true;
            if (!IsOutsidePlayerDistance(
                member.Position,
                ResolveEnemyPartyTargetPosition(member),
                distance))
                return false;
        }
        return hasParticipant;
    }

    internal static bool IsMemberAtFormationDestination(
        Vector3 memberPosition,
        Vector3 squadDestination,
        Vector3 localOffset,
        float offsetScale,
        float arrivalDistance)
    {
        Vector3 personalDestination = squadDestination + localOffset * Mathf.Max(0f, offsetScale);
        float threshold = Mathf.Max(0f, arrivalDistance);
        return HorizontalSqrDistance(memberPosition, personalDestination) <= threshold * threshold;
    }

    internal static bool IsOutsidePlayerDistance(
        Vector3 memberPosition,
        Vector3 playerPosition,
        float distance)
    {
        float threshold = Mathf.Max(0f, distance);
        return HorizontalSqrDistance(memberPosition, playerPosition) > threshold * threshold;
    }

    internal static Vector3 PreserveMinimumForwardProgress(
        Vector3 seekDirection,
        Vector3 combinedDirection,
        float minimumForwardProgress)
    {
        Vector3 seek = Flatten(seekDirection);
        if (seek.sqrMagnitude <= 0.0001f)
            return Vector3.ClampMagnitude(Flatten(combinedDirection), 1f);

        seek.Normalize();
        Vector3 resolved = Vector3.ClampMagnitude(Flatten(combinedDirection), 1f);
        float minimum = Mathf.Clamp01(minimumForwardProgress);
        float forward = Vector3.Dot(resolved, seek);
        if (forward >= minimum)
            return resolved;

        Vector3 lateral = resolved - seek * forward;
        float maximumLateral = Mathf.Sqrt(Mathf.Max(0f, 1f - minimum * minimum));
        return seek * minimum + Vector3.ClampMagnitude(lateral, maximumLateral);
    }

    private Vector3 ResolveObstacleMovement(Vector3 current, Vector3 proposed)
    {
        if (IsWalkable(proposed))
            return proposed;
        Vector3 xOnly = new Vector3(proposed.x, 0f, current.z);
        if (IsWalkable(xOnly))
            return xOnly;
        Vector3 zOnly = new Vector3(current.x, 0f, proposed.z);
        return IsWalkable(zOnly) ? zOnly : current;
    }

    private bool IsWalkable(Vector3 position)
    {
        Vector2 xz = new Vector2(position.x, position.z);
        for (int i = 0; i < obstacles.Count; i++)
        {
            if (obstacles[i].XzRect.Contains(xz))
                return false;
        }
        return true;
    }

    private bool IsSlotWalkable(int slotIndex)
    {
        return slotIndex >= 0
            && slotIndex < slots.Count
            && IsWalkable(slots[slotIndex].Position);
    }

    private bool IsSlotAssignable(int slotIndex)
    {
        return IsSlotWalkable(slotIndex)
            && slots[slotIndex].Kind != EnemySquadPursuitSlotKind.Rear;
    }

    private void SimulateAutomaticCombatDeaths(float deltaTime)
    {
        if (!automaticCombatDeaths || automaticKillRate <= 0f)
            return;
        automaticKillAccumulator += deltaTime * automaticKillRate;
        while (automaticKillAccumulator >= 1f)
        {
            automaticKillAccumulator -= 1f;
            KillRandomAliveEnemy(true);
        }
    }

    private void KillRandomAliveEnemy(bool requireNearPlayer)
    {
        unassignedBuffer.Clear();
        float maximumDistanceSqr = Mathf.Pow(Mathf.Max(nearReleaseDistance, 4f), 2f);
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (!enemy.Alive || requireNearPlayer && !enemy.AggroActive)
                continue;
            if (requireNearPlayer
                && HorizontalSqrDistance(
                    enemy.Position,
                    ResolveEnemyPartyTargetPosition(enemy)) > maximumDistanceSqr)
                continue;
            unassignedBuffer.Add(enemy);
        }
        if (unassignedBuffer.Count == 0)
            return;
        KillEnemy(unassignedBuffer[random.Next(unassignedBuffer.Count)]);
    }

    private void KillEnemy(SimEnemy enemy)
    {
        if (enemy == null || !enemy.Alive)
            return;
        enemy.Alive = false;
        enemy.AggroActive = false;
        enemy.Returning = false;
        enemy.SkipRoamDetectionOnce = false;
        enemy.AlertRemaining = 0f;
        enemy.AggroOutsideTime = 0f;
        ClearEnemyPartyTarget(enemy);
        enemy.Velocity = Vector3.zero;
        sessionMonsterDeathCount++;
        AddEvent("M" + enemy.Id + " 사망" + (enemy.SquadId >= 0 ? " · S" + enemy.SquadId : string.Empty));
        selectedEnemyId = enemy.Id;
        selectedSquadId = enemy.SquadId;
    }

    private void KillSelectedSquad()
    {
        SimSquad squad = FindSquad(selectedSquadId);
        if (squad == null)
            return;
        for (int i = 0; i < squad.Members.Count; i++)
            KillEnemy(squad.Members[i]);
        AddEvent("S" + squad.Id + " 수동 전멸");
    }

    private void SimulateContinuousSpawner(float deltaTime)
    {
        if (!continuousSpawnerEnabled)
        {
            continuousSpawnAccumulator = 0f;
            return;
        }

        continuousSpawnAccumulator += deltaTime;
        int safety = 0;
        while (continuousSpawnAccumulator >= spawnInterval && safety++ < 8)
        {
            continuousSpawnAccumulator -= spawnInterval;
            SpawnContinuousBatch();
        }
    }

    private void SpawnContinuousBatch()
    {
        if (random == null)
            random = new System.Random(stableSeed);

        int count = Mathf.Max(1, spawnBatchCount);
        for (int i = 0; i < count; i++)
        {
            int directionIndex = spawnDirectionCursor++ % 8; // 회차가 작아도 누적해서 8방향 순환
            float baseAngle = directionIndex * 45f;
            Vector3 position = ResolveContinuousSpawnPosition(baseAngle);
            enemies.Add(CreateEnemy(position));
        }

        AddEvent("사방 연속 스폰 +" + count + "명");
    }

    private Vector3 ResolveContinuousSpawnPosition(float baseAngle)
    {
        Vector3 fallback = playerPosition;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = baseAngle + NextSignedFloat() * 9f + attempt * 22.5f;
            Vector3 outward = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.right;
            Vector3 lateral = Quaternion.AngleAxis(90f, Vector3.up) * outward;
            float radius = spawnRadius + NextSignedFloat() * 1.2f;
            Vector3 candidate = playerPosition
                + outward * radius
                + lateral * NextSignedFloat() * 0.9f;
            fallback = candidate;
            if (IsWalkable(candidate))
                return candidate;
        }

        return fallback;
    }

    private float ResolveNearMemberRatio(SimSquad squad)
    {
        int participantCount = squad.ParticipantCount;
        if (participantCount <= 0)
            return 0f;
        int nearCount = 0;
        float nearDistanceSqr = nearReleaseDistance * nearReleaseDistance;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            SimEnemy member = squad.Members[i];
            if (member.Alive
                && member.AggroActive
                && HorizontalSqrDistance(
                    member.Position,
                    ResolveEnemyPartyTargetPosition(member)) <= nearDistanceSqr)
                nearCount++;
        }
        return nearCount / (float)participantCount;
    }

    private Vector3 CalculateSquadCenter(SimSquad squad)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            SimEnemy member = squad.Members[i];
            if (!member.Alive || !member.AggroActive)
                continue;
            sum += member.Position;
            count++;
        }
        return count > 0 ? sum / count : squad.SmoothedCenter;
    }

    private SimSquad FindNearestSlotEligibleSquad()
    {
        SimSquad result = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (!IsFarEligibleSquad(squad))
                continue;
            float distance = HorizontalSqrDistance(playerPosition, squad.SmoothedCenter);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                result = squad;
            }
        }
        return result;
    }

    private SimSquad FindSlotOwner(int slotIndex)
    {
        if (slotIndex < 0)
            return null;
        for (int i = 0; i < squads.Count; i++)
        {
            SimSquad squad = squads[i];
            if (squad.ParticipantCount > 0 && squad.SlotIndex == slotIndex && squad.Mode == SquadMode.Pursuit)
                return squad;
        }
        return null;
    }

    private int FindSlotIndexByKind(EnemySquadPursuitSlotKind kind)
    {
        for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            if (slots[slotIndex].Kind == kind)
                return slotIndex;
        }
        return -1;
    }

    private SimEnemy FindEnemy(int id)
    {
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Id == id)
                return enemies[i];
        }
        return null;
    }

    private SimSquad FindSquad(int id)
    {
        for (int i = 0; i < squads.Count; i++)
        {
            if (squads[i].Id == id)
                return squads[i];
        }
        return null;
    }

    private SimEnemy FindEnemyAtCanvasPosition(Rect canvasRect, Vector2 canvasPosition, float maximumPixelDistance)
    {
        SimEnemy result = null;
        float bestDistance = maximumPixelDistance;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            float distance = Vector2.Distance(WorldToCanvas(canvasRect, enemy.Position), canvasPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                result = enemy;
            }
        }
        return result;
    }

    private int FindNearestEnemyIndex(List<SimEnemy> candidates, Vector3 position)
    {
        int result = 0;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            float distance = HorizontalSqrDistance(candidates[i].Position, position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                result = i;
            }
        }
        return result;
    }

    private int CountAliveEnemies()
    {
        int count = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Alive)
                count++;
        }
        return count;
    }

    private int CountAggroEnemies()
    {
        int count = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Alive && enemies[i].AggroActive)
                count++;
        }
        return count;
    }

    private int CountRoamingEnemies()
    {
        int count = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            SimEnemy enemy = enemies[i];
            if (enemy.Alive && !enemy.AggroActive && !enemy.Returning)
                count++;
        }
        return count;
    }

    private int CountReturningEnemies()
    {
        int count = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Alive && enemies[i].Returning)
                count++;
        }
        return count;
    }

    private static string ResolveAggroStateName(SimEnemy enemy)
    {
        if (enemy == null || !enemy.Alive)
            return "Dead";
        if (enemy.AggroActive)
            return enemy.AlertRemaining > 0f ? "Aggro/Alert" : "Aggro";
        return enemy.Returning ? "Return" : "Roam";
    }

    private static string ResolveTargetPhaseShortName(EnemyPartyTargetPhase phase)
    {
        switch (phase)
        {
            case EnemyPartyTargetPhase.LeaderApproach:
                return "L";
            case EnemyPartyTargetPhase.MemberEngaged:
                return "M";
            default:
                return "-";
        }
    }

    private float ResolveMaximumDetectionRange()
    {
        if (simulationDataMode == SimulationDataMode.균일테스트 || overridePrefabAggroRanges)
            return aggroDetectionRange;

        float result = DefaultAggroDetectionRange;
        for (int i = 0; i < aiMinimumData.Count; i++)
            result = Mathf.Max(result, aiMinimumData[i].DetectionRange);
        return result;
    }

    private float ResolveCurrentAggroReleaseDistance()
    {
        return ResolveAggroReleaseDistance(baseAggroReleaseDistance);
    }

    internal static float ResolveAggroReleaseDistance(float distance)
    {
        return Mathf.Max(0f, distance);
    }

    internal static bool ShouldReleaseAggro(
        float targetDistanceSqr,
        float releaseDistance,
        float outsideTime,
        float releaseDelay)
    {
        float distance = Mathf.Max(0f, releaseDistance);
        return targetDistanceSqr > distance * distance
            && outsideTime >= Mathf.Max(0f, releaseDelay);
    }

    private int CountLivingSquads()
    {
        int count = 0;
        for (int i = 0; i < squads.Count; i++)
        {
            if (squads[i].AliveCount > 0)
                count++;
        }
        return count;
    }

    private int CountOccupiedSlots()
    {
        int count = 0;
        for (int i = 0; i < 8; i++)
        {
            if (FindSlotOwner(i) != null)
                count++;
        }
        return count;
    }

    private int CountSquadsInMode(SquadMode mode)
    {
        int count = 0;
        for (int i = 0; i < squads.Count; i++)
        {
            if (squads[i].ParticipantCount > 0 && squads[i].Mode == mode)
                count++;
        }
        return count;
    }

    private void AddEvent(string message)
    {
        eventLines.Insert(0, elapsedSimulationTime.ToString("00.0") + "  " + message);
        if (eventLines.Count > MaximumEventLines)
            eventLines.RemoveAt(eventLines.Count - 1);
    }

    private float NextFloat()
    {
        return random != null ? (float)random.NextDouble() : 0.5f;
    }

    private float NextSignedFloat()
    {
        return NextFloat() * 2f - 1f;
    }

    private float ResolveCanvasScale(Rect rect)
    {
        float viewHalfExtent = Mathf.Max(
            mapHalfExtent,
            baseAggroReleaseDistance + 2f,
            reserveOrbitRadius
                + EnemySquadPursuitPlanner.DefaultReserveRingSpacing
                * (EnemySquadPursuitPlanner.DefaultReserveRingCount - 1)
                + 2f);
        return ResolveViewScale(rect, viewHalfExtent, viewZoom);
    }

    internal static float ResolveViewScale(Rect rect, float viewHalfExtent, float zoom)
    {
        return Mathf.Min(rect.width, rect.height)
            / (Mathf.Max(0.01f, viewHalfExtent) * 2f)
            * Mathf.Clamp(zoom, MinimumViewZoom, MaximumViewZoom);
    }

    private Vector2 WorldToCanvas(Rect rect, Vector3 worldPosition)
    {
        float scale = ResolveCanvasScale(rect);
        return WorldToCanvasPoint(rect, worldPosition, viewCenter, scale);
    }

    internal static Vector2 WorldToCanvasPoint(Rect rect, Vector3 worldPosition, Vector3 center, float scale)
    {
        return new Vector2(
            rect.center.x + (worldPosition.x - center.x) * scale,
            rect.center.y - (worldPosition.z - center.z) * scale);
    }

    private Vector3 CanvasToWorld(Rect rect, Vector2 canvasPosition)
    {
        float scale = Mathf.Max(0.0001f, ResolveCanvasScale(rect));
        return CanvasToWorldPoint(rect, canvasPosition, viewCenter, scale);
    }

    internal static Vector3 CanvasToWorldPoint(Rect rect, Vector2 canvasPosition, Vector3 center, float scale)
    {
        float safeScale = Mathf.Max(0.0001f, scale);
        return new Vector3(
            center.x + (canvasPosition.x - rect.center.x) / safeScale,
            0f,
            center.z - (canvasPosition.y - rect.center.y) / safeScale);
    }

    private Color ResolveSquadVisualColor(SimSquad squad)
    {
        if (squad != null && squad.SlotIndex >= 0 && squad.SlotIndex < slots.Count)
            return ResolveSlotRoleColor(slots[squad.SlotIndex].Kind);
        return UnassignedSquadColor;
    }

    private static int ResolveMovePriority(SimSquad squad)
    {
        if (squad == null)
            return 0;
        return ResolveMovePriorityValue(
            squad.Mode == SquadMode.NearCombat || squad.Mode == SquadMode.Remnant,
            squad.Mode == SquadMode.Rush,
            squad.Mode == SquadMode.Pursuit);
    }

    internal static int ResolveMovePriorityValue(
        bool usesNearCombatPattern,
        bool isRush,
        bool isPursuit)
    {
        if (usesNearCombatPattern)
            return 3;
        if (isRush)
            return 2;
        if (isPursuit)
            return 1;
        return 0;
    }

    internal static bool ShouldUseRemnantPattern(
        bool slotEligibilityRevoked,
        int aliveCount,
        int initialCount,
        float remnantRatio)
    {
        if (aliveCount <= 0)
            return false;
        if (slotEligibilityRevoked)
            return true;
        float aliveRatio = aliveCount / (float)Mathf.Max(1, initialCount);
        return aliveRatio < Mathf.Clamp01(remnantRatio) - 0.0001f;
    }

    private static string ResolveMovePriorityName(int priority)
    {
        switch (priority)
        {
            case 3:
                return "Near/Remnant · 3";
            case 2:
                return "Rush · 2";
            case 1:
                return "Pursuit · 1";
            default:
                return "Reserve/일반 · 0";
        }
    }

    internal static Color ResolveSlotRoleColor(EnemySquadPursuitSlotKind kind)
    {
        return EnemySquadPursuitDebugPalette.ResolveSlotRoleColor(kind);
    }

    private static Color ResolveModeColor(SquadMode mode)
    {
        switch (mode)
        {
            case SquadMode.Pursuit:
                return new Color(0.2f, 0.76f, 1f, 1f);
            case SquadMode.Reserve:
                return new Color(0.72f, 0.42f, 1f, 1f);
            case SquadMode.Rush:
                return new Color(1f, 0.3f, 0.2f, 1f);
            case SquadMode.NearCombat:
                return new Color(1f, 0.56f, 0.22f, 1f);
            case SquadMode.Remnant:
                return new Color(1f, 0.82f, 0.2f, 1f);
            case SquadMode.Dead:
                return DeadColor;
            default:
                return new Color(0.68f, 0.7f, 0.74f, 1f);
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static float HorizontalDistance(Vector3 from, Vector3 to)
    {
        return Mathf.Sqrt(HorizontalSqrDistance(from, to));
    }

    private static float HorizontalSqrDistance(Vector3 from, Vector3 to)
    {
        return Flatten(to - from).sqrMagnitude;
    }

    private static string ResolveModeShortName(SquadMode mode)
    {
        switch (mode)
        {
            case SquadMode.Pursuit:
                return "P";
            case SquadMode.Reserve:
                return "R";
            case SquadMode.Rush:
                return "RU";
            case SquadMode.NearCombat:
                return "N";
            case SquadMode.Remnant:
                return "M";
            case SquadMode.Dead:
                return "D";
            default:
                return "L";
        }
    }
}
