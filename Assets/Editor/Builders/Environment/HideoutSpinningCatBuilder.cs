using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class HideoutSpinningCatBuilder
{
    public const string Root = "Assets/ProjectOverburst/05_Art/Environment/OiiaCat";
    public const string Model = Root + "/OiiaCat_Animated.fbx";
    public const string Texture = Root + "/OiiaCat_BaseColor.png";
    public const string CatPrefab = Root + "/PF_HideoutOiiaCat.prefab";
    public const string ChaosPrefab = Root + "/PF_OiiaChaos.prefab";
    public const string ScenePath = "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    public const string Output = "../개인파일/코덱스산출/EasterEgg/20261004_OiiaCat/Applied";
    public const string CatName = "OIIA Spinning Cat";

    [MenuItem("Tools/Overburst/Hideout/Apply OIIA Cat")]
    public static void Menu() => Debug.Log(Apply());

    public static string Apply()
    {
        RequireIdle();
        Directory.CreateDirectory(Output);
        string before = EditorSnapshot();
        var active = SceneManager.GetActiveScene();
        if (SceneManager.GetSceneByPath(ScenePath).isLoaded) throw new InvalidOperationException("Close Hideout before applying; unsaved scene ownership must be preserved.");
        if (!File.Exists(Path.Combine(Output, "HideoutScene.before.unity")))
        {
            File.Copy(ScenePath, Path.Combine(Output, "HideoutScene.before.unity"), false);
            File.Copy(ScenePath + ".meta", Path.Combine(Output, "HideoutScene.before.unity.meta"), false);
        }
        ConfigureImport();
        var clip = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        if (clip == null || clip.length < 5f) throw new InvalidOperationException("Original spin clip missing.");
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var worldMaterial = MaterialAsset("MAT_OiiaCat", "Universal Render Pipeline/Lit");
        worldMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Texture));
        worldMaterial.SetFloat("_Smoothness", .12f);
        EditorUtility.SetDirty(worldMaterial); AssetDatabase.SaveAssetIfDirty(worldMaterial);
        var screenMaterial = MaterialAsset("MAT_OiiaCat_Screen", "Universal Render Pipeline/Unlit");
        screenMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Texture));
        EditorUtility.SetDirty(screenMaterial); AssetDatabase.SaveAssetIfDirty(screenMaterial);
        BuildChaos();
        GameObject cat = null;
        try
        {
            cat = new GameObject(CatName);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(imported);
            visual.name = "Meme Visual"; visual.transform.SetParent(cat.transform, false);
            foreach (var a in visual.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            foreach (var a in visual.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
            clip.SampleAnimation(visual, 0);
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = worldMaterial;
            var health = cat.AddComponent<CombatHealth>();
            var hs = new SerializedObject(health);
            hs.FindProperty("maxHp").floatValue = 1000000;
            hs.FindProperty("currentHp").floatValue = 1000000;
            hs.FindProperty("destroyOnDeath").boolValue = false;
            hs.FindProperty("showDamageNumbers").boolValue = false;
            hs.ApplyModifiedPropertiesWithoutUndo();
            var target = cat.AddComponent<CombatTarget>();
            target.Configure(CombatTeam.Neutral, false);
            target.ConfigureVolume(new Vector3(0, .3f, 0), .48f, .65f);
            var body = cat.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0, .3f, 0); body.radius = .38f; body.height = .6f;
            var source = cat.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.volume = .65f;
            var gag = cat.AddComponent<HideoutSpinningCatEasterEgg>();
            var so = new SerializedObject(gag);
            so.FindProperty("health").objectReferenceValue = health;
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("spinClip").objectReferenceValue = clip;
            so.FindProperty("chaosPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ChaosPrefab);
            so.FindProperty("chaosMaterial").objectReferenceValue = screenMaterial;
            so.FindProperty("musicSource").objectReferenceValue = source;
            so.FindProperty("hitsToActivate").intValue = 50;
            so.FindProperty("maximumCats").intValue = 12;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(cat, CatPrefab);
        }
        finally {if (cat != null) Object.DestroyImmediate(cat);}
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            if (scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HideoutSpinningCatEasterEgg>(true)).Any())
                throw new InvalidOperationException("A cat is already installed.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefab), scene);
            instance.name = CatName;
            instance.transform.position = new Vector3(1.4f, 0, -2.7f);
            Physics.SyncTransforms();
            var ground = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "Camp Ground");
            var ray = new Ray(instance.transform.position + Vector3.up * 20, Vector3.down);
            if (!ground.GetComponent<Collider>().Raycast(ray, out var hit, 40)) throw new InvalidOperationException("Cat has no camp ground.");
            instance.transform.position = hit.point + Vector3.up * .02f;
            var overlap = Physics.OverlapCapsule(instance.transform.position + Vector3.up * .25f,
                instance.transform.position + Vector3.up * .55f, .42f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => c.gameObject.scene == scene && !c.transform.IsChildOf(instance.transform) && c.transform != ground && c.bounds.max.y > instance.transform.position.y + .18f).ToArray();
            if (overlap.Length > 0) throw new InvalidOperationException("Cat placement overlaps: " + string.Join(",", overlap.Select(c => c.name)));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Hideout save failed.");
            Write("apply.json", new {status = "PASS", position = instance.transform.position.ToString(), clip = clip.name,
                clip.length, clip.frameRate, hitThreshold = 50, maximumCats = 12, audioAssigned = false,
                modelGuid = AssetDatabase.AssetPathToGUID(Model), prefabGuid = AssetDatabase.AssetPathToGUID(CatPrefab),
                triangles = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r => r.sharedMesh.triangles.Length / 3),
                blendShapes = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r => r.sharedMesh.blendShapeCount)});
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
        if (before != EditorSnapshot()) throw new InvalidOperationException("Existing editor scene state changed.");
        return "PASS: original model/animation, 50 melee contacts, 12-cat chaos and Escape cancellation installed in Hideout.";
    }

    private static void ConfigureImport()
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(Texture);
        ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.Compressed; ti.SaveAndReimport();
        var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        importer.animationType = ModelImporterAnimationType.Legacy;
        importer.importAnimation = true; importer.importBlendShapes = true;
        importer.importCameras = false; importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.SaveAndReimport();
    }

    private static Material MaterialAsset(string name, string shaderName)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/" + name + ".mat");
        if (material != null) return material;
        var shader = Shader.Find(shaderName);
        if (shader == null) throw new InvalidOperationException(shaderName + " missing.");
        material = new Material(shader) {name = name};
        AssetDatabase.CreateAsset(material, Root + "/" + name + ".mat");
        return material;
    }

    private static void BuildChaos()
    {
        GameObject root = null;
        try
        {
            root = new GameObject("OIIA Chaos");
            var stage = new GameObject("Stage"); stage.transform.SetParent(root.transform, false);
            stage.transform.localPosition = new Vector3(20000, 20000, 20000);
            var camObject = new GameObject("Cat Camera"); camObject.transform.SetParent(stage.transform, false);
            camObject.transform.localPosition = new Vector3(0, 0, -20);
            var camera = camObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 6;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.nearClipPlane = .05f; camera.farClipPlane = 40; camera.allowHDR = false; camera.allowMSAA = false;
            camera.useOcclusionCulling = false; camera.depth = -50;
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10000;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var background = UiChild("Club Wash", canvasObject.transform); background.AddComponent<Image>().color = new Color(.15f, .02f, .4f);
            var beams = UiChild("Beams", canvasObject.transform);
            for (int i = 0; i < 10; i++)
            {
                var beam = UiChild("Neon Beam " + i, beams.transform);
                var rect = (RectTransform)beam.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(2600, 60 + (i % 3) * 40); rect.localRotation = Quaternion.Euler(0, 0, i * 36);
                var image = beam.AddComponent<Image>(); image.color = new Color(.2f, .8f, 1, .2f); image.raycastTarget = false;
            }
            var surface = UiChild("Cats", canvasObject.transform); surface.AddComponent<RawImage>().raycastTarget = false;
            var title = UiChild("Title", canvasObject.transform).AddComponent<Text>();
            title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); title.fontSize = 115;
            title.alignment = TextAnchor.UpperCenter; title.text = "OIIA OIIA"; title.raycastTarget = false;
            title.rectTransform.offsetMin = new Vector2(0, 0); title.rectTransform.offsetMax = new Vector2(0, -28);
            var exit = UiChild("Exit", canvasObject.transform).AddComponent<Text>();
            exit.font = title.font; exit.fontSize = 24; exit.alignment = TextAnchor.LowerRight;
            exit.text = "ESC"; exit.color = new Color(.7f, .7f, .7f); exit.raycastTarget = false;
            exit.rectTransform.offsetMin = new Vector2(24, 24); exit.rectTransform.offsetMax = new Vector2(-24, -24);
            PrefabUtility.SaveAsPrefabAsset(root, ChaosPrefab);
        }
        finally {if (root != null) Object.DestroyImmediate(root);}
    }

    private static GameObject UiChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        return go;
    }

    public static string EditorSnapshot() => string.Join("|", Enumerable.Range(0, SceneManager.sceneCount)
        .Select(i => SceneManager.GetSceneAt(i)).Select(s => s.path + ":" + s.isDirty + ":" + s.isLoaded + ":" + s.rootCount))
        + "|active=" + SceneManager.GetActiveScene().path;

    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("An isolated account is prepared by another operation.");
    }
    public static void Write(string file, object result) => File.WriteAllText(Path.Combine(Output, file), JsonConvert.SerializeObject(result, Formatting.Indented));
}
