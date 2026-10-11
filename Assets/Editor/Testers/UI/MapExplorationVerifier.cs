using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Caves;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MapExplorationVerifier
{
    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        output = Path.GetFullPath(output);
        var allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Fresh evidence directory required.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        var scene = EditorSceneManager.NewPreviewScene();
        int before = OwnedTextures();
        try
        {
            Check(MapCombatGeometry.BodiesSeparated(Vector3.zero, .27f, Vector3.right * .5f, .18f), "Packed bodies at the exact 5cm gap survive float rounding");
            Check(!MapCombatGeometry.BodiesSeparated(Vector3.zero, .27f, Vector3.right * .499f, .18f), "Body spacing still rejects a genuinely smaller gap");
            using (var grid = Grid(80, 8))
            {
                grid.EnsureTextures();
                Check(grid.RevealedCount == 0 && grid.TerrainTexture.GetPixels32().All(p => p.a == 0), "New map is entirely hidden");
                Check(grid.Reveal(new Vector3(5.5f, 0, 3.5f), 2) && grid.RevealedCount == 13, "Reveal uses floor distance and a fixed radius");
                int uploads = grid.UploadCount;
                Check(!grid.Reveal(new Vector3(5.8f, 0, 3.6f), 2) && grid.UploadCount == uploads, "Stationary movement within a cell does not upload again");
                grid.Reveal(new Vector3(65.5f, 0, 3.5f), 2);
                Check(grid.IsRevealed(new Vector3(65.5f, 0, 3.5f)) && !grid.IsRevealed(new Vector3(35.5f, 0, 3.5f)), "Teleport reveals only its destination, not the skipped corridor");
                Check(!grid.Reveal(new Vector3(-5, 0, 0), 12), "Out-of-bounds positions cannot reveal the map");
            }
            var root = new GameObject("Minimap topology fixture");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var cave = root.AddComponent<CaveWorld>();
            AddPlatform(cave, new Vector3(0, 0, 0), Quaternion.identity, Vector3.one, 8, true);
            AddPlatform(cave, new Vector3(24, 0, 0), Quaternion.Euler(0, 90, 0), new Vector3(1, 1, 1.5f), 6, false);
            AddPlatform(cave, new Vector3(0, 0, 16), Quaternion.identity, Vector3.one, 8, false);
            cave.passages.Add(new CaveWorld.Passage { a = 0, b = 1,
                points = new[] { new Vector3(7, 0, 0), new Vector3(16, 0, 0) }, widths = new[] { 3f, 3f } });
            using (var map = CaveMinimapTerrain.Build(cave))
            {
                map.EnsureTextures();
                Check(!map.IsGround(map.CellAt(Vector3.zero)), "Authored interior hole remains empty");
                Check(map.IsGround(map.CellAt(new Vector3(12, 0, 1))) && !map.IsGround(map.CellAt(new Vector3(12, 0, 2))), "Bridge uses its actual width");
                Check(map.IsGround(map.CellAt(new Vector3(31, 0, 0))) && !map.IsGround(map.CellAt(new Vector3(24, 0, 7))), "Platform rotation and nonuniform scale are applied");
                map.Reveal(new Vector3(5, 0, 0), 100);
                Check(map.IsRevealed(new Vector3(24, 0, 0)), "Connected platform reveals through the bridge");
                Check(!map.IsRevealed(new Vector3(0, 0, 10)), "Touching but unconnected platforms do not reveal each other");
                Check(!map.IsRevealed(Vector3.zero), "Interior hole never becomes explored floor");
                Check(map.TerrainTexture.GetPixel(0, 0).a == 0, "Texture padding remains transparent outside the map");
            }
            for (int i = 0; i < 10; i++)
                using (var map = CaveMinimapTerrain.Build(cave)) { map.EnsureTextures(); map.Reveal(new Vector3(5, 0, 0), 12); }
            Check(OwnedTextures() == before, "Ten topology/texture lifecycles leave no owned textures");
            VerifyDesertTiles(scene, Check);
            string path = Path.Combine(output, "native-result.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = "PASS", checks, scope = "Editor native geometry, fog, texture lifecycle" }, Formatting.Indented));
            return path;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(output, "native-result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = e.ToString() }, Formatting.Indented));
            throw;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static int OwnedTextures() => Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t.name == "Explored terrain (run)" || t.name == "Explored outline (run)");
    static void VerifyDesertTiles(UnityEngine.SceneManagement.Scene scene, Action<bool, string> check)
    {
        var patch = ScriptableObject.CreateInstance<MojavePatch>(); patch.size = 16;
        var replacement = ScriptableObject.CreateInstance<MojavePatch>(); replacement.size = 16;
        var catalog = ScriptableObject.CreateInstance<MojaveCatalog>(); catalog.patches = new[] { patch };
        var terrain = new TerrainData { heightmapResolution = 33, size = new Vector3(256, 48, 256) };
        var root = Terrain.CreateTerrainGameObject(terrain); root.transform.position = new Vector3(-128, 0, -128);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var world = root.AddComponent<MojaveWorld>(); world.catalog = catalog; world.surface = root.GetComponent<Terrain>();
        world.seed = world.bakedSeed = 1; world.bakedLayoutVersion = MojaveLayout.Version;
        world.layout = new MojaveLayout(1, 6); world.layout.places.Clear(); world.layout.trails.Clear();
        var place = new MojavePlace { center = new Vector2(10, 20), radius = new Vector2(12, 5), kind = MojaveCombatKind.LongWash, patch = 0 };
        world.layout.places.Add(place);
        var data = ScriptableObject.CreateInstance<DesertMinimapData>(); Texture2D image = null;
        try
        {
            Rect initial = DesertMinimapData.SamplingBounds(world, 1); string signature = DesertMinimapData.LayoutSignature(world);
            Vector3 entry = DesertRunWorld.EntryFor(world);
            place.center += new Vector2(30, -10);
            Rect moved = DesertMinimapData.SamplingBounds(world, 1);
            check(moved.position == initial.position + new Vector2(30, -10) && moved.size == initial.size, "Desert bounds follow moved combat tiles without a fixed map origin");
            check(DesertMinimapData.LayoutSignature(world) != signature && DesertRunWorld.EntryFor(world) == entry + new Vector3(30, 0, -10), "Tile movement invalidates the bake and moves the entrance");
            place.rotation = 90;
            Rect rotated = DesertMinimapData.SamplingBounds(world, 1);
            check(Mathf.Abs(rotated.width - moved.height) <= 2 && Mathf.Abs(rotated.height - moved.width) <= 2, "Desert sampling bounds follow tile rotation");
            Vector2 localEntry = place.Local(new Vector2(DesertRunWorld.EntryFor(world).x, DesertRunWorld.EntryFor(world).z));
            check((localEntry - new Vector2(-2, -2)).sqrMagnitude < .0001f, "Entrance stays on the same tile-local anchor after rotation");
            place.radius *= 1.5f;
            check(DesertMinimapData.SamplingBounds(world, 1).height > rotated.height, "Larger combat tiles expand minimap bounds");
            var trail = new MojaveTrail { from = 0, to = 0, points = new[] { place.center, new Vector2(-80, 0) }, widths = new[] { 3f, 5f }, heights = new float[2] };
            world.layout.trails.Add(trail);
            check(DesertMinimapData.SamplingBounds(world, 1).xMin <= -86, "Actual connection bends and widths expand minimap bounds");
            var candidate = new MapTerrainCatalog.DesertCandidate { id = "fixture", scene = "fixture.unity", seed = 1, areaCount = 1, width = 256, depth = 256 };
            data.candidateId = candidate.id; data.sourceScene = candidate.scene; data.sourceDependencyHash = "fixture";
            data.version = DesertMinimapData.CurrentVersion; data.seed = 1; data.layoutVersion = MojaveLayout.Version; data.cellSize = 1;
            data.layoutSignature = DesertMinimapData.LayoutSignature(world); data.combatTiles = new[] { patch };
            data.bounds = DesertMinimapData.SamplingBounds(world, 1); data.width = (int)data.bounds.width; data.height = (int)data.bounds.height;
            data.cells = new byte[data.width * data.height]; data.connections = new byte[data.cells.Length];
            image = new Texture2D(data.width, data.height, TextureFormat.RGBA32, false); data.image = image;
            data.terrainPosition = root.transform.position; data.terrainSize = terrain.size;
            data.Validate(candidate, world); check(true, "Matching tile-derived metadata passes runtime validation");
            void Reject(string label, Action change, Action restore)
            {
                change(); bool rejected = false;
                try { data.Validate(candidate, world); } catch (InvalidOperationException) { rejected = true; }
                finally { restore(); }
                check(rejected, label);
            }
            Reject("Same candidate/seed/size with changed tile placement rejects stale bake", () => place.center += Vector2.one, () => place.center -= Vector2.one);
            Reject("Changed connection width rejects stale bake", () => trail.widths[0]++, () => trail.widths[0]--);
            Reject("Changed connection endpoint rejects stale bake", () => trail.to++, () => trail.to--);
            Reject("Replaced combat tile source rejects stale bake", () => catalog.patches[0] = replacement, () => catalog.patches[0] = patch);
            Reject("Wrong candidate rejects bake", () => data.candidateId = "other", () => data.candidateId = candidate.id);
            Reject("Wrong generator version rejects bake", () => data.layoutVersion++, () => data.layoutVersion--);
            Reject("Wrong bounds reject bake", () => data.bounds = new Rect(0, 0, data.bounds.width, data.bounds.height), () => data.bounds = DesertMinimapData.SamplingBounds(world, 1));
            Reject("Missing image rejects bake", () => data.image = null, () => data.image = image);
            data.Validate(candidate, world); check(true, "Validation fixtures restore original valid data");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(terrain); Object.DestroyImmediate(patch); Object.DestroyImmediate(replacement); Object.DestroyImmediate(catalog); Object.DestroyImmediate(data); if (image) Object.DestroyImmediate(image); }
    }
    static MinimapExplorationMap Grid(int width, int height)
    {
        var cells = Enumerable.Repeat(MinimapTerrainPixels.Ground, width * height).ToArray();
        var links = new byte[cells.Length];
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
        {
            int i = z * width + x;
            if (x > 0) links[i] |= MinimapExplorationMap.West;
            if (x + 1 < width) links[i] |= MinimapExplorationMap.East;
            if (z > 0) links[i] |= MinimapExplorationMap.South;
            if (z + 1 < height) links[i] |= MinimapExplorationMap.North;
        }
        return new MinimapExplorationMap(width, height, 1, Vector2.zero, cells, links);
    }
    static void AddPlatform(CaveWorld cave, Vector3 position, Quaternion rotation, Vector3 scale, float half, bool hole)
    {
        var root = new GameObject("Platform"); root.transform.SetParent(cave.transform, false);
        root.transform.SetPositionAndRotation(position, rotation); root.transform.localScale = scale;
        var boundary = root.AddComponent<CavePlatformBoundary>();
        CavePlatformBoundary.Loop Square(float r) => new CavePlatformBoundary.Loop { points = new[] {
            new Vector3(-r,0,-r), new Vector3(r,0,-r), new Vector3(r,0,r), new Vector3(-r,0,r) } };
        boundary.loops = hole ? new[] { Square(half), Square(2) } : new[] { Square(half) };
        cave.courts.Add(new CaveWorld.Court { center = position, tile = root.AddComponent<CaveTile>() });
    }

    public static IEnumerator VerifyPlay(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var controller = WorldMinimapController.Instance;
        var player = PlayerContext.Instance.CurrentActor.transform;
        yield return Frames(12);
        var cave = gate.GetComponent<CaveWorld>(); var desert = gate.GetComponent<DesertRunWorld>();
        string terrainLabel = cave ? "Cave" : "Desert";
        var map = controller ? controller.Exploration : null;
        check(map != null && controller.IsVisible, terrainLabel + " exploration is bound to the real minimap");
        if (desert) check(desert.MinimapData.candidateId == gate.Map.desertCandidateId && map.Bounds == desert.MinimapData.bounds,
            "Actual selected desert bake and tile-derived bounds are bound");
        check(map.IsRevealed(player.position) && map.RevealedCount > 0 && map.RevealedCount < map.Width * map.Height,
            "Entry reveals a local part of the " + terrainLabel);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "minimap-entry-" + (cave ? "cave-" + cave.courts.Count : desert.MinimapData.candidateId) + ".png"));
        yield return Frames(4);
        int builds = controller.TerrainBuildCount, uploads = map.UploadCount, seen = map.RevealedCount;
        var texture = map.TerrainTexture;
        foreach (float zoom in new[] { 20f, 80f, 55f }) { controller.SetZoom(zoom); yield return Frames(8); }
        check(ReferenceEquals(map, controller.Exploration) && builds == controller.TerrainBuildCount && map.RevealedCount == seen && map.UploadCount == uploads,
            "Zoom does not rebuild terrain or reveal extra floor");
        controller.ForceHide(); yield return Frames(3);
        controller.ShowForScene(player, gate.gameObject.scene.handle); yield return Frames(12);
        check(ReferenceEquals(map, controller.Exploration) && texture == controller.View.TerrainImage.texture && map.RevealedCount == seen,
            "Hide and reopen preserves the same terrain and visit bits");
        controller.enabled = false; yield return Frames(3); controller.enabled = true; yield return Frames(12);
        controller.ShowForScene(player, gate.gameObject.scene.handle); yield return Frames(12);
        check(ReferenceEquals(map, controller.Exploration) && controller.TerrainBuildCount == builds, "Disable/rebind and repeated ShowForScene preserve the run map");
        var reachable = Reachable(map, map.CellAt(player.position));
        check(gate.Roster.Areas.All(a => map.IsGround(map.CellAt(a.Ground.Points[0])) && reachable[map.CellAt(a.Ground.Points[0])]), "Actual generated connections reach every combat tile in the map topology");
        var far = gate.Roster.Areas.OrderByDescending(a => (a.Ground.Center - player.position).sqrMagnitude).First();
        Vector3 origin = player.position, destination = far.Ground.Points[0];
        check(!map.IsRevealed(destination), "Unvisited distant platform is hidden");
        ActorTeleportUtility.TeleportSafely(player, destination + Vector3.up * .12f, Quaternion.identity);
        yield return Frames(30);
        check(map.IsRevealed(player.position) && map.IsRevealed(origin), "Teleport reveals the destination and keeps previously explored floor");
        var ranks = new List<EnemyRank>(); EnemyRank.CollectActive(ranks);
        var byId = ranks.ToDictionary(r => r.GetInstanceID());
        var graphic = controller.View.Markers;
        for (int i = 0; i < graphic.MarkerCount; i++)
        {
            var marker = graphic.GetMarker(i);
            check(byId.TryGetValue(marker.Id, out var rank) && map.IsRevealed(rank.transform.position)
                && MinimapProjection.DistanceSquared(rank.transform.position, player.position) <= controller.ZoomRadius * controller.ZoomRadius,
                "Enemy marker is explored and inside the local detection radius: " + marker.Id);
        }
        var buildWatch = System.Diagnostics.Stopwatch.StartNew();
        using (var rebuilt = cave ? CaveMinimapTerrain.Build(cave) : desert.MinimapData.CreateExploration())
        {
            long buildMilliseconds = buildWatch.ElapsedMilliseconds;
            var watch = System.Diagnostics.Stopwatch.StartNew(); rebuilt.EnsureTextures();
            foreach (var area in gate.Roster.Areas) rebuilt.Reveal(area.Ground.Points[0], 12);
            long milliseconds = watch.ElapsedMilliseconds;
            File.WriteAllText(Path.Combine(output, "minimap-" + (cave ? "cave-" + cave.courts.Count : desert.MinimapData.candidateId) + ".json"), JsonConvert.SerializeObject(new {
                status = "PASS", map.Width, map.Height, bounds = new[] { map.Bounds.x, map.Bounds.y, map.Bounds.width, map.Bounds.height }, map.CellSize, map.RevealedCount, controller.TerrainBuildCount,
                localMarkerCount = graphic.MarkerCount, buildMilliseconds, sampleUploadMilliseconds = milliseconds,
                textureBytes = (long)map.Width * map.Height * 4 * 2, uploadCount = rebuilt.UploadCount,
                scope = "Editor Play projection, lifecycle and bounded CPU upload sample; not Player/GPU profiling" }, Formatting.Indented));
        }
        ActorTeleportUtility.TeleportSafely(player, origin, Quaternion.identity);
        yield return Frames(12);
    }
    static bool[] Reachable(MinimapExplorationMap map, int start)
    {
        var result = new bool[map.Width * map.Height];
        var queue = new Queue<int>(); queue.Enqueue(start); result[start] = true;
        while (queue.Count > 0)
        {
            int here = queue.Dequeue(); byte links = map.Connections(here);
            Add(here + 1, MinimapExplorationMap.East); Add(here - 1, MinimapExplorationMap.West);
            Add(here + map.Width, MinimapExplorationMap.North); Add(here - map.Width, MinimapExplorationMap.South);
            void Add(int next, byte flag)
            {
                if ((links & flag) == 0 || result[next]) return;
                result[next] = true; queue.Enqueue(next);
            }
        }
        return result;
    }
    public static void InspectPreparing(RunWorldGate gate, string output)
    {
        if (!gate || gate.Roster == null || !gate.SpawnBudget || !gate.EntryPoint) return;
        var rows = new List<object>();
        foreach (var field in gate.SpawnBudget.Fields.Where(f => f.PrepareAtEntry))
        foreach (var member in gate.Roster.Members.Where(m => m.AreaIndex == field.FiniteArea.Ground.Index && m.Initial && m.State == MapRunRoster.Life.Pending))
        {
            var body = MapCombatGeometry.BodyFor(member.Definition); var planned = member.PlannedPosition;
            var path = new UnityEngine.AI.NavMeshPath();
            bool runtimePoint = MapCombatGeometry.TryRuntimePoint(field.FiniteArea.Ground, member.Definition, planned, false, new Collider[32], path, out var actual);
            var nearest = new List<object>();
            foreach (var other in gate.Roster.Members.Where(m => m.State == MapRunRoster.Life.Active))
            foreach (var otherField in gate.SpawnBudget.Fields)
                if (otherField.TryGetActor(other.Index, out var actor))
                {
                    var delta = actor.transform.position - actual; delta.y = 0;
                    float margin = delta.magnitude - body.Radius - MapCombatGeometry.BodyFor(other.Definition).Radius - MapCombatGeometry.BodyGap;
                    if (margin < .5f) nearest.Add(new { other.Index, margin, position = new[] { actor.transform.position.x, actor.transform.position.y, actor.transform.position.z }, planned = new[] { other.PlannedPosition.x, other.PlannedPosition.y, other.PlannedPosition.z } });
                }
            var entry = planned - gate.EntryPoint.position; entry.y = 0;
            rows.Add(new { member.Index, enemy = member.Definition.EnemyId, body.Radius, entryDistance = entry.magnitude,
                planned = new[] { planned.x, planned.y, planned.z }, point = new[] { actual.x, actual.y, actual.z }, runtimePoint,
                canPlace = MapCombatGeometry.CanPlace(field.FiniteArea.Ground, member.Definition, planned, true), nearest });
        }
        if (rows.Count > 0) File.WriteAllText(Path.Combine(output, "preparing-snapshot.json"), JsonConvert.SerializeObject(new { stage = gate.PreparationStage, gate.SpawnBudget.SpawnRetries, rows }, Formatting.Indented));
    }
    static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
}
