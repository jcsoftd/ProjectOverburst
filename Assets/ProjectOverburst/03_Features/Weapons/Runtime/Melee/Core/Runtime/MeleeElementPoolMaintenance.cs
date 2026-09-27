using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

// Shares preparation/retirement budget between element contacts and charged-heavy requests.
// Active feedback always completes through TransientVfxPool; targets never cap visible effects.
public static class MeleeElementPoolMaintenance
{
    public const int PreparedBudget = 1536;
    public const int MaxOperationsPerFrame = 8;
    public const float RetireDelay = 5f;
    private const double MillisecondsPerFrame = 2d;
    private const float RequestDuration = 30f;
    private sealed class State
    {
        public GameObject Prefab;
        public int Desired, Target, Owners;
        public float RequestUntil, LastUse;
    }
    private static readonly List<State> States = new List<State>(5);
    private static readonly Dictionary<Object, State> Owners = new Dictionary<Object, State>();
    private static readonly List<Object> DeadOwners = new List<Object>();
    private static Host host;
    private static int cursor;
    private static readonly ProfilerMarker Marker = new ProfilerMarker("Overburst.ElementHit.Maintenance");
    public static int LastOperations { get; private set; }
    public static int PeakOperations { get; private set; }
    public static double PeakMilliseconds { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        States.Clear(); Owners.Clear(); DeadOwners.Clear(); host = null; cursor = 0;
        LastOperations = PeakOperations = 0; PeakMilliseconds = 0;
    }

    private static State Get(GameObject prefab, int desired = 0)
    {
        if (host == null)
        {
            var root = new GameObject("Element pool maintenance");
            Object.DontDestroyOnLoad(root);
            host = root.AddComponent<Host>();
        }
        foreach (var state in States)
            if (state.Prefab == prefab) { state.Desired = Mathf.Max(state.Desired, desired); return state; }
        var created = new State { Prefab = prefab, Desired = desired, LastUse = Time.unscaledTime };
        States.Add(created);
        return created;
    }

    public static void SetOwner(Object owner, GameObject prefab, int desired)
    {
        if (owner == null || prefab == null) return;
        var state = Get(prefab, desired);
        if (Owners.TryGetValue(owner, out var previous) && previous == state) return;
        ReleaseOwner(owner);
        Owners[owner] = state;
        state.Owners++;
        state.LastUse = Time.unscaledTime;
        RecomputeTargets();
    }

    public static void ReleaseOwner(Object owner)
    {
        if (ReferenceEquals(owner, null) || !Owners.TryGetValue(owner, out var state)) return;
        Owners.Remove(owner);
        state.Owners--;
        state.LastUse = Time.unscaledTime;
        RecomputeTargets();
    }

    public static void Request(GameObject prefab, int desired)
    {
        var state = Get(prefab, desired);
        state.RequestUntil = Time.unscaledTime + RequestDuration;
        state.LastUse = Time.unscaledTime;
        RecomputeTargets();
    }

    public static void Touch(GameObject prefab) { Get(prefab).LastUse = Time.unscaledTime; }
    public static int GetTarget(GameObject prefab)
    {
        foreach (var state in States) if (state.Prefab == prefab) return state.Target;
        return 0;
    }
    public static bool IsPrepared(GameObject prefab)
    {
        int target = GetTarget(prefab);
        var stats = TransientVfxPool.GetStatistics(prefab);
        return target > 0 && stats.Active + stats.Idle >= target;
    }

    private static bool Required(State state) => state.Owners > 0 || Time.unscaledTime < state.RequestUntil;
    private static void RecomputeTargets()
    {
        int total = 0;
        foreach (var state in States) if (Required(state)) total += state.Desired;
        foreach (var state in States)
            state.Target = !Required(state) ? 0 : total <= PreparedBudget ? state.Desired
                : Mathf.FloorToInt((float)state.Desired * PreparedBudget / total);
    }

    private sealed class Host : MonoBehaviour
    {
        private void Update()
        {
            using (Marker.Auto())
            {
                DeadOwners.Clear();
                foreach (var pair in Owners)
                    if (pair.Key == null || pair.Key is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                        DeadOwners.Add(pair.Key);
                foreach (var owner in DeadOwners) ReleaseOwner(owner);
                RecomputeTargets();
                LastOperations = 0;
                if (States.Count == 0) return;
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                int total = 0;
                foreach (var state in States)
                {
                    var stats = TransientVfxPool.GetStatistics(state.Prefab);
                    total += stats.Active + stats.Idle;
                }
                int noWork = 0;
                while (LastOperations < MaxOperationsPerFrame && noWork < States.Count)
                {
                    var state = States[cursor++ % States.Count];
                    if (cursor == int.MaxValue) cursor = 0;
                    bool worked = false;
                    if (state.Target > 0 || Time.unscaledTime - Mathf.Max(state.LastUse, state.RequestUntil) >= RetireDelay)
                    {
                        if (TransientVfxPool.TrimOne(state.Prefab, state.Target)) { total--; worked = true; }
                        else if (state.Target > 0 && total < PreparedBudget
                            && TransientVfxPool.PrepareOne(state.Prefab, state.Target)) { total++; worked = true; }
                    }
                    if (worked) { LastOperations++; noWork = 0; } else noWork++;
                    if ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency >= MillisecondsPerFrame) break;
                }
                PeakOperations = Mathf.Max(PeakOperations, LastOperations);
                PeakMilliseconds = System.Math.Max(PeakMilliseconds,
                    (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency);
            }
        }
        private void OnDestroy() { if (host == this) host = null; }
    }
}
