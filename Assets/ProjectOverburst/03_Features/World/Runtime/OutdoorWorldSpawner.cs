using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools.Performance;
#endif


[DisallowMultipleComponent]
public sealed class OutdoorWorldSpawner : MonoBehaviour
{
    public static OutdoorWorldSpawner Current { get; private set; }
    [SerializeField] private Terrain terrain;
    [SerializeField] private EnemyThemeTable theme;
    [SerializeField] private EnemySpawnService spawn;
    [SerializeField] private BoxCollider town;
    [SerializeField] private Transform enemiesRoot;
    [SerializeField, Min(16)] private float areaSize = 64;
    [SerializeField, Min(0)] private int smallCount = 40, mediumCount = 9, eliteCount = 1;
    [SerializeField, Min(1)] private int perFrame = 2, aliveLimit = 150;
    private readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
    private readonly Queue<Pending> pending = new Queue<Pending>();
    private readonly List<Lease> leases = new List<Lease>();
    private readonly System.Random random = new System.Random(73130);
    private sealed class SpawnGroup
    {
        public Vector3 point;
        public bool placed;
    }
    private readonly struct Pending
    {
        public readonly EnemyDefinition definition; public readonly Vector2Int cell; public readonly int level, seed;
        public readonly SpawnGroup group;
        public Pending(EnemyDefinition definition, Vector2Int cell, int level, int seed, SpawnGroup group = null)
        { this.definition = definition; this.cell = cell; this.level = level; this.seed = seed; this.group = group; }
    }
    private readonly struct Lease
    {
        public readonly EnemyActor actor; public readonly uint version;
        public Lease(EnemyActor actor) { this.actor = actor; version = actor.LeaseVersion; }
        public bool Valid => actor && actor.LeaseVersion == version && actor.gameObject.activeInHierarchy;
    }
    #if UNITY_EDITOR || DEVELOPMENT_BUILD
    static readonly Unity.Profiling.ProfilerMarker CspTotal = new Unity.Profiling.ProfilerMarker("OVERBURST.CSP.Outdoor.Total");
    static readonly Unity.Profiling.ProfilerMarker CspQueue = new Unity.Profiling.ProfilerMarker("OVERBURST.CSP.Outdoor.Queue");
    static readonly Unity.Profiling.ProfilerMarker CspPosition = new Unity.Profiling.ProfilerMarker("OVERBURST.CSP.Outdoor.Position");
#endif
    private string character;
    private int stage = 1;
    private long revision = -1;
    private bool wasInTown = true;
    public string SessionId { get; private set; }
    public int PendingCount => pending.Count;
    public int VisitedCount => visited.Count;
    public int ActiveCount => leases.Count;
    public int AppliedStage => stage;
    public bool IsInTown(Vector3 position)
    {
        if (!town) return true;
        var p = town.transform.InverseTransformPoint(position) - town.center;
        return Mathf.Abs(p.x) <= town.size.x * .5f && Mathf.Abs(p.z) <= town.size.z * .5f;
    }
    private void OnEnable() { Current = this; SessionId = Guid.NewGuid().ToString("N"); }
    private void OnDisable() { ResetOwned(); if (Current == this) Current = null; }
    public void ResetForWorldLevel()
    {
        ResetOwned(); SyncAccount();
    }
    private void SyncAccount()
    {
        var account = AccountGameplaySession.Current;
        if (account == null) return;
        revision = account.Revision; character = account.ActiveCharacterId; stage = account.AppliedWorldLevel;
    }
    private void ResetOwned()
    {
        // Invalidate reward facts before deactivating any pooled actor.
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        CombatSpawnQualityTrace.Emit(14);
#endif
        SessionId = Guid.NewGuid().ToString("N"); pending.Clear(); visited.Clear();
        if (spawn) foreach (var lease in leases) if (lease.Valid) spawn.Release(lease.actor);
        leases.Clear();
    }
    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        using var cspTotal = CspTotal.Auto();
#endif

        var account = AccountGameplaySession.Current;
        var player = PlayerContext.Instance?.CurrentActor;
        if (!WorldSessionState.IsHideout || !accountReady(account) || !player || !player.Health || player.Health.IsDead
            || PersistentSceneFlow.Instance?.IsSwitching == true || !terrain || !theme || !spawn || !enemiesRoot) return;
        if (account.Revision != revision)
        {
            if (character != account.ActiveCharacterId || stage != account.AppliedWorldLevel) ResetOwned();
            character = account.ActiveCharacterId; stage = account.AppliedWorldLevel; revision = account.Revision;
        }
        bool inTown = IsInTown(player.transform.position);
        if (inTown && !wasInTown) ResetOwned();
        wasInTown = inTown; if (inTown) return;
        var local = player.transform.position - terrain.transform.position;
        var size = terrain.terrainData.size;
        if (local.x < 0 || local.z < 0 || local.x >= size.x || local.z >= size.z) return;
        var cell = new Vector2Int(Mathf.FloorToInt(local.x / areaSize), Mathf.FloorToInt(local.z / areaSize));
        if (visited.Add(cell)) QueueArea(cell);
        leases.RemoveAll(lease => !lease.Valid);
        int attempts = Mathf.Min(pending.Count, perFrame * 8), created = 0;
        var camera = Camera.main;
        for (int i = 0; i < attempts && created < perFrame && leases.Count < aliveLimit; i++)
        {
            var next = pending.Dequeue();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CombatSpawnQualityTrace.ResetCandidates();
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CombatSpawnQualityTrace.Emit(11, next.seed, next.cell, next.definition, group: next.group, level: next.level);
#endif

            if ((next.cell - cell).sqrMagnitude > 2 || !TryPosition(next, player.transform.position, camera, out var point))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CombatSpawnQualityTrace.Emit(12, next.seed, next.cell, next.definition, group: next.group, level: next.level);
#endif
                pending.Enqueue(next); continue; }
            var context = new EncounterContext(null, next.level, ItemGrade.Common, outdoorSessionId: SessionId);
            var request = new EnemySpawnRequest(next.definition, point, Quaternion.Euler(0, random.Next(360), 0),
                player.transform, gameObject, transform, enemiesRoot, seed: next.seed, context: context);
            if (!spawn.TrySpawn(request, out var actor)) { pending.Enqueue(next); continue; }
            if (next.group != null && !next.group.placed) { next.group.point = point; next.group.placed = true; }
            leases.Add(new Lease(actor)); created++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CombatSpawnQualityTrace.Emit(13, next.seed, next.cell, next.definition, point, next.group, next.level, actor);
#endif

        }
    }
    private static bool accountReady(AccountGameplaySession account) => AccountBootstrap.Ready && account != null && !account.NeedsProjectionRecovery;
    private void QueueArea(Vector2Int cell)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        using var cspQueue = CspQueue.Auto();
#endif

        int seed = unchecked(73130 + cell.x * 73856093 + cell.y * 19349663 + stage * 83492791);
        int areaEliteCount = stage < 2 || stage == 2 && (seed & 1) != 0 ? 0 : eliteCount;
        int areaSmallCount = smallCount + eliteCount - areaEliteCount;
        var roster = theme.BuildRoster(areaSmallCount, mediumCount, areaEliteCount, seed, stage);
        var areaRandom = new System.Random(seed);
        for (int i = roster.Count - 1; i > 0; i--)
        { int j = areaRandom.Next(i + 1); var swap = roster[i]; roster[i] = roster[j]; roster[j] = swap; }
        int minimum = AccountWorldLevelCommands.MinimumMonsterLevel(stage);
        var requests = new List<Pending>();
        foreach (var definition in roster) requests.Add(new Pending(definition, cell, minimum + areaRandom.Next(10), areaRandom.Next()));
        var used = new bool[requests.Count];
        for (int i = 0; i < requests.Count; i++)
        {
            if (used[i]) continue;
            var group = new SpawnGroup();
            int remaining = MapCombatGeometry.GroupSize(MapSpawnPolicy.Tier(theme, requests[i].definition));
            for (int j = i; j < requests.Count && remaining > 0; j++)
            {
                if (used[j] || requests[j].definition != requests[i].definition) continue;
                var next = requests[j]; used[j] = true; remaining--;
                pending.Enqueue(new Pending(next.definition, next.cell, next.level, next.seed, group));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CombatSpawnQualityTrace.Emit(10, next.seed, next.cell, next.definition, group: group, level: next.level);
#endif

            }
        }
    }
    private bool TryPosition(Pending next, Vector3 player, Camera camera, out Vector3 result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        using var cspPosition = CspPosition.Auto();
        CombatSpawnQualityTrace.ResetCandidates();
#endif

        result = default;
        if (!camera || !next.definition || !next.definition.ActorPrefab) return false;
        var capsule = next.definition.ActorPrefab.CollisionRoot.GetComponentInChildren<CapsuleCollider>();
        var stats = next.definition.ResolveRuntimeStats();
        float radius = (capsule ? capsule.radius : .5f) * Mathf.Max(stats.CollisionScale.x, stats.CollisionScale.z);
        float height = (capsule ? capsule.height : 1f) * stats.CollisionScale.y;
        var basePoint = terrain.transform.position + new Vector3(next.cell.x * areaSize, 0, next.cell.y * areaSize);
        var terrainMax = terrain.transform.position + terrain.terrainData.size;
        int groundMask = ~LayerMask.GetMask("Enemy", "Player", "Ignore Raycast");
        for (int i = 0; i < 24; i++)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (CombatSpawnQualityTrace.Enabled) CombatSpawnQualityTrace.Candidates++;
#endif

            var point = basePoint + new Vector3((float)random.NextDouble() * areaSize, 0, (float)random.NextDouble() * areaSize);
            if (next.group != null && next.group.placed && i < 16)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float distance = Mathf.Max(radius * 2 + .12f, radius + 1.6f) * (1 + i / 8);
                point = next.group.point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                if (point.x < basePoint.x + radius || point.z < basePoint.z + radius
                    || point.x > basePoint.x + areaSize - radius || point.z > basePoint.z + areaSize - radius) continue;
            }
            if (point.x > terrainMax.x - radius || point.z > terrainMax.z - radius || IsInTown(point)
                || new Vector2(point.x - player.x, point.z - player.z).sqrMagnitude < 14 * 14) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CombatSpawnQualityTrace.Reject(IsInTown(point) ? 1 : 2);
#endif
                continue; }
            point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
            if (!Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var hit, 8, groundMask, QueryTriggerInteraction.Ignore)
                || hit.normal.y < .72f || Mathf.Abs(hit.point.y - point.y) > 2) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CombatSpawnQualityTrace.Reject(3);
#endif
                continue; }
            point = hit.point + Vector3.up * .035f;
            if (Visible(camera, point, height, radius)) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CombatSpawnQualityTrace.Reject(4);
#endif
                continue; }
            if (Physics.CheckCapsule(point + Vector3.up * (radius + .12f), point + Vector3.up * Mathf.Max(radius + .12f, height - radius),
                radius, ~LayerMask.GetMask("Ignore Raycast"), QueryTriggerInteraction.Ignore)) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CombatSpawnQualityTrace.Reject(5);
#endif
                continue; }
            bool crowded = false;
            foreach (var lease in leases) if (lease.Valid && (lease.actor.transform.position - point).sqrMagnitude < (radius + 1.5f) * (radius + 1.5f))
            { crowded = true; break; }
            if (crowded) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CombatSpawnQualityTrace.Reject(6);
#endif
                continue; }
            result = point; return true;
        }
        return false;
    }
    public static bool Visible(Camera camera, Vector3 position, float height, float radius)
    {
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        return GeometryUtility.TestPlanesAABB(planes, new Bounds(position + Vector3.up * height * .5f, new Vector3(radius * 2, height, radius * 2)));
    }
}
