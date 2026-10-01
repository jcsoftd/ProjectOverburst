using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only 2026-09-30: 적용 후 확인 — 소형·중형·정예에 실제 화상 5중첩(게임 오라 경로)과 실제 불 타격(MeleeElementHitVfxService)
// Game 뷰 캡처. 격리 계정, Play는 스스로 끝낸다.
[InitializeOnLoad]
public static class FireAuraHitApplyCapture
{
    const string Key = "FireAuraHitApplyCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static FireAuraHitApplyCapture() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
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
            shots.Clear(); notes.Clear(); errors.Clear(); stack.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 150;
            work = Capture(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
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
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, shots, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    static IEnumerator Wait(float s) { float until = Time.time + s; while (Time.time < until) yield return null; }
    static void Shot(string name) { string p = Path.Combine(Output, name + ".png"); ScreenCapture.CaptureScreenshot(p); shots.Add(p); }

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var list = new[] { defs.First(d => d.EnemyId.Contains("Ceratoferox")), defs.First(d => d.EnemyId.Contains("Scolokarck")),
                defs.FirstOrDefault(d => d.Grade != null && d.Grade.GradeType == EnemyGradeType.Elite) ?? defs.First(d => d.EnemyId.Contains("Ursacetus")) };
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            yield return Wait(1.0f);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Quaternion FaceCamera(Vector3 at) { Vector3 f = cam.transform.position - at; f.y = 0f; return Quaternion.LookRotation(f.sqrMagnitude > .01f ? f : Vector3.back); }
            EnemyActor Spawn(EnemyDefinition d, Vector3 at)
            {
                if (!spawn.TrySpawn(new EnemySpawnRequest(d, at, FaceCamera(at), player.transform), out var e)) throw new Exception("Spawn " + d.EnemyId);
                leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; return e;
            }
            float[] ys = { .8f, .62f, .28f };
            var aura = new EnemyActor[3]; var hit = new EnemyActor[3];
            for (int i = 0; i < 3; i++) { aura[i] = Spawn(list[i], Ground(.3f, ys[i])); hit[i] = Spawn(list[i], Ground(.7f, ys[i])); }
            notes.Add("rows: " + string.Join(", ", list.Select(d => d.EnemyId)));
            yield return Wait(0.6f);
            foreach (var e in aura)
            {
                var s = e.GetComponent<ElementalStatusController>();
                for (int k = 0; k < 5; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Fire, 10, player.gameObject, energy.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
            }
            yield return Wait(1.5f);
            Shot("aura_1.5s");
            foreach (var e in hit) MeleeElementHitVfxService.TryPlay(WeaponElement.Fire, CombatTargetVfxPlacement.ResolveVolume(e.GetComponent<CombatTarget>()).Center);
            yield return Wait(.12f); Shot("hit_0.12s");
            yield return Wait(.23f); Shot("hit_0.35s");
            yield return Wait(.45f); Shot("hit_0.8s");
            yield return Wait(1.5f); Shot("aura_4s");
            yield return null; yield return null;
        }
        finally
        {
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }
}
