using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

/// <summary>Owns a scene-scoped snapshot of the existing enemy registry, never the enemies themselves.</summary>
public sealed class MinimapEnemySource : IDisposable
{
    public const int Capacity = 160;
    private static readonly ProfilerMarker CandidateMarker = new ProfilerMarker("Minimap.Candidates");
    private readonly List<EnemyRank> ranks = new List<EnemyRank>(256);
    private readonly List<Entry> entries = new List<Entry>(256);
    private readonly Dictionary<EnemyRank, Entry> cached = new Dictionary<EnemyRank, Entry>(256);
    private readonly List<EnemyRank> removed = new List<EnemyRank>(256);
    private readonly Candidate[] selected = new Candidate[Capacity];
    private readonly Action<CombatHealth, DamageInfo> deadHandler;
    private readonly Action<CombatHealth> resetHandler;
    private uint revision;
    private uint membershipRevision;
    private int sceneHandle = int.MinValue;
    private bool dirty = true;
    private bool snapshotDirty = true;

    public int Count { get; private set; }
    public int RegisteredCount => entries.Count;
    public bool IsDirty => dirty || revision != EnemyRank.ActiveRevision || membershipRevision != EnemyRank.MembershipRevision;

    public MinimapEnemySource()
    {
        deadHandler = HandleDeath;
        resetHandler = HandleReset;
    }

    private struct Entry
    {
        public EnemyRank Rank;
        public Transform Transform;
        public CombatHealth Health;
        public int Id;
    }

    private struct Candidate
    {
        public Entry Entry;
        public float Distance;
        public EnemyGradeType Grade;
    }

    public void Select(Vector3 origin, float radius, int targetScene, MinimapExplorationMap exploration = null)
    {
        using (CandidateMarker.Auto())
        {
            if (sceneHandle != targetScene || membershipRevision != EnemyRank.MembershipRevision || snapshotDirty)
                RebuildSnapshot(targetScene);
            Count = 0;
            float radiusSquared = radius * radius;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (!IsValid(entry)) continue;
                if (exploration != null && !exploration.IsRevealed(entry.Transform.position)) continue;
                float distance = MinimapProjection.DistanceSquared(entry.Transform.position, origin);
                if (distance > radiusSquared) continue;
                var candidate = new Candidate { Entry = entry, Distance = distance, Grade = entry.Rank.GradeType };
                if (Count < Capacity)
                {
                    int index = Count++;
                    selected[index] = candidate;
                    while (index > 0)
                    {
                        int parent = (index - 1) / 2;
                        if (!Worse(selected[index], selected[parent])) break;
                        Swap(index, parent);
                        index = parent;
                    }
                }
                else if (Worse(selected[0], candidate))
                {
                    selected[0] = candidate;
                    SiftDown(0, Count);
                }
            }
            // Heap sort gives deterministic best-first order without an allocated comparer/delegate.
            for (int end = Count - 1; end > 0; end--)
            {
                Swap(0, end);
                SiftDown(0, end);
            }
            Array.Clear(selected, Count, selected.Length - Count);
            revision = EnemyRank.ActiveRevision;
            dirty = false;
        }
    }

    public bool TryGet(int index, Vector3 origin, float radiusSquared, out Vector3 position, out EnemyGradeType grade, out int id)
    {
        Candidate candidate = selected[index];
        grade = candidate.Grade;
        id = candidate.Entry.Id;
        if (IsValid(candidate.Entry))
        {
            position = candidate.Entry.Transform.position;
            return MinimapProjection.DistanceSquared(position, origin) <= radiusSquared;
        }
        position = default;
        return false;
    }

    public void Invalidate() { dirty = true; snapshotDirty = true; }

    private bool IsValid(Entry entry) => entry.Rank != null && entry.Rank.isActiveAndEnabled
        && entry.Transform != null && entry.Transform.gameObject.scene.handle == sceneHandle
        && entry.Health != null && entry.Health.isActiveAndEnabled && !entry.Health.IsDead;

    private void RebuildSnapshot(int targetScene)
    {
        if (sceneHandle != targetScene)
        {
            Unsubscribe();
            cached.Clear();
        }
        entries.Clear();
        sceneHandle = targetScene;
        EnemyRank.CollectActive(ranks);
        for (int i = 0; i < ranks.Count; i++)
        {
            EnemyRank rank = ranks[i];
            if (rank.gameObject.scene.handle != sceneHandle) continue;
            if (!cached.TryGetValue(rank, out Entry entry) || entry.Health == null)
            {
                CombatHealth health = rank.GetComponent<CombatHealth>();
                if (health == null) health = rank.GetComponentInParent<CombatHealth>();
                if (health == null) continue;
                entry = new Entry { Rank = rank, Transform = rank.transform, Health = health, Id = rank.GetInstanceID() };
                cached[rank] = entry;
                health.OnDead += deadHandler;
                health.OnReset += resetHandler;
            }
            entries.Add(entry);
        }
        removed.Clear();
        foreach (var pair in cached)
            if (pair.Key == null || !pair.Key.isActiveAndEnabled || pair.Key.gameObject.scene.handle != sceneHandle)
            {
                Unsubscribe(pair.Value.Health);
                removed.Add(pair.Key);
            }
        foreach (var rank in removed) cached.Remove(rank);
        removed.Clear();
        ranks.Clear();
        membershipRevision = EnemyRank.MembershipRevision;
        snapshotDirty = false;
    }

    private static bool Worse(Candidate a, Candidate b)
    {
        if (a.Grade != b.Grade) return a.Grade < b.Grade;
        if (a.Distance != b.Distance) return a.Distance > b.Distance;
        return a.Entry.Id > b.Entry.Id;
    }

    private void SiftDown(int index, int length)
    {
        while (index * 2 + 1 < length)
        {
            int child = index * 2 + 1;
            if (child + 1 < length && Worse(selected[child + 1], selected[child])) child++;
            if (!Worse(selected[child], selected[index])) break;
            Swap(index, child);
            index = child;
        }
    }

    private void Swap(int a, int b) { Candidate value = selected[a]; selected[a] = selected[b]; selected[b] = value; }
    private void HandleDeath(CombatHealth health, DamageInfo info) => dirty = true;
    private void HandleReset(CombatHealth health) => dirty = true;

    private void Unsubscribe()
    {
        foreach (var entry in cached.Values) Unsubscribe(entry.Health);
    }

    private void Unsubscribe(CombatHealth health)
    {
        if (health == null) return;
        health.OnDead -= deadHandler;
        health.OnReset -= resetHandler;
    }

    public void Dispose()
    {
        Unsubscribe();
        entries.Clear();
        cached.Clear();
        removed.Clear();
        ranks.Clear();
        Array.Clear(selected, 0, selected.Length);
        Count = 0;
        sceneHandle = int.MinValue;
        dirty = true;
        snapshotDirty = true;
    }
}
