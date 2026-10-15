using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.AI;

namespace Overburst.Mojave
{
    public sealed partial class DesertRunWorld
    {
        const float BoundarySkin = .05f;
        // The minimap bake already excludes obstacle clearance; loosen the invisible guard by 50 cm.
        internal static float BoundaryRadius(float radius) => Mathf.Max(BoundarySkin, radius + BoundarySkin - .50f);
        static DesertRunWorld activeBoundary;
        WalkBoundary boundary;
        Vector3 boundaryEntry;

        internal static DesertRunWorld BoundaryWorld => activeBoundary && activeBoundary.isActiveAndEnabled
            && activeBoundary.gate && activeBoundary.gate.IsReady
            && WorldSessionState.Phase == WorldPhase.Run
            && WorldSessionState.ContentScene == activeBoundary.gameObject.scene ? activeBoundary : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBoundary() => activeBoundary = null;
        void OnEnable() { if (boundary != null && gate && gate.IsReady) activeBoundary = this; }
        void ReleaseBoundary() { if (activeBoundary == this) activeBoundary = null; }
        internal bool ContainsWalkableFootprint(Vector3 point, float radius)
            => boundary != null && boundary.CanStand(new Vector2(point.x, point.z), radius + BoundarySkin);

        internal struct BoundaryMovement
        {
            DesertRunWorld world;
            WalkBoundary grid;
            Vector2 safe;
            float safeRadius;
            bool hasSafe;
            public bool Active => world && BoundaryWorld == world && grid == world.boundary;

            public Vector3 Resolve(Vector3 from, Vector3 target, float radius)
            {
                var current = BoundaryWorld;
                if (!current) { this = default; return target; }
                if (!Active) { world = current; grid = current.boundary; hasSafe = false; }
                float margin = BoundaryRadius(radius);
                var start = new Vector2(from.x, from.z); var goal = new Vector2(target.x, target.z);
                if (!hasSafe || safeRadius != margin)
                {
                    if (grid.CanStand(start, margin)) safe = start;
                    else if (!hasSafe || !grid.CanStand(safe, margin)) safe = new Vector2(world.boundaryEntry.x, world.boundaryEntry.z);
                    if (!grid.CanStand(safe, margin)) return from;
                    hasSafe = true; safeRadius = margin;
                }
                if (start != safe) safe = grid.Move(safe, start, margin);
                if (goal != safe) safe = grid.Move(safe, goal, margin);
                return new Vector3(safe.x, target.y, safe.y);
            }
        }

        // The full minimap topology owns the boundary; explored pixels never affect movement.
        internal sealed class WalkBoundary
        {
            readonly DesertMinimapData data;
            const float Epsilon = .00001f;
            static readonly byte[] Flags = { MinimapExplorationMap.East, MinimapExplorationMap.West, MinimapExplorationMap.North, MinimapExplorationMap.South };
            static readonly int[] DX = { 1, -1, 0, 0 }, DZ = { 0, 0, 1, -1 };
            public WalkBoundary(DesertMinimapData value)
            {
                data = value;
                if (!data || data.width < 1 || data.height < 1 || !float.IsFinite(data.cellSize) || data.cellSize <= 0
                    || data.cells == null || data.connections == null || (long)data.width * data.height != data.cells.Length
                    || data.connections.Length != data.cells.Length) throw new InvalidOperationException("사막 이동 경계 격자가 올바르지 않습니다.");
                for (int z = 0; z < data.height; z++) for (int x = 0; x < data.width; x++)
                {
                    int i = z * data.width + x;
                    if (data.cells[i] > MinimapTerrainPixels.Obstacle || (data.connections[i] & ~15) != 0
                        || !Ground(x, z) && data.connections[i] != 0) throw new InvalidOperationException("사막 이동 경계 셀 값이 올바르지 않습니다.");
                    for (int direction = 0; direction < 4; direction++)
                        if ((data.connections[i] & Flags[direction]) != 0
                            && (!Ground(x + DX[direction], z + DZ[direction])
                                || (data.connections[(z + DZ[direction]) * data.width + x + DX[direction]] & Flags[direction ^ 1]) == 0))
                            throw new InvalidOperationException("사막 이동 경계 연결이 대칭이 아닙니다.");
                }
            }
            bool Ground(int x, int z) => x >= 0 && z >= 0 && x < data.width && z < data.height
                && data.cells[z * data.width + x] == MinimapTerrainPixels.Ground;
            bool Closed(int x, int z, int direction) => (data.connections[z * data.width + x] & Flags[direction]) == 0;
            Vector2 Corner(int x, int z) => data.bounds.position + new Vector2(x, z) * data.cellSize;
            int X(float x) => Mathf.FloorToInt((x - data.bounds.xMin) / data.cellSize);
            int Z(float z) => Mathf.FloorToInt((z - data.bounds.yMin) / data.cellSize);
            void Edge(int x, int z, int direction, out Vector2 a, out Vector2 b, out Vector2 inward)
            {
                var low = Corner(x, z); float cell = data.cellSize;
                switch (direction)
                {
                    case 0: a = low + Vector2.right * cell; b = a + Vector2.up * cell; inward = Vector2.left; break;
                    case 1: a = low; b = a + Vector2.up * cell; inward = Vector2.right; break;
                    case 2: a = low + Vector2.up * cell; b = a + Vector2.right * cell; inward = Vector2.down; break;
                    default: a = low; b = a + Vector2.right * cell; inward = Vector2.up; break;
                }
            }
            static float DistanceSquared(Vector2 p, Vector2 a, Vector2 b)
            {
                var edge = b - a;
                return (p - (a + edge * Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude))).sqrMagnitude;
            }
            public bool CanStand(Vector2 point, float radius)
            {
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(radius) || radius < 0 || !Ground(X(point.x), Z(point.y))) return false;
                int left = Mathf.Max(0, X(point.x - radius) - 1), right = Mathf.Min(data.width - 1, X(point.x + radius) + 1);
                int bottom = Mathf.Max(0, Z(point.y - radius) - 1), top = Mathf.Min(data.height - 1, Z(point.y + radius) + 1);
                float squared = radius * radius;
                for (int z = bottom; z <= top; z++) for (int x = left; x <= right; x++)
                {
                    if (!Ground(x, z)) continue;
                    for (int direction = 0; direction < 4; direction++)
                        if (Closed(x, z, direction))
                        {
                            Edge(x, z, direction, out var a, out var b, out _);
                            if (DistanceSquared(point, a, b) + Epsilon < squared) return false;
                        }
                }
                return true;
            }
            struct Contact { public float Time; public Vector2 Normal; }
            static void HitPoint(Vector2 from, Vector2 delta, float radius, Vector2 point, ref Contact contact)
            {
                var offset = from - point; float approach = Vector2.Dot(offset, delta);
                if (approach >= 0) return;
                float squared = delta.sqrMagnitude, c = offset.sqrMagnitude - radius * radius;
                float discriminant = approach * approach - squared * c;
                if (discriminant < 0) return;
                float time = c <= 0 ? 0 : (-approach - Mathf.Sqrt(discriminant)) / squared;
                if (time < 0 || time > contact.Time) return;
                var normal = (offset + delta * time).normalized;
                if (Vector2.Dot(delta, normal) >= -Epsilon) return;
                contact.Time = time; contact.Normal = normal;
            }
            void HitCell(int x, int z, Vector2 from, Vector2 delta, float radius, ref Contact contact)
            {
                if (!Ground(x, z)) return;
                for (int direction = 0; direction < 4; direction++)
                {
                    if (!Closed(x, z, direction)) continue;
                    Edge(x, z, direction, out var a, out var b, out var inward);
                    float speed = Vector2.Dot(delta, inward), distance = Vector2.Dot(from - a, inward);
                    if (speed < -Epsilon && distance >= radius - Epsilon)
                    {
                        float time = Mathf.Max(0, (radius - distance) / speed);
                        var edge = b - a;
                        float along = Vector2.Dot(from + delta * time - a, edge) / edge.sqrMagnitude;
                        if (time <= contact.Time && along >= 0 && along <= 1)
                        { contact.Time = time; contact.Normal = inward; }
                    }
                    HitPoint(from, delta, radius, a, ref contact); HitPoint(from, delta, radius, b, ref contact);
                }
            }
            Contact Sweep(Vector2 from, Vector2 delta, float radius)
            {
                var contact = new Contact { Time = 1 };
                int x = X(from.x), z = Z(from.y), reach = Mathf.CeilToInt(radius / data.cellSize) + 1;
                int sx = Math.Sign(delta.x), sz = Math.Sign(delta.y);
                float nextX = sx == 0 ? float.PositiveInfinity : (Corner(x + (sx > 0 ? 1 : 0), z).x - from.x) / delta.x;
                float nextZ = sz == 0 ? float.PositiveInfinity : (Corner(x, z + (sz > 0 ? 1 : 0)).y - from.y) / delta.y;
                float stepX = sx == 0 ? float.PositiveInfinity : data.cellSize / Mathf.Abs(delta.x);
                float stepZ = sz == 0 ? float.PositiveInfinity : data.cellSize / Mathf.Abs(delta.y);
                for (int step = 0; step <= data.width + data.height; step++)
                {
                    for (int nz = Mathf.Max(0, z - reach); nz <= Mathf.Min(data.height - 1, z + reach); nz++)
                        for (int nx = Mathf.Max(0, x - reach); nx <= Mathf.Min(data.width - 1, x + reach); nx++)
                            HitCell(nx, nz, from, delta, radius, ref contact);
                    float next = Mathf.Min(nextX, nextZ);
                    if (contact.Time <= next || next > 1) break;
                    if (nextX <= next) { x += sx; nextX += stepX; }
                    if (nextZ <= next) { z += sz; nextZ += stepZ; }
                }
                return contact;
            }
            public Vector2 Move(Vector2 from, Vector2 target, float radius)
            {
                if (!float.IsFinite(target.x) || !float.IsFinite(target.y)) return from;
                var position = from; var remaining = target - from; bool redirected = false;
                for (int collision = 0; collision < 3 && remaining.sqrMagnitude > Epsilon * Epsilon; collision++)
                {
                    var hit = Sweep(position, remaining, radius);
                    position += remaining * hit.Time;
                    if (hit.Normal == Vector2.zero) break;
                    redirected = true;
                    remaining *= 1 - hit.Time;
                    remaining -= hit.Normal * Mathf.Min(0, Vector2.Dot(remaining, hit.Normal));
                }
                // Motors receive one displacement, so its chord must be safe even after sliding.
                var direct = position - from;
                if (redirected && direct.sqrMagnitude > Epsilon * Epsilon)
                    position = from + direct * Sweep(from, direct, radius).Time;
                return CanStand(position, radius) ? position : from;
            }
            public void AppendNavigationSources(List<NavMeshBuildSource> sources, Bounds bounds, float voxel)
            {
                int area = NavMesh.GetAreaFromName("Not Walkable");
                if (area < 0) throw new InvalidOperationException("NavMesh 통행 불가 영역이 없습니다.");
                void Box(float x, float z, float width, float depth)
                {
                    if (width <= 0 || depth <= 0) return;
                    sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox, area = area,
                        transform = Matrix4x4.TRS(new Vector3(x + width * .5f, bounds.center.y, z + depth * .5f), Quaternion.identity, Vector3.one),
                        size = new Vector3(width, bounds.size.y + 2, depth) });
                }
                var map = data.bounds; float cell = data.cellSize;
                Box(bounds.min.x, bounds.min.z, Mathf.Max(0, map.xMin - bounds.min.x), bounds.size.z);
                Box(map.xMax, bounds.min.z, Mathf.Max(0, bounds.max.x - map.xMax), bounds.size.z);
                Box(map.xMin, bounds.min.z, map.width, Mathf.Max(0, map.yMin - bounds.min.z));
                Box(map.xMin, map.yMax, map.width, Mathf.Max(0, bounds.max.z - map.yMax));
                var used = new bool[data.cells.Length];
                for (int z = 0; z < data.height; z++) for (int x = 0; x < data.width; x++)
                {
                    int index = z * data.width + x;
                    if (Ground(x, z) || used[index]) continue;
                    int width = 1, height = 1;
                    while (x + width < data.width && !Ground(x + width, z) && !used[index + width]) width++;
                    bool Extend(int row)
                    { for (int i = 0; i < width; i++) if (Ground(x + i, row) || used[row * data.width + x + i]) return false; return true; }
                    while (z + height < data.height && Extend(z + height)) height++;
                    for (int row = z; row < z + height; row++) for (int column = x; column < x + width; column++) used[row * data.width + column] = true;
                    var low = Corner(x, z); Box(low.x, low.y, width * cell, height * cell);
                }
                float thickness = Mathf.Max(.05f, voxel * 2);
                for (int z = 0; z < data.height; z++) for (int x = 0; x < data.width; x++)
                {
                    if (!Ground(x, z)) continue;
                    var low = Corner(x, z);
                    if (Ground(x + 1, z) && Closed(x, z, 0)) Box(low.x + cell - thickness * .5f, low.y, thickness, cell);
                    if (Ground(x, z + 1) && Closed(x, z, 2)) Box(low.x, low.y + cell - thickness * .5f, cell, thickness);
                }
            }
        }
    }
}
