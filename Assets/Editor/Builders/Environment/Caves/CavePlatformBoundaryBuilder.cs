using System;
using System.Collections.Generic;
using System.Linq;
using Overburst.Caves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CavePlatformBoundaryBuilder
{
    const float Cell = .35f;
    public static object DraftMissing()
    {
        CavePlatformMapBuilder.Guard();
        var stage = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath;
        if (stage != null) StageUtility.GoToMainStage();
        var library = AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(CavePlatformMapBuilder.LibraryPath);
        var results = new List<object>();
        try
        {
            foreach (var platform in library.platforms)
            {
                string sourcePath = AssetDatabase.GetAssetPath(platform.source);
                var root = PrefabUtility.LoadPrefabContents(sourcePath);
                try
                {
                    var boundary = root.GetComponent<CavePlatformBoundary>();
                    if (!boundary)
                    {
                        boundary = root.AddComponent<CavePlatformBoundary>();
                        boundary.loops = Trace(platform);
                        boundary.floorTriangles = platform.surfaceTriangles;
                        boundary.ports = platform.regions;
                        if (boundary.loops.Length == 0 || !boundary.ContainsLocal(platform.spawn)) throw new InvalidOperationException("Invalid draft: " + root.name);
                        PrefabUtility.SaveAsPrefabAsset(root, sourcePath);
                    }
                    results.Add(new { name = root.name, loops = boundary.loops.Length, vertices = boundary.loops.Sum(l => l.points.Length) });
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath).GetComponent<CavePlatformBoundary>();
                string runtimePath = AssetDatabase.GetAssetPath(platform.prefab);
                var runtime = PrefabUtility.LoadPrefabContents(runtimePath);
                try
                {
                    var boundary = runtime.GetComponent<CavePlatformBoundary>() ?? runtime.AddComponent<CavePlatformBoundary>();
                    boundary.definition = source;
                    boundary.loops = Array.Empty<CavePlatformBoundary.Loop>();
                    boundary.floorTriangles = Array.Empty<Vector3>();
                    boundary.ports = Array.Empty<CavePlatformLibrary.Region>();
                    PrefabUtility.SaveAsPrefabAsset(runtime, runtimePath);
                }
                finally { PrefabUtility.UnloadPrefabContents(runtime); }
            }
        }
        finally { if (stage != null) PrefabStageUtility.OpenPrefab(stage); }
        return results;
    }

    static CavePlatformBoundary.Loop[] Trace(CavePlatformLibrary.Platform platform)
    {
        var vertices = platform.surfaceTriangles;
        int minX = Mathf.FloorToInt(vertices.Min(v => v.x) / Cell) - 2, maxX = Mathf.CeilToInt(vertices.Max(v => v.x) / Cell) + 2;
        int minZ = Mathf.FloorToInt(vertices.Min(v => v.z) / Cell) - 2, maxZ = Mathf.CeilToInt(vertices.Max(v => v.z) / Cell) + 2;
        var filled = new HashSet<Vector2Int>();
        for (int z = minZ; z < maxZ; z++)
            for (int x = minX; x < maxX; x++)
                if (platform.Surface(new Vector3((x + .5f) * Cell, 0, (z + .5f) * Cell), out _)) filled.Add(new Vector2Int(x, z));
        var edges = new Dictionary<Vector2Int, List<Vector2Int>>();
        void Edge(Vector2Int a, Vector2Int b) { if (!edges.TryGetValue(a, out var list)) edges[a] = list = new List<Vector2Int>(); list.Add(b); }
        foreach (var c in filled.OrderBy(p => p.y).ThenBy(p => p.x))
        {
            if (!filled.Contains(c + Vector2Int.down)) Edge(c, c + Vector2Int.right);
            if (!filled.Contains(c + Vector2Int.right)) Edge(c + Vector2Int.right, c + Vector2Int.one);
            if (!filled.Contains(c + Vector2Int.up)) Edge(c + Vector2Int.one, c + Vector2Int.up);
            if (!filled.Contains(c + Vector2Int.left)) Edge(c + Vector2Int.up, c);
        }
        var loops = new List<CavePlatformBoundary.Loop>();
        while (edges.Count > 0)
        {
            var first = edges.Keys.OrderBy(p => p.y).ThenBy(p => p.x).First();
            var cursor = first; var polygon = new List<Vector3>(); var incoming = Vector2Int.right;
            do
            {
                var point = new Vector3(cursor.x * Cell, 0, cursor.y * Cell);
                if (!platform.Surface(point, out var floor))
                {
                    floor = vertices.OrderBy(p => new Vector2(p.x - point.x, p.z - point.z).sqrMagnitude).First();
                }
                point.y = floor.y; polygon.Add(point);
                if (!edges.TryGetValue(cursor, out var choices)) throw new InvalidOperationException("Open platform contour");
                var next = choices.OrderByDescending(p => Mathf.Atan2(incoming.x * (p.y - cursor.y) - incoming.y * (p.x - cursor.x), Vector2.Dot(incoming, p - cursor))).First();
                choices.Remove(next); if (choices.Count == 0) edges.Remove(cursor);
                incoming = next - cursor; cursor = next;
            } while (cursor != first);
            float area = 0;
            for (int i = 0; i < polygon.Count; i++) { var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; area += a.x * b.z - b.x * a.z; }
            if (Mathf.Abs(area) < 1.5f) continue;
            int split = polygon.Count / 2;
            var firstHalf = Simplify(polygon.Take(split + 1).ToList());
            var secondHalf = Simplify(polygon.Skip(split).Concat(new[] { polygon[0] }).ToList());
            loops.Add(new CavePlatformBoundary.Loop { points = firstHalf.Take(firstHalf.Count - 1).Concat(secondHalf.Take(secondHalf.Count - 1)).ToArray() });
        }
        return loops.ToArray();
    }

    static List<Vector3> Simplify(List<Vector3> p)
    {
        if (p.Count <= 2) return p;
        var a = new Vector2(p[0].x, p[0].z); var b = new Vector2(p[p.Count - 1].x, p[p.Count - 1].z); var d = b - a;
        float maximum = 0; int split = 0;
        for (int i = 1; i < p.Count - 1; i++)
        {
            var v = new Vector2(p[i].x, p[i].z);
            float distance = Vector2.Distance(v, a + d * Mathf.Clamp01(Vector2.Dot(v - a, d) / Mathf.Max(d.sqrMagnitude, .00001f)));
            if (distance > maximum) { maximum = distance; split = i; }
        }
        if (maximum <= .22f) return new List<Vector3> { p[0], p[p.Count - 1] };
        var left = Simplify(p.Take(split + 1).ToList()); left.RemoveAt(left.Count - 1);
        left.AddRange(Simplify(p.Skip(split).ToList())); return left;
    }

    public static object ConnectExisting(string path)
    {
        CavePlatformMapBuilder.Guard();
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            var world = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CaveWorld>()).Single();
            var connectors = world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>();
            foreach (var connection in connectors)
            {
                var passage = world.passages.OrderBy(p => Vector3.SqrMagnitude(p.points[0] - connection.start) + Vector3.SqrMagnitude(p.points[p.points.Length - 1] - connection.end)).First();
                if (Vector3.Distance(passage.points[0], connection.start) > 3) throw new InvalidOperationException("Unmatched connector: " + connection.name);
                connection.boundaryA = world.courts[passage.a].tile.GetComponent<CavePlatformBoundary>();
                connection.boundaryB = world.courts[passage.b].tile.GetComponent<CavePlatformBoundary>();
                if (!connection.boundaryA || !connection.boundaryB) throw new InvalidOperationException("Missing platform boundary");
                connection.walkWidth = passage.widths.Min();
                connection.portA = ClosestPort(connection.boundaryA, connection.start, connection.stairCount > 0);
                connection.portB = ClosestPort(connection.boundaryB, connection.end, connection.stairCount > 0);
            }
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed: " + path);
            return new { path, platforms = world.generatedRoot.GetComponentsInChildren<CavePlatformBoundary>().Length, connections = connectors.Length };
        }
        finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
    }

    static int ClosestPort(CavePlatformBoundary boundary, Vector3 worldPoint, bool stairs)
    {
        var local = boundary.transform.InverseTransformPoint(worldPoint); var ports = boundary.Data.ports;
        return Enumerable.Range(0, ports.Length).Where(i => ports[i].stone == stairs)
            .OrderBy(i => Vector3.SqrMagnitude(ports[i].Anchor(ports[i].heading) - local)).First();
    }
}
