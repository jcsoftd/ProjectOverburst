using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class BarbarianHideoutBuilder
{
    const string CookSource = "Assets/ProjectOverburst/02_Shared/CharacterVisual/Prefabs/Medieval_NPC_Pack_2/Cook/Prefab/SK_Cook_Skin1.prefab";
    const string CookModel = "Assets/ThirdParty/02_인간캐릭터/Medieval_NPC_Pack_2/Cook/Base Mesh/SK_Cook_Skin1.fbx";
    public const string CookVisualPath = MerchantGenerated + "/PF_HideoutProvisionMerchantVisual.prefab";
    static readonly Vector3 CourtyardCenter = new Vector3(0, 0, 8);

    // Use the visible geometry centre: several vendor meshes have corner or remote pivots.
    static void PlaceBounds(Transform transform, Vector3 centre, float yaw)
    {
        transform.rotation = Quaternion.Euler(0, yaw, 0);
        var bounds = CombinedBounds(transform.gameObject);
        transform.position += new Vector3(centre.x - bounds.center.x, centre.y - bounds.min.y, centre.z - bounds.center.z);
    }

    static GameObject BuildProvisionVisual()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CookVisualPath);
        if (existing != null) return existing;
        var root = PrefabUtility.LoadPrefabContents(CookSource);
        try
        {
            root.name = "PF_HideoutProvisionMerchantVisual";
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(CookModel).OfType<Avatar>().Single(a => a.isHuman && a.isValid);
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(MerchantGenerated + "/AC_HideoutMerchantIdle.controller");
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return PrefabUtility.SaveAsPrefabAsset(root, CookVisualPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void DressMerchant(GeneralGoodsMerchantInteractable merchant, GameObject visual, Scene scene, Vector3 target)
    {
        merchant.transform.position = target; merchant.transform.localScale = Vector3.one;
        var direction = CourtyardCenter - target; direction.y = 0;
        merchant.transform.rotation = Quaternion.LookRotation(direction);
        merchant.GetComponent<MeshRenderer>().enabled = false;
        var collider = merchant.GetComponent<CapsuleCollider>();
        collider.center = new Vector3(0, .9f, 0); collider.height = 1.8f; collider.radius = .35f;
        var old = merchant.transform.Find("Merchant Visual");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(visual, scene);
        instance.name = "Merchant Visual"; instance.transform.SetParent(merchant.transform, false);
        instance.transform.localPosition = Vector3.zero; instance.transform.localRotation = Quaternion.identity;
        var prompt = merchant.transform.Find("PromptRoot");
        if (prompt != null) prompt.localPosition = new Vector3(0, 2.2f, 0);
    }

    static Vector3 NearbyClear(GameObject environment, GameObject workshop, Vector3 target)
    {
        // Search only inside this authored stall, keeping the NPC in its assigned district.
        for (float distance = 0; distance <= 2; distance += .25f)
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8;
                var candidate = target + new Vector3(Mathf.Sin(a) * distance, 0, Mathf.Cos(a) * distance);
                if (ClearCapsule(environment, candidate) && ClearCapsule(workshop, candidate)) return candidate;
            }
        throw new InvalidOperationException("No clear floor within assigned stall: " + target);
    }

    static void ArrangeServices(Scene scene)
    {
        var roots = scene.GetRootGameObjects(); var environment = roots.Single(r => r.name == "Barbarian Camp Environment");
        var workshop = roots.Single(r => r.name == "Weapon Merchant Workshop");
        workshop.transform.position = new Vector3(8.5f, 0, 7);
        var merchants = roots.SelectMany(r => r.GetComponentsInChildren<GeneralGoodsMerchantInteractable>(true)).ToArray();
        var weapon = merchants.Single(m => m.name == "WeaponMerchantObject"); var provision = merchants.Single(m => m != weapon);
        DressMerchant(weapon, BuildMerchantVisual(), scene, NearbyClear(environment, workshop, new Vector3(7, 0, 6)));
        DressMerchant(provision, BuildProvisionVisual(), scene, NearbyClear(environment, workshop, new Vector3(-7, 0, 6)));
        var stash = roots.SelectMany(r => r.GetComponentsInChildren<StashInteractable>(true)).Single();
        stash.transform.localScale = Vector3.one;
        stash.transform.position = new Vector3(0, .55f, 9);
        stash.GetComponent<MeshRenderer>().enabled = false;
        var stashVisual = stash.transform.Find("Stash Visual");
        if (stashVisual != null) Object.DestroyImmediate(stashVisual.gameObject);
        var box = Object.Instantiate(environment.transform.Find("TD_Barbarian_Camp_Scene/Box_large").gameObject, stash.transform);
        box.name = "Stash Visual"; box.transform.localScale *= 1.6f;
        PlaceBounds(box.transform, new Vector3(stash.transform.position.x, .02f, stash.transform.position.z), 0);
        var prompt = stash.transform.Find("PromptRoot"); if (prompt != null) prompt.position = stash.transform.position + Vector3.up;
        var points = roots.SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)).ToArray();
        var spawn = points.Single(p => p.ReturnPointId == "Default");
        var boiler = environment.transform.Find("TD_Barbarian_Camp_Scene/Boiler").GetComponent<MeshRenderer>();
        var nearFire = boiler.bounds.center + Vector3.back * 3; nearFire.y = .12f;
        spawn.transform.position = NearbyClear(environment, workshop, nearFire);
        spawn.transform.rotation = Quaternion.identity;
        roots.Single(r => r.name == "Test Pickup Origin").transform.position = new Vector3(0, 0, -12);
        var portal = points.Single(p => p.ReturnPointId == "DungeonPortal");
        portal.transform.position = NearbyClear(environment, workshop, new Vector3(-11, .12f, 10));
        portal.transform.rotation = Quaternion.Euler(0, 180, 0);
    }

    public static string ApplyOriginalCamp(string output, string backupFolder = "BeforeOriginalOnly")
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output); RequireIdle();
        if (SceneManager.GetSceneByPath(HideoutPath).IsValid()) throw new InvalidOperationException("Close Hideout first; preserve any dirty scene.");
        string backup = Path.Combine(output, backupFolder, HideoutPath);
        if (!File.Exists(backup) || !File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(HideoutPath)))
            throw new IOException("Current Hideout scene backup required.");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(HideoutPath, OpenSceneMode.Additive);
        try
        {
            var old = scene.GetRootGameObjects().Single(r => r.name == "Barbarian Camp Environment");
            var original = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath), scene);
            original.name = old.name;
            original.transform.SetPositionAndRotation(new Vector3(0, 0, 12), Quaternion.Euler(0, 180, 0));
            Object.DestroyImmediate(old);
            // Keep the supplied tent, fence and small-prop transforms intact.
            AddCampAtmosphere(original);
            ArrangeServices(scene);
            SceneManager.SetActiveScene(scene); ApplyCampLighting(scene);
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(AtmosphereProfile);
            profile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var color);
            color.postExposure.Override(.8f); EditorUtility.SetDirty(color); AssetDatabase.SaveAssetIfDirty(profile);
            var checks = ValidateLoaded(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Original camp scene save failed.");
            File.WriteAllText(Path.Combine(output, "original_only_result.json"), JsonConvert.SerializeObject(new {
                status = "PASS", scene = HideoutPath, environment = EnvironmentPath, checks = checks.Count,
                sourceCompositionPreserved = true, campfireSpawn = true, pickupsOutsideSouthEntrance = true
            }, Formatting.Indented));
            return "Original camp applied to product Hideout; source composition preserved.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void ValidateOriginalLayout(Scene scene, List<string> checks)
    {
        var roots = scene.GetRootGameObjects();
        var environment = roots.Single(r => r.name == "Barbarian Camp Environment");
        var workshop = roots.Single(r => r.name == "Weapon Merchant Workshop");
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var camp = environment.transform.Find("TD_Barbarian_Camp_Scene");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath).transform.Find("TD_Barbarian_Camp_Scene");
        foreach (var transform in camp.GetComponentsInChildren<Transform>(true))
        {
            string path = AnimationUtility.CalculateTransformPath(transform, camp);
            var original = string.IsNullOrEmpty(path) ? source : source.Find(path);
            Check(original != null && Vector3.Distance(transform.localPosition, original.localPosition) < .0001f
                && Quaternion.Angle(transform.localRotation, original.localRotation) < .001f
                && Vector3.Distance(transform.localScale, original.localScale) < .0001f,
                "Original camp transform preserved: " + path);
            var mesh = transform.GetComponent<MeshFilter>();
            if (mesh != null) Check(mesh.sharedMesh == original.GetComponent<MeshFilter>().sharedMesh, "Original camp mesh preserved: " + path);
        }
        Check(environment.transform.Find("Camp Extensions") == null && environment.transform.Find("Camp Perimeter") == null
            && environment.transform.Find("Camp Rest Area") == null, "Original composition has no expanded additions.");
        foreach (var merchant in roots.SelectMany(r => r.GetComponentsInChildren<GeneralGoodsMerchantInteractable>(true)))
        {
            Check(merchant.transform.position.z >= 6, "NPC stays away from south foreground: " + merchant.name);
            Check(ClearCapsule(environment, merchant.transform.position) && ClearCapsule(workshop, merchant.transform.position), "NPC feet clear: " + merchant.name);
        }
        var spawn = roots.SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)).Single(p => p.ReturnPointId == "Default");
        var distance = spawn.transform.position - camp.Find("Boiler").GetComponent<MeshRenderer>().bounds.center; distance.y = 0;
        Check(distance.magnitude <= 4.5f, "Default spawn near campfire.");
        var pickup = roots.Single(r => r.name == "Test Pickup Origin").transform;
        Check(pickup.position.z == -12 && pickup.position.x == 0, "Startup item origin outside south entrance.");
    }
}
