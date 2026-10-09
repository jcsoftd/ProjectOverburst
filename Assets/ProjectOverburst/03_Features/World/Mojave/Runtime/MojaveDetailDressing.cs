using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overburst.Mojave
{
    // Opt-in, baked dressing. Its own RNG leaves the accepted terrain/rock generation unchanged.
    [DisallowMultipleComponent, RequireComponent(typeof(MojaveWorld))]
    public sealed class MojaveDetailDressing : MonoBehaviour
    {
        public int variation = 1;
        public Transform dressingRoot;
        public int colonyCount, roadsideColonies, rockColonies, plantCount;
        public float buildSeconds;

        readonly List<(Bounds bounds, Vector2[] outline)> solids = new List<(Bounds, Vector2[])>();
        readonly Dictionary<Vector2Int, List<int>> solidCells = new Dictionary<Vector2Int, List<int>>();
        readonly HashSet<int> visitedSolids = new HashSet<int>();
        readonly List<Vector2> colonies = new List<Vector2>();
        readonly Dictionary<Vector2Int, List<Vector2>> plants = new Dictionary<Vector2Int, List<Vector2>>();
        readonly Dictionary<GameObject, Bounds> prefabBounds = new Dictionary<GameObject, Bounds>();
        System.Random random;
        MojaveWorld world;
        GameObject[] flowers, dry, bushes;

        [ContextMenu("마지막 식생 마감 다시 생성")]
        public void Rebuild()
        {
            world = GetComponent<MojaveWorld>();
            if (world.surface == null || world.generatedRoot == null || world.catalog == null)
                throw new InvalidOperationException("Generate or load a baked Mojave map first.");
            flowers = world.catalog.grasses.Where(p => p != null && p.name.StartsWith("Thistle_")).ToArray();
            dry = world.catalog.grasses.Where(p => p != null && p.name.StartsWith("DryGrass")).ToArray();
            bushes = world.catalog.shrubs.Where(p => p != null && (p.name.StartsWith("Brittlebush") || p.name.StartsWith("Orangili"))).ToArray();
            if (flowers.Length == 0 || dry.Length == 0 || bushes.Length == 0)
                throw new InvalidOperationException("The Mojave flower/grass/shrub palette is required.");
            world.EnsureLayout();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (dressingRoot != null) {
                dressingRoot.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(dressingRoot.gameObject); else DestroyImmediate(dressingRoot.gameObject);
            }
            solids.Clear(); solidCells.Clear(); visitedSolids.Clear(); colonies.Clear(); plants.Clear(); prefabBounds.Clear();
            colonyCount = roadsideColonies = rockColonies = plantCount = 0;
            random = new System.Random(unchecked(world.seed * 397 ^ variation * 7919 ^ 0x4d4a));
            var plantNames = new HashSet<string>(world.catalog.shrubs.Concat(world.catalog.grasses).Where(p => p != null).Select(p => p.name));
            var rocks = new List<(Bounds bounds, Vector2[] outline)>();
            var meshVertices = new Dictionary<Mesh, Vector3[]>();
            foreach (var go in MojaveTerrainFinish.PropRoots(world)) {
                if (!go.activeInHierarchy) continue;
                if (plantNames.Contains(go.name)) { AddPlant(XZ(go.transform.position)); continue; }
                var rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = BoundsOf(rs);
                if (go.name.Contains("Rock") || go.name.Contains("Stone") || go.name.Contains("Boulder")) {
                    var points = new List<Vector2>();
                    var lod = go.GetComponent<LODGroup>();
                    foreach (var renderer in lod != null ? lod.GetLODs()[0].renderers : rs) {
                        if (renderer == null) continue;
                        var mf = renderer.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                        if (!meshVertices.TryGetValue(mf.sharedMesh, out var vertices)) meshVertices[mf.sharedMesh] = vertices = MojaveTerrainFinish.Vertices(mf.sharedMesh);
                        foreach (var v in vertices) points.Add(XZ(mf.transform.TransformPoint(v)));
                    }
                    var outline = MojaveTerrainFinish.Hull(points);
                    solids.Add((b, outline));
                    if (go.name.StartsWith("RockAssemble") && b.size.y > 1.8f && Mathf.Max(b.size.x, b.size.z) > 3)
                        rocks.Add((b, outline));
                } else if (go.name.StartsWith("Joshua") || go.name.StartsWith("Saguaro") || go.name.StartsWith("Piko")) {
                    var p = go.transform.position;
                    solids.Add((new Bounds(p, new Vector3(.8f, 2, .8f)), null));
                }
            }
            for (int i = 0; i < solids.Count; i++) {
                var b = solids[i].bounds; var a = SolidCell(XZ(b.min)); var end = SolidCell(XZ(b.max));
                for (int z = a.y; z <= end.y; z++) for (int x = a.x; x <= end.x; x++) {
                    var cell = new Vector2Int(x, z);
                    if (!solidCells.TryGetValue(cell, out var list)) solidCells[cell] = list = new List<int>();
                    list.Add(i);
                }
            }
            dressingRoot = new GameObject("Final vegetation · flower colonies and sheltered scrub").transform;
            dressingRoot.SetParent(world.generatedRoot, false);
            dressingRoot.gameObject.layer = world.generatedRoot.gameObject.layer;
            int roadsideBudget = Mathf.RoundToInt(36 * world.MapSize / 320);
            foreach (var trail in world.layout.trails) {
                float next = Range(3, 8), travelled = 0;
                for (int i = 0; i < trail.points.Length - 1; i++) {
                    var delta = trail.points[i + 1] - trail.points[i]; float length = delta.magnitude;
                    if (length < .001f) continue;
                    var tangent = delta / length; var normal = new Vector2(-tangent.y, tangent.x);
                    while (next < travelled + length) {
                        float t = Mathf.Clamp01((next - travelled) / length);
                        var center = Vector2.Lerp(trail.points[i], trail.points[i + 1], t);
                        float half = Mathf.Lerp(trail.widths[i], trail.widths[i + 1], t);
                        int side = random.Next(2) == 0 ? -1 : 1;
                        if (roadsideColonies < roadsideBudget && world.layout.RoomDistance(center, out _) > 0 && Range(0, 1) < .82f)
                            for (int attempt = 0; attempt < 4; attempt++) {
                                var p = center + tangent * Range(-1.5f, 1.5f) + normal * side * (half + Range(1, 2.3f) + attempt * .55f);
                                if (TryColony(p, tangent, true)) break;
                                side = -side;
                            }
                        next += Range(8, 15);
                    }
                    travelled += length;
                }
            }
            // Stable spatial order makes the same seed repeatable after saving and reloading a scene.
            rocks = rocks.OrderBy(b => b.bounds.center.x).ThenBy(b => b.bounds.center.z).ThenBy(b => b.bounds.size.y).ToList();
            for (int i = rocks.Count - 1; i > 0; i--) { int j = random.Next(i + 1); var swap = rocks[i]; rocks[i] = rocks[j]; rocks[j] = swap; }
            int rockBudget = Mathf.RoundToInt(44 * world.MapSize * world.MapSize / (320 * 320));
            foreach (var rock in rocks) {
                if (rockColonies >= rockBudget) break;
                if (Range(0, 1) > .55f) continue;
                if (rock.outline.Length < 3) continue;
                for (int attempt = 0; attempt < 3; attempt++) {
                    float angle = Range(0, Mathf.PI * 2);
                    var normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var outside = XZ(rock.bounds.center) + normal * (rock.bounds.extents.magnitude + 2);
                    var edge = outside; float closest = float.PositiveInfinity;
                    for (int i = 0; i < rock.outline.Length; i++) {
                        var a = rock.outline[i]; var d = rock.outline[(i + 1) % rock.outline.Length] - a;
                        var q = a + d * Mathf.Clamp01(Vector2.Dot(outside - a, d) / Mathf.Max(.0001f, d.sqrMagnitude));
                        float distance = (q - outside).sqrMagnitude;
                        if (distance < closest) { closest = distance; edge = q; }
                    }
                    normal = (outside - edge).normalized;
                    var p = edge + normal * Range(.45f, .7f);
                    if (TryColony(p, new Vector2(-normal.y, normal.x), false)) break;
                }
            }
            buildSeconds = (float)clock.Elapsed.TotalSeconds;
        }

        bool TryColony(Vector2 center, Vector2 tangent, bool roadside)
        {
            if (!Clear(center, .32f) || Nearby(center, 2.2f) >= 7 || colonies.Any(p => (p - center).sqrMagnitude < 36)) return false;
            var groupPosition = world.Ground(center);
            var normal = new Vector2(-tangent.y, tangent.x);
            float length = Range(1.5f, 2.2f), width = Range(.75f, 1.15f);
            // Nearby colonies share a broad colour family, rather than mixing every colour in each patch.
            float colour = world.layout.Noise(center, .021f, 1841);
            string suffix = colour < .40f ? "_violet" : colour > .63f ? "_orange" : "";
            var palette = flowers.Where(p => suffix.Length == 0 ? !p.name.EndsWith("_violet") && !p.name.EndsWith("_orange") : p.name.EndsWith(suffix)).ToArray();
            bool blooming = Range(0, 1) < (roadside ? .76f : .65f);
            string shrubFamily = Range(0, 1) < .55f ? "Brittlebush" : "Orangili";
            var primary = blooming ? palette : bushes.Where(p => p.name.StartsWith(shrubFamily)).ToArray();
            var fringe = blooming ? dry : bushes.Where(p => p.name.StartsWith("Orangili")).ToArray();
            var positions = new List<Vector2>();
            var accepted = new List<(GameObject prefab, float scale, Vector3 position, Quaternion rotation)>();
            int coreTarget = random.Next(18, 25), target = coreTarget + random.Next(5, 8);
            // Grow connected, overlapping foliage from a core; roots still have separate contact points.
            for (int attempt = 0; attempt < target * 18 && positions.Count < target; attempt++) {
                bool edge = positions.Count >= coreTarget;
                float angle = Range(0, Mathf.PI * 2);
                var p = positions.Count == 0 ? center : positions[random.Next(positions.Count)] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(.32f, edge ? .68f : .6f);
                var offset = p - center;
                float u = Vector2.Dot(offset, tangent) / length, v = (Vector2.Dot(offset, normal) - Mathf.Sin(u * 3) * .22f) / width;
                if (u * u + v * v > (edge ? 1.6f : 1f)) continue;
                var prefab = Pick(edge && Range(0, 1) < .55f ? fringe : primary);
                float scale = Range(.8f, 1.12f) * (edge ? Range(.6f, .83f) : Mathf.Lerp(1, .65f, Mathf.Clamp01(u * u + v * v)));
                if (!prefabBounds.TryGetValue(prefab, out var b)) {
                    b = BoundsOf(prefab.GetComponentsInChildren<Renderer>(true)); prefabBounds.Add(prefab, b);
                }
                float radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * scale, .12f, .8f);
                float rootSpacing = edge ? .38f : .3f;
                if (!Clear(p, radius) || Nearby(p, .4f) != 0 || positions.Any(q => (q - p).sqrMagnitude < rootSpacing * rootSpacing)) continue;
                // Consume rotation RNG here even when the whole colony is later discarded.
                accepted.Add((prefab, scale, world.Ground(p, -b.min.y * scale - .025f), Quaternion.Euler(0, Range(0, 360), 0)));
                positions.Add(p);
            }
            // A heavily clipped patch is discarded instead of leaving a few random-looking stragglers.
            if (positions.Count < 16) return false;
            var group = new GameObject((roadside ? "Roadside" : "Rock foot") + " colony " + (colonyCount + 1)).transform;
            group.SetParent(dressingRoot, false); group.position = groupPosition;
            group.gameObject.layer = dressingRoot.gameObject.layer;
            foreach (var item in accepted) {
                var go = Instantiate(item.prefab, group); go.name = item.prefab.name;
                go.transform.localScale = item.prefab.transform.localScale * item.scale;
                go.transform.SetPositionAndRotation(item.position, item.rotation);
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = dressingRoot.gameObject.layer;
                foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var p in positions) AddPlant(p);
            plantCount += positions.Count;
            colonies.Add(center); colonyCount++; if (roadside) roadsideColonies++; else rockColonies++;
            return true;
        }

        bool Clear(Vector2 p, float radius)
        {
            float half = world.MapSize * .5f - 5;
            if (Mathf.Abs(p.x) + radius > half || Mathf.Abs(p.y) + radius > half || world.TrailDistance(p, out _, out _) < radius + .35f || world.layout.RoomDistance(p, out _) < radius + .15f) return false;
            var origin = world.surface.transform.position; var size = world.surface.terrainData.size;
            if (world.surface.terrainData.GetSteepness((p.x - origin.x) / size.x, (p.y - origin.z) / size.z) > 30) return false;
            visitedSolids.Clear();
            var start = SolidCell(p - Vector2.one * (radius + .13f)); var end = SolidCell(p + Vector2.one * (radius + .13f));
            for (int z = start.y; z <= end.y; z++) for (int x = start.x; x <= end.x; x++) {
                if (!solidCells.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                foreach (int i in list) {
                    if (!visitedSolids.Add(i)) continue;
                    var solid = solids[i]; var b = solid.bounds;
                    float dx = Mathf.Max(b.min.x - p.x, 0, p.x - b.max.x), dz = Mathf.Max(b.min.z - p.y, 0, p.y - b.max.z);
                    if (dx * dx + dz * dz >= (radius + .12f) * (radius + .12f)) continue;
                    if (solid.outline == null || solid.outline.Length < 3 || MojavePatch.OutlineDistance(solid.outline, p) < radius + .12f) return false;
                }
            }
            // Reject abrupt root contact; do not sink the entire plant to fit a steep bank.
            float h = world.Ground(p).y, foot = Mathf.Min(.16f, radius);
            foreach (var dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                if (Mathf.Abs(world.Ground(p + dir * foot).y - h) > .08f) return false;
            return true;
        }
        static Bounds BoundsOf(Renderer[] rs) { var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); return b; }
        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        static Vector2Int SolidCell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / 8), Mathf.FloorToInt(p.y / 8));
        static Vector2Int Cell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / 3), Mathf.FloorToInt(p.y / 3));
        void AddPlant(Vector2 p) { var cell = Cell(p); if (!plants.TryGetValue(cell, out var list)) plants[cell] = list = new List<Vector2>(); list.Add(p); }
        int Nearby(Vector2 p, float radius) {
            int count = 0; var a = Cell(p - Vector2.one * radius); var b = Cell(p + Vector2.one * radius);
            for (int z = a.y; z <= b.y; z++) for (int x = a.x; x <= b.x; x++)
                if (plants.TryGetValue(new Vector2Int(x, z), out var list)) foreach (var q in list) if ((p - q).sqrMagnitude < radius * radius) count++;
            return count;
        }
        float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        GameObject Pick(GameObject[] list) => list[random.Next(list.Length)];
    }
}
