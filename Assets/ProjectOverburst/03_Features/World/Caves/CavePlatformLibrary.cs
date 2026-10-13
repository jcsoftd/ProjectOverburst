using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Caves
{
    [CreateAssetMenu(menuName = "Overburst/Caves/Authored platform library")]
    public sealed class CavePlatformLibrary : ScriptableObject
    {
        [Serializable] public sealed class SpawnBody
        {
            public float radius, height;
            public EnemyDefinition[] definitions;
        }
        [Serializable] public struct SpawnPoint
        {
            public Vector3 position;
            // Bit i refers to spawnBodies[i]. Coordinates belong to the platform prefab root.
            public long bodyMask;
        }
        [Serializable] public sealed class Region
        {
            public string name;
            public bool stone;
            public Vector3[] anchors;
            public float[] angles;
            public GameObject template;
            public Vector3 pivot;
            public float heading, halfAngle, radius, guideFitError;
            public float pitch, pitchTolerance = 5, width, deckLength;
            public int authoredStairCount = 1, maxStairCount = 1;
            public int stairDirection = -1;
            public float MinAngle => heading - halfAngle;
            public float MaxAngle => heading + halfAngle;

            public Vector3 Anchor(float angle)
            {
                return pivot + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
            }
        }
        [Serializable] public sealed class Connector
        {
            public GameObject prefab;
            // Contact points in prefab-local coordinates. Meshes and source scale stay unchanged.
            public Vector3 entry, exit;
            public float width;
            public Vector3 ScaledEntry => Vector3.Scale(entry, prefab.transform.localScale);
            public Vector3 ScaledExit => Vector3.Scale(exit, prefab.transform.localScale);
            public Vector3 Span => ScaledExit - ScaledEntry;
        }
        [Serializable] public sealed class Platform
        {
            public GameObject source;
            public GameObject prefab;
            public Vector2[] hull;
            public Vector3[] surfaceTriangles;
            public Vector3 spawn;
            public Region[] regions;
            public Bounds[] obstacles;
            [HideInInspector] public SpawnPoint[] spawnPoints = Array.Empty<SpawnPoint>();
            [NonSerialized] Vector3[] indexedTriangles;
            [NonSerialized] Dictionary<Vector2Int, List<int>> surfaceCells;
            const float SurfaceCellSize = 2f;

            public void InvalidateSurfaceCache() { indexedTriangles = null; surfaceCells = null; }
            static Vector2Int SurfaceCell(Vector3 point) => new Vector2Int(
                Mathf.FloorToInt(point.x / SurfaceCellSize), Mathf.FloorToInt(point.z / SurfaceCellSize));
            void EnsureSurfaceCache()
            {
                if (ReferenceEquals(indexedTriangles, surfaceTriangles) && surfaceCells != null) return;
                indexedTriangles = surfaceTriangles;
                surfaceCells = new Dictionary<Vector2Int, List<int>>();
                for (int i = 0; i < surfaceTriangles.Length; i += 3)
                {
                    var a = SurfaceCell(surfaceTriangles[i]); var b = SurfaceCell(surfaceTriangles[i + 1]); var c = SurfaceCell(surfaceTriangles[i + 2]);
                    for (int z = Mathf.Min(a.y, Mathf.Min(b.y, c.y)); z <= Mathf.Max(a.y, Mathf.Max(b.y, c.y)); z++)
                        for (int x = Mathf.Min(a.x, Mathf.Min(b.x, c.x)); x <= Mathf.Max(a.x, Mathf.Max(b.x, c.x)); x++)
                        {
                            var cell = new Vector2Int(x, z);
                            if (!surfaceCells.TryGetValue(cell, out var triangles)) surfaceCells.Add(cell, triangles = new List<int>());
                            triangles.Add(i);
                        }
                }
            }

            public Vector3 Anchor(Region region, float angle)
            {
                if (TryAnchor(region, angle, out var point)) return point;
                throw new InvalidOperationException(prefab.name + ": connection angle has no safe floor contact.");
            }

            public bool TryAnchor(Region region, float angle, out Vector3 point)
            {
                point = default;
                if (angle < region.MinAngle - .01f || angle > region.MaxAngle + .01f) return false;
                var direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0, Mathf.Sin(angle * Mathf.Deg2Rad));
                float last = -1; bool found = false;
                // The guides describe a fan, so every candidate lies on a ray from its common pivot.
                for (float distance = Mathf.Max(0, region.radius - 3); distance <= region.radius + 4; distance += .2f)
                {
                    if (Surface(region.pivot + direction * distance, out _)) { last = distance; found = true; }
                    else if (found) break;
                }
                if (last < .8f || !Surface(region.pivot + direction * (last - .8f), out point)) return false;
                var right = new Vector3(-direction.z, 0, direction.x);
                for (int side = -1; side <= 1; side++)
                {
                    var approach = point - direction * 1.1f + right * (side * Mathf.Min(.85f, region.width * .35f));
                    if (!Surface(approach, out var floor) || Mathf.Abs(floor.y - point.y) > .45f) return false;
                }
                return true;
            }

            public bool Surface(Vector3 p, out Vector3 point)
            {
                float height = float.MinValue;
                EnsureSurfaceCache();
                if (surfaceCells.TryGetValue(SurfaceCell(p), out var candidates))
                    foreach (int i in candidates)
                    {
                        var a = surfaceTriangles[i]; var b = surfaceTriangles[i + 1]; var c = surfaceTriangles[i + 2];
                        if (p.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || p.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x)) || p.z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) || p.z > Mathf.Max(a.z, Mathf.Max(b.z, c.z))) continue;
                        float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                        if (Mathf.Abs(denominator) < .00001f) continue;
                        float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / denominator;
                        float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / denominator;
                        if (u >= -.001f && v >= -.001f && u + v <= 1.001f) height = Mathf.Max(height, u * a.y + v * b.y + (1 - u - v) * c.y);
                    }
                point = new Vector3(p.x, height, p.z);
                return height > float.MinValue;
            }
        }
        public Platform[] platforms;
        public SpawnBody[] spawnBodies = Array.Empty<SpawnBody>();
        public string spawnBakeSignature;
        public Connector timber, singleStair, doubleStair;
        void OnValidate()
        {
            if (platforms != null) foreach (var platform in platforms) platform?.InvalidateSurfaceCache();
        }
    }
}
