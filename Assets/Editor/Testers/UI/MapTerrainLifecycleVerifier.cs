using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Caves;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MapTerrainLifecycleVerifier
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Run(string output, bool includeLargeCave = true)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output)) throw new ArgumentException("Fresh owned output required");
        Directory.CreateDirectory(output);
        var checks = new List<string>(); var measurements = new List<object>();
        void Check(bool valid, string label) { if (!valid) throw new InvalidOperationException(label); checks.Add(label); }
        int before = Textures(); var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var cave = Cave(scene, 4);
            var platform = cave.courts[0].tile.transform;
            var wall = Box(platform, "Thin solid wall", new Vector3(0, 1, 0), new Vector3(.1f, 2, 17));
            Box(platform, "Trigger", new Vector3(-4, 1, 0), Vector3.one).isTrigger = true;
            Box(platform, "Overhead", new Vector3(-4, 4, 3), Vector3.one);
            Box(platform, "Low dressing", new Vector3(-4, .1f, -3), new Vector3(1, .2f, 1));
            Box(platform, "Inactive", new Vector3(-6, 1, 3), Vector3.one).gameObject.SetActive(false);
            Box(platform, "Dynamic body", new Vector3(-6, 1, -3), Vector3.one).gameObject.AddComponent<Rigidbody>().useGravity = false;
            Box(platform, "Floor", new Vector3(0, -.1f, 0), new Vector3(16, .2f, 16)).gameObject.AddComponent<CaveWalkSurface>();
            Physics.SyncTransforms();
            using (var map = CaveMinimapTerrain.Build(cave))
            {
                Check(map.IsGround(map.CellAt(new Vector3(.25f, 0, .25f))), "A thin wall does not erase neighbouring clear floor centres");
                map.Reveal(new Vector3(-2, 0, 0), 100);
                Check(!map.IsRevealed(new Vector3(2, 0, 0)), "Exploration cannot cross a solid internal wall");
                foreach (var p in new[] { new Vector3(-4, 0, 0), new Vector3(-4, 0, 3), new Vector3(-4, 0, -3), new Vector3(-6, 0, 3), new Vector3(-6, 0, -3) })
                    Check(map.IsGround(map.CellAt(p)), "Trigger/overhead/low/inactive/dynamic objects keep floor: " + p);
            }
            wall.size = new Vector3(.1f, 2, 8); wall.transform.localPosition = new Vector3(0, 1, 4);
            Physics.SyncTransforms();
            using (var map = CaveMinimapTerrain.Build(cave))
            {
                map.Reveal(new Vector3(-2, 0, 0), 100);
                Check(map.IsRevealed(new Vector3(2, 0, 0)), "A real opening around the wall stays connected");
            }
            var second = cave.courts[1].tile.transform;
            second.rotation = Quaternion.Euler(0, 37, 0); second.localScale = new Vector3(1.3f, 1, .8f);
            var rotated = Box(second, "Rotated obstacle", new Vector3(2, 1, 1), new Vector3(3, 2, 3));
            Physics.SyncTransforms();
            using (var map = CaveMinimapTerrain.Build(cave))
                Check(!map.IsGround(map.CellAt(rotated.transform.position)), "Obstacle sampling follows the generated platform transform");
            VerifyLifecycle(cave, scene, Check);
            Object.DestroyImmediate(cave.gameObject);
            var library = AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(CavePlatformMapBuilder.LibraryPath);
            foreach (var definition in library.platforms)
            {
                var actual = new GameObject("Actual cave minimap fixture").AddComponent<CaveWorld>();
                SceneManager.MoveGameObjectToScene(actual.gameObject, scene);
                try
                {
                    actual.generatedRoot = new GameObject("Actual generated platform").transform; actual.generatedRoot.SetParent(actual.transform, false);
                    var instance = Object.Instantiate(definition.prefab, actual.generatedRoot);
                    var tile = instance.GetComponent<CaveTile>();
                    actual.courts.Add(new CaveWorld.Court { name = definition.source.name, tile = tile, center = tile.transform.TransformPoint(definition.spawn) });
                    Physics.SyncTransforms();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using (var map = CaveMinimapTerrain.Build(actual))
                    {
                        long milliseconds = watch.ElapsedMilliseconds;
                        int footprint = 0, floor = 0; var boundary = tile.GetComponent<CavePlatformBoundary>();
                        for (int i = 0; i < map.Width * map.Height; i++)
                        {
                            if (boundary.ContainsLocal(boundary.transform.InverseTransformPoint(map.Center(i)))) footprint++;
                            if (map.IsGround(i)) floor++;
                        }
                        Check(floor > 10 && floor <= footprint, "Actual platform retains floor within its authored outline: " + definition.source.name);
                        for (int i = 0; i < map.Width * map.Height; i += 8) if (map.IsGround(i)) map.Reveal(map.Center(i), 12);
                        map.EnsureTextures();
                        File.WriteAllBytes(Path.Combine(output, definition.source.name + "-map.png"), map.TerrainTexture.EncodeToPNG());
                        measurements.Add(new { platform = definition.source.name, map.Width, map.Height, footprint, floor, blocked = footprint - floor, milliseconds });
                    }
                }
                finally { Object.DestroyImmediate(actual.gameObject); }
            }
            VerifyGeneratedEntrances(scene, library, Check, measurements, includeLargeCave);
            Check(Textures() == before, "Native obstacle and binding fixtures release all owned map textures");
            string path = Path.Combine(output, "native-result.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = "PASS", checks, measurements, includeLargeCave, scope = "Native collider/topology and synthetic preparation lifecycle; actual generation/Play separate" }, Formatting.Indented));
            return path;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "native-result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, measurements, error = error.ToString() }, Formatting.Indented));
            throw;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    static void VerifyGeneratedEntrances(Scene scene, CavePlatformLibrary library, Action<bool, string> check, List<object> measurements, bool includeLargeCave)
    {
        foreach (var fixture in new[] { (count: 4, seed: 48310), (count: 4, seed: 48601), (count: 20, seed: 48310) })
        {
            if (fixture.count == 20 && !includeLargeCave) continue;
            int seed = fixture.seed;
            var plan = CavePlatformMapBuilder.Plan(library, fixture.count, seed);
            var root = new GameObject("Generated entrance regression"); SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var world = root.AddComponent<CaveWorld>(); world.authoredLayout = true;
                world.generatedRoot = new GameObject("Generated tiles").transform; world.generatedRoot.SetParent(root.transform, false);
                foreach (var room in plan.rooms)
                {
                    var definition = library.platforms[room.platform]; var go = Object.Instantiate(definition.prefab, world.generatedRoot);
                    go.transform.SetPositionAndRotation(room.position, Quaternion.Euler(0, room.yaw, 0));
                    world.courts.Add(new CaveWorld.Court { name = definition.source.name, tile = go.GetComponent<CaveTile>(), center = go.transform.TransformPoint(definition.spawn) });
                }
                foreach (var link in plan.links) CaveRuntimeGenerator.Connect(world, library, link);
                Physics.SyncTransforms();
                var obstacles = world.generatedRoot.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && !c.GetComponentInParent<CaveWalkSurface>()).ToArray();
                var probeObject = new GameObject("Owned player clearance probe"); probeObject.transform.SetParent(root.transform, false);
                var probe = probeObject.AddComponent<CapsuleCollider>(); probe.center = Vector3.up * .95f; probe.height = 1.8f; probe.radius = .32f;
                foreach (var link in plan.links)
                    for (int step = 0; step <= 20; step++)
                    {
                        var point = Vector3.Lerp(link.start, link.end, step / 20f);
                        foreach (var collider in obstacles)
                        {
                            var bounds = collider.bounds;
                            if (bounds.max.y <= point.y + .5f || bounds.min.y >= point.y + 1.85f
                                || point.x < bounds.min.x - .4f || point.x > bounds.max.x + .4f || point.z < bounds.min.z - .4f || point.z > bounds.max.z + .4f) continue;
                            if (Physics.ComputePenetration(probe, point, Quaternion.identity, collider, collider.transform.position, collider.transform.rotation, out _, out float depth) && depth > .015f)
                                throw new InvalidOperationException("Generated bridge blocked by platform decoration: seed " + seed + " link " + link.a + "→" + link.b + " " + collider.name + " depth " + depth);
                        }
                    }
                check(true, "New and existing platforms leave their complete connecting paths clear: " + seed);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using (var map = CaveMinimapTerrain.Build(world))
                {
                    long buildMs = watch.ElapsedMilliseconds;
                    map.Reveal(world.courts[0].center, 500);
                    foreach (var court in world.courts)
                    {
                        var boundary = court.tile.GetComponent<CavePlatformBoundary>();
                        var target = Enumerable.Range(0, map.Width * map.Height).Where(i => map.IsGround(i)
                            && boundary.ContainsLocal(boundary.transform.InverseTransformPoint(map.Center(i))))
                            .OrderBy(i => (map.Center(i) - court.center).sqrMagnitude).First();
                        check(map.IsRevealed(map.Center(target)), "Generated obstacle topology reaches platform " + court.name + " seed " + seed);
                    }
                    measurements.Add(new { seed, fixture.count, map.Width, map.Height, buildMs });
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
    static int Textures() => Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t.name == "Explored terrain (run)" || t.name == "Explored outline (run)");
    static BoxCollider Box(Transform parent, string name, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        var box = go.AddComponent<BoxCollider>(); box.size = size; return box;
    }
    static CaveWorld Cave(Scene scene, int count)
    {
        var root = new GameObject("Owned cave map fixture"); SceneManager.MoveGameObjectToScene(root, scene);
        var cave = root.AddComponent<CaveWorld>();
        cave.generatedRoot = new GameObject("Generated tiles").transform; cave.generatedRoot.SetParent(root.transform, false);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Platform " + i); go.transform.SetParent(cave.generatedRoot, false); go.transform.localPosition = Vector3.right * i * 24;
            var boundary = go.AddComponent<CavePlatformBoundary>();
            boundary.loops = new[] { new CavePlatformBoundary.Loop { points = new[] { new Vector3(-8, 0, -8), new Vector3(8, 0, -8), new Vector3(8, 0, 8), new Vector3(-8, 0, 8) } } };
            cave.courts.Add(new CaveWorld.Court { name = go.name, center = go.transform.position, tile = go.AddComponent<CaveTile>() });
        }
        return cave;
    }
    static void VerifyLifecycle(CaveWorld cave, Scene scene, Action<bool, string> check)
    {
        var gate = cave.gameObject.AddComponent<RunWorldGate>();
        var map = new MapInstanceState { monsterThemeId = "CavernMutants" };
        MapGenerationPolicy.Initialize(map, "map-lifecycle-fixture");
        map.terrain = MapTerrainKind.Cave; map.areaCount = 4; map.width = map.depth = 0; map.desertCandidateId = null;
        var ground = cave.courts.Select((c, i) => new MapCombatGeometry.Area(i, c.name, c.center, i == 0 ? 0 : 2,
            Enumerable.Range(0, 6).Select(n => c.center + Vector3.left * (n + 1)).ToList())).ToArray();
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MapOverlayBuilder.MinimapPath));
        SceneManager.MoveGameObjectToScene(root, scene);
        var controller = root.GetComponent<WorldMinimapController>();
        void Set(string name, object value) => typeof(WorldMinimapController).GetField(name, Private).SetValue(controller, value);
        void Refresh() => typeof(WorldMinimapController).GetMethod("RefreshRunTerrain", Private).Invoke(controller, null);
        void Prepare(string id)
        { gate.BeginPreparation(new RunSnapshot { runId = id, phase = RunPhase.EntryPending, map = map }); }
        void Complete(string id)
        {
            var roster = MapRunRoster.Build(id, map, ground);
            typeof(MapRunRoster).GetProperty(nameof(MapRunRoster.PlacementReady)).SetValue(roster, true);
            if (!gate.SetRoster(roster) || !gate.CompletePreparation(id, cave.transform)) throw new InvalidOperationException("Fixture preparation failed");
        }
        try
        {
            Set("mapGate", gate); Set("caveWorld", cave); Set("contentScene", (int)scene.handle); Set("contentSceneInfo", scene);
            Prepare("same-run"); Refresh(); check(controller.Exploration == null, "Preparing terrain has no map");
            Complete("same-run"); Refresh(); var first = controller.Exploration; var texture = first.TerrainTexture;
            first.Reveal(new Vector3(-2, 0, 0), 12); int seen = first.RevealedCount, builds = controller.TerrainBuildCount;
            Refresh(); check(ReferenceEquals(first, controller.Exploration) && builds == controller.TerrainBuildCount && seen == first.RevealedCount,
                "Unchanged preparation keeps its texture and visit record");
            // The public prepare lifecycle changes context while all serialized identity fields remain identical.
            Prepare("same-run"); Refresh();
            check(controller.Exploration == null && !texture && !controller.View.TerrainImage.texture && controller.Guides.Count == 0,
                "Repreparing the same run/root/version releases old terrain, visits and guidance immediately");
            check(gate.RejectPreparation("same-run", "owned failure fixture"), "A failed preparation is accepted by the gate"); Refresh();
            check(controller.Exploration == null, "Failed preparation cannot display the previous terrain");
            Prepare("same-run"); Complete("same-run"); Refresh();
            check(controller.Exploration != null && !ReferenceEquals(first, controller.Exploration) && controller.Exploration.RevealedCount == 0
                && controller.TerrainBuildCount == builds + 1, "A completed replacement starts fresh once, even with the same run ID/root/version");
            var second = controller.Exploration;
            typeof(WorldMinimapController).GetMethod("BindTerrain", Private).Invoke(controller, null); Refresh();
            check(ReferenceEquals(second, controller.Exploration), "View rebinding preserves the same completed preparation");
            var previous = cave.generatedRoot;
            cave.generatedRoot = new GameObject("Replacement generated root").transform; cave.generatedRoot.SetParent(cave.transform, false);
            foreach (var court in cave.courts) court.tile.transform.SetParent(cave.generatedRoot, true);
            Object.DestroyImmediate(previous.gameObject); Physics.SyncTransforms(); Refresh();
            check(!ReferenceEquals(second, controller.Exploration) && controller.Exploration.RevealedCount == 0,
                "Replacing the generated root invalidates both map views");
            var finalTexture = controller.Exploration.TerrainTexture;
            // EditMode fixture components do not receive runtime MonoBehaviour destruction callbacks.
            typeof(WorldMinimapController).GetMethod("OnDestroy", Private).Invoke(controller, null);
            Object.DestroyImmediate(root); root = null;
            check(!finalTexture, "The controller destruction handler releases its final owned textures");
        }
        finally
        {
            if (root)
            {
                typeof(WorldMinimapController).GetMethod("OnDestroy", Private).Invoke(controller, null);
                Object.DestroyImmediate(root);
            }
        }
    }
}
