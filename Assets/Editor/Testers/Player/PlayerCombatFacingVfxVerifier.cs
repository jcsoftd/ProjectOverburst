using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PlayerCombatFacingVfxVerifier
{
    const string Key = "Overburst.CombatFacingVerifier.";
    static readonly List<object> checks = new List<object>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator routine;
    static PlayerActorRuntime actor;
    static PlayerCombatFacingVfx effect;
    static string output;
    static double deadline;
    static bool background;

    static PlayerCombatFacingVfxVerifier()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "output", "")))
        {
            EditorApplication.update += AutoBegin;
            EditorApplication.playModeStateChanged += OnPlayState;
            if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + "return", false)) ScheduleReturn();
        }
    }

    public static void Start(string directory)
    {
        PlayerCombatFacingVfxBuilder.RequireIdle();
        if (IsolatedSavePlayGuard.RequiresAccountChoice) throw new InvalidOperationException("이전 격리 검증의 실제 계정 반환을 기다립니다.");
        output = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(output);
        string startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        SessionState.SetString(Key + "startScene", startScene);
        SessionState.SetString(Key + "output", output);
        SessionState.SetBool(Key + "return", false);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(output, "before.json"), JsonConvert.SerializeObject(new { startScene, scenes = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => { var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }).ToArray() }, Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayState; EditorApplication.playModeStateChanged += OnPlayState;
        EditorApplication.update -= AutoBegin; EditorApplication.update += AutoBegin;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "Account")); }
        catch { SessionState.SetBool(Key + "return", true); ScheduleReturn(); throw; }
    }

    static void AutoBegin()
    {
        output = SessionState.GetString(Key + "output", "");
        if (string.IsNullOrEmpty(output)) { EditorApplication.update -= AutoBegin; return; }
        if (SessionState.GetBool(Key + "return", false)) { EditorApplication.update -= AutoBegin; ScheduleReturn(); return; }
        if (!EditorApplication.isPlaying)
        {
            double limit = double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);
            if (EditorApplication.timeSinceStartup > limit) { errors.Add("Play boot timeout"); SessionState.SetBool(Key + "return", true); ScheduleReturn(); }
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= AutoBegin;
        checks.Clear(); errors.Clear(); deadline = EditorApplication.timeSinceStartup + 65;
        background = Application.runInBackground; Application.runInBackground = true;
        Application.logMessageReceived += OnLog;
        routine = Scenario(); EditorApplication.update += Tick;
        Status("RUNNING", "Waiting for the actual player and ground");
    }

    static void OnLog(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) errors.Add(message); }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Combat facing Play verification timed out");
            if (!EditorApplication.isPlaying || !OwnAccount()) throw new InvalidOperationException("Owned Play was interrupted");
            if (!routine.MoveNext()) Finish();
        }
        catch (Exception e) { errors.Add(e.ToString()); Finish(); }
    }
    static void Check(bool passed, string name)
    { checks.Add(new { name, passed }); if (!passed) errors.Add(name); }
    static IEnumerator Wait(double seconds)
    { double until = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < until) yield return null; }
    static void Step() { typeof(PlayerCombatFacingVfx).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(effect, null); }

    static IEnumerator Scenario()
    {
        while (actor == null || !actor.Movement.IsGrounded)
        {
            actor = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null;
            yield return null;
        }
        effect = actor.GetComponent<PlayerCombatFacingVfx>();
        if (effect == null) throw new InvalidOperationException("Actual spawned player does not contain the authored component");
        var toggle = Overburst.DebugTools.DebugRegistry.Find("player.presentation.combatFacingVfx") as Overburst.DebugTools.DebugToggle;
        Check(toggle != null && !toggle.Value && !toggle.InPresets && !PlayerCombatFacingVfx.DebugVisible, "새 Play 기본 꺼짐과 임시 토글 등록");
        if (toggle == null) throw new InvalidOperationException("Facing VFX debug toggle missing");
        var mode = PlayerCombatModeController.GetOrCreate();
        mode.EnterCombatMode(PlayerCombatModeReason.System);
        var defaultWait = Wait(.35); while (defaultWait.MoveNext()) yield return null;
        Check(!effect.IsVisible, "전투라도 기본 꺼짐이면 표시하지 않음");
        toggle.ApplyValue("1");
        mode.ExitCombatMode(PlayerCombatModeReason.System);
        var wait = Wait(.3); while (wait.MoveNext()) yield return null;
        Check(!effect.IsVisible, "탐험 모드에서는 숨김");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            mode.EnterCombatMode(PlayerCombatModeReason.System);
            wait = Wait(.35); while (wait.MoveNext()) yield return null;
            Check(effect.IsVisible && effect.Visibility > .99f, "전투 진입 페이드 " + cycle);
            toggle.ApplyValue("0"); Check(!effect.IsVisible, "토글 끄면 즉시 숨김 " + cycle);
            toggle.ApplyValue("1"); wait = Wait(.35); while (wait.MoveNext()) yield return null;
            Check(effect.IsVisible, "토글 다시 켜면 전투 표시 " + cycle);
            Check(Object.FindObjectsByType<PlayerCombatFacingVfx>(FindObjectsSortMode.None).All(v => v == effect || !v.IsVisible), "AI 파티원에는 표시하지 않음 " + cycle);
            mode.ExitCombatMode(PlayerCombatModeReason.System);
            wait = Wait(.3); while (wait.MoveNext()) yield return null;
            Check(!effect.IsVisible, "전투 해제 페이드 " + cycle);
        }
        mode.EnterCombatMode(PlayerCombatModeReason.System);
        wait = Wait(.5); while (wait.MoveNext()) yield return null;
        Status("RUNNING", "Checking ground, facing, fixed silver, pause and lifecycle");
        Vector3 originalPosition = actor.transform.position;
        Quaternion originalRotation = actor.transform.rotation;
        foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
        {
            actor.transform.rotation = Quaternion.Euler(0, yaw, 0); Physics.SyncTransforms(); Step();
            Vector3 expected = Vector3.ProjectOnPlane(actor.transform.forward, effect.VisualRoot.up).normalized;
            Check(Vector3.Angle(expected, effect.VisualRoot.forward) < .1f, "실제 바라보는 방향 " + yaw);
            Check((new Vector2(effect.VisualRoot.position.x - actor.transform.position.x, effect.VisualRoot.position.z - actor.transform.position.z)).sqrMagnitude < .001f, "캐릭터 발밑 위치 " + yaw);
        }
        actor.transform.SetPositionAndRotation(originalPosition, originalRotation);
        Step();
        var renderers = effect.VisualRoot.GetComponentsInChildren<Renderer>(true);
        var tintBefore = renderers.SelectMany(r => r.sharedMaterials).Distinct().Select(m => m.GetColor("_Tint")).ToArray();
        var dash = actor.GetComponent<PlayerDashVfx>();
        int style = dash != null ? dash.ColorStyle : 0;
        if (dash != null)
        {
            foreach (int palette in new[] { 1, 4, 6 })
            {
                dash.SetColorStyle(palette); Step();
                Check(tintBefore.SequenceEqual(renderers.SelectMany(r => r.sharedMaterials).Distinct().Select(m => m.GetColor("_Tint"))), "대시 색상과 독립된 은백색 " + palette);
            }
            dash.SetColorStyle(style);
        }
        float phase = effect.FlowTime;
        bool wasPaused = OverburstTimeEffectArbiter.IsPaused;
        try
        {
            OverburstTimeEffectArbiter.SetPaused(true);
            wait = Wait(.2); while (wait.MoveNext()) yield return null;
            Check(Mathf.Abs(effect.FlowTime - phase) < .0001f, "메뉴 정지 중 빛결 시계 정지");
        }
        finally { OverburstTimeEffectArbiter.SetPaused(wasPaused); }
        effect.enabled = false; Check(!effect.IsVisible && !effect.VisualRoot.gameObject.activeSelf, "비활성화 즉시 표시 반환");
        effect.enabled = true; wait = Wait(.35); while (wait.MoveNext()) yield return null;
        Check(effect.IsVisible, "재활성화 후 전투 표시 복귀");
        Capture("Combat.png");
        mode.ExitCombatMode(PlayerCombatModeReason.System); wait = Wait(.3); while (wait.MoveNext()) yield return null;
        Capture("Exploration.png");
        Check(!effect.IsVisible, "최종 탐험 반환");
        toggle.ApplyValue("0");
    }

    static void Capture(string file)
    {
        var camera = Camera.main; if (camera == null) throw new InvalidOperationException("Game camera missing");
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        RenderTexture rt = null; Texture2D texture = null;
        try
        {
            rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf); rt.Create();
            camera.targetTexture = rt;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = rt; texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(output, file), texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            if (texture != null) Object.DestroyImmediate(texture);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
        }
    }
    static bool OwnAccount()
    { return string.Equals(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), Path.Combine(output, "Account"), StringComparison.OrdinalIgnoreCase); }
    static void Finish()
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        (routine as IDisposable)?.Dispose(); routine = null;
        if (OwnAccount()) { PlayerCombatFacingVfx.SetDebugVisible(false); if (effect != null) effect.enabled = false; OverburstTimeEffectArbiter.SetPaused(false); Application.runInBackground = background; }
        File.WriteAllText(Path.Combine(output, "play_validation.json"), JsonConvert.SerializeObject(new { status = errors.Count == 0 ? "PASS" : "FAIL", checks, errors, ownsAccount = OwnAccount(), productPlayerPrefab = true }, Formatting.Indented));
        Status(errors.Count == 0 ? "PASS" : "FAIL", "Runtime checks finished; returning the Editor and account");
        actor = null; effect = null;
        SessionState.SetBool(Key + "return", true);
        if (EditorApplication.isPlaying && OwnAccount()) EditorApplication.ExitPlaymode();
        else ScheduleReturn();
    }
    static void OnPlayState(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Key + "return", true); ScheduleReturn(); } }
    static void ScheduleReturn()
    { EditorApplication.update -= Restore; EditorApplication.update += Restore; }
    static void Restore()
    {
        double limit = double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (limit > 0 && EditorApplication.timeSinceStartup > limit + 180)
        {
            output = SessionState.GetString(Key + "output", "");
            if (!string.IsNullOrEmpty(output)) Status("DEFERRED", "Return deadline reached; other Editor state was preserved. RetryReturn when idle.");
            Detach(); return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        output = SessionState.GetString(Key + "output", ""); if (string.IsNullOrEmpty(output)) { Detach(); return; }
        string account = Path.Combine(output, "Account");
        string active = IsolatedSavePlayGuard.ActiveDirectory;
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if ((!string.IsNullOrEmpty(active) && !string.Equals(active, account, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(prepared) && !string.Equals(prepared, account, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(current) && !string.Equals(current, account, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "startScene", ""));
            IsolatedSavePlayGuard.UseRealAccount();
            EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect();
            bool ready = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""));
            File.WriteAllText(Path.Combine(output, "editor_return.json"), JsonConvert.SerializeObject(new { status = ready ? "PASS" : "FAIL", requiresAccountChoice = IsolatedSavePlayGuard.RequiresAccountChoice, saveOverride = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), active, playStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), resourcesCleaned = true, scenes = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => { var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }).ToArray() }, Formatting.Indented));
            SessionState.EraseString(Key + "output"); SessionState.EraseString(Key + "deadline"); SessionState.EraseString(Key + "startScene"); SessionState.EraseBool(Key + "return"); Detach();
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "return_error.txt"), e.ToString()); Detach(); }
    }
    static void Detach()
    { EditorApplication.update -= Restore; EditorApplication.update -= AutoBegin; EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= OnPlayState; Application.logMessageReceived -= OnLog; }
    public static void RetryReturn()
    {
        if (string.IsNullOrEmpty(SessionState.GetString(Key + "output", ""))) return;
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString(System.Globalization.CultureInfo.InvariantCulture));
        ScheduleReturn();
    }
    static void Status(string status, string note)
    { File.WriteAllText(Path.Combine(output, "status.json"), JsonConvert.SerializeObject(new { status, note }, Formatting.Indented)); }
}
