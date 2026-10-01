using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ParryRainbowFlareBuilder
{
    public const string SourcePath = "Assets/ThirdParty/06_VFX/Vefects/Rainbow Lens Flare VFX URP/VFX/Particles/VFX_Rainbow_Lens_Flare_Combo_01.prefab";
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/Combat/VFX/PF_ParrySuccessRainbowFlare.prefab";
    public const string LibraryPath = "Assets/ProjectOverburst/Resources/Enemies/Balance/EnemyTelegraphVisualLibrary.asset";
    public const float PlaybackSpeed = 1.8f;
    private const string MaterialFolder = "Assets/ProjectOverburst/Resources/Combat/VFX/ParryRainbowMaterials";

    [MenuItem("OVERBURST/Builders/Combat/Build Parry Rainbow Flare")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit mode required.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        var library = AssetDatabase.LoadAssetAtPath<EnemyTelegraphVisualLibrary>(LibraryPath);
        if (source == null || library == null)
            throw new InvalidOperationException("Restore the Rainbow Lens Flare URP package and telegraph library first.");
        if (EditorUtility.IsDirty(library))
            throw new InvalidOperationException("Telegraph library has unsaved changes.");
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null && EditorUtility.IsDirty(existing))
            throw new InvalidOperationException("Parry prefab has unsaved changes.");

        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("PF_ParrySuccessRainbowFlare");
            root.transform.localScale = Vector3.one * .70f;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var flare = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            flare.transform.SetParent(root.transform, false);
            flare.transform.localPosition = Vector3.zero;
            flare.transform.localRotation = Quaternion.identity;
            flare.transform.localScale = Vector3.one;
            var shader = PrepareOrthographicShader(source);
            foreach (var ps in flare.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                main.useUnscaledTime = true;
                main.simulationSpeed = PlaybackSpeed;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.stopAction = ParticleSystemStopAction.None;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                PrefabUtility.RecordPrefabInstancePropertyModifications(ps);
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && renderer.enabled && renderer.sharedMaterial != null)
                {
                    renderer.sharedMaterial = PrepareMaterial(renderer.sharedMaterial, shader);
                    if (renderer.renderMode == ParticleSystemRenderMode.Mesh)
                    {
                        // Keep the supplier's radial shard mesh and motion, extend the rays for the game camera.
                        main.startSizeX = new ParticleSystem.MinMaxCurve(.05f, .10f);
                        main.startSizeY = new ParticleSystem.MinMaxCurve(.75f, 1.05f);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(ps);
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            foreach (var audio in flare.GetComponentsInChildren<AudioSource>(true))
                UnityEngine.Object.DestroyImmediate(audio);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved || prefab == null) throw new InvalidOperationException("Parry prefab save failed.");
            var serialized = new SerializedObject(library);
            serialized.FindProperty("parrySuccess").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(library);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static Shader PrepareOrthographicShader(GameObject source)
    {
        var material = source.GetComponentInChildren<ParticleSystemRenderer>().sharedMaterial;
        if (material == null)
            foreach (var renderer in source.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (renderer.sharedMaterial != null) { material = renderer.sharedMaterial; break; }
        string sourcePath = AssetDatabase.GetAssetPath(material.shader);
        string text = File.ReadAllText(sourcePath);
        string originalName = "Vefects/SH_Vefects_VFX_Rainbow_Lens_Flare_01_URP";
        if (!text.Contains(originalName) || !text.Contains("LinearEyeDepth("))
            throw new InvalidOperationException("Supplier shader format changed; review the projection adapter.");
        text = text.Replace(originalName, "Overburst/ParryRainbowFlareURP").Replace("LinearEyeDepth(", "OB_FLARE_EYE_DEPTH(");
        const string helper = "\n#if UNITY_REVERSED_Z\n#define OB_FLARE_EYE_DEPTH(d,p) (unity_OrthoParams.w > 0.5 ? lerp(_ProjectionParams.z, _ProjectionParams.y, (d)) : LinearEyeDepth(d,p))\n#else\n#define OB_FLARE_EYE_DEPTH(d,p) (unity_OrthoParams.w > 0.5 ? lerp(_ProjectionParams.y, _ProjectionParams.z, (d)) : LinearEyeDepth(d,p))\n#endif\n";
        const string commonInclude = "#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl\"";
        text = text.Replace(commonInclude, commonInclude + helper);
        const string folder = "Assets/ThirdParty/06_VFX/OverburstAdapters";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/ThirdParty/06_VFX", "OverburstAdapters");
        string path = folder + "/SH_ParryRainbowFlare_URP.shader";
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Projection adapter shader failed.");
        return shader;
    }

    private static Material PrepareMaterial(Material source, Shader shader)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/ProjectOverburst/Resources/Combat/VFX", "ParryRainbowMaterials");
        string path = MaterialFolder + "/" + source.name + "_Parry.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null && EditorUtility.IsDirty(material))
            throw new InvalidOperationException("Parry material has unsaved changes: " + path);
        if (material == null)
        {
            material = new Material(source) { name = source.name + "_Parry" };
            AssetDatabase.CreateAsset(material, path);
        }
        else material.CopyPropertiesFromMaterial(source);
        material.shader = shader;
        material.SetFloat("_EmissiveMult", source.GetFloat("_EmissiveMult") * (source.name.Contains("Spec") ? 5f : 8f));
        // The supplier's cutout reference is absent from the package; keep the original intact.
        if (material.HasProperty("_CutoutTexture") && material.GetTexture("_CutoutTexture") == null)
        {
            string whitePath = MaterialFolder + "/T_ParryCutoutWhite.asset";
            var white = AssetDatabase.LoadAssetAtPath<Texture2D>(whitePath);
            if (white == null)
            {
                white = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "T_ParryCutoutWhite" };
                white.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); white.Apply();
                AssetDatabase.CreateAsset(white, whitePath);
            }
            material.SetTexture("_CutoutTexture", white);
        }
        if (material.HasProperty("_ErosionSmoothness"))
            material.SetFloat("_ErosionSmoothness", Mathf.Max(.001f, material.GetFloat("_ErosionSmoothness")));
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }
}
