using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ChainElectricityLiteVfxSetupUtility
{
    private const string ShaderPath =
        "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Shaders/ChainElectricityAdditive.shader";
    private const string MaterialPath =
        "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Materials/MAT_ChainElectricity_Link_Lite.mat";
    private const string WrapperPath =
        "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Prefabs/ChainElectricity/PF_VFX_Reaction_ChainElectricity_Link.prefab";
    private const string ContentRootName = "VFX_CONTENT";
    private const string GeneratedRootName = "LIGHTNING_LITE_CONTENT";
    private const int MainPointCount = 33;
    private const int BranchPointCount = 11;
    private const int PoolCapacity = 100;
    private const int BranchCount = 2;
    private const float Lifetime = 0.28f;
    private const float SafetyLifetime = 0.34f;

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Chain Electricity Lite VFX")]
    public static void RunSetupFromMenu()
    {
        RunSetupFromCommandLine();
    }

    public static void RunSetupFromCommandLine()
    {
        Material material = CreateOrUpdateMaterial();
        GameObject wrapperRoot = PrefabUtility.LoadPrefabContents(WrapperPath);
        try
        {
            Transform contentRoot = wrapperRoot.transform.Find(ContentRootName);
            if (contentRoot == null)
                throw new InvalidOperationException("연쇄감전 Link wrapper에 VFX_CONTENT가 없습니다.");

            Transform existing = contentRoot.Find(GeneratedRootName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            if (contentRoot.childCount != 0)
                throw new InvalidOperationException("연쇄감전 VFX_CONTENT에 보존해야 할 사용자 내용물이 있습니다.");

            GameObject generatedRoot = new GameObject(GeneratedRootName);
            generatedRoot.transform.SetParent(contentRoot, false);
            Normalize(generatedRoot.transform);

            LineRenderer mainGlow = CreateLine(
                generatedRoot.transform,
                "Main_Glow",
                material,
                0.18f,
                CreateGradient(
                    new Color(0.18f, 0.32f, 1f, 0.3f),
                    new Color(0.57f, 0.23f, 1f, 0.58f),
                    new Color(0.16f, 0.62f, 1f, 0.3f)),
                0);
            LineRenderer mainCore = CreateLine(
                generatedRoot.transform,
                "Main_Core",
                material,
                0.052f,
                CreateGradient(
                    new Color(0.65f, 0.88f, 1f, 0.65f),
                    new Color(1f, 1f, 1f, 1f),
                    new Color(0.64f, 0.9f, 1f, 0.65f)),
                1);

            LineRenderer[] branchGlows = new LineRenderer[BranchCount];
            LineRenderer[] branchCores = new LineRenderer[BranchCount];
            for (int i = 0; i < BranchCount; i++)
            {
                branchGlows[i] = CreateLine(
                    generatedRoot.transform,
                    $"Branch_{i + 1}_Glow",
                    material,
                    0.082f,
                    CreateGradient(
                        new Color(0.3f, 0.45f, 1f, 0.2f),
                        new Color(0.5f, 0.23f, 1f, 0.42f),
                        new Color(0.12f, 0.4f, 1f, 0f)),
                    -2);
                branchCores[i] = CreateLine(
                    generatedRoot.transform,
                    $"Branch_{i + 1}_Core",
                    material,
                    0.024f,
                    CreateGradient(
                        new Color(0.58f, 0.84f, 1f, 0.5f),
                        new Color(0.9f, 0.95f, 1f, 0.88f),
                        new Color(0.55f, 0.74f, 1f, 0f)),
                    -1);
            }

            ChainElectricityLiteVfxController controller =
                generatedRoot.AddComponent<ChainElectricityLiteVfxController>();
            controller.ConfigureForAuthoring(
                mainGlow,
                mainCore,
                branchGlows,
                branchCores,
                MainPointCount,
                BranchPointCount,
                Lifetime,
                0.04f,
                0.075f,
                0.11f);

            ElementalReactionVfxAuthoring authoring =
                wrapperRoot.GetComponent<ElementalReactionVfxAuthoring>();
            if (authoring == null)
                throw new InvalidOperationException("연쇄감전 Link wrapper에 authoring 컴포넌트가 없습니다.");
            authoring.ConfigureInitialDefaults(
                ElementalReactionType.ChainElectricity,
                ElementalReactionVfxSlotType.Link,
                ElementalReactionVfxSpawnBasis.SourceToTarget,
                true,
                ElementalReactionVfxScaleMode.None,
                0f,
                Vector3.zero,
                SafetyLifetime,
                PoolCapacity,
                true);

            PrefabUtility.SaveAsPrefabAsset(wrapperRoot, WrapperPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(wrapperRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RunValidationFromCommandLine();
        Debug.Log("[ChainElectricityLiteVfxSetup] 경량 연쇄감전 Link VFX 생성 및 연결 완료.");
    }

    [MenuItem("OVERBURST/Codex/Validate/Chain Electricity Lite VFX")]
    public static void RunValidationFromMenu()
    {
        RunValidationFromCommandLine();
    }

    public static void RunValidationFromCommandLine()
    {
        GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (wrapper == null || material == null || material.shader == null
            || material.shader.name != "OVERBURST/VFX/Chain Electricity Additive")
        {
            throw new InvalidOperationException("경량 연쇄감전 wrapper 또는 전용 재질이 없습니다.");
        }

        ElementalReactionVfxAuthoring authoring = wrapper.GetComponent<ElementalReactionVfxAuthoring>();
        if (authoring == null
            || authoring.ReactionType != ElementalReactionType.ChainElectricity
            || authoring.SlotType != ElementalReactionVfxSlotType.Link
            || authoring.SpawnBasis != ElementalReactionVfxSpawnBasis.SourceToTarget
            || !authoring.FollowTarget
            || authoring.ScaleMode != ElementalReactionVfxScaleMode.None
            || Mathf.Abs(authoring.Lifetime - SafetyLifetime) > 0.001f
            || authoring.PoolCapacity != PoolCapacity
            || !authoring.NaturalCompletion)
        {
            throw new InvalidOperationException("경량 연쇄감전 Link authoring 계약이 다릅니다.");
        }

        Transform contentRoot = wrapper.transform.Find(ContentRootName);
        Transform generatedRoot = contentRoot != null ? contentRoot.Find(GeneratedRootName) : null;
        ChainElectricityLiteVfxController controller =
            generatedRoot != null ? generatedRoot.GetComponent<ChainElectricityLiteVfxController>() : null;
        LineRenderer[] lines = generatedRoot != null
            ? generatedRoot.GetComponentsInChildren<LineRenderer>(true)
            : Array.Empty<LineRenderer>();
        if (contentRoot == null || contentRoot.childCount != 1 || generatedRoot == null
            || controller == null || lines.Length != 6
            || controller.MainPointCount != MainPointCount
            || controller.BranchPointCount != BranchPointCount
            || controller.BranchCount != BranchCount
            || Mathf.Abs(controller.Lifetime - Lifetime) > 0.001f)
        {
            throw new InvalidOperationException("경량 연쇄감전 Link 구성 수량 또는 재생값이 다릅니다.");
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].sharedMaterial != material
                || lines[i].useWorldSpace
                || lines[i].shadowCastingMode != ShadowCastingMode.Off
                || lines[i].receiveShadows)
            {
                throw new InvalidOperationException("경량 연쇄감전 LineRenderer 렌더링 계약이 다릅니다.");
            }
        }

        if (wrapper.GetComponentsInChildren<ParticleSystem>(true).Length != 0
            || GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(wrapper) != 0)
        {
            throw new InvalidOperationException("경량 연쇄감전 Link에 불필요한 ParticleSystem 또는 Missing Script가 있습니다.");
        }

        Debug.Log("[ChainElectricityLiteVfxSetup] 6 LineRenderer/재질/풀/수명 검증 PASS");
    }

    public static void RenderPreviewFromCommandLine()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPath);
        if (prefab == null)
            throw new InvalidOperationException("경량 연쇄감전 Link wrapper가 없습니다.");

        GameObject instance = null;
        GameObject cameraObject = null;
        RenderTexture renderTexture = null;
        Texture2D screenshot = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "ChainElectricityLitePreview";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = new Vector3(1f, 1f, 5f);
            ChainElectricityLiteVfxController controller =
                instance.GetComponentInChildren<ChainElectricityLiteVfxController>(true);
            if (controller == null)
                throw new InvalidOperationException("미리보기 대상에 경량 번개 컨트롤러가 없습니다.");
            controller.RestartVfx();

            cameraObject = new GameObject("ChainElectricityLitePreviewCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.016f, 0.035f, 1f);
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 50f;
            camera.transform.position = new Vector3(4.6f, 2.4f, -4.8f);
            camera.transform.LookAt(Vector3.zero);

            renderTexture = new RenderTexture(1280, 640, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            screenshot = new Texture2D(1280, 640, TextureFormat.RGBA32, false);
            screenshot.ReadPixels(new Rect(0f, 0f, 1280f, 640f), 0, 0);
            screenshot.Apply();
            RenderTexture.active = previous;

            string outputPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../Temp/ChainElectricityLitePreview.png"));
            File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
            Debug.Log("[ChainElectricityLiteVfxSetup] Preview=" + outputPath);
        }
        finally
        {
            if (screenshot != null)
                UnityEngine.Object.DestroyImmediate(screenshot);
            if (renderTexture != null)
                UnityEngine.Object.DestroyImmediate(renderTexture);
            if (cameraObject != null)
                UnityEngine.Object.DestroyImmediate(cameraObject);
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static Material CreateOrUpdateMaterial()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
            throw new InvalidOperationException("경량 연쇄감전 Shader를 찾을 수 없습니다: " + ShaderPath);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "MAT_ChainElectricity_Link_Lite" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetColor("_TintColor", Color.white);
        material.SetFloat("_Intensity", 3.2f);
        material.SetFloat("_EdgePower", 2.15f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static LineRenderer CreateLine(
        Transform parent,
        string name,
        Material material,
        float width,
        Gradient gradient,
        int sortingOrder)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(parent, false);
        Normalize(lineObject.transform);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.enabled = false;
        line.sharedMaterial = material;
        line.useWorldSpace = false;
        line.loop = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.widthMultiplier = width;
        line.widthCurve = CreateTaperCurve();
        line.colorGradient = gradient;
        line.numCornerVertices = 1;
        line.numCapVertices = 0;
        line.generateLightingData = false;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        line.allowOcclusionWhenDynamic = false;
        line.sortingOrder = sortingOrder;
        return line;
    }

    private static AnimationCurve CreateTaperCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.055f, 0.88f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.945f, 0.88f),
            new Keyframe(1f, 0f));
    }

    private static Gradient CreateGradient(Color start, Color middle, Color end)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(middle, 0.5f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(start.a, 0f),
                new GradientAlphaKey(middle.a, 0.5f),
                new GradientAlphaKey(end.a, 1f)
            });
        return gradient;
    }

    private static void Normalize(Transform transform)
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }
}
