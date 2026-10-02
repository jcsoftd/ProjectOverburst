using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class LoadingScreenArtworkVerifier
{
    const string Key = "Overburst.LoadingArtworkVerifier.";
    static readonly List<object> checks = new List<object>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator work;
    static int frame, failures;
    static double deadline;
    static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");

    static LoadingScreenArtworkVerifier() { EditorApplication.playModeStateChanged += State; }

    public static string Run(string output)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "scenes", SceneSnapshot());
        SessionState.SetString(Key + "startScene", EditorSceneManager.playModeStartScene != null ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "");
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        SessionState.SetBool(Key + "run", true);
        SessionState.SetString(Key + "status", "RUNNING");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount")); }
        catch { Restore(); SessionState.SetBool(Key + "run", false); throw; }
        return "RUNNING: initial random art and two real dungeon returns.";
    }

    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Editor must be idle.");
    }

    static string SceneSnapshot() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount)
        .Select(i => SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty, s.isLoaded,
            active = s == SceneManager.GetActiveScene(), roots = s.GetRootGameObjects().Length }));

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "run", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); failures = 0; frame = -1;
            SessionState.SetBool(Key + "background", Application.runInBackground);
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 180;
            work = Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Restore();
            File.WriteAllText(Path.Combine(Output, "editor_restored.json"), JsonConvert.SerializeObject(new {
                scenesPreserved = SceneSnapshot() == SessionState.GetString(Key + "scenes", ""),
                isolatedEnvironmentCleared = string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            }, Formatting.Indented));
            SessionState.SetBool(Key + "run", false);
        }
    }

    static void Restore()
    {
        string path = SessionState.GetString(Key + "startScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
    }

    static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Loading artwork Play verification timed out.");
            if (!work.MoveNext()) Finish();
        }
        catch (Exception error) { Check(false, error.ToString()); Finish(); }
    }

    static void Finish()
    {
        string status = failures == 0 && errors.Count == 0 ? "PASS" : "FAIL";
        SessionState.SetString(Key + "status", status);
        File.WriteAllText(Path.Combine(Output, "Result.json"), JsonConvert.SerializeObject(new { status, failures, checks, errors }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    static void Check(bool passed, string name) { checks.Add(new { name, passed }); if (!passed) failures++; }
    static Image Background(LoadingScreenUI ui) => ui.transform.Find("Background").GetComponent<Image>();
    static Texture Artwork(LoadingScreenUI ui) => Background(ui).material.HasProperty("_ArtworkTex") ? Background(ui).material.GetTexture("_ArtworkTex") : null;
    static bool Visible(LoadingScreenUI ui) => ui.GetComponent<CanvasGroup>().alpha > .99f;

    static IEnumerator Verify()
    {
        LoadingScreenUI ui = null;
        while (ui == null || Artwork(ui) == null)
        {
            ui = Object.FindFirstObjectByType<LoadingScreenUI>(FindObjectsInactive.Include);
            yield return null;
        }
        var catalog = Resources.Load<LoadingScreenArtworkCatalog>(LoadingScreenArtworkCatalog.ResourcePath);
        var allowed = Enumerable.Range(0, catalog.RandomArtworkCount).Select(i => catalog.Select(LoadingScreenArtworkContext.Random, i)).ToHashSet();
        Check(catalog.RandomArtworkCount == 9 && allowed.Count == 9, "Nine distinct attached artworks load");
        Check(allowed.Contains(Artwork(ui)), "Initial boot chooses one of the nine random artworks");
        while (!AccountBootstrap.Ready || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        Check(!Visible(ui) && !ui.GetComponent<CanvasGroup>().blocksRaycasts, "Initial loading ends after hideout is ready");
        Check(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            && Path.GetFullPath(AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Product boot uses this verifier's isolated account");
        var randomState = UnityEngine.Random.state;
        var sampled = new HashSet<Texture>();
        for (int i = 0; i < 40; i++)
        {
            ui.Show("LOADING", "검증"); sampled.Add(Artwork(ui));
            Check(allowed.Contains(Artwork(ui)), "Random loading image is in the catalog: " + i);
        }
        Check(sampled.Count > 1, "Repeated loading can select different images");
        Check(UnityEngine.Random.state.Equals(randomState), "Loading artwork does not consume gameplay random state");
        ui.SetProgress(.57f);
        Check(ui.transform.Find("PF_OverburstLoadingBar_Rpg11/Text Pct").GetComponent<Text>().text == "57%", "Existing progress bar still reports 57 percent");
        ui.Hide();
        var flow = PersistentSceneFlow.Instance;
        var account = AccountGameplaySession.Current;
        var registry = (AccountContentRegistry)typeof(AccountGameplaySession).GetProperty("ContentRegistry",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(account);
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        Texture camp = catalog.Select(LoadingScreenArtworkContext.ReturnToHideout, 0);
        Check(camp.name == "Loading_Hideout_Camp", "Fixed return art is the user-selected camp image");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var map = new MapInstanceState { mapContentId = registry.IdFor(definition), level = 1,
                grade = ItemGrade.Common, monsterThemeId = "CavernMutants" };
            Check(flow.EnterDebugRun(DiamondDungeonWorld.SceneName, map), "Real dungeon entry accepted: " + cycle);
            Check(Visible(ui) && allowed.Contains(Artwork(ui)), "Dungeon entry shows catalog art: " + cycle);
            while (flow.IsSwitching) yield return null;
            Check(flow.RunEntryError == null && WorldSessionState.Phase == WorldPhase.Run, "Dungeon is ready: " + cycle);
            flow.GetComponent<RunLifetimeDriver>().RequestAbandon();
            flow.GetComponent<RunLifetimeDriver>().Poll(DateTime.UtcNow.Ticks);
            while (!flow.IsSwitching && !WorldSessionState.IsHideout) yield return null;
            Check(Visible(ui) && Artwork(ui) == camp, "Actual dungeon-to-hideout transition shows fixed camp: " + cycle);
            while (flow.IsSwitching || !WorldSessionState.IsHideout)
            {
                Check(Artwork(ui) == camp, "Camp remains fixed throughout return: " + cycle);
                yield return null;
            }
            Check(!Visible(ui) && !ui.GetComponent<CanvasGroup>().blocksRaycasts, "Return hides loading and releases UI interception: " + cycle);
        }
    }

    public static string CapturePreview(string output, int width, int height)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        var source = Object.FindFirstObjectByType<LoadingScreenUI>(FindObjectsInactive.Include);
        if (source == null) throw new InvalidOperationException("Open product loading UI is required.");
        var inventories = Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var locks = inventories.Select(inventory => inventory.InputToggleLocked).ToArray();
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject cameraObject = null, copy = null;
        RenderTexture render = null;
        Texture2D pixels = null;
        Material temporaryMaterial = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            cameraObject = new GameObject("Loading Preview Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview;
            cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = false;
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = height / 2f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -10);
            copy = Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(copy, preview);
            foreach (Transform child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            var canvas = copy.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            var root = (RectTransform)copy.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(width, height); root.position = Vector3.zero;
            root.rotation = Quaternion.identity; root.localScale = Vector3.one;
            render = new RenderTexture(width, height, 24); render.Create(); camera.targetTexture = render;
            Canvas.ForceUpdateCanvases();
            var ui = copy.GetComponent<LoadingScreenUI>();
            ui.Show("RETURNING", "하이드아웃으로 복귀 중...", LoadingScreenArtworkContext.ReturnToHideout);
            ui.SetProgress(.57f);
            temporaryMaterial = Background(ui).material;
            Canvas.ForceUpdateCanvases();
            var rect = Background(ui).rectTransform.rect;
            temporaryMaterial.SetFloat("_ViewportAspect", rect.width / rect.height);
            camera.Render(); RenderTexture.active = render;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            string path = Path.Combine(output, "hideout_return_" + width + "x" + height + ".png");
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            return path;
        }
        finally
        {
            RenderTexture.active = previous;
            if (copy != null) { copy.GetComponent<LoadingScreenUI>().ForceHide(); Object.DestroyImmediate(copy); }
            if (temporaryMaterial != null) Object.DestroyImmediate(temporaryMaterial);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (render != null) { render.Release(); Object.DestroyImmediate(render); }
            EditorSceneManager.ClosePreviewScene(preview);
            for (int i = 0; i < inventories.Length; i++) if (inventories[i] != null) inventories[i].InputToggleLocked = locks[i];
        }
    }
}
