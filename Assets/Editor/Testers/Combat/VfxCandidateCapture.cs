using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Local-only: plays candidate VFX prefabs on the player (auras, looping) or on an enemy's chest (hits) in the real
// Hideout Play Mode and takes Game-view screenshots, so looks can be compared before anything is wired in.
[InitializeOnLoad]
public static class VfxCandidateCapture
{
    const string Key = "VfxCandidateCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> meta = new List<string>(), errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static VfxCandidateCapture() { EditorApplication.playModeStateChanged += State; }

    // candidates: lines "aura|<asset path>" or "hit|<asset path>".
    public static void Run(string output, string candidates)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        Directory.CreateDirectory(output);
        foreach (var line in candidates.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            if (AssetDatabase.LoadAssetAtPath<GameObject>(line.Substring(line.IndexOf('|') + 1)) == null) throw new ArgumentException("Missing prefab " + line);
        SessionState.SetString(Key + ".output", output); SessionState.SetString(Key + ".list", candidates);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            meta.Clear(); errors.Clear(); stack.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 600;
            work = Capture(SessionState.GetString(Key + ".list", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray());
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
    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception) errors.Add(m); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish("COMPLETE"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }
    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }
    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllLines(Path.Combine(Output, "meta.txt"), meta);
        File.WriteAllLines(Path.Combine(Output, "status.txt"), new[] { status }.Concat(errors.Take(20)));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    static IEnumerator Wait(float s) { float until = Time.time + s; while (Time.time < until) yield return null; }
    static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Output, name + ".png"));

    static IEnumerator Capture(string[] list)
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; EnemyActor dummy = null; GameObject live = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current;
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var def = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null && d.EnemyId == "SpiderBrood_Horridomorph");
            var cam = Camera.main;
            Vector3 camFlat = cam.transform.forward; camFlat.y = 0f; camFlat.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, camFlat);
            Shot("00_baseline"); yield return null; yield return null;
            int index = 0;
            foreach (var line in list)
            {
                index++;
                string kind = line.Substring(0, line.IndexOf('|')); string path = line.Substring(line.IndexOf('|') + 1);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string tag = index.ToString("D2") + "_" + kind + "_" + Path.GetFileNameWithoutExtension(path).Replace(" ", "").Replace(")", "").Replace("'", "");
                var vp = cam.WorldToViewportPoint(player.transform.position + Vector3.up);
                if (kind == "aura")
                {
                    live = UnityEngine.Object.Instantiate(prefab, player.transform);
                    live.transform.localPosition = Vector3.zero; live.transform.localRotation = Quaternion.identity;
                    yield return Wait(.35f); Shot(tag + "_a"); yield return null;
                    yield return Wait(1.1f); Shot(tag + "_b"); yield return null;
                }
                else
                {
                    if (dummy == null)
                    {
                        Vector3 at = player.transform.position + side * 2.2f;
                        if (!spawn.TrySpawn(new EnemySpawnRequest(def, at, Quaternion.LookRotation(-side), player.transform), out dummy)) throw new Exception("Spawn dummy");
                        dummy.AI.enabled = false; dummy.Movement.StopMovement(); dummy.Health.SetMaxHp(1000000, true);
                        yield return Wait(.5f);
                    }
                    Vector3 chest = dummy.transform.position + Vector3.up * .9f - side * .3f;
                    vp = cam.WorldToViewportPoint(chest);
                    live = UnityEngine.Object.Instantiate(prefab, chest, Quaternion.LookRotation(-cam.transform.forward));
                    yield return Wait(.06f); Shot(tag + "_a"); yield return null;
                    yield return Wait(.14f); Shot(tag + "_b"); yield return null;
                    yield return Wait(.25f); Shot(tag + "_c"); yield return null;
                }
                int particles = live.GetComponentsInChildren<ParticleSystem>(true).Length;
                var vfxGraphs = live.GetComponentsInChildren<Component>(true).Count(c => c != null && c.GetType().Name == "VisualEffect");
                meta.Add(tag + "|" + path + "|vp=" + vp.x.ToString("F3") + ":" + vp.y.ToString("F3") + "|ps=" + particles + "|vfx=" + vfxGraphs);
                UnityEngine.Object.Destroy(live); live = null;
                yield return Wait(.5f);
            }
            yield return null; yield return null;
        }
        finally
        {
            if (live != null) UnityEngine.Object.Destroy(live);
            if (spawn != null && dummy != null && dummy.IsLeased) spawn.Release(dummy);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }
}
