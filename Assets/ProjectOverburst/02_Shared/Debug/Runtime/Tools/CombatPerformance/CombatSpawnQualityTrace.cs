#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Diagnostics;
using UnityEngine;

namespace Overburst.DebugTools.Performance
{
    // Observation only; no RNG, spawn scheduling or product values are changed.
    public static class CombatSpawnQualityTrace
    {
        public struct Entry
        {
            public int kind, frame, seed, group, level;
            public Vector2Int cell;
            public Vector3 point;
            public string definition;
            public EnemyActor actor;
            public int candidates, town, distance, ground, visible, capsule, crowded;
            public long ticks;
        }
        public static Action<Entry> Sink;
        public static long ObserverTicks, ObserverAllocated;
        public static Exception Failure;
        public static int Candidates, Town, Distance, Ground, Visible, Capsule, Crowded;
        public static bool Enabled => Sink != null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Sink = null; Failure=null; ObserverTicks = ObserverAllocated = 0; ResetCandidates(); }
        public static void ResetCandidates() { Candidates = Town = Distance = Ground = Visible = Capsule = Crowded = 0; }
        public static void Reject(int reason)
        {
            if (!Enabled) return;
            switch (reason) { case 1: Town++; break; case 2: Distance++; break; case 3: Ground++; break;
                case 4: Visible++; break; case 5: Capsule++; break; case 6: Crowded++; break; }
        }
        public static void Emit(int kind, int seed = 0, Vector2Int cell = default, EnemyDefinition definition = null,
            Vector3 point = default, object group = null, int level = 0, EnemyActor actor = null)
        {
            var sink = Sink; if (sink == null) return;
            long began = Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread();
            try { sink(new Entry { kind = kind, frame = Time.frameCount, ticks = began, seed = seed, cell = cell,
                definition = definition ? definition.EnemyId : "", point = point, group = group == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(group),
                level = level, actor = actor, candidates = Candidates, town = Town, distance = Distance, ground = Ground,
                visible = Visible, capsule = Capsule, crowded = Crowded }); } catch(Exception error) { Failure=error; }
            ObserverAllocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
            ObserverTicks += Stopwatch.GetTimestamp() - began;
        }
    }
}
#endif
