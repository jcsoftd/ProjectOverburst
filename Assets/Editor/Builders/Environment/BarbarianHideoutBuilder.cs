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

public static partial class BarbarianHideoutBuilder
{
    public const string HideoutPath = "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    public const string VendorRoot = "Assets/ThirdParty/04_환경맵/TopDown Barbarian Camp";
    public const string DemoPath = VendorRoot + "/TopDown_BarbarianCamp_Scene.unity";
    public const string GeneratedRoot = VendorRoot + "/OVERBURST_URP";
    public const string EnvironmentPath = GeneratedRoot + "/PF_BarbarianCamp_Hideout.prefab";
    public static readonly Vector3 CampOffset = new Vector3(7f, .02f, 18f);
    static readonly Vector3 TrainingOffset = new Vector3(45f, 0f, 5f);
    static readonly Vector3 PickupOrigin = new Vector3(45f, 0f, 17f);

    public static string Build(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        RequireIdle();
        var active = SceneManager.GetActiveScene();
        var loaded = SceneManager.GetSceneByPath(HideoutPath);
        if (loaded.IsValid()) throw new InvalidOperationException("Close Hideout before rebuilding; preserve any dirty scene.");
        if (!File.Exists(Path.Combine(output, "before_manifest.json"))) throw new IOException("Scene backup is required.");
        var backup = Path.Combine(output, "Before/ProjectOverburst", HideoutPath);
        if (!File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(HideoutPath)))
            throw new InvalidOperationException("Hideout changed after backup; review before rebuilding.");
        var generated = BuildEnvironment();
        var scene = EditorSceneManager.OpenScene(HideoutPath, OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var ground = roots.Single(r => r.name == "Ground");
            var groundObjects = new HashSet<Object>(ground.GetComponentsInChildren<Component>(true).Cast<Object>());
            groundObjects.UnionWith(ground.GetComponentsInChildren<Transform>(true).Select(t => (Object)t.gameObject));
            foreach (var component in roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c != null))
            {
                var fields = new SerializedObject(component).GetIterator();
                while (fields.Next(true))
                    if (fields.propertyType == SerializedPropertyType.ObjectReference && groundObjects.Contains(fields.objectReferenceValue))
                        throw new InvalidOperationException("Old ground is referenced by " + component.name + ": " + fields.propertyPath);
            }
            Object.DestroyImmediate(ground);
            roots = roots.Where(r => r != null).ToArray();
            var environment = (GameObject)PrefabUtility.InstantiatePrefab(generated, scene);
            environment.name = "Barbarian Camp Environment";
            var services = NewRoot("Hideout Services", scene);
            var training = NewRoot("Training Grounds", scene);
            var pickup = NewRoot("Test Pickup Origin", scene);
            pickup.transform.position = PickupOrigin;
            MoveService(roots, services.transform, "StashObject", new Vector3(-4.5f, .55f, 7.5f));
            MoveService(roots, services.transform, "GeneralGoodsMerchantObject", new Vector3(-5.6f, .9f, 11.8f));
            MoveService(roots, services.transform, "WeaponMerchantObject", new Vector3(7.5f, .9f, 7f));
            var spawn = roots.Single(r => r.name == "HubReturnPoint_Default");
            spawn.transform.SetParent(services.transform, true);
            spawn.transform.SetPositionAndRotation(new Vector3(1f, .12f, 9f), Quaternion.Euler(0f, 180f, 0f));
            var portals = roots.Single(r => r.name == "HubPortals");
            portals.transform.SetParent(services.transform, true);
            var portal = portals.GetComponentsInChildren<HubReturnPoint>(true).Single(p => p.ReturnPointId == "DungeonPortal");
            portal.transform.SetPositionAndRotation(new Vector3(11f, .12f, 18f), Quaternion.Euler(0f, 180f, 0f));
            foreach (var root in roots.Where(r => r.name == "TrainingDummy" || r.name == "anitest"
                || r.name.StartsWith("TrainingMonster_", StringComparison.Ordinal)
                || r.name.StartsWith("ParryPracticeStation_", StringComparison.Ordinal)))
            {
                root.transform.position += TrainingOffset;
                root.transform.SetParent(training.transform, true);
            }
            foreach (var root in roots.Where(r => r.name.StartsWith("GRS024_TrainingIronGreatsword_Hideout_", StringComparison.Ordinal)))
            {
                root.transform.position += PickupOrigin;
                root.transform.SetParent(pickup.transform, true);
            }
            var spawner = roots.SelectMany(r => r.GetComponentsInChildren<ItemPickupSpawner>(true)).Single();
            var spawnerFields = new SerializedObject(spawner);
            spawnerFields.FindProperty("authoredSpawnOrigin").objectReferenceValue = pickup.transform;
            spawnerFields.ApplyModifiedPropertiesWithoutUndo();
            SceneManager.SetActiveScene(scene);
            ConfigureLighting(roots);
            var checks = ValidateLoaded(scene);
            if (!EditorSceneManager.SaveScene(scene, HideoutPath)) throw new IOException("Hideout save failed.");
            File.WriteAllText(Path.Combine(output, "build_result.json"), JsonConvert.SerializeObject(new {
                status = "PASS", checks, scene = HideoutPath, environment = EnvironmentPath,
                prefabGuid = AssetDatabase.AssetPathToGUID(EnvironmentPath), hideoutGuid = AssetDatabase.AssetPathToGUID(HideoutPath)
            }, Formatting.Indented));
            return "Barbarian Camp applied; " + checks.Count + " scene checks passed.";
        }
        finally
        {
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static GameObject BuildEnvironment()
    {
        EnsureFolder(GeneratedRoot);
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
        if (existing != null) return existing; // Reuse without overwriting any authored prefab edits.
        var materials = BuildMaterials();
        var scene = EditorSceneManager.OpenPreviewScene(DemoPath);
        GameObject root = null;
        try
        {
            root = NewRoot("PF_BarbarianCamp_Hideout", scene);
            foreach (var original in scene.GetRootGameObjects().Where(r => r != root).ToArray())
            {
                if (original.GetComponent<Light>() != null || original.GetComponent<Camera>() != null) continue;
                original.transform.SetParent(root.transform, true);
                original.transform.position += CampOffset;
            }
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m != null && materials.TryGetValue(m, out var converted) ? converted : m).ToArray();
                renderer.lightmapIndex = -1;
                if (renderer is MeshRenderer meshRenderer) meshRenderer.receiveGI = ReceiveGI.LightProbes;
            }
            var ground = root.transform.Find("Plane");
            if (ground == null || ground.GetComponent<MeshCollider>() == null) throw new InvalidOperationException("Demo ground is missing.");
            ground.name = "Camp Ground";
            ground.gameObject.layer = LayerMask.NameToLayer("Ground");
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.transform == ground || filter.sharedMesh == null) continue;
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || renderer.bounds.size.y < .4f
                    || Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z) < .45f) continue;
                if (filter.GetComponent<Collider>() == null)
                {
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false;
                }
            }
            AddBoundary(root.transform, ground.GetComponent<Renderer>().bounds);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPath);
            if (prefab == null) throw new IOException("Environment prefab save failed.");
            return prefab;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static Dictionary<Material, Material> BuildMaterials()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
        var result = new Dictionary<Material, Material>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { VendorRoot + "/Materials" }))
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            string path = GeneratedRoot + "/" + original.name + "_URP.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) throw new InvalidOperationException("Material already exists: " + path);
            var material = new Material(shader) { name = original.name + "_URP" };
            CopyTexture(original, material, "_MainTex", "_BaseMap");
            CopyTexture(original, material, "_BumpMap", "_BumpMap");
            CopyTexture(original, material, "_MetallicGlossMap", "_MetallicGlossMap");
            CopyTexture(original, material, "_OcclusionMap", "_OcclusionMap");
            CopyTexture(original, material, "_EmissionMap", "_EmissionMap");
            if (original.HasProperty("_Color")) material.SetColor("_BaseColor", original.GetColor("_Color"));
            if (original.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", original.GetColor("_EmissionColor"));
            foreach (var field in new[] { "_Metallic", "_BumpScale", "_Cutoff", "_OcclusionStrength" })
                if (original.HasProperty(field)) material.SetFloat(field, original.GetFloat(field));
            if (original.HasProperty("_Glossiness")) material.SetFloat("_Smoothness", original.GetFloat("_Glossiness"));
            if (material.GetTexture("_BumpMap") != null) material.EnableKeyword("_NORMALMAP");
            if (material.GetTexture("_OcclusionMap") != null) material.EnableKeyword("_OCCLUSIONMAP");
            if (material.GetTexture("_MetallicGlossMap") != null) material.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (original.HasProperty("_Mode") && Mathf.RoundToInt(original.GetFloat("_Mode")) == 1)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.SetOverrideTag("RenderType", "TransparentCutout");
            }
            AssetDatabase.CreateAsset(material, path);
            result.Add(original, material);
        }
        if (result.Count != 7) throw new InvalidOperationException("Expected seven source materials.");
        return result;
    }

    static void CopyTexture(Material original, Material target, string from, string to)
    {
        if (!original.HasProperty(from)) return;
        target.SetTexture(to, original.GetTexture(from));
        target.SetTextureScale(to, original.GetTextureScale(from));
        target.SetTextureOffset(to, original.GetTextureOffset(from));
    }

    static void ConfigureLighting(GameObject[] roots)
    {
        var sun = roots.Single(r => r.name == "Directional Light").GetComponent<Light>();
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        sun.color = new Color(1f, .9568f, .8392f);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.lightmapBakeType = LightmapBakeType.Realtime;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.48f, .52f, .58f);
        RenderSettings.ambientEquatorColor = new Color(.38f, .37f, .34f);
        RenderSettings.ambientGroundColor = new Color(.22f, .20f, .17f);
        RenderSettings.fog = false;
    }

    static void AddBoundary(Transform root, Bounds ground)
    {
        int layer = LayerMask.NameToLayer("PlayerBoundary");
        if (layer < 0) throw new InvalidOperationException("PlayerBoundary layer is missing.");
        for (int i = 0; i < 4; i++)
        {
            var wall = new GameObject("Camp Boundary " + i);
            wall.transform.SetParent(root, false);
            wall.layer = layer;
            bool side = i < 2;
            float sign = i % 2 == 0 ? -1f : 1f;
            wall.transform.position = ground.center + new Vector3(side ? sign * ground.extents.x : 0f, 2.5f,
                side ? 0f : sign * ground.extents.z);
            var box = wall.AddComponent<BoxCollider>();
            box.size = side ? new Vector3(1f, 5f, ground.size.z) : new Vector3(ground.size.x, 5f, 1f);
        }
    }

    static void MoveService(GameObject[] roots, Transform parent, string name, Vector3 position)
    {
        var root = roots.Single(r => r.name == name);
        root.transform.SetParent(parent, true);
        root.transform.position = position;
    }

    public static List<string> ValidateLoaded(Scene scene)
    {
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(message);
        }
        var roots = scene.GetRootGameObjects();
        var transforms = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        foreach (var transform in transforms)
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing Script 0: " + transform.name);
        var environment = roots.Single(r => r.name == "Barbarian Camp Environment");
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(environment);
        Check(prefabPath == EnvironmentPath || prefabPath == ExpandedPath, "Connected environment prefab.");
        var renderers = environment.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
        var originalCamp = environment.transform.Find("TD_Barbarian_Camp_Scene");
        Check(originalCamp.GetComponentsInChildren<Renderer>(true).Length == 514, "All 514 original camp renderers retained, plus floor.");
        foreach (var renderer in renderers)
            Check(renderer.sharedMaterials.All(m => m != null && m.shader != null && m.shader.name == "Universal Render Pipeline/Lit"
                && m.GetTexture("_BaseMap") != null && !ShaderUtil.ShaderHasError(m.shader)), "URP texture/shader valid: " + renderer.name);
        foreach (var ps in roots.SelectMany(r => r.GetComponentsInChildren<ParticleSystem>(true)))
        {
            var velocity = ps.velocityOverLifetime;
            Check(!velocity.enabled || (velocity.x.mode == velocity.y.mode && velocity.x.mode == velocity.z.mode),"Particle velocity axes use one mode: " + ps.name);
            var mat = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Check(mat != null && mat.shader.name == "Universal Render Pipeline/Particles/Unlit" && mat.GetTexture("_BaseMap") != null && !ShaderUtil.ShaderHasError(mat.shader),"Atmosphere particle texture/shader valid: " + ps.name);
        }
        var ground = transforms.Single(t => t.name == "Camp Ground");
        Check(ground.gameObject.layer == LayerMask.NameToLayer("Ground") && ground.GetComponent<Collider>() != null, "Camp floor uses Ground layer.");
        Check(environment.GetComponentsInChildren<Collider>(true).Length > 100, "Solid camp obstacles and boundaries.");
        Check(roots.SelectMany(r => r.GetComponentsInChildren<StashInteractable>(true)).Count() == 1, "One stash retained.");
        Check(roots.SelectMany(r => r.GetComponentsInChildren<GeneralGoodsMerchantInteractable>(true)).Count() == 2, "Both merchants retained.");
        Check(roots.SelectMany(r => r.GetComponentsInChildren<TrainingDummy>(true)).Count() == 1, "Training dummy retained.");
        Check(roots.SelectMany(r => r.GetComponentsInChildren<HideoutParryPracticeStation>(true)).Count() == 2, "Both parry stations retained.");
        var spawner = roots.SelectMany(r => r.GetComponentsInChildren<ItemPickupSpawner>(true)).Single();
        Check(new SerializedObject(spawner).FindProperty("authoredSpawnOrigin").objectReferenceValue != null, "Test pickups use their own origin.");
        var points = roots.SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)).ToArray();
        Check(points.Count(p => p.ReturnPointId == "Default") == 1 && points.Count(p => p.ReturnPointId == "DungeonPortal") == 1, "Both hub return IDs retained.");
        foreach (var point in points)
        {
            Check(ground.GetComponent<Collider>().Raycast(new Ray(point.transform.position + Vector3.up * 10f, Vector3.down), out _, 20f), "Ground under return point: " + point.ReturnPointId);
            Check(ClearCapsule(environment, point.transform.position), "Clear player capsule at return point: " + point.ReturnPointId);
        }
        Check(roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Count() == 0, "Persistent camera remains the only game camera.");
        if (roots.Any(r => r.name == "Weapon Merchant Workshop")) ValidateMerchantWorkshop(scene,checks);
        return checks;
    }

    public static bool ClearCapsule(GameObject environment, Vector3 foot)
    {
        var probe = new GameObject("Temporary Capsule Clearance Probe");
        SceneManager.MoveGameObjectToScene(probe, environment.scene);
        try
        {
            var capsule = probe.AddComponent<CapsuleCollider>();
            capsule.radius = .4f; capsule.height = 1.8f; capsule.center = Vector3.up * .9f;
            capsule.isTrigger = true;
            foreach (var obstacle in environment.GetComponentsInChildren<Collider>(true))
            {
                if (obstacle.name == "Camp Ground" || !obstacle.enabled || obstacle.isTrigger) continue;
                if (Physics.ComputePenetration(capsule, foot, Quaternion.identity, obstacle,
                    obstacle.transform.position, obstacle.transform.rotation, out _, out float distance) && distance > .02f) return false;
            }
            return true;
        }
        finally { Object.DestroyImmediate(probe); }
    }

    public static string Validate(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        RequireIdle();
        var scene = EditorSceneManager.OpenPreviewScene(HideoutPath);
        try
        {
            var checks = ValidateLoaded(scene);
            File.WriteAllText(Path.Combine(output, "asset_result.json"), JsonConvert.SerializeObject(new { status = "PASS", checks = checks.Count, details = checks }, Formatting.Indented));
            return checks.Count + " saved-scene checks PASS.";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static GameObject NewRoot(string name, Scene scene)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root;
    }
    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Use an idle Editor.");
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
