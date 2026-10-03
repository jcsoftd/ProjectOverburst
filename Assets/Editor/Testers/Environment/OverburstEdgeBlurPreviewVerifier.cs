using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class OverburstEdgeBlurPreviewVerifier
{
    private const string Key = "Overburst.EdgeBlurPreviewVerifier.";
    private static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    private static int Cycle { get => SessionState.GetInt(Key + "cycle", 0); set => SessionState.SetInt(Key + "cycle", value); }
    private static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    private static double Deadline => double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);

    static OverburstEdgeBlurPreviewVerifier()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    public static string VerifyAssets()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(OverburstEdgeBlurPreviewBuilder.RendererPath);
        var features = renderer.rendererFeatures.OfType<OverburstEdgeBlurRendererFeature>().ToArray();
        Require(features.Length == 1 && features[0].isActive && features[0].BlurShader != null, "Single active PC blur feature with shader");
        Require(features[0].BlurShader.isSupported && !ShaderUtil.ShaderHasError(features[0].BlurShader), "Blur shader supported and error-free");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstEdgeBlurPreviewBuilder.PrefabPath);
        Require(prefab != null && prefab.GetComponent<OverburstEdgeBlurPreview>() != null, "Toggle prefab loads");
        var preview = prefab.GetComponent<OverburstEdgeBlurPreview>();
        Require(preview.ToggleButton != null && preview.Caption != null && preview.Caption.font != null, "Button, caption and font references load");
        Require(prefab.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "Toggle renders after world blur");
        Require(((RectTransform)preview.ToggleButton.transform).anchoredPosition == new Vector2(16, -120), "Toggle stays below location heading");
        Require(prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "Prefab Missing Script 0");
        Require(AssetDatabase.AssetPathToGUID(OverburstEdgeBlurPreviewBuilder.PrefabPath).Length == 32, "Prefab GUID valid");
        VerifyShaderPixels(features[0].BlurShader);
        return "PASS: feature/prefab/shader references and actual GPU pixel checks";
    }

    private static void VerifyShaderPixels(Shader shader)
    {
        Texture2D pattern = null, readback = null;
        RenderTexture source = null, target = null;
        RTHandle handle = null;
        Material material = null;
        CommandBuffer commands = null;
        var previous = RenderTexture.active;
        const int width = 640, height = 360;
        try
        {
            pattern = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                pixels[y * width + x] = ((x + y) & 1) == 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
            pattern.SetPixels32(pixels); pattern.Apply();
            source = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            source.Create(); target.Create(); Graphics.Blit(pattern, source);
            handle = RTHandles.Alloc(source);
            material = CoreUtils.CreateEngineMaterial(shader);
            material.SetFloat("_EdgeBlurStrength", .72f);
            commands = new CommandBuffer { name = "Edge blur pixel verification" };
            commands.SetRenderTarget(target);
            Blitter.BlitTexture(commands, handle, new Vector4(1, 1, 0, 0), material, 0);
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = target;
            readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
            Require(Mathf.Abs(readback.GetPixel(width / 2, height / 2).r - 1) < .02f, "Shader central white pixel stays sharp");
            Require(readback.GetPixel(width / 2 + 1, height / 2).r < .02f, "Shader central black pixel stays sharp");
            float edgeValue = readback.GetPixel(2, height / 2).r;
            Require(edgeValue > .06f && edgeValue < .94f, "Shader outer edge actually blurs high-frequency detail");
            Require(readback.GetPixel(2, height / 2).a > .99f, "Shader preserves alpha");
        }
        finally
        {
            RenderTexture.active = previous;
            commands?.Release();
            handle?.Release();
            if (source != null) { source.Release(); Object.DestroyImmediate(source); }
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            CoreUtils.Destroy(material);
            if (pattern != null) Object.DestroyImmediate(pattern);
            if (readback != null) Object.DestroyImmediate(readback);
        }
    }

    public static string Begin(string output)
    {
        if (Phase != 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) ||
            !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))
            throw new InvalidOperationException("Another isolated account is active.");
        var boot = SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        Require(boot.IsValid() && boot.isLoaded, "Loaded product PersistentScene required");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "checks", "[]");
        SessionState.SetString(Key + "errors", "[]");
        SessionState.SetString(Key + "status", "RUNNING");
        SessionState.EraseString(Key + "failure");
        SessionState.SetString(Key + "activeScene", SceneManager.GetActiveScene().path);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 180).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Cycle = 1;
        Phase = 1;
        SceneManager.SetActiveScene(boot);
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        return "Started two isolated toggle/render lifecycle cycles";
    }

    private static void Tick()
    {
        if (Phase == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (EditorApplication.timeSinceStartup > Deadline) throw new TimeoutException("Edge blur verification timeout");
            if (Phase == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (Cycle == 1 && Status == "RUNNING")
                {
                    Cycle = 2;
                    Phase = 1;
                    IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount"));
                }
                else Phase = 4;
                return;
            }
            if (Phase == 4)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
                string active = IsolatedSavePlayGuard.ActiveDirectory;
                string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
                if (current.Length != 0 || active.Length != 0 || prepared.Length != 0)
                    throw new InvalidOperationException("Account return deferred: another account path is present");
                IsolatedSavePlayGuard.UseRealAccount();
                Require(!IsolatedSavePlayGuard.RequiresAccountChoice, "Normal Play account gate released");
                Require(string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "")), "Guard preparation expiry cleared");
                var scene = SceneManager.GetSceneByPath(SessionState.GetString(Key + "activeScene", ""));
                if (scene.IsValid() && scene.isLoaded) SceneManager.SetActiveScene(scene);
                Phase = 0;
                if (Status == "RUNNING") SessionState.SetString(Key + "status", "PASS");
                WriteResult();
                SessionState.EraseString(Key + "deadline");
                SessionState.EraseString(Key + "activeScene");
                return;
            }
            if (!EditorApplication.isPlaying) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase != 1) return;
            var flow = Object.FindFirstObjectByType<PersistentSceneFlow>(FindObjectsInactive.Include);
            if (flow != null && !flow.gameObject.activeInHierarchy) flow.gameObject.SetActive(true);
            if (!AccountBootstrap.Ready || !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null ||
                PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching) return;
            Check(Path.GetFullPath(AccountBootstrap.SaveDirectory) == Path.GetFullPath(Path.Combine(Output, "IsolatedAccount")), "Product boot uses isolated account");
            Phase = 2;
            new GameObject("EdgeBlurVerificationRunner").AddComponent<OverburstEdgeBlurVerificationRunner>();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || (Phase != 1 && Phase != 2)) return;
        SessionState.SetString(Key + "status", "CANCELLED");
        SessionState.SetString(Key + "failure", "Play stopped before verification completed");
        Phase = 4;
    }

    public static void Check(bool value, string label)
    {
        Require(value, label);
        var values = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
        values.Add("Cycle " + Cycle + ": " + label);
        SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(values));
    }

    private static void Require(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    public static string CapturePath(string label) => Path.Combine(Output, "Cycle" + Cycle + "_" + label + ".png");
    public static void CompleteCycle()
    {
        Check(JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]")).Count == 0, "Runtime errors 0");
        Phase = 3;
        EditorApplication.ExitPlaymode();
    }

    public static void Fail(Exception error)
    {
        // 다른 검증이 계정을 준비한 경우 반환을 중단해 그 계정과 상태를 보존한다.
        if (Phase == 4 && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) ||
            !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) ||
            !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))))
        {
            Phase = 0;
            SessionState.SetString(Key + "status", "RETURN_DEFERRED");
            SessionState.SetString(Key + "failure", error.ToString());
            WriteResult();
            return;
        }
        SessionState.SetString(Key + "status", "FAIL");
        SessionState.SetString(Key + "failure", error.ToString());
        WriteResult();
        Phase = EditorApplication.isPlayingOrWillChangePlaymode ? 3 : 4;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
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

public sealed class OverburstEdgeBlurVerificationRunner : MonoBehaviour
{
    private IEnumerator Start()
    {
        var routine = Run();
        while (true)
        {
            object yielded;
            try { if (!routine.MoveNext()) break; yielded = routine.Current; }
            catch (Exception error) { OverburstEdgeBlurPreviewVerifier.Fail(error); break; }
            yield return yielded;
        }
        Destroy(gameObject);
    }

    private static IEnumerator Run()
    {
        var preview = Object.FindFirstObjectByType<OverburstEdgeBlurPreview>();
        OverburstEdgeBlurPreviewVerifier.Check(preview != null && OverburstEdgeBlurPreview.IsEnabled, "Temporary toggle appears and defaults on");
        OverburstEdgeBlurPreviewVerifier.Check(Object.FindObjectsByType<OverburstEdgeBlurPreview>(FindObjectsSortMode.None).Length == 1, "Single persistent toggle instance");
        int count = OverburstEdgeBlurRendererFeature.RecordedPassCount;
        for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
        OverburstEdgeBlurPreviewVerifier.Check(OverburstEdgeBlurRendererFeature.RecordedPassCount > count, "Actual main camera records blur passes");
        Capture("On");
        preview.ToggleButton.onClick.Invoke();
        OverburstEdgeBlurPreviewVerifier.Check(!OverburstEdgeBlurPreview.IsEnabled && preview.Caption.text.EndsWith("꺼짐"), "UI button switches effect off and updates caption");
        count = OverburstEdgeBlurRendererFeature.RecordedPassCount;
        for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
        OverburstEdgeBlurPreviewVerifier.Check(OverburstEdgeBlurRendererFeature.RecordedPassCount == count, "Off performs zero additional blur passes");
        Capture("Off");
        preview.ToggleButton.onClick.Invoke();
        count = OverburstEdgeBlurRendererFeature.RecordedPassCount;
        for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
        OverburstEdgeBlurPreviewVerifier.Check(OverburstEdgeBlurPreview.IsEnabled && preview.Caption.text.EndsWith("켜짐") &&
            OverburstEdgeBlurRendererFeature.RecordedPassCount > count, "Second UI click restores active effect");
        OverburstEdgeBlurPreviewVerifier.CompleteCycle();
    }

    private static void Capture(string label)
    {
        Texture2D texture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            OverburstEdgeBlurPreviewVerifier.Check(texture != null && texture.width > 0, "Game view capture " + label);
            File.WriteAllBytes(OverburstEdgeBlurPreviewVerifier.CapturePath(label), texture.EncodeToPNG());
        }
        finally { if (texture != null) Object.Destroy(texture); }
    }
}
