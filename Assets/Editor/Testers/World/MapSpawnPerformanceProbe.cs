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
    [Serializable] sealed class RepeatOptions { public bool warmLoot; public bool verifyPreparedLoot; public int targetFrameRate; public double seconds = 75; public string traceGroup; public bool platformTour; public int startArea = -1; public bool memoryProfiler; public bool allocationCallstacks; public double stopHitchMs; public double stopPrepareMs; public double minimumHitchSeconds = 10; public int stopHitchArea = -1; }
    static CombatPerformanceRecorder recorder;
    static MapSpawnBudget budget;
    static long tick;
    static readonly string[] Metrics = {
        "Overburst.ElementHit.Maintenance",
        "OVERBURST.MapSpawn.Detail.Tint.CreateBlock", "OVERBURST.MapSpawn.Detail.Tint.Materials", "OVERBURST.MapSpawn.Detail.Tint.GetBlock", "OVERBURST.MapSpawn.Detail.Tint.Color", "OVERBURST.MapSpawn.Detail.Tint.SetBlock",
        "OVERBURST.MapSpawn.Detail.Placement", "OVERBURST.MapSpawn.Detail.Stats", "OVERBURST.MapSpawn.Detail.Actor.References", "OVERBURST.MapSpawn.Detail.Actor.Identity", "OVERBURST.MapSpawn.Detail.Actor.Body", "OVERBURST.MapSpawn.Detail.Actor.Tint", "OVERBURST.MapSpawn.Detail.Actor.Animator", "OVERBURST.MapSpawn.Detail.Actor.Health", "OVERBURST.MapSpawn.Detail.Actor.Movement", "OVERBURST.MapSpawn.Detail.Actor.Abilities", "OVERBURST.MapSpawn.Detail.Actor.AI",
        "Overburst.CaveBoundary.Sample", "Overburst.CaveBoundary.HeightQuery", "Overburst.CaveBoundary.CacheBuild", "Overburst.CaveBoundary.RimHeightFallback", "Overburst.CaveBoundary.AnchorSweep", "Overburst.CaveBoundary.TargetSweep", "Overburst.CaveBoundary.SlideX", "Overburst.CaveBoundary.SlideZ",
        "WaitForTargetFPS", "FixedUpdate.ScriptRunBehaviourFixedUpdate", "FixedUpdate.PhysicsFixedUpdate", "Overburst.AI.Crowd.Total", "Overburst.AI.Crowd.Build", "Overburst.AI.Crowd.Solve", "Overburst.AI.Crowd.Apply", "Overburst.AI.Crowd.Cells", "Overburst.AI.Navigation", "Overburst.AI.Navigation.Path", "Overburst.AI.Navigation.Obstacle", "Overburst.AI.SquadUpdate", "Overburst.AI.SquadBinding", "Overburst.AI.ChasePlan", "Overburst.AI.TargetPlan", "Overburst.EnemyMotor.Predict", "Overburst.EnemyMotor.Correct", "Overburst.CaveBoundary.Resolve", "OVERBURST.MapSpawnFinite.PlayerArea.NavMesh", "OVERBURST.MapSpawnFinite.PlayerArea.Containment", "Overburst.Melee.Damage.CanPlayVisual", "Overburst.Melee.Damage.Apply", "Overburst.Melee.Damage.Airborne", "Overburst.Melee.Damage.Feedback", "Overburst.Melee.Damage.OnHit", "Overburst.Melee.Damage.TakeDamage", "Mono.JIT",
        "Overburst.Melee.Phase.CaptureTargets", "Overburst.Melee.Phase.Progress", "Overburst.Melee.Phase.ScanTargets", "Overburst.Melee.Phase.SortTargets", "Overburst.Melee.Phase.Contact", "Overburst.Melee.Phase.Callback", "Overburst.Melee.Phase.VfxBegin", "Overburst.Melee.Phase.VfxTrace", "Overburst.Melee.Phase.VfxTick",
        "Overburst.Melee.Advance.Movement", "Overburst.Melee.Advance.VisualHeight", "Overburst.Melee.Advance.Trail", "Overburst.Melee.Advance.HitPhases", "Overburst.Melee.Advance.HeavyArea", "Overburst.Melee.Advance.DischargeTick", "Overburst.Melee.Advance.ComboContinue", "Overburst.Melee.Advance.Finish", "Overburst.Melee.Advance.SwingAudio", "Overburst.Account.ES3.ReadOpen", "Overburst.Account.ES3.WriteOpen", "Overburst.Account.ES3.PreparePath", "Overburst.Account.ES3.PrepareDirectory", "Overburst.Diagnostics.Stutter.ReadMetrics", "Overburst.Diagnostics.Stutter.Context", "Overburst.Diagnostics.Stutter.Audio", "Overburst.Diagnostics.Stutter.Timing", "Overburst.Diagnostics.Stutter.Pool", "Overburst.Diagnostics.Stutter.Hit", "Overburst.Diagnostics.Stutter.Sample", "Overburst.Diagnostics.Stutter.Observe", "Overburst.Diagnostics.Performance.Tick", "Overburst.Diagnostics.Performance.ReadMetrics", "Overburst.Diagnostics.Performance.Timing",
        "Overburst.Account.DiskSave", "Overburst.Account.DiskSave.Lock", "Overburst.Account.DiskSave.ScanSlots", "Overburst.Account.DiskSave.SerializeSnapshot", "Overburst.Account.DiskSave.Checksum", "Overburst.Account.DiskSave.CreateWriter", "Overburst.Account.DiskSave.WriteEnvelope", "Overburst.Account.DiskSave.CommitFile", "Overburst.Account.DiskSave.Readback", "Overburst.Account.DiskSave.ReadBytes", "Overburst.Account.DiskSave.CompareBytes", "Overburst.Account.DiskSave.ParseEnvelope", "Gfx Used Memory", "Gfx Reserved Memory", "GfxDeviceD3D12.ReserveScratchMemory", "CreateCommittedResourceWithTag", "ParticleSystem.ScheduleGeometryJobs", "GfxDeviceD3D12.WaitForLastPresentation.WaitForGPU",
        "Overburst.Melee.Update", "Overburst.Melee.Resolve", "Overburst.Melee.BufferedEvade", "Overburst.Melee.InputDispatch", "Overburst.Melee.Advance",
        "Overburst.LootReserve.Prepare", "Overburst.LootReserve.Roll", "Overburst.LootReserve.Create", "Overburst.LootReserve.Vfx", "Overburst.LootReserve.Warm",
        "Resources.Load", "Shader.CreateGPUProgram", "Overburst.Loot.Resolve", "Overburst.Loot.Origin", "Overburst.Loot.Items",
        "Main Thread", "PlayerLoop", "GC Allocated In Frame", "GC.Collect", "Animators.Update", "Physics.Simulate",
        "OVERBURST.MapSpawnFinite.PlayerArea", "OVERBURST.MapSpawnFinite.Prewarm", "OVERBURST.MapSpawnFinite.Total", "OVERBURST.MapSpawnFinite.Reconcile", "OVERBURST.MapSpawnFinite.Refresh",
        "OVERBURST.MapSpawnFinite.Refresh.Detail.Reinforcement", "OVERBURST.MapSpawnFinite.Priority", "OVERBURST.MapSpawnBatch",
        "OVERBURST.MapSpawn.Detail.Acquire", "OVERBURST.MapSpawn.Detail.Prepare", "OVERBURST.MapSpawn.Detail.Activate", "OVERBURST.MapSpawn.Detail.Finalize"
    };
    public static void RecordFixtureTransition() { recorder?.Event(7); }
    public static void Tick()
    {
        if (recorder == null) return;
        tick = System.Diagnostics.Stopwatch.GetTimestamp(); recorder.Tick(); recorder.Observe(budget.AliveCount);
    }
    public static void EndTick() { if (recorder != null) recorder.AddObserverTicks(tick); }
    public static IEnumerator Verify(RunWorldGate gate, string output, Action<bool, string> check, bool detailed = false, bool playerCombat = false, bool navigationRegions = false)
    {
        var optionsPath = Path.Combine(output,"repeat-options.json");
        var repeat = File.Exists(optionsPath) ? JsonConvert.DeserializeObject<RepeatOptions>(File.ReadAllText(optionsPath)) : null;
        budget = gate.SpawnBudget;
        var player = PlayerContext.Instance.CurrentActor;
        if (playerCombat) yield return CombatHitchFixVerifier.CheckTimeStepTransition(output, check);
        var field = repeat != null && repeat.startArea >= 0 ? budget.Fields.First(f => f.FiniteArea.Ground.Index == repeat.startArea) : budget.Fields.Where(f => f.FiniteArea.Count > f.FiniteArea.InitialCount)
            .OrderBy(f => playerCombat ? -f.FiniteArea.InitialCount : (f.FiniteArea.Ground.Center - player.transform.position).sqrMagnitude).First();
        var ground = field.FiniteArea.Ground;
        var stand = ground.Points.OrderBy(p => (p - ground.Center).sqrMagnitude).First();
        ActorTeleportUtility.TeleportSafely(player.transform, stand + Vector3.up * .12f, Quaternion.identity);
        double until = EditorApplication.timeSinceStartup + 30;
        while (!field.InitialPlaced && EditorApplication.timeSinceStartup < until) yield return null;
        check(field.InitialPlaced, "Spawn capture: initial placement settled on selected platform");
        for (int i = 0; i < 30; i++) yield return null;
        CombatHitchFixVerifier.CheckRuntime(gate, output, check);
        if (playerCombat)
        {
            yield return SquadObstaclePlayProbe.VerifyLooseGrouping(field, check);
            if (navigationRegions) yield return SquadObstaclePlayProbe.VerifyRegions(gate, output, check);
        }
        for (int i = 0; i < 30; i++) yield return null;
        var preparedEnergy = player.GetComponent<OverburstElementEnergy>();
        File.WriteAllText(Path.Combine(output, "tint-loading-preparation.json"), JsonConvert.SerializeObject(new {
            budget.LoadingMaterialReads, budget.LoadingMaterialOverruns, budget.LoadingMaterialMaximumMilliseconds,
            scope = "Readonly map-prefab material queries during loading; no actor leases, RNG or spawn quotas consumed"
        }, Formatting.Indented));
        if (playerCombat) CombatHitchFixVerifier.CheckFirstHitPreparation(player.Equipment, output, check);
        if (playerCombat && repeat?.verifyPreparedLoot == true) yield return PreparedLootPlayVerifier.Verify(gate, output, check);
        if (playerCombat && repeat == null) yield return PreparedLootPlayVerifier.Verify(gate, output, check);
        else if (playerCombat && repeat.warmLoot)
        {
            double readyUntil = Time.realtimeSinceStartupAsDouble + 45;
            while (Time.realtimeSinceStartupAsDouble < readyUntil && Enumerable.Range(0,3).Any(t=>gate.LootReserve.ReadyCount((EnemyThemeTier)t)<18))
            { gate.LootReserve.PrepareOne(); yield return null; }
            check(gate.LootReserve.Error == null && Enumerable.Range(0,3).All(t=>gate.LootReserve.ReadyCount((EnemyThemeTier)t)>=18),"Repeat: explicit loot warmup completed before measurement");
        }
        if (playerCombat && gate.LootReserve) File.WriteAllText(Path.Combine(output,"prepared-loot-before-combat.json"),JsonConvert.SerializeObject(new {
            gate.LootReserve.Hits,gate.LootReserve.Misses,gate.LootReserve.Prepared,gate.LootReserve.BudgetOverruns,
            gate.LootReserve.MaximumStepMilliseconds,gate.LootReserve.Error,
            ready=Enumerable.Range(0,3).Select(t=>gate.LootReserve.ReadyCount((EnemyThemeTier)t)).ToArray(), channels=gate.LootReserve.DiagnosticSnapshot(), vfx=MeleeElementPoolMaintenance.Snapshot(), vfxBudget=new{MeleeElementPoolMaintenance.LastOperations,MeleeElementPoolMaintenance.PeakOperations,MeleeElementPoolMaintenance.PeakMilliseconds},
            explicitWarmup=repeat?.warmLoot,seconds=repeat?.seconds ?? 75
        },Formatting.Indented));
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MeleeDamageResolver).TypeHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(AttackPhaseExecutor).TypeHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(CombatPerformanceRecorder).TypeHandle);
        bool captureOwned = false, previousProfileEditor = UnityEditorInternal.ProfilerDriver.profileEditor;
        bool previousDriverEnabled = UnityEditorInternal.ProfilerDriver.enabled;
        bool memoryOwned = false, previousProfilerEnabled = UnityEngine.Profiling.Profiler.enabled, previousBinaryEnabled = UnityEngine.Profiling.Profiler.enableBinaryLog;
        bool previousAllocationStacks = UnityEngine.Profiling.Profiler.enableAllocationCallstacks;
        var previousMemoryRecordMode = UnityEditorInternal.ProfilerDriver.memoryRecordMode;
        System.Reflection.MethodInfo setHistory = null;
        int previousHistory = 0;
        var prepareTrigger = default(Unity.Profiling.ProfilerRecorder);
        bool prepareTriggerOwned = false;
        double detectedPrepareMs = 0, detectedWallMs = 0;
        int detectedFrame = -1;
        try
        {
            if (repeat?.stopPrepareMs > 0)
            {
                prepareTrigger = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, "OVERBURST.MapSpawn.Detail.Prepare", 1);
                prepareTriggerOwned = true;
                if (!prepareTrigger.Valid) throw new InvalidOperationException("Spawn preparation trigger recorder is unavailable");
            }
            if (repeat?.memoryProfiler == true)
            {
                if (detailed || previousProfilerEnabled || previousBinaryEnabled) throw new InvalidOperationException("Memory profiling requires an unowned disabled profiler and no binary capture");
                var settingsType = typeof(UnityEditorInternal.ProfilerDriver).Assembly.GetType("UnityEditor.Profiling.ProfilerUserSettings");
                var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                previousHistory = (int)settingsType.GetProperty("frameCount", flags).GetValue(null);
                setHistory = typeof(UnityEditorInternal.ProfilerDriver).GetMethod("SetMaxFrameHistoryLength", flags);
                memoryOwned = true;
                setHistory.Invoke(null, new object[] { 600 });
                UnityEditorInternal.ProfilerDriver.ClearAllFrames();
                if (repeat.allocationCallstacks)
                {
                    UnityEditorInternal.ProfilerDriver.memoryRecordMode = UnityEditorInternal.ProfilerMemoryRecordMode.GCAlloc;
                    UnityEngine.Profiling.Profiler.enableAllocationCallstacks = true;
                }
                UnityEditorInternal.ProfilerDriver.enabled = true;
                UnityEngine.Profiling.Profiler.enabled = true;
                UnityEditorInternal.ProfilerDriver.profileEditor = true;
            }
            {
                var prefixes = repeat?.traceGroup == "Melee" ? new[] { "Main Thread", "PlayerLoop", "Mono.JIT", "GC", "Animators.Update", "Physics.Simulate", "ParticleSystem.ScheduleGeometryJobs", "GfxDeviceD3D12", "CreateCommittedResourceWithTag", "Overburst.Diagnostics.", "Overburst.Melee.", "Overburst.Cost.", "Overburst.ElementHit.", "Overburst.Feel.", "Overburst.BloodDecal.", "OVERBURST.CSP.", "Overburst.Loot." } : repeat?.traceGroup == "Spawn" ? new[] { "Main Thread", "PlayerLoop", "Mono.JIT", "GC", "Physics.Simulate", "Animators.Update", "ParticleSystem.ScheduleGeometryJobs", "GfxDeviceD3D12", "CreateCommittedResourceWithTag", "Overburst.Diagnostics.", "Overburst.Account.", "Overburst.LootReserve.", "Overburst.Loot.", "Overburst.Melee.Update", "Overburst.AI.", "Overburst.EnemyMotor.", "Overburst.CaveBoundary.", "OVERBURST.MapSpawn", "OVERBURST.CSP." } : null;
                if (repeat?.traceGroup == "Spawn") prefixes = prefixes.Concat(new[] { "Overburst.ElementHit." }).ToArray();
                var result = Overburst.DebugTools.CombatStutterCapture.Start(playerCombat ? (float)((repeat?.seconds ?? 75) + 30) : 60, 50, detailed, null, false, prefixes, deferExport: true);
                if (!result.Success) throw new InvalidOperationException(Overburst.DebugTools.CombatStutterCapture.LastStatus);
                captureOwned = true;
                File.WriteAllText(Path.Combine(output, "detailed-path.txt"), Overburst.DebugTools.CombatStutterCapture.LastFolder);
                if (detailed) UnityEditorInternal.ProfilerDriver.profileEditor = true;
            }
        var segment = new CombatPerformanceSegment { key = "replenishment", scenario = playerCombat ? "production keyboard/mouse combat" : "real actor deaths, one per second", theme = gate.Map.monsterThemeId };
        var actors = new List<EnemyActor>();
        int killedBefore = gate.Roster.Kills;
        int kills = 0, initialReleased = field.ReinforcementReleased, total = gate.Roster.Total;
        recorder = new CombatPerformanceRecorder(new CombatPerformanceProfile { frameCapacity = Math.Max(8192,(int)((repeat?.seconds ?? 75)*120)), eventCapacity = 256 }, segment, null, Math.Max(48, Metrics.Length), true, Metrics);
        var missing = recorder.MissingRequestedMetrics;
        double nextKill = 0; until = EditorApplication.timeSinceStartup + 24;
        try
        {
            if (playerCombat)
            {
                var tourFields = repeat?.platformTour == true ? budget.Fields.Where(f => f.FiniteArea.Count > f.FiniteArea.InitialCount && f.FiniteArea.Ground.Points.Count > 0).ToArray() : null;
                Func<bool> stopRequested = repeat != null && (repeat.stopHitchMs > 0 || repeat.stopPrepareMs > 0) ? () => {
                    double prepareMs = prepareTriggerOwned && prepareTrigger.Count > 0 ? prepareTrigger.LastValue / 1000000d : 0;
                    bool detected = (repeat.stopHitchMs > 0 && recorder.LastWallMilliseconds >= repeat.stopHitchMs)
                        || (repeat.stopPrepareMs > 0 && prepareMs >= repeat.stopPrepareMs);
                    if (!detected || (repeat.stopHitchArea >= 0 && budget.CurrentPlayerArea != repeat.stopHitchArea)) return false;
                    detectedPrepareMs = prepareMs; detectedWallMs = recorder.LastWallMilliseconds; detectedFrame = Time.frameCount; return true;
                } : (Func<bool>)null;
                int previousRate = Application.targetFrameRate, previousVsync = QualitySettings.vSyncCount;
                try
                {
                    if (repeat?.targetFrameRate > 0) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = repeat.targetFrameRate; }
                    yield return CombatHitchInputProbe.Fight(field, output, check, repeat?.seconds ?? 75, tourFields, stopRequested, Math.Max(0, repeat?.minimumHitchSeconds ?? 10));
                }
                finally
                {
                    if (repeat?.targetFrameRate > 0)
                    {
                        Application.targetFrameRate = previousRate; QualitySettings.vSyncCount = previousVsync;
                        File.WriteAllText(Path.Combine(output, "combat-framerate-fixture.json"), JsonConvert.SerializeObject(new { requested = repeat.targetFrameRate, previousRate, previousVsync, restoredRate = Application.targetFrameRate, restoredVsync = QualitySettings.vSyncCount }));
                    }
                }
                check(preparedEnergy && player.GetComponent<OverburstElementEnergy>() == preparedEnergy,
                    "Combat reuses the zero-energy component prepared while loading");
                kills = gate.Roster.Kills - killedBefore; segment.kills = kills;
            }
            else while (EditorApplication.timeSinceStartup < until)
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
            if (prepareTriggerOwned) File.WriteAllText(Path.Combine(output, "capture-trigger.json"), JsonConvert.SerializeObject(new { detectedFrame, detectedPrepareMs, detectedWallMs, prepareThreshold = repeat.stopPrepareMs, wallThreshold = repeat.stopHitchMs, scope = "Latest completed profiler samples; trigger context is not an exact same-frame attribution" }, Formatting.Indented));
            File.WriteAllText(Path.Combine(output, "spawn-capture.json"), JsonConvert.SerializeObject(new { area = ground.Index, kills,
                released = field.ReinforcementReleased - initialReleased, field.ReservePrewarmedCount, total, missing, scope = playerCombat ? "Editor Play; virtual keyboard/mouse -> production attacks, natural hits/deaths; fixture death prevention; not human/Player FPS" : "Editor Play; real actors, injected deaths, live AI; no Player or direct input claim" }, Formatting.Indented));
        }
        check(!missing.Any(n => n.StartsWith("Overburst.Melee.", StringComparison.Ordinal) || n.StartsWith("Overburst.LootReserve.", StringComparison.Ordinal) || n.StartsWith("Overburst.Account.", StringComparison.Ordinal) || n.StartsWith("OVERBURST.MapSpawnFinite.PlayerArea.", StringComparison.Ordinal)), "Required split diagnostic markers were bound before measured combat");
        CombatHitchFixVerifier.CheckRuntime(gate, output, check);
        if (playerCombat)
        {
            // The ten-minute combat capture may already be frozen at its duration limit.
            // Keep synthetic saturation and its export checks in a separate capture.
            if (captureOwned) { Overburst.DebugTools.CombatStutterCapture.Stop(); captureOwned = false; }
            var calibration = Overburst.DebugTools.CombatStutterCapture.Start(10, 50, false,
                null, false, new[] { "Overburst.Diagnostics.Stutter.Pool" });
            if (!calibration.Success) throw new InvalidOperationException(Overburst.DebugTools.CombatStutterCapture.LastStatus);
            captureOwned = true;
            File.WriteAllText(Path.Combine(output, "pool-calibration-path.txt"), Overburst.DebugTools.CombatStutterCapture.LastFolder);
            CombatHitchFixVerifier.CalibrateAudioPools(output, check);
        }
        if (gate.LootReserve) File.WriteAllText(Path.Combine(output,"prepared-loot-after-combat.json"),JsonConvert.SerializeObject(new {
            gate.LootReserve.Hits,gate.LootReserve.Misses,gate.LootReserve.Prepared,gate.LootReserve.BudgetOverruns,
            gate.LootReserve.MaximumStepMilliseconds,gate.LootReserve.Error,
            ready=Enumerable.Range(0,3).Select(t=>gate.LootReserve.ReadyCount((EnemyThemeTier)t)).ToArray(), channels=gate.LootReserve.DiagnosticSnapshot(), vfx=MeleeElementPoolMaintenance.Snapshot(), vfxBudget=new{MeleeElementPoolMaintenance.LastOperations,MeleeElementPoolMaintenance.PeakOperations,MeleeElementPoolMaintenance.PeakMilliseconds}
        },Formatting.Indented));
        check((playerCombat ? kills > 0 : kills == 20) && field.ReinforcementReleased > initialReleased && gate.Roster.Total == total,
            "Spawn capture: real deaths replenish finite reserves without changing total");
        }
        finally
        {
            try
            {
                if (prepareTriggerOwned) prepareTrigger.Dispose();
                if (captureOwned) Overburst.DebugTools.CombatStutterCapture.Stop();
                if (memoryOwned)
                {
                    int first = UnityEditorInternal.ProfilerDriver.firstFrameIndex, last = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
                    File.WriteAllText(Path.Combine(output, "memory-profiler-state.json"), JsonConvert.SerializeObject(new { first, last, driverEnabled = UnityEditorInternal.ProfilerDriver.enabled, engineEnabled = UnityEngine.Profiling.Profiler.enabled, profileEditor = UnityEditorInternal.ProfilerDriver.profileEditor, allocationStacks = UnityEngine.Profiling.Profiler.enableAllocationCallstacks, memoryRecordMode = UnityEditorInternal.ProfilerDriver.memoryRecordMode.ToString() }));
                    UnityEditorInternal.ProfilerDriver.enabled = false;
                    UnityEngine.Profiling.Profiler.enabled = false;
                    if (first < 0 || last < first) throw new IOException("Memory profiler did not receive native frames");
                    if (!UnityEditorInternal.ProfilerDriver.SaveProfile(Path.Combine(output, "memory-profiler.raw"))) throw new IOException("Memory profiler snapshot failed");
                }
            }
            finally
            {
                if (memoryOwned)
                {
                    UnityEngine.Profiling.Profiler.enabled = false;
                    try { UnityEditorInternal.ProfilerDriver.ClearAllFrames(); setHistory.Invoke(null, new object[] { previousHistory }); }
                    finally { UnityEngine.Profiling.Profiler.enableAllocationCallstacks = previousAllocationStacks; UnityEditorInternal.ProfilerDriver.memoryRecordMode = previousMemoryRecordMode; UnityEngine.Profiling.Profiler.enableBinaryLog = previousBinaryEnabled; UnityEditorInternal.ProfilerDriver.enabled = previousDriverEnabled; UnityEngine.Profiling.Profiler.enabled = previousProfilerEnabled; }
                }
                UnityEditorInternal.ProfilerDriver.profileEditor = previousProfileEditor;
            }
        }
        if (playerCombat) CombatHitchFixVerifier.CheckPoolExport(Overburst.DebugTools.CombatStutterCapture.LastFolder, check);
    }
}
