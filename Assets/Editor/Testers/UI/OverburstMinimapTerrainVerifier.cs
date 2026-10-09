using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
public static class OverburstMinimapTerrainVerifier
{
    const string Key = "Overburst.MinimapTerrainVerifier.";
    const string Prefab = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstWorldMinimap_Rpg11.prefab";
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static string Output => SessionState.GetString(Key + "output", "");
    static IEnumerator work;
    static int frame = -1;
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly List<object> reports = new List<object>();
    static string pendingShot;
    static double shotDeadline;

    static OverburstMinimapTerrainVerifier()
    {
        if (Phase == 2) { SessionState.SetString(Key + "failure", "Runtime probe interrupted by domain reload."); Phase = 3; }
        if (Phase != 0) EditorApplication.update += Tick;
    }

    public static string AssetsCheck()
    {
        EditorSceneSafety.RequireNoUnsavedScenes("Minimap terrain assets check");
        GameObject root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var controller = root.GetComponent<WorldMinimapController>();
            var view = root.GetComponent<MinimapView>();
            if (controller == null || view == null || !view.IsReady || view.TerrainImage == null)
                throw new InvalidOperationException("Minimap authored references are missing.");
            var image = view.TerrainImage;
            if (image.raycastTarget || !image.maskable || image.transform.GetSiblingIndex() != 0
                || root.GetComponentsInChildren<RawImage>(true).Length != 1
                || image.GetComponentInParent<Mask>() == null || root.GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Terrain image mask/order/raycast contract failed.");
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                    throw new InvalidOperationException("Missing script: " + child.name);
            var entries = new SerializedObject(controller).FindProperty("terrainMaps");
            if (entries.arraySize != 2) throw new InvalidOperationException("Two hub maps required.");
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var texture = entry.FindPropertyRelative("texture").objectReferenceValue as Texture2D;
                var importer = texture == null ? null : AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                Rect bounds = entry.FindPropertyRelative("worldBounds").rectValue;
                if (importer == null || texture.isReadable || importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp
                    || texture.width > 512 || texture.height > 512 || bounds.width <= 0 || bounds.height <= 0)
                    throw new InvalidOperationException("Map import/reference contract failed.");
            }
            // Reconstruct world coordinates through the displayed UV rectangle and local rotation.
            var testBounds = new Rect(-40, -70, 180, 300);
            var origin = new Vector3(11, 0, 23);
            var point = new Vector3(27, 0, 39);
            view.SetTerrain((Texture2D)entries.GetArrayElementAtIndex(0).FindPropertyRelative("texture").objectReferenceValue, testBounds);
            foreach (float zoom in new[] { 20f, 55f, 80f })
            foreach (float yaw in new[] { 0f, 45f, 90f, 346f })
            {
                view.UpdateTerrain(origin, yaw, zoom);
                Rect uv = image.uvRect;
                Vector2 worldUv = new Vector2((point.x - testBounds.xMin) / testBounds.width, (point.z - testBounds.yMin) / testBounds.height);
                Vector2 local = Vector2.Scale((worldUv - uv.center), new Vector2(image.rectTransform.rect.width / uv.width, image.rectTransform.rect.height / uv.height));
                Vector2 displayed = image.rectTransform.localRotation * (Vector3)local;
                if ((displayed - MinimapProjection.Project(point, origin, yaw, zoom, view.Radius)).sqrMagnitude > .0001f)
                    throw new InvalidOperationException("Terrain/marker coordinate mismatch.");
            }
            return "PASS: native references/import/mask/missing scripts and 12 terrain-marker coordinate cases";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static string Start(string output)
    {
        EditorSceneSafety.RequireNoUnsavedScenes("Minimap terrain Play");
        if (Phase != 0 || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Editor/verifier is busy.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Another account session must be returned first.");
        output = Path.GetFullPath(output);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
            throw new ArgumentException("A fresh Codex artifact directory is required.");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetInt(Key + "pid", System.Diagnostics.Process.GetCurrentProcess().Id);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 420).ToString("R", CultureInfo.InvariantCulture));
        SessionState.EraseString(Key + "failure"); Phase = 1;
        Application.runInBackground = true;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/MainScene.unity");
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (type != null) { var window = EditorWindow.GetWindow(type); window.Show(); window.Focus(); }
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "Account")); }
        catch (Exception error) { Fail(error); throw; }
        return "STARTED_ISOLATED_MINIMAP_PROBE";
    }

    public static string Cancel()
    {
        if (Phase == 0) return "IDLE";
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture));
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Fail(new OperationCanceledException("Minimap verification cancelled.")); return "RETURN_REQUESTED";
    }

    static void Tick()
    {
        try
        {
            if (Phase == 0) { EditorApplication.update -= Tick; return; }
            if (Phase == 3)
            {
                if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), CultureInfo.InvariantCulture))
                    throw new TimeoutException("Editor return deadline. Cancel() can retry after the Editor is available.");
                if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating) ReturnEditor();
                return;
            }
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), CultureInfo.InvariantCulture))
                throw new TimeoutException("Minimap verification deadline.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (!EditorApplication.isPlaying) return;
            if (Phase == 1)
            {
                if (!AccountBootstrap.Ready || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                    || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.MainSceneName) return;
                checks.Clear(); errors.Clear(); reports.Clear(); Application.logMessageReceived += Log;
                work = Probe(); frame = -1; Phase = 2;
            }
            if (!string.IsNullOrEmpty(pendingShot))
            {
                if (!File.Exists(pendingShot) || new FileInfo(pendingShot).Length == 0)
                { if (EditorApplication.timeSinceStartup > shotDeadline) throw new TimeoutException("Screenshot did not finish."); return; }
                pendingShot = null;
            }
            if (work == null) throw new InvalidOperationException("Probe lost.");
            if (frame == Time.frameCount) return; frame = Time.frameCount;
            if (work.MoveNext()) return;
            WriteResult("PASS", null); Cleanup(); Phase = 3; EditorApplication.isPlaying = false;
        }
        catch (Exception error)
        {
            if (Phase == 3)
            {
                File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(new { status = "BLOCKED", failure = error.ToString() }, Formatting.Indented));
                Cleanup(); Phase = 4; EditorApplication.update -= Tick;
            }
            else Fail(error);
        }
    }

    static IEnumerator Probe()
    {
        for (int i = 0; i < 15; i++) yield return null;
        var controller = WorldMinimapController.Instance;
        var view = controller.View;
        var actor = PlayerContext.Instance.CurrentActor;
        Check(controller.IsVisible && view.TerrainImage.texture != null, "MainScene terrain visible");
        reports.Add(TextureReport("MainScene", view.TerrainImage.texture));
        checks.Add(OverburstMinimapValidator.ValidateAuthored());
        reports.Add(OverburstMinimapValidator.ValidatePlayCore());
        for (int i = 0; i < 3; i++) yield return null;
        Shot("MainScene"); yield return null;
        int builds = controller.TerrainBuildCount;
        Vector3 initial = actor.transform.position;
        float yaw = QuarterViewCamera.ActiveInstance.CurrentYaw;
        foreach (float zoom in new[] { 20f, 55f, 80f })
        foreach (float rotation in new[] { 0f, 45f, 90f, 346f })
        {
            controller.SetZoom(zoom); QuarterViewCamera.ActiveInstance.SetYaw(rotation);
            ActorTeleportUtility.TeleportSafely(actor.transform, initial + Vector3.right * 2, actor.transform.rotation);
            for (int i = 0; i < 3; i++) yield return null;
            Check(view.TerrainImage.gameObject.activeSelf && Mathf.Abs(Mathf.DeltaAngle(view.TerrainImage.rectTransform.eulerAngles.z, rotation)) < .01f, "Zoom/yaw/teleport " + zoom + "/" + rotation);
        }
        Check(controller.TerrainBuildCount == builds, "Hub movement/zoom/yaw creates no terrain texture");
        ActorTeleportUtility.TeleportSafely(actor.transform, initial, actor.transform.rotation);
        controller.SetZoom(55); QuarterViewCamera.ActiveInstance.SetYaw(yaw);
        for (int i = 0; i < 10; i++) view.UpdateTerrain(initial + Vector3.right * i, yaw, 55);
        var watch = new System.Diagnostics.Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread(); watch.Start();
        for (int i = 0; i < 1000; i++) view.UpdateTerrain(initial + Vector3.right * i * .001f, i % 360, 55);
        watch.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "1000 warm terrain updates allocate 0 managed bytes");
        reports.Add(new { terrainUpdateCalls = 1000, allocatedBytes = allocated, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, scope = "Editor main-thread method calls; excludes Canvas rebuild/GPU" });
        PersistentSceneFlow.Instance.SwitchHubScene(PersistentSceneFlow.HideoutSceneName);
        while (PersistentSceneFlow.Instance.IsSwitching) yield return null;
        for (int i = 0; i < 12; i++) yield return null;
        Check(controller.IsVisible && view.TerrainImage.texture.name == "HideoutScene", "Hideout baked terrain binds"); Shot("HideoutScene"); yield return null;
        reports.Add(TextureReport("HideoutScene", view.TerrainImage.texture));
        PersistentSceneFlow.Instance.SwitchHubScene(PersistentSceneFlow.MainSceneName);
        while (PersistentSceneFlow.Instance.IsSwitching) yield return null;
        for (int i = 0; i < 12; i++) yield return null;
        for (int cycle = 0; cycle < 2; cycle++)
        {
            Check(Object.FindFirstObjectByType<MapDungeonPortal>().EnterLevelOne(), "Dungeon request " + cycle);
            while (PersistentSceneFlow.Instance.IsSwitching) yield return null;
            for (int i = 0; i < 15; i++) yield return null;
            Check(PersistentSceneFlow.Instance.CurrentSubSceneName == DiamondDungeonWorld.SceneName && controller.IsVisible
                && view.TerrainImage.texture != null && !((Texture2D)view.TerrainImage.texture).isReadable, "Dungeon runtime terrain binds " + cycle);
            builds = controller.TerrainBuildCount;
            if (cycle == 0) reports.Add(TextureReport("DiamondDungeon", view.TerrainImage.texture));
            for (int i = 0; i < 20; i++) { controller.SetZoom(i % 2 == 0 ? 20 : 80); yield return null; }
            Check(controller.TerrainBuildCount == builds, "Dungeon zoom does not rebuild terrain " + cycle);
            controller.SetZoom(55); for (int i = 0; i < 3; i++) yield return null;
            if (cycle == 0) { Shot("DiamondDungeon"); yield return null; }
            Texture old = view.TerrainImage.texture;
            RunWalkableContext.SetCurrent(RunWalkableContext.Current);
            for (int i = 0; i < 3; i++) yield return null;
            Check(controller.TerrainBuildCount == builds + 1 && old == null, "Revision replacement releases owned texture " + cycle);
            old = view.TerrainImage.texture; controller.ForceHide(); yield return null;
            Check(old == null && view.TerrainImage.texture == null && !controller.IsVisible, "Hide releases runtime terrain " + cycle);
            controller.ShowForScene(actor.transform, WorldSessionState.ContentScene.handle);
            for (int i = 0; i < 3; i++) yield return null;
            Check(controller.IsVisible && view.TerrainImage.texture != null, "Dungeon hide/show rebind " + cycle);
            old = view.TerrainImage.texture;
            Object.FindFirstObjectByType<RunLifetimeDriver>().RequestAbandon();
            while (WorldSessionState.Phase != WorldPhase.Hideout || PersistentSceneFlow.Instance.IsSwitching) yield return null;
            for (int i = 0; i < 12; i++) yield return null;
            Check(old == null && view.TerrainImage.texture.name == "MainScene", "Run return releases runtime texture and restores hub " + cycle);
        }
        Check(errors.Count == 0, "No errors during scoped minimap Play");
    }

    static void Shot(string name) { pendingShot = Path.Combine(Output, name + ".png"); shotDeadline = EditorApplication.timeSinceStartup + 20; ScreenCapture.CaptureScreenshot(pendingShot); }
    static object TextureReport(string scene, Texture texture) => new { scene, width = texture.width, height = texture.height, rgba32PixelBytes = texture.width * texture.height * 4, cpuReadable = ((Texture2D)texture).isReadable };
    static void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException(label); checks.Add(label); }
    static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void WriteResult(string status, string error) => File.WriteAllText(Path.Combine(Output, "result.json"), JsonConvert.SerializeObject(new { status, error, checks, errors, reports }, Formatting.Indented));
    static void Cleanup() { Application.logMessageReceived -= Log; work = null; pendingShot = null; }
    static void Fail(Exception error)
    {
        if (Phase == 0) return;
        SessionState.SetString(Key + "failure", error.ToString()); if (Directory.Exists(Output)) WriteResult("FAIL", error.ToString());
        Cleanup(); Phase = 3; if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
    }
    static void ReturnEditor()
    {
        if (System.Diagnostics.Process.GetCurrentProcess().Id != SessionState.GetInt(Key + "pid", 0)) throw new InvalidOperationException("Editor owner changed.");
        foreach (string value in new[] { Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.ActiveDirectory, SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") })
            if (!string.IsNullOrEmpty(value) && !Path.GetFullPath(value).Equals(Path.Combine(Output, "Account"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Another account session owns the Editor.");
        IsolatedSavePlayGuard.UseRealAccount();
        string start = SessionState.GetString(Key + "startScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        Application.runInBackground = SessionState.GetBool(Key + "background", false);
        File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(new { status = SessionState.GetString(Key + "failure", "") == "" ? "PASS" : "FAIL", failure = SessionState.GetString(Key + "failure", ""), environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), blocked = IsolatedSavePlayGuard.RequiresAccountChoice, active = IsolatedSavePlayGuard.ActiveDirectory, prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), compilationFailed = EditorUtility.scriptCompilationFailed }, Formatting.Indented));
        foreach (string name in new[] { "output", "startScene", "deadline", "failure" }) SessionState.EraseString(Key + name);
        SessionState.EraseInt(Key + "pid"); SessionState.EraseBool(Key + "background"); SessionState.EraseInt(Key + "phase"); EditorApplication.update -= Tick;
    }
}
