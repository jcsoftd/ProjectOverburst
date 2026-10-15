#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Diagnostics;
using UnityEngine;

namespace Overburst.DebugTools.Performance
{
    // Observation only; no RNG, spawn scheduling or product values are changed.
    public static class CombatSpawnQualityTrace
    {
        // Registered together before recorder discovery, including paths not yet exercised.
        public const string TraceObserverName = "OVERBURST.CSP.Observer.Trace";
        public static readonly Unity.Profiling.ProfilerMarker TraceObserver = new Unity.Profiling.ProfilerMarker(TraceObserverName);
        public const string QuestRecordKillName = "OVERBURST.CSP.Rewards.RecordKill";
        public static readonly Unity.Profiling.ProfilerMarker QuestRecordKill = new Unity.Profiling.ProfilerMarker(QuestRecordKillName);
        public const string QuestDrainName = "OVERBURST.CSP.Rewards.Drain";
        public static readonly Unity.Profiling.ProfilerMarker QuestDrain = new Unity.Profiling.ProfilerMarker(QuestDrainName);
        public const string LootTotalName = "OVERBURST.CSP.Loot.Total";
        public static readonly Unity.Profiling.ProfilerMarker LootTotal = new Unity.Profiling.ProfilerMarker(LootTotalName);
        public const string LootTableRollName = "OVERBURST.CSP.Loot.TableRoll";
        public static readonly Unity.Profiling.ProfilerMarker LootTableRoll = new Unity.Profiling.ProfilerMarker(LootTableRollName);
        public const string PickupCreateName = "OVERBURST.CSP.Pickup.Create";
        public static readonly Unity.Profiling.ProfilerMarker PickupCreate = new Unity.Profiling.ProfilerMarker(PickupCreateName);
        public const string PickupInstantiateName = "OVERBURST.CSP.Pickup.Instantiate";
        public static readonly Unity.Profiling.ProfilerMarker PickupInstantiate = new Unity.Profiling.ProfilerMarker(PickupInstantiateName);
        public const string CorpsePrepareName = "OVERBURST.CSP.Corpse.Prepare";
        public static readonly Unity.Profiling.ProfilerMarker CorpsePrepare = new Unity.Profiling.ProfilerMarker(CorpsePrepareName);
        public const string NameplateMeasureName = "OVERBURST.CSP.Nameplate.Measure";
        public static readonly Unity.Profiling.ProfilerMarker NameplateMeasure = new Unity.Profiling.ProfilerMarker(NameplateMeasureName);
        public const string VfxNaturalCheckName = "OVERBURST.CSP.Vfx.NaturalCheck";
        public static readonly Unity.Profiling.ProfilerMarker VfxNaturalCheck = new Unity.Profiling.ProfilerMarker(VfxNaturalCheckName);
        public const string VfxMaintenanceName = "OVERBURST.CSP.Vfx.Maintenance";
        public static readonly Unity.Profiling.ProfilerMarker VfxMaintenance = new Unity.Profiling.ProfilerMarker(VfxMaintenanceName);
        public static readonly string[] DetailedMetricNames = { TraceObserverName, QuestRecordKillName, QuestDrainName, LootTotalName, LootTableRollName, PickupCreateName, PickupInstantiateName, CorpsePrepareName, NameplateMeasureName, VfxNaturalCheckName, VfxMaintenanceName };

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
        public static int ItemPickupsCreated;
        public static Exception Failure;
        public static int Candidates, Town, Distance, Ground, Visible, Capsule, Crowded;
        public static bool Enabled => Sink != null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Sink = null; Failure=null; ObserverTicks = ObserverAllocated = 0; ItemPickupsCreated = 0; ResetCandidates(); }
        public static void ResetObservation()
        {
            if (Sink != null) throw new InvalidOperationException("CSP trace already has an owner");
            Reset();
            _ = ElementCombatCostMarkers.Pool_ResolveLifetime;
        }
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
            using var traceScope = TraceObserver.Auto();
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
