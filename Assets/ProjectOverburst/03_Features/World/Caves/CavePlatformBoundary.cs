using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Caves
{
    [DisallowMultipleComponent]
    public sealed class CavePlatformBoundary : MonoBehaviour
    {
        [Serializable] public sealed class Loop { public Vector3[] points; }
        [Tooltip("Generated copies read the boundary saved on the edited source prefab.")]
        public CavePlatformBoundary definition;
        public bool excludeMonsters;
        public Loop[] loops = Array.Empty<Loop>();
        [HideInInspector] public Vector3[] floorTriangles = Array.Empty<Vector3>();
        [HideInInspector] public CavePlatformLibrary.Region[] ports = Array.Empty<CavePlatformLibrary.Region>();
        public CavePlatformBoundary Data => definition ? definition : this;
        QueryCache cache;
        static readonly Unity.Profiling.ProfilerMarker SampleMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.Sample");
        static readonly Unity.Profiling.ProfilerMarker HeightMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.HeightQuery");
        static readonly Unity.Profiling.ProfilerMarker CacheBuildMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.CacheBuild");
        static readonly Unity.Profiling.ProfilerMarker RimHeightMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.RimHeightFallback");
        void OnValidate() => cache = null;
        public void InvalidateCache() => cache = null;

        public bool ContainsLocal(Vector3 p)
        {
            var data = Data;
            if (data.cache == null) data.cache = new QueryCache(data);
            int index = data.cache.Index(p.x, p.z);
            if (index < 0) return false;
            byte cell = data.cache.coverage[index];
            return cell == 1 || (cell == 2 && data.cache.ContainsExact(p));
        }

        bool ContainsExact(Vector3 p)
        {
            bool inside = false;
            foreach (var loop in loops)
            {
                var v = loop.points;
                for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                    if ((v[i].z > p.z) != (v[j].z > p.z)
                        && p.x < (v[j].x - v[i].x) * (p.z - v[i].z) / (v[j].z - v[i].z) + v[i].x) inside = !inside;
            }
            return inside;
        }

        public bool Sample(Vector3 world, float up, float down, out Vector3 ground)
        {
            using var sample = SampleMarker.Auto();
            var p = transform.InverseTransformPoint(world); ground = world;
            var data = Data;
            if (data.cache == null) data.cache = new QueryCache(data);
            if (!data.cache.TryHeight(p, out float height, out bool found))
            {
                found = SampleHeight(p, data, out height);
                data.cache.StoreHeight(p, height, found);
            }
            if (!found) return false;
            ground = transform.TransformPoint(new Vector3(p.x, height, p.z));
            return ground.y <= world.y + up && ground.y >= world.y - down;
        }

        bool SampleHeight(Vector3 p, CavePlatformBoundary data, out float height)
        {
            using var sample = HeightMarker.Auto();
            height = 0;
            if (!ContainsLocal(p)) return false;
            float best = float.MaxValue;
            var triangles = data.floorTriangles;
            var candidates = data.cache.triangles[data.cache.Index(p.x, p.z)];
            if (candidates != null) foreach (int i in candidates)
            {
                var a = triangles[i]; var b = triangles[i + 1]; var c = triangles[i + 2];
                if (p.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || p.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x))
                    || p.z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) || p.z > Mathf.Max(a.z, Mathf.Max(b.z, c.z))) continue;
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < .00001f) continue;
                float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / det;
                float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / det;
                if (u < -.001f || v < -.001f || u + v > 1.001f) continue;
                float y = u * a.y + v * b.y + (1 - u - v) * c.y;
                if (Mathf.Abs(y - p.y) < best) { best = Mathf.Abs(y - p.y); height = y; }
            }
            // A hand-adjusted outline can bridge small mesh cracks; use the closest rim height there.
            if (best == float.MaxValue)
            using (RimHeightMarker.Auto())
                foreach (int i in data.cache.rims[data.cache.Index(p.x, p.z)])
                    {
                        var a = data.cache.edges[i].a; var b = data.cache.edges[i].b;
                        var d = new Vector2(b.x - a.x, b.z - a.z);
                        float t = Mathf.Clamp01(Vector2.Dot(new Vector2(p.x - a.x, p.z - a.z), d) / Mathf.Max(.00001f, d.sqrMagnitude));
                        var closest = Vector3.Lerp(a, b, t);
                        float distance = new Vector2(p.x - closest.x, p.z - closest.z).sqrMagnitude;
                        if (distance < best) { best = distance; height = closest.y; }
                    }
            return best < float.MaxValue;
        }

        sealed class QueryCache
        {
            const float Cell = 2;
            readonly int minX, minZ, width, depth;
            public readonly byte[] coverage;
            public readonly List<int>[] triangles;
            public readonly (Vector3 a, Vector3 b)[] edges;
            public readonly int[][] rims;
            struct HeightSample { public Vector3 point; public float height; public byte state; }
            readonly HeightSample[] recent = new HeightSample[128];
            static int HeightIndex(Vector3 p)
            {
                int hash = p.GetHashCode(); hash ^= hash >> 16; hash ^= hash >> 8;
                return hash & 127;
            }
            public bool TryHeight(Vector3 p, out float height, out bool found)
            {
                var entry = recent[HeightIndex(p)]; height = entry.height; found = entry.state == 2;
                return entry.state != 0 && entry.point.Equals(p);
            }
            public void StoreHeight(Vector3 p, float height, bool found)
            {
                recent[HeightIndex(p)] = new HeightSample { point = p, height = height, state = found ? (byte)2 : (byte)1 };
            }
            static float EdgeDistance(Vector3 p, (Vector3 a, Vector3 b) edge)
            {
                var d = new Vector2(edge.b.x - edge.a.x, edge.b.z - edge.a.z);
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(p.x - edge.a.x, p.z - edge.a.z), d) / Mathf.Max(.00001f, d.sqrMagnitude));
                var closest = Vector3.Lerp(edge.a, edge.b, t);
                return new Vector2(p.x - closest.x, p.z - closest.z).sqrMagnitude;
            }
            readonly List<(Vector3 a, Vector3 b)>[] crossingRows;
            public bool ContainsExact(Vector3 p)
            {
                int row = Mathf.FloorToInt(p.z / Cell) - minZ;
                if (row < 0 || row >= depth || crossingRows[row] == null) return false;
                bool inside = false;
                foreach (var edge in crossingRows[row])
                {
                    var a = edge.a; var b = edge.b;
                    if ((a.z > p.z) != (b.z > p.z)
                        && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
                }
                return inside;
            }
            public int Index(float x, float z)
            {
                int ix = Mathf.FloorToInt(x / Cell) - minX, iz = Mathf.FloorToInt(z / Cell) - minZ;
                return ix < 0 || iz < 0 || ix >= width || iz >= depth ? -1 : iz * width + ix;
            }
            public QueryCache(CavePlatformBoundary source)
            {
                using var sample = CacheBuildMarker.Auto();
                var bounds = new Bounds(Vector3.zero, Vector3.zero); bool first = true;
                foreach (var loop in source.loops) foreach (var p in loop.points)
                { if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p); }
                minX = Mathf.FloorToInt(bounds.min.x / Cell); minZ = Mathf.FloorToInt(bounds.min.z / Cell);
                width = Mathf.FloorToInt(bounds.max.x / Cell) - minX + 1; depth = Mathf.FloorToInt(bounds.max.z / Cell) - minZ + 1;
                coverage = new byte[width * depth]; triangles = new List<int>[coverage.Length];
                crossingRows = new List<(Vector3, Vector3)>[depth];
                // A horizontal ray can only cross edges sharing its Z row. Preserve exact parity and edge arithmetic.
                foreach (var loop in source.loops)
                for (int i = 0, j = loop.points.Length - 1; i < loop.points.Length; j = i++)
                {
                    var a = loop.points[i]; var b = loop.points[j];
                    if (a.z == b.z) continue;
                    int low = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.z, b.z) / Cell) - minZ, 0, depth - 1);
                    int high = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.z, b.z) / Cell) - minZ, 0, depth - 1);
                    for (int row = low; row <= high; row++)
                    {
                        if (crossingRows[row] == null) crossingRows[row] = new List<(Vector3, Vector3)>();
                        crossingRows[row].Add((a, b));
                    }
                }
                for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
                    coverage[z * width + x] = source.ContainsExact(new Vector3((minX + x + .5f) * Cell, 0, (minZ + z + .5f) * Cell)) ? (byte)1 : (byte)0;
                void Visit(float x0, float z0, float x1, float z1, Action<int> action)
                {
                    int left = Mathf.Clamp(Mathf.FloorToInt(x0 / Cell) - minX, 0, width - 1), right = Mathf.Clamp(Mathf.FloorToInt(x1 / Cell) - minX, 0, width - 1);
                    int bottom = Mathf.Clamp(Mathf.FloorToInt(z0 / Cell) - minZ, 0, depth - 1), top = Mathf.Clamp(Mathf.FloorToInt(z1 / Cell) - minZ, 0, depth - 1);
                    for (int z = bottom; z <= top; z++) for (int x = left; x <= right; x++) action(z * width + x);
                }
                foreach (var loop in source.loops) for (int i = 0; i < loop.points.Length; i++)
                {
                    var a = loop.points[i]; var b = loop.points[(i + 1) % loop.points.Length];
                    Visit(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z), index => coverage[index] = 2);
                }
                var t = source.floorTriangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var a = t[i]; var b = t[i + 1]; var c = t[i + 2]; int triangle = i;
                    Visit(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), Mathf.Min(a.z, Mathf.Min(b.z, c.z)), Mathf.Max(a.x, Mathf.Max(b.x, c.x)), Mathf.Max(a.z, Mathf.Max(b.z, c.z)), index =>
                    { if (triangles[index] == null) triangles[index] = new List<int>(); triangles[index].Add(triangle); });
                }
                var edgeList = new List<(Vector3, Vector3)>();
                foreach (var loop in source.loops) for (int i = 0; i < loop.points.Length; i++)
                    edgeList.Add((loop.points[i], loop.points[(i + 1) % loop.points.Length]));
                edges = edgeList.ToArray(); rims = new int[coverage.Length][];
                var distances = new float[edges.Length]; var selected = new List<int>();
                for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
                {
                    int index = z * width + x;
                    if (coverage[index] == 0) continue;
                    var centre = new Vector3((minX + x + .5f) * Cell, 0, (minZ + z + .5f) * Cell);
                    float nearest = float.MaxValue;
                    for (int i = 0; i < edges.Length; i++) { distances[i] = EdgeDistance(centre, edges[i]); nearest = Mathf.Min(nearest, distances[i]); }
                    // Across one cell, a winning edge's centre distance is at most nearest + two half-diagonals.
                    // The extra centimetre only widens candidates; final distances and tie order stay exact.
                    float limit = Mathf.Sqrt(nearest) + Cell * 1.41421357f + .01f;
                    selected.Clear();
                    for (int i = 0; i < edges.Length; i++) if (distances[i] <= limit * limit) selected.Add(i);
                    rims[index] = selected.ToArray();
                }
            }
        }
    }
}
