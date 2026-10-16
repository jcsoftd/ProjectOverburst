using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.DebugTools.Performance;
using UnityEditor;
using UnityEngine;

// Controlled real-actor deaths and replenishment; not a Player FPS or direct-input benchmark.
public static class MapSpawnPerformanceProbe
{
    static CombatPerformanceRecorder recorder;
    static MapSpawnBudget budget;
    static long tick;
    static readonly string[] Metrics = {
        "Resources.Load", "Shader.CreateGPUProgram", "Overburst.Loot.Resolve", "Overburst.Loot.Origin", "Overburst.Loot.Items",
        "Main Thread", "PlayerLoop", "GC Allocated In Frame", "GC.Collect", "Animators.Update", "Physics.Simulate",
        "OVERBURST.MapSpawnFinite.PlayerArea", "OVERBURST.MapSpawnFinite.Prewarm", "OVERBURST.MapSpawnFinite.Total", "OVERBURST.MapSpawnFinite.Reconcile", "OVERBURST.MapSpawnFinite.Refresh",
        "OVERBURST.MapSpawnFinite.Refresh.Detail.Reinforcement", "OVERBURST.MapSpawnFinite.Priority", "OVERBURST.MapSpawnBatch",
        "OVERBURST.MapSpawn.Detail.Acquire", "OVERBURST.MapSpawn.Detail.Prepare", "OVERBURST.MapSpawn.Detail.Activate", "OVERBURST.MapSpawn.Detail.Finalize"
    };
    public static void Tick()
    {
        if (recorder == null) return;
        tick = System.Diagnostics.Stopwatch.GetTimestamp(); recorder.Tick(); recorder.Observe(budget.AliveCount);
    }
    public static void EndTick() { if (recorder != null) recorder.AddObserverTicks(tick); }
    public static IEnumerator Verify(RunWorldGate gate, string output, Action<bool, string> check, bool detailed = false)
    {
        budget = gate.SpawnBudget;
        var player = PlayerContext.Instance.CurrentActor;
        var field = budget.Fields.Where(f => f.FiniteArea.Count > f.FiniteArea.InitialCount)
            .OrderBy(f => (f.FiniteArea.Ground.Center - player.transform.position).sqrMagnitude).First();
        var ground = field.FiniteArea.Ground;
        var stand = ground.Points.OrderBy(p => (p - ground.Center).sqrMagnitude).First();
        ActorTeleportUtility.TeleportSafely(player.transform, stand + Vector3.up * .12f, Quaternion.identity);
        double until = EditorApplication.timeSinceStartup + 30;
        while (!field.InitialPlaced && EditorApplication.timeSinceStartup < until) yield return null;
        check(field.InitialPlaced, "Spawn capture: initial placement settled on selected platform");
        for (int i = 0; i < 30; i++) yield return null;
        bool captureOwned = false, previousProfileEditor = UnityEditorInternal.ProfilerDriver.profileEditor;
        try
        {
            if (detailed)
            {
                var result = Overburst.DebugTools.CombatStutterCapture.Start(60, 50, true, null, false);
                if (!result.Success) throw new InvalidOperationException(Overburst.DebugTools.CombatStutterCapture.LastStatus);
                captureOwned = true;
                File.WriteAllText(Path.Combine(output, "detailed-path.txt"), Overburst.DebugTools.CombatStutterCapture.LastFolder);
                UnityEditorInternal.ProfilerDriver.profileEditor = true;
            }
        var segment = new CombatPerformanceSegment { key = "replenishment", scenario = "real actor deaths, one per second", theme = gate.Map.monsterThemeId };
        var actors = new List<EnemyActor>();
        int kills = 0, initialReleased = field.ReinforcementReleased, total = gate.Roster.Total;
        recorder = new CombatPerformanceRecorder(new CombatPerformanceProfile { frameCapacity = 8192, eventCapacity = 256 }, segment, null, 48, true, Metrics);
        var missing = recorder.MissingRequestedMetrics;
        double nextKill = 0; until = EditorApplication.timeSinceStartup + 24;
        try
        {
            while (EditorApplication.timeSinceStartup < until)
            {
                if (EditorApplication.timeSinceStartup >= nextKill && kills < 20)
                {
                    actors.Clear(); field.CollectLivingActors(actors);
                    if (actors.Count > 0)
                    {
                        var actor = actors[0]; recorder.Event(6, fatal: true);
                        actor.Health.TakeDamage(new DamageInfo(1000000, actor.transform.position, player.gameObject, suppressDefaultHitVfx: true));
                        kills++; segment.kills++;
                    }
                    nextKill = EditorApplication.timeSinceStartup + 1;
                }
                yield return null;
            }
        }
        finally
        {
            recorder.End(Path.Combine(output, "SpawnCapture")); recorder.Dispose(); recorder = null; budget = null;
            File.WriteAllText(Path.Combine(output, "spawn-capture.json"), JsonConvert.SerializeObject(new { area = ground.Index, kills,
                released = field.ReinforcementReleased - initialReleased, field.ReservePrewarmedCount, total, missing, scope = "Editor Play; real actors, injected deaths, live AI; no Player or direct input claim" }, Formatting.Indented));
        }
        check(kills == 20 && field.ReinforcementReleased > initialReleased && gate.Roster.Total == total,
            "Spawn capture: twenty real deaths replenish finite reserves without changing total");
        }
        finally
        {
            if (captureOwned)
            {
                try { Overburst.DebugTools.CombatStutterCapture.Stop(); }
                finally { UnityEditorInternal.ProfilerDriver.profileEditor = previousProfileEditor; }
            }
        }
    }
}
