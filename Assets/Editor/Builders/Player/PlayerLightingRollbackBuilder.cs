using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class PlayerLightingRollbackBuilder
{
    public static readonly string[] RendererPaths = {
        "Assets/ProjectOverburst/01_Core/Settings/PC_Renderer.asset",
        "Assets/ProjectOverburst/01_Core/Settings/Mobile_Renderer.asset"
    };

    public static string Apply(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Idle Editor required.");
        foreach (string path in RendererPaths)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null) throw new IOException("Renderer missing: " + path);
            var owned = renderer.rendererFeatures.Where(IsRetiredFeature).ToArray();
            foreach (var feature in owned)
            {
                renderer.rendererFeatures.Remove(feature);
                UnityEngine.Object.DestroyImmediate(feature, true);
            }
            if (owned.Length == 0) continue;
            var serialized = new SerializedObject(renderer);
            var map = serialized.FindProperty("m_RendererFeatureMap");
            map.arraySize = renderer.rendererFeatures.Count;
            for (int i = 0; i < map.arraySize; i++)
            {
                if (renderer.rendererFeatures[i] == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out _, out long id))
                    throw new IOException("Unrelated renderer feature is invalid: " + path);
                map.GetArrayElementAtIndex(i).longValue = id;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            renderer.SetDirty(); EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssetIfDirty(renderer);
        }
        return Validate(output);
    }

    public static string Validate(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        var reports = RendererPaths.Select(path =>
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null || renderer.rendererFeatures.Any(f => f == null || IsRetiredFeature(f)) ||
                AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableRendererFeature>().Any(IsRetiredFeature))
                throw new IOException("Retired or missing renderer feature remains: " + path);
            var map = new SerializedObject(renderer).FindProperty("m_RendererFeatureMap");
            if (map.arraySize != renderer.rendererFeatures.Count)
                throw new IOException("Renderer feature map size mismatch: " + path);
            for (int i = 0; i < map.arraySize; i++)
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out _, out long id) ||
                    map.GetArrayElementAtIndex(i).longValue != id)
                    throw new IOException("Renderer feature map ID mismatch: " + path);
            return new { path, guid = AssetDatabase.AssetPathToGUID(path),
                features = renderer.rendererFeatures.Select(f => new { f.name, type = f.GetType().Name, active = f.isActive }).ToArray() };
        }).ToArray();
        var report = new { status = "PASS", renderers = reports };
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "renderer_result.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
        return "Player lighting separation removed; other renderer features preserved.";
    }

    static bool IsRetiredFeature(ScriptableRendererFeature feature) => feature != null &&
        (feature.GetType().Name == "PlayerLightingRendererFeature" ||
         feature.GetType().Name == "HideoutPlayerLightingRendererFeature");
}
