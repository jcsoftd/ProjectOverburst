using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Author platform-local candidates only. The assembled run still owns entry/bridge/roster checks.
public static class CavePlatformSpawnBuilder
{
    public const string Version = "CaveSpawn.v4:1m-five-samples:rim72+.01+continuous-outline.002:nav.32/1.2/.12/128:body.05/.12";
    public static CavePlatformLibrary Library => AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(CavePlatformMapBuilder.LibraryPath);

    [MenuItem("Tools/OVERBURST/Caves/Bake platform spawn positions")]
    public static void BakeMenu() => Bake(Path.Combine(CavePlatformMapBuilder.Output, "SpawnPositions"));

    public static CavePlatformLibrary.SpawnBody[] Bodies()
    {
        var result = new List<CavePlatformLibrary.SpawnBody>();
        foreach (var definition in MapThemeCatalog.Tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct().OrderBy(d => d.EnemyId))
        {
            var body = MapCombatGeometry.BodyFor(definition);
            var existing = result.Find(b => Mathf.Abs(b.radius - body.Radius) < .00001f && Mathf.Abs(b.height - body.Height) < .00001f);
            if (existing == null)
            {
                existing = new CavePlatformLibrary.SpawnBody { radius = body.Radius, height = body.Height, definitions = Array.Empty<EnemyDefinition>() };
                result.Add(existing);
            }
            existing.definitions = existing.definitions.Concat(new[] { definition }).ToArray();
        }
        if (result.Count == 0 || result.Count > 63) throw new InvalidOperationException("Spawn bake supports 1..63 current collision shapes.");
        return result.ToArray();
    }

    public static string Signature(CavePlatformLibrary library, CavePlatformLibrary.SpawnBody[] bodies)
    {
        var settings = NavMesh.GetSettingsByID(0);
        return Hash128.Compute(Version + JsonConvert.SerializeObject(new {
            settings.agentTypeID, settings.agentHeight, settings.agentSlope,
            platforms = library.platforms.Select(p => new { prefab = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(p.prefab)).ToString(), source = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(p.source)).ToString() }),
            bodies = bodies.Select(b => new { b.radius, b.height, definitions = b.definitions.Select(d => new { id = d.EnemyId, guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(d)) }) })
        })).ToString();
    }

    public sealed class Probe : IDisposable
    {
        public readonly GameObject root;
        public readonly CavePlatformBoundary boundary;
        public readonly Collider[] colliders;
        public readonly Bounds bounds;
        readonly Collider[] floors;
        readonly Scene scene;
        readonly CapsuleCollider capsule;
        NavMeshData navigation;
        NavMeshDataInstance navigationInstance;
        public Vector3 center;
        public int connectedCells;

        public Probe(CavePlatformLibrary.Platform platform)
        {
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(platform.prefab, scene);
                root.transform.SetPositionAndRotation(new Vector3(10000, 500, 10000), Quaternion.identity);
                if (root.transform.localScale != Vector3.one) throw new InvalidOperationException("Rebake policy needs review for a scaled platform: " + root.name);
                boundary = root.GetComponent<CavePlatformBoundary>();
                if (!boundary || boundary.Data.loops.Length == 0) throw new InvalidOperationException("Missing authored boundary: " + root.name);
                colliders = root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
                floors = colliders.Where(c => c.GetComponentInParent<CaveWalkSurface>()).ToArray();
                if (floors.Length == 0) throw new InvalidOperationException("Missing walk-surface colliders: " + root.name);
                Physics.SyncTransforms();
                bounds = colliders[0].bounds;
                foreach (var c in colliders.Skip(1)) bounds.Encapsulate(c.bounds);
                var go = new GameObject("Owned spawn capsule");
                SceneManager.MoveGameObjectToScene(go, scene);
                capsule = go.AddComponent<CapsuleCollider>(); capsule.direction = 1; capsule.isTrigger = true;
            }
            catch { Dispose(); throw; }
        }

        public bool Floor(Vector3 near, float margin, out Vector3 point)
        {
            point = default;
            if (!boundary.Sample(near, 3, 3, out var surface)) return false;
            float best = float.MaxValue; bool found = false;
            var ray = new Ray(surface + Vector3.up * .4f, Vector3.down);
            foreach (var collider in floors)
                if (collider.Raycast(ray, out var hit, 1.3f) && hit.normal.y >= .7f && Mathf.Abs(hit.point.y - surface.y) < best)
                { point = hit.point; best = Mathf.Abs(hit.point.y - surface.y); found = true; }
            if (!found || best > .3f) return false;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                if (!boundary.Sample(point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * margin, .55f, .55f, out _)) return false;
            }
            return true;
        }

        public bool Clear(Vector3 point, float bodyRadius, float bodyHeight, float padding = .05f)
        {
            // Bake 1cm beyond the runtime's .04m padding to absorb rotated mesh/float contact rounding.
            float radius = bodyRadius + padding;
            float bottom = radius + .12f, top = Mathf.Max(bottom, bodyHeight - radius);
            capsule.radius = radius; capsule.height = top - bottom + radius * 2; capsule.center = Vector3.up * ((top + bottom) * .5f);
            var volume = new Bounds(point + capsule.center, new Vector3(radius * 2, capsule.height, radius * 2));
            foreach (var collider in colliders)
                if (collider.bounds.Intersects(volume) && Physics.ComputePenetration(capsule, point, Quaternion.identity, collider, collider.transform.position, collider.transform.rotation, out _, out _)) return false;
            return true;
        }

        public float RimDistance(Vector3 floor)
        {
            var local = root.transform.InverseTransformPoint(floor); var point = new Vector2(local.x, local.z);
            float closest = float.MaxValue;
            foreach (var loop in boundary.Data.loops)
                for (int i = 0; i < loop.points.Length; i++)
                {
                    var v = loop.points[i]; var w = loop.points[(i + 1) % loop.points.Length];
                    var a = new Vector2(v.x, v.z); var delta = new Vector2(w.x - v.x, w.z - v.z);
                    float along = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0;
                    closest = Mathf.Min(closest, (point - a - delta * along).sqrMagnitude);
                }
            return Mathf.Sqrt(closest);
        }

        public bool SupportedRing(Vector3 floor, float margin)
        {
            // Height breaks inside a multi-level platform also need support between the runtime's eight probes.
            for (int i = 0; i < 72; i++)
            {
                float angle = i * Mathf.PI / 36;
                var edge = floor + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (margin + .01f);
                if (!boundary.Sample(edge, .55f, .55f, out _)) return false;
            }
            return true;
        }

        public void Navigation(CavePlatformLibrary.Platform platform)
        {
            var sources = new List<NavMeshBuildSource>();
            // Editor stage collection includes isolated preview scenes, as NavMeshSurface does.
            UnityEditor.AI.NavMeshEditorHelpers.CollectSourcesInStage(root.transform, ~0, NavMeshCollectGeometry.PhysicsColliders,
                0, false, new List<NavMeshBuildMarkup>(), false, root.scene, sources);
            if (sources.Count == 0) throw new InvalidOperationException("No preview-stage navigation sources: " + root.name);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = .32f; settings.agentClimb = 1.2f;
            settings.overrideVoxelSize = true; settings.voxelSize = .12f; settings.overrideTileSize = true; settings.tileSize = 128;
            var buildBounds = bounds; buildBounds.Expand(4);
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources, buildBounds, Vector3.zero, Quaternion.identity);
            if (!navigation) throw new InvalidOperationException("Platform navigation bake failed: " + root.name);
            navigationInstance = NavMesh.AddNavMeshData(navigation);
            var representatives = new List<Vector3>(); var nearest = new List<Vector3>(); var counts = new List<int>();
            var path = new NavMeshPath(); var desired = root.transform.TransformPoint(platform.spawn);
            foreach (var local in Grid(platform, false))
            {
                if (!Floor(root.transform.TransformPoint(local), .4f, out var floor) || !Clear(floor, .35f, 1.8f) || !NavMesh.SamplePosition(floor, out var nav, .18f, NavMesh.AllAreas)) continue;
                int group = 0;
                for (; group < representatives.Count; group++)
                    if (NavMesh.CalculatePath(representatives[group], nav.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) break;
                if (group == representatives.Count) { representatives.Add(nav.position); nearest.Add(nav.position); counts.Add(0); }
                counts[group]++;
                if ((nav.position - desired).sqrMagnitude < (nearest[group] - desired).sqrMagnitude) nearest[group] = nav.position;
            }
            if (counts.Count == 0) throw new InvalidOperationException("No connected combat floor: " + root.name);
            int best = counts.IndexOf(counts.Max()); connectedCells = counts[best]; center = nearest[best];
            if (connectedCells < 6) throw new InvalidOperationException("Insufficient connected combat floor: " + root.name);
        }

        public bool Connected(Vector3 point, NavMeshPath path)
            => NavMesh.SamplePosition(point, out var nav, .18f, NavMesh.AllAreas) && NavMesh.CalculatePath(center, nav.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;

        public void Dispose()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation) Object.DestroyImmediate(navigation);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static IEnumerable<Vector3> Grid(CavePlatformLibrary.Platform platform, bool subcells)
    {
        var vertices = platform.surfaceTriangles;
        int left = Mathf.FloorToInt(vertices.Min(v => v.x)), right = Mathf.CeilToInt(vertices.Max(v => v.x));
        int bottom = Mathf.FloorToInt(vertices.Min(v => v.z)), top = Mathf.CeilToInt(vertices.Max(v => v.z));
        for (int z = bottom; z < top; z++)
            for (int x = left; x < right; x++)
                for (int sample = 0; sample < (subcells ? 5 : 1); sample++)
                    yield return new Vector3(x + .5f, platform.spawn.y, z + .5f) + (sample == 0 ? Vector3.zero : new Vector3((sample & 1) == 0 ? -.25f : .25f, 0, sample <= 2 ? -.25f : .25f));
    }

    public static object Bake(string output)
    {
        CavePlatformMapBuilder.Guard();
        Directory.CreateDirectory(output);
        var library = Library; var bodies = Bodies(); var signature = Signature(library, bodies);
        var placements = new List<CavePlatformLibrary.SpawnPoint[]>(); var results = new List<object>();
        for (int i = 0; i < library.platforms.Length; i++)
        {
            var platform = library.platforms[i]; var points = new List<CavePlatformLibrary.SpawnPoint>();
            if (platform.source.GetComponent<CavePlatformBoundary>().excludeMonsters)
            {
                placements.Add(Array.Empty<CavePlatformLibrary.SpawnPoint>());
                results.Add(new { name = platform.prefab.name, excluded = true, points = 0 });
                continue;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew(); int tested = 0;
            using (var probe = new Probe(platform))
            {
                probe.Navigation(platform); var path = new NavMeshPath();
                foreach (var local in Grid(platform, true))
                {
                    tested++;
                    var near = probe.root.transform.TransformPoint(local);
                    if (!probe.Floor(near, 0, out var floor) || !probe.Connected(floor, path)) continue;
                    // A circular footprint must fit at every platform yaw, between the eight rim probes too.
                    float rimDistance = probe.RimDistance(floor);
                    var support = new Dictionary<float, bool>();
                    long mask = 0;
                    for (int body = 0; body < bodies.Length; body++)
                    {
                        var shape = bodies[body];
                        if (rimDistance < shape.radius + .102f) continue;
                        if (!support.TryGetValue(shape.radius, out bool supported))
                        {
                            supported = probe.SupportedRing(floor, shape.radius + .1f);
                            support.Add(shape.radius, supported);
                        }
                        if (supported && probe.Floor(floor, shape.radius + .1f, out var bodyFloor)
                            && probe.Clear(bodyFloor, shape.radius, shape.height)) mask |= 1L << body;
                    }
                    if (mask != 0) points.Add(new CavePlatformLibrary.SpawnPoint { position = probe.root.transform.InverseTransformPoint(floor), bodyMask = mask });
                }
                if (points.Count == 0 || Enumerable.Range(0, bodies.Length).Any(b => !points.Any(p => (p.bodyMask & (1L << b)) != 0))) throw new InvalidOperationException("Platform has no positions for a current body: " + platform.prefab.name);
                results.Add(new { name = platform.prefab.name, tested, points = points.Count, probe.connectedCells, seconds = watch.Elapsed.TotalSeconds,
                    bodyCounts = Enumerable.Range(0, bodies.Length).Select(b => points.Count(p => (p.bodyMask & (1L << b)) != 0)).ToArray() });
            }
            placements.Add(points.ToArray());
            File.WriteAllText(Path.Combine(output, "bake-progress.json"), JsonConvert.SerializeObject(new { status = "RUNNING", completed = i + 1, total = library.platforms.Length, platforms = results }, Formatting.Indented));
        }
        // Publish the bank only after every platform passes, preserving geometry and references.
        Undo.RecordObject(library, "동굴 플랫폼 몬스터 배치 후보 베이크");
        library.spawnBodies = bodies; library.spawnBakeSignature = signature;
        for (int i = 0; i < library.platforms.Length; i++) library.platforms[i].spawnPoints = placements[i];
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
        var result = new { status = "PASS", signature, profiles = bodies.Length, definitions = bodies.Sum(b => b.definitions.Length), platforms = results };
        File.WriteAllText(Path.Combine(output, "bake-result.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        return result;
    }
}
