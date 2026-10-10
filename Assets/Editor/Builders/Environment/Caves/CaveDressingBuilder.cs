using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Overburst.Caves;
using Object = UnityEngine.Object;

// Scenery is generated after the rigid platform/connector layout. It never moves a gameplay piece.
public static class CaveDressingBuilder
{
    public const string Root = "Assets/ProjectOverburst/04_Contents/World/Caves/Procedural/Atmosphere";
    const string Demo = "Assets/ThirdParty/04_환경맵/TopDownCaves/Demo/Demo1.unity";
    const string Props = "Assets/ThirdParty/04_환경맵/TopDownCaves/Prefabs/";
    public const string SceneryName = "Demo1 attached rock formations";
    public const string LightingName = "Demo1 lighting";
    public const string GroundName = "Demo1 terrain ground";
    public const float GroundClearance = 18;
    public const string ColorProfilePath = Root + "/CaveOverburst_Color.asset";
    static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    { var d = b - a; return Vector2.Distance(p, a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(.0001f, d.sqrMagnitude))); }
    static float Clearance(Vector2 p, Vector2[] hull)
    {
        bool inside = true; float distance = float.MaxValue;
        for (int i = 0; i < hull.Length; i++)
        { var a = hull[i]; var b = hull[(i + 1) % hull.Length]; inside &= Cross(b - a, p - a) >= 0; distance = Mathf.Min(distance, SegmentDistance(p, a, b)); }
        return inside ? -distance : distance;
    }
    static Bounds BoundsOf(GameObject go)
    { var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray(); var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b; }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    public static int EnsureRenderer()
    {
        Folder(Root);
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!pipeline) throw new InvalidOperationException("Cave dressing requires URP.");
        var settings = new SerializedObject(pipeline); var list = settings.FindProperty("m_RendererDataList");
        string path = Root + "/CaveDemo1_AuthoredRenderer.asset";
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
        if (!renderer) throw new InvalidOperationException("Restore the authored Cave Demo1 renderer before generating a map.");
        for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) return i;
        int index = list.arraySize; list.InsertArrayElementAtIndex(index); list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
        settings.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(pipeline); return index;
    }

    public static VolumeProfile EnsureColorProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ColorProfilePath);
        if (profile) return profile;
        Folder(Root);
        const string mainProfile = "Assets/ProjectOverburst/03_Features/World/AtmosphereTrial/OvercastVolume.asset";
        if (!AssetDatabase.CopyAsset(mainProfile, ColorProfilePath))
            throw new InvalidOperationException("Restore the MainScene atmosphere profile before creating the cave grade.");
        profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ColorProfilePath);
        // Match the town's tonal response while keeping unlit cave paths readable.
        profile.TryGet<ColorAdjustments>(out var color); color.postExposure.Override(.65f);
        profile.TryGet<Bloom>(out var bloom); bloom.intensity.Override(.15f); bloom.threshold.Override(1.1f);
        profile.TryGet<Vignette>(out var vignette); vignette.intensity.Override(.15f); vignette.smoothness.Override(.35f);
        foreach (var component in profile.components) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
        return profile;
    }

    public static Light DemoLighting(CaveWorld world)
    {
        var target = world.gameObject.scene; var active = SceneManager.GetActiveScene(); Scene demo = default;
        try
        {
            demo = EditorSceneManager.OpenScene(Demo, OpenSceneMode.Additive); SceneManager.SetActiveScene(demo);
            var mode = RenderSettings.ambientMode; var sky = RenderSettings.skybox;
            var ambient = RenderSettings.ambientLight; var equator = RenderSettings.ambientEquatorColor; var ground = RenderSettings.ambientGroundColor;
            float intensity = RenderSettings.ambientIntensity, reflection = RenderSettings.reflectionIntensity;
            var fogColor = RenderSettings.fogColor; var fogMode = RenderSettings.fogMode;
            bool fog = RenderSettings.fog; float density = RenderSettings.fogDensity, start = RenderSettings.fogStartDistance, end = RenderSettings.fogEndDistance;
            var lights = demo.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>()).ToArray();
            var main = lights.First(l => l.type == LightType.Directional);
            var colorProfile = EnsureColorProfile();
            SceneManager.SetActiveScene(target);
            var root = new GameObject(LightingName);
            var volume = root.AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = colorProfile;
            var sun = Object.Instantiate(main.gameObject, root.transform).GetComponent<Light>(); sun.name = "Demo1 Directional Light";
            sun.transform.rotation = main.transform.rotation; sun.lightmapBakeType = LightmapBakeType.Realtime;
            RenderSettings.ambientMode = mode; RenderSettings.skybox = sky; RenderSettings.ambientLight = ambient;
            RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground; RenderSettings.ambientIntensity = intensity;
            RenderSettings.reflectionIntensity = reflection; RenderSettings.sun = sun;
            // The authored green camera-distance fog is separate from the requested grey abyss mist.
            RenderSettings.fog = false; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = density; RenderSettings.fogStartDistance = start; RenderSettings.fogEndDistance = end;

            // Extracted crystal/torch prefabs keep their own lights. Add missing Demo1 light types only at visible emitters.
            var warm = lights.First(l => l.type == LightType.Point && l.color.r > .98f);
            var blue = lights.First(l => l.type == LightType.Point && l.color.b > .99f && l.color.r < .5f);
            var yellow = lights.First(l => l.type == LightType.Point && l.color.r > .9f && l.color.b < .2f);
            var existing = world.GetComponentsInChildren<Light>().ToList();
            foreach (var court in world.courts)
            {
                var candidates = court.tile.GetComponentsInChildren<Renderer>().Where(r => r.enabled &&
                    (r.name.IndexOf("torch", StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("Crystals", StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(r => r.bounds.size.sqrMagnitude).ToArray();
                int added = 0;
                foreach (var r in candidates)
                {
                    var p = r.bounds.center + Vector3.up * 1.1f;
                    if (existing.Any(l => Vector3.Distance(l.transform.position, p) < 6) || added >= 2) continue;
                    var template = r.name.IndexOf("torch", StringComparison.OrdinalIgnoreCase) >= 0 ? warm : r.name.Contains("yellow") ? yellow : blue;
                    var light = Object.Instantiate(template.gameObject, root.transform).GetComponent<Light>();
                    light.name = "Demo1 local light · " + r.name; light.transform.position = p; light.lightmapBakeType = LightmapBakeType.Realtime;
                    existing.Add(light); added++;
                }
            }
            return sun;
        }
        finally { if (demo.IsValid() && demo.isLoaded) EditorSceneManager.CloseScene(demo, true); SceneManager.SetActiveScene(active); }
    }

    public static object Apply(CaveWorld world, Camera camera)
    {
        CavePlatformMapBuilder.Guard();
        if (world.transform.Find(SceneryName)) throw new InvalidOperationException("This map is already dressed.");
        Folder(Root); SceneManager.SetActiveScene(world.gameObject.scene);
        var library = AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(CavePlatformMapBuilder.LibraryPath);
        var hulls = world.courts.Select(c =>
        {
            var definition = library.platforms.First(p => c.tile.name.Contains(p.source.name));
            return definition.hull.Select(v => XZ(c.tile.transform.TransformPoint(new Vector3(v.x, 0, v.y)))).ToArray();
        }).ToArray();
        var all = hulls.SelectMany(h => h).ToArray(); float minX = all.Min(v => v.x), maxX = all.Max(v => v.x), minZ = all.Min(v => v.y), maxZ = all.Max(v => v.y);
        float floor = world.courts.Min(c => c.center.y);
        float rockBaseLevel = floor - 2.8f;
        var root = new GameObject(SceneryName).transform; root.SetParent(world.transform, false);
        var random = new System.Random(unchecked(world.seed ^ 0x574129)); float Next(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        var occupied = new List<(Vector2 p, float radius)>(); int rocks = 0, groups = 0;
        float PassageClearance(Vector2 p)
        {
            float d = float.MaxValue;
            foreach (var passage in world.passages)
                for (int i = 1; i < passage.points.Length; i++)
                    d = Mathf.Min(d, SegmentDistance(p, XZ(passage.points[i - 1]), XZ(passage.points[i])) - passage.widths[Mathf.Min(i, passage.widths.Length - 1)] * .5f - 1.5f);
            return d;
        }
        // Grow overlapping rock masses from selected platform edges. Never scatter isolated columns in empty space.
        var viewer = XZ(-camera.transform.forward).normalized;
        for (int room = 0; room < hulls.Length; room++)
        {
            var hull = hulls[room];
            var edges = Enumerable.Range(0, hull.Length).Select(i => {
                var a = hull[i]; var delta = hull[(i + 1) % hull.Length] - a;
                var tangent = delta.normalized; var outward = new Vector2(tangent.y, -tangent.x);
                return new { a, delta, tangent, outward, score = delta.magnitude * Next(.7f, 1.2f) * (Vector2.Dot(outward, viewer) > .35f ? .5f : 1) };
            }).OrderByDescending(e => e.score).ToArray();
            int roomGroups = 0;
            foreach (var edge in edges)
            {
                if (roomGroups >= 2) break;
                if (edge.delta.magnitude < 5) continue;
                var anchor = edge.a + edge.delta * Next(.32f, .68f);
                float mainRadius = Next(4.2f, 6.8f);
                var center = anchor + edge.outward * (mainRadius * .78f);
                if (PassageClearance(center) < mainRadius + 1 || hulls.Where((h, i) => i != room).Any(h => Clearance(center, h) < mainRadius + .3f)) continue;
                if (occupied.Any(o => Vector2.Distance(o.p, center) < o.radius + mainRadius * .7f)) continue;
                var group = new GameObject("Platform " + (room + 1) + " · attached cliff " + (++groups)).transform; group.SetParent(root, false);
                int members = random.Next(4, 7), placed = 0; float span = mainRadius * Next(.85f, 1.15f);
                for (int member = 0; member < members; member++)
                {
                    float offset = member == 0 ? 0 : (member % 2 == 0 ? 1 : -1) * ((member + 1) / 2) * span * .48f;
                    float radius = member == 0 ? mainRadius : mainRadius * Next(.58f, .85f);
                    string name = member == 0 || radius > 4 ? "Rock1_2" : member % 2 == 0 ? "Rock1_1" : "Rock1_3";
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Props + name + ".prefab"), world.gameObject.scene);
                    go.transform.SetParent(group, false); go.transform.rotation = Quaternion.Euler(Next(-4, 4), Next(0, 360), Next(-4, 4));
                    var b = BoundsOf(go); go.transform.localScale *= radius / Mathf.Max(b.extents.x, b.extents.z);
                    b = BoundsOf(go);
                    float facing = Vector2.Dot(edge.outward, viewer);
                    float rise = facing > .35f ? Next(1.5f, 3.5f) : Next(4.5f, 10.5f);
                    float top = world.courts[room].center.y + rise * (member == 0 ? 1 : .7f);
                    float height = Mathf.Max(b.size.y, top - rockBaseLevel + 5);
                    go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(1, height / b.size.y, 1)); b = BoundsOf(go);
                    var vertices = go.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh).SelectMany(f => f.sharedMesh.vertices.Select(f.transform.TransformPoint)).Select(XZ).ToArray();
                    var shape = CavePlatformMapBuilder.Hull(vertices); var origin = XZ(b.center);
                    float support = shape.Max(p => -Vector2.Dot(p - origin, edge.outward));
                    var target = anchor + edge.tangent * offset + edge.outward * (support - .12f + (member > 2 ? radius * .45f : 0));
                    var shifted = shape.Select(p => p - origin + target).ToArray();
                    if (shifted.Any(p => PassageClearance(p) < 1.1f || hulls.Any(h => Clearance(p, h) < -.4f))) { Object.DestroyImmediate(go); continue; }
                    go.transform.position += new Vector3(target.x - b.center.x, top - b.max.y, target.y - b.center.z);
                    foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
                    go.name = "Attached rock · " + (++rocks) + " · " + name; occupied.Add((target, radius * .6f)); placed++;
                }
                if (placed == 0) { Object.DestroyImmediate(group.gameObject); groups--; } else roomGroups++;
            }
        }
        // The old draft had two synthetic directional lights. Only remove those from this owned scene copy.
        foreach (var light in world.gameObject.scene.GetRootGameObjects().Where(g => g.name == "Cool cave light" || g.name == "Cave bounce").ToArray()) Object.DestroyImmediate(light);
        var sun = DemoLighting(world);
        int rendererIndex = EnsureRenderer(); var cameraData = camera.GetUniversalAdditionalCameraData(); cameraData.SetRenderer(rendererIndex); cameraData.requiresDepthTexture = true; cameraData.renderPostProcessing = true;
        var terrain = AddDemoTerrain(world, hulls, new Vector2((minX + maxX) * .5f, (minZ + maxZ) * .5f));
        var details = CaveDetailDressingBuilder.Apply(world);
        return new { rocks, groups, ruins = 0, lowestFloor = floor, rendererIndex, terrainData = AssetDatabase.GetAssetPath(terrain.terrainData), rooms = world.courts.Count, passages = world.passages.Count, details };
    }

    internal static void FitGroundHeights(float[,] heights, Vector3 origin, Vector3 size,
        IReadOnlyList<Bounds> supports, Vector2[][] hulls, IReadOnlyList<CaveWorld.Court> courts, IReadOnlyList<CaveWorld.Passage> passages)
    {
        int resolution = heights.GetLength(0);
        for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
            heights[z, x] = origin.y + heights[z, x] * size.y;

        // Keep each cell's original operation order; only omit features whose blend weight is zero.
        void Patch(Vector2 min, Vector2 max, Func<float, Vector2, float> adjust)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - origin.x) / size.x * (resolution - 1)) - 1, 0, resolution - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((max.x - origin.x) / size.x * (resolution - 1)) + 1, 0, resolution - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((min.y - origin.z) / size.z * (resolution - 1)) - 1, 0, resolution - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((max.y - origin.z) / size.z * (resolution - 1)) + 1, 0, resolution - 1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(origin.x + x * size.x / (resolution - 1), origin.z + z * size.z / (resolution - 1));
                heights[z, x] = adjust(heights[z, x], p);
            }
        }
        foreach (var b in supports)
            Patch(XZ(b.min) - Vector2.one * 9, XZ(b.max) + Vector2.one * 9, (h, p) =>
            {
                float dx = Mathf.Max(0, Mathf.Abs(p.x - b.center.x) - b.extents.x);
                float dz = Mathf.Max(0, Mathf.Abs(p.y - b.center.z) - b.extents.z);
                float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.Sqrt(dx * dx + dz * dz) / 9);
                return Mathf.Max(h, Mathf.Lerp(h, b.min.y + .35f, weight));
            });
        for (int i = 0; i < hulls.Length; i++)
        {
            var hull = hulls[i]; float ceiling = courts[i].center.y - 3.5f;
            var min = new Vector2(hull.Min(p => p.x), hull.Min(p => p.y));
            var max = new Vector2(hull.Max(p => p.x), hull.Max(p => p.y));
            Patch(min - Vector2.one * 12, max + Vector2.one * 12, (h, p) =>
            {
                float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.Max(0, Clearance(p, hull)) / 12);
                return Mathf.Min(h, Mathf.Lerp(h, ceiling, weight));
            });
        }
        foreach (var passage in passages) for (int i = 1; i < passage.points.Length; i++)
        {
            var start = passage.points[i - 1]; var end = passage.points[i];
            var a = XZ(start); var b = XZ(end); var delta = b - a;
            float halfWidth = passage.widths[Mathf.Min(i, passage.widths.Length - 1)] * .5f;
            var padding = Vector2.one * (halfWidth + 8);
            Patch(Vector2.Min(a, b) - padding, Vector2.Max(a, b) + padding, (h, p) =>
            {
                float t = Mathf.Clamp01(Vector2.Dot(p - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
                float distance = Vector2.Distance(p, a + delta * t);
                float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.Max(0, distance - halfWidth) / 8);
                float ceiling = Mathf.Lerp(start.y, end.y, t) - 3.5f;
                return Mathf.Min(h, Mathf.Lerp(h, ceiling, weight));
            });
        }
        for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
            heights[z, x] = Mathf.Clamp01((heights[z, x] - origin.y) / size.y);
    }

    public static float LowerGround(CaveWorld world, Terrain terrain)
    {
        CavePlatformMapBuilder.Guard();
        // Translate the painted relief as a whole, so no shelf can resemble a second walkable floor.
        float floor = world.courts.Min(c => c.center.y);
        foreach (var passage in world.passages) foreach (var p in passage.points) floor = Mathf.Min(floor, p.y);
        var bounds = new Bounds(world.courts[0].center, Vector3.zero);
        foreach (var c in world.courts) foreach (var r in c.tile.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        bounds.Expand(40);
        var data = terrain.terrainData; var origin = terrain.transform.position; var size = data.size;
        int n = data.heightmapResolution;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - origin.x) / size.x * (n - 1)), 0, n - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - origin.z) / size.z * (n - 1)), 0, n - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.x - origin.x) / size.x * (n - 1)), x0, n - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.z - origin.z) / size.z * (n - 1)), z0, n - 1);
        float highest = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1).Cast<float>().Max() * size.y + origin.y;
        float drop = Mathf.Max(0, highest - (floor - GroundClearance));
        terrain.transform.position -= Vector3.up * drop;
        return drop;
    }

    static Terrain AddDemoTerrain(CaveWorld world, Vector2[][] hulls, Vector2 center)
    {
        var active = SceneManager.GetActiveScene(); Scene demo = default;
        TerrainData data = null;
        try
        {
            demo = EditorSceneManager.OpenScene(Demo, OpenSceneMode.Additive);
            var source = demo.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>()).Single();
            data = Object.Instantiate(source.terrainData); data.name = "Demo1 Ground " + world.seed;
            // Retain the demo's painted layers, texture scale and authored relief in a private TerrainData copy.
            var size = data.size; size.x = size.z = Mathf.Max(size.x, world.mapSize + 160); data.size = size;
            var origin = new Vector3(center.x - size.x * .5f, source.transform.position.y, center.y - size.z * .5f);
            var supports = world.transform.Find(SceneryName).GetComponentsInChildren<Renderer>().Select(r => r.bounds).ToList();
            supports.AddRange(world.courts.SelectMany(c => c.tile.GetComponentsInChildren<MeshFilter>())
                .Where(m => m.name.StartsWith("Platform_") && m.GetComponent<Renderer>()).Select(m => m.GetComponent<Renderer>().bounds));
            int resolution = data.heightmapResolution; var heights = data.GetHeights(0, 0, resolution, resolution);
            FitGroundHeights(heights, origin, size, supports, hulls, world.courts, world.passages);
            data.SetHeights(0, 0, heights);
            string path = AssetDatabase.GenerateUniqueAssetPath(Root + "/Demo1_Ground_" + world.seed + ".asset");
            AssetDatabase.CreateAsset(data, path);
            SceneManager.SetActiveScene(world.gameObject.scene);
            var go = Terrain.CreateTerrainGameObject(data); go.name = GroundName; go.transform.SetParent(world.transform, true); go.transform.position = origin;
            var terrain = go.GetComponent<Terrain>(); terrain.materialTemplate = source.materialTemplate;
            terrain.heightmapPixelError = 2; terrain.drawInstanced = true; terrain.basemapDistance = 2000;
            go.GetComponent<TerrainCollider>().enabled = false;
            LowerGround(world, terrain);
            AssetDatabase.SaveAssetIfDirty(data); terrain.Flush(); return terrain;
        }
        finally
        {
            if (demo.IsValid() && demo.isLoaded) EditorSceneManager.CloseScene(demo, true);
            SceneManager.SetActiveScene(active);
            if (data && !EditorUtility.IsPersistent(data)) Object.DestroyImmediate(data);
        }
    }
}

