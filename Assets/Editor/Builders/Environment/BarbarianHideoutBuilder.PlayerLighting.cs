using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static partial class BarbarianHideoutBuilder
{
    public static string ApplyPlayerLightingIsolation(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output); RequireIdle();
        if (SceneManager.GetSceneByPath(HideoutPath).IsValid()) throw new InvalidOperationException("Close Hideout; preserve dirty scenes.");
        string backup = Path.Combine(output, "Before", HideoutPath);
        if (!File.Exists(backup) || !File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(HideoutPath))) throw new IOException("Current scene backup required.");
        InstallPlayerLightingFeature("Assets/ProjectOverburst/01_Core/Settings/PC_Renderer.asset");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(HideoutPath, OpenSceneMode.Additive);
        try
        {
            var baseline = JObject.Parse(File.ReadAllText(Path.Combine(output, "baseline.json")));
            var root = new GameObject("Player Original Lighting"); SceneManager.MoveGameObjectToScene(root, scene);
            var scope = root.AddComponent<HideoutPlayerLightingScope>(); var fields = new SerializedObject(scope);
            var color = baseline["color"].Values<float>().ToArray(); float intensity = (float)baseline["intensity"];
            fields.FindProperty("originalMainColor").colorValue = new Color(color[0], color[1], color[2]).linear * intensity;
            var rotation = baseline["rotation"].Values<float>().ToArray();
            fields.FindProperty("originalMainDirection").vector3Value = -(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]) * Vector3.forward);
            var ambient = baseline["probe"].Values<float>().ToArray();
            var sh = fields.FindProperty("originalAmbientProbe"); sh.arraySize = 27;
            for (int i = 0; i < 27; i++) sh.GetArrayElementAtIndex(i).floatValue = ambient[i];
            fields.ApplyModifiedPropertiesWithoutUndo();
            ConfigureEnvironmentLightLayers(scene);
            var checks = ValidateLoaded(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Lighting isolation scene save failed.");
            File.WriteAllText(Path.Combine(output, "apply_result.json"), JsonConvert.SerializeObject(new {status="PASS",checks=checks.Count,playerLayer=HideoutPlayerLightingScope.PlayerLayer,source="pre-camp Hideout backup",environmentLightColorsUnchanged=true}, Formatting.Indented));
            return "Player original light/probe and post-grading pass connected; environment settings retained.";
        }
        finally {SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);}
    }

    static void InstallPlayerLightingFeature(string path)
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
        if (!renderer.rendererFeatures.Any(f => f is HideoutPlayerLightingRendererFeature))
        {
            var feature = ScriptableObject.CreateInstance<HideoutPlayerLightingRendererFeature>();
            feature.name = "Hideout Player Original Lighting"; feature.Create();
            AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature);
        }
        var serialized = new SerializedObject(renderer);
        var map = serialized.FindProperty("m_RendererFeatureMap");
        map.arraySize = renderer.rendererFeatures.Count;
        for (int i = 0; i < map.arraySize; i++)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out _, out long localId))
                throw new IOException("Renderer feature local ID missing.");
            map.GetArrayElementAtIndex(i).longValue = localId;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        renderer.SetDirty(); EditorUtility.SetDirty(renderer); AssetDatabase.SaveAssetIfDirty(renderer);
    }

    static void ConfigureEnvironmentLightLayers(Scene scene)
    {
        foreach (var light in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)))
        {
            var data = light.GetUniversalAdditionalLightData();
            data.renderingLayers = (uint)light.renderingLayerMask & ~HideoutPlayerLightingScope.PlayerLayer;
            data.customShadowLayers = true;
            data.shadowRenderingLayers = (uint)light.renderingLayerMask | HideoutPlayerLightingScope.PlayerLayer;
        }
    }
}
