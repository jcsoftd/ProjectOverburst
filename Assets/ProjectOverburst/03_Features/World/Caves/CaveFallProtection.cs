using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Caves
{
    // Authored platform/connector colliders own the boundary. Scenery and NavMesh gaps do not.
    public static class CaveFallProtection
    {
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
            readonly Dictionary<Vector2Int, List<Collider>> cells = new Dictionary<Vector2Int, List<Collider>>();
            public Surface(CaveWorld value) { world = value; }
            static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

            void Prepare()
            {
                if (root == world.generatedRoot) return;
                root = world.generatedRoot;
                cells.Clear();
                if (!root) return;
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

            public bool Ground(Vector3 near, float up, float down, out Vector3 point)
            {
                point = near;
                if (!world || !world.isActiveAndEnabled) return false;
                Prepare();
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

            public bool Supported(Vector3 near, float margin, out Vector3 ground)
            {
                if (!Ground(near, StepHeight, StepHeight, out ground)) return false;
                // Check the footprint, not just the centre, so capsules cannot balance over an edge.
                for (int i = 0; i < 4 && margin > 0; i++)
                {
                    Vector3 offset = i < 2 ? Vector3.right * (i == 0 ? margin : -margin)
                        : Vector3.forward * (i == 2 ? margin : -margin);
                    if (!Ground(ground + offset, StepHeight, StepHeight, out _)) return false;
                }
                return true;
            }

            public Vector3 Sweep(Vector3 start, Vector3 target, float margin)
            {
                Vector3 delta = target - start; delta.y = 0;
                if (delta.sqrMagnitude < .00000001f) return start;
                int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / SweepStep));
                Vector3 safe = start;
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 next = start + delta * (i / (float)steps); next.y = safe.y;
                    if (Supported(next, margin, out var ground)) { safe = ground; continue; }
                    // Keep the full continuous segment inside the deck, including high-speed dashes.
                    Vector3 blocked = next;
                    for (int j = 0; j < 6; j++)
                    {
                        Vector3 middle = Vector3.Lerp(safe, blocked, .5f);
                        if (Supported(middle, margin, out ground)) safe = ground;
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

        // Each motor owns its last supported point, including while jumping or being pushed by physics.
        public struct Movement
        {
            Surface surface;
            Vector3 safeGround;
            public bool Active => surface != null && surface.world && surface.world.isActiveAndEnabled;

            public Vector3 Resolve(Vector3 from, Vector3 target, float radius, float rootAboveFoot = 0)
            {
                if (!Active) surface = Find(from, out safeGround);
                if (surface == null) return target;
                float margin = Mathf.Clamp(radius * .7f, .12f, .45f);
                // Physics contacts may have moved 'from' since the previous call; sweep that displacement too.
                safeGround = surface.Sweep(safeGround, from, margin);
                Vector3 next = surface.Sweep(safeGround, target, margin);
                Vector3 alongX = surface.Sweep(next, new Vector3(target.x, next.y, next.z), margin);
                Vector3 alongZ = surface.Sweep(next, new Vector3(next.x, next.y, target.z), margin);
                if ((alongX - next).sqrMagnitude > (alongZ - next).sqrMagnitude) next = alongX;
                else next = alongZ;
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
            landing = surface.Sweep(ground, target, .18f);
            landing.y += clearance;
            return true;
        }
    }
}
