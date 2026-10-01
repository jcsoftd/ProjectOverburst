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
    public const string CraftsmanRoot = "Assets/ThirdParty/04_환경맵/Medieval Craftsman Tools";
    public const string CraftsmanGenerated = CraftsmanRoot + "/OVERBURST_URP";
    public const string WorkshopPath = CraftsmanGenerated + "/PF_WeaponMerchantWorkshop.prefab";
    const string MerchantSource = "Assets/ProjectOverburst/02_Shared/CharacterVisual/Prefabs/Medieval_NPC_Pack_2/Merchant/Prefab/SK_Merchant.prefab";
    const string MerchantModel = "Assets/ThirdParty/02_인간캐릭터/Medieval_NPC_Pack_2/Merchant/Base Mesh/SK_Merchant.fbx";
    const string MerchantGenerated = "Assets/ThirdParty/02_인간캐릭터/Medieval_NPC_Pack_2/OVERBURST_Merchants";
    public const string MerchantVisualPath = MerchantGenerated + "/PF_HideoutWeaponMerchantVisual.prefab";

    public static string ApplyMerchantWorkshop(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        RequireIdle();
        if (SceneManager.GetSceneByPath(HideoutPath).IsValid()) throw new InvalidOperationException("Close Hideout first.");
        string backup = Path.Combine(output,"BeforeMerchant/HideoutScene.unity");
        if (!File.Exists(backup) || !File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(HideoutPath))) throw new InvalidOperationException("Review current scene backup.");
        if (!File.Exists(Path.Combine(output,"craftsman_import_result.json"))) throw new IOException("Verified Craftsman import required.");
        var visual = BuildMerchantVisual();
        var workshop = BuildWorkshop();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(HideoutPath, OpenSceneMode.Additive);
        try
        {
            var merchant = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GeneralGoodsMerchantInteractable>(true)).Single(c => c.name == "WeaponMerchantObject");
            if (merchant.transform.Find("Merchant Visual") != null) throw new InvalidOperationException("Merchant visual already exists.");
            var position = merchant.transform.position; position.y = 0;
            merchant.transform.position = position; merchant.transform.localScale = Vector3.one;
            merchant.transform.rotation = Quaternion.LookRotation((CampCenter - position).normalized);
            var placeholder = merchant.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;
            var collider = merchant.GetComponent<CapsuleCollider>();
            if (collider != null) { collider.center = new Vector3(0,.9f,0); collider.height = 1.8f; collider.radius = .35f; }
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(visual,scene);
            npc.name = "Merchant Visual"; npc.transform.SetParent(merchant.transform,false);
            npc.transform.localPosition = Vector3.zero; npc.transform.localRotation = Quaternion.identity;
            var prompt = merchant.transform.Find("PromptRoot");
            if (prompt != null) prompt.localPosition = new Vector3(0,2.2f,0);
            var props = (GameObject)PrefabUtility.InstantiatePrefab(workshop,scene);
            props.name = "Weapon Merchant Workshop";
            props.transform.position = position;
            var checks = ValidateLoaded(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Merchant scene save failed.");
            File.WriteAllText(Path.Combine(output,"merchant_result.json"),JsonConvert.SerializeObject(new {
                status="PASS", visual=MerchantVisualPath, workshop=WorkshopPath, source=MerchantSource, checks=checks.Count,
                visualGuid=AssetDatabase.AssetPathToGUID(MerchantVisualPath),workshopGuid=AssetDatabase.AssetPathToGUID(WorkshopPath),
                retainedMerchants=2,mechanicGirlImported=false
            },Formatting.Indented));
            return "Existing NPC-pack merchant and Craftsman workshop applied.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene,true);
        }
    }
    static GameObject BuildMerchantVisual()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(MerchantVisualPath);
        if (existing != null) return existing;
        EnsureFolder(MerchantGenerated);
        string controllerPath = MerchantGenerated + "/AC_HideoutMerchantIdle.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            var clips = AssetDatabase.FindAssets("Idle01",new[] { "Assets/ThirdParty/02_인간캐릭터/Medieval_NPC_Pack_2/Peasant_woman/Test Animation" });
            var idle = clips.SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                .OfType<AnimationClip>().First(c => c.isHumanMotion && !c.name.StartsWith("__preview__",StringComparison.Ordinal));
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Merchant Idle"); state.motion = idle; state.iKOnFeet = true;
            controller.layers[0].stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        }
        var root = PrefabUtility.LoadPrefabContents(MerchantSource);
        try
        {
            root.name = "PF_HideoutWeaponMerchantVisual";
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(MerchantModel).OfType<Avatar>().Single(a => a.isHuman);
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return PrefabUtility.SaveAsPrefabAsset(root,MerchantVisualPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static GameObject BuildWorkshop()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopPath);
        if (existing != null) return existing;
        EnsureFolder(CraftsmanGenerated);
        var materials = ConvertCraftMaterials();
        var scene = EditorSceneManager.NewPreviewScene();
        var root = NewRoot("PF_WeaponMerchantWorkshop",scene);
        try
        {
            GameObject Place(string prefab, Vector3 position, float yaw)
            {
                string path = CraftsmanRoot + "/Medieval_Vol_07/Prefabs/" + prefab + ".prefab";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null) throw new IOException("Craftsman prefab missing: " + path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source,scene);
                instance.transform.SetParent(root.transform,false);
                instance.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m != null && materials.TryGetValue(m,out var copy) ? copy : m).ToArray();
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<Renderer>();
                    if (filter.sharedMesh == null || renderer == null || renderer.bounds.size.y < .3f || filter.GetComponent<Collider>() != null) continue;
                    if (filter.name.Contains("_LOD") && !filter.name.EndsWith("_LOD0",StringComparison.Ordinal)) continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh;
                }
                // Align each prop's bottom with the floor; preserve the supplier's actual dimensions.
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0) instance.transform.position -= Vector3.up * renderers.Min(r => r.bounds.min.y);
                return instance;
            }
            var forge = Place("SM_Forge_01a",new Vector3(3.4f,0,-2.8f),180);
            Place("SM_Anvil_01a",new Vector3(1.5f,0,-1.6f),30);
            var bench = Place("SM_WorkBench_01a",new Vector3(2.8f,0,1.6f),90);
            Place("SM_Tool_Rack_01a",new Vector3(3.8f,0,3.8f),-90);
            Place("SM_Grindstone_01a",new Vector3(4.8f,0,.2f),-90);
            Place("SM_Bellow_01a",new Vector3(4.7f,0,-2.3f),180);
            Place("SM_Metal_Bucket_01a",new Vector3(2.5f,0,-2.2f),0);
            Place("SM_Barrel_01a",new Vector3(4.9f,0,2.6f),0);
            Place("SM_Blacksmith_Sign_01a",new Vector3(1.5f,0,3.4f),180);
            float top = bench.GetComponentsInChildren<Renderer>(true).Max(r => r.bounds.max.y);
            void LayTool(string prefab, Vector3 position, float yaw)
            {
                var tool = Place(prefab,position,yaw);
                tool.transform.rotation = Quaternion.Euler(90,yaw,0);
                float bottom = tool.GetComponentsInChildren<Renderer>(true).Min(r => r.bounds.min.y);
                tool.transform.position += Vector3.up * (top+.015f-bottom);
            }
            LayTool("SM_BlacksmithTool_01a",new Vector3(2.5f,0,1.6f),35);
            LayTool("SM_BlacksmithTool_01b",new Vector3(3.1f,0,1.5f),-20);
            var lightObject = new GameObject("Forge Warm Light"); lightObject.transform.SetParent(root.transform,false);
            lightObject.transform.position = new Vector3(3.4f,1,-2.8f);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(1,.4f,.1f); light.intensity = 2; light.range = 4;
            var camp = AssetDatabase.LoadAssetAtPath<GameObject>(ExpandedPath);
            var flameSource = camp.GetComponentsInChildren<ParticleSystem>(true).Single(p => p.name == "Campfire Flames");
            var flame = Object.Instantiate(flameSource.gameObject,root.transform);
            flame.name = "Forge Fire"; flame.transform.position = new Vector3(3.4f,.5f,-2.8f);
            return PrefabUtility.SaveAsPrefabAsset(root,WorkshopPath);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    static Dictionary<Material,Material> ConvertCraftMaterials()
    {
        var result = new Dictionary<Material,Material>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var guid in AssetDatabase.FindAssets("t:Material",new[] {CraftsmanRoot}))
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
            if (sourcePath.StartsWith(CraftsmanGenerated + "/",StringComparison.Ordinal) || !sourcePath.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)) continue;
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source.shader.name.StartsWith("Skybox/",StringComparison.Ordinal)) continue;
            string path = CraftsmanGenerated + "/" + source.name + "_" + guid.Substring(0,8) + "_URP.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                if (source.shader.name == "Universal Render Pipeline/Lit")
                {
                    mat = new Material(source) { name=source.name + "_URP" };
                    AssetDatabase.CreateAsset(mat,path);
                    result.Add(source,mat);
                    continue;
                }
                mat = new Material(shader) { name=source.name + "_URP" };
                CopyTexture(source,mat,"_MainTex","_BaseMap"); CopyTexture(source,mat,"_BumpMap","_BumpMap");
                CopyTexture(source,mat,"_MetallicGlossMap","_MetallicGlossMap"); CopyTexture(source,mat,"_OcclusionMap","_OcclusionMap"); CopyTexture(source,mat,"_EmissionMap","_EmissionMap");
                if (source.HasProperty("_Color")) mat.SetColor("_BaseColor",source.GetColor("_Color"));
                foreach (var property in new[] { "_Metallic","_BumpScale","_OcclusionStrength" }) if (source.HasProperty(property)) mat.SetFloat(property,source.GetFloat(property));
                if (source.HasProperty("_Glossiness")) mat.SetFloat("_Smoothness",source.GetFloat("_Glossiness"));
                if (mat.GetTexture("_BumpMap") != null) mat.EnableKeyword("_NORMALMAP");
                if (mat.GetTexture("_MetallicGlossMap") != null) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (mat.GetTexture("_OcclusionMap") != null) mat.EnableKeyword("_OCCLUSIONMAP");
                if (source.HasProperty("_EmissionColor"))
                {
                    var color = source.GetColor("_EmissionColor"); mat.SetColor("_EmissionColor",color);
                    if (color.maxColorComponent > .001f) mat.EnableKeyword("_EMISSION");
                }
                AssetDatabase.CreateAsset(mat,path);
            }
            result.Add(source,mat);
        }
        return result;
    }
    static void ValidateMerchantWorkshop(Scene scene, List<string> checks)
    {
        void Check(bool value,string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(message);
        }
        var roots = scene.GetRootGameObjects();
        var merchant = roots.SelectMany(r => r.GetComponentsInChildren<GeneralGoodsMerchantInteractable>(true)).Single(c => c.name == "WeaponMerchantObject");
        var visual = merchant.transform.Find("Merchant Visual");
        Check(visual != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual.gameObject) == MerchantVisualPath,"Connected NPC-pack merchant visual.");
        var animator = visual.GetComponent<Animator>();
        Check(animator != null && animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid,"Merchant humanoid avatar valid.");
        Check(animator.runtimeAnimatorController != null && animator.runtimeAnimatorController.animationClips.All(c => c.isHumanMotion && c.isLooping),"Merchant looping humanoid idle connected.");
        Check(!merchant.GetComponent<MeshRenderer>().enabled && merchant.GetComponent<CapsuleCollider>().enabled,"Merchant placeholder hidden and interaction collider retained.");
        var workshop = roots.Single(r => r.name == "Weapon Merchant Workshop");
        Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(workshop) == WorkshopPath,"Connected Craftsman workshop prefab.");
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true).Concat(workshop.GetComponentsInChildren<Renderer>(true)).Where(r => !(r is ParticleSystemRenderer)))
            Check(renderer.sharedMaterials.All(mat => mat != null && mat.shader.name == "Universal Render Pipeline/Lit" && mat.GetTexture("_BaseMap") != null && !ShaderUtil.ShaderHasError(mat.shader)),"Merchant/workshop URP texture valid: " + renderer.name);
        Check(workshop.transform.childCount == 13,"Eleven workshop props plus forge fire and light retained.");
        foreach (var point in roots.SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)))
            Check(ClearCapsule(workshop,point.transform.position),"Workshop clear at return point: " + point.ReturnPointId);
    }

}
