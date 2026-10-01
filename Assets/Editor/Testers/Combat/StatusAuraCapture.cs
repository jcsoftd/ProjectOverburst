using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: Game view screenshots of the burning (FireAura) and corrosion auras on monsters of different sizes,
// at several stack counts, in real Play Mode. Isolated account (OVERBURST_SAVE_DIRECTORY); exits Play itself.
[InitializeOnLoad]
public static class StatusAuraCapture
{
    const string Key = "StatusAuraCapture";
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<string> shots = new List<string>();
    static readonly List<string> notes = new List<string>();
    static readonly List<string> errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static StatusAuraCapture() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output) => Start(output, "groups");
    // Every monster in the debug tables, three per shot, fire 1 and 5 stacks; writes screen boxes for close-up crops.
    public static void RunAll(string output) => Start(output, "all");
    // Corrosion aura at 5 stacks: current rule, then the same aura rescaled to the previous rule
    // (clamp(R/0.6, 0.45, 3) x 0.8, stack size 1.1) on the same monsters. Runtime-only, nothing is saved.
    public static void RunDarkCompare(string output) => Start(output, "dark");
    // Corrosion aura red layers: left monster restored to the red size before the 0.6x shrink, right current.
    public static void RunDarkRedCompare(string output) => Start(output, "darkred");
    // Same monster three times: red layers at the original size, at 0.6x, and current (0.4x). Emission radius as saved.
    public static void RunDarkRed3(string output) => Start(output, "darkred3");
    static readonly string[] DarkRedLayers = { "Crescent_Halftone_Add", "Flare_Glowdot", "ImpactLightrays_Blurry" };

    static void Start(string output, string mode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".mode", mode);
        SessionState.SetString(Key + ".output", output);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
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
            Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 300;
            string mode = SessionState.GetString(Key + ".mode", "groups");
            stack.Clear(); work = mode == "all" ? CaptureAll() : mode == "darkred3" ? DarkRed3() : mode == "dark" || mode == "darkred" ? DarkCompare(mode == "darkred") : Capture(); boxes.Clear();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish("COMPLETE"); }
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

    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "capture-results.json"), JsonConvert.SerializeObject(new { status, shots, notes, errors, boxes }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    static IEnumerator Shot(string name)
    {
        string path = Path.Combine(Output, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        shots.Add(path);
        yield return null; yield return null;
    }

    static void Apply(EnemyActor e, WeaponElement element, int times, GameObject source, string weaponId)
    {
        var s = e.GetComponent<ElementalStatusController>();
        for (int k = 0; k < times; k++)
            s.TryApplyDirectHit(new ElementalStatusApplication(element, 10, source, weaponId, true, false, e.transform.position, Vector3.forward));
    }

    static void Measure(string tag, EnemyActor e)
    {
        var volume = CombatTargetVfxPlacement.ResolveVolume(e.GetComponent<CombatTarget>());
        var pres = e.GetComponentInChildren<MeleeElementStatusAuraPresentation>();
        string Aura(MeleeElementStatusAuraType t)
        {
            var go = pres != null ? pres.GetAuraObject(t) : null;
            if (go == null || !go.activeInHierarchy) return t + " off";
            float y = (go.transform.position.y - (volume.Center.y - volume.HalfHeight)) / Mathf.Max(.01f, volume.HalfHeight * 2f);
            return t + " worldScale " + go.transform.lossyScale.x.ToString("F2") + " heightFrac " + y.ToString("F2")
                + " alive " + go.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount);
        }
        notes.Add(tag + " " + e.name + " R " + volume.Radius.ToString("F2") + " H " + (volume.HalfHeight * 2f).ToString("F2")
            + " | " + Aura(MeleeElementStatusAuraType.Burning) + " | " + Aura(MeleeElementStatusAuraType.Corroded));
    }

    static readonly List<object> boxes = new List<object>();

    // Screen-space box (top-left origin, pixels) of the monster's meshes, plus the body center and fire origin.
    static void Box(string shot, EnemyActor e, Camera cam)
    {
        var aura = e.GetComponentInChildren<MeleeElementStatusAuraPresentation>();
        var bounds = new Bounds(); bool any = false;
        foreach (var r in e.GetComponentsInChildren<Renderer>())
        {
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer) || !r.enabled) continue;
            if (aura != null && r.transform.IsChildOf(aura.transform)) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        if (!any) return;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var c = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var s = cam.WorldToScreenPoint(c);
            x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
        }
        var volume = CombatTargetVfxPlacement.ResolveVolume(e.GetComponent<CombatTarget>());
        var fire = aura != null ? aura.GetAuraObject(MeleeElementStatusAuraType.Burning) : null;
        Vector3 Sp(Vector3 w) { var s = cam.WorldToScreenPoint(w); return new Vector3(s.x, Screen.height - s.y, 0f); }
        var center = Sp(volume.Center); var origin = fire != null ? Sp(fire.transform.position) : center;
        boxes.Add(new
        {
            shot, id = e.name.Replace("(Clone)", "").Replace("PF_", ""), screenW = Screen.width, screenH = Screen.height,
            x0 = Mathf.RoundToInt(x0), y0 = Mathf.RoundToInt(Screen.height - y1), x1 = Mathf.RoundToInt(x1), y1 = Mathf.RoundToInt(Screen.height - y0),
            cx = Mathf.RoundToInt(center.x), cy = Mathf.RoundToInt(center.y), fx = Mathf.RoundToInt(origin.x), fy = Mathf.RoundToInt(origin.y),
            radius = Math.Round(volume.Radius, 2), height = Math.Round(volume.HalfHeight * 2f, 2),
            meshSize = new[] { Math.Round(bounds.size.x, 2), Math.Round(bounds.size.y, 2), Math.Round(bounds.size.z, 2) },
            meshCenterY = Math.Round(bounds.center.y - e.transform.position.y, 2), bodyCenterY = Math.Round(volume.Center.y - e.transform.position.y, 2),
            authored = e.TryGetComponent(out CombatTargetVfxPlacement placement)
        });
    }

    static IEnumerator CaptureAll()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current;
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.5f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null)
                .GroupBy(d => d.EnemyId).Select(g => g.First()).OrderBy(d => d.EnemyId, StringComparer.Ordinal).ToArray();
            notes.Add("definitions " + defs.Length + ": " + string.Join(", ", defs.Select(d => d.EnemyId)));
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            yield return Wait(1f);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            float[] xs = { .36f, .6f, .84f };
            for (int b = 0; b * 3 < defs.Length; b++)
            {
                var row = new List<EnemyActor>();
                for (int i = 0; i < 3 && b * 3 + i < defs.Length; i++)
                {
                    Vector3 at = Ground(xs[i], .3f); Vector3 f = cam.transform.position - at; f.y = 0f;
                    if (!spawn.TrySpawn(new EnemySpawnRequest(defs[b * 3 + i], at, Quaternion.LookRotation(f), player.transform), out var e)) throw new Exception("Spawn " + defs[b * 3 + i].EnemyId);
                    leased.Add(e); row.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                }
                yield return Wait(0.3f);
                int applied = 0;
                foreach (int target in new[] { 1, 5 })
                {
                    foreach (var e in row) Apply(e, WeaponElement.Fire, target - applied, player.gameObject, energy.WeaponInstanceId);
                    applied = target;
                    yield return Wait(1.4f);
                    string tag = "All" + b.ToString("00") + "_Fire" + target;
                    foreach (var e in row) Box(tag, e, cam);
                    yield return Shot(tag);
                }
                foreach (var e in row) if (e != null && e.IsLeased) spawn.Release(e);
                leased.RemoveAll(e => row.Contains(e));
                yield return Wait(0.4f);
            }
        }
        finally
        {
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    static IEnumerator DarkCompare(bool redOnly)
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current;
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.5f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            EnemyDefinition Def(string id) => defs.FirstOrDefault(d => d.EnemyId == id) ?? defs.First(d => d.EnemyId.EndsWith("_" + id) || d.EnemyId.Contains(id));
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            yield return Wait(1f);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            string[] ids = { "BoneAsh", "Horridomorph", "Limadon", "Gorhorrid", "Venodonte_Tint1", "Occisodonte", "Kupolobrach", "Ursacetus", "Carcinoptera" };
            for (int g = 0; g < ids.Length; g++)
            {
                // Same monster twice at the same moment: left rescaled to the previous rule, right current.
                var row = new List<EnemyActor>();
                foreach (float x in new[] { .44f, .74f })
                {
                    Vector3 at = Ground(x, .3f); Vector3 f = cam.transform.position - at; f.y = 0f;
                    if (!spawn.TrySpawn(new EnemySpawnRequest(Def(ids[g]), at, Quaternion.LookRotation(f), player.transform), out var e)) throw new Exception("Spawn " + ids[g]);
                    leased.Add(e); row.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                }
                yield return Wait(0.3f);
                foreach (var e in row) Apply(e, WeaponElement.Dark, 5, player.gameObject, energy.WeaponInstanceId);
                yield return null; yield return null;
                var old = row[0];
                var volume = CombatTargetVfxPlacement.ResolveVolume(old.GetComponent<CombatTarget>());
                float oldSize = Mathf.Clamp(volume.Radius / .6f, .45f, 3f) * .8f * 1.1f;
                float newSize = Mathf.Clamp(Mathf.Min(volume.Radius / .6f, volume.HalfHeight * 2f / .7f), .45f, 3f) * .7f * 1f;
                var aura = old.GetComponentInChildren<MeleeElementStatusAuraPresentation>()?.GetAuraObject(MeleeElementStatusAuraType.Corroded);
                if (aura == null) throw new Exception("No corroded aura on " + ids[g]);
                if (redOnly)
                {
                    int restored = 0;
                    foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (Array.IndexOf(DarkRedLayers, ps.name) < 0) continue;
                        var main = ps.main; main.startSizeMultiplier /= .6f;
                        var shape = ps.shape; if (shape.enabled) shape.radius /= .6f;
                        restored++;
                    }
                    notes.Add(ids[g] + " red layers restored on left: " + restored);
                }
                else aura.transform.localScale *= oldSize / newSize;
                notes.Add(ids[g] + " R " + volume.Radius.ToString("F2") + " H " + (volume.HalfHeight * 2f).ToString("F2")
                    + " old " + oldSize.ToString("F2") + " new " + newSize.ToString("F2") + " ratio " + (newSize / oldSize).ToString("F2"));
                yield return Wait(1.6f);
                string tag = "Dark_" + g.ToString("00");
                Box(tag + "_old", row[0], cam); Box(tag + "_new", row[1], cam);
                yield return Shot(tag);
                foreach (var e in row) if (e != null && e.IsLeased) spawn.Release(e);
                leased.RemoveAll(e => row.Contains(e));
                yield return Wait(0.4f);
            }
        }
        finally
        {
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    static IEnumerator DarkRed3()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current;
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.5f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            EnemyDefinition Def(string id) => defs.FirstOrDefault(d => d.EnemyId == id) ?? defs.First(d => d.EnemyId.EndsWith("_" + id) || d.EnemyId.Contains(id));
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            yield return Wait(1f);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            string[] ids = { "BoneAsh", "Horridomorph", "Limadon", "Gorhorrid", "Occisodonte", "Ursacetus" };
            // Particle size multiplier relative to the saved (0.4x) size; emission radius is left as saved.
            var states = new (string tag, float x, float size)[] { ("orig", .36f, 1f / .4f), ("mid", .6f, .6f / .4f), ("new", .84f, 1f) };
            for (int g = 0; g < ids.Length; g++)
            {
                var row = new List<EnemyActor>();
                foreach (var s in states)
                {
                    Vector3 at = Ground(s.x, .3f); Vector3 f = cam.transform.position - at; f.y = 0f;
                    if (!spawn.TrySpawn(new EnemySpawnRequest(Def(ids[g]), at, Quaternion.LookRotation(f), player.transform), out var e)) throw new Exception("Spawn " + ids[g]);
                    leased.Add(e); row.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                }
                yield return Wait(0.3f);
                foreach (var e in row) Apply(e, WeaponElement.Dark, 5, player.gameObject, energy.WeaponInstanceId);
                yield return null; yield return null;
                for (int i = 0; i < row.Count; i++)
                {
                    if (Mathf.Approximately(states[i].size, 1f)) continue;
                    var aura = row[i].GetComponentInChildren<MeleeElementStatusAuraPresentation>()?.GetAuraObject(MeleeElementStatusAuraType.Corroded);
                    if (aura == null) throw new Exception("No corroded aura on " + ids[g]);
                    foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (Array.IndexOf(DarkRedLayers, ps.name) < 0) continue;
                        var main = ps.main; main.startSizeMultiplier *= states[i].size;
                    }
                }
                yield return Wait(1.6f);
                string tag = "Red_" + g.ToString("00");
                for (int i = 0; i < row.Count; i++) Box(tag + "_" + states[i].tag, row[i], cam);
                yield return Shot(tag);
                foreach (var e in row) if (e != null && e.IsLeased) spawn.Release(e);
                leased.RemoveAll(e => row.Contains(e));
                yield return Wait(0.4f);
            }
        }
        finally
        {
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current;
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.5f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            EnemyDefinition Def(string id) => defs.FirstOrDefault(d => d.EnemyId == id) ?? defs.First(d => d.EnemyId.EndsWith("_" + id) || d.EnemyId.Contains(id));
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            yield return Wait(1f);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }

            var groups = new (string tag, string[] ids, float[] xs, float y)[]
            {
                ("A", new[] { "BoneAsh", "Horridomorph", "Gorhorrid", "Occisodonte" }, new[] { .32f, .5f, .66f, .84f }, .3f),
                ("B", new[] { "Kupolobrach", "Ursacetus", "Limadon" }, new[] { .34f, .6f, .85f }, .32f),
            };
            foreach (var g in groups)
            {
                foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Dark })
                {
                    var row = new List<EnemyActor>();
                    for (int i = 0; i < g.ids.Length; i++)
                    {
                        Vector3 at = Ground(g.xs[i], g.y); Vector3 f = cam.transform.position - at; f.y = 0f;
                        if (!spawn.TrySpawn(new EnemySpawnRequest(Def(g.ids[i]), at, Quaternion.LookRotation(f), player.transform), out var e)) throw new Exception("Spawn " + g.ids[i]);
                        leased.Add(e); row.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                    }
                    yield return Wait(0.3f);
                    int[] steps = element == WeaponElement.Fire ? new[] { 1, 3, 5 } : new[] { 1, 5 };
                    int applied = 0;
                    foreach (int target in steps)
                    {
                        foreach (var e in row) Apply(e, element, target - applied, player.gameObject, energy.WeaponInstanceId);
                        applied = target;
                        yield return Wait(element == WeaponElement.Dark ? 2.2f : 1.4f);
                        string tag = g.tag + "_" + element + target;
                        foreach (var e in row) Measure(tag, e);
                        yield return Shot(tag);
                    }
                    foreach (var e in row) if (e != null && e.IsLeased) spawn.Release(e);
                    leased.RemoveAll(e => row.Contains(e));
                    yield return Wait(0.4f);
                }
            }
        }
        finally
        {
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }
}
