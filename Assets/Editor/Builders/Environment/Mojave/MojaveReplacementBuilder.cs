using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MojaveReplacementBuilder
{
    public const string SceneRoot = MojaveWorldBuilder.SceneRoot + "/Replacement";
    public const string DataRoot = MojaveWorldBuilder.OwnedRoot + "/Replacement";
    public const string MinimapRoot = DataRoot + "/Minimaps";

    static string DirectoryFor(string directory)
    {
        directory = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Application.dataPath + "/../../개인파일/코덱스산출") + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(directory))
            throw new ArgumentException("Existing owned evidence directory required.");
        return directory;
    }
    static void Guard()
    {
        MojaveWorldBuilder.Guard();
        if (IsolatedSavePlayGuard.ActiveDirectory != "" || SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") != ""
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))
            || SessionState.GetString("Overburst.DesertMinimapBuilder.output", "") != ""
            || SessionState.GetString("Overburst.MapRunEntryPlayVerifier.pending", "") != ""
            || SessionState.GetString("Overburst.MapRunEntryPlayVerifier.return", "") != "")
            throw new InvalidOperationException("Preserve the other Editor owner's preparation and reservations.");
    }
    static void Write(string path, object value) => File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
    static string SceneState() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount)
        .Select(i => SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty, s.rootCount }));
    static JArray Plans(string directory)
    {
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(directory, "approved-plan.json")));
        var rows = (JArray)plan["candidates"];
        if ((int)plan["revision"] != 3 || rows.Count != 30 || rows.Select(r => (string)r["planningId"]).Distinct().Count() != 30)
            throw new InvalidDataException("The approved 30-map rectangular plan is required.");
        foreach (var row in rows)
        {
            var c = Counts(row); int total = (int)row["areaCount"];
            bool valid = total == 4 ? c == new Vector3Int(0, 2, 0) || c == new Vector3Int(0, 1, 1)
                : total == 6 ? c == new Vector3Int(1, 1, 2) : total == 9 ? c == new Vector3Int(1, 2, 4)
                : total == 12 ? c == new Vector3Int(2, 3, 5) : total == 15 && c == new Vector3Int(3, 4, 6);
            if (!valid || c.x + c.y + c.z + 2 != total || (int)row["entranceSmall"] != 1 || (int)row["bossDedicated"] != 1)
                throw new InvalidDataException("Unapproved combat composition: " + row["planningId"]);
        }
        return rows;
    }
    static Vector3Int Counts(JToken row) => new Vector3Int((int)row["ordinarySmall"], (int)row["ordinaryMedium"], (int)row["ordinaryLarge"]);
    static Vector2 Dimensions(JToken row) => new Vector2((int)row["width"], (int)row["depth"]);
    static string Signature(MojaveLayout layout) => JsonConvert.SerializeObject(new {
        bounds = new[] { layout.bounds.x, layout.bounds.y },
        places = layout.places.Select(p => new { p.patch, p.kind, p.role, p.rotation, p.height, center = new[] { p.center.x, p.center.y }, radius = new[] { p.radius.x, p.radius.y } }),
        trails = layout.trails.Select(t => new { t.from, t.to, points = t.points.Select(p => new[] { p.x, p.y }), t.widths, t.heights }), layout.junctionRounding
    });

    static void Check(MojaveLayout layout, MojaveCatalog catalog, JToken row)
    {
        if (layout.places.Count != (int)row["areaCount"] || layout.bounds != Dimensions(row)
            || layout.places.Count(p => p.role == MojaveTileRole.Entrance) != 1 || layout.places.Count(p => p.role == MojaveTileRole.Boss) != 1)
            throw new InvalidOperationException("Map dimensions or dedicated roles differ from the approved plan.");
        var entry = layout.places.Single(p => p.role == MojaveTileRole.Entrance);
        var boss = layout.places.Single(p => p.role == MojaveTileRole.Boss);
        if (catalog.patches[entry.patch].combatSize != MojaveCombatSize.Small || catalog.patches[boss.patch].name != "Boss_Basin_Combat")
            throw new InvalidOperationException("Dedicated entrance/boss identity changed.");
        var ordinary = layout.places.Where(p => p.role == MojaveTileRole.Combat).ToArray();
        var counts = new Vector3Int(ordinary.Count(p => catalog.patches[p.patch].combatSize == MojaveCombatSize.Small),
            ordinary.Count(p => catalog.patches[p.patch].combatSize == MojaveCombatSize.Medium), ordinary.Count(p => catalog.patches[p.patch].combatSize == MojaveCombatSize.Large));
        if (counts != Counts(row)) throw new InvalidOperationException("Ordinary combat composition changed.");
        int bossIndex = layout.places.IndexOf(boss);
        if (layout.trails.Count(t => t.from == bossIndex || t.to == bossIndex) != 1 || layout.RouteDistances(0).Any(float.IsInfinity))
            throw new InvalidOperationException("Disconnected map or multiple boss approaches.");
        float Radius(MojavePlace p) => Mathf.Max(catalog.patches[p.patch].size * .707107f, Mathf.Max(p.radius.x, p.radius.y) * 1.28f);
        for (int i = 0; i < layout.places.Count; i++)
        {
            var p = layout.places[i]; float radius = Radius(p);
            if (Mathf.Abs(p.center.x) + radius > layout.bounds.x * .5f - 7.999f || Mathf.Abs(p.center.y) + radius > layout.bounds.y * .5f - 7.999f)
                throw new InvalidOperationException("Tile exceeds rectangular map boundary.");
            for (int j = i + 1; j < layout.places.Count; j++)
                if (Vector2.Distance(p.center, layout.places[j].center) < radius + Radius(layout.places[j]) + 5.999f)
                    throw new InvalidOperationException("Overlapping full tile footprints.");
        }
    }
    public static string VerifyPlans(string directory)
    {
        Guard(); directory = DirectoryFor(directory); var rows = Plans(directory); var reports = new List<object>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; int accepted = 0; string last = null;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                int seed = 131001 + i * 1000 + attempt;
                try
                {
                    var a = MojaveMapGeneratorWindow.BuildLayoutPreview(seed, Math.Max((int)row["width"], (int)row["depth"]), (int)row["areaCount"], true, true, true, true, out var catalog, true, Dimensions(row), Counts(row));
                    Check(a, catalog, row);
                    var b = MojaveMapGeneratorWindow.BuildLayoutPreview(seed, Math.Max((int)row["width"], (int)row["depth"]), (int)row["areaCount"], true, true, true, true, out _, true, Dimensions(row), Counts(row));
                    if (Signature(a) != Signature(b)) throw new InvalidOperationException("Layout reconstruction is not deterministic.");
                    accepted = seed; break;
                }
                catch (InvalidOperationException error) { last = error.Message; }
            }
            if (accepted == 0) throw new InvalidOperationException("No valid layout for " + row["planningId"] + ": " + last);
            reports.Add(new { index = i, id = (string)row["planningId"], seed = accepted, status = "PASS", dimensions = new[] { (int)row["width"], (int)row["depth"] }, ordinary = new[] { (int)row["ordinarySmall"], (int)row["ordinaryMedium"], (int)row["ordinaryLarge"] } });
        }
        string json = JsonConvert.SerializeObject(new { status = "PASS_30_APPROVED_LAYOUTS", maps = reports }, Formatting.Indented);
        File.WriteAllText(Path.Combine(directory, "Evidence/layouts.json"), json); return json;
    }

    public static string BuildMap(string directory, int index)
    {
        Guard(); directory = DirectoryFor(directory); var rows = Plans(directory);
        if (index < 0 || index >= rows.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var row = rows[index]; string id = (string)row["planningId"], resultPath = Path.Combine(directory, "Evidence/map-" + id + ".json");
        if (File.Exists(resultPath)) throw new InvalidOperationException("Inspect the durable map result before repeating generation.");
        var verified = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(directory, "Evidence/layouts.json")))["maps"];
        int seed = (int)verified[index]["seed"];
        string scenePath = SceneRoot + "/Mojave_" + id + ".unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath)) throw new InvalidOperationException("Inspect the existing staged scene before repeating generation.");
        string initial = SceneState(); int previews = EditorSceneManager.previewSceneCount;
        var active = SceneManager.GetActiveScene(); var selection = Selection.objects; MojaveMapGeneratorWindow window = null; Scene saved = default;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            window = ScriptableObject.CreateInstance<MojaveMapGeneratorWindow>(); window.SetMapPreset(2, false);
            window.SetMapSize(Math.Max((int)row["width"], (int)row["depth"]), false); window.SetCombatCount((int)row["areaCount"], false);
            window.SetCombatSizes(true, false); window.SetRandomCombatLayout(true, false); window.SetBackdrops(true, false);
            Write(Path.Combine(directory, "Evidence/current-map.json"), new { id, index, stage = "GENERATING", utc = DateTime.UtcNow });
            window.Generate(seed, Dimensions(row), Counts(row)); var world = window.DraftWorld; Check(world.layout, world.catalog, row);
            var explorer = world.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MojaveExplorer>()).Single();
            foreach (var place in world.layout.places)
                if (explorer.FindPath(world.layout.places[0].center, place.center).Count == 0) throw new InvalidOperationException("Generator cannot reach " + place.name);
            window.ShowPlace(1); Capture(window.PreviewCamera, Path.Combine(directory, "Captures/" + id + "_Overview.png"));
            window.ShowPlace(0); Capture(window.PreviewCamera, Path.Combine(directory, "Captures/" + id + "_Entrance.png"));
            window.ShowPlace(world.layout.places.FindIndex(p => p.role == MojaveTileRole.Boss) + 2);
            Capture(window.PreviewCamera, Path.Combine(directory, "Captures/" + id + "_Boss.png"));
            var previousData = new HashSet<string>(AssetDatabase.FindAssets("", new[] { MojaveWorldBuilder.OwnedRoot + "/Data/Generated" }).Select(AssetDatabase.GUIDToAssetPath), StringComparer.Ordinal);
            string generatedScene = window.SaveSnapshot(); MojaveWorldBuilder.Folder(SceneRoot); MojaveWorldBuilder.Folder(DataRoot + "/Terrain");
            var moves = new List<object>();
            foreach (var dependency in AssetDatabase.GetDependencies(generatedScene, true).Where(p => p.StartsWith(MojaveWorldBuilder.OwnedRoot + "/Data/Generated/", StringComparison.Ordinal)))
            {
                if(previousData.Contains(dependency))throw new InvalidOperationException("A replacement scene still references a prior generated map: " + dependency);
                string target = DataRoot + "/Terrain/" + Path.GetFileName(dependency);
                string guid = AssetDatabase.AssetPathToGUID(dependency); Move(dependency, target); moves.Add(new { from = dependency, to = target, guid });
            }
            Move(generatedScene, scenePath);
            saved = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var restored = saved.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MojaveWorld>(true)).Single();
            restored.EnsureLayout(); Check(restored.layout, restored.catalog, row);
            if (Signature(restored.layout) != Signature(world.layout) || restored.bakedSeed != seed || restored.bakedLayoutVersion != MojaveLayout.Version)
                throw new InvalidOperationException("Stored scene reconstruction differs from the generated map.");
            if (saved.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0))
                throw new InvalidOperationException("Missing script in replacement map.");
            var candidate = new MapTerrainCatalog.DesertCandidate { id = id, scene = scenePath, seed = seed, areaCount = (int)row["areaCount"], width = (int)row["width"], depth = (int)row["depth"] };
            var result = new { status = "PASS_SAVED_MAP", candidate, ordinary = new[] { (int)row["ordinarySmall"], (int)row["ordinaryMedium"], (int)row["ordinaryLarge"] }, sceneGuid = AssetDatabase.AssetPathToGUID(scenePath), terrain = AssetDatabase.GetAssetPath(restored.surface.terrainData), seconds = watch.Elapsed.TotalSeconds, generatorPaths = restored.layout.places.Count, moves };
            Write(resultPath, result); return JsonConvert.SerializeObject(result);
        }
        catch (Exception error) { Write(Path.Combine(directory, "Evidence/map-" + id + "-error.json"), new { id, error = error.ToString(), seconds = watch.Elapsed.TotalSeconds }); throw; }
        finally
        {
            if (saved.IsValid()) EditorSceneManager.CloseScene(saved, true);
            if (window) Object.DestroyImmediate(window);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); Selection.objects = selection;
            bool returned = SceneState() == initial && previews == EditorSceneManager.previewSceneCount;
            Write(Path.Combine(directory, "Evidence/map-" + id + "-return.json"), new { status = returned ? "PASS_RETURN" : "FAIL_RETURN", scenes = JArray.Parse(SceneState()), previewCount = EditorSceneManager.previewSceneCount, playStopped = !EditorApplication.isPlayingOrWillChangePlaymode, save = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "" });
            if (!returned) throw new InvalidOperationException("Shared Editor scene or preview state was not returned.");
        }
    }
    public static string BakeMinimap(string directory, int index)
    {
        Guard(); directory = DirectoryFor(directory); var row = Plans(directory)[index]; string id = (string)row["planningId"];
        string resultPath = Path.Combine(directory, "Evidence/minimap-" + id + ".json");
        if (File.Exists(resultPath)) throw new InvalidOperationException("Inspect the durable minimap result before repeating baking.");
        var map = JObject.Parse(File.ReadAllText(Path.Combine(directory, "Evidence/map-" + id + ".json")));
        var candidate = map["candidate"].ToObject<MapTerrainCatalog.DesertCandidate>();
        var result = DesertMinimapBuilder.BakeCandidate(candidate, Path.Combine(directory, "MinimapBake/" + id + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")), MinimapRoot);
        Write(resultPath, result); return JsonConvert.SerializeObject(result);
    }
    static void Move(string from, string to)
    {
        if (AssetDatabase.LoadMainAssetAtPath(to) || File.Exists(to)) throw new IOException("Preserve existing destination: " + to);
        string guid = AssetDatabase.AssetPathToGUID(from); string error = AssetDatabase.MoveAsset(from, to);
        if (error != "" || AssetDatabase.AssetPathToGUID(to) != guid) throw new IOException("Native GUID-preserving move failed: " + from + ": " + error);
    }
    static void Capture(Camera camera, string path)
    {
        var priorTarget = camera.targetTexture; var priorActive = RenderTexture.active; float priorAspect = camera.aspect;
        var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32); Texture2D image = null;
        try { camera.targetTexture = rt; camera.aspect = 16f / 9; camera.Render(); RenderTexture.active = rt; image = new Texture2D(1920, 1080, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { camera.targetTexture = priorTarget; camera.aspect = priorAspect; RenderTexture.active = priorActive; RenderTexture.ReleaseTemporary(rt); if (image) Object.DestroyImmediate(image); }
    }
}
