using System;
using System.Collections.Generic;
using HighlightPlus;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class OverburstHighlightPlusBuilder
{
    private const string ResourceFolder = "Assets/ProjectOverburst/Resources/UI/World/HighlightPlus";
    private static readonly string[] RendererPaths = {
        "Assets/ProjectOverburst/01_Core/Settings/PC_Renderer.asset",
        "Assets/ProjectOverburst/01_Core/Settings/Mobile_Renderer.asset"
    };

    [MenuItem("OVERBURST/UI/Apply Highlight Plus Integration")]
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Editor must be idle.");
        foreach (string path in RendererPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null || EditorUtility.IsDirty(data))
                throw new InvalidOperationException("Renderer is missing or has unsaved changes: " + path);
        }
        EnsureFolder(ResourceFolder);
        var changed = new List<string>();
        foreach (OverburstWorldHighlightStyle style in Enum.GetValues(typeof(OverburstWorldHighlightStyle)))
        {
            string path = ResourceFolder + "/HP_" + style + ".asset";
            HighlightProfile profile = AssetDatabase.LoadAssetAtPath<HighlightProfile>(path);
            if (profile != null) continue;
            var temporary = new GameObject("Highlight profile authoring") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var effect = temporary.AddComponent<HighlightEffect>();
                OverburstWorldHighlight.Configure(effect, style);
                profile = ScriptableObject.CreateInstance<HighlightProfile>();
                profile.Save(effect);
                AssetDatabase.CreateAsset(profile, path);
                AssetDatabase.SaveAssetIfDirty(profile);
                changed.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary); }
        }
        foreach (string path in RendererPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data.rendererFeatures.Exists(feature => feature is HighlightPlusRenderPassFeature)) continue;
            var feature = ScriptableObject.CreateInstance<HighlightPlusRenderPassFeature>();
            feature.name = "OVERBURST Highlight Plus";
            feature.renderPassEvent = UnityEngine.Rendering.Universal.RenderPassEvent.AfterRenderingTransparents;
            feature.Create();
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            changed.Add(path);
        }
        return string.Join("\n", changed);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
}
