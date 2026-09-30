using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

// Local-only 60D crowd cost probe. 50/100 enemies at the user's production ratio (39/10/1, 78/20/2),
// SpiderBrood theme, Hideout debug arena, real AI/movement/animation/VFX, high HP, gameplay input isolated.
// Isolated account (OVERBURST_SAVE_DIRECTORY); exits Play itself. Editor numbers, not a Player-build FPS guarantee.
// Marker values are inclusive CPU scopes of the last completed frame; nested scopes overlap and must not be summed.
[InitializeOnLoad]
public static class UpperElementCrowdPerfProbe
{
    const string Key = "UpperElementCrowdPerfProbe";
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<object> results = new List<object>();
    static readonly List<string> errors = new List<string>();
    static readonly List<string> notes = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static UpperElementCrowdPerfProbe() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PersistentScene", "Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".env", Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY") ?? "");
        Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetInt(Key + ".fps", Application.targetFrameRate);
            Application.runInBackground = true; Application.targetFrameRate = -1;
            results.Clear(); errors.Clear(); notes.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 900;
            stack.Clear(); work = Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            StopRecorders();
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Application.targetFrameRate = SessionState.GetInt(Key + ".fps", -1);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", SessionState.GetString(Key + ".env", ""));
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { Check(EditorApplication.timeSinceStartup < deadline, "Timeout"); if (Step()) return; Finish("COMPLETE"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }

    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (top.MoveNext())
            {
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                return true;
            }
            stack.Pop();
        }
        return false;
    }

    static void Write(string status)
    {
        File.WriteAllText(Path.Combine(Output, "perf-results.json"),
            JsonConvert.SerializeObject(new { status, environment = notes, results, errors }, Formatting.Indented));
    }

    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        Write(status);
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    static void SetPrivate(object target, string property, object value)
    {
        target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
    }

    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    // ---------------- recorders ----------------
    static readonly List<ProfilerRecorder> recs = new List<ProfilerRecorder>();
    static readonly List<string> recNames = new List<string>();
    static ProfilerRecorder draws, setpass, gcAlloc;
    static readonly FrameTiming[] timing = new FrameTiming[1];
    static readonly HashSet<string> engineMarkers = new HashSet<string>
    {
        "PlayerLoop", "ParticleSystem.UpdateJob", "ParticleSystem.GeometryJob", "ParticleSystem.Draw",
        "ParticleSystem.WaitForPreviousRenderingToFinish", "ParticleSystem.ScheduleJobs", "Gfx.WaitForPresentOnGfxThread",
        "Gfx.WaitForRenderThread", "RenderLoop.DrawSRPBatcher", "Physics.Simulate", "FixedUpdate.PhysicsFixedUpdate",
        "Update.ScriptRunBehaviourUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
        "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.ParticleSystemBeginUpdateAll", "PostLateUpdate.FinishFrameRendering"
    };

    static void StartRecorders()
    {
        StopRecorders();
        foreach (var f in typeof(ElementCombatCostMarkers).GetFields(BindingFlags.Public | BindingFlags.Static)) f.GetValue(null);
        MeleeElementHitVfxService.CanPlay(WeaponElement.Light); TransientVfxPool.GetStatistics(null);
        var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var h in handles)
        {
            var d = ProfilerRecorderHandle.GetDescription(h);
            if ((d.Name.StartsWith("Overburst.") || engineMarkers.Contains(d.Name)) && !recNames.Contains(d.Name))
            { recNames.Add(d.Name); recs.Add(ProfilerRecorder.StartNew(d.Category, d.Name, 1)); }
        }
        draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
        setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
    }

    static void StopRecorders()
    {
        foreach (var r in recs) r.Dispose(); recs.Clear(); recNames.Clear();
        draws.Dispose(); setpass.Dispose(); gcAlloc.Dispose();
    }

    sealed class Sampler
    {
        public readonly List<double> frameMs = new List<double>(), cpuMs = new List<double>(), gpuMs = new List<double>();
        public readonly List<double> drawCalls = new List<double>(), setPass = new List<double>(), gcBytes = new List<double>();
        public readonly List<double[]> markers = new List<double[]>();
        public readonly List<string> segments = new List<string>();
        public void Sample(string segment)
        {
            frameMs.Add(Time.unscaledDeltaTime * 1000.0);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                if (timing[0].cpuMainThreadFrameTime > 0 && timing[0].cpuMainThreadFrameTime < 1000) cpuMs.Add(timing[0].cpuMainThreadFrameTime);
                if (timing[0].gpuFrameTime > 0 && timing[0].gpuFrameTime < 1000) gpuMs.Add(timing[0].gpuFrameTime);
            }
            if (draws.Valid) drawCalls.Add(draws.LastValue);
            if (setpass.Valid) setPass.Add(setpass.LastValue);
            if (gcAlloc.Valid) gcBytes.Add(gcAlloc.LastValue);
            var row = new double[recs.Count];
            for (int i = 0; i < recs.Count; i++) row[i] = recs[i].Valid ? recs[i].LastValue / 1000000.0 : double.NaN;
            markers.Add(row); segments.Add(segment);
        }
    }

    static object Sum(IEnumerable<double> source)
    {
        var a = source.Where(x => !double.IsNaN(x)).ToList();
        if (a.Count == 0) return null;
        a.Sort();
        return new { avg = Math.Round(a.Average(), 3), p95 = Math.Round(a[Math.Min(a.Count - 1, (int)(a.Count * .95))], 3), max = Math.Round(a[a.Count - 1], 3) };
    }

    static Dictionary<string, object> MarkerSummary(Sampler s, IList<int> frames)
    {
        var map = new Dictionary<string, object>();
        for (int m = 0; m < recNames.Count; m++)
        {
            var values = frames.Select(i => s.markers[i][m]).Where(x => !double.IsNaN(x)).ToList();
            if (values.Count == 0 || values.Max() < 0.0005) continue;
            map[recNames[m]] = Sum(values);
        }
        return map;
    }

    // ---------------- fixture state ----------------
    static int heavyHits, derivedHits, weakHits, casts;
    static void Hit(CombatHealth h, DamageInfo d, float actual, bool fatal)
    {
        if (actual <= 0 || d.isDamageOverTime) return;
        if ((d.playerAttackKind & PlayerAttackKind.Heavy) != 0) heavyHits++;
        else if ((d.playerAttackKind & PlayerAttackKind.Elemental) != 0) derivedHits++;
        else if ((d.playerAttackKind & PlayerAttackKind.Weak) != 0) weakHits++;
    }

    static readonly List<EnemyActor> enemies = new List<EnemyActor>();
    static GameObject[] trackedPrefabs = Array.Empty<GameObject>();
    static string[] trackedNames = Array.Empty<string>();

    static IEnumerator Measure(int count, string name, float seconds, Action beforeFrame, Func<string> segmentAfter, Func<object> extra)
    {
        heavyHits = derivedHits = weakHits = casts = 0;
        var before = trackedPrefabs.Select(p => p != null ? TransientVfxPool.GetStatistics(p) : default).ToArray();
        var s = new Sampler();
        int auraLeasedMax = 0, auraRegisteredMax = 0, magnetMax = 0, magnetMovingMax = 0, darkActiveMax = 0, lightPendingMax = 0;
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            beforeFrame?.Invoke();
            yield return null;
            s.Sample(segmentAfter != null ? segmentAfter() : "steady");
            auraLeasedMax = Math.Max(auraLeasedMax, MeleeElementStatusAuraVisibilityScheduler.LeasedPresentationCount);
            auraRegisteredMax = Math.Max(auraRegisteredMax, MeleeElementStatusAuraVisibilityScheduler.RegisteredControllerCount);
            magnetMax = Math.Max(magnetMax, DarkMagnetismSystem.ParticipantCount);
            magnetMovingMax = Math.Max(magnetMovingMax, DarkMagnetismSystem.LastMovingCount);
            darkActiveMax = Math.Max(darkActiveMax, DarkGatherBurstScheduler.ActiveCount);
            lightPendingMax = Math.Max(lightPendingMax, LightTripleImpactScheduler.PendingCount);
        }
        int alive = enemies.Count(e => e != null && e.gameObject.activeInHierarchy && !e.Health.IsDead);
        var all = Enumerable.Range(0, s.frameMs.Count).ToList();
        var segs = new Dictionary<string, object>();
        foreach (var seg in s.segments.Distinct())
        {
            var idx = all.Where(i => s.segments[i] == seg).ToList();
            segs[seg] = new { frames = idx.Count, frameMs = Sum(idx.Select(i => s.frameMs[i])), markers = MarkerSummary(s, idx) };
        }
        var worst = all.OrderByDescending(i => s.frameMs[i]).Take(3).Select(i => new
        {
            frameMs = Math.Round(s.frameMs[i], 3), segment = s.segments[i],
            top = Enumerable.Range(0, recNames.Count).Where(m => !double.IsNaN(s.markers[i][m]) && recNames[m] != "PlayerLoop")
                .OrderByDescending(m => s.markers[i][m]).Take(6).ToDictionary(m => recNames[m], m => Math.Round(s.markers[i][m], 3))
        }).ToList();
        var pools = new Dictionary<string, object>();
        for (int i = 0; i < trackedPrefabs.Length; i++)
        {
            if (trackedPrefabs[i] == null) continue;
            var a = TransientVfxPool.GetStatistics(trackedPrefabs[i]);
            if (a.Requests == before[i].Requests && a.Created == before[i].Created) continue;
            pools[trackedNames[i]] = new { requests = a.Requests - before[i].Requests, created = a.Created - before[i].Created, misses = a.Misses - before[i].Misses, active = a.Active, peakActive = a.PeakActive, idle = a.Idle };
        }
        results.Add(new
        {
            count, scenario = name, frames = s.frameMs.Count, seconds, alive, casts, heavyHits, derivedHits, weakHits,
            frameMs = Sum(s.frameMs), cpuMainMs = Sum(s.cpuMs), gpuMs = Sum(s.gpuMs), drawCalls = Sum(s.drawCalls), setPass = Sum(s.setPass), gcBytes = Sum(s.gcBytes),
            auraLeasedMax, auraRegisteredMax, magnetParticipantsMax = magnetMax, magnetMovingMax, darkGatherActiveMax = darkActiveMax, lightPendingMax,
            extra = extra?.Invoke(), pools, markers = MarkerSummary(s, all), segments = segs, worst
        });
        Write("RUNNING " + count + " " + name);
        Check(alive == count, "fixture lost enemies in " + name + ": " + alive + "/" + count);
    }

    static WeaponActionResult lastHeavyResult;
    static IEnumerator StartHeavy(MeleeRuntime melee)
    {
        float until = Time.time + 3f;
        while (Time.time < until)
        {
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee.SetManualInputEnabled(true);
            lastHeavyResult = melee.TryStartHeavyAttack(Vector3.forward);
            if (lastHeavyResult == WeaponActionResult.Accepted) { casts = 1; yield break; }
            yield return null;
        }
    }

    static IEnumerator Settle(MeleeRuntime melee, float seconds)
    {
        float until = Time.time + 6f;
        while ((melee.IsAttackInProgress || DarkGatherBurstScheduler.ActiveCount > 0 || LightTripleImpactScheduler.PendingCount > 0
                || ElementChainScheduler.ActiveCastCount > 0) && Time.time < until) yield return null;
        melee.CancelCurrentAttackState();
        yield return Wait(seconds);
    }

    static void Inject(WeaponElement element, string weaponId, GameObject source, int stacks)
    {
        foreach (var e in enemies)
        {
            var s = e.GetComponent<ElementalStatusController>();
            for (int k = 0; k < stacks; k++)
                s.TryApplyDirectHit(new ElementalStatusApplication(element, 100, source, weaponId, true, false, e.transform.position, Vector3.forward));
        }
    }

    static void ClearStatuses() { foreach (var e in enemies) e.GetComponent<ElementalStatusController>().ClearAllStatuses(); }

    static IEnumerator Verify()
    {
        EnemySpawnService spawn = null; EnemyThemeDebugUI ui = null; MeleeRuntime melee = null; PlayerInputFacade input = null; bool oldGameplay = false;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase),
                "Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var heavyDef = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset");
            var hitCatalog = Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);
            hitCatalog.TryResolve(WeaponElement.Light, out var lightHitPrefab); hitCatalog.TryResolve(WeaponElement.Dark, out var darkHitPrefab); hitCatalog.TryResolve(WeaponElement.Fire, out var fireHitPrefab);
            var v = heavyDef.elementVfx;
            trackedPrefabs = new[] { v.darkGatherBurst, v.lightTripleImpact, v.lightDoubleImpact, v.fireImpact, v.FireChainExplosion, lightHitPrefab, darkHitPrefab, fireHitPrefab };
            trackedNames = new[] { "darkGatherBurst", "lightTriple", "lightDouble", "fireImpact", "fireChain", "lightHit", "darkHit", "fireHit" };
            actor.Health.SetMaxHp(1000000, true);
            ui = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include); ui.gameObject.SetActive(true); if (!ui.InArena) ui.ToggleArena();
            yield return null; yield return null;
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service");
            var theme = MapThemeCatalog.Resolve("SpiderBrood");
            Check(theme != null && theme.Validate(out _), "SpiderBrood theme");
            Check(spawn.RegisterAdditionalCatalog(theme.Catalog, out string regError), regError);
            melee = player.GetComponent<MeleeRuntime>();
            input = player; oldGameplay = input.IsGameplayEnabled; input.DisableGameplay();
            var origin = player.transform.position;
            StartRecorders();
            notes.Add(SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName + " / GameView " + Screen.width + "x" + Screen.height + " / Unity " + Application.unityVersion);
            notes.Add("Editor Play, isolated account, Hideout debug arena, theme SpiderBrood, production ratio 50=39/10/1 100=78/20/2, AI active, enemy HP 1e5, player HP 1e6, gameplay input disabled, targetFrameRate -1");
            notes.Add("SharedLocalAuraRenderer.Enabled=" + SharedLocalAuraRenderer.Enabled + " markers=" + recNames.Count);

            ItemData Equip(WeaponElement element)
            {
                melee.CancelCurrentAttackState();
                var item = new ItemData(weapon, 1, ItemGrade.Common, element: element);
                Check(actor.Equipment.EquipWeaponItem(item), "Equip " + element);
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                Warp(player, origin, Vector3.forward);
                (player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>()).Clear();
                return item;
            }
            OverburstElementEnergy Energy() => player.GetComponent<OverburstElementEnergy>();
            int seq = 50000;
            void Fill(OverburstElementEnergy e) { e.Clear(); for (int i = 0; i < 10; i++) e.RecordConfirmedHit(e.WeaponInstanceId, e.Element, ++seq, 1); }
            void Swing()
            {
                if (melee.IsAttackInProgress) return;
                melee.SetManualInputEnabled(true);
                if (melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput, null, Vector3.forward), out _) == WeaponActionResult.Accepted) casts++;
            }

            foreach (int count in new[] { 50, 100 })
            {
                foreach (var e in enemies) if (e != null) { e.Health.OnDamageResolved -= Hit; if (e.IsLeased) spawn.Release(e); }
                enemies.Clear(); ClearStatusesSafe();
                Equip(WeaponElement.Dark); yield return null;
                int medium = Mathf.RoundToInt(count * .2f), elite = count >= 100 ? 2 : 1;
                var roster = theme.BuildRoster(count - medium - elite, medium, elite, 27100);
                double spawnBegin = EditorApplication.timeSinceStartup;
                int groundLayer = LayerMask.NameToLayer("Ground"); int mask = groundLayer >= 0 ? 1 << groundLayer : Physics.DefaultRaycastLayers;
                for (int i = 0; i < count; i++)
                {
                    float angle = i * 2.399963f, radius = 4f + (i % 5) * .6f;
                    Vector3 point = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    if (Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var hit, 12, mask, QueryTriggerInteraction.Ignore)) point.y = hit.point.y + .1f;
                    Vector3 look = origin - point; look.y = 0;
                    Check(spawn.TrySpawn(new EnemySpawnRequest(roster[i], point, Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.back), player.transform), out var enemy), "spawn " + i);
                    enemy.Health.SetMaxHp(100000, true); enemy.Health.OnDamageResolved += Hit; enemies.Add(enemy);
                }
                notes.Add("spawn " + count + " syncMs=" + ((EditorApplication.timeSinceStartup - spawnBegin) * 1000).ToString("F1") + " roster: "
                    + string.Join(", ", roster.GroupBy(d => d.EnemyId).Select(g => g.Key + "=" + g.Count())));
                yield return Wait(3f);

                // ---------- Dark ----------
                var darkItem = Equip(WeaponElement.Dark); var dark = Energy();
                yield return Measure(count, "dark-idle", 4f, null, null, null);

                Inject(WeaponElement.Dark, darkItem.runtimeInstanceId, player.gameObject, 5);
                yield return Wait(0.5f);
                yield return Measure(count, "dark-corroded-all", 5f, null, null, () => new { corroded = enemies.Count(e => e.GetComponent<ElementalStatusController>().GetStackCount(WeaponElement.Dark) > 0) });
                yield return Settle(melee, 0.5f);

                Warp(player, origin, Vector3.forward);
                Inject(WeaponElement.Dark, darkItem.runtimeInstanceId, player.gameObject, 5);
                Fill(dark); Check(Mathf.Approximately(dark.Amount, 100f), "dark full");
                int bursts = DarkGatherBurstScheduler.BurstCount; float commit = -1f; int seenBursts = bursts;
                yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "dark heavy " + lastHeavyResult);
                yield return Measure(count, "dark-heavy", 5f, null, () =>
                {
                    if (commit < 0 && dark.Amount <= 0f) commit = Time.time;
                    if (DarkGatherBurstScheduler.BurstCount != seenBursts) { seenBursts = DarkGatherBurstScheduler.BurstCount; return "burst"; }
                    if (commit < 0) return "windup";
                    float t = Time.time - commit;
                    return t < 0.15f ? "slam" : DarkGatherBurstScheduler.ActiveCount > 0 ? "gather" : "post";
                }, () => new { bursts = DarkGatherBurstScheduler.BurstCount - bursts, pulled = DarkGatherBurstScheduler.LastPulledCount, burstTargets = DarkGatherBurstScheduler.LastBurstTargetCount, stackSum = DarkGatherBurstScheduler.LastBurstStackSum });
                yield return Settle(melee, 0.5f); ClearStatuses();

                Warp(player, origin, Vector3.forward); dark.Clear();
                yield return Measure(count, "dark-weak-combat", 5f, Swing, null, () => new { corroded = enemies.Count(e => e.GetComponent<ElementalStatusController>().GetStackCount(WeaponElement.Dark) > 0), energy = dark.Amount });
                yield return Settle(melee, 0.5f); ClearStatuses();

                // ---------- Light ----------
                Equip(WeaponElement.Light); var light = Energy(); yield return Wait(1f);
                SetPrivate(light, "Amount", 200f); SetPrivate(light, "RadianceStacks", 100);
                yield return Measure(count, "light-overcharged-idle", 4f, null, null, () => new { energy = light.Amount, radiance = light.RadianceStacks });

                Warp(player, origin, Vector3.forward); SetPrivate(light, "Amount", 150f); SetPrivate(light, "RadianceStacks", 50);
                yield return Measure(count, "light-weak-combat", 5f, Swing, null, () => new { energy = light.Amount, radiance = light.RadianceStacks, lightStatus = enemies.Count(e => e.GetComponent<ElementalStatusController>().GetStackCount(WeaponElement.Light) > 0) });
                yield return Settle(melee, 0.5f);

                foreach (bool triple in new[] { true, false })
                {
                    Warp(player, origin, Vector3.forward);
                    light.Clear(); SetPrivate(light, "Amount", triple ? 200f : 100f); if (triple) SetPrivate(light, "RadianceStacks", 100);
                    int dispatched = LightTripleImpactScheduler.DispatchedHitCount, seenDispatch = dispatched; float lightCommit = -1f;
                    var perHit = new List<int>();
                    yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "light heavy " + lastHeavyResult);
                    yield return Measure(count, triple ? "light-triple" : "light-double", triple ? 4.5f : 4f, null, () =>
                    {
                        if (lightCommit < 0 && light.Amount <= 0f) lightCommit = Time.time;
                        if (LightTripleImpactScheduler.DispatchedHitCount != seenDispatch)
                        { seenDispatch = LightTripleImpactScheduler.DispatchedHitCount; perHit.Add(LightTripleImpactScheduler.LastDispatchTargetCount); return "hit" + (LightTripleImpactScheduler.LastDispatchHitIndex + 1); }
                        if (lightCommit < 0) return "windup";
                        if (Time.time - lightCommit < 0.15f) return "slam";
                        return LightTripleImpactScheduler.PendingCount > 0 ? "between" : "post";
                    }, () => new { followUps = LightTripleImpactScheduler.DispatchedHitCount - dispatched, targetsPerFollowUp = perHit });
                    yield return Settle(melee, 0.5f);
                }

                // ---------- Fire reference (same fixture, upper-bound 5 stacks) ----------
                var fireItem = Equip(WeaponElement.Fire); var fire = Energy(); yield return Wait(1f);
                Inject(WeaponElement.Fire, fireItem.runtimeInstanceId, player.gameObject, 5);
                Fill(fire);
                float fireCommit = -1f;
                yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "fire heavy " + lastHeavyResult);
                yield return Measure(count, "fire-heavy-ref", 5f, null, () =>
                {
                    if (fireCommit < 0 && fire.Amount <= 0f) fireCommit = Time.time;
                    if (fireCommit < 0) return "windup";
                    if (Time.time - fireCommit < 0.15f) return "slam";
                    return ElementChainScheduler.ActiveCastCount > 0 ? "propagation" : "post";
                }, null);
                yield return Settle(melee, 0.5f); ClearStatuses();
            }
        }
        finally
        {
            melee?.CancelCurrentAttackState();
            foreach (var e in enemies) if (e != null) { e.Health.OnDamageResolved -= Hit; if (spawn != null && e.IsLeased) spawn.Release(e); }
            enemies.Clear();
            if (input != null && oldGameplay) input.EnableGameplay();
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    static void ClearStatusesSafe() { foreach (var e in enemies) if (e != null) e.GetComponent<ElementalStatusController>()?.ClearAllStatuses(); }
}
