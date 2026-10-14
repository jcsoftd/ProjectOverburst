using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.DebugTools.Performance;
using Overburst.Mojave;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

public static class DesertPerformanceProbe
{
    public sealed class Visit
    {
        public int area, lap, point;
        public string name, role, status = "PLANNED", reason;
        public Vector3 target, reached;
        public double distance;
    }
    public sealed class Segment
    {
        public string label, kind;
        public int area, lap, point;
        public CombatPerformanceSegment performance;
        public DesertPerformanceDetails.Analysis analysis;
        public object workloadStart, workloadEnd;
    }
    public sealed class Result
    {
        public int schema = 5;
        public string status = "INTERRUPTED", reason, candidate, scene, startedUtc, theme, preparationStage;
        public int seed, level, expectedAreas, visitedAreas, entryFrame, total, initial, roadMembers, errors;
        public double entrySeconds, preparationSeconds;
        public object environment;
        public object stressReference;
        public string routeContract = "Combat-area center and configured internal points only. Outside-area transit and entry are recorded separately. Primary samples: VALID travel/dwell, combat_area >= 0, input_blocked == 0. Travel with fewer than two samples is SHORT_TRAVEL: raw evidence retained, excluded from primary distributions. Owned player excludes Enemy body collisions; terrain collisions and natural AI retained. Compact progress is written per visit; full detailed map is written once on completion/cancellation.";
        public DesertPerformanceProfile profile;
        public string observationContract = "Workload is sampled in the early Editor callback. Latest completed profiler samples can lag. Actor/camera/geometry census and report generation occur outside sampling. Candidate IDs are static audit references; no causal or performance acceptance is inferred.";
        public List<Visit> visits = new List<Visit>();
        public List<Segment> segments = new List<Segment>();
        public List<object> entryStages = new List<object>();
    }
    struct PositionSample
    {
        public int frame, active, pending, total, killed, aliveLimit, spawned, retries, recoveries, streamReturns;
        public int poolCreated, poolLeased, poolAvailable, poolPending, globalAi, width, height, inputBlocked, combatArea;
        public Vector3 position; public Vector2 input; public float scale; public double spawnAttemptMs;
    }
    static CombatPerformanceRecorder recorder;
    static PositionSample[] positions;
    static int positionCount;
    static long observerBegan;
    static PlayerActorRuntime player;
    static RunWorldGate gate;
    static Vector2 queuedInput;
    static string segmentFolder;
    static Result result;
    static int width, height;
    static bool environmentChanged;
    static EnemyPoolService pool;
    static MojavePlace[] combatPlaces;

    static int CombatAreaAt(Vector3 position)
    {
        if (combatPlaces == null) return -1;
        var point = new Vector2(position.x, position.z);
        // Reuse the same authored boundary as MapCombatGeometry.Source.Floor without per-frame physics queries.
        for (int i = 0; i < combatPlaces.Length; i++)
            if (combatPlaces[i].Distance(point) <= 0) return i;
        return -1;
    }

    static void BindPool()
    {
        if (pool || !gate || !gate.SpawnBudget) return;
        if (gate.SpawnBudget.TryGetComponent<EnemySpawnService>(out var service)) pool = service.Pool;
    }
    static object Workload()
    {
        BindPool(); var budget = gate ? gate.SpawnBudget : null; var roster = gate ? gate.Roster : null;
        return new { frame = Time.frameCount, total = roster?.Total ?? -1, active = roster?.Active ?? -1, pending = roster?.Pending ?? -1, killed = roster?.Kills ?? -1,
            fields = budget ? budget.Fields.Count : -1, aliveLimit = budget ? budget.AliveLimit : -1, initialPrepared = budget ? budget.InitialPreparedCount : -1,
            spawnedLastCompletedUpdate = budget ? budget.SpawnedThisFrame : -1, spawnAttemptMs = budget ? budget.LastSpawnMilliseconds : -1,
            retries = budget ? budget.SpawnRetries : -1, boundaryRecoveries = budget ? budget.BoundaryRecoveries : -1, streamReturns = budget ? budget.StreamReturns : -1,
            poolCreated = pool ? pool.CreatedCount : -1, poolLeased = pool ? pool.LeasedCount : -1, poolAvailable = pool ? pool.AvailableCount : -1,
            poolPendingReturn = pool ? pool.PendingReturnCount : -1, globalRegisteredAi = EnemyAIController.ActiveEnemyCount,
            budgetContract = "2ms/2 attempts covers spawn attempts only. Total/Reconcile/Refresh/Priority markers measure excluded finite maintenance; nested times must not be summed." };
    }

    // Called once per distinct rendered Play frame. No scene search or file writing while sampling.
    public static void Tick()
    {
        observerBegan = Stopwatch.GetTimestamp();
        if (recorder == null) return;
        BindPool(); var budget = gate ? gate.SpawnBudget : null; var roster = gate ? gate.Roster : null;
        recorder.Tick(); recorder.Observe(gate && gate.Roster != null ? gate.Roster.Active : 0);
        if (player && positionCount < positions.Length)
            positions[positionCount++] = new PositionSample { frame = Time.frameCount, position = player.transform.position,
                input = queuedInput, active = gate && gate.Roster != null ? gate.Roster.Active : 0,
                pending = roster?.Pending ?? -1, total = roster?.Total ?? -1, killed = roster?.Kills ?? -1,
                aliveLimit = budget ? budget.AliveLimit : -1, spawned = budget ? budget.SpawnedThisFrame : -1,
                spawnAttemptMs = budget ? budget.LastSpawnMilliseconds : -1, retries = budget ? budget.SpawnRetries : -1,
                recoveries = budget ? budget.BoundaryRecoveries : -1, streamReturns = budget ? budget.StreamReturns : -1,
                poolCreated = pool ? pool.CreatedCount : -1, poolLeased = pool ? pool.LeasedCount : -1,
                poolAvailable = pool ? pool.AvailableCount : -1, poolPending = pool ? pool.PendingReturnCount : -1,
                globalAi = EnemyAIController.ActiveEnemyCount, width = Screen.width, height = Screen.height,
                inputBlocked = GameplayInputBlocker.IsGameplayInputBlocked ? 1 : 0, combatArea = CombatAreaAt(player.transform.position), scale = Time.timeScale };
        if (Screen.width != width || Screen.height != height || Math.Abs(Time.timeScale - 1) > .001f) environmentChanged = true;
    }
    public static void EndTick()
    { if (recorder != null) recorder.AddObserverTicks(observerBegan); }
    static void Require(bool condition, string reason)
    { if (!condition) { if (result != null) result.reason = reason; throw new InvalidOperationException(reason); } }
    static double Now => Time.realtimeSinceStartupAsDouble;
    static IEnumerator Delay(float seconds)
    { double end = Now + seconds; while (Now < end) yield return null; }
    sealed class PositionConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(Vector3);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        { var p = (Vector3)value; writer.WriteStartObject(); writer.WritePropertyName("x"); writer.WriteValue(p.x); writer.WritePropertyName("y"); writer.WriteValue(p.y); writer.WritePropertyName("z"); writer.WriteValue(p.z); writer.WriteEndObject(); }
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        { var p = JObject.Load(reader); return new Vector3((float)p["x"], (float)p["y"], (float)p["z"]); }
    }
    public static string Serialize(object data) => JsonConvert.SerializeObject(data, Formatting.Indented, new PositionConverter());
    static void Write(string path, object data) { File.WriteAllText(path, Serialize(data)); }
    static void BeginSegment(string output, DesertPerformanceProfile profile, string kind, int area = -1, int lap = 0, int point = -1)
    {
        Require(recorder == null, "측정 구간 중첩");
        segmentFolder = Path.Combine(output, "Segments", result.segments.Count.ToString("D4") + "_" + kind);
        var value = new CombatPerformanceSegment { key = kind + "/" + area + "/" + lap + "/" + point, folder = segmentFolder,
            scenario = kind, inputMode = "VirtualGamepad -> production PlayerMovement; player damage immunity; natural AI retained",
            theme = profile.theme, repetition = lap, aliveAtStart = gate && gate.Roster != null ? gate.Roster.Active : 0 };
        positions = new PositionSample[profile.frameCapacity]; positionCount = 0; width = Screen.width; height = Screen.height; environmentChanged = false;
        var startWorkload = Workload();
        recorder = new CombatPerformanceRecorder(new CombatPerformanceProfile { frameCapacity = profile.frameCapacity, eventCapacity = 128 }, value, null, 256, true);
        result.segments.Add(new Segment { label = value.key, kind = kind, area = area, lap = lap, point = point, performance = value, workloadStart = startWorkload });
    }
    static void EndSegment()
    {
        if (recorder == null) return;
        var owned = recorder; recorder = null;
        try
        {
            owned.Result.aliveAtEnd = gate && gate.Roster != null ? gate.Roster.Active : 0;
            owned.End(segmentFolder);
            if (environmentChanged) { owned.Result.validity = "ENVIRONMENT_CHANGED"; owned.Result.reason = "해상도 또는 timeScale 변경"; }
            if (positionCount >= positions.Length) { owned.Result.validity = "INCOMPLETE"; owned.Result.reason = "좌표 기록 버퍼 포화"; }
            var segment = result.segments[result.segments.Count - 1];
            if (!environmentChanged && positionCount < positions.Length && IsShortTravel(segment.kind, owned.Result))
            { owned.Result.validity = "SHORT_TRAVEL"; owned.Result.reason = "짧은 실제 이동으로 프레임 표본이 두 개 미만입니다. 원본은 보존하고 주요 분포에서 제외합니다."; }
            Write(Path.Combine(segmentFolder, "segment.json"), owned.Result);
            using (var writer = new StreamWriter(Path.Combine(segmentFolder, "positions.csv")))
            {
                writer.WriteLine("frame,x,y,z,input_x,input_y,active,pending,time_scale,total,killed,alive_limit,spawned_last_update,spawn_attempt_ms,retries_total,boundary_recoveries_total,stream_returns_total,pool_created_total,pool_leased,pool_available,pool_pending_return,ai_registered_global,width,height,input_blocked,combat_area");
                for (int i = 0; i < positionCount; i++)
                {
                    var f = positions[i];
                    writer.WriteLine(string.Join(",", new[] { N(f.frame), N(f.position.x), N(f.position.y), N(f.position.z),
                        N(f.input.x), N(f.input.y), N(f.active), N(f.pending), N(f.scale), N(f.total), N(f.killed), N(f.aliveLimit), N(f.spawned),
                        N(f.spawnAttemptMs), N(f.retries), N(f.recoveries), N(f.streamReturns), N(f.poolCreated), N(f.poolLeased), N(f.poolAvailable),
                        N(f.poolPending), N(f.globalAi), N(f.width), N(f.height), N(f.inputBlocked), N(f.combatArea) }));
                }
            }
            segment.workloadEnd = Workload();
            segment.analysis = DesertPerformanceDetails.Analyze(segmentFolder, result.profile.targetFps, owned.Result.memoryEndBytes - owned.Result.memoryStartBytes);
            Write(Path.Combine(segmentFolder, "workload.json"), new { start = segment.workloadStart, end = segment.workloadEnd });
        }
        finally { owned.Dispose(); positions = null; }
    }
    static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    public static bool IsShortTravel(string kind, CombatPerformanceSegment performance)
        => kind == "travel" && performance.frames >= 0 && performance.frames < 2 && performance.validity == "INCOMPLETE"
            && !performance.capacityReached && performance.droppedEvents == 0;
    static void Queue(Gamepad pad, Vector2 stick)
    { queuedInput = stick; InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = stick }); }

    public static IEnumerator Verify(MapInstanceState map, string itemId, string output, DesertPerformanceProfile profile)
    {
        profile.Validate();
        Require(recorder == null, "기존 사막 측정이 실행 중입니다.");
        player = PlayerContext.Instance.CurrentActor;
        var input = player.GetComponent<PlayerInputFacade>(); var movement = player.GetComponent<PlayerMovement>();
        var controller = player.GetComponent<CharacterController>();
        int enemyMask = LayerMask.GetMask("Enemy");
        Require(controller && enemyMask != 0, "플레이어 충돌체/Enemy 레이어가 없습니다.");
        int oldExcludedLayers = controller.excludeLayers.value;
        Require(input && movement && input.TryGetGameplayAction("Move", out var moveAction)
            && moveAction.bindings.Any(b => b.effectivePath == "<Gamepad>/leftStick"), "가상 게임패드 이동 바인딩이 없습니다.");
        var flow = PersistentSceneFlow.Instance;
        Gamepad pad = null;
        ReadOnlyArray<InputDevice>? oldDevices = input.GameplayMap.devices;
        bool oldEnabled = input.IsGameplayEnabled;
        int oldVsync = QualitySettings.vSyncCount, oldTarget = Application.targetFrameRate;
        float oldScale = Time.timeScale;
        result = new Result { candidate = map.desertCandidateId, scene = MapTerrainCatalog.SceneFor(map), seed = map.seed,
            level = map.level, theme = map.monsterThemeId, startedUtc = DateTime.UtcNow.ToString("O"), profile = profile };
        gate = null; pool = null; combatPlaces = null;
        void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) result.errors++; }
        Application.logMessageReceived += OnLog;
        try
        {
            if (profile.uncapped) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
            Time.timeScale = 1;
            controller.excludeLayers = oldExcludedLayers | enemyMask;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MapSpawnBudget).TypeHandle);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(EnemySquadPursuitRuntimeService).TypeHandle);
            result.environment = new { origin = "Editor", unity = Application.unityVersion, cpu = SystemInfo.processorType,
                gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), os = SystemInfo.operatingSystem,
                width = Screen.width, height = Screen.height, quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                vSync = QualitySettings.vSyncCount, targetFps = Application.targetFrameRate,
                pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "BuiltIn",
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), profilerEnabled = UnityEngine.Profiling.Profiler.enabled,
                deepProfiling = UnityEditorInternal.ProfilerDriver.deepProfiling, editorCameras = DesertPerformanceDetails.EditorCameraContext(),
                metricCapacity = 256, enemyBodyPassThrough = true, oldExcludedLayers, measuredExcludedLayers = controller.excludeLayers.value,
                safety = "Player immunity and Enemy body pass-through; terrain collision and natural AI retained. No attacks/teleport. Boss intro recorded separately." };
            BeginSegment(output, profile, "entry"); result.entryFrame = Time.frameCount;
            double entryBegan = Now, end = Now + profile.entryTimeout; string stage = null; int inspectFrame = -100;
            Require(flow.EnterMapRun(map, itemId), "지도 입장 요청이 거절됐습니다.");
            while (flow.IsSwitching && Now < end)
            {
                if (Time.frameCount - inspectFrame >= 30)
                {
                    inspectFrame = Time.frameCount;
                    if (!gate) gate = UnityEngine.Object.FindFirstObjectByType<RunWorldGate>();
                    string next = gate ? gate.PreparationStage : "Scene loading";
                    if (next != stage) { stage = next; result.entryStages.Add(new { frame = Time.frameCount, seconds = Now - entryBegan, stage }); }
                }
                Require(!recorder.CapacityReached, "입장 기록 버퍼 포화"); yield return null;
            }
            result.entrySeconds = Now - entryBegan; result.preparationStage = stage;
            EndSegment();
            Require(!flow.IsSwitching && flow.RunEntryError == null && WorldSessionState.Phase == WorldPhase.Run,
                "입장 실패/제한시간: " + flow.RunEntryError + " / " + stage);
            gate = WorldSessionState.ContentScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RunWorldGate>()).Single();
            Require(gate.IsReady && gate.Roster != null && gate.Roster.PlacementReady && gate.Roster.Areas.Count == map.areaCount, "플랫폼 명단/준비 불일치");
            var world = gate.GetComponent<DesertRunWorld>(); var authored = gate.GetComponent<MojaveWorld>(); authored.EnsureLayout();
            combatPlaces = authored.layout.places.ToArray();
            if (profile.monsterMultiplier > 1)
            {
                var baselineMap = ItemSnapshotCodec.CopyValues(map);
                baselineMap.options.RemoveAll(o => o.optionId == MapOptionPolicy.MoreSmall || o.optionId == MapOptionPolicy.MoreMedium || o.optionId == MapOptionPolicy.MoreElite);
                var ground = gate.Roster.Areas.Select(a => a.Ground).ToArray();
                int boss = Array.FindIndex(ground, a => authored.layout.places[a.Index].role == MojaveTileRole.Boss);
                var baseline = MapRunRoster.Build("performance-stress-reference", baselineMap, ground, boss, gate.Roster.EntryAreaIndex);
                Require(gate.Roster.Total == baseline.Total * profile.monsterMultiplier, "스트레스 명단 수량 배수가 일치하지 않습니다.");
                int required = baseline.Areas.Where(a => gate.SpawnBudget.Fields.Any(f => f.FiniteArea.Ground.Index == a.Ground.Index && f.PrepareAtEntry)).Sum(a => a.InitialCount);
                int baselineLimit = Math.Min(baseline.Total, Math.Max(150, required + 24));
                int priorLimit = gate.SpawnBudget.AliveLimit, stressLimit = Math.Min(gate.Roster.Total, baselineLimit * profile.monsterMultiplier);
                // Owned stress fixture: raise this runtime instance's cap, leaving product policy/defaults unchanged.
                typeof(MapSpawnBudget).GetProperty(nameof(MapSpawnBudget.AliveLimit)).GetSetMethod(true).Invoke(gate.SpawnBudget, new object[] { stressLimit });
                result.stressReference = new { multiplier = profile.monsterMultiplier, baselineTotal = baseline.Total, requestedTotal = baseline.Total * profile.monsterMultiplier, actualTotal = gate.Roster.Total,
                    baselineInitial = baseline.Members.Count(m => m.Initial), actualInitial = gate.Roster.Members.Count(m => m.Initial),
                    baselineLimit, priorLimit, stressLimit, condition = "Owned isolated small/medium/elite quantities and active cap scaled by requested multiplier; actual active/pending/created values recorded, not assumed" };
            }
            result.preparationSeconds = world.PreparationSeconds; result.total = gate.Roster.Total;
            result.initial = gate.Roster.Members.Count(m => m.Initial); result.roadMembers = gate.Roster.Members.Count(m => m.PlannedOnRoad);
            result.expectedAreas = profile.maximumAreas == 0 ? gate.Roster.Areas.Count : Math.Min(profile.maximumAreas, gate.Roster.Areas.Count);
            var plan = BuildVisits(gate, authored, profile);
            result.visits.AddRange(plan);
            Write(Path.Combine(output, "route-plan.json"), plan);
            Write(Path.Combine(output, "roster.json"), gate.Roster.Members.Select(m => new { m.Id, m.Index, m.AreaIndex, definition = m.Definition.EnemyId, m.Initial, m.PlannedOnRoad, m.PlannedPosition }));
            pad = InputSystem.AddDevice<Gamepad>("OverburstDesertPerformanceGamepad");
            input.GameplayMap.devices = new InputDevice[] { pad }; input.EnableGameplay();
            player.Health.SetDamageImmunity(flow, true);
            yield return Delay(profile.warmupSeconds);
            foreach (var visit in plan)
            {
                BeginSegment(output, profile, "travel", visit.area, visit.lap, visit.point);
                try { yield return WalkTo(visit, movement, pad, profile); }
                finally { Queue(pad, Vector2.zero); EndSegment(); }
                visit.reached = player.transform.position;
                // Room entry may trigger a production boss cinematic on the next Player frame.
                yield return null;
                if (GameplayInputBlocker.IsGameplayInputBlocked)
                {
                    BeginSegment(output, profile, "cinematic", visit.area, visit.lap, visit.point);
                    try
                    {
                        double cinematicEnd = Now + 90;
                        while (GameplayInputBlocker.IsGameplayInputBlocked && Now < cinematicEnd) yield return null;
                        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "보스 연출 종료 제한시간");
                    }
                    finally { EndSegment(); }
                }
                yield return Delay(profile.warmupSeconds);
                BeginSegment(output, profile, "dwell", visit.area, visit.lap, visit.point);
                try { yield return Delay(profile.dwellSeconds); }
                finally { EndSegment(); }
                Require(!GameplayInputBlocker.IsGameplayInputBlocked, "플랫폼 도착 후 입력 차단이 남았습니다: " + visit.name);
                visit.status = "VISITED";
                Write(Path.Combine(output, "actors_" + visit.area + "_" + visit.lap + "_" + visit.point + ".json"), DesertPerformanceDetails.ActorSnapshot(gate, player.transform.position));
                if (visit.point == 0)
                {
                    Write(Path.Combine(output, "census_" + visit.area + "_" + visit.lap + ".json"), Census(WorldSessionState.ContentScene, player.transform.position));
                    if (profile.screenshots) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "area_" + visit.area + "_" + visit.lap + ".png")); yield return Delay(.25f); }
                }
                Write(Path.Combine(output, "progress.json"), new { result.schema, status = "RUNNING", result.candidate, result.startedUtc,
                    result.profile, result.expectedAreas, result.total, result.errors, result.visits,
                    segments = result.segments.Select(s => new { s.label, s.kind, s.area, s.lap, s.point, s.workloadEnd,
                        performance = new { s.performance.frames, s.performance.validity, s.performance.reason } }).ToArray() });
            }
            result.visitedAreas = plan.Where(v => v.status == "VISITED" && v.area >= 0).Select(v => v.area).Distinct().Count();
            Require(plan.All(v => v.status == "VISITED") && result.visitedAreas == result.expectedAreas, "플랫폼 방문 누락");
            Require(result.errors == 0, "실행 중 Unity 오류가 발생했습니다.");
            Require(result.segments.All(s => s.performance.validity == "VALID" || s.performance.validity == "SHORT_TRAVEL"), "신뢰 불충분한 측정 구간이 있습니다.");
            Queue(pad, Vector2.zero);
            flow.GetComponent<RunLifetimeDriver>().RequestAbandon();
            end = Now + 120;
            while ((!WorldSessionState.IsHideout || flow.IsSwitching) && Now < end) yield return null;
            Require(WorldSessionState.IsHideout && !flow.IsSwitching, "측정 후 마을 반환 제한시간");
            result.status = profile.maximumAreas > 0 ? "COMPLETE_LIMITED" : "COMPLETE";
        }
        finally
        {
            // IEnumerator disposal on cancellation/failure uses the same cleanup as successful traversal.
            try { EndSegment(); }
            finally
            {
                Application.logMessageReceived -= OnLog;
                if (pad != null) { Queue(pad, Vector2.zero); InputSystem.RemoveDevice(pad); }
                if (input) { input.GameplayMap.devices = oldDevices; if (oldEnabled) input.EnableGameplay(); else input.DisableGameplay(); }
                if (player) { player.Health.SetDamageImmunity(flow, false); if (player.TryGetComponent<PlayerMovementInputSource>(out var source)) source.Clear(); }
                if (controller) controller.excludeLayers = oldExcludedLayers;
                QualitySettings.vSyncCount = oldVsync; Application.targetFrameRate = oldTarget; Time.timeScale = oldScale;
                bool devicesRestored = input && input.GameplayMap.devices.HasValue == oldDevices.HasValue
                    && (!oldDevices.HasValue || input.GameplayMap.devices.Value.SequenceEqual(oldDevices.Value));
                bool collisionRestored = controller && controller.excludeLayers.value == oldExcludedLayers;
                bool restored = collisionRestored && devicesRestored && input.IsGameplayEnabled == oldEnabled && (pad == null || !InputSystem.devices.Contains(pad))
                    && QualitySettings.vSyncCount == oldVsync && Application.targetFrameRate == oldTarget && Time.timeScale == oldScale;
                Write(Path.Combine(output, "probe-return.json"), new { status = restored ? "PASS" : "FAIL", devicesRestored,
                    collisionRestored, oldExcludedLayers, returnedExcludedLayers = controller ? controller.excludeLayers.value : -1,
                    virtualPadRemoved = pad == null || !InputSystem.devices.Contains(pad), vSync = QualitySettings.vSyncCount, target = Application.targetFrameRate, timeScale = Time.timeScale });
                result.visitedAreas = result.visits.Where(v => v.status == "VISITED" && v.area >= 0).Select(v => v.area).Distinct().Count();
                Write(Path.Combine(output, "map.json"), result);
                DesertPerformanceReport.WriteMap(output, result);
                player = null; gate = null; pool = null; combatPlaces = null; queuedInput = Vector2.zero;
            }
        }
    }

    public static List<Visit> BuildVisits(RunWorldGate owner, MojaveWorld authored, DesertPerformanceProfile profile)
    {
        var visits = new List<Visit>();
        var areas = owner.Roster.Areas.OrderBy(a => authored.layout.places[a.Ground.Index].role == MojaveTileRole.Boss ? 1 : 0).ThenBy(a => a.Ground.Index);
        var selected = (profile.maximumAreas > 0 ? areas.Take(profile.maximumAreas) : areas).ToArray();
        for (int lap = 0; lap < profile.repetitions; lap++)
            foreach (var area in selected)
            {
                var ground = area.Ground; var place = authored.layout.places[ground.Index];
                Vector3 center = Floor(ground.Center);
                Require(MapCombatGeometry.Contains(ground, center), "중심 목적지가 전투영역 밖입니다: " + ground.Name);
                visits.Add(new Visit { area = ground.Index, lap = lap, point = 0, name = ground.Name, role = place.role.ToString(), target = center });
                for (int p = 1; p < profile.pointsPerArea; p++)
                {
                    float angle = (p - 1) * Mathf.PI * 2 / (profile.pointsPerArea - 1);
                    Vector2 local = new Vector2(Mathf.Cos(angle) * place.radius.x * .55f, Mathf.Sin(angle) * place.radius.y * .55f);
                    var desired = place.World(local);
                    // Connected physical floor points retain authored irregular platform shapes and rock clearance.
                    Vector3 target = ground.Points.OrderBy(v => (new Vector2(v.x, v.z) - desired).sqrMagnitude).Select(v => FloorOrNull(v)).FirstOrDefault(v => v.HasValue)
                        ?? throw new InvalidOperationException("플랫폼 이동 표본을 찾지 못했습니다: " + ground.Name);
                    Require(MapCombatGeometry.Contains(ground, target), "이동 표본이 플랫폼 밖입니다: " + ground.Name);
                    visits.Add(new Visit { area = ground.Index, lap = lap, point = p, name = ground.Name, role = place.role.ToString(), target = target });
                }
            }
        return visits;
    }
    static Vector3? FloorOrNull(Vector3 p)
        => NavMesh.SamplePosition(p, out var hit, 1.25f, NavMesh.AllAreas) && Math.Abs(hit.position.y - p.y) < .75f ? hit.position : (Vector3?)null;
    static Vector3 Floor(Vector3 p) => FloorOrNull(p) ?? throw new InvalidOperationException("이동 지면이 없습니다: " + p);
    static float Planar(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    static IEnumerator WalkTo(Visit visit, PlayerMovement movement, Gamepad pad, DesertPerformanceProfile profile)
    {
        var path = new NavMeshPath();
        Require(NavMesh.CalculatePath(Floor(player.transform.position), visit.target, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete,
            "플랫폼 경로가 끊겼습니다: " + visit.name);
        var corners = path.corners; double end = Now + profile.travelTimeout, progressAt = Now;
        Vector3 last = player.transform.position; float best = float.PositiveInfinity;
        visit.status = "TRAVELLING";
        for (int c = 1; c < corners.Length; c++)
        {
            best = float.PositiveInfinity; progressAt = Now;
            while (Planar(player.transform.position, corners[c]) > (c == corners.Length - 1 ? .65f : .15f))
            {
                Vector3 current = player.transform.position; visit.distance += Planar(current, last); last = current;
                float remaining = Planar(current, corners[c]);
                if (remaining + .15f < best) { best = remaining; progressAt = Now; }
                if (GameplayInputBlocker.IsGameplayInputBlocked)
                {
                    Queue(pad, Vector2.zero); progressAt = Now;
                    Require(Now < end, "컷신/입력 차단 제한시간: " + visit.name);
                    yield return null; continue;
                }
                if (Now >= end || Now - progressAt >= profile.stallSeconds)
                {
                    EndSegment(); // Failure-only physics census must not enter performance samples.
                    CaptureMovementStall(visit, movement, corners, c);
                }
                Require(Now < end && Now - progressAt < profile.stallSeconds,
                    "이동 정체: " + visit.name + " / 위치=" + current + " / 목표=" + corners[c]);
                Require(!recorder.CapacityReached, "이동 프레임 버퍼 포화");
                var delta = corners[c] - current; delta.y = 0; delta.Normalize();
                Vector3 right = movement.ResolveMoveDirection(Vector2.right), forward = movement.ResolveMoveDirection(Vector2.up);
                Queue(pad, Vector2.ClampMagnitude(new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, forward)), 1));
                yield return null;
            }
        }
        Queue(pad, Vector2.zero);
        Require(Planar(player.transform.position, visit.target) <= 1.25f, "이동 종료 위치 불일치: " + visit.name);
        Require(CombatAreaAt(player.transform.position) == visit.area, "실제 도착 위치가 목표 전투영역 밖입니다: " + visit.name);
    }
    static void CaptureMovementStall(Visit visit, PlayerMovement movement, Vector3[] corners, int corner)
    {
        var controller = movement.GetComponent<CharacterController>();
        Vector3 current = movement.transform.position, direction = corners[corner] - current; direction.y = 0; direction.Normalize();
        string Hierarchy(Transform t) { string name = t.name; while (t.parent) { t = t.parent; name = t.name + "/" + name; } return name; }
        object Describe(Collider collider) => new { path = Hierarchy(collider.transform), type = collider.GetType().Name,
            layer = LayerMask.LayerToName(collider.gameObject.layer), collider.isTrigger, boundsCenter = collider.bounds.center,
            boundsSize = collider.bounds.size, scene = collider.gameObject.scene.name };
        Vector3 center = controller.transform.TransformPoint(controller.center);
        float radius = controller.radius * Mathf.Max(Mathf.Abs(controller.transform.lossyScale.x), Mathf.Abs(controller.transform.lossyScale.z));
        float half = Mathf.Max(0, controller.height * Mathf.Abs(controller.transform.lossyScale.y) / 2 - radius);
        Vector3 bottom = center - Vector3.up * half, top = center + Vector3.up * half;
        var cast = Physics.CapsuleCastAll(bottom + Vector3.up * .05f, top, radius * .98f, direction, 2, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => !h.collider.transform.IsChildOf(player.transform)).OrderBy(h => h.distance)
            .Select(h => new { collider = Describe(h.collider), h.distance, h.point, h.normal }).ToArray();
        var overlap = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore)
            .Where(c => !c.transform.IsChildOf(player.transform)).Select(Describe).ToArray();
        var floor = Enumerable.Range(0, 5).Select(i => { Vector3 at = current + direction * (i * .5f);
            return new { at, hits = Physics.RaycastAll(at + Vector3.up * 3, Vector3.down, 8, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => !h.collider.transform.IsChildOf(player.transform)).OrderBy(h => h.distance)
                .Select(h => new { collider = Describe(h.collider), h.point, h.normal, h.distance }).ToArray() }; }).ToArray();
        var intent = movement.ActiveMovementIntent;
        Write(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(segmentFolder)), "movement-stall-" + visit.area + "-" + visit.point + ".json"),
            new { current, visit, corners, corner, direction, input = new { x = queuedInput.x, y = queuedInput.y }, movement.IsGrounded,
                movement.GroundNormal, movement.IsConditionMovementBlocked, movement.IsKnockedDown, movement.IsEvading, movement.IsMeleeAttackMoveLocked,
                moveInput = new { x = movement.MoveInput.x, y = movement.MoveInput.y }, movement.MoveDirection,
                shouldMove = intent.ShouldMove, intentDirection = intent.DesiredDirection, speed = movement.BaseMoveSpeed,
                controller = new { controller.radius, controller.height, controller.stepOffset, controller.slopeLimit, controller.skinWidth, controller.collisionFlags }, cast, overlap, floor });
    }
    static object Census(UnityEngine.SceneManagement.Scene scene, Vector3 at)
    {
        var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToArray();
        var camera = Camera.main; var planes = camera ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
        var visible = renderers.Where(r => planes != null && !r.forceRenderingOff && GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToArray();
        string Hierarchy(Transform t) { string name = t.name; while (t.parent) { t = t.parent; name = t.name + "/" + name; } return name; }
        return new { at, renderers = renderers.Length, frustumCandidates = visible.Length,
            estimate = "Frustum candidates include occluded objects and all LOD renderers; not actual draw calls/overdraw. Census is outside sampling.",
            withoutLod = visible.Count(r => !r.GetComponentInParent<LODGroup>()), shadowCasters = visible.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off),
            transparent = visible.Count(r => r.sharedMaterials.Any(m => m && m.renderQueue >= 3000)),
            materials = visible.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count(),
            lights = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>()).Count(l => l.enabled),
            colliders = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>()).Count(c => c.enabled),
            particles = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ParticleSystem>()).Sum(p => p.particleCount),
            terrains = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>()).Where(t => t.terrainData).Select(t => new {
                path = Hierarchy(t.transform), t.drawInstanced, t.heightmapPixelError, t.basemapDistance, t.detailObjectDistance, t.detailObjectDensity,
                t.treeDistance, t.treeBillboardDistance, heightmapResolution = t.terrainData.heightmapResolution, size = t.terrainData.size,
                terrainLayers = t.terrainData.terrainLayers.Length }),
            largestMeshes = visible.OrderByDescending(CountTriangles).Take(15).Select(r => new { path = Hierarchy(r.transform), triangles = CountTriangles(r),
                materialSlots = r.sharedMaterials.Length, distance = Vector3.Distance(at, r.bounds.center), hasLod = (bool)r.GetComponentInParent<LODGroup>() }) };
    }
    public static long CountTriangles(Renderer renderer)
    {
        Mesh mesh = null;
        if (renderer is SkinnedMeshRenderer skin) mesh = skin.sharedMesh;
        else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
        long count = 0;
        if (mesh) for (int s = 0; s < mesh.subMeshCount; s++) if (mesh.GetTopology(s) == MeshTopology.Triangles) count += mesh.GetIndexCount(s) / 3;
        return count;
    }
}
