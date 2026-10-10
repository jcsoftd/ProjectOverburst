using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overburst.Caves;

public static class CaveGroundHeightVerifier
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static float Clearance(Vector2 p, Vector2[] hull)
    {
        bool inside = true; float distance = float.MaxValue;
        for (int i = 0; i < hull.Length; i++)
        {
            var a = hull[i]; var b = hull[(i + 1) % hull.Length]; var d = b - a;
            inside &= Cross(d, p - a) >= 0;
            distance = Mathf.Min(distance, Vector2.Distance(p, a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(.0001f, d.sqrMagnitude))));
        }
        return inside ? -distance : distance;
    }
    // Frozen exhaustive reference: retain the pre-optimization equations and per-cell order.
    static void Reference(float[,] heights, Vector3 origin, Vector3 size, Bounds[] supports,
        Vector2[][] hulls, CaveWorld.Court[] courts, CaveWorld.Passage[] passages)
    {
        int n = heights.GetLength(0);
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            var p = new Vector2(origin.x + x * size.x / (n - 1), origin.z + z * size.z / (n - 1));
            float h = origin.y + heights[z, x] * size.y;
            foreach (var b in supports)
            {
                float dx = Mathf.Max(0, Mathf.Abs(p.x - b.center.x) - b.extents.x);
                float dz = Mathf.Max(0, Mathf.Abs(p.y - b.center.z) - b.extents.z);
                float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.Sqrt(dx * dx + dz * dz) / 9);
                h = Mathf.Max(h, Mathf.Lerp(h, b.min.y + .35f, weight));
            }
            for (int i = 0; i < hulls.Length; i++)
            {
                float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.Max(0, Clearance(p, hulls[i])) / 12);
                h = Mathf.Min(h, Mathf.Lerp(h, courts[i].center.y - 3.5f, weight));
            }
            foreach (var passage in passages) for (int i = 1; i < passage.points.Length; i++)
            {
                var a = XZ(passage.points[i - 1]); var b = XZ(passage.points[i]); var delta = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
                float distance = Vector2.Distance(p, a + delta * t);
                float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.Max(0, distance - passage.widths[Mathf.Min(i, passage.widths.Length - 1)] * .5f) / 8);
                h = Mathf.Min(h, Mathf.Lerp(h, Mathf.Lerp(passage.points[i - 1].y, passage.points[i].y, t) - 3.5f, weight));
            }
            heights[z, x] = Mathf.Clamp01((h - origin.y) / size.y);
        }
    }
    public static object VerifyFixtures()
    {
        int samples = 0;
        for (int fixture = 0; fixture < 16; fixture++)
        {
            var random = new System.Random(500 + fixture);
            float Next(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            int n = fixture % 2 == 0 ? 33 : 65;
            var origin = new Vector3(Next(-35, 10), Next(-15, 0), Next(-25, 15));
            var size = new Vector3(Next(20, 110), Next(30, 200), Next(15, 90));
            var heights = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) heights[z, x] = Next(0, 1);
            Vector3 Position() => origin + new Vector3(Next(-20, size.x + 20), Next(0, size.y), Next(-20, size.z + 20));
            var supports = Enumerable.Range(0, 5).Select(_ => new Bounds(Position(), new Vector3(Next(0, 25), Next(0, 15), Next(0, 25)))).ToArray();
            var hulls = Enumerable.Range(0, 3).Select(_ =>
            {
                var center = XZ(Position()); float r = Next(2, 15), angle = Next(0, 6);
                return Enumerable.Range(0, 5).Select(i => center + new Vector2(Mathf.Cos(angle + i * Mathf.PI * .4f), Mathf.Sin(angle + i * Mathf.PI * .4f)) * r).ToArray();
            }).ToArray();
            var courts = hulls.Select(_ => new CaveWorld.Court { center = Position() }).ToArray();
            var point = Position();
            var passages = new[] {
                new CaveWorld.Passage {points = new[] {Position(), Position(), Position()}, widths = new[] {1f, 3f, 7f}},
                new CaveWorld.Passage {points = new[] {point, point, Position()}, widths = new[] {2f}}
            };
            if (fixture == 0) { supports = Array.Empty<Bounds>(); hulls = Array.Empty<Vector2[]>(); courts = Array.Empty<CaveWorld.Court>(); passages = Array.Empty<CaveWorld.Passage>(); }
            // Exercise exact influence boundaries and features wholly outside the terrain.
            if (fixture == 1) supports = new[] {new Bounds(origin + new Vector3(9, 20, 9), Vector3.zero), new Bounds(origin + Vector3.one * 1000, Vector3.one)};
            var expected = (float[,])heights.Clone();
            Reference(expected, origin, size, supports, hulls, courts, passages);
            CaveDressingBuilder.FitGroundHeights(heights, origin, size, supports, hulls, courts, passages);
            Require(expected.Cast<float>().SequenceEqual(heights.Cast<float>()), "Height mismatch in fixture " + fixture);
            samples += n * n;
        }
        return new {status = "PASS", fixtures = 16, samples, exact = true, includes = "Overlapping features, translated rectangular terrain, influence boundaries, outside features, variable widths, slopes, zero-length segment and empty inputs"};
    }
    static string Key(Transform t) => (t.parent ? Key(t.parent) + "/" : "") + t.name + "[" + t.GetSiblingIndex() + "]";
    static Dictionary<string, Transform> Objects(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToDictionary(Key);
    public static object VerifyMaps(string beforePath, string afterPath)
    {
        CavePlatformMapBuilder.Guard(); var active = SceneManager.GetActiveScene(); bool dirty = active.isDirty;
        Scene before = default, after = default;
        try
        {
            before = EditorSceneManager.OpenPreviewScene(beforePath); after = EditorSceneManager.OpenPreviewScene(afterPath);
            var a = Objects(before); var b = Objects(after); Require(a.Keys.OrderBy(k => k).SequenceEqual(b.Keys.OrderBy(k => k)), "Hierarchy changed.");
            int generatedMeshes = 0;
            foreach (var pair in a)
            {
                var x = pair.Value; var y = b[pair.Key];
                Require(x.localPosition.Equals(y.localPosition) && x.localRotation.Equals(y.localRotation) && x.localScale.Equals(y.localScale) && x.gameObject.activeSelf == y.gameObject.activeSelf, "Transform changed: " + pair.Key);
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(y.gameObject) == 0, "Missing script: " + pair.Key);
                var mx = x.GetComponent<MeshFilter>(); var my = y.GetComponent<MeshFilter>();
                if (mx && mx.sharedMesh != my.sharedMesh)
                {
                    Require(pair.Key.Contains(CaveDetailDressingBuilder.RootName), "Source mesh changed: " + pair.Key);
                    Require(mx.sharedMesh.vertices.SequenceEqual(my.sharedMesh.vertices) && mx.sharedMesh.triangles.SequenceEqual(my.sharedMesh.triangles) && mx.sharedMesh.uv.SequenceEqual(my.sharedMesh.uv) && mx.sharedMesh.normals.SequenceEqual(my.sharedMesh.normals) && mx.sharedMesh.tangents.SequenceEqual(my.sharedMesh.tangents), "Generated mesh changed: " + pair.Key); generatedMeshes++;
                }
                var rx = x.GetComponent<Renderer>(); var ry = y.GetComponent<Renderer>();
                if (rx) Require(rx.enabled == ry.enabled && rx.sharedMaterials.SequenceEqual(ry.sharedMaterials), "Rendering changed: " + pair.Key);
                Require(x.GetComponents<Collider>().Select(c => (c.GetType(), c.enabled, c.isTrigger)).SequenceEqual(y.GetComponents<Collider>().Select(c => (c.GetType(), c.enabled, c.isTrigger))), "Collider changed: " + pair.Key);
                var px = x.GetComponent<ParticleSystem>(); var py = y.GetComponent<ParticleSystem>();
                if (px) Require(px.randomSeed == py.randomSeed && px.main.startColor.color.Equals(py.main.startColor.color) && px.main.maxParticles == py.main.maxParticles, "Particle setup changed: " + pair.Key);
            }
            var ta = a.Values.Select(t => t.GetComponent<Terrain>()).Single(t => t); var tb = b.Values.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            var da = ta.terrainData; var db = tb.terrainData; int n = da.heightmapResolution;
            Require(n == db.heightmapResolution && da.size.Equals(db.size), "Terrain dimensions changed.");
            Require(da.GetHeights(0, 0, n, n).Cast<float>().SequenceEqual(db.GetHeights(0, 0, n, n).Cast<float>()), "Saved terrain height changed.");
            Require(da.terrainLayers.SequenceEqual(db.terrainLayers) && ta.materialTemplate == tb.materialTemplate, "Terrain material changed.");
            Require(da.alphamapWidth == db.alphamapWidth && da.alphamapHeight == db.alphamapHeight && da.GetAlphamaps(0, 0, da.alphamapWidth, da.alphamapHeight).Cast<float>().SequenceEqual(db.GetAlphamaps(0, 0, db.alphamapWidth, db.alphamapHeight).Cast<float>()), "Terrain painting changed.");
            return new {status = "PASS", beforePath, afterPath, objects = a.Count, exactHeightSamples = n * n, generatedMeshes, terrainPainting = "identical", scope = "Native saved scene geometry, transforms, materials, colliders and decoration parity; not character traversal"};
        }
        finally
        {
            if (after.IsValid()) EditorSceneManager.ClosePreviewScene(after);
            if (before.IsValid()) EditorSceneManager.ClosePreviewScene(before);
            Require(SceneManager.GetActiveScene() == active && active.isDirty == dirty, "Open scene changed.");
        }
    }
}
