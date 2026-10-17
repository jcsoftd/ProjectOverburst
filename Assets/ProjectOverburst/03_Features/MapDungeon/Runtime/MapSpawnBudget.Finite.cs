using System;
using System.Collections;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.AI;

public sealed partial class MapSpawnBudget
{
    static readonly ProfilerMarker FiniteTotalMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.Total");
    static readonly ProfilerMarker ReconcileMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.Reconcile");
    static readonly ProfilerMarker RefreshMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.Refresh");
    static readonly ProfilerMarker ReinforcementMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.Refresh.Detail.Reinforcement");
    static readonly ProfilerMarker PriorityMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.Priority");
    static readonly ProfilerMarker PlayerAreaMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.PlayerArea");
    static readonly ProfilerMarker PlayerNavMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.PlayerArea.NavMesh");
    static readonly ProfilerMarker PlayerContainsMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.PlayerArea.Containment");
    RunWorldGate runGate;
    int playerAreaFrame = -1, playerAreaIndex = -1;
    Vector3 playerAreaPosition, playerAreaSafe;
    public int CurrentPlayerArea => playerAreaIndex;
    readonly List<MapMonsterField> finiteFields = new List<MapMonsterField>();
    readonly List<MapMonsterField> priorityFields = new List<MapMonsterField>();
    Transform runPlayer;
    Camera runCamera;
    readonly Plane[] viewPlanes = new Plane[6];
    int viewFrame = -1;
    internal int ReservePreparationFrame { get; set; } = -1;
    bool preparingInitial;
    float nextFiniteRefresh;
    int lastFiniteFrame = -1;
    int finiteRefreshField, localCountFrame = -1, localCount;
    bool finiteRefreshing;
    Vector3 localCountPosition;
    public IReadOnlyList<MapMonsterField> Fields => finiteFields;
    public bool InitialReady { get; private set; }
    public int BoundaryRecoveries { get; internal set; }
    public int SpawnRetries { get; internal set; }
    public int StreamReturns { get; internal set; }
    public int InitialPreparedCount { get; private set; }

    public static IEnumerator PrepareRun(RunWorldGate gate, Transform player)
    {
        if (!gate || !gate.IsReady || gate.Roster == null || !player || gate.SpawnBudget)
            throw new InvalidOperationException("고정 명단과 입구가 준비된 새 지도만 배치할 수 있습니다.");
        var root = new GameObject("Map Enemy Services"); root.transform.SetParent(gate.transform, false);
        var inactive = new GameObject("Inactive Enemy Pool"); inactive.transform.SetParent(root.transform, false); inactive.SetActive(false);
        var pool = root.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
        var service = root.AddComponent<EnemySpawnService>(); var theme = MapThemeCatalog.Resolve(gate.Map.monsterThemeId);
        service.Configure(theme.Catalog, pool);
        if (!service.Validate(out var error)) throw new InvalidOperationException(error);
        var budget = root.AddComponent<MapSpawnBudget>(); gate.SpawnBudget = budget;
        budget.service = service; budget.runGate = gate; budget.runPlayer = player;
        budget.runCamera = QuarterViewCamera.ActiveInstance?.GetComponent<Camera>() ?? Camera.main;
        budget.preparingInitial = true;
        var account = AccountGameplaySession.Current;
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        gate.Completion = gate.gameObject.AddComponent<MapRunCompletion>();
        gate.Completion.Configure(gate, player.GetComponent<PlayerActorRuntime>(), service);
        MapEmergencyExit.Prepare(gate, player.GetComponent<PlayerActorRuntime>());
        foreach (var area in gate.Roster.Areas)
        {
            var node = new GameObject("Natural Population " + area.Ground.Index); node.transform.SetParent(root.transform, false);
            node.transform.position = area.Ground.Center;
            var field = node.AddComponent<MapMonsterField>();
            field.Configure(theme, service, gate.Context, gate.Map, definition, account.ContentRegistry, area.Ground.Band, gate.Map.seed + area.Ground.Index);
            field.ConfigureFinite(gate.Roster, area, budget); budget.finiteFields.Add(field);
        }
        int required = 0;
        foreach (var field in budget.finiteFields)
            if (field.NearForInitial(gate.EntryPoint.position)) { field.PrepareAtEntry = true; required += field.FiniteArea.InitialCount; }
        // Initial visible demand is independent of map level, local fight thresholds and minimap markers.
        budget.AliveLimit = Mathf.Min(gate.Roster.Total, Mathf.Max(150, required + 24));
        float until = Time.realtimeSinceStartup + 60;
        try
        {
            while (true)
            {
                budget.TickFinite(gate.EntryPoint.position, true);
                bool ready = true;
                foreach (var field in budget.finiteFields) if (field.PrepareAtEntry && !field.InitialPlaced) { ready = false; break; }
                gate.PreparationStage = "입구 몬스터 배치 " + gate.Roster.Active + " / " + required;
                if (ready) break;
                if (Time.realtimeSinceStartup >= until) throw new InvalidOperationException("입구 몬스터 배치를 완료하지 못했습니다. 지도는 소모하지 않습니다.");
                yield return null;
            }
            budget.InitialPreparedCount = gate.Roster.Active; budget.InitialReady = true;
        }
        finally { budget.preparingInitial = false; }
    }
    void UpdateFinite()
    {
        if (preparingInitial || !InitialReady || !runPlayer) return;
        bool live = WorldSessionState.Phase == WorldPhase.Run && WorldSessionState.ContentScene == gameObject.scene
            && runGate.Context.CanGrantRewards && PersistentSceneFlow.Instance?.IsSwitching != true;
        foreach (var field in finiteFields) field.SetFiniteAwake(live);
        if (live) TickFinite(runPlayer.position, false);
    }
    void TickFinite(Vector3 player, bool preparing)
    {
        if (lastFiniteFrame == Time.frameCount) return;
        using (FiniteTotalMarker.Auto())
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            lastFiniteFrame = Time.frameCount; SpawnedThisFrame = 0; LastSpawnMilliseconds = 0;
            if (!preparing) FindPlayerArea(player, out _);
            using (ReconcileMarker.Auto())
                foreach (var field in finiteFields) field.ReconcileFinite();
            if (preparing || finiteRefreshing || Time.unscaledTime >= nextFiniteRefresh)
            using (RefreshMarker.Auto())
            {
                finiteRefreshing = true;
                while (finiteRefreshField < finiteFields.Count
                    && finiteFields[finiteRefreshField].RefreshFinite(player, preparing, started)) finiteRefreshField++;
                if (finiteRefreshField == finiteFields.Count)
                {
                    using (ReinforcementMarker.Auto())
                    {
                        if (!preparing && playerAreaIndex >= 0)
                        {
                            var field = finiteFields[playerAreaIndex];
                            field.PrepareReinforcement(player, field.FiniteActiveCount, false);
                        }
                    }
                    nextFiniteRefresh = Time.unscaledTime + .1f;
                    finiteRefreshing = false; finiteRefreshField = 0;
                }
            }
            PendingCount = runGate.Roster.Pending;
            bool capacity = service.Pool.LeasedCount + service.Pool.PendingReturnCount < AliveLimit;
            using (PriorityMarker.Auto())
            {
                priorityFields.Clear();
                if (capacity && HasFiniteTime(started))
                {
                    foreach (var field in finiteFields)
                        if (field.HasFiniteSpawnWork && (preparing ? field.PrepareAtEntry : field.NearForInitial(player))
                            && field.HasEligibleFiniteWork(player, preparing))
                        { field.CacheFinitePriority(player); priorityFields.Add(field); }
                    priorityFields.Sort((a, b) => a.FinitePriority.CompareTo(b.FinitePriority));
                }
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            using (SpawnMarker.Auto())
            {
                int attempts = 0;
                while (attempts < MaxPerFrame && ReservePreparationFrame != Time.frameCount && HasFiniteTime(started)
                    && service.Pool.LeasedCount + service.Pool.PendingReturnCount < AliveLimit)
                {
                    bool attempted = false;
                    foreach (var field in priorityFields)
                    {
                        if (!HasFiniteTime(started)) break;
                        if (!field.TrySpawnFinite(player, preparing, started, out bool spawned)) continue;
                        attempted = true; attempts++; if (spawned) SpawnedThisFrame++;
                        break;
                    }
                    LastSpawnMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                    if (!attempted || LastSpawnMilliseconds >= FrameBudgetMilliseconds) break;
                }
            }
            if (!preparing && SpawnedThisFrame == 0 && ReservePreparationFrame != Time.frameCount
                && Time.unscaledDeltaTime < .025f && HasFiniteTime(started)) runGate.LootReserve?.PrepareOne();
        }
    }
    // Completion and reinforcement share the current floor, separate from the last safe completion area.
    internal int FindPlayerArea(Vector3 position, out Vector3 safe)
    {
        if (playerAreaFrame == Time.frameCount && playerAreaPosition == position)
        { safe = playerAreaSafe; return playerAreaIndex; }
        using (PlayerAreaMarker.Auto())
        {
            playerAreaFrame = Time.frameCount; playerAreaPosition = position; playerAreaSafe = default;
            int previous = playerAreaIndex; playerAreaIndex = -1;
            if (runGate && runGate.Roster != null)
            {
                bool onNav; NavMeshHit nav;
                using (PlayerNavMarker.Auto()) onNav = NavMesh.SamplePosition(position, out nav, 1f, NavMesh.AllAreas);
                if (onNav && Mathf.Abs(nav.position.y - position.y) <= .75f)
                using (PlayerContainsMarker.Auto())
                {
                    var areas = runGate.Roster.Areas;
                    if (previous >= 0 && previous < areas.Count && MapCombatGeometry.Contains(areas[previous].Ground, position))
                        playerAreaIndex = previous;
                    else
                        for (int i = 0; i < areas.Count; i++)
                            if (i != previous && MapCombatGeometry.Contains(areas[i].Ground, position))
                            { playerAreaIndex = i; break; }
                    if (playerAreaIndex >= 0) playerAreaSafe = nav.position;
                }
            }
            safe = playerAreaSafe; return playerAreaIndex;
        }
    }
    internal bool AllowsReinforcement(int areaIndex, Vector3 player) => FindPlayerArea(player, out _) == areaIndex;
    public bool InView(Vector3 point, float padding = .12f, float radius = .5f, float height = 2)
    {
        if (!runCamera) return true;
        if (viewFrame != Time.frameCount) { GeometryUtility.CalculateFrustumPlanes(runCamera, viewPlanes); viewFrame = Time.frameCount; }
        float margin = Mathf.Max(2, QuarterViewCamera.ActiveInstance ? QuarterViewCamera.ActiveInstance.EffectiveDistance : 20) * padding;
        var bounds = new Bounds(point + Vector3.up * height * .5f,
            new Vector3(radius * 2, height, radius * 2) + Vector3.one * (margin * 2));
        return GeometryUtility.TestPlanesAABB(viewPlanes, bounds);
    }
    internal int LocalAlive(Vector3 player)
    {
        if (localCountFrame != Time.frameCount || localCountPosition != player)
        {
            localCount = 0; foreach (var field in finiteFields) localCount += field.CountNear(player, 24);
            localCountFrame = Time.frameCount; localCountPosition = player;
        }
        return localCount;
    }
    internal void InvalidateFiniteCounts() => localCountFrame = -1;
    internal static bool HasFiniteTime(long started) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency < FrameBudgetMilliseconds;
    internal bool IsSeparated(Vector3 point, float radius)
    {
        if (!preparingInitial && runPlayer && Vector3.Distance(point, runPlayer.position) < radius + 3) return false;
        foreach (var field in finiteFields) if (!field.IsSeparated(point, radius)) return false;
        return true;
    }
}
