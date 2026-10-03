using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class OverburstMotionBlurPreviewVerifier
{
    private const string Key = "Overburst.MotionBlurPreviewVerifier.";
    private static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    private static int Cycle { get => SessionState.GetInt(Key + "cycle", 0); set => SessionState.SetInt(Key + "cycle", value); }
    private static string Output => SessionState.GetString(Key + "output", "");
    private static string Account => Path.Combine(Output, "IsolatedAccount");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");

    static OverburstMotionBlurPreviewVerifier()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
    }

    public static string VerifyAssets()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstMotionBlurPreviewBuilder.PrefabPath);
        Require(prefab != null, "Native toggle prefab loads");
        var preview = prefab.GetComponent<OverburstMotionBlurPreview>();
        Require(preview != null && preview.ToggleButton != null && preview.Caption?.font != null, "Button/caption/font references load");
        Require(prefab.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "UI renders after world post processing");
        Require(((RectTransform)preview.ToggleButton.transform).anchoredPosition == new Vector2(16, -168), "Button avoids existing location/blur controls");
        Require(prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "Missing Script 0");
        Require(AssetDatabase.AssetPathToGUID(OverburstMotionBlurPreviewBuilder.PrefabPath).Length == 32, "Native GUID valid");
        return "PASS: native toggle prefab, references, layout, Missing Script and GUID";
    }

    public static string Start(string output)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && Phase == 0,
            "Idle Editor required");
        Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) &&
            string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) &&
            string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")), "Another isolated account must not be active");
        VerifyAssets();
        var boot = SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        Require(boot.IsValid() && boot.isLoaded, "Loaded PersistentScene required");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "checks", "[]");
        SessionState.SetString(Key + "errors", "[]");
        SessionState.SetString(Key + "failure", "");
        SessionState.SetString(Key + "status", "RUNNING");
        SessionState.SetString(Key + "activeScene", SceneManager.GetActiveScene().path);
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 180).ToString("R", CultureInfo.InvariantCulture));
        Phase = 1;
        Cycle = 1;
        SceneManager.SetActiveScene(boot);
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch (Exception error) { Fail(error); }
        return "Started two isolated product toggle/render lifecycle cycles";
    }

    private static void Tick()
    {
        if (Phase == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (Phase == 4)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
                string active = IsolatedSavePlayGuard.ActiveDirectory;
                string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
                Require(new[] { current, active, prepared }.All(p => string.IsNullOrEmpty(p) ||
                    string.Equals(Path.GetFullPath(p), Path.GetFullPath(Account), StringComparison.OrdinalIgnoreCase)),
                    "Account return deferred: another account is present");
                IsolatedSavePlayGuard.UseRealAccount();
                Check(!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) &&
                    string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) &&
                    string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")) &&
                    string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "")), "Real-account normal Play gate returned");
                Check(AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene) ==
                    SessionState.GetString(Key + "startScene", ""), "Play start scene unchanged");
                var original = SceneManager.GetSceneByPath(SessionState.GetString(Key + "activeScene", ""));
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (Status == "RUNNING") SessionState.SetString(Key + "status", "PASS");
                Phase = 0;
                SessionState.EraseString(Key + "deadline");
                SessionState.EraseString(Key + "activeScene");
                SessionState.EraseString(Key + "startScene");
                WriteResult();
                return;
            }
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), CultureInfo.InvariantCulture))
                throw new TimeoutException("Motion blur verification timeout");
            if (Phase == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (Cycle == 1 && Status == "RUNNING")
                {
                    Cycle = 2;
                    Phase = 1;
                    IsolatedSavePlayGuard.EnterIsolatedPlay(Account);
                }
                else Phase = 4;
                return;
            }
            if (!EditorApplication.isPlaying) return;
            Require(string.Equals(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), Account, StringComparison.OrdinalIgnoreCase),
                "Verification owns current isolated Play");
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase != 1) return;
            var flow = Object.FindFirstObjectByType<PersistentSceneFlow>(FindObjectsInactive.Include);
            if (flow != null && !flow.gameObject.activeInHierarchy) flow.gameObject.SetActive(true);
            if (!AccountBootstrap.Ready || !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null ||
                PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching) return;
            Check(Path.GetFullPath(AccountBootstrap.SaveDirectory) == Path.GetFullPath(Account), "Product boot uses isolated account");
            Phase = 2;
            new GameObject("MotionBlurVerificationRunner").AddComponent<OverburstMotionBlurVerificationRunner>();
        }
        catch (Exception error)
        {
            if (Phase == 4) { SessionState.SetString(Key + "status", "RETURN_DEFERRED"); SessionState.SetString(Key + "failure", error.ToString()); Phase = 0; WriteResult(); }
            else Fail(error);
        }
    }

    public static void Check(bool value, string label)
    {
        Require(value, label);
        var values = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
        values.Add("Cycle " + Cycle + ": " + label);
        SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(values));
    }

    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
    public static string CapturePath(string label) => Path.Combine(Output, "Cycle" + Cycle + "_" + label + ".png");
    public static void CompleteCycle()
    {
        Check(JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]")).Count == 0, "Runtime errors 0");
        Phase = 3;
        EditorApplication.ExitPlaymode();
    }

    public static void Fail(Exception error)
    {
        SessionState.SetString(Key + "status", "FAIL");
        SessionState.SetString(Key + "failure", error.ToString());
        bool ownsPlay = string.Equals(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), Account, StringComparison.OrdinalIgnoreCase);
        Phase = EditorApplication.isPlayingOrWillChangePlaymode ? 3 : 4;
        WriteResult();
        if (EditorApplication.isPlaying && ownsPlay) EditorApplication.ExitPlaymode();
    }

    private static void Log(string message, string trace, LogType type)
    {
        if (Phase == 0 || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        var errors = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
        errors.Add(message);
        SessionState.SetString(Key + "errors", JsonConvert.SerializeObject(errors));
    }

    private static void WriteResult()
    {
        if (string.IsNullOrEmpty(Output)) return;
        File.WriteAllText(Path.Combine(Output, "play-result.json"), JsonConvert.SerializeObject(new {
            status = Status, phase = Phase, cycles = Cycle,
            checks = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]")),
            errors = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]")),
            failure = SessionState.GetString(Key + "failure", ""),
            accountChoice = IsolatedSavePlayGuard.RequiresAccountChoice,
            isolatedDirectory = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            guardActive = IsolatedSavePlayGuard.ActiveDirectory
        }, Formatting.Indented));
    }
}

public sealed class OverburstMotionBlurVerificationRunner : MonoBehaviour
{
    private IEnumerator Start()
    {
        var routine = Run();
        try
        {
            while (true)
            {
                object yielded;
                try { if (!routine.MoveNext()) break; yielded = routine.Current; }
                catch (Exception error) { OverburstMotionBlurPreviewVerifier.Fail(error); break; }
                yield return yielded;
            }
        }
        finally { (routine as IDisposable)?.Dispose(); Destroy(gameObject); }
    }

    private static IEnumerator Run()
    {
        var preview = Object.FindFirstObjectByType<OverburstMotionBlurPreview>();
        Check(preview != null && preview.IsEnabled, "Temporary toggle appears and defaults on");
        Check(Object.FindObjectsByType<OverburstMotionBlurPreview>(FindObjectsSortMode.None).Length == 1, "Single persistent instance");
        Check(preview.TargetCamera != null && preview.TargetCamera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing,
            "Product camera post processing enabled");
        Check(preview.PreviewVolume.sharedProfile != null && (preview.TargetCamera.GetComponent<UniversalAdditionalCameraData>().volumeLayerMask.value &
            (1 << preview.PreviewVolume.gameObject.layer)) != 0, "Dedicated runtime profile is visible to product camera");
        Check(preview.Settings.mode.value == MotionBlurMode.CameraAndObjects && preview.Settings.quality.value == MotionBlurQuality.Low,
            "Native camera/object mode and Low quality");
        var skins = PlayerContext.Instance.CurrentActor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check(skins.Any(r => r.skinnedMotionVectors && r.sharedMaterials.Any(m => m != null && m.FindPass("MotionVectors") >= 0 &&
            m.GetShaderPassEnabled("MotionVectors"))), "Player skeletal shader supports motion vectors");
        for (int i = 0; i < 4; i++) yield return new WaitForEndOfFrame();
        CheckStack(true);
        Check(Shader.GetGlobalTexture("_MotionVectorTexture") != null, "Actual rendering produces native motion-vector texture");
        Capture("On");
        var mouse = Mouse.current;
        Check(mouse != null && EventSystem.current != null, "Mouse and UI EventSystem available");
        Vector2 previousPosition = mouse.position.ReadValue();
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null,
            ((RectTransform)preview.ToggleButton.transform).TransformPoint(((RectTransform)preview.ToggleButton.transform).rect.center));
        try
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
            yield return null;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(preview.ToggleButton.transform), "Actual UI raycast reaches temporary button");
            Check(GameplayInputBlocker.IsGameplayInputBlocked, "Button hover blocks gameplay input before click");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 1 });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
            yield return new WaitForEndOfFrame();
            Check(!preview.IsEnabled && preview.Caption.text.EndsWith("꺼짐"), "Queued mouse click switches off and updates visible label");
            CheckStack(false);
            Capture("Off");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 1 });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
            yield return new WaitForEndOfFrame();
            Check(preview.IsEnabled && preview.Caption.text.EndsWith("켜짐"), "Second queued mouse click restores effect");
            CheckStack(true);
            preview.enabled = false;
            Check(!preview.PreviewVolume.enabled, "Disable removes owned volume influence");
            preview.enabled = true;
            yield return new WaitForEndOfFrame();
            CheckStack(true);
        }
        finally { InputSystem.QueueStateEvent(mouse, new MouseState { position = previousPosition }); }
        OverburstMotionBlurPreviewVerifier.CompleteCycle();
    }

    private static void CheckStack(bool enabled)
    {
        var value = VolumeManager.instance.stack.GetComponent<MotionBlur>();
        Check(value != null && value.mode.value == MotionBlurMode.CameraAndObjects && value.quality.value == MotionBlurQuality.Low &&
            Mathf.Approximately(value.intensity.value, enabled ? OverburstMotionBlurPreview.PreviewIntensity : 0f) && value.IsActive() == enabled,
            enabled ? "Rendered camera volume stack applies weak native blur" : "Rendered camera volume stack disables native blur at zero intensity");
    }

    private static void Check(bool value, string label) => OverburstMotionBlurPreviewVerifier.Check(value, label);
    private static void Capture(string label)
    {
        Texture2D texture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            Check(texture != null && texture.width > 0, "Actual Game view capture " + label);
            File.WriteAllBytes(OverburstMotionBlurPreviewVerifier.CapturePath(label), texture.EncodeToPNG());
        }
        finally { if (texture != null) Object.Destroy(texture); }
    }
}
