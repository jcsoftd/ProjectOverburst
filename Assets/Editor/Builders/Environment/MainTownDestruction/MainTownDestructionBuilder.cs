using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overburst.EnvironmentDestructionAuthoring;
using Object = UnityEngine.Object;

public static class MainTownDestructionBuilder
{
    public const string Root = "Assets/ProjectOverburst/03_Features/World/MainTownDestruction";
    public const string Town = "Assets/ProjectOverburst/00_Scenes/MainScene.unity";
    public const string Group = "__MAIN_TOWN_DESTRUCTION__";
    public const string CatalogPath = Root + "/MainTownDestructionCatalog.asset";
    const string Vendor = "Assets/ThirdParty/04_환경맵/Dark Fantasy Bandit Camp & Wilderness/Prefabs";
    const string Dense = "Assets/ProjectOverburst/05_Art/Environment/MainTown/BanditExpansion/Vegetation/Prefabs";
    static bool TreeName(string name) => Regex.IsMatch(name, "^DF_(Aspen_|Birch_|Maple_|Pine_|Spruce_|Dead_Trees_|DeadStump_|Broken_Tree_Trunk_|FallenDeadTree_|Branch_)");
    static bool PropName(string name) => Regex.IsMatch(name, "^DF_(Barrel_Wood_|Wood_Box_|Wagon_Wheel_|Firewood_|Storage_Jar_|ClayJar$|Bucket$|Wood_Mortar$|Basket_[0-9]+$|Tavern_(Stool|Chair|Bench|Table)$)");
    static bool Eligible(string path) => TreeName(Path.GetFileNameWithoutExtension(path)) || PropName(Path.GetFileNameWithoutExtension(path));
    static bool Protected(Transform transform)
    {
        for (var p = transform; p != null; p = p.parent)
        {
            if (p.name.StartsWith("__EXPERIMENT") || p.name == Group || p.name == "Main Town Services" || p.name.StartsWith("__EDITOR")) return true;
            if (p.GetComponents<MonoBehaviour>().Any(x => x != null && (x is CombatHealth || x.GetType().Name.Contains("Interactable")))) return true;
        }
        return false;
    }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
    public static Scene RequireScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Shared Editor is not idle.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || IsolatedSavePlayGuard.ActiveDirectory != "" || SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") != "") throw new InvalidOperationException("Shared account/Play is busy.");
        var scene = SceneManager.GetSceneByPath(Town);
        if (!scene.IsValid() || !scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Use the loaded, saved MainScene without changing another scene.");
        return scene;
    }
    public static object Plan()
    {
        var scene = RequireScene(); var plan = Collect(scene);
        return new { status = "READ_ONLY_PLAN", sources = plan.sources.ToArray(), placements = plan.bindings.Select(x => new { name = x.root.name, source = plan.sources[x.definition], position = x.root.position.ToString("R") }).ToArray(), terrain = plan.terrain.terrainData.name, plan.terrain.terrainData.heightmapResolution, plan.terrain.terrainData.alphamapResolution, terrainTreeCount = plan.terrain.terrainData.treeInstanceCount };
    }
    sealed class PlanData
    {
        public readonly List<string> sources = new List<string>();
        public readonly List<MainTownDestruction.Placement> bindings = new List<MainTownDestruction.Placement>();
        public Terrain terrain;
    }
    static PlanData Collect(Scene scene)
    {
        var data = new PlanData(); var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
        var known = AssetDatabase.FindAssets("t:Prefab", new[] { Vendor, Dense }).Select(AssetDatabase.GUIDToAssetPath).Where(Eligible).ToDictionary(x => Path.GetFileNameWithoutExtension(x), x => x);
        var bound = new HashSet<Transform>();
        void Bind(Transform root, string path)
        {
            if (root == null || Protected(root) || !root.gameObject.activeInHierarchy || !bound.Add(root)) return;
            string denseName = Path.GetFileNameWithoutExtension(path) + "_Dense";
            if (TreeName(denseName) && known.TryGetValue(denseName, out var densePath) && root.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m != null && AssetDatabase.GetAssetPath(m).Contains("/BanditExpansion/Vegetation/Materials/")))) path = densePath;
            int definition = data.sources.IndexOf(path); if (definition < 0) { definition = data.sources.Count; data.sources.Add(path); }
            data.bindings.Add(new MainTownDestruction.Placement { root = root, definition = definition });
        }
        foreach (var t in all)
        {
            if (Protected(t)) continue;
            var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
            string path = prefabRoot == null ? "" : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabRoot);
            if (path.EndsWith(".prefab") && Eligible(path)) { Bind(prefabRoot.transform, path); continue; }
            if (t.root.name != "내부 확장 · 숲과 거점") continue;
            if (t.GetComponent<LODGroup>() == null && (t.GetComponent<MeshFilter>() == null || t.GetComponentInParent<LODGroup>() != null)) continue;
            string name = Regex.Match(t.name, "^DF_[A-Za-z0-9_]+").Value;
            name = Regex.Replace(name, "_LOD[0-9]+$", "");
            if (known.TryGetValue(name, out var source)) Bind(t, source);
        }
        data.terrain = all.Select(t => t.GetComponent<Terrain>()).Single(t => t != null);
        var trees = data.terrain.terrainData.treeInstances; var prototypes = data.terrain.terrainData.treePrototypes;
        for (int i = 0; i < prototypes.Length; i++)
        {
            string source = AssetDatabase.GetAssetPath(prototypes[i].prefab);
            if (TreeName(Path.GetFileNameWithoutExtension(source)) && trees.Any(t => t.prototypeIndex == i && t.widthScale > 0 && t.heightScale > 0) && !data.sources.Contains(source)) data.sources.Add(source);
        }
        return data;
    }
    [MenuItem("OVERBURST/World/MainScene 파괴/현재 맵에 생성 및 연결")]
    public static void Install()
    {
        var scene = RequireScene();
        if (scene.GetRootGameObjects().Any(x => x.name == Group)) throw new InvalidOperationException("Already installed. Remove only this connection before reinstalling.");
        var plan = Collect(scene); Folder(Root + "/Generated");
        var definitions = new List<MainTownDestructionCatalog.Definition>();
        foreach (string path in plan.sources) definitions.Add(BuildDefinition(path));
        var catalog = AssetDatabase.LoadAssetAtPath<MainTownDestructionCatalog>(CatalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<MainTownDestructionCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        catalog.definitions = definitions.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
        var group = new GameObject(Group); SceneManager.MoveGameObjectToScene(group, scene); Undo.RegisterCreatedObjectUndo(group, "메인 맵 파괴 연결");
        var controller = group.AddComponent<MainTownDestruction>(); controller.catalog = catalog; controller.placements = plan.bindings.ToArray(); controller.terrain = plan.terrain;
        EditorUtility.SetDirty(controller); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log($"MainScene destruction: {definitions.Count} source types, {plan.bindings.Count} placed roots, nearby Terrain trees. Vendor/NPC/scene objects preserved.");
    }
    [MenuItem("OVERBURST/World/MainScene 파괴/이 연결만 제거")]
    public static void Remove()
    {
        var scene = RequireScene(); var group = scene.GetRootGameObjects().SingleOrDefault(x => x.name == Group);
        if (group == null) return;
        if (group.GetComponent<MainTownDestruction>() == null) throw new InvalidOperationException("Unexpected object using the owned name.");
        Undo.DestroyObjectImmediate(group); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    static MainTownDestructionCatalog.Definition BuildDefinition(string sourcePath)
    {
        string name = Path.GetFileNameWithoutExtension(sourcePath), folder = Root + "/Generated/" + name;
        Folder(folder); string prefabPath = folder + "/PF_Debris_" + name + ".prefab";
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) return new MainTownDestructionCatalog.Definition { source = sourcePrefab, debris = existing, bounds = BoundsFor(sourcePrefab), tree = TreeName(name) };
        var scene = EditorSceneManager.NewPreviewScene(); GameObject original = null, debris = null;
        var transient = new List<Object>(); var state = UnityEngine.Random.state;
        try
        {
            original = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, scene); debris = new GameObject("Debris " + name); SceneManager.MoveGameObjectToScene(debris, scene);
            var lods = original.GetComponentsInChildren<LODGroup>(true);
            var renderers = lods.Length == 0 ? original.GetComponentsInChildren<MeshRenderer>(true) : lods.SelectMany(l => l.GetLODs()[0].renderers).OfType<MeshRenderer>().Distinct().ToArray();
            bool tree = TreeName(name); var bounds = BoundsFor(original); var bodies = new List<Rigidbody>();
            var cut = new Material(Shader.Find("Universal Render Pipeline/Lit")); cut.name = name + " wood interior";
            cut.SetColor("_BaseColor", PropName(name) && (name.Contains("Jar") || name.Contains("Clay")) ? new Color(.25f, .16f, .09f) : new Color(.31f, .20f, .11f)); cut.SetFloat("_Smoothness", .04f);
            AssetDatabase.CreateAsset(cut, folder + "/Cut.mat");
            foreach (var renderer in renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>(); if (filter == null || filter.sharedMesh == null) continue;
                var meshSource = MainTownMeshSlicer.Read(filter.sharedMesh, original.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                var meshes = new List<Mesh>(); var offsets = new Dictionary<Mesh, Vector3>(); bool voronoi = !tree && meshSource.triangles.Length == 1 && Closed(meshSource);
                if (voronoi)
                {
                    var readable = new Mesh(); transient.Add(readable); readable.vertices = meshSource.vertices.Select(v => v.p).ToArray(); readable.normals = meshSource.vertices.Select(v => v.n).ToArray(); readable.uv = meshSource.vertices.Select(v => v.uv).ToArray(); readable.triangles = meshSource.triangles[0]; readable.RecalculateTangents(); readable.RecalculateBounds();
                    UnityEngine.Random.InitState(17361);
                    var pieces = MainTownMeshFragmenter.Fragment(readable, 8, new Vector3(2.33f, 7.5f, 17.6f));
                    meshes.AddRange(pieces.Select(p => p.Mesh));
                    foreach (var fragment in pieces) offsets.Add(fragment.Mesh, fragment.Centroid);
                    if (meshes.Count == 0 || meshes.Any(m => m.vertexCount == 0)) throw new InvalidOperationException("Empty fragments: " + name);
                }
                else
                {
                    float low = meshSource.bounds.min.y - .001f, high = meshSource.bounds.max.y + .001f;
                    float[] cuts = tree && bounds.size.y >= 3 ? new[] { low, low + .6f, low + 1.6f, high } : new[] { low, Mathf.Lerp(low, high, .25f), Mathf.Lerp(low, high, .5f), Mathf.Lerp(low, high, .75f), high };
                    for (int i = 0; i < cuts.Length - 1; i++) { var mesh = MainTownMeshSlicer.Slice(meshSource, cuts[i], cuts[i + 1], true); if (mesh.vertexCount > 0) meshes.Add(mesh); else Object.DestroyImmediate(mesh); }
                }
                foreach (var mesh in meshes)
                {
                    int index = bodies.Count; mesh.name = name + " fragment " + index; AssetDatabase.CreateAsset(mesh, folder + "/Fragment_" + index + ".asset");
                    var piece = new GameObject("Fragment " + index); piece.transform.SetParent(debris.transform, false); piece.layer = 2;
                    if (offsets.TryGetValue(mesh, out var offset)) piece.transform.localPosition = offset;
                    piece.AddComponent<MeshFilter>().sharedMesh = mesh; var visual = piece.AddComponent<MeshRenderer>();
                    visual.sharedMaterials = voronoi ? new[] { renderer.sharedMaterials[0], cut } : renderer.sharedMaterials.Concat(new[] { cut }).ToArray();
                    visual.shadowCastingMode = tree ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    Collider shape;
                    if (tree && bounds.size.y >= 3)
                    {
                        var capsule = piece.AddComponent<CapsuleCollider>(); capsule.direction = 1;
                        capsule.center = new Vector3(0, mesh.bounds.center.y, 0); capsule.radius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * .06f, .12f, .35f);
                        capsule.height = Mathf.Max(capsule.radius * 2, mesh.bounds.size.y); shape = capsule;
                    }
                    else { var box = piece.AddComponent<BoxCollider>(); box.center = mesh.bounds.center; box.size = Vector3.Max(mesh.bounds.size, Vector3.one * .03f); shape = box; }
                    shape.enabled = false; int excluded = 1 << 2; foreach (string layer in new[] { "Player", "Enemy" }) { int id = LayerMask.NameToLayer(layer); if (id >= 0) excluded |= 1 << id; } shape.excludeLayers = excluded;
                    var body = piece.AddComponent<Rigidbody>(); body.mass = tree ? 1f : .12f; body.isKinematic = true; body.useGravity = false; bodies.Add(body);
                }
            }
            if (bodies.Count == 0) throw new InvalidOperationException("No visible LOD0 meshes: " + name);
            debris.AddComponent<MainTownDebris>().bodies = bodies.ToArray();
            var prefab = PrefabUtility.SaveAsPrefabAsset(debris, prefabPath);
            return new MainTownDestructionCatalog.Definition { source = sourcePrefab, debris = prefab, bounds = bounds, tree = tree };
        }
        finally { UnityEngine.Random.state = state; if (original != null) Object.DestroyImmediate(original); if (debris != null) Object.DestroyImmediate(debris); foreach (var item in transient) if (item != null && !AssetDatabase.Contains(item)) Object.DestroyImmediate(item); EditorSceneManager.ClosePreviewScene(scene); }
    }
    public static object RepairFragmentPositions()
    {
        RequireScene();
        var catalog = AssetDatabase.LoadAssetAtPath<MainTownDestructionCatalog>(CatalogPath);
        var repaired = new List<string>();
        foreach (var definition in catalog.definitions)
        {
            if (definition.tree) continue;
            var preview = EditorSceneManager.NewPreviewScene(); GameObject source = null, contents = null;
            var temporary = new List<Object>(); var state = UnityEngine.Random.state;
            try
            {
                source = (GameObject)PrefabUtility.InstantiatePrefab(definition.source, preview);
                var lods = source.GetComponentsInChildren<LODGroup>(true);
                var renderers = lods.Length == 0 ? source.GetComponentsInChildren<MeshRenderer>(true) : lods.SelectMany(l => l.GetLODs()[0].renderers).OfType<MeshRenderer>().Distinct().ToArray();
                string path = AssetDatabase.GetAssetPath(definition.debris);
                contents = PrefabUtility.LoadPrefabContents(path);
                var bodies = contents.GetComponent<MainTownDebris>().bodies; int index = 0; bool changed = false;
                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>(); if (filter == null || filter.sharedMesh == null) continue;
                    var read = MainTownMeshSlicer.Read(filter.sharedMesh, source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                    if (read.triangles.Length != 1 || !Closed(read))
                    {
                        float low = read.bounds.min.y - .001f, high = read.bounds.max.y + .001f;
                        for (int i = 0; i < 4; i++) { var sliced = MainTownMeshSlicer.Slice(read, Mathf.Lerp(low, high, i / 4f), Mathf.Lerp(low, high, (i + 1) / 4f), true); if (sliced.vertexCount > 0) index++; Object.DestroyImmediate(sliced); }
                        continue;
                    }
                    var mesh = new Mesh(); temporary.Add(mesh); mesh.vertices = read.vertices.Select(v => v.p).ToArray(); mesh.normals = read.vertices.Select(v => v.n).ToArray(); mesh.uv = read.vertices.Select(v => v.uv).ToArray(); mesh.triangles = read.triangles[0]; mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    UnityEngine.Random.InitState(17361);
                    var pieces = MainTownMeshFragmenter.Fragment(mesh, 8, new Vector3(2.33f, 7.5f, 17.6f));
                    temporary.AddRange(pieces.Select(p => (Object)p.Mesh));
                    foreach (var piece in pieces)
                    {
                        var body = bodies[index++]; var saved = body.GetComponent<MeshFilter>().sharedMesh;
                        if (!saved.vertices.SequenceEqual(piece.Mesh.vertices) || !saved.triangles.SequenceEqual(piece.Mesh.triangles)) throw new InvalidOperationException("Repair geometry mismatch: " + definition.source.name);
                        if (body.transform.localPosition != piece.Centroid) { body.transform.localPosition = piece.Centroid; changed = true; }
                    }
                }
                if (index != bodies.Length) throw new InvalidOperationException("Repair piece count mismatch: " + definition.source.name);
                if (changed) { PrefabUtility.SaveAsPrefabAsset(contents, path); repaired.Add(path); }
            }
            finally { UnityEngine.Random.state = state; if (contents != null) PrefabUtility.UnloadPrefabContents(contents); if (source != null) Object.DestroyImmediate(source); foreach (var item in temporary) if (item != null && !AssetDatabase.Contains(item)) Object.DestroyImmediate(item); EditorSceneManager.ClosePreviewScene(preview); }
        }
        return new { status = "PASS", repaired };
    }
    static Bounds BoundsFor(GameObject source)
    {
        var filters = source.GetComponentsInChildren<MeshFilter>(true); Bounds bounds = default; bool first = true;
        foreach (var filter in filters)
        {
            if (filter.sharedMesh == null) continue; var matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var b = filter.sharedMesh.bounds;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            { var point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))); if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point); }
        }
        return bounds;
    }
    static bool Closed(MainTownMeshSlicer.Source source)
    {
        var vertices = new Dictionary<Vector3Int, int>(); var indices = new int[source.vertices.Length];
        for (int i = 0; i < indices.Length; i++) { var p = source.vertices[i].p * 10000; var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z)); if (!vertices.TryGetValue(key, out int index)) { index = vertices.Count; vertices.Add(key, index); } indices[i] = index; }
        var edges = new Dictionary<(int, int), int>();
        foreach (var triangles in source.triangles) for (int i = 0; i < triangles.Length; i += 3) for (int k = 0; k < 3; k++) { int a = indices[triangles[i + k]], b = indices[triangles[i + (k + 1) % 3]]; if (a == b) continue; var edge = a < b ? (a, b) : (b, a); edges.TryGetValue(edge, out int count); edges[edge] = count + 1; }
        return edges.Count > 0 && edges.Values.All(count => count == 2);
    }
}
