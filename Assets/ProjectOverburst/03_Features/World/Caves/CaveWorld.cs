using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Overburst.Caves
{
    public sealed class CaveWorld : MonoBehaviour
    {
        [Serializable] public sealed class Court
        {
            public string name;
            public Vector3 center;
            public float radius;
            public CaveTile tile;
        }
        [Serializable] public sealed class Passage
        {
            public int a, b;
            public Vector3[] points;
            public float[] widths;
        }
        public CaveCatalog catalog;
        public bool authoredLayout;
        public int seed = 87443;
        [Range(4, 12)] public int combatCount = 6;
        [Range(140, 512)] public float mapSize = 200;
        public Transform generatedRoot;
        public List<Court> courts = new List<Court>();
        public List<Passage> passages = new List<Passage>();
        public int backgroundCount;
        public long generationMilliseconds;
        readonly List<Mesh> ownedMeshes = new List<Mesh>();

        void OnEnable() => CaveFallProtection.Register(this);
        void OnDisable() => CaveFallProtection.Unregister(this);

        public void Generate(int nextSeed)
        {
            if (!catalog || !catalog.bridge || !catalog.abyssMaterial || catalog.combatTiles == null || catalog.combatTiles.Length != 6 || catalog.backgroundTiles == null || catalog.backgroundTiles.Length < 4)
                throw new InvalidOperationException("The tuned cave tile library is required.");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (generatedRoot) { generatedRoot.gameObject.SetActive(false); Dispose(generatedRoot.gameObject); }
            foreach (var mesh in ownedMeshes) if (mesh) Dispose(mesh);
            ownedMeshes.Clear(); courts.Clear(); passages.Clear(); backgroundCount = 0;
            seed = nextSeed; combatCount = Mathf.Clamp(combatCount, 4, 12); mapSize = Mathf.Clamp(mapSize, 140, 512);
            generatedRoot = new GameObject("Generated cave expedition").transform;
            generatedRoot.SetParent(transform, false);
            var random = new System.Random(seed);
            float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            int rows = Mathf.CeilToInt(combatCount / 2f);
            mapSize = Mathf.Max(mapSize, (rows - 1) * 66 + 65);
            float rowSpacing = Mathf.Max(66, (mapSize - 65) / (rows - 1));
            float columnSpacing = Mathf.Max(58, mapSize * .39f);
            for (int i = 0; i < combatCount; i++)
            {
                int row = i / 2, column = i % 2;
                float height = 18 + row * 5 + column * 3;
                var position = new Vector3((column - .5f) * columnSpacing + Range(-5, 5), height,
                    (row - (rows - 1) * .5f) * rowSpacing + Range(-5, 5));
                var instance = Instantiate(catalog.combatTiles[i % 6], generatedRoot);
                instance.name = "Court " + (i + 1) + " · " + catalog.combatTiles[i % 6].name;
                instance.transform.localPosition = position;
                instance.transform.localRotation = Quaternion.Euler(0, random.Next(4) * 90, 0);
                ExtendColumn(instance.transform);
                var tile = instance.GetComponent<CaveTile>();
                courts.Add(new Court { name = instance.name, center = position, radius = tile.combatRadius, tile = tile });
            }
            Physics.SyncTransforms();
            for (int i = 0; i < courts.Count; i++)
            {
                if (i % 2 == 0 && i + 1 < courts.Count) Connect(i, i + 1, random);
                if (i + 2 < courts.Count) Connect(i, i + 2, random);
            }
            for (int attempt = 0; attempt < 500 && backgroundCount < 24; attempt++)
            {
                var prefab = catalog.backgroundTiles[attempt % catalog.backgroundTiles.Length];
                float radius = prefab.GetComponent<CaveTile>().footprintRadius;
                var point = new Vector2(Range(-mapSize * .63f, mapSize * .63f), Range(-mapSize * .59f, mapSize * .59f));
                if (courts.Any(c => Vector2.Distance(point, XZ(c.center)) < c.tile.footprintRadius + radius + 3)) continue;
                if (passages.Any(p => DistanceToPassage(point, p) < radius + 5)) continue;
                bool overlap = false;
                foreach (Transform child in generatedRoot)
                {
                    var tile = child.GetComponent<CaveTile>();
                    if (tile && !tile.combat && Vector2.Distance(point, XZ(child.position)) < radius + tile.footprintRadius + 1)
                        overlap = true;
                }
                if (overlap) continue;
                var go = Instantiate(prefab, generatedRoot);
                go.transform.position = new Vector3(point.x, Range(5, 14), point.y);
                go.transform.rotation = Quaternion.Euler(0, Range(0, 360), 0);
                ExtendColumn(go.transform);
                backgroundCount++;
            }
            // A distant, non-colliding dark bed closes the bottoms of the rock columns.
            var abyss = GameObject.CreatePrimitive(PrimitiveType.Plane);
            abyss.name = "Deep cave bed · scenery only"; abyss.transform.SetParent(generatedRoot, false);
            abyss.transform.localPosition = new Vector3(0, -18, 0);
            abyss.transform.localScale = Vector3.one * mapSize * .2f;
            abyss.GetComponent<Collider>().enabled = false;
            abyss.GetComponent<Renderer>().sharedMaterial = catalog.abyssMaterial;
            Physics.SyncTransforms();
            generationMilliseconds = clock.ElapsedMilliseconds;
        }

        void ExtendColumn(Transform tile)
        {
            var floor = tile.Find("Platform surface"); if (!floor) return;
            var filter = floor.GetComponentInChildren<MeshFilter>();
            var mesh = Instantiate(filter.sharedMesh); mesh.name = "Grounded " + tile.name;
            var vertices = mesh.vertices; float bottom = vertices.Min(v => v.y);
            float depth = tile.position.y + 18;
            for (int i = 0; i < vertices.Length; i++)
                if (vertices[i].y < -2) vertices[i].y = -2 + (vertices[i].y + 2) * ((depth - 2) / Mathf.Max(1, -bottom - 2));
            mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); ownedMeshes.Add(mesh);
            filter.sharedMesh = mesh; floor.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        void Connect(int a, int b, System.Random random)
        {
            var ca = courts[a]; var cb = courts[b];
            var direction = (XZ(cb.center) - XZ(ca.center)).normalized;
            var side = new Vector2(-direction.y, direction.x);
            var start = ca.center + new Vector3(direction.x, 0, direction.y) * (ca.radius - 1.2f);
            var end = cb.center - new Vector3(direction.x, 0, direction.y) * (cb.radius - 1.2f);
            float length = Vector2.Distance(XZ(start), XZ(end));
            float bend = ((float)random.NextDouble() - .5f) * 5;
            int count = Mathf.Max(12, Mathf.CeilToInt(length / .7f));
            var points = new Vector3[count + 1]; var widths = new float[count + 1];
            float phase = (float)random.NextDouble() * 6;
            for (int i = 0; i <= count; i++)
            {
                float t = i / (float)count;
                points[i] = Vector3.Lerp(start, end, t) + new Vector3(side.x, 0, side.y) * (Mathf.Sin(t * Mathf.PI) * bend);
                points[i].y += Mathf.Sin(t * Mathf.PI) * -.3f;
                widths[i] = 4.9f + .35f * Mathf.Sin(t * Mathf.PI * 2 + phase);
            }
            // Hold the deck level over each real platform footprint, including the
            // character's approach margin, so its vertical cliff never forms a step.
            var floorA = ca.tile.transform.Find("Platform surface").GetComponent<MeshCollider>();
            var floorB = cb.tile.transform.Find("Platform surface").GetComponent<MeshCollider>();
            bool OverFloor(MeshCollider floor, int i, float height)
            {
                for (int lateral = -1; lateral <= 1; lateral++)
                {
                    var p = points[i] + new Vector3(side.x, 0, side.y) * widths[i] * .5f * lateral;
                    p.y = height + 2;
                    if (floor.Raycast(new Ray(p, Vector3.down), out var hit, 4) && hit.normal.y > .8f) return true;
                }
                return false;
            }
            int departure = 0, arrival = count;
            for (int i = 0; i < count / 2; i++) if (OverFloor(floorA, i, ca.center.y)) departure = i;
            for (int i = count; i > count / 2; i--) if (OverFloor(floorB, i, cb.center.y)) arrival = i;
            departure = Mathf.Min(departure + 2, count / 2 - 1);
            arrival = Mathf.Max(arrival - 2, count / 2 + 1);
            for (int i = 0; i <= count; i++)
            {
                float t = Mathf.Clamp01((i - departure) / (float)(arrival - departure));
                points[i].y = Mathf.Lerp(ca.center.y, cb.center.y, t) - Mathf.Sin(t * Mathf.PI) * .18f + .08f;
            }
            var passage = new Passage { a = a, b = b, points = points, widths = widths };
            passages.Add(passage);
            var root = new GameObject("Passage " + (a + 1) + "—" + (b + 1)).transform;
            root.SetParent(generatedRoot, false); root.gameObject.AddComponent<CaveWalkSurface>();
            var source = catalog.bridge.GetComponentInChildren<MeshFilter>();
            var sourceRenderer = source.GetComponent<Renderer>();
            var sourceMesh = source.sharedMesh; var bounds = sourceMesh.bounds;
            float timberTop = sourceMesh.vertices.Max(v => source.transform.TransformPoint(v).y - catalog.bridge.transform.position.y);
            int modules = Mathf.Max(1, Mathf.CeilToInt(length / 11));
            for (int module = 0; module < modules; module++)
            {
                var mesh = Instantiate(sourceMesh); mesh.name = "Cave timber span " + a + " " + b + " " + module;
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var local = source.transform.TransformPoint(vertices[i]) - catalog.bridge.transform.position;
                    float u = Mathf.Clamp01((vertices[i].x - bounds.min.x) / bounds.size.x);
                    float t = (module + u) / modules;
                    var p = Sample(passage, t, out var forward, out float width);
                    var right = new Vector3(-forward.z, 0, forward.x).normalized;
                    vertices[i] = p + right * (vertices[i].z / bounds.size.z * width) + Vector3.up * (local.y - timberTop + .03f);
                }
                mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); ownedMeshes.Add(mesh);
                var go = new GameObject("Timber " + module); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
            }
            if (catalog.bridgeSupport)
            {
                int supports = Mathf.Max(1, Mathf.FloorToInt(length / 18));
                for (int i = 1; i <= supports; i++)
                {
                    var point = Sample(passage, i / (float)(supports + 1), out _, out _);
                    var support = Instantiate(catalog.bridgeSupport, root); support.name = "Bridge rock pier " + i;
                    support.transform.localScale *= .48f;
                    var renderers = support.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                    var bnd = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bnd.Encapsulate(renderer.bounds);
                    var scale = support.transform.localScale; scale.y *= (point.y + 18 - .3f) / bnd.size.y; support.transform.localScale = scale;
                    bnd = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bnd.Encapsulate(renderer.bounds);
                    support.transform.position += new Vector3(point.x - bnd.center.x, point.y - .3f - bnd.max.y, point.z - bnd.center.z);
                }
            }
            var strip = new Mesh { name = "Cave continuous passage support " + a + " " + b };
            var v = new Vector3[points.Length * 2]; var triangles = new int[(points.Length - 1) * 6];
            for (int i = 0; i < points.Length; i++)
            {
                var d = points[Mathf.Min(i + 1, count)] - points[Mathf.Max(0, i - 1)];
                var right = new Vector3(-d.z, 0, d.x).normalized;
                v[i * 2] = points[i] - right * widths[i] * .49f; v[i * 2 + 1] = points[i] + right * widths[i] * .49f;
                if (i == count) continue;
                int j = i * 6, n = i * 2;
                triangles[j] = n; triangles[j + 1] = n + 1; triangles[j + 2] = n + 2;
                triangles[j + 3] = n + 1; triangles[j + 4] = n + 3; triangles[j + 5] = n + 2;
            }
            strip.vertices = v; strip.triangles = triangles; strip.RecalculateNormals(); strip.RecalculateBounds(); ownedMeshes.Add(strip);
            root.gameObject.AddComponent<MeshCollider>().sharedMesh = strip;
        }

        public static Vector3 Sample(Passage passage, float t, out Vector3 direction, out float width)
        {
            float f = Mathf.Clamp01(t) * (passage.points.Length - 1); int i = Mathf.Min(Mathf.FloorToInt(f), passage.points.Length - 2);
            direction = (passage.points[i + 1] - passage.points[i]).normalized;
            width = Mathf.Lerp(passage.widths[i], passage.widths[i + 1], f - i);
            return Vector3.Lerp(passage.points[i], passage.points[i + 1], f - i);
        }
        public static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        public static float DistanceToPassage(Vector2 point, Passage passage)
        {
            float result = float.MaxValue;
            for (int i = 1; i < passage.points.Length; i++)
            {
                var a = XZ(passage.points[i - 1]); var d = XZ(passage.points[i]) - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, d) / Mathf.Max(.0001f, d.sqrMagnitude));
                result = Mathf.Min(result, Vector2.Distance(point, a + d * t));
            }
            return result;
        }
        public bool Ground(Vector3 near, out RaycastHit result, float tolerance = 2)
        {
            result = default; float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(near + Vector3.up * tolerance, Vector3.down, tolerance * 2 + .5f, ~0, QueryTriggerInteraction.Ignore))
            {
                var floor = hit.collider.GetComponentInParent<CaveWalkSurface>();
                if (!floor || !floor.transform.IsChildOf(generatedRoot) || hit.normal.y < .7f) continue;
                float delta = Mathf.Abs(hit.point.y - near.y);
                if (delta < best) { best = delta; result = hit; }
            }
            return best < float.MaxValue;
        }
        public bool CanStand(Vector3 point, float margin = .45f)
        {
            if (!Ground(point, out var center)) return false;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                if (!Ground(center.point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * margin, out var edge) || Mathf.Abs(edge.point.y - center.point.y) > .6f) return false;
            }
            return true;
        }
        bool ClearLine(Vector3 a, Vector3 b)
        {
            int count = Mathf.CeilToInt(Vector3.Distance(a, b) / .65f);
            for (int i = 0; i <= count; i++) if (!CanStand(Vector3.Lerp(a, b, i / (float)Mathf.Max(1, count)))) return false;
            return true;
        }
        public List<Vector3> FindRoute(Vector3 from, Vector3 target)
        {
            if (authoredLayout)
            {
                var path = new UnityEngine.AI.NavMeshPath();
                if (!UnityEngine.AI.NavMesh.SamplePosition(from, out var startHit, 2, UnityEngine.AI.NavMesh.AllAreas) ||
                    !UnityEngine.AI.NavMesh.SamplePosition(target, out var endHit, 2, UnityEngine.AI.NavMesh.AllAreas) ||
                    !UnityEngine.AI.NavMesh.CalculatePath(startHit.position, endHit.position, UnityEngine.AI.NavMesh.AllAreas, path) ||
                    path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) return new List<Vector3>();
                return path.corners.Skip(1).ToList();
            }
            if (!CanStand(target)) return new List<Vector3>();
            if (ClearLine(from, target)) return new List<Vector3> { target };
            var nodes = courts.Select(c => c.center).ToList(); var links = new List<(int a, int b)>();
            foreach (var p in passages)
            {
                int previous = p.a;
                for (int i = 0; i < p.points.Length; i += 4)
                { int index = nodes.Count; nodes.Add(p.points[i]); links.Add((previous, index)); previous = index; }
                int last = nodes.Count; nodes.Add(p.points[p.points.Length - 1]); links.Add((previous, last)); links.Add((last, p.b));
            }
            int start = nodes.Count; nodes.Add(from); int end = nodes.Count; nodes.Add(target);
            foreach (int extra in new[] { start, end })
            {
                int connected = 0;
                foreach (int i in Enumerable.Range(0, start).OrderBy(i => Vector3.SqrMagnitude(nodes[i] - nodes[extra])))
                    if (ClearLine(nodes[extra], nodes[i])) { links.Add((extra, i)); if (++connected == 3) break; }
                if (connected == 0) return new List<Vector3>();
            }
            var cost = Enumerable.Repeat(float.MaxValue, nodes.Count).ToArray(); var previousNode = Enumerable.Repeat(-1, nodes.Count).ToArray();
            var open = new HashSet<int> { start }; cost[start] = 0;
            while (open.Count > 0)
            {
                int current = open.OrderBy(i => cost[i] + Vector3.Distance(nodes[i], target)).First(); open.Remove(current);
                if (current == end)
                { var path = new List<Vector3>(); for (int i = end; i != start; i = previousNode[i]) path.Add(nodes[i]); path.Reverse(); return path; }
                foreach (var edge in links)
                {
                    int next = edge.a == current ? edge.b : edge.b == current ? edge.a : -1; if (next < 0) continue;
                    float candidate = cost[current] + Vector3.Distance(nodes[current], nodes[next]);
                    if (candidate >= cost[next]) continue; cost[next] = candidate; previousNode[next] = current; open.Add(next);
                }
            }
            return new List<Vector3>();
        }
        static void Dispose(UnityEngine.Object obj) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
