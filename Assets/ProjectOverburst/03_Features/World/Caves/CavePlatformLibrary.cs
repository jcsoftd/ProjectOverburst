using System;
using UnityEngine;

namespace Overburst.Caves
{
    [CreateAssetMenu(menuName = "Overburst/Caves/Authored platform library")]
    public sealed class CavePlatformLibrary : ScriptableObject
    {
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
                    for (int i = 0; i < surfaceTriangles.Length; i += 3)
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
        public Connector timber, singleStair, doubleStair;
    }
}
