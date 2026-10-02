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
using Object = UnityEngine.Object;

/// <summary>Boots the product and checks the complete catalog, pickup/save identities and hideout reentry.</summary>
[InitializeOnLoad]
public static class HideoutCatalogPlayVerifier
{
    const string Key = "Overburst.HideoutCatalogPlayVerifier.";
    static readonly List<object> checks = new List<object>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator work;
    static int lastFrame, failures;
    static double deadline;
    static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static HideoutCatalogPlayVerifier() { EditorApplication.playModeStateChanged += State; }

    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Editor must be returned and idle.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "sceneSetup", HideoutCatalogLayoutBuilder.EditorSnapshot());
        SessionState.SetString(Key + "previousStartScene", EditorSceneManager.playModeStartScene != null ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "");
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        SessionState.SetBool(Key + "run", true);
        SessionState.SetString(Key + "status", "RUNNING");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount")); }
        catch { RestoreEditor(); SessionState.SetBool(Key + "run", false); throw; }
        return "RUNNING: isolated product catalog boot and reentry checks.";
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "run", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); failures = 0; lastFrame = -1;
            SessionState.SetBool(Key + "background", Application.runInBackground);
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 150;
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
            RestoreEditor();
            bool same = HideoutCatalogLayoutBuilder.EditorSnapshot() == SessionState.GetString(Key + "sceneSetup", "");
            File.WriteAllText(Path.Combine(Output, "editor_restored.json"), JsonConvert.SerializeObject(new {
                status = same ? "PASS" : "FAIL", sceneSetupPreserved = same,
                isolatedEnvironmentCleared = string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            }, Formatting.Indented));
            SessionState.SetBool(Key + "run", false);
        }
    }

    static void RestoreEditor()
    {
        string previous = SessionState.GetString(Key + "previousStartScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
    }

    static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Catalog boot/reentry timed out.");
            if (!work.MoveNext()) Finish();
        }
        catch (Exception error) { Check(false, error.ToString()); Finish(); }
    }

    static void Finish()
    {
        string status = failures == 0 && errors.Count == 0 ? "PASS" : "FAIL";
        SessionState.SetString(Key + "status", status);
        File.WriteAllText(Path.Combine(Output, "Result.json"), JsonConvert.SerializeObject(new { status, failures, checks, errors, account = AccountBootstrap.SaveDirectory }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    static void Check(bool passed, string name)
    { checks.Add(new { name, passed }); if (!passed) failures++; }

    static WorldItemPickup[] Pickups(Scene scene) => Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Where(p => p.gameObject.scene == scene).ToArray();

    static IEnumerator Verify()
    {
        while (!AccountBootstrap.Ready || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout)
        {
            if (AccountBootstrap.Error != null) throw new InvalidOperationException(AccountBootstrap.Error);
            yield return null;
        }
        for (int i = 0; i < 8; i++) yield return null;
        Check(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            && Path.GetFullPath(AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Product boot uses this verifier's isolated account");
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var catalog = ItemPickupSpawner.CollectHideoutCatalog();
        var scene = SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName);
        var pickups = CheckDisplay(scene, catalog, registry, "initial boot");
        var actor = PlayerContext.Instance.CurrentActor;
        var spawn = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)).Single(p => p.ReturnPointId == "Default");
        Check(Vector2.Distance(new Vector2(actor.transform.position.x, actor.transform.position.z), new Vector2(spawn.transform.position.x, spawn.transform.position.z)) < .12f, "Player starts at the saved photographed-area anchor");
        Check(actor.Movement.IsGrounded, "Player starts grounded");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "player_start.png"));
        for (int i = 0; i < 4; i++) yield return null;
        var overview = CaptureOverview();
        try { while (overview.MoveNext()) yield return null; }
        finally { (overview as IDisposable)?.Dispose(); }
        ItemPickupSpawner.SpawnConfiguredPickupsInScene(scene);
        Check(Pickups(scene).Length == pickups.Length, "Repeated startup call creates no duplicates");

        File.WriteAllText(Path.Combine(Output, "catalog_initial.json"), JsonConvert.SerializeObject(pickups.Select(p => new {
            contentId = registry.IdFor(p.RuntimeItem.baseData), p.RuntimeItem.grade,
            position = new[] { p.transform.position.x, p.transform.position.y, p.transform.position.z }
        }), Formatting.Indented));
        var initialGrades = pickups.OrderBy(p => registry.IdFor(p.RuntimeItem.baseData), StringComparer.Ordinal).Select(p => p.RuntimeItem.grade).ToArray();
        var oldItems = pickups.Select(p => p.RuntimeItem.runtimeInstanceId).ToHashSet();
        foreach (var type in catalog.Select(d => d.GetType()).Distinct())
        {
            var pickup = pickups.First(p => p.RuntimeItem.baseData.GetType() == type);
            ItemData item = pickup.RuntimeItem;
            var snapshot = ItemSnapshotCodec.Capture(item, registry);
            var restored = ItemSnapshotCodec.Restore(snapshot, registry);
            Check(restored.runtimeInstanceId == item.runtimeInstanceId && restored.baseData == item.baseData && restored.grade == item.grade && restored.stackCount == 1, "Item snapshot identity/grade/count roundtrip: " + type.Name);
            Check(pickup.TryPickup(PlayerAccountInventoryService.SharedInventory), "Existing pickup route accepts catalog item: " + type.Name);
            yield return null;
        }
        var session = AccountGameplaySession.Current;
        Check(session != null && session.FlushPendingSave(), "Acquired catalog samples checkpoint in isolated account");
        var unload = SceneManager.UnloadSceneAsync(scene);
        while (unload != null && !unload.isDone) yield return null;
        PersistentSceneFlow.Instance.SwitchHubScene(PersistentSceneFlow.HideoutSceneName);
        yield return null;
        while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        for (int i = 0; i < 5; i++) yield return null;
        scene = SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName);
        var next = CheckDisplay(scene, catalog, registry, "hideout reentry");
        Check(!next.Any(p => oldItems.Contains(p.RuntimeItem.runtimeInstanceId)), "Reentry clears prior world items and creates new identities");
        Check(!next.OrderBy(p => registry.IdFor(p.RuntimeItem.baseData), StringComparer.Ordinal).Select(p => p.RuntimeItem.grade).SequenceEqual(initialGrades), "Reentry rolls fresh random grades");
        File.WriteAllText(Path.Combine(Output, "catalog_reentry.json"), JsonConvert.SerializeObject(next.Select(p => new {
            contentId = registry.IdFor(p.RuntimeItem.baseData), p.RuntimeItem.grade,
            position = new[] { p.transform.position.x, p.transform.position.y, p.transform.position.z }
        }), Formatting.Indented));
    }

    static WorldItemPickup[] CheckDisplay(Scene scene, List<BaseItemData> catalog, AccountContentRegistry registry, string label)
    {
        var pickups = Pickups(scene);
        var spawner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ItemPickupSpawner>(true)).Single();
        Check(pickups.Length == catalog.Count && spawner.HideoutCatalogSpawnCount == catalog.Count, "Complete catalog count: " + label);
        Check(pickups.Select(p => p.RuntimeItem.baseData).Distinct().Count() == catalog.Count
            && pickups.Select(p => p.RuntimeItem.baseData).ToHashSet().SetEquals(catalog), "Exactly one pickup per catalog definition: " + label);
        Check(pickups.All(p => p.RuntimeItem.stackCount == 1 && ItemGradeAvailabilityPolicy.IsEnabled(p.RuntimeItem.grade)), "One item and enabled random grade for every pickup: " + label);
        Check(pickups.Select(p => p.RuntimeItem.grade).Distinct().Count() > 1, "Random grades vary across the display: " + label);
        Check(!Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t => t.name.StartsWith("[TEMP] Hideout", StringComparison.Ordinal)), "Separate temporary bag spawner absent: " + label);
        var camp = HideoutCatalogLayoutBuilder.CampBounds(scene);
        Check(pickups.All(p => p.transform.position.x >= camp.max.x + HideoutCatalogLayoutBuilder.CampClearance), "Entire catalog stays outside camp with a clear gap: " + label);
        var ground = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)).Single(c => c.name == "Camp Ground");
        foreach (var p in pickups)
        {
            Check(ground.Raycast(new Ray(p.transform.position + Vector3.up * 20, Vector3.down), out _, 40), "Ground below " + p.RuntimeItem.baseData.name + ": " + label);
            ItemSnapshotCodec.Validate(ItemSnapshotCodec.Capture(p.RuntimeItem, registry), registry);
        }
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < pickups.Length; i++)
            for (int j = i + 1; j < pickups.Length; j++)
                nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(pickups[i].transform.position.x, pickups[i].transform.position.z), new Vector2(pickups[j].transform.position.x, pickups[j].transform.position.z)));
        Check(nearest >= HideoutCatalogLayoutBuilder.Spacing - .005f, "Every item keeps the requested grid spacing: " + label);
        return pickups;
    }

    static IEnumerator CaptureOverview()
    {
        var go = new GameObject("Hideout Catalog Verification Camera");
        try
        {
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Camera.main);
            camera.depth = Camera.main.depth + 10;
            camera.orthographic = true;
            camera.orthographicSize = 40;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 200;
            camera.transform.SetPositionAndRotation(new Vector3(18, 80, 8), Quaternion.Euler(90, 0, 0));
            for (int i = 0; i < 3; i++) yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "camp_catalog_overview.png"));
            for (int i = 0; i < 4; i++) yield return null;
        }
        finally { if (go != null) Object.DestroyImmediate(go); }
    }
}
