using System;
using System.Collections.Generic;
using Overburst.Caves;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CaveMinimapTerrain
{
    public const float CellSize = .5f;
    public static MinimapExplorationMap Build(CaveWorld world)
    {
        if (!world || world.courts.Count == 0) throw new ArgumentException("Generated cave required.");
        var boundaries = new CavePlatformBoundary[world.courts.Count];
        var footprints = new Bounds[boundaries.Length];
        Bounds bounds = default;
        for (int i = 0; i < boundaries.Length; i++)
        {
            var boundary = world.courts[i].tile.GetComponent<CavePlatformBoundary>();
            if (!boundary || boundary.Data.loops.Length == 0) throw new InvalidOperationException("Cave platform outline missing.");
            boundaries[i] = boundary;
            bool first = true;
            foreach (var loop in boundary.Data.loops) foreach (var p in loop.points)
            {
                var point = boundary.transform.TransformPoint(p);
                if (first) { footprints[i] = new Bounds(point, Vector3.zero); first = false; } else footprints[i].Encapsulate(point);
            }
            if (i == 0) bounds = footprints[i]; else bounds.Encapsulate(footprints[i]);
        }
        foreach (var passage in world.passages)
            for (int i = 0; i < passage.points.Length; i++)
            { var p = passage.points[i]; float half = passage.widths[i] * .5f; bounds.Encapsulate(p + new Vector3(half,0,half)); bounds.Encapsulate(p - new Vector3(half,0,half)); }
        float minX = Mathf.Floor(bounds.min.x / CellSize) * CellSize - CellSize;
        float minZ = Mathf.Floor(bounds.min.z / CellSize) * CellSize - CellSize;
        int width = Mathf.CeilToInt((bounds.max.x - minX) / CellSize) + 1, height = Mathf.CeilToInt((bounds.max.z - minZ) / CellSize) + 1;
        if ((long)width * height > 4194304 || width > 4096 || height > 4096) throw new InvalidOperationException("Cave minimap exceeds its topology budget.");
        var cells = new byte[width * height]; var owners = new int[cells.Length];
        for (int i = 0; i < boundaries.Length; i++)
        {
            int owner = i + 1; var boundary = boundaries[i];
            Fill(footprints[i], p => boundary.ContainsLocal(boundary.transform.InverseTransformPoint(p)), owner);
        }
        for (int i = 0; i < world.passages.Count; i++)
        {
            var passage = world.passages[i];
            if (passage.a < 0 || passage.b < 0 || passage.a >= boundaries.Length || passage.b >= boundaries.Length)
                throw new InvalidOperationException("Cave passage endpoints missing.");
            for (int segment = 0; segment < passage.points.Length - 1; segment++)
            {
                Vector3 a = passage.points[segment], b = passage.points[segment + 1];
                float wa = passage.widths[segment] * .5f, wb = passage.widths[segment + 1] * .5f;
                var span = b - a; span.y = 0; float length = span.sqrMagnitude;
                if (length < .000001f) continue;
                var box = new Bounds(a, Vector3.zero); box.Encapsulate(b); box.Expand(Mathf.Max(wa, wb) * 2);
                Fill(box, p =>
                {
                    var delta = p - a; delta.y = 0;
                    float t = Vector3.Dot(delta, span) / length;
                    if (t < 0 || t > 1) return false;
                    return (delta - span * t).sqrMagnitude <= Mathf.Pow(Mathf.Lerp(wa, wb, t), 2);
                }, boundaries.Length + i + 1);
            }
        }
        var obstacleLinks = new byte[cells.Length];
        RemoveObstacles();
        var links = new byte[cells.Length];
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
        {
            int here = z * width + x;
            if (x + 1 < width && (obstacleLinks[here] & MinimapExplorationMap.East) == 0 && Connected(owners[here], owners[here + 1], new Vector3(minX + (x + 1) * CellSize, 0, minZ + (z + .5f) * CellSize)))
            { links[here] |= MinimapExplorationMap.East; links[here + 1] |= MinimapExplorationMap.West; }
            if (z + 1 < height && (obstacleLinks[here] & MinimapExplorationMap.North) == 0 && Connected(owners[here], owners[here + width], new Vector3(minX + (x + .5f) * CellSize, 0, minZ + (z + 1) * CellSize)))
            { links[here] |= MinimapExplorationMap.North; links[here + width] |= MinimapExplorationMap.South; }
        }
        return new MinimapExplorationMap(width, height, CellSize, new Vector2(minX, minZ), cells, links);

        // One snapshot of fixed colliders per generated map; actors and overhead dressing never become walls.
        void RemoveObstacles()
        {
            var colliders = new HashSet<Collider>();
            foreach (var collider in (world.generatedRoot ? world.generatedRoot : world.transform).GetComponentsInChildren<Collider>(true))
                if (collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger
                    && !(collider is CharacterController) && !collider.attachedRigidbody
                    && !collider.GetComponentInParent<CaveWalkSurface>()
                    && !collider.GetComponentInParent<EnemyActor>() && !collider.GetComponentInParent<PlayerActorRuntime>()) colliders.Add(collider);
            if (colliders.Count == 0) return;
            var physics = world.gameObject.scene.GetPhysicsScene();
            var overlaps = new Collider[64]; var sampled = new bool[cells.Length];
            foreach (var collider in colliders)
            {
                var box = collider.bounds;
                int left = Mathf.Max(0, Mathf.FloorToInt((box.min.x - minX) / CellSize) - 1), right = Mathf.Min(width - 1, Mathf.CeilToInt((box.max.x - minX) / CellSize));
                int bottom = Mathf.Max(0, Mathf.FloorToInt((box.min.z - minZ) / CellSize) - 1), top = Mathf.Min(height - 1, Mathf.CeilToInt((box.max.z - minZ) / CellSize));
                for (int z = bottom; z <= top; z++) for (int x = left; x <= right; x++)
                {
                    int index = z * width + x, owner = owners[index];
                    if (owner == 0 || sampled[index]) continue;
                    var point = new Vector3(minX + (x + .5f) * CellSize, 0, minZ + (z + .5f) * CellSize);
                    if (owner <= boundaries.Length)
                    {
                        var boundary = boundaries[owner - 1]; point.y = footprints[owner - 1].center.y;
                        if (!boundary.Sample(point, 10000, 10000, out point)) continue;
                    }
                    else
                    {
                        var passage = world.passages[owner - boundaries.Length - 1]; float closest = float.PositiveInfinity;
                        for (int segment = 1; segment < passage.points.Length; segment++)
                        {
                            var a = passage.points[segment - 1]; var span = passage.points[segment] - a;
                            float t = Mathf.Clamp01(((point.x - a.x) * span.x + (point.z - a.z) * span.z) / Mathf.Max(.000001f, span.x * span.x + span.z * span.z));
                            var contact = a + span * t; float distance = (point.x - contact.x) * (point.x - contact.x) + (point.z - contact.z) * (point.z - contact.z);
                            if (distance < closest) { closest = distance; point.y = contact.y; }
                        }
                    }
                    if (box.max.y <= point.y + .35f || box.min.y >= point.y + 1.45f) continue;
                    sampled[index] = true;
                    // Sample centres and links separately: full-cell dilation seals narrow diagonal entrances.
                    if (Blocked(point, new Vector3(.01f, .55f, .01f)))
                    { cells[index] = MinimapTerrainPixels.Obstacle; owners[index] = 0; continue; }
                    if (Blocked(point + Vector3.right * CellSize * .5f, new Vector3(CellSize * .5f, .55f, .01f)))
                        obstacleLinks[index] |= MinimapExplorationMap.East;
                    if (Blocked(point + Vector3.forward * CellSize * .5f, new Vector3(.01f, .55f, CellSize * .5f)))
                        obstacleLinks[index] |= MinimapExplorationMap.North;
                }
            }
            bool Blocked(Vector3 point, Vector3 half)
            {
                int count = physics.OverlapBox(point + Vector3.up * .9f, half, overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) throw new InvalidOperationException("Cave minimap obstacle buffer saturated.");
                for (int i = 0; i < count; i++) if (colliders.Contains(overlaps[i])) return true;
                return false;
            }
        }

        void Fill(Bounds box, Func<Vector3, bool> contains, int owner)
        {
            int left = Mathf.Max(0, Mathf.FloorToInt((box.min.x - minX) / CellSize)), right = Mathf.Min(width - 1, Mathf.CeilToInt((box.max.x - minX) / CellSize));
            int bottom = Mathf.Max(0, Mathf.FloorToInt((box.min.z - minZ) / CellSize)), top = Mathf.Min(height - 1, Mathf.CeilToInt((box.max.z - minZ) / CellSize));
            for (int z = bottom; z <= top; z++) for (int x = left; x <= right; x++)
            {
                int index = z * width + x;
                if (owners[index] != 0 || !contains(new Vector3(minX + (x + .5f) * CellSize, box.center.y, minZ + (z + .5f) * CellSize))) continue;
                owners[index] = owner; cells[index] = MinimapTerrainPixels.Ground;
            }
        }
        bool Connected(int a, int b, Vector3 middle)
        {
            if (a == 0 || b == 0) return false;
            if (a == b)
            {
                if (a > boundaries.Length) return true;
                var boundary = boundaries[a - 1];
                middle.y = boundary.transform.position.y;
                return boundary.ContainsLocal(boundary.transform.InverseTransformPoint(middle));
            }
            if (a > b) { int swap = a; a = b; b = swap; }
            if (a > boundaries.Length || b <= boundaries.Length) return false;
            var passage = world.passages[b - boundaries.Length - 1];
            return passage.a == a - 1 || passage.b == a - 1;
        }
    }
}
