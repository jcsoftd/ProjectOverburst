using System;
using System.Collections;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;
using Unity.Profiling;

public static class MapSpawnPolicy
{
    public static int WaveSize(MapInstanceState map)
        => 30 + (Mathf.Clamp(map?.level ?? 1, 1, 100) - 1) / 20 * 5
            + Mathf.Clamp((int)(map?.grade ?? ItemGrade.Common), 0, 6) * 2;
    // The temporary boss has its own actor, so reserve its slot under the total 150 ceiling.
    public static int AliveLimit(MapInstanceState map)
        => Mathf.Min(149, 60 + (Mathf.Clamp(map?.level ?? 1, 1, 100) - 1) / 20 * 22
            + Mathf.Clamp((int)(map?.grade ?? ItemGrade.Common), 0, 6) * 5);
    public static List<EnemyDefinition> Roster(EnemyThemeTable theme, MapInstanceState map, int band, int seed)
    {
        int total = WaveSize(map);
        int medium = 4 + Mathf.Clamp(band, 0, 2) * 2;
        int elite = Mathf.Clamp(band, 0, 2);
        if (MapOptionPolicy.Value(map, MapOptionPolicy.MoreMediumElite) > 0)
        { medium++; if (band > 0) elite++; }
        return theme.BuildRoster(total - medium - elite, medium, elite, seed);
    }
    public static EnemyThemeTier Tier(EnemyThemeTable theme, EnemyDefinition definition)
    {
        foreach (var entry in theme.Entries) if (entry.definition == definition) return entry.tier;
        return EnemyThemeTier.Small;
    }
}

// One queue for fields and events: individual producers cannot multiply the per-frame budget.
[DisallowMultipleComponent]
public sealed class MapSpawnBudget : MonoBehaviour
{
    sealed class Batch
    {
        public MonoBehaviour Owner;
        public List<EnemySpawnRequest> Requests;
        public Action<EnemyActor, int> Spawned;
        public Action<int> Finished;
        public int Index, Count, Failures;
    }
    static readonly ProfilerMarker SpawnMarker = new ProfilerMarker("OVERBURST.MapSpawnBatch");
    readonly Queue<Batch> batches = new Queue<Batch>();
    readonly HashSet<CombatHealth> living = new HashSet<CombatHealth>();
    readonly List<CombatHealth> expired = new List<CombatHealth>();
    EnemySpawnService service;
    public int AliveLimit { get; private set; }
    public int AliveCount => living.Count;
    public int PendingCount { get; private set; }
    public int SpawnedThisFrame { get; private set; }
    public double LastSpawnMilliseconds { get; private set; }
    public const int MaxPerFrame = 2;
    public const double FrameBudgetMilliseconds = 2;

    public void Configure(EnemySpawnService value, MapInstanceState map)
    { service = value; AliveLimit = MapSpawnPolicy.AliveLimit(map); }

    public bool Enqueue(MonoBehaviour owner, List<EnemySpawnRequest> requests,
        Action<EnemyActor, int> spawned, Action<int> finished)
    {
        if (owner == null || requests == null || requests.Count == 0 || service == null) return false;
        batches.Enqueue(new Batch { Owner = owner, Requests = requests, Spawned = spawned, Finished = finished });
        PendingCount += requests.Count;
        return true;
    }
    public void Cancel(MonoBehaviour owner)
    {
        int count = batches.Count;
        for (int i = 0; i < count; i++)
        {
            var batch = batches.Dequeue();
            if (batch.Owner == owner) PendingCount -= batch.Requests.Count - batch.Index;
            else batches.Enqueue(batch);
        }
    }
    void Update()
    {
        SpawnedThisFrame = 0; LastSpawnMilliseconds = 0;
        expired.Clear();
        foreach (var health in living)
            if (health == null || health.IsDead || !health.gameObject.activeInHierarchy) expired.Add(health);
        foreach (var health in expired) Remove(health);
        if (WorldSessionState.Phase != WorldPhase.Run || batches.Count == 0) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        using (SpawnMarker.Auto())
        {
            int attempts = 0;
            while (batches.Count > 0 && attempts < MaxPerFrame && living.Count < AliveLimit
                && service.Pool.LeasedCount + service.Pool.PendingReturnCount < AliveLimit)
            {
                var batch = batches.Peek();
                if (batch.Owner == null || !batch.Owner.isActiveAndEnabled
                    || !batch.Requests[batch.Index].Encounter.CanGrantRewards)
                { PendingCount -= batch.Requests.Count - batch.Index; batches.Dequeue(); continue; }
                attempts++;
                int index = batch.Index;
                if (service.TrySpawn(batch.Requests[index], out var actor))
                {
                    living.Add(actor.Health); actor.Health.OnDead += Dead;
                    batch.Index++; batch.Count++; batch.Failures = 0; PendingCount--; SpawnedThisFrame++;
                    batch.Spawned?.Invoke(actor, index);
                }
                else if (++batch.Failures >= 3)
                {
                    Debug.LogWarning("[MapSpawnBudget] 반복 소환 실패: " + batch.Requests[index].DefinitionId, batch.Owner);
                    batch.Index++; batch.Failures = 0; PendingCount--;
                }
                if (batch.Index == batch.Requests.Count)
                { batches.Dequeue(); batch.Finished?.Invoke(batch.Count); }
                LastSpawnMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                if (LastSpawnMilliseconds >= FrameBudgetMilliseconds) break;
            }
        }
    }
    void Dead(CombatHealth health, DamageInfo hit) => Remove(health);
    void Remove(CombatHealth health)
    { if (living.Remove(health) && health != null) health.OnDead -= Dead; }
    void OnDisable()
    {
        foreach (var health in living) if (health != null) health.OnDead -= Dead;
        living.Clear(); batches.Clear(); PendingCount = 0;
    }

    // Clone and activate each pooled actor during loading, including first-use Awake/Animator work.
    public IEnumerator Warmup(EnemyThemeTable theme, MapInstanceState map)
    {
        var counts = new Dictionary<EnemyDefinition, int>();
        for (int band = 0; band < 3; band++)
        {
            var roster = MapSpawnPolicy.Roster(theme, map, band, 19029);
            var waveCounts = new Dictionary<EnemyDefinition, int>();
            foreach (var def in roster) { waveCounts.TryGetValue(def, out int n); waveCounts[def] = n + 1; }
            foreach (var pair in waveCounts)
            {
                // Keep one extra wave for uneven deaths and tier order at the next field.
                int n = (Mathf.CeilToInt((float)AliveLimit / roster.Count) + 1) * pair.Value + 2;
                counts.TryGetValue(pair.Key, out int previous); counts[pair.Key] = Mathf.Max(previous, n);
            }
        }
        foreach (var pair in counts)
        {
            for (int i = 1; i <= pair.Value; i++) { service.Prewarm(pair.Key, i); yield return null; }
            for (int i = 0; i < pair.Value; i++)
            {
                var request = new EnemySpawnRequest(pair.Key, Vector3.zero, Quaternion.identity,
                    spawnParent: transform, context: EncounterContext.Test);
                if (!service.TrySpawn(request, out var actor)) throw new InvalidOperationException("몬스터 풀 준비 실패: " + pair.Key.EnemyId);
                service.Release(actor);
                yield return null;
            }
        }
    }
}
