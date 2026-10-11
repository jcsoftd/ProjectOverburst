using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Caves;
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
        if (!gate.GetComponent<CaveWorld>())
        {
            check(controller && controller.Exploration == null, "Desert does not inherit cave exploration");
            yield break;
        }
        var map = controller ? controller.Exploration : null;
        check(map != null && controller.IsVisible, "Cave exploration is bound to the real minimap");
        check(map.IsRevealed(player.position) && map.RevealedCount > 0 && map.RevealedCount < map.Width * map.Height,
            "Entry reveals a local part of the cave");
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
        var cave = gate.GetComponent<CaveWorld>();
        var reachable = Reachable(map, map.CellAt(player.position));
        check(gate.Roster.Areas.All(a => reachable[map.CellAt(a.Ground.Points[0])]), "Actual generated bridges connect every combat platform in the map topology");
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
        using (var rebuilt = CaveMinimapTerrain.Build(cave))
        {
            long buildMilliseconds = buildWatch.ElapsedMilliseconds;
            var watch = System.Diagnostics.Stopwatch.StartNew(); rebuilt.EnsureTextures();
            foreach (var area in gate.Roster.Areas) rebuilt.Reveal(area.Ground.Points[0], 12);
            long milliseconds = watch.ElapsedMilliseconds;
            File.WriteAllText(Path.Combine(output, "minimap-cave-" + cave.courts.Count + ".json"), JsonConvert.SerializeObject(new {
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
    static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
}
