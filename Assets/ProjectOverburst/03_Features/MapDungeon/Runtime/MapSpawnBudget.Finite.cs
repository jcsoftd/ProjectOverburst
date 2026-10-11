using System;
using System.Collections;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

public sealed partial class MapSpawnBudget
{
    RunWorldGate runGate;
    readonly List<MapMonsterField> finiteFields = new List<MapMonsterField>();
    readonly List<MapMonsterField> priorityFields = new List<MapMonsterField>();
    Transform runPlayer;
    Camera runCamera;
    readonly Plane[] viewPlanes = new Plane[6];
    int viewFrame = -1;
    bool preparingInitial;
    float nextFiniteRefresh;
    int lastFiniteFrame = -1;
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
        lastFiniteFrame = Time.frameCount; SpawnedThisFrame = 0; LastSpawnMilliseconds = 0;
        foreach (var field in finiteFields) field.ReconcileFinite();
        if (preparing || Time.unscaledTime >= nextFiniteRefresh)
        {
            foreach (var field in finiteFields) field.RefreshFinite(player, preparing);
            int local = 0;
            foreach (var field in finiteFields) local += field.CountNear(player, 24);
            foreach (var field in finiteFields) field.PrepareReinforcement(player, local, preparing);
            nextFiniteRefresh = Time.unscaledTime + .1f;
        }
        PendingCount = runGate.Roster.Pending;
        priorityFields.Clear(); priorityFields.AddRange(finiteFields);
        priorityFields.Sort((a, b) => a.Priority(player).CompareTo(b.Priority(player)));
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        using (SpawnMarker.Auto())
        {
            int attempts = 0;
            while (attempts < MaxPerFrame && service.Pool.LeasedCount + service.Pool.PendingReturnCount < AliveLimit)
            {
                bool attempted = false;
                foreach (var field in priorityFields)
                {
                    if (!field.TrySpawnFinite(player, preparing, out bool spawned)) continue;
                    attempted = true; attempts++; if (spawned) SpawnedThisFrame++;
                    break;
                }
                LastSpawnMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                if (!attempted || LastSpawnMilliseconds >= FrameBudgetMilliseconds) break;
            }
        }
    }
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
        int count = 0; foreach (var field in finiteFields) count += field.CountNear(player, 24); return count;
    }
    internal bool IsSeparated(Vector3 point, float radius)
    {
        if (!preparingInitial && runPlayer && Vector3.Distance(point, runPlayer.position) < radius + 3) return false;
        foreach (var field in finiteFields) if (!field.IsSeparated(point, radius)) return false;
        return true;
    }
}
