using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Imports the source composition as individual connected props into the product scene.</summary>
public static partial class EditableBarbarianHideoutBuilder
{
    public const string LayoutName = "Camp Layout";
    public const string VariantRoot = "Assets/ProjectOverburst/05_Art/Environment/BarbarianHideout/DemoVariants";
    const string Vendor = BarbarianHideoutBuilder.VendorRoot;
    const string ScenePath = BarbarianHideoutBuilder.HideoutPath;
    class Placement
    {
        public string name, prefab;
        public float[] position, rotation, scale;
        public float error;
    }
    sealed class Variant : Placement
    {
        public int[] faces;
        public float[] pivot;
    }
    static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
    static Quaternion Q(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]);
    static Matrix4x4 Pose(Placement p) => Matrix4x4.TRS(V(p.position), Q(p.rotation), V(p.scale));
    static string VariantPath(int i) => VariantRoot + "/PF_" + "CampCanopy_Demo_" + (i + 1).ToString("00") + ".prefab";

    public static string Build(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        RequireIdle();
        if (SceneManager.GetSceneByPath(ScenePath).IsValid())
            throw new InvalidOperationException("Hideout is already open; preserve its current state.");
        RequireExactBackup(output, ScenePath);
        var placements = JsonConvert.DeserializeObject<Placement[]>(File.ReadAllText(Path.Combine(output, "formal_placement_plan.json")));
        var variants = JsonConvert.DeserializeObject<Variant[]>(File.ReadAllText(Path.Combine(output, "demo_variants_plan.json")));
        if (placements.Length != 514 || variants.Length != 5 || placements.Any(p => p.error > .0001f))
            throw new InvalidOperationException("Complete geometry-matched placement plan required.");
        foreach (var p in placements)
            if (!p.prefab.StartsWith(Vendor + "/", StringComparison.Ordinal) || p.prefab.Contains("/Scene/"))
                throw new InvalidOperationException("Expected an individual source prop: " + p.prefab);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Vendor + "/Meshes/Scene/TD_Barbarian_Camp_Scene.FBX");
        var compound = source.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "Redskin_tent_1");
        BuildVariants(compound, variants);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var old = scene.GetRootGameObjects().Single(r => r.name == "Barbarian Camp Environment");
            var original = old.transform.Find("TD_Barbarian_Camp_Scene");
            if (original == null || original.GetComponentsInChildren<MeshFilter>(true).Length != 517)
                throw new InvalidOperationException("Review the current source camp before replacing it.");
            var oldObjects = old.GetComponentsInChildren<Component>(true).Cast<Object>().ToHashSet();
            foreach (var t in old.GetComponentsInChildren<Transform>(true)) oldObjects.Add(t.gameObject);
            foreach (var behaviour in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).Where(m => m != null && !oldObjects.Contains(m)))
            {
                var property = new SerializedObject(behaviour).GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && oldObjects.Contains(property.objectReferenceValue))
                        throw new InvalidOperationException("Existing environment reference must be remapped: " + behaviour.name + ":" + property.propertyPath);
            }
            var campWorld = original.localToWorldMatrix;
            var environment = Root("Barbarian Camp Environment", scene);
            var layout = Child(LayoutName, environment.transform);
            var categories = new Dictionary<string, Transform>();
            Transform ParentFor(string name)
            {
                string group = Category(name);
                if (!categories.TryGetValue(group, out var parent))
                    categories[group] = parent = Child(group, layout.transform).transform;
                return parent;
            }
            foreach (var p in placements)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);
                if (asset == null) throw new IOException("Prop missing: " + p.prefab);
                var prop = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                prop.name = p.name;
                prop.transform.SetParent(ParentFor(p.name), false);
                SetWorldPose(prop.transform, campWorld * Pose(p));
                ConfigureGeometry(prop);
            }
            // Three individual fences were added in the vendor demo prefab.
            var sourceFilters = source.GetComponentsInChildren<MeshFilter>(true);
            var sourceNames = sourceFilters.Select(f => f.name).ToHashSet();
            var extraFences = original.GetComponentsInChildren<MeshFilter>(true).Where(f => !sourceNames.Contains(f.name)).ToArray();
            if (extraFences.Length != 3 || extraFences.Any(f => !f.name.StartsWith("Fence_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Review vendor demo additions before replacing the environment.");
            foreach (var fence in extraFences)
            {
                var originalFilter = sourceFilters.Single(f => f.sharedMesh == fence.sharedMesh);
                var template = placements.Single(p => p.name == originalFilter.name);
                var prop = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(template.prefab), scene);
                prop.name = fence.name; prop.transform.SetParent(ParentFor(fence.name), false);
                SetWorldPose(prop.transform, fence.transform.localToWorldMatrix * originalFilter.transform.worldToLocalMatrix * Pose(template));
                ConfigureGeometry(prop);
            }
            for (int i = 0; i < variants.Length; i++)
            {
                var prop = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath(i)), scene);
                prop.name = "Demo Canopy " + (i + 1).ToString("00");
                prop.transform.SetParent(ParentFor("Canopy"), false);
                SetWorldPose(prop.transform, campWorld * compound.transform.localToWorldMatrix * Matrix4x4.Translate(V(variants[i].pivot)));
                ConfigureGeometry(prop);
            }
            var oldGround = old.transform.Find("Camp Ground");
            var ground = Child("Camp Ground", environment.transform);
            SetWorldPose(ground.transform, oldGround.localToWorldMatrix);
            ground.layer = LayerMask.NameToLayer("Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = oldGround.GetComponent<MeshFilter>().sharedMesh;
            ground.AddComponent<MeshRenderer>().sharedMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>(Vendor + "/Materials/Mud.mat") };
            ground.AddComponent<MeshCollider>().sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
            var boundaries = Child("Camp Boundaries", environment.transform);
            foreach (var wall in old.GetComponentsInChildren<BoxCollider>(true).Where(c => c.name.StartsWith("Camp Boundary ", StringComparison.Ordinal)))
            {
                var clone = Child(wall.name, boundaries.transform);
                clone.layer = wall.gameObject.layer;
                SetWorldPose(clone.transform, wall.transform.localToWorldMatrix);
                var box = clone.AddComponent<BoxCollider>(); box.center = wall.center; box.size = wall.size;
                box.isTrigger = wall.isTrigger; box.enabled = wall.enabled; box.sharedMaterial = wall.sharedMaterial;
            }
            var effects = old.transform.Find("Camp Atmosphere");
            if (effects == null) throw new InvalidOperationException("Camp atmosphere must be retained.");
            var atmosphere = Object.Instantiate(effects.gameObject, environment.transform);
            atmosphere.name = "Camp Atmosphere";
            SetWorldPose(atmosphere.transform, effects.localToWorldMatrix);
            ReplaceStashVisual(scene);
            RemapCampMaterials(scene);
            Object.DestroyImmediate(old);
            ConfigureServicePrompts(scene);
            var checks = ValidateLoaded(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Hideout save failed.");
            File.WriteAllText(Path.Combine(output, "scene_build_result.json"), JsonConvert.SerializeObject(new {
                status = "PASS", checks, scene = ScenePath, sourceProps = placements.Length, demoCanopies = variants.Length,
                sourceSceneFbxReferences = 0, environmentPrefab = false, prefabInstances = 522, vendorAddedFences = 3,
                guid = AssetDatabase.AssetPathToGUID(ScenePath), servicesRetained = true
            }, Formatting.Indented));
            return "Individual camp props saved; NPCs, stash, portal, training and atmosphere retained.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void BuildVariants(MeshFilter source, Variant[] variants)
    {
        EnsureFolder(VariantRoot);
        var original = source.sharedMesh;
        var allTriangles = original.triangles;
        var ranges = new List<(int start, int end)>();
        int cursor = 0;
        for (int i = 0; i < original.subMeshCount; i++)
        {
            int count = original.GetTriangles(i).Length / 3;
            ranges.Add((cursor, cursor + count)); cursor += count;
        }
        var covered = variants.SelectMany(v => v.faces).ToArray();
        if (covered.Distinct().Count() != 71932 || covered.Length != 71932)
            throw new InvalidOperationException("Demo canopy faces are incomplete or duplicated.");
        for (int i = 0; i < variants.Length; i++)
        {
            string path = VariantPath(i);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
            string meshPath = VariantRoot + "/M_CampCanopy_Demo_" + (i + 1).ToString("00") + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) throw new IOException("Review partial canopy import: " + meshPath);
            var plan = variants[i];
            var ids = plan.faces.SelectMany(f => new[] { allTriangles[f * 3], allTriangles[f * 3 + 1], allTriangles[f * 3 + 2] }).Distinct().OrderBy(v => v).ToArray();
            var remap = ids.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
            var mesh = new Mesh { name = "M_CampCanopy_Demo_" + (i + 1).ToString("00"), indexFormat = ids.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var vertices = original.vertices;
            mesh.vertices = ids.Select(id => vertices[id] - V(plan.pivot)).ToArray();
            var normals = original.normals; if (normals.Length > 0) mesh.normals = ids.Select(id => normals[id]).ToArray();
            var tangents = original.tangents; if (tangents.Length > 0) mesh.tangents = ids.Select(id => tangents[id]).ToArray();
            var colors = original.colors; if (colors.Length > 0) mesh.colors = ids.Select(id => colors[id]).ToArray();
            for (int channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>(); original.GetUVs(channel, uv);
                if (uv.Count > 0) mesh.SetUVs(channel, ids.Select(id => uv[id]).ToList());
            }
            mesh.subMeshCount = original.subMeshCount;
            for (int submesh = 0; submesh < ranges.Count; submesh++)
            {
                var range = ranges[submesh];
                var indices = plan.faces.Where(f => f >= range.start && f < range.end)
                    .SelectMany(f => Enumerable.Range(f * 3, 3).Select(j => remap[allTriangles[j]])).ToArray();
                mesh.SetTriangles(indices, submesh, false);
            }
            mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, meshPath);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = Root("PF_CampCanopy_Demo_" + (i + 1).ToString("00"), preview);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
                root.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException("Canopy prefab save failed.");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }

    static void ReplaceStashVisual(Scene scene)
    {
        const string storagePrefab = "Assets/ProjectOverburst/05_Art/Environment/AnimatedChests/Hideout_Storage_Chest/Prefabs/PF_HideoutStorageChest.prefab";
        var stash = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<StashInteractable>(true)).Single();
        var old = stash.transform.Find("Stash Visual");
        if (old == null) throw new InvalidOperationException("Stash visual is missing.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(storagePrefab);
        if (prefab == null) throw new IOException("Verified hideout storage chest prefab required.");
        var box = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        box.name = "Stash Visual"; box.transform.SetParent(stash.transform, false);
        box.transform.SetPositionAndRotation(new Vector3(stash.transform.position.x, .02f, stash.transform.position.z), Quaternion.identity);
        Object.DestroyImmediate(old.gameObject);
        var controller = stash.GetComponent<StashChestAnimator>();
        if (controller == null) controller = stash.gameObject.AddComponent<StashChestAnimator>();
        var fields = new SerializedObject(controller);
        fields.FindProperty("stash").objectReferenceValue = stash;
        fields.FindProperty("animator").objectReferenceValue = box.GetComponentInChildren<Animator>(true);
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    static void RemapCampMaterials(Scene scene)
    {
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
        {
            var mats = renderer.sharedMaterials; bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                string path = AssetDatabase.GetAssetPath(mats[i]);
                if (!path.StartsWith(BarbarianHideoutBuilder.GeneratedRoot + "/", StringComparison.Ordinal)) continue;
                string name = mats[i].name.Replace("_URP", "");
                mats[i] = AssetDatabase.LoadAssetAtPath<Material>(Vendor + "/Materials/" + name + ".mat");
                if (mats[i] == null) throw new IOException("Source material missing: " + name);
                changed = true;
            }
            if (changed) renderer.sharedMaterials = mats;
        }
    }
    static void ConfigureGeometry(GameObject prop)
    {
        foreach (var filter in prop.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            renderer.lightmapIndex = -1; renderer.receiveGI = ReceiveGI.LightProbes;
            if (filter.sharedMesh != null && renderer.bounds.size.y >= .4f && Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z) >= .45f
                && filter.GetComponent<Collider>() == null)
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }
    }
    static string Category(string name)
    {
        name = name.ToLowerInvariant();
        if (name.Contains("tent") || name.Contains("canopy")) return "Tents and Canopies";
        if (name.Contains("fence")) return "Fences";
        if (name == "boiler") return "Campfire";
        if (name.StartsWith("rock") || name.StartsWith("stone") || name.StartsWith("tree") || name.StartsWith("branch") || name.StartsWith("bush")) return "Nature";
        if (name.StartsWith("barrel") || name.StartsWith("box") || name.StartsWith("cart") || name.StartsWith("sack") || name.StartsWith("basket")) return "Supplies";
        return "Camp Props";
    }
    static GameObject Root(string name, Scene scene)
    {
        var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene); return root;
    }
    static GameObject Child(string name, Transform parent)
    {
        var root = Root(name, parent.gameObject.scene); root.transform.SetParent(parent, false); return root;
    }
    static void SetWorldPose(Transform transform, Matrix4x4 matrix)
    {
        var local = transform.parent != null ? transform.parent.worldToLocalMatrix * matrix : matrix;
        var scale = new Vector3(local.GetColumn(0).magnitude, local.GetColumn(1).magnitude, local.GetColumn(2).magnitude);
        if (local.determinant < 0) scale.x *= -1;
        var rotation = Quaternion.LookRotation((Vector3)local.GetColumn(2) / scale.z, (Vector3)local.GetColumn(1) / scale.y);
        var rebuilt = Matrix4x4.TRS(local.GetColumn(3), rotation, scale);
        for (int i = 0; i < 16; i++) if (Mathf.Abs(local[i] - rebuilt[i]) > .001f)
            throw new InvalidOperationException("Non-TRS source prop: " + transform.name);
        transform.localPosition = local.GetColumn(3); transform.localRotation = rotation; transform.localScale = scale;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash);
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
    static void RequireExactBackup(string output, string path)
    {
        string backup = Path.Combine(output, "Before", path);
        if (!File.Exists(backup) || !File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(path)))
            throw new IOException("Exact current backup required: " + path);
    }
    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("Idle Editor and real-account routing required.");
    }

    public static List<string> ValidateLoaded(Scene scene)
    {
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var roots = scene.GetRootGameObjects();
        var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var environment = roots.Single(r => r.name == "Barbarian Camp Environment");
        var layout = environment.transform.Find(LayoutName);
        Check(!PrefabUtility.IsPartOfPrefabInstance(environment), "Environment is an editable scene root.");
        Check(layout != null && layout.GetComponentsInChildren<MeshFilter>(true).Length == 522, "522 independently connected camp props.");
        Check(environment.transform.Find("TD_Barbarian_Camp_Scene") == null, "No whole-scene FBX instance.");
        foreach (var t in all) Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing Script 0: " + t.name);
        foreach (var filter in all.Select(t => t.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null))
            Check(!AssetDatabase.GetAssetPath(filter.sharedMesh).Contains(Vendor + "/Meshes/Scene/"), "Individual mesh source: " + filter.name);
        foreach (var renderer in all.Select(t => t.GetComponent<Renderer>()).Where(r => r != null))
            foreach (var mat in renderer.sharedMaterials)
            {
                Check(mat != null && mat.shader != null && mat.shader.isSupported && !ShaderUtil.ShaderHasError(mat.shader), "Shader valid: " + renderer.name);
                Check(!AssetDatabase.GetAssetPath(mat).StartsWith(BarbarianHideoutBuilder.GeneratedRoot + "/", StringComparison.Ordinal), "No legacy camp material: " + renderer.name);
            }
        foreach (var category in layout.Cast<Transform>())
            foreach (var prop in category.Cast<Transform>())
                Check(PrefabUtility.IsAnyPrefabInstanceRoot(prop.gameObject) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prop.gameObject) != BarbarianHideoutBuilder.EnvironmentPath,
                    "Connected individual prop: " + prop.name);
        var ground = environment.transform.Find("Camp Ground");
        Check(ground != null && ground.gameObject.layer == LayerMask.NameToLayer("Ground") && ground.GetComponent<Collider>() != null, "Ground and collision retained.");
        Check(environment.GetComponentsInChildren<Collider>(true).Length > 100, "Camp obstacle collision retained.");
        Check(environment.GetComponentsInChildren<ParticleSystem>(true).Length == 5, "Five camp atmosphere effects retained.");
        Check(roots.SelectMany(r => r.GetComponentsInChildren<StashInteractable>(true)).Count() == 1, "One stash retained.");
        var merchants = roots.SelectMany(r => r.GetComponentsInChildren<GeneralGoodsMerchantInteractable>(true)).ToArray();
        Check(merchants.Length == 2 && merchants.All(m => m.transform.Find("Merchant Visual") != null && m.transform.position.z >= 6), "Both NPCs retained away from south foreground.");
        var stash = roots.SelectMany(r => r.GetComponentsInChildren<StashInteractable>(true)).Single();
        var storage = stash.transform.Find("Stash Visual");
        Check(storage != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(storage.gameObject).EndsWith("/PF_HideoutStorageChest.prefab", StringComparison.Ordinal), "New storage prefab connected.");
        var storageAnimator = storage.GetComponentInChildren<Animator>(true);
        var hook = stash.GetComponent<StashChestAnimator>();
        Check(storageAnimator != null && storageAnimator.runtimeAnimatorController != null && hook != null, "Storage animation controller and UI hook retained.");
        var hookFields = new SerializedObject(hook);
        Check(hookFields.FindProperty("stash").objectReferenceValue == stash && hookFields.FindProperty("animator").objectReferenceValue == storageAnimator, "Storage UI and lid bindings saved.");
        foreach (var service in merchants.Cast<MonoBehaviour>().Concat(new[] { stash }))
        {
            var fields = new SerializedObject(service);
            var promptRoot = fields.FindProperty("promptRoot").objectReferenceValue as GameObject;
            var prompt = promptRoot != null ? promptRoot.GetComponent<WorldInteractionKeyPrompt>() : null;
            Check(prompt != null && prompt.HasUsableView && prompt.KeyLabel == "F", "Saved F keycap binding: " + service.name);
            Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(promptRoot) == PromptPath, "Connected F keycap prefab: " + service.name);
            if (service is GeneralGoodsMerchantInteractable)
                Check(prompt.transform.position.y > service.transform.position.y + 2.2f, "NPC keycap position saved above head: " + service.name);
        }

        Check(roots.SelectMany(r => r.GetComponentsInChildren<TrainingDummy>(true)).Count() == 1, "Training dummy retained.");
        Check(roots.SelectMany(r => r.GetComponentsInChildren<HideoutParryPracticeStation>(true)).Count() == 2, "Parry training retained.");
        var points = roots.SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)).ToArray();
        Check(points.Count(p => p.ReturnPointId == "Default") == 1 && points.Count(p => p.ReturnPointId == "DungeonPortal") == 1, "Both return anchors retained.");
        var spawn = points.Single(p => p.ReturnPointId == "Default");
        var fire = layout.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Boiler");
        var distance = spawn.transform.position - fire.bounds.center; distance.y = 0;
        Check(distance.magnitude <= 4.5f, "Player starts near campfire.");
        var pickup = roots.Single(r => r.name == "Test Pickup Origin");
        Check(pickup.transform.position == new Vector3(0, 0, -12), "Startup pickup origin outside south entrance.");
        var spawner = roots.SelectMany(r => r.GetComponentsInChildren<ItemPickupSpawner>(true)).Single();
        Check(new SerializedObject(spawner).FindProperty("authoredSpawnOrigin").objectReferenceValue == pickup.transform, "Pickup origin connected.");
        foreach (var point in points)
        {
            Check(ground.GetComponent<Collider>().Raycast(new Ray(point.transform.position + Vector3.up * 10, Vector3.down), out _, 20), "Ground under anchor: " + point.ReturnPointId);
            Check(BarbarianHideoutBuilder.ClearCapsule(environment, point.transform.position), "Clear anchor: " + point.ReturnPointId);
        }
        Check(roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Count() == 0, "Persistent game camera retained.");
        return checks;
    }
}
