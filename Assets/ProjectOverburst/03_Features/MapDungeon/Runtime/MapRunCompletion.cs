using System;
using System.Collections.Generic;
using Overburst.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

// Owns the product run's one completion event and its recoverable floor presentation.
[DisallowMultipleComponent]
[DefaultExecutionOrder(31000)]
public sealed class MapRunCompletion : MonoBehaviour
{
    RunWorldGate gate;
    PlayerActorRuntime player;
    int lastArea, completionArea, completionFrame;
    Vector3 lastSafe, completionAnchor, portalPoint, rewardPoint;
    bool requested, placed;
    float nextAttempt;
    ItemSnapshot pendingReward;
    readonly Collider[] overlaps = new Collider[64];
    NavMeshPath path;
    public DungeonExitPortal Portal { get; private set; }
    public WorldItemPickup Reward { get; private set; }
    public int CompletionArea => requested ? completionArea : -1;
    public string LastError { get; private set; }
    public bool IsCompleted { get; private set; }

    public void Configure(RunWorldGate world, PlayerActorRuntime actor)
    {
        if (gate || !world || world.Roster == null || !actor) throw new InvalidOperationException("Prepared map and player required.");
        gate = world; player = actor; path = new NavMeshPath();
        lastSafe = gate.EntryPoint.position;
        lastArea = Mathf.Max(0, FindArea(lastSafe, out _));
        gate.Roster.DeathRecorded += ObserveDeath;
    }

    int FindArea(Vector3 position, out Vector3 safe)
    {
        safe = default;
        foreach (var area in gate.Roster.Areas)
            if (MapCombatGeometry.Contains(area.Ground, position)
                && NavMesh.SamplePosition(position, out var nav, 1f, NavMesh.AllAreas)
                && Mathf.Abs(nav.position.y - position.y) <= .75f)
            { safe = nav.position; return area.Ground.Index; }
        return -1;
    }
    void ObservePlayer()
    {
        int area = FindArea(player.transform.position, out var point);
        if (area >= 0) { lastArea = area; lastSafe = point; }
    }
    void ObserveDeath()
    {
        if (requested || !gate.Roster.CaveTargetReached || !player) return;
        ObservePlayer();
        completionArea = lastArea; completionAnchor = lastSafe;
        completionFrame = Time.frameCount; requested = true;
    }
    void LateUpdate()
    {
        if (!gate || !player || WorldSessionState.Phase != WorldPhase.Run
            || WorldSessionState.ContentScene != gameObject.scene || PersistentSceneFlow.Instance?.IsSwitching == true) return;
        if (!requested) { ObservePlayer(); ObserveDeath(); }
        Poll();
    }
    public void Poll()
    {
        if (!gate || !player || player.Health == null || player.Health.IsDead
            || Time.unscaledTime < nextAttempt || WorldSessionState.Phase != WorldPhase.Run) return;
        var account = AccountGameplaySession.Current;
        var run = account?.ReadRun();
        if (run == null || run.runId != gate.Context.RunId || !AccountInvariants.IsRunning(run.phase)) return;
        if (!requested && run.phase != RunPhase.Completed) return;
        // Capture the platform on the exact death event, then let that frame's player damage finish.
        if (run.phase == RunPhase.Active && Time.frameCount <= completionFrame) return;
        nextAttempt = Time.unscaledTime + .5f;
        try
        {
            if (run.phase == RunPhase.Active)
            {
                if (pendingReward == null)
                {
                    var definition = account.ContentRegistry.Resolve<MapItemData>(run.map.mapContentId);
                    var item = MapRewardPolicy.CreateBossMap(definition, account.ContentRegistry, run.map.level,
                        UnityEngine.Random.value, UnityEngine.Random.value, run.runId);
                    item.runtimeInstanceId = AccountRunCommands.CompletionRewardId(run.runId);
                    pendingReward = ItemSnapshotCodec.Capture(item, account.ContentRegistry);
                }
                // A missing floor visual never rolls back an achieved target. Retry it from the saved event.
                TryPreparePresentation(pendingReward, false, account);
                account.ExecuteState("complete-" + run.runId, state => AccountRunCommands.CompleteMap(state,
                    run.runId, DateTime.UtcNow.Ticks, completionArea, completionAnchor, pendingReward));
                run = account.ReadRun();
            }
            if (run.phase != RunPhase.Completed) return;
            IsCompleted = true;
            completionArea = run.completionArea; completionAnchor = run.completionAnchor; requested = true;
            if (!TryPreparePresentation(run.completionReward, run.completionRewardClaimed, account)) return;
            if (Reward && !run.completionRewardClaimed) Reward.gameObject.SetActive(true);
            Portal.gameObject.SetActive(true);
            LastError = null;
        }
        catch (Exception error) { Report(error.Message); }
    }

    bool TryPreparePresentation(ItemSnapshot reward, bool claimed, AccountGameplaySession account)
    {
        try
        {
            if (!placed && !TryPlace()) { Report("완료한 플랫폼의 안전한 귀환 지점을 찾는 중입니다."); return false; }
            if (!Portal)
            {
                var root = new GameObject("Map Completion Portal"); root.SetActive(false); root.transform.SetParent(transform, false);
                try
                {
                    root.transform.position = portalPoint;
                    var portal = root.AddComponent<DungeonExitPortal>(); portal.ConfigureRun(gate.Context.RunId);
                    var visual = root.AddComponent<HideoutPortalVisual>(); visual.Configure(HideoutPortalKind.Dungeon);
                    if (!visual.HasAssetPresentation) throw new InvalidOperationException("귀환 포탈 표시 자산이 없습니다.");
                    root.transform.Find("Portal Name").GetComponent<TMP_Text>().text = "은신처 귀환";
                    Portal = portal;
                }
                finally { if (!Portal) Destroy(root); }
            }
            if (claimed)
            {
                if (Reward) { Destroy(Reward.gameObject); Reward = null; }
            }
            else if (!Reward)
            {
                var pickup = WorldItemDropFactory.CreateWorldPickup(ItemSnapshotCodec.Restore(reward, account.ContentRegistry),
                    rewardPoint, PlayerAccountInventoryService.SharedInventory, player.transform);
                if (!pickup) throw new InvalidOperationException("완료 지도 표시를 만들지 못했습니다.");
                try
                {
                    pickup.gameObject.SetActive(false);
                    pickup.PlaceAuthored(rewardPoint, gameObject.scene);
                    pickup.transform.SetParent(transform, true);
                    Reward = pickup;
                }
                finally { if (!Reward) Destroy(pickup.gameObject); }
            }
            return true;
        }
        catch (Exception error) { Report(error.Message); return false; }
    }
    bool TryPlace()
    {
        var area = gate.Roster.Areas[completionArea].Ground;
        var points = new List<Vector3>(area.Points);
        points.Sort((a, b) => (a - completionAnchor).sqrMagnitude.CompareTo((b - completionAnchor).sqrMagnitude));
        foreach (var point in points)
        {
            if (!MapCombatGeometry.TryCompletionPoint(area, point, 1f, overlaps, path, out var portal)) continue;
            foreach (var near in points)
            {
                float distance = (near - portal).sqrMagnitude;
                if (distance < 2.25f || distance > 16f
                    || !MapCombatGeometry.TryCompletionPoint(area, near, .35f, overlaps, path, out var drop)) continue;
                portalPoint = portal; rewardPoint = drop; placed = true; return true;
            }
        }
        return false;
    }
    void Report(string error)
    {
        if (LastError != error) Debug.LogWarning("지도 완료 준비 재시도: " + error, this);
        LastError = error;
    }
    void OnDestroy()
    {
        if (gate && gate.Roster != null) gate.Roster.DeathRecorded -= ObserveDeath;
    }
}
