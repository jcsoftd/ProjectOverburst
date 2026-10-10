using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Overburst.Caves;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

public class CavePlatformMapBuilder : CavePlatformLayout
{
    public const string Root = "Assets/ProjectOverburst/04_Contents/World/Caves/Procedural";
    public const string SceneRoot = "Assets/ProjectOverburst/00_Scenes/World/Caves";
    public const string LibraryPath = Root + "/PlatformLibrary.asset";
    public const string Source = "Assets/ProjectOverburst/04_Contents/World/Caves/Extracted/Demo1/Platforms";
    public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/World/CavesGeneration"));
    sealed class Marker
    {
        public Transform transform;
        public Vector3 anchor;
        public float angle;
        public bool stone;
        public float pitch;
        public int stairDirection;
    }
    public static void Guard()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Unity is busy. Retry in Edit Mode after compilation/import.");
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.scene.isDirty) throw new InvalidOperationException("Save the currently edited prefab before generating.");
    }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
    static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    static Bounds BoundsOf(IEnumerable<Renderer> renderers)
    { var array = renderers.ToArray(); var bounds = array[0].bounds; foreach (var r in array.Skip(1)) bounds.Encapsulate(r.bounds); return bounds; }
    static bool FloorHit(MeshCollider[] floors, Vector3 near, out Vector3 point)
    {
        point = default; float highest = float.MinValue;
        foreach (var f in floors)
            if (f.Raycast(new Ray(new Vector3(near.x, 50, near.z), Vector3.down), out var hit, 150) && hit.normal.y > .6f && hit.point.y > highest)
            { highest = hit.point.y; point = hit.point; }
        return highest > float.MinValue;
    }
    static void RestoreMaterials(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
            {
                if (!m) return m;
                string path = AssetDatabase.GetAssetPath(m), original = null;
                if (path.Contains("Bridge_Cyan_98325")) original = "Assets/ThirdParty/04_환경맵/TopDownCaves/Prefabs/Origin/Models/Materials/Wood_elements1.mat";
                if (path.Contains("Stair_Yellow_2526")) original = "Assets/ThirdParty/04_환경맵/TopDownCaves/Prefabs/Origin/Models/Materials/Stairs_elements.mat";
                if (path.Contains("Bridge_Cyan_30f66")) original = "Assets/ThirdParty/04_환경맵/TopDownCaves/Prefabs/Origin/collision.mat";
                return original == null ? m : AssetDatabase.LoadAssetAtPath<Material>(original) ?? throw new IOException(original);
            }).ToArray();
            if (renderer.sharedMaterials.Any(m => m && m.name.IndexOf("collision", StringComparison.OrdinalIgnoreCase) >= 0)) renderer.enabled = false;
        }
    }

    static CavePlatformLibrary.Region FitRegion(Marker[] guides, Marker[] allGuides, GameObject template, int index)
    {
        float heading = (guides.First().angle + guides.Last().angle) * .5f;
        var average = guides.Aggregate(Vector3.zero, (p, g) => p + g.anchor) / guides.Length;
        var fallback = XZ(average - Direction(heading) * 3);
        // A small regularizer keeps nearly parallel hand-placed guides from producing a distant pivot.
        float xx = .04f, xz = 0, zz = .04f, bx = fallback.x * .04f, bz = fallback.y * .04f;
        foreach (var guide in guides)
        {
            var d = XZ(Direction(guide.angle)); var n = new Vector2(-d.y, d.x); float v = Vector2.Dot(n, XZ(guide.anchor));
            xx += n.x * n.x; xz += n.x * n.y; zz += n.y * n.y; bx += n.x * v; bz += n.y * v;
        }
        float determinant = xx * zz - xz * xz;
        var fitted = new Vector2((bx * zz - bz * xz) / determinant, (bz * xx - bx * xz) / determinant);
        var forward = XZ(Direction(heading)); var lateral = new Vector2(-forward.y, forward.x); var offset = fitted - XZ(average);
        fitted = XZ(average) - forward * Mathf.Clamp(-Vector2.Dot(offset, forward), 1.5f, 8) + lateral * Mathf.Clamp(Vector2.Dot(offset, lateral), -2, 2);
        var pivot = new Vector3(fitted.x, average.y, fitted.y);
        var vertices = template.GetComponentsInChildren<MeshFilter>(true).Where(f => f.GetComponent<Renderer>() && f.GetComponent<Renderer>().enabled)
            .SelectMany(f => f.sharedMesh.vertices.Select(v => template.transform.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();
        var size = vertices.Aggregate(Vector3.Max) - vertices.Aggregate(Vector3.Min); bool stone = guides[0].stone;
        int stairCount = stone ? Mathf.Clamp(Mathf.RoundToInt((allGuides.Max(g => g.transform.position.y) - allGuides.Min(g => g.transform.position.y)) / 2.35f) + 1, 1, 3) : 0;
        return new CavePlatformLibrary.Region
        {
            name = "Connection " + (index + 1), stone = stone, template = template,
            anchors = guides.Select(g => g.anchor).ToArray(), angles = guides.Select(g => g.angle).ToArray(), pivot = pivot,
            heading = heading, halfAngle = Mathf.Max(5, (guides.Last().angle - guides.First().angle) * .5f),
            radius = guides.Average(g => Vector2.Distance(XZ(g.anchor), fitted)),
            guideFitError = Mathf.Sqrt(guides.Average(g => Mathf.Pow(Cross(XZ(Direction(g.angle)), fitted - XZ(g.anchor)), 2))),
            pitch = stone ? 0 : Mathf.Clamp(guides.Average(g => g.pitch), -7, 7), pitchTolerance = stone ? 0 : 5,
            authoredStairCount = stairCount, maxStairCount = stairCount > 1 ? 3 : 1,
            stairDirection = stone ? (guides.Average(g => g.stairDirection) >= 0 ? 1 : -1) : 0,
            width = stone ? 3.8f : 2.65f, deckLength = stone ? size.z : Mathf.Max(size.x, size.z)
        };
    }

    static CavePlatformLibrary.Connector MeasureConnector(GameObject prefab, bool alongZ, Scene preview)
    {
        var clone = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(clone, preview);
        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        try
        {
            RestoreMaterials(clone);
            var filters = clone.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<Renderer>() && f.GetComponent<Renderer>().enabled).ToArray();
            var vertices = filters.SelectMany(f => f.sharedMesh.vertices.Select(f.transform.TransformPoint)).ToArray();
            var min = vertices.Aggregate(Vector3.Min); var max = vertices.Aggregate(Vector3.Max);
            var colliders = filters.Select(f => { var c = f.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = f.sharedMesh; return c; }).ToArray();
            var contacts = new List<Vector3>();
            for (int i = 0; i <= 512; i++)
            {
                var p = (min + max) * .5f;
                if (alongZ) p.z = Mathf.Lerp(min.z, max.z, i / 512f); else p.x = Mathf.Lerp(min.x, max.x, i / 512f);
                p.y = max.y + 1;
                bool found = false; RaycastHit top = default;
                foreach (var c in colliders)
                    if (c.Raycast(new Ray(p, Vector3.down), out var hit, max.y - min.y + 3) && hit.normal.y > .55f && (!found || hit.point.y > top.point.y)) { top = hit; found = true; }
                if (found) contacts.Add(top.point);
            }
            if (contacts.Count < 2) throw new InvalidOperationException("No deck contacts: " + prefab.name);
            return new CavePlatformLibrary.Connector { prefab = prefab, entry = clone.transform.InverseTransformPoint(contacts.First()), exit = clone.transform.InverseTransformPoint(contacts.Last()), width = (alongZ ? max.x - min.x : max.z - min.z) * (alongZ ? .55f : .68f) };
        }
        finally { Object.DestroyImmediate(clone); }
    }

    [MenuItem("Overburst/Caves/User Platforms/1 Build library from edited prefabs")]
    public static CavePlatformLibrary BuildLibrary() => BuildLibrary(false);
    [MenuItem("Overburst/Caves/User Platforms/Refresh generated library")]
    static void RefreshLibrary() => BuildLibrary(true);
    public static CavePlatformLibrary BuildLibrary(bool updateGenerated)
    {
        Guard();
        var existing = AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(LibraryPath);
        if (existing && !updateGenerated) throw new InvalidOperationException("The generated library exists. Use Refresh generated library to apply later source edits.");
        foreach (var sub in new[] { "Platforms", "Connectors", "Scenes", "Meshes" }) Folder(Root + "/" + sub);
        Directory.CreateDirectory(Output + "/Data");
        var preview = EditorSceneManager.NewPreviewScene(); var entries = new List<CavePlatformLibrary.Platform>(); var report = new List<object>();
        try
        {
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { Source }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = Object.Instantiate(original); go.name = original.name; SceneManager.MoveGameObjectToScene(go, preview);
                try
                {
                    var floorRoot = go.transform.Find("Platform");
                    var filters = floorRoot.GetComponentsInChildren<MeshFilter>(true);
                    var floors = filters.Select(f => { var c = f.GetComponent<MeshCollider>() ?? f.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = f.sharedMesh; c.enabled = true; if (!f.GetComponent<CaveWalkSurface>()) f.gameObject.AddComponent<CaveWalkSurface>(); return c; }).ToArray();
                    var hull = Hull(filters.SelectMany(f => f.sharedMesh.vertices.Select(v => XZ(f.transform.TransformPoint(v)))));
                    var surfaceTriangles = new List<Vector3>();
                    foreach (var filter in filters)
                    {
                        var vertices = filter.sharedMesh.vertices; var indices = filter.sharedMesh.triangles;
                        for (int n = 0; n < indices.Length; n += 3)
                        {
                            var a = filter.transform.TransformPoint(vertices[indices[n]]); var b = filter.transform.TransformPoint(vertices[indices[n + 1]]); var c = filter.transform.TransformPoint(vertices[indices[n + 2]]);
                            if (Vector3.Cross(b - a, c - a).normalized.y > .6f) { surfaceTriangles.Add(a); surfaceTriangles.Add(b); surfaceTriangles.Add(c); }
                        }
                    }
                    var center = hull.Aggregate(Vector2.zero, (a, p) => a + p) / hull.Length;
                    var markers = new List<Marker>(); var keep = new List<string>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Wood_Bridge") || t.name.StartsWith("Stairs_el")))
                    {
                        if (t.name.Contains("다리아")) { keep.Add(t.name); continue; }
                        bool stone = t.name.StartsWith("Stairs");
                        var b = BoundsOf(t.GetComponentsInChildren<Renderer>(true));
                        var filter = t.GetComponentInChildren<MeshFilter>();
                        var axis = stone ? Vector3.forward : filter.sharedMesh.bounds.size.x >= filter.sharedMesh.bounds.size.z ? Vector3.right : Vector3.forward;
                        var fullDirection = t.TransformDirection(axis).normalized;
                        var d = fullDirection; d.y = 0; d.Normalize();
                        if (Vector2.Dot(XZ(d), XZ(b.center) - center) < 0) { d = -d; fullDirection = -fullDirection; }
                        if (!stone && Inside(hull, XZ(b.center)) && (EdgeDistance(hull, XZ(b.center)) > 4 || FloorHit(floors, b.center + d * 2.3f, out _) && FloorHit(floors, b.center - d * 2.3f, out _))) { keep.Add(t.name); continue; }
                        Vector3 anchor = b.center; bool found = false;
                        // Take the last real floor contact along this authored direction, not its prefab bounding-box edge.
                        for (float distance = -10; distance <= 10; distance += .15f)
                            if (FloorHit(floors, b.center + d * distance, out var hit)) { anchor = hit - d * .75f; found = true; }
                        if (!found) throw new InvalidOperationException(original.name + " connector has no floor contact: " + t.name);
                        FloorHit(floors, anchor, out anchor);
                        markers.Add(new Marker { transform = t, anchor = anchor, angle = Angle(d), stone = stone, pitch = Mathf.Asin(fullDirection.y) * Mathf.Rad2Deg, stairDirection = stone ? (Vector3.Dot(t.TransformDirection(Vector3.forward), d) > 0 ? -1 : 1) : 0 });
                    }
                    var groups = new List<List<Marker>>();
                    foreach (var marker in markers)
                    {
                        var group = groups.FirstOrDefault(g => g[0].stone == marker.stone && g.Any(m => Vector2.Distance(XZ(m.anchor), XZ(marker.anchor)) < 7.5f && Mathf.Abs(Mathf.DeltaAngle(m.angle, marker.angle)) < 85));
                        if (group == null) { group = new List<Marker>(); groups.Add(group); } group.Add(marker);
                    }
                    var regions = new List<CavePlatformLibrary.Region>();
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var group = groups[i]; float baseline = group[0].angle;
                        foreach (var marker in group) marker.angle = baseline + Mathf.DeltaAngle(baseline, marker.angle);
                        var ordered = group.OrderBy(m => m.angle).GroupBy(m => Mathf.RoundToInt(m.angle * 10)).Select(g => g.OrderBy(m => Vector2.Distance(XZ(m.transform.position), XZ(m.anchor))).First()).ToArray();
                        var template = Object.Instantiate(ordered[0].transform.gameObject); template.name = original.name + "_" + (group[0].stone ? "Stone" : "Timber") + i;
                        SceneManager.MoveGameObjectToScene(template, preview); template.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); RestoreMaterials(template);
                        GameObject saved;
                        try { saved = PrefabUtility.SaveAsPrefabAsset(template, Root + "/Connectors/" + template.name + ".prefab"); }
                        finally { Object.DestroyImmediate(template); }
                        regions.Add(FitRegion(ordered, group.ToArray(), saved, i));
                    }
                    foreach (var marker in markers) Object.DestroyImmediate(marker.transform.gameObject);
                    RestoreMaterials(go);
                    // Collider geometry distinguishes high obstacles from low rails/planks that can be stepped over.
                    // Keep the authored floor/detail meshes; choose a clear point on them for spawn/navigation.
                    Vector3 spawn = Vector3.zero; float best = float.MinValue;
                    var floorBounds = BoundsOf(floorRoot.GetComponentsInChildren<Renderer>(true));
                    for (float x = floorBounds.min.x + 1; x < floorBounds.max.x - 1; x += 1)
                        for (float z = floorBounds.min.z + 1; z < floorBounds.max.z - 1; z += 1)
                        {
                            if (!FloorHit(floors, new Vector3(x, 0, z), out var p)) continue;
                            bool supported = true;
                            for (int sample = 0; sample < 8; sample++)
                            {
                                float a = sample * Mathf.PI / 4;
                                if (!FloorHit(floors, p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .6f, out var edge) || Mathf.Abs(edge.y - p.y) > .35f) { supported = false; break; }
                            }
                            if (!supported) continue;
                            float clearance = EdgeDistance(hull, XZ(p));
                            foreach (var r in go.GetComponentsInChildren<Collider>(true).Where(r => !r.transform.IsChildOf(floorRoot) && r.enabled && !r.isTrigger))
                                if (r.bounds.max.y > p.y + .6f && r.bounds.min.y < p.y + 1.8f)
                                    clearance = Mathf.Min(clearance, Vector3.Distance(p + Vector3.up, r.bounds.ClosestPoint(p + Vector3.up)));
                            float score = clearance - Vector2.Distance(XZ(p), center) * .035f;
                            if (score > best) { best = score; spawn = p; }
                        }
                    if (regions.Count < 2) throw new InvalidOperationException(original.name + " has fewer than two usable connection regions.");
                    var tile = go.GetComponent<CaveTile>() ?? go.AddComponent<CaveTile>(); tile.combat = true; tile.combatRadius = Mathf.Max(1, best); tile.footprintRadius = floorBounds.extents.magnitude; tile.sourcePlatform = original.name;
                    var guide = go.GetComponent<CaveConnectionGuide>() ?? go.AddComponent<CaveConnectionGuide>(); guide.regions = regions.ToArray();
                    var boundary = go.GetComponent<CavePlatformBoundary>();
                    if (boundary) boundary.definition = original.GetComponent<CavePlatformBoundary>();
                    var prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Platforms/" + go.name + ".prefab");
                    var obstacles = go.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && !c.transform.IsChildOf(floorRoot)).Select(c => c.bounds).ToArray();
                    entries.Add(new CavePlatformLibrary.Platform { source = original, prefab = prefab, hull = hull, surfaceTriangles = surfaceTriangles.ToArray(), spawn = spawn, regions = regions.ToArray(), obstacles = obstacles });
                    report.Add(new { source = path, regions = regions.Select(r => new { r.name, r.stone, pivot = V(r.pivot), r.heading, min = r.MinAngle, max = r.MaxAngle, r.pitch, r.pitchTolerance, r.radius, r.guideFitError, r.deckLength, r.authoredStairCount, r.maxStairCount, r.stairDirection, anchors = r.anchors.Select(V).ToArray() }), retainedDecorativeBridges = keep, spawn = V(spawn), clearRadius = best });
                }
                finally { Object.DestroyImmediate(go); }
            }
            var library = existing ? existing : ScriptableObject.CreateInstance<CavePlatformLibrary>(); library.platforms = entries.ToArray();
            library.timber = MeasureConnector(entries.SelectMany(p => p.regions).First(r => !r.stone).template, false, preview);
            library.singleStair = MeasureConnector(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/04_환경맵/TopDownCaves/Prefabs/Stairs_el01A.prefab"), true, preview);
            library.doubleStair = MeasureConnector(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/04_환경맵/TopDownCaves/Prefabs/Stairs_el01B.prefab"), true, preview);
            if (!existing) AssetDatabase.CreateAsset(library, LibraryPath); else EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            File.WriteAllText(Output + "/Data/library.json", JsonConvert.SerializeObject(report, Formatting.Indented)); return library;
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static CapsuleCollider placementProbe;
    static readonly Dictionary<CavePlatformLibrary.Platform, Collider[]> placementColliders = new Dictionary<CavePlatformLibrary.Platform, Collider[]>();
    public static Layout Plan(CavePlatformLibrary library, int count, int seed, float spacing = 1)
    {
        Guard();
        if (!library || library.platforms == null || library.platforms.Length < 2) throw new ArgumentException("Build the platform library first.");
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var probe = new GameObject("Connection clearance probe"); SceneManager.MoveGameObjectToScene(probe, preview);
            placementProbe = probe.AddComponent<CapsuleCollider>(); placementProbe.height = 1.8f; placementProbe.radius = .32f; placementProbe.center = Vector3.up * .95f;
            foreach (var platform in library.platforms)
            {
                var clone = Object.Instantiate(platform.prefab); SceneManager.MoveGameObjectToScene(clone, preview); var floor = clone.transform.Find("Platform");
                placementColliders[platform] = clone.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && !c.transform.IsChildOf(floor)).ToArray();
            }
            Physics.SyncTransforms();
            Layout result = null;
            var search = Solve(library, count, seed, spacing, Blocked, value => result = value);
            try { while(search.MoveNext()) { } } finally { (search as IDisposable)?.Dispose(); }
            return result;
        }
        finally { placementProbe = null; placementColliders.Clear(); EditorSceneManager.ClosePreviewScene(preview); }
    }
    static bool Blocked(CavePlatformLibrary.Platform platform, Vector3 floor)
    {
        if (!placementColliders.TryGetValue(platform, out var colliders)) return false;
        foreach (var collider in colliders)
        {
            var bounds = collider.bounds;
            if (bounds.max.y <= floor.y + .5f || bounds.min.y >= floor.y + 1.85f) continue;
            if (floor.x < bounds.min.x - .4f || floor.x > bounds.max.x + .4f || floor.z < bounds.min.z - .4f || floor.z > bounds.max.z + .4f) continue;
            if (Physics.ComputePenetration(placementProbe, floor, Quaternion.identity, collider, collider.transform.position, collider.transform.rotation, out _, out float depth) && depth > .015f) return true;
        }
        return false;
    }
    static void Bridge(CaveWorld world, CavePlatformLibrary library, Layout plan, Link link)
        => CaveRuntimeGenerator.Connect(world, library, link);
    static void Background(CaveWorld world, CavePlatformLibrary library, Layout plan, System.Random random)
    {
        var root = new GameObject("Cave perimeter · scenery").transform; root.SetParent(world.generatedRoot, false);
        var hull = Hull(plan.rooms.SelectMany(r => WorldHull(library, r)));
        string[] names = { "Platform_09", "Platform_06", "Platform_07" };
        for (int edge = 0; edge < hull.Length; edge++)
        {
            var a = hull[edge]; var b = hull[(edge + 1) % hull.Length]; var d = (b - a).normalized; var outside = new Vector2(d.y, -d.x);
            int count = Mathf.CeilToInt(Vector2.Distance(a, b) / 17);
            for (int i = 0; i < count; i++)
            {
                var p = Vector2.Lerp(a, b, (i + .25f + (float)random.NextDouble() * .5f) / count) + outside * (19 + (float)random.NextDouble() * 10);
                string name = names[random.Next(names.Length)]; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/04_환경맵/TopDownCaves/Prefabs/" + name + ".prefab"); if (!prefab) continue;
                var go = Object.Instantiate(prefab, root); go.name = name; go.transform.position = new Vector3(p.x, 12 + (float)random.NextDouble() * 6, p.y); go.transform.rotation = Quaternion.Euler(0, random.Next(360), 0); go.transform.localScale *= 1.4f + (float)random.NextDouble() * 1.1f;
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false; world.backgroundCount++;
            }
        }
        var bed = GameObject.CreatePrimitive(PrimitiveType.Plane); bed.name = "Deep cavern shadow bed"; bed.transform.SetParent(root, false); bed.transform.position = new Vector3(0, -9, 0); bed.transform.localScale = Vector3.one * world.mapSize * .28f; bed.GetComponent<Collider>().enabled = false;
        bed.GetComponent<Renderer>().sharedMaterial = world.catalog.abyssMaterial;
    }

    public static object Generate(int count = 9, int seed = 19091, float spacing = 1)
    {
        for (int attempt = 0; attempt < 5; attempt++)
            try { return GenerateAttempt(count, seed, spacing, attempt); }
            catch (InvalidOperationException e) when (e.Data.Contains("LayoutConnectivity") && attempt < 4) { }
        throw new InvalidOperationException("No connected layout found.");
    }
    public static object GenerateFromLayout(Layout plan, int seed)
    {
        if (plan == null || plan.rooms.Count < 2 || plan.rooms.Count > 24 || plan.links.Count != plan.rooms.Count - 1)
            throw new ArgumentException("A complete preview layout is required.", nameof(plan));
        return GenerateAttempt(plan.rooms.Count, seed, 1, 0, plan);
    }
    static object GenerateAttempt(int count, int seed, float spacing, int layoutAttempt, Layout preparedPlan = null)
    {
        Guard(); var library = AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(LibraryPath); if (!library) library = BuildLibrary();
        var plan = preparedPlan ?? Plan(library, count, unchecked(seed + layoutAttempt * 104729), spacing); var initialScene = SceneManager.GetActiveScene(); bool dirty = initialScene.isDirty; var stage = PrefabStageUtility.GetCurrentPrefabStage(); string stagePath = stage == null ? null : stage.assetPath;
        var selection = Selection.objects; Scene scene = default; var owned = new List<Mesh>(); NavMeshData ownedNavigation = null; var clock = System.Diagnostics.Stopwatch.StartNew();
        string path = AssetDatabase.GenerateUniqueAssetPath(SceneRoot + "/Caves_Authored_" + count + "_" + seed + ".unity");
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var world = new GameObject("Authored Cave Platforms · " + seed).AddComponent<CaveWorld>(); world.authoredLayout = true; world.seed = seed; world.combatCount = count; world.catalog = AssetDatabase.LoadAssetAtPath<CaveCatalog>("Assets/ProjectOverburst/04_Contents/World/Caves/Data/CaveCatalog.asset");
            world.generatedRoot = new GameObject("Generated platforms and connections").transform; world.generatedRoot.SetParent(world.transform, false);
            for (int i = 0; i < plan.rooms.Count; i++)
            {
                var room = plan.rooms[i]; var definition = library.platforms[room.platform]; var go = (GameObject)PrefabUtility.InstantiatePrefab(definition.prefab, scene); go.transform.SetParent(world.generatedRoot, false); go.transform.SetPositionAndRotation(room.position, Quaternion.Euler(0, room.yaw, 0)); go.name = "Area " + (i + 1) + " · " + definition.source.name;
                world.courts.Add(new CaveWorld.Court { name = go.name, center = room.position + Rotate(definition.spawn, room.yaw), radius = go.GetComponent<CaveTile>().combatRadius, tile = go.GetComponent<CaveTile>() });
            }
            foreach (var link in plan.links) Bridge(world, library, plan, link);

            var bounds = BoundsOf(world.generatedRoot.GetComponentsInChildren<Renderer>().Where(r => r.enabled)); world.mapSize = Mathf.Max(bounds.size.x, bounds.size.z) + 40;
            Physics.SyncTransforms();
            var surface = world.gameObject.AddComponent<NavMeshSurface>(); surface.collectObjects = CollectObjects.Children; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders; surface.overrideVoxelSize = true; surface.voxelSize = .12f; surface.overrideTileSize = true; surface.tileSize = 128; surface.BuildNavMesh();
            if (!surface.navMeshData) throw new InvalidOperationException("NavMesh bake produced no data.");
            ownedNavigation = surface.navMeshData;
            int supportedSeams = 0;
            var routeChecks = new List<object>(); var disconnected = new List<int>();
            for (int i = 0; i < world.courts.Count; i++)
            {
                var desired = world.courts[i].center; bool standing = false;
                for (int candidate = 0; candidate < 49 && !standing; candidate++)
                {
                    float radius = candidate == 0 ? 0 : 1 + (candidate - 1) / 16;
                    float angle = (candidate % 16) * Mathf.PI / 8;
                    var point = desired + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    if (NavMesh.SamplePosition(point, out var sample, .7f, NavMesh.AllAreas) && world.CanStand(sample.position, .45f))
                    { world.courts[i].center = sample.position; standing = true; }
                }
                if (!standing) { disconnected.Add(i + 1); continue; }
                var route = new NavMeshPath(); bool connected = NavMesh.CalculatePath(world.courts[0].center, world.courts[i].center, NavMesh.AllAreas, route) && route.status == NavMeshPathStatus.PathComplete;
                routeChecks.Add(new { area = i + 1, connected, corners = route.corners.Length });
                if (!connected) disconnected.Add(i + 1);
            }
            var navPath = AssetDatabase.GenerateUniqueAssetPath(Root + "/Meshes/" + Path.GetFileNameWithoutExtension(path) + "_NavMesh.asset"); AssetDatabase.CreateAsset(surface.navMeshData, navPath);
            foreach (var mesh in owned) AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(Root + "/Meshes/" + Path.GetFileNameWithoutExtension(path) + "_" + mesh.name.Replace('–', '_') + ".asset"));
            var actor = new GameObject("Explorer"); var body = actor.AddComponent<CharacterController>(); body.height = 1.8f; body.radius = .32f; body.center = Vector3.up * .9f; body.stepOffset = .4f; body.skinWidth = .03f; body.slopeLimit = 40;
            var explorer = actor.AddComponent<CaveExplorer>(); explorer.world = world; actor.transform.position = world.courts[0].center + Vector3.up * .06f;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule); visual.name = "Explorer preview"; visual.transform.SetParent(actor.transform, false); visual.transform.localPosition = Vector3.up * .85f; visual.transform.localScale = new Vector3(.55f, .8f, .55f); visual.GetComponent<Collider>().enabled = false; visual.GetComponent<Renderer>().sharedMaterial = world.catalog.explorerMaterial;
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.backgroundColor = new Color(.015f, .018f, .025f); camera.clearFlags = CameraClearFlags.SolidColor; camera.gameObject.AddComponent<AudioListener>();
            var follow = camera.gameObject.AddComponent<CaveCamera>(); follow.target = explorer; explorer.view = camera; follow.Snap();
            var dressing = CaveDressingBuilder.Apply(world, camera);
            world.generationMilliseconds = clock.ElapsedMilliseconds;
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException(path);
            Directory.CreateDirectory(Output + "/Data");
            var result = new { scenePath = path, rooms = count, links = plan.links.Count, dressing, seed, layoutAttempt, supportedSeams, disconnected, reviewConnections = world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>().Count(c => c.needsReview), seconds = clock.Elapsed.TotalSeconds, elevations = plan.rooms.Select(r => r.position.y).ToArray(), junctions = plan.rooms.Select((r, i) => new { area = i + 1, capacity = r.used.Length, connected = r.used.Count(u => u) }).Where(r => r.capacity > 2).ToArray(), routes = routeChecks, regionChoices = plan.links.Select(l => new { l.a, l.b, l.portA, l.portB, l.angleA, l.angleB, l.pitch, l.stone, l.stairCount, l.reviewShortfall, start = V(l.start), end = V(l.end) }) };
            File.WriteAllText(Output + "/Data/" + Path.GetFileNameWithoutExtension(path) + ".json", JsonConvert.SerializeObject(result, Formatting.Indented)); return result;
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var mesh in owned) if (mesh && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
            if (ownedNavigation && !EditorUtility.IsPersistent(ownedNavigation)) Object.DestroyImmediate(ownedNavigation);
            if (initialScene.IsValid() && initialScene.isLoaded) SceneManager.SetActiveScene(initialScene);
            Selection.objects = selection;
            if (stagePath != null && PrefabStageUtility.GetCurrentPrefabStage() == null) PrefabStageUtility.OpenPrefab(stagePath);
            if (initialScene.isDirty != dirty) throw new InvalidOperationException("User scene dirty state changed.");
        }
    }
}

public sealed class CavePlatformGeneratorWindow : EditorWindow
{
    [SerializeField] int seed = 19215, count = 15;
    [SerializeField] string lastScene, message;
    CavePlatformMapBuilder.Layout preview;
    CavePlatformLibrary library;
    Hash128 previewLibraryHash;
    double previewSeconds;
    bool failed;
    public string LastScenePath => lastScene;
    public CavePlatformMapBuilder.Layout PreviewLayout => PreviewIsCurrent ? preview : null;
    bool PreviewIsCurrent => preview != null && library &&
        previewLibraryHash == AssetDatabase.GetAssetDependencyHash(CavePlatformMapBuilder.LibraryPath);

    [MenuItem("Overburst/Caves/맵 생성기")]
    [MenuItem("Overburst/Caves/User Platforms/2 Generator and layout preview")]
    public static void Open() => GetWindow<CavePlatformGeneratorWindow>("동굴 맵 생성기");
    void OnEnable() => minSize = new Vector2(480, 640);
    public void Configure(int platformCount, int layoutSeed)
    {
        if (count == Mathf.Clamp(platformCount, 2, 24) && seed == layoutSeed) return;
        count = Mathf.Clamp(platformCount, 2, 24); seed = layoutSeed;
        preview = null; message = "설정이 바뀌었어요. 레이아웃을 다시 미리 보세요."; failed = false;
        Repaint();
    }
    public void BuildPreview()
    {
        CavePlatformMapBuilder.Guard(); preview = null;
        library = AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(CavePlatformMapBuilder.LibraryPath);
        if (!library) library = CavePlatformMapBuilder.BuildLibrary();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        preview = CavePlatformMapBuilder.Plan(library, count, seed);
        previewSeconds = timer.Elapsed.TotalSeconds;
        previewLibraryHash = AssetDatabase.GetAssetDependencyHash(CavePlatformMapBuilder.LibraryPath);
        var junctions = preview.rooms.Where(r => r.used.Length > 2).ToArray();
        message = $"미리보기 준비 · {count}개 플랫폼 / {preview.links.Count}개 연결 · {previewSeconds:F2}초\n" +
            $"분기 플랫폼 우선 연결 · 모든 방향 연결 {junctions.Count(r => r.used.All(u => u))}/{junctions.Length}개";
        failed = false; Repaint();
    }
    public object GeneratePreview()
    {
        CavePlatformMapBuilder.Guard();
        if (!PreviewIsCurrent) BuildPreview();
        var result = CavePlatformMapBuilder.GenerateFromLayout(preview, seed);
        var data = Newtonsoft.Json.Linq.JObject.FromObject(result);
        lastScene = (string)data["scenePath"];
        int review = (int)data["reviewConnections"];
        message = $"새 씬 저장 완료 · {count}개 플랫폼 / {preview.links.Count}개 연결 · {(double)data["seconds"]:F1}초" +
            (review > 0 ? $"\n연결부 {review}곳에 REVIEW 표시가 있어요. 씬에서 확인하세요." : "");
        failed = false; Repaint(); return result;
    }
    void Run(Action action, string progress)
    {
        try { EditorUtility.DisplayProgressBar("동굴 맵 생성기", progress, .5f); action(); }
        catch (Exception e) { failed = true; message = e.Message; Debug.LogException(e); }
        finally { EditorUtility.ClearProgressBar(); Repaint(); }
    }
    void OnGUI()
    {
        GUILayout.Space(8);
        GUILayout.Label("동굴 플랫폼 맵", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("다듬은 플랫폼 9종으로 생성합니다. 분기 플랫폼의 열린 방향부터 채우며, 10개 이상이면 일부 플랫폼이 반복됩니다. 다리 길이는 고정하고, 계단은 연결부 규격에 따라 1~3개를 사용합니다.", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        int nextCount = EditorGUILayout.IntSlider("플랫폼 수", count, 2, 24);
        int nextSeed = EditorGUILayout.IntField("시드", seed);
        if (EditorGUI.EndChangeCheck()) Configure(nextCount, nextSeed);
        if (GUILayout.Button("다른 시드 선택")) Configure(count, Guid.NewGuid().GetHashCode() & int.MaxValue);
        if (preview != null && !PreviewIsCurrent)
        {
            preview = null; message = "플랫폼 자산이 변경되어 미리보기를 다시 계산해야 합니다.";
        }
        bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating;
        using (new EditorGUI.DisabledScope(busy))
        {
            if (GUILayout.Button("1. 빠른 레이아웃 미리보기", GUILayout.Height(30))) Run(BuildPreview, "플랫폼과 연결부를 배치하고 있습니다.");
            if (GUILayout.Button("2. 이 배치로 새 씬 생성", GUILayout.Height(34))) Run(() => GeneratePreview(), "프리팹 배치와 NavMesh 저장을 진행하고 있습니다.");
        }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, failed ? MessageType.Error : MessageType.None);
        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(lastScene ?? "");
        if (sceneAsset)
        {
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("최근 생성 씬", sceneAsset, typeof(SceneAsset), false);
            using (new EditorGUI.DisabledScope(busy))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Project에서 선택")) { Selection.activeObject = sceneAsset; EditorGUIUtility.PingObject(sceneAsset); }
                if (GUILayout.Button("생성한 씬 열기"))
                    Run(() => { CavePlatformMapBuilder.Guard(); if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(lastScene); }, "저장한 씬을 열고 있습니다.");
            }
        }
        EditorGUILayout.HelpBox("평면 배치와 높이를 확인하는 미리보기입니다. 내부 단차의 캐릭터 이동 검증은 별도이며, 씬은 매번 새 파일로 저장됩니다.", MessageType.None);
        GUILayout.Label("청록: 다리    주황: 계단    빨강: 접점 확인 필요    숫자: 구역 / Y 높이", EditorStyles.miniLabel);
        var rect = GUILayoutUtility.GetRect(100, 10000, 200, 10000);
        EditorGUI.DrawRect(rect, new Color(.07f, .09f, .12f));
        if (preview == null) { GUI.Label(new Rect(rect.x + 16, rect.y + 16, rect.width - 32, 30), "플랫폼 수와 시드를 선택한 뒤 미리보기를 누르세요."); return; }
        if (Event.current.type != EventType.Repaint) return;
        DrawPreview(rect);
    }
    void DrawPreview(Rect rect)
    {
        var polygons = preview.rooms.Select(r => library.platforms[r.platform].hull.Select(p =>
            r.position + Quaternion.Euler(0, r.yaw, 0) * new Vector3(p.x, 0, p.y)).ToArray()).ToArray();
        var all = polygons.SelectMany(p => p).ToArray();
        float minX = all.Min(p => p.x), maxX = all.Max(p => p.x), minZ = all.Min(p => p.z), maxZ = all.Max(p => p.z);
        float scale = Mathf.Min((rect.width - 60) / Mathf.Max(1, maxX - minX), (rect.height - 50) / Mathf.Max(1, maxZ - minZ));
        Vector3 Screen(Vector3 p) => new Vector3(rect.center.x + (p.x - (minX + maxX) * .5f) * scale,
            rect.center.y - (p.z - (minZ + maxZ) * .5f) * scale, 0);
        var savedColor = Handles.color; Handles.BeginGUI();
        foreach (var link in preview.links)
        {
            Handles.color = link.reviewShortfall > 0 ? new Color(1, .3f, .25f) : link.stone ? new Color(1, .66f, .3f) : new Color(.3f, .85f, .88f);
            Handles.DrawAAPolyLine(4, Screen(link.start), Screen(link.end));
        }
        for (int i = 0; i < preview.rooms.Count; i++)
        {
            var room = preview.rooms[i];
            Handles.color = Color.Lerp(new Color(.22f, .38f, .44f), new Color(.58f, .44f, .25f), Mathf.InverseLerp(10, 34, room.position.y));
            Handles.DrawAAConvexPolygon(polygons[i].Select(Screen).ToArray());
            GUI.Label(new Rect(Screen(room.position).x - 22, Screen(room.position).y - 10, 110, 24), $"{i + 1} · {room.position.y:F1}m");
            if (room.used.Length > 2)
                GUI.Label(new Rect(Screen(room.position).x - 22, Screen(room.position).y + 8, 110, 24), $"분기 {room.used.Count(u => u)}/{room.used.Length}", EditorStyles.miniLabel);
        }
        Handles.color = savedColor; Handles.EndGUI();
    }
}
