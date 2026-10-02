using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Changes only the initial player anchor and the unified item display layout.</summary>
public static class HideoutCatalogLayoutBuilder
{
    public const string ScenePath = "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    public static readonly Vector3 PlayerStart = new Vector3(-.65f, .12f, -1.4f);
    public static readonly Vector3 CatalogCenter = new Vector3(42f, 0f, 20f);
    public const int Columns = 16;
    public const float Spacing = 1.8f;
    public const float CampClearance = 3f;
    const string Output = "../개인파일/코덱스산출/Items/20261002_HideoutCatalogLayout";

    [MenuItem("OVERBURST/Items/Apply Unified Hideout Catalog Layout")]
    public static void Menu() => Debug.Log(Apply(Output, Hash(ScenePath)));

    public static string Apply(string output, string expectedHash)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        if (Hash(ScenePath) != expectedHash) throw new InvalidOperationException("Hideout changed; inspect the latest scene first.");
        if (SceneManager.GetSceneByPath(ScenePath).isLoaded)
            throw new InvalidOperationException("The existing Hideout must be returned by its owner before applying.");
        string backup = Path.Combine(output, "BeforeScene");
        if (File.Exists(Path.Combine(backup, "HideoutScene.unity")))
            backup = Path.Combine(output, "BeforeScene_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"));
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, Path.Combine(backup, "HideoutScene.unity"));
        File.Copy(ScenePath + ".meta", Path.Combine(backup, "HideoutScene.unity.meta"));
        string setup = EditorSnapshot();
        var active = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var start = roots.SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)).Single(p => p.ReturnPointId == "Default").transform;
            var origin = roots.Single(r => r.name == "Test Pickup Origin").transform;
            var spawner = roots.SelectMany(r => r.GetComponentsInChildren<ItemPickupSpawner>(true)).Single();
            var oldSamples = roots.SelectMany(r => r.GetComponentsInChildren<WorldItemPickup>(true)).ToArray();
            var removedComponents = oldSamples.SelectMany(p => p.GetComponentsInChildren<Component>(true)).Select(c => c.GetInstanceID()).ToHashSet();
            string unrelated = Unrelated(scene, start, origin, spawner, removedComponents);
            foreach (var sample in oldSamples) UnityEngine.Object.DestroyImmediate(sample.gameObject);
            Vector3 previousStart = start.position, previousCenter = origin.position;
            start.position = PlayerStart;
            origin.position = CatalogCenter;
            var fields = new SerializedObject(spawner);
            fields.FindProperty("authoredSpawnOrigin").objectReferenceValue = origin;
            fields.FindProperty("hideoutCatalogColumns").intValue = Columns;
            fields.FindProperty("hideoutCatalogSpacing").floatValue = Spacing;
            fields.ApplyModifiedPropertiesWithoutUndo();
            Validate(scene);
            if (unrelated != Unrelated(scene, start, origin, spawner, removedComponents))
                throw new InvalidOperationException("An unrelated scene component changed.");
            if (Hash(ScenePath) != expectedHash) throw new InvalidOperationException("Hideout changed externally; no save attempted.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Native scene save failed.");
            File.WriteAllText(Path.Combine(output, "layout_apply.json"), JsonConvert.SerializeObject(new {
                status = "PASS", beforeSceneHash = expectedHash, afterSceneHash = Hash(ScenePath),
                oldStart = V(previousStart), newStart = V(start.position), oldCenter = V(previousCenter), newCenter = V(origin.position),
                columns = Columns, spacing = Spacing, removedAuthoredSamples = oldSamples.Length, backup,
                unrelatedComponentsPreserved = true
            }, Formatting.Indented));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
        if (EditorSnapshot() != setup) throw new InvalidOperationException("Existing Editor scene setup changed.");
        VerifySaved(output);
        return "PASS: initial player anchor and one catalog grid saved; existing Editor and scene content preserved.";
    }

    public static string VerifySaved(string output)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            int count = Validate(scene);
            var camp = CampBounds(scene);
            var first = CatalogCenter + ItemPickupSpawner.CalculateCatalogGridOffset(0, count, Columns, Spacing);
            File.WriteAllText(Path.Combine(output, "layout_saved.json"), JsonConvert.SerializeObject(new { status = "PASS", catalogCount = count, columns = Columns, spacing = Spacing, campMin = V(camp.min), campMax = V(camp.max), gridWestEdge = first.x, campGap = first.x - camp.max.x }, Formatting.Indented));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        return "PASS: saved scene references, ground coverage, spawn clearance and catalog grid reloaded.";
    }

    static int Validate(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var points = roots.SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)).ToArray();
        if (points.Count(p => p.ReturnPointId == "Default") != 1 || points.Count(p => p.ReturnPointId == "DungeonPortal") != 1)
            throw new InvalidOperationException("Return anchors must remain unique.");
        var spawn = points.Single(p => p.ReturnPointId == "Default");
        if (Vector3.Distance(spawn.transform.position, PlayerStart) > .001f) throw new InvalidOperationException("Player anchor not saved.");
        var origin = roots.Single(r => r.name == "Test Pickup Origin").transform;
        var spawner = roots.SelectMany(r => r.GetComponentsInChildren<ItemPickupSpawner>(true)).Single();
        if (roots.SelectMany(r => r.GetComponentsInChildren<WorldItemPickup>(true)).Any())
            throw new InvalidOperationException("Authored samples must be replaced by the unified runtime catalog.");
        var fields = new SerializedObject(spawner);
        if (spawner.AuthoredSpawnOrigin != origin || Vector3.Distance(origin.position, CatalogCenter) > .001f
            || fields.FindProperty("hideoutCatalogColumns").intValue != Columns || Mathf.Abs(fields.FindProperty("hideoutCatalogSpacing").floatValue - Spacing) > .001f)
            throw new InvalidOperationException("Unified grid settings not saved.");
        var ground = roots.SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)).Single(c => c.name == "Camp Ground");
        if (!ground.Raycast(new Ray(PlayerStart + Vector3.up * 20, Vector3.down), out _, 40))
            throw new InvalidOperationException("No ground below initial player spawn.");
        var environment = roots.Single(r => r.name == "Barbarian Camp Environment");
        if (!BarbarianHideoutBuilder.ClearCapsule(environment, PlayerStart))
            throw new InvalidOperationException("Initial player spawn intersects a camp obstacle.");
        var catalog = ItemPickupSpawner.CollectHideoutCatalog();
        if (catalog.Count == 0 || catalog.Distinct().Count() != catalog.Count) throw new InvalidOperationException("Catalog empty or duplicated.");
        var campBounds = CampBounds(scene);
        for (int i = 0; i < catalog.Count; i++)
        {
            var position = CatalogCenter + ItemPickupSpawner.CalculateCatalogGridOffset(i, catalog.Count, Columns, Spacing);
            if (position.x < campBounds.max.x + CampClearance)
                throw new InvalidOperationException("Catalog cell overlaps camp clearance: " + i);
            if (!ground.Raycast(new Ray(position + Vector3.up * 20, Vector3.down), out _, 40))
                throw new InvalidOperationException("Catalog cell outside ground: " + i);
        }
        foreach (var go in roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go.gameObject) != 0)
                throw new InvalidOperationException("Missing script: " + go.name);
        return catalog.Count;
    }

    public static Bounds CampBounds(Scene scene)
    {
        var layout = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "Camp Layout");
        var renderers = layout.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        if (renderers.Length == 0) throw new InvalidOperationException("Camp has no visible geometry.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    static string Unrelated(Scene scene, Transform start, Transform origin, ItemPickupSpawner spawner, HashSet<int> removedComponents)
    {
        var rows = new List<string>();
        foreach (var component in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)))
        {
            if (component == null || component == start || component == origin || removedComponents.Contains(component.GetInstanceID())) continue;
            var json = EditorJsonUtility.ToJson(component);
            if (component == spawner)
            {
                var value = Newtonsoft.Json.Linq.JObject.Parse(json);
                value.Remove("authoredSpawnOrigin"); value.Remove("hideoutCatalogColumns"); value.Remove("hideoutCatalogSpacing");
                json = value.ToString(Formatting.None);
            }
            rows.Add(component.GetInstanceID() + ":" + component.GetType().FullName + ":" + json);
        }
        return string.Join("\n", rows);
    }

    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Finish the current Editor operation first.");
    }
    static float[] V(Vector3 p) => new[] { p.x, p.y, p.z };
    static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    public static string EditorSnapshot() => string.Join("|", Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
        var scene = SceneManager.GetSceneAt(i); return scene.path + ":" + scene.isLoaded + ":" + scene.isDirty + ":" + scene.rootCount + ":" + (SceneManager.GetActiveScene() == scene);
    }));
}
