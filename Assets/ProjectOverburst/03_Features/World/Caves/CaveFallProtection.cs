using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Caves
{
    // Edited prefab outlines plus installed connectors own the boundary; legacy decks use colliders.
    public static class CaveFallProtection
    {
        static readonly Unity.Profiling.ProfilerMarker ResolveMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.Resolve");
        static readonly Unity.Profiling.ProfilerMarker AnchorMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.AnchorSweep");
        static readonly Unity.Profiling.ProfilerMarker TargetMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.TargetSweep");
        static readonly Unity.Profiling.ProfilerMarker SlideXMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.SlideX");
        static readonly Unity.Profiling.ProfilerMarker SlideZMarker = new Unity.Profiling.ProfilerMarker("Overburst.CaveBoundary.SlideZ");
        public static bool HasActiveWorld => surfaces.Count > 0;
        const float CellSize = 8f;
        const float SweepStep = .2f;
        const float StepHeight = 3f; // Authored stair/platform seams include rises above 2m.
        static readonly List<Surface> surfaces = new List<Surface>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => surfaces.Clear();

        public static void Register(CaveWorld world)
        {
            Unregister(world);
            surfaces.Add(new Surface(world));
        }

        public static void Unregister(CaveWorld world)
        {
            for (int i = surfaces.Count - 1; i >= 0; i--)
                if (!surfaces[i].world || surfaces[i].world == world) surfaces.RemoveAt(i);
        }

        sealed class Surface
        {
            public readonly CaveWorld world;
            Transform root;
            CavePlatformBoundary[] boundaries;
            CaveRigidConnection[] connections;
            Bounds[] boundaryBounds;
            readonly Dictionary<CavePlatformBoundary, List<CaveRigidConnection>> exits = new Dictionary<CavePlatformBoundary, List<CaveRigidConnection>>();
            readonly Dictionary<Vector2Int, List<Collider>> cells = new Dictionary<Vector2Int, List<Collider>>();
            public Surface(CaveWorld value) { world = value; }
            static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

            void Prepare()
            {
                if (root == world.generatedRoot) return;
                root = world.generatedRoot;
                cells.Clear();
                if (!root) return;
                boundaries = root.GetComponentsInChildren<CavePlatformBoundary>();
                connections = root.GetComponentsInChildren<CaveRigidConnection>();
                exits.Clear();
                foreach (var boundary in boundaries) exits[boundary] = new List<CaveRigidConnection>();
                foreach (var connection in connections)
                {
                    if (connection.boundaryA && exits.TryGetValue(connection.boundaryA, out var a)) a.Add(connection);
                    if (connection.boundaryB && exits.TryGetValue(connection.boundaryB, out var b)) b.Add(connection);
                }
                boundaryBounds = new Bounds[boundaries.Length];
                for (int i = 0; i < boundaries.Length; i++)
                {
                    bool first = true;
                    foreach (var loop in boundaries[i].Data.loops)
                        foreach (var point in loop.points)
                        {
                            var p = boundaries[i].transform.TransformPoint(point);
                            if (first) { boundaryBounds[i] = new Bounds(p, Vector3.zero); first = false; }
                            else boundaryBounds[i].Encapsulate(p);
                        }
                }
                if (boundaries.Length > 0) return;
                foreach (var collider in root.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.isTrigger || !collider.GetComponentInParent<CaveWalkSurface>()) continue;
                    var bounds = collider.bounds;
                    var min = Cell(bounds.min); var max = Cell(bounds.max);
                    for (int z = min.y; z <= max.y; z++)
                        for (int x = min.x; x <= max.x; x++)
                        {
                            var key = new Vector2Int(x, z);
                            if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<Collider>());
                            list.Add(collider);
                        }
                }
            }

            public bool ConnectorWaypoint(Vector3 from, Vector3 goal, float radius, out Vector3 point)
            {
                point = from; Prepare();
                if (connections == null) return false;
                foreach (var link in connections)
                {
                    if (!link || link.stairCount > 0 || !link.Sample(from, .8f, .8f, out _)) continue;
                    var axis = link.end - link.start; axis.y = 0; float length = axis.magnitude;
                    if (length < .1f || link.walkWidth <= radius * 2 + .1f) continue;
                    axis /= length;
                    float along = Vector3.Dot(from - link.start, axis);
                    float target = Vector3.Dot(goal - link.start, axis);
                    bool forward = target > along;
                    // Give the platform path back control after crossing; Sample includes a 2m seam overlap.
                    if (forward ? along >= length + 1f : along <= -1f) continue;
                    // Only a destination beyond this installed passage owns its approach.
                    if (forward ? target < length + .5f : target > -.5f) continue;
                    float next = forward ? Mathf.Min(length + 1.5f, along + 3f) : Mathf.Max(-1.5f, along - 3f);
                    point = link.start + axis * next; point.y = from.y;
                    return true;
                }
                return false;
            }

            public bool Ground(Vector3 near, float up, float down, out Vector3 point, CavePlatformBoundary owner = null, bool allowStairs = true)
            {
                point = near;
                if (!world || !world.isActiveAndEnabled) return false;
                Prepare();
                if (boundaries != null && boundaries.Length > 0)
                {
                    if (owner)
                    {
                        bool found = owner.isActiveAndEnabled && owner.Sample(near, up, down, out point);
                        float ownerDistance = found ? Mathf.Abs(point.y - near.y) : float.MaxValue;
                        if (exits.TryGetValue(owner, out var links))
                            foreach (var link in links)
                            {
                                if (!link || !allowStairs && link.stairCount > 0 || !link.Sample(near, up, down, out var p)) continue;
                                float difference = Mathf.Abs(p.y - near.y);
                                if (difference < ownerDistance) { ownerDistance = difference; point = p; found = true; }
                            }
                        return found;
                    }
                    float distance = float.MaxValue;
                    for (int i = 0; i < boundaries.Length; i++)
                    {
                        var b = boundaryBounds[i];
                        if (!boundaries[i] || !boundaries[i].isActiveAndEnabled || near.x < b.min.x || near.x > b.max.x || near.z < b.min.z || near.z > b.max.z) continue;
                        if (!boundaries[i].Sample(near, up, down, out var p)) continue;
                        float d = Mathf.Abs(near.y - p.y);
                        if (d < distance) { distance = d; point = p; }
                    }
                    foreach (var connection in connections)
                    {
                        if (!connection || !connection.Sample(near, up, down, out var p)) continue;
                        float d = Mathf.Abs(near.y - p.y);
                        if (d < distance) { distance = d; point = p; }
                    }
                    return distance < float.MaxValue;
                }
                if (!cells.TryGetValue(Cell(near), out var colliders)) return false;
                var ray = new Ray(near + Vector3.up * up, Vector3.down);
                float best = float.MaxValue;
                foreach (var collider in colliders)
                {
                    if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                    if (!collider.Raycast(ray, out var hit, up + down) || hit.normal.y < .7f) continue;
                    float delta = Mathf.Abs(hit.point.y - near.y);
                    if (delta >= best) continue;
                    best = delta; point = hit.point;
                }
                return best < float.MaxValue;
            }

            public CavePlatformBoundary Owner(Vector3 near)
            {
                Prepare();
                if (boundaries == null) return null;
                CavePlatformBoundary best = null; float distance = float.MaxValue;
                foreach (var boundary in boundaries)
                    if (boundary && boundary.isActiveAndEnabled && boundary.Sample(near, StepHeight, 40, out var p) && Mathf.Abs(p.y - near.y) < distance)
                    { best = boundary; distance = Mathf.Abs(p.y - near.y); }
                foreach (var link in connections)
                    if (link && link.Sample(near, StepHeight, 40, out var p) && Mathf.Abs(p.y - near.y) < distance)
                    { best = link.boundaryA; distance = Mathf.Abs(p.y - near.y); }
                return best;
            }

            CavePlatformBoundary NextOwner(CavePlatformBoundary owner, Vector3 near, bool allowStairs)
            {
                if (!owner || !exits.TryGetValue(owner, out var links)) return owner;
                foreach (var link in links)
                {
                    if (!link || !allowStairs && link.stairCount > 0 || !link.Sample(near, StepHeight, StepHeight, out _)) continue;
                    var other = link.boundaryA == owner ? link.boundaryB : link.boundaryA;
                    if (other.Sample(near, StepHeight, StepHeight, out _) && !owner.ContainsLocal(owner.transform.InverseTransformPoint(near))) return other;
                }
                return owner;
            }

            public bool Supported(Vector3 near, float margin, out Vector3 ground, CavePlatformBoundary owner = null, bool allowStairs = true)
            {
                if (!Ground(near, StepHeight, StepHeight, out ground, owner, allowStairs)) return false;
                // Check the footprint, not just the centre, so capsules cannot balance over an edge.
                for (int i = 0; i < 4 && margin > 0; i++)
                {
                    Vector3 offset = i < 2 ? Vector3.right * (i == 0 ? margin : -margin)
                        : Vector3.forward * (i == 2 ? margin : -margin);
                    if (!Ground(ground + offset, StepHeight, StepHeight, out _, owner, allowStairs)) return false;
                }
                return true;
            }

            public Vector3 Sweep(Vector3 start, Vector3 target, float margin, ref CavePlatformBoundary owner, bool allowStairs = true)
            {
                Vector3 delta = target - start; delta.y = 0;
                if (delta.sqrMagnitude < .00000001f) return start;
                int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / SweepStep));
                Vector3 safe = start;
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 next = start + delta * (i / (float)steps); next.y = safe.y;
                    var candidateOwner = NextOwner(owner, next, allowStairs);
                    if (Supported(next, margin, out var ground, candidateOwner, allowStairs)) { safe = ground; owner = candidateOwner; continue; }
                    // Keep the full continuous segment inside the deck, including high-speed dashes.
                    Vector3 blocked = next;
                    for (int j = 0; j < 6; j++)
                    {
                        Vector3 middle = Vector3.Lerp(safe, blocked, .5f);
                        candidateOwner = NextOwner(owner, middle, allowStairs);
                        if (Supported(middle, margin, out ground, candidateOwner, allowStairs)) { safe = ground; owner = candidateOwner; }
                        else blocked = middle;
                    }
                    break;
                }
                return safe;
            }
        }

        static Surface Find(Vector3 position, out Vector3 ground)
        {
            Surface result = null; ground = position; float best = float.MaxValue;
            foreach (var surface in surfaces)
            {
                if (!surface.Ground(position, StepHeight, 40f, out var hit)) continue;
                float distance = Mathf.Abs(position.y - hit.y);
                if (distance >= best) continue;
                result = surface; best = distance; ground = hit;
            }
            return result;
        }

        public static bool TryConnectorWaypoint(Vector3 from, Vector3 goal, float radius, out Vector3 point)
        {
            foreach (var surface in surfaces)
                if (surface.world && surface.world.isActiveAndEnabled && surface.ConnectorWaypoint(from, goal, radius, out point)) return true;
            point = from; return false;
        }

        // Each motor owns its last supported point, including while jumping or being pushed by physics.
        public struct Movement
        {
            Surface surface;
            Vector3 safeGround;
            CavePlatformBoundary owner;
            Overburst.Mojave.DesertRunWorld.BoundaryMovement desert;
            bool CaveActive => surface != null && surface.world && surface.world.isActiveAndEnabled;
            public bool Active => CaveActive || desert.Active;

            public Vector3 Resolve(Vector3 from, Vector3 target, float radius, float rootAboveFoot = 0, bool allowStairs = true)
            {
                using var sample = ResolveMarker.Auto();
                if (Overburst.Mojave.DesertRunWorld.BoundaryWorld)
                    return desert.Resolve(from, target, radius);
                desert = default;
                if (!CaveActive) { surface = Find(from, out safeGround); owner = surface?.Owner(safeGround); }
                if (surface == null) return target;
                float margin = Mathf.Clamp(radius * .7f, .12f, .45f);
                // Physics contacts may have moved 'from' since the previous call; sweep that displacement too.
                using (AnchorMarker.Auto()) safeGround = surface.Sweep(safeGround, from, margin, ref owner, allowStairs);
                Vector3 next;
                using (TargetMarker.Auto()) next = surface.Sweep(safeGround, target, margin, ref owner, allowStairs);
                var ownerX = owner; var ownerZ = owner;
                Vector3 alongX;
                using (SlideXMarker.Auto()) alongX = surface.Sweep(next, new Vector3(target.x, next.y, next.z), margin, ref ownerX, allowStairs);
                Vector3 alongZ;
                using (SlideZMarker.Auto()) alongZ = surface.Sweep(next, new Vector3(next.x, next.y, target.z), margin, ref ownerZ, allowStairs);
                if ((alongX - next).sqrMagnitude > (alongZ - next).sqrMagnitude) { next = alongX; owner = ownerX; }
                else { next = alongZ; owner = ownerZ; }
                safeGround = next;
                // Keep jump height; only prevent the feet sinking below their supporting deck.
                next.y = Mathf.Max(target.y, next.y + rootAboveFoot);
                return next;
            }
        }

        // Loot offsets can already start beyond a rim. Repair that origin before playing its arc.
        public static bool TryDropOrigin(Vector3 origin, out Vector3 safe)
        {
            if (surfaces.Count == 0) { safe = origin; return false; }
            var surface = Find(origin, out var ground);
            if (surface != null && surface.Supported(ground, .18f, out ground))
            { safe = new Vector3(ground.x, Mathf.Max(origin.y, ground.y), ground.z); return true; }
            for (float radius = .25f; radius <= 3f; radius += .25f)
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    var p = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    surface = Find(p, out ground);
                    if (surface == null || !surface.Supported(ground, .18f, out ground)) continue;
                    safe = new Vector3(ground.x, Mathf.Max(origin.y, ground.y), ground.z); return true;
                }
            safe = origin; return false;
        }

        public static bool TryDropLanding(Vector3 origin, Vector3 target, float clearance, out Vector3 landing)
        {
            var surface = Find(origin, out var ground);
            if (surface == null) { landing = target; return false; }
            var owner = surface.Owner(ground);
            landing = surface.Sweep(ground, target, .18f, ref owner);
            landing.y += clearance;
            return true;
        }
    }
}
