using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

// Local-only crowd cost probe for the burning aura (FireAura). 100 enemies at the production ratio 78/20/2,
// SpiderBrood theme, Hideout debug arena, real AI/movement/animation, high HP, gameplay input isolated.
// Scenarios: no status, fire 1 and 5 stacks, shock 5 stacks (reference), and fire 5 stacks with the previous
// vendor fire (Fire Loop sim 1) swapped into the pooled auras at runtime (nothing is saved).
// Isolated account (OVERBURST_SAVE_DIRECTORY); exits Play itself. Editor numbers, not a Player-build FPS guarantee.
[InitializeOnLoad]
public static class BurnAuraCrowdPerfProbe
{
    const string Key = "BurnAuraCrowdPerfProbe";
    const string OldFire = "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle 02/Realistic Environmental Fire and Explosions Pack/Fire Loop sim 1.prefab";
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<object> results = new List<object>();
    static readonly List<string> errors = new List<string>();
    static readonly List<string> notes = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static BurnAuraCrowdPerfProbe() { EditorApplication.playModeStateChanged += State; }

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
            results.Clear(); errors.Clear(); notes.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 600;
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
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }

    static void Write(string status) => File.WriteAllText(Path.Combine(Output, "perf-results.json"),
        JsonConvert.SerializeObject(new { status, environment = notes, results, errors }, Formatting.Indented));

    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        Write(status);
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    // ---------------- recorders ----------------
    static readonly List<ProfilerRecorder> recs = new List<ProfilerRecorder>();
    static readonly List<string> recNames = new List<string>();
    static ProfilerRecorder draws, setpass;
    static readonly FrameTiming[] timing = new FrameTiming[1];
    static readonly HashSet<string> engineMarkers = new HashSet<string>
    {
        "PlayerLoop", "ParticleSystem.UpdateJob", "ParticleSystem.GeometryJob", "ParticleSystem.Draw", "ParticleSystem.ScheduleJobs",
        "ParticleSystem.WaitForPreviousRenderingToFinish", "PreLateUpdate.ParticleSystemBeginUpdateAll", "Gfx.WaitForPresentOnGfxThread",
        "RenderLoop.DrawSRPBatcher", "Update.ScriptRunBehaviourUpdate", "PostLateUpdate.FinishFrameRendering",
        "PreLateUpdate.ScriptRunBehaviourLateUpdate", "PostLateUpdate.PlayerUpdateCanvases"
    };
    // dot mode: the damage-number popups (pool of DamageNumberSpawner.DefaultPopupBudget), counted by activeSelf each frame.
    static readonly List<DamageNumberPopup> popups = new List<DamageNumberPopup>();

    static void StartRecorders()
    {
        StopRecorders();
        foreach (var f in typeof(ElementCombatCostMarkers).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) f.GetValue(null);
        var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var h in handles)
        {
            var d = ProfilerRecorderHandle.GetDescription(h);
            if ((d.Name.StartsWith("Overburst.") || engineMarkers.Contains(d.Name)) && !recNames.Contains(d.Name))
            { recNames.Add(d.Name); recs.Add(ProfilerRecorder.StartNew(d.Category, d.Name, 1)); }
        }
        draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
        setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
    }

    static void StopRecorders()
    {
        foreach (var r in recs) r.Dispose(); recs.Clear(); recNames.Clear();
        draws.Dispose(); setpass.Dispose();
    }

    static object Sum(IEnumerable<double> source)
    {
        var a = source.Where(x => !double.IsNaN(x)).ToList();
        if (a.Count == 0) return null;
        a.Sort();
        return new { avg = Math.Round(a.Average(), 3), p95 = Math.Round(a[Math.Min(a.Count - 1, (int)(a.Count * .95))], 3), max = Math.Round(a[a.Count - 1], 3) };
    }

    static readonly List<EnemyActor> enemies = new List<EnemyActor>();

    static (int auras, int particles) Count(MeleeElementStatusAuraType type)
    {
        int auras = 0, particles = 0;
        foreach (var p in UnityEngine.Object.FindObjectsByType<MeleeElementStatusAuraPresentation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var go = p.GetAuraObject(type);
            if (go == null || !go.activeInHierarchy) continue;
            auras++;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) particles += ps.particleCount;
        }
        return (auras, particles);
    }

    static IEnumerator Measure(string name, float seconds, MeleeElementStatusAuraType? aura)
    {
        var frameMs = new List<double>(); var cpu = new List<double>(); var gpu = new List<double>();
        var dc = new List<double>(); var sp = new List<double>(); var markers = new List<double[]>();
        (int auras, int particles) mid = (0, 0);
        var shown = new List<double>(); int atBudget = 0;
        float start = Time.time, end = Time.time + seconds; bool counted = false;
        while (Time.time < end)
        {
            yield return null;
            frameMs.Add(Time.unscaledDeltaTime * 1000.0);
            if (popups.Count > 0)
            {
                int active = 0;
                foreach (var p in popups) if (p != null && p.gameObject.activeSelf) active++;
                shown.Add(active);
                if (active >= popups.Count) atBudget++;
            }
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                if (timing[0].cpuMainThreadFrameTime > 0 && timing[0].cpuMainThreadFrameTime < 1000) cpu.Add(timing[0].cpuMainThreadFrameTime);
                if (timing[0].gpuFrameTime > 0 && timing[0].gpuFrameTime < 1000) gpu.Add(timing[0].gpuFrameTime);
            }
            if (draws.Valid) dc.Add(draws.LastValue);
            if (setpass.Valid) sp.Add(setpass.LastValue);
            var row = new double[recs.Count];
            for (int i = 0; i < recs.Count; i++) row[i] = recs[i].Valid ? recs[i].LastValue / 1000000.0 : double.NaN;
            markers.Add(row);
            if (!counted && aura.HasValue && Time.time - start > seconds * .5f) { mid = Count(aura.Value); counted = true; }
        }
        var markerMap = new Dictionary<string, object>();
        for (int m = 0; m < recNames.Count; m++)
        {
            var values = markers.Select(r => r[m]).Where(x => !double.IsNaN(x)).ToList();
            if (values.Count == 0 || values.Max() < 0.0005) continue;
            markerMap[recNames[m]] = Sum(values);
        }
        int alive = enemies.Count(e => e != null && e.gameObject.activeInHierarchy && !e.Health.IsDead);
        results.Add(new
        {
            scenario = name, frames = frameMs.Count, seconds, alive, activeAuras = mid.auras, auraParticles = mid.particles,
            auraLeasedMax = MeleeElementStatusAuraVisibilityScheduler.LeasedPresentationCount,
            frameMs = Sum(frameMs), cpuMainMs = Sum(cpu), gpuMs = Sum(gpu), drawCalls = Sum(dc), setPass = Sum(sp), markers = markerMap,
            popupsShown = Sum(shown), popupPool = popups.Count, framesPoolFull = atBudget,
            styled = DamageNumberStyleSettings.Enabled
        });
        Write("RUNNING " + name);
        Check(alive == enemies.Count, "fixture lost enemies in " + name + ": " + alive + "/" + enemies.Count);
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

    static void ClearStatuses() { foreach (var e in enemies) if (e != null) e.GetComponent<ElementalStatusController>()?.ClearAllStatuses(); }

    public static void RunSpike(string output) { SessionState.SetString(Key + ".mode", "spike"); Run(output); }
    public static void RunDotCrowd(string output) { SessionState.SetString(Key + ".mode", "dot"); Run(output); }

    // 2026-09-30: 100 enemies burning at fire 5 stacks, every tick showing its damage number, with the previous
    // damage-number look and the per-type look (DamageNumberStyleSettings) alternated twice on the same fixture.
    // Records frame cost plus how many popups were on screen and how many frames the popup pool was full.
    static IEnumerator DotCrowd(string weaponId, GameObject source)
    {
        SessionState.SetString(Key + ".mode", "");
        popups.Clear();
        popups.AddRange(UnityEngine.Object.FindObjectsByType<DamageNumberPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        notes.Add("damage popups pooled=" + popups.Count + " styleDefault=" + DamageNumberStyleSettings.Enabled);
        bool original = DamageNumberStyleSettings.Enabled;
        try
        {
            DamageNumberStyleSettings.SetEnabled(false);
            yield return Measure("idle", 3f, null);
            yield return Wait(1f);
            foreach (var (label, styled) in new[] { ("dot-legacy-1", false), ("dot-styled-1", true), ("dot-legacy-2", false), ("dot-styled-2", true) })
            {
                DamageNumberStyleSettings.SetEnabled(styled);
                Inject(WeaponElement.Fire, weaponId, source, 5);
                yield return Wait(.7f); // popups ramp up to a steady count
                yield return Measure(label, 4f, MeleeElementStatusAuraType.Burning); // burn lasts 5 s, ends inside it
                ClearStatuses();
                yield return Wait(1.5f); // let the last popups expire before the next look
            }
        }
        finally
        {
            DamageNumberStyleSettings.SetEnabled(original);
            popups.Clear();
        }
    }
    public static void RunProfile(string output) { SessionState.SetString(Key + ".mode", "profile"); Run(output); }
    // Same, but the status is applied from EditorApplication.update (the RunSpike path) and the Editor loop is profiled too.
    public static void RunProfileEditor(string output) { SessionState.SetString(Key + ".mode", "profile-editor"); Run(output); }

    // Records the Editor profiler around the session's first fire status (one monster) and a second one for
    // comparison, then dumps the main-thread hierarchy of the apply frame and the next frames.
    static IEnumerator ProfileFirstFire(string weaponId, GameObject source, bool editorPath)
    {
        SessionState.SetString(Key + ".mode", "");
        var fireMaterials = new[] { "Fire_AlphaDistort_Ramped", "Flecks_Alpha", "FireLick_Flip1", "FireLick_Flip2", "Smoke_Harsh_Alpha" };
        notes.Add("fire materials loaded before first fire: " + string.Join(",", fireMaterials.Select(n =>
            n + "=" + Resources.FindObjectsOfTypeAll<Material>().Any(m => m.name == n))));
        yield return Wait(1f);
        UnityEditorInternal.ProfilerDriver.ClearAllFrames();
        UnityEditorInternal.ProfilerDriver.profileEditor = editorPath;
        UnityEditorInternal.ProfilerDriver.enabled = true;
        yield return Wait(1f);
        string[] labels = { "first", "second" };
        Action pending = null;
        UnityEngine.Events.UnityAction hook = () => { var a = pending; pending = null; a?.Invoke(); };
        // Apply inside the player loop (like a real hit), so the profiler records it; EditorApplication.update is opaque.
        Application.onBeforeRender += hook;
        try
        {
            for (int k = 0; k < labels.Length; k++)
            {
                var e = enemies[k]; string marker = "Probe.Apply." + labels[k];
                var loadedBefore = LoadedAssetIds();
                for (int f = 0; f < 5; f++) yield return null; // keep the scan's cost out of the apply frame
                pending = () =>
                {
                    using (new ProfilerMarker(marker).Auto())
                        e.GetComponent<ElementalStatusController>().TryApplyDirectHit(
                            new ElementalStatusApplication(WeaponElement.Fire, 100, source, weaponId, true, false, e.transform.position, Vector3.forward));
                };
                while (pending != null)
                {
                    if (editorPath) { var a = pending; pending = null; a(); break; }
                    yield return null;
                }
                for (int f = 0; f < 12; f++) yield return null;
                results.Add(new { label = labels[k], offset = -1, frame = -1, newlyLoaded = NewlyLoaded(loadedBefore) });
                ClearStatuses();
                yield return Wait(1.5f);
            }
        }
        finally { Application.onBeforeRender -= hook; }
        UnityEditorInternal.ProfilerDriver.enabled = false;
        UnityEditorInternal.ProfilerDriver.profileEditor = false;
        for (int f = 0; f < 3; f++) yield return null;
        int first = UnityEditorInternal.ProfilerDriver.firstFrameIndex, last = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
        notes.Add("profiler frames " + first + ".." + last);
        UnityEditorInternal.ProfilerDriver.SaveProfile(Path.Combine(Output, "first-fire.data"));
        for (int k = 0; k < labels.Length; k++)
        {
            int applyFrame = -1;
            for (int fr = first; fr <= last && applyFrame < 0; fr++)
                if (FrameHasSample(fr, "Probe.Apply." + labels[k])) applyFrame = fr;
            notes.Add(labels[k] + " apply frame " + applyFrame);
            if (applyFrame < 0) continue;
            for (int o = 0; o < 4 && applyFrame + o <= last; o++) results.Add(DumpFrame(applyFrame + o, labels[k], o));
        }
        var slowest = new List<(int frame, float ms)>();
        for (int fr = first; fr <= last; fr++)
            using (var view = View(fr)) if (view != null && view.valid) slowest.Add((fr, view.frameTimeMs));
        foreach (var s in slowest.OrderByDescending(s => s.ms).Take(3)) results.Add(DumpFrame(s.frame, "slowest", 0));
    }

    static HashSet<int> LoadedAssetIds()
    {
        var ids = new HashSet<int>();
        foreach (var o in Resources.FindObjectsOfTypeAll<Material>()) ids.Add(o.GetInstanceID());
        foreach (var o in Resources.FindObjectsOfTypeAll<Texture>()) ids.Add(o.GetInstanceID());
        foreach (var o in Resources.FindObjectsOfTypeAll<Shader>()) ids.Add(o.GetInstanceID());
        foreach (var o in Resources.FindObjectsOfTypeAll<Mesh>()) ids.Add(o.GetInstanceID());
        return ids;
    }

    static List<string> NewlyLoaded(HashSet<int> before)
    {
        var list = new List<string>();
        void Scan<T>() where T : UnityEngine.Object
        {
            foreach (var o in Resources.FindObjectsOfTypeAll<T>())
                if (!before.Contains(o.GetInstanceID()))
                    list.Add(typeof(T).Name + " " + o.name + " | " + AssetDatabase.GetAssetPath(o));
        }
        Scan<Material>(); Scan<Texture>(); Scan<Shader>(); Scan<Mesh>();
        return list;
    }

    static UnityEditor.Profiling.HierarchyFrameDataView View(int frame) =>
        UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(frame, 0,
            UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
            UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime, false);

    static bool FrameHasSample(int frame, string name)
    {
        using (var view = View(frame))
        {
            if (view == null || !view.valid) return false;
            var stack = new Stack<int>(); stack.Push(view.GetRootItemID()); var kids = new List<int>();
            while (stack.Count > 0)
            {
                int id = stack.Pop(); kids.Clear(); view.GetItemChildren(id, kids);
                foreach (int c in kids) { if (view.GetItemName(c) == name) return true; stack.Push(c); }
            }
            return false;
        }
    }

    static object DumpFrame(int frame, string label, int offset)
    {
        using (var view = View(frame))
        {
            if (view == null || !view.valid) return new { label, offset, frame, invalid = true };
            int T = UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime, S = UnityEditor.Profiling.HierarchyFrameDataView.columnSelfTime,
                G = UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory, C = UnityEditor.Profiling.HierarchyFrameDataView.columnCalls;
            var rows = new List<object>(); var selfs = new List<(string path, float ms)>();
            void Walk(int id, string path, int depth)
            {
                var kids = new List<int>(); view.GetItemChildren(id, kids);
                foreach (int c in kids)
                {
                    string n = view.GetItemName(c); float total = view.GetItemColumnDataAsFloat(c, T), self = view.GetItemColumnDataAsFloat(c, S);
                    string p = path.Length == 0 ? n : path + " > " + n;
                    if (self >= .2f) selfs.Add((p, self));
                    if (total < .3f) continue;
                    if (depth <= 16) rows.Add(new { depth, name = n, totalMs = Math.Round(total, 2), selfMs = Math.Round(self, 2),
                        calls = (int)view.GetItemColumnDataAsFloat(c, C), gcKB = Math.Round(view.GetItemColumnDataAsFloat(c, G) / 1024f, 1) });
                    if (depth < 30) Walk(c, p, depth + 1);
                }
            }
            Walk(view.GetRootItemID(), "", 0);
            return new { label, offset, frame, frameMs = Math.Round(view.frameTimeMs, 2),
                topSelf = selfs.OrderByDescending(s => s.ms).Take(25).Select(s => new { s.path, ms = Math.Round(s.ms, 2) }).ToList(), hierarchy = rows };
        }
    }

    static int PresentationCount() =>
        UnityEngine.Object.FindObjectsByType<MeleeElementStatusAuraPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

    // Frame cost of the moment a status is applied: session first, single monsters, and 100 at once for fire,
    // electric and dark; then with the idle pool drained and warm-up paused (the capped short-pool path).
    static IEnumerator Spike(string weaponId, GameObject source)
    {
        SessionState.SetString(Key + ".mode", "");
        var scheduler = UnityEngine.Object.FindFirstObjectByType<MeleeElementStatusAuraVisibilityScheduler>();
        Check(scheduler != null, "aura scheduler");
        notes.Add("pool before idle: created=" + scheduler.CreatedPresentationCountForValidation + " warmTarget=" + scheduler.WarmTargetForValidation);
        notes.Add("warmPlayDone=" + scheduler.WarmPlayDoneForValidation + " cameras=" + string.Join(",",
            UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(c => c.name)));
        yield return Measure("idle", 3f, null);
        yield return Wait(1f); // Measure ends with a JSON write; keep that cost out of the first event's frames
        IEnumerator Event(string label, IList<EnemyActor> targets, WeaponElement element)
        {
            int before = PresentationCount(), poolBefore = scheduler.CreatedPresentationCountForValidation;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var e in targets)
                e.GetComponent<ElementalStatusController>().TryApplyDirectHit(
                    new ElementalStatusApplication(element, 100, source, weaponId, true, false, e.transform.position, Vector3.forward));
            double syncMs = sw.Elapsed.TotalMilliseconds;
            var frames = new List<double>(); var cpu = new List<double>(); int fullAt = -1;
            for (int f = 0; f < 40; f++)
            {
                yield return null;
                frames.Add(Math.Round(Time.unscaledDeltaTime * 1000.0, 2));
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].cpuMainThreadFrameTime > 0 && timing[0].cpuMainThreadFrameTime < 1000)
                    cpu.Add(Math.Round(timing[0].cpuMainThreadFrameTime, 2));
                if (fullAt < 0 && MeleeElementStatusAuraVisibilityScheduler.LeasedPresentationCount >= targets.Count) fullAt = f;
            }
            int created = PresentationCount() - before;
            double max = frames.Max();
            results.Add(new { scenario = label, targets = targets.Count, poolBefore, created, leasedAfter40 = MeleeElementStatusAuraVisibilityScheduler.LeasedPresentationCount,
                allShownAtFrame = fullAt, syncApplyMs = Math.Round(syncMs, 2), maxFrameMs = max, maxAt = frames.IndexOf(max),
                medianFrameMs = frames.OrderBy(x => x).ElementAt(frames.Count / 2), maxCpuMs = cpu.Count > 0 ? cpu.Max() : 0, first10 = frames.Take(10) });
            Write("RUNNING " + label);
            ClearStatuses();
            yield return Wait(1.5f);
        }
        for (int i = 0; i < 5; i++) yield return Event("single-fire-" + i, new[] { enemies[i] }, WeaponElement.Fire);
        yield return Event("burst100-fire", enemies, WeaponElement.Fire);
        yield return Event("burst100-electric", enemies, WeaponElement.Electric);
        yield return Event("burst100-dark", enemies, WeaponElement.Dark);
        scheduler.WarmPoolEnabledForValidation = false;
        yield return null; scheduler.DrainPoolForValidation(); yield return null;
        yield return Event("burst100-fire-drained", enemies, WeaponElement.Fire);
        scheduler.WarmPoolEnabledForValidation = true;
        yield return Wait(2.5f);
        notes.Add("pool after refill: created=" + scheduler.CreatedPresentationCountForValidation + " warmTarget=" + scheduler.WarmTargetForValidation);
        yield return Measure("idle-after", 3f, null);
    }

    // Runtime-only: put the previous vendor fire into every pooled aura, as the module held it (0.45, y 0.45).
    static int SwapToOldFire()
    {
        var old = AssetDatabase.LoadAssetAtPath<GameObject>(OldFire);
        Check(old != null, "old fire prefab");
        int swapped = 0;
        foreach (var p in UnityEngine.Object.FindObjectsByType<MeleeElementStatusAuraPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var fire = p.GetAuraObject(MeleeElementStatusAuraType.Burning);
            if (fire == null) continue;
            for (int i = fire.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(fire.transform.GetChild(i).gameObject);
            var inst = UnityEngine.Object.Instantiate(old, fire.transform, false);
            inst.transform.localPosition = new Vector3(0f, .45f, 0f); inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one * .45f;
            p.RebuildModulesForValidation(); p.ClearAllAuras();
            swapped++;
        }
        return swapped;
    }

    static IEnumerator Verify()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; PlayerInputFacade input = null; bool oldGameplay = false;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase),
                "Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return null; yield return null;
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service");
            var theme = MapThemeCatalog.Resolve("SpiderBrood");
            Check(theme != null && theme.Validate(out _), "SpiderBrood theme");
            Check(spawn.RegisterAdditionalCatalog(theme.Catalog, out string regError), regError);
            input = player; oldGameplay = input.IsGameplayEnabled; input.DisableGameplay();
            var origin = player.transform.position;
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            string weaponId = energy.WeaponInstanceId;
            StartRecorders();
            notes.Add(SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName + " / GameView " + Screen.width + "x" + Screen.height + " / Unity " + Application.unityVersion);
            notes.Add("Editor Play, isolated account, Hideout debug arena, SpiderBrood 100 = 78/20/2 (seed 27100), AI active, enemy HP 1e5, player HP 1e6, gameplay input disabled, targetFrameRate -1");
            notes.Add("SharedLocalAuraRenderer.Enabled=" + SharedLocalAuraRenderer.Enabled + " markers=" + recNames.Count);

            const int count = 100;
            var roster = theme.BuildRoster(count - 20 - 2, 20, 2, 27100);
            int groundLayer = LayerMask.NameToLayer("Ground"); int mask = groundLayer >= 0 ? 1 << groundLayer : Physics.DefaultRaycastLayers;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.399963f, radius = 4f + (i % 5) * .6f;
                Vector3 point = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                if (Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var hit, 12, mask, QueryTriggerInteraction.Ignore)) point.y = hit.point.y + .1f;
                Vector3 look = origin - point; look.y = 0;
                Check(spawn.TrySpawn(new EnemySpawnRequest(roster[i], point, Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.back), player.transform), out var enemy), "spawn " + i);
                enemy.Health.SetMaxHp(100000, true); enemies.Add(enemy);
            }
            notes.Add("roster: " + string.Join(", ", roster.GroupBy(d => d.EnemyId).Select(g => g.Key + "=" + g.Count())));
            yield return Wait(3f);

            if (SessionState.GetString(Key + ".mode", "") == "spike") { yield return Spike(weaponId, player.gameObject); yield break; }
            if (SessionState.GetString(Key + ".mode", "") == "dot") { yield return DotCrowd(weaponId, player.gameObject); yield break; }
            if (SessionState.GetString(Key + ".mode", "") == "profile") { yield return ProfileFirstFire(weaponId, player.gameObject, false); yield break; }
            if (SessionState.GetString(Key + ".mode", "") == "profile-editor") { yield return ProfileFirstFire(weaponId, player.gameObject, true); yield break; }
            yield return Measure("idle", 4f, null);
            foreach (var (label, element, stacks, type) in new[]
            {
                ("fire1-FireAura", WeaponElement.Fire, 1, MeleeElementStatusAuraType.Burning),
                ("fire5-FireAura", WeaponElement.Fire, 5, MeleeElementStatusAuraType.Burning),
                ("shock5-reference", WeaponElement.Electric, 5, MeleeElementStatusAuraType.Shocked),
            })
            {
                Inject(element, weaponId, player.gameObject, stacks);
                yield return Wait(.5f);
                yield return Measure(label, 4f, type);
                ClearStatuses(); yield return Wait(1f);
            }
            notes.Add("swapped pooled auras to Fire Loop sim 1: " + SwapToOldFire());
            yield return Wait(.5f);
            Inject(WeaponElement.Fire, weaponId, player.gameObject, 5);
            yield return Wait(.5f);
            yield return Measure("fire5-oldFireLoop", 4f, MeleeElementStatusAuraType.Burning);
            ClearStatuses(); yield return Wait(.5f);
            yield return Measure("idle-after", 3f, null);
        }
        finally
        {
            foreach (var e in enemies) if (e != null && spawn != null && e.IsLeased) spawn.Release(e);
            enemies.Clear();
            if (input != null && oldGameplay) input.EnableGameplay();
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }
}
