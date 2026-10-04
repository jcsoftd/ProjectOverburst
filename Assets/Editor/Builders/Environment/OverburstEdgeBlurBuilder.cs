using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstEdgeBlurBuilder
{
    public const string RendererPath = "Assets/ProjectOverburst/01_Core/Settings/PC_Renderer.asset";
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/Camera/PF_OverburstEdgeBlur.prefab";
    public const string ShaderPath = "Assets/ProjectOverburst/05_Art/Shaders/Camera/OverburstEdgeBlur.shader";

    [MenuItem("OVERBURST/Camera/Build Edge Blur")]
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
            feature.name = "OVERBURST Edge Blur";
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
        BuildPrefab();
    }

    public static void BuildPrefab()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Editor required");
        const string oldPath="Assets/ProjectOverburst/Resources/Debug/PF_OverburstEdgeBlurToggle.prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(oldPath)!=null){string error=AssetDatabase.MoveAsset(oldPath,PrefabPath);if(error.Length>0)throw new InvalidOperationException(error);}
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            for(int i=root.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach(var component in root.GetComponents<GraphicRaycaster>())Object.DestroyImmediate(component);
            foreach(var component in root.GetComponents<CanvasScaler>())Object.DestroyImmediate(component);
            foreach(var component in root.GetComponents<Canvas>())Object.DestroyImmediate(component);
            if(root.GetComponent<OverburstEdgeBlur>()==null)throw new InvalidOperationException("Edge blur script GUID binding lost");
            root.name="OverburstEdgeBlur";PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
}