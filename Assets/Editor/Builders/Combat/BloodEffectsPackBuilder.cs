using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class BloodEffectsPackBuilder
{
    public const string Vendor = "Assets/ThirdParty/06_VFX/RVFX/BloodEffectsPack";
    public const string Root = "Assets/ProjectOverburst/Resources/Combat/BloodEffectsPack";
    public const string CatalogPath = "Assets/ProjectOverburst/Resources/Combat/BloodEffectsPackCatalog.asset";
    public const string TogglePath = "Assets/ProjectOverburst/Resources/Combat/VFX/PF_TemporaryBloodComparison.prefab";
    [MenuItem("OVERBURST/Combat/Blood Comparison/새 혈흔과 임시 비교 버튼 생성")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        Folder(Root); Folder(Root + "/Sprays"); Folder(Root + "/Decals"); Folder(Root + "/Screens");
        var catalog = AssetDatabase.LoadAssetAtPath<BloodEffectsPackCatalog>(CatalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<BloodEffectsPackCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        catalog.sprays = new BloodEffectsPackCatalog.Spray[7];
        int[] numbers = { 1, 3, 5, 2, 4, 1, 2 };
        int[] masks = { 5, 3, 3, 5, 3, 7, 6 };
        string[] labels = { "짧은 베기 비산", "부채꼴 베기 비산", "굵은 베기 비산", "관통 분출", "강타 분출", "짧게 흐르는 분출", "가는 연속 분출" };
        for (int i = 0; i < numbers.Length; i++)
        {
            string path = Vendor + "/1_URP/Blood/Splash/Blood_Splash_" + numbers[i].ToString("00") + (i >= 5 ? "_Continuous" : "") + "_URP.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new InvalidOperationException("Missing splash: " + path);
            var root = PrefabUtility.InstantiatePrefab(source) as GameObject;
            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.SetActive(false); root.name = "PF_BloodPack_" + numbers[i].ToString("00") + (i >= 5 ? "_Flow" : "");
                // Supplier auto-destruction and projector spawners are replaced by our bounded leases.
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(component);
                float lifetime = 0f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.playOnAwake = false; main.loop = false; main.stopAction = ParticleSystemStopAction.None;
                    if (i >= 5) { main.duration = .22f; main.startDelay = 0f; }
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    main.useUnscaledTime = false;
                    lifetime = Mathf.Max(lifetime, main.duration + main.startDelay.constantMax + main.startLifetime.constantMax);
                    var collision = ps.collision; collision.enabled = false;
                    ps.useAutoRandomSeed = false;
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Sprays/" + root.name + ".prefab");
                catalog.sprays[i] = new BloodEffectsPackCatalog.Spray { label = labels[i], prefab = prefab,
                    shapeMask = masks[i], minimumPriority = i >= 5 ? 1 : 0, flowing = i >= 5,
                    localEuler = Vector3.zero, scale = i == 4 ? 1f : i == 3 ? .85f : .9f, lifetime = Mathf.Clamp(lifetime, 2f, 4f) };
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        var decals = new GameObject[10];
        for (int i = 0; i < decals.Length; i++)
        {
            int number = i < 4 ? i + 1 : i < 8 ? i - 3 : i - 7;
            string id = number.ToString("00");
            string sourcePath = Vendor + "/1_URP/Blood/Decal_Projector/" + (i < 4
                ? "BloodDecal_" + id + "_Static_Projected_URP.prefab"
                : "SubPrefabs/BloodDecal_" + id + "/SubSystem_BloodDecal_" + id + (i < 8 ? "_SpriteSheet" : "_Trail") + "_Projector_URP.prefab");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var projector = source != null ? source.GetComponentInChildren<DecalProjector>(true) : null;
            if (projector == null || projector.material == null) throw new InvalidOperationException("Missing decal: " + sourcePath);
            var root = new GameObject("PF_BloodPackGround_" + (i + 1).ToString("00"));
            try
            {
                root.SetActive(false);
                var decal = root.AddComponent<DecalProjector>(); decal.material = projector.material;
                decal.size = i >= 8 ? new Vector3(.5f, 1.5f, .07f) : new Vector3(i == 0 ? .85f : 1f, i == 0 ? 1.2f : 1f, .07f);
                decal.drawDistance = 40f; decal.fadeScale = .9f;
                var pattern = root.AddComponent<BloodPackGroundPattern>(); pattern.variant = i;
                if (i >= 4)
                {
                    bool found = false;
                    foreach (var component in source.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        var properties = new SerializedObject(component);
                        if (properties.FindProperty("columns") == null) continue;
                        pattern.columns = properties.FindProperty("columns").intValue;
                        pattern.rows = properties.FindProperty("rows").intValue;
                        pattern.frames = properties.FindProperty("frameLength").intValue;
                        pattern.spreadSeconds = i >= 8 ? .32f : .48f;
                        found = true; break;
                    }
                    if (!found) throw new InvalidOperationException("Missing atlas layout: " + sourcePath);
                }
                decals[i] = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Decals/" + root.name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        catalog.sweepDecals = new[] { decals[0], decals[1], decals[3], decals[4], decals[6], decals[8] };
        catalog.thrustDecals = new[] { decals[0], decals[1], decals[5], decals[8], decals[9] };
        catalog.downwardDecals = new[] { decals[2], decals[3], decals[6], decals[7] };
        catalog.lethalDecals = decals;
        catalog.trailDecals = new[] { decals[8], decals[9] };
        BuildProfileShaders();
        BuildScreen(catalog);
        EditorUtility.SetDirty(catalog); BuildToggle(); AssetDatabase.SaveAssetIfDirty(catalog);
    }
    // Keep the pack's texture relief and alpha, while profile RGB supplies the blood hue.
    [MenuItem("OVERBURST/Combat/Blood Comparison/A 색감 기준 셰이더 연결")]
    public static void BuildProfileShaders()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        var catalog = AssetDatabase.LoadAssetAtPath<BloodEffectsPackCatalog>(CatalogPath);
        if (catalog == null) throw new InvalidOperationException("Build the blood catalog first");
        Folder(Root + "/Shaders");
        catalog.sprayProfileShader = ProfileShader("BloodFX_PBR_URP", "SG_BloodPackSprayProfile");
        catalog.groundProfileShader = ProfileShader("BloodFX_PBR_Projector_URP", "SG_BloodPackGroundProfile");
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
    }
    static Shader ProfileShader(string sourceName, string name)
    {
        string source = Vendor + "/1_URP/Shader/" + sourceName + ".shadergraph";
        string path = Root + "/Shaders/" + name + ".shadergraph";
        if (!System.IO.File.Exists(path) && !AssetDatabase.CopyAsset(source, path))
            throw new InvalidOperationException("Profile shader copy failed: " + path);
        var assembly = System.Linq.Enumerable.First(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetType("UnityEditor.ShaderGraph.GraphData") != null);
        var graphType = assembly.GetType("UnityEditor.ShaderGraph.GraphData");
        var jsonType = assembly.GetType("UnityEditor.ShaderGraph.Serialization.MultiJson");
        var graph = Activator.CreateInstance(graphType, true);
        var messages = graphType.GetProperty("messageManager");
        messages.SetValue(graph, Activator.CreateInstance(messages.PropertyType, true));
        jsonType.GetMethod("Deserialize").MakeGenericMethod(graphType).Invoke(null,
            new object[] { graph, System.IO.File.ReadAllText(source), null, false });
        graphType.GetProperty("assetGuid").SetValue(graph, AssetDatabase.AssetPathToGUID(path));
        graphType.GetProperty("path").SetValue(graph, "OVERBURST/Blood");
        graphType.GetMethod("OnEnable").Invoke(graph, null);
        var getNode = System.Linq.Enumerable.Single(graphType.GetMethods(), m => m.Name == "GetNodeFromId" && !m.IsGenericMethod);
        var split = getNode.Invoke(graph, new object[] { "7c3a7b7b56114c22b3d0affef96583a7" });
        var combine = getNode.Invoke(graph, new object[] { "a9d96fa7ca2d414dbcc28a2ad8bd40ce" });
        if (split == null || combine == null) throw new InvalidOperationException("Supplier texture color nodes changed");
        var slot = split.GetType().GetMethod("GetSlotReference");
        var from = slot.Invoke(split, new object[] { 1 });
        for (int channel = 1; channel <= 2; channel++)
        {
            var to = combine.GetType().GetMethod("GetSlotReference").Invoke(combine, new object[] { channel });
            if (graphType.GetMethod("Connect").Invoke(graph, new[] { from, to }) == null)
                throw new InvalidOperationException("Profile shader color connection failed");
        }
        string serialized = (string)jsonType.GetMethod("Serialize").Invoke(null, new[] { graph });
        var graphUtil = assembly.GetType("UnityEditor.ShaderGraph.GraphUtil");
        if (!(bool)graphUtil.GetMethod("WriteToFile").Invoke(null, new object[] { path, serialized }))
            throw new InvalidOperationException("Profile shader save failed");
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<Shader>(path) ?? throw new InvalidOperationException("Profile shader import failed");
    }
    static void BuildScreen(BloodEffectsPackCatalog catalog)
    {
        catalog.screenSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Vendor + "/UIEffect/Texture/BloodUISprite.png");
        if (catalog.screenSprite == null) throw new InvalidOperationException("Blood screen sprite missing");
        catalog.screenMaterials = new Material[4];
        for (int i = 0; i < 4; i++)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(Vendor + "/UIEffect/Material/BloodScreen_" + (i + 1).ToString("00") + ".mat");
            if (source == null) throw new InvalidOperationException("Blood screen material missing");
            string path = Root + "/Screens/M_BloodPackScreen_" + (i + 1).ToString("00") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(source);
            material.SetFloat("_HueShift", 0f); material.SetFloat("_ColorIntensity", .8f);
            material.SetColor("_BackgroundColor", new Color(.18f, 0f, 0f, .025f));
            material.SetColor("_EdgeColor", new Color(.28f, .01f, .01f, .4f));
            // Source screen treatments retain their separate edge widths and opacity patterns.
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            catalog.screenMaterials[i] = material;
        }
    }
    static void BuildToggle()
    {
        var root = new GameObject("Temporary_BloodComparison", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        try
        {
            root.layer = 5;
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 12063;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
            var font = Resources.Load<TMP_FontAsset>("UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body");
            if (font == null) throw new InvalidOperationException("Project UI font missing");
            var button = RunUiLayout.Button(root.transform, "BloodVersionToggle", "혈흔 A · 기존", font, null, 0, 0, 232, 40, null);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1); rect.anchoredPosition = new Vector2(16,-312);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.GetComponentInChildren<TMP_Text>().fontSize = 19;
            var so = new SerializedObject(root.AddComponent<TemporaryBloodComparisonToggle>());
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("caption").objectReferenceValue = button.GetComponentInChildren<TMP_Text>();
            var colorButton = RunUiLayout.Button(root.transform, "BloodColorToggle", "색상 · 몬스터별 조정", font, null, 0, 0, 232, 40, null);
            var colorRect = (RectTransform)colorButton.transform;
            colorRect.anchorMin = colorRect.anchorMax = colorRect.pivot = new Vector2(0,1); colorRect.anchoredPosition = new Vector2(16,-360);
            colorButton.navigation = new Navigation { mode = Navigation.Mode.None };
            colorButton.GetComponentInChildren<TMP_Text>().fontSize = 19;
            so.FindProperty("colorButton").objectReferenceValue = colorButton;
            so.FindProperty("colorCaption").objectReferenceValue = colorButton.GetComponentInChildren<TMP_Text>();
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, TogglePath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0,path.LastIndexOf('/')); Folder(parent);
        AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
    }
}
