using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstEdgeBlurPreviewBuilder
{
    public const string RendererPath = "Assets/ProjectOverburst/01_Core/Settings/PC_Renderer.asset";
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/Debug/PF_OverburstEdgeBlurToggle.prefab";
    public const string ShaderPath = "Assets/ProjectOverburst/05_Art/Shaders/Camera/OverburstEdgeBlur.shader";

    [MenuItem("OVERBURST/Camera/Build Temporary Edge Blur Toggle")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required.");
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (renderer == null || shader == null) throw new InvalidOperationException("PC renderer or blur shader missing.");
        var feature = renderer.rendererFeatures.OfType<OverburstEdgeBlurRendererFeature>().SingleOrDefault();
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<OverburstEdgeBlurRendererFeature>();
            feature.name = "OVERBURST Temporary Edge Blur";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
        }
        var serialized = new SerializedObject(feature);
        serialized.FindProperty("blurShader").objectReferenceValue = shader;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        feature.SetActive(true);
        feature.Create();
        var rendererFields = new SerializedObject(renderer);
        var featureMap = rendererFields.FindProperty("m_RendererFeatureMap");
        featureMap.arraySize = renderer.rendererFeatures.Count;
        for (int i = 0; i < renderer.rendererFeatures.Count; i++)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string guid, out long localId);
            featureMap.GetArrayElementAtIndex(i).longValue = localId;
        }
        rendererFields.ApplyModifiedPropertiesWithoutUndo();
        renderer.SetDirty();
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssetIfDirty(renderer);

        GameObject root = null;
        try
        {
            root = new GameObject("Temporary_EdgeBlurToggle", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = 5;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12060;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var button = RunUiLayout.Button(root.transform, "EdgeBlurToggle", "가장자리 흐림: 켜짐",
                Resources.Load<TMP_FontAsset>("UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body"),
                null, 0, 0, 232, 40, null);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(16, -120);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.targetGraphic.color = new Color(.20f, .31f, .27f, .94f);
            var caption = button.GetComponentInChildren<TMP_Text>();
            caption.fontSize = 19;
            var preview = root.AddComponent<OverburstEdgeBlurPreview>();
            var fields = new SerializedObject(preview);
            fields.FindProperty("toggleButton").objectReferenceValue = button;
            fields.FindProperty("caption").objectReferenceValue = caption;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { if (root != null) Object.DestroyImmediate(root); }
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
    }
}
