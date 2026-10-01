using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class PlayerLightingBuilder
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
        string path = BarbarianHideoutBuilder.HideoutPath;
        if (SceneManager.GetSceneByPath(path).IsValid()) throw new InvalidOperationException("Close Hideout; preserve dirty scenes.");
        string backup = Path.Combine(output, "Before", path);
        if (!File.Exists(backup) || !File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(path)))
            throw new IOException("Current scene exact backup required.");
        foreach (string renderer in RendererPaths) Install(renderer);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        int checks;
        try
        {
            foreach (var root in scene.GetRootGameObjects().Where(r => r.GetComponent<PlayerLightingScope>() != null))
                UnityEngine.Object.DestroyImmediate(root);
            checks = BarbarianHideoutBuilder.ValidateLoaded(scene).Count;
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene-local lighting owner removal failed.");
        }
        finally {SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);}
        File.WriteAllText(Path.Combine(output, "apply_result.json"), JsonConvert.SerializeObject(new {status="PASS",checks,renderers=RendererPaths,runtimeOwner="BeforeSceneLoad / DontDestroyOnLoad"},Formatting.Indented));
        return "Global player lighting installed; scene-local owner removed.";
    }

    static void Install(string path)
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
        if (renderer == null) throw new IOException("Renderer missing: " + path);
        var feature = renderer.rendererFeatures.OfType<PlayerLightingRendererFeature>().SingleOrDefault();
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<PlayerLightingRendererFeature>();
            feature.Create(); AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature);
        }
        feature.name = "Player Original Lighting"; EditorUtility.SetDirty(feature);
        var serialized = new SerializedObject(renderer);
        var map = serialized.FindProperty("m_RendererFeatureMap"); map.arraySize = renderer.rendererFeatures.Count;
        for (int i = 0; i < map.arraySize; i++)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out _, out long localId))
                throw new IOException("Renderer feature local ID missing.");
            map.GetArrayElementAtIndex(i).longValue = localId;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo(); renderer.SetDirty(); EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssetIfDirty(renderer);
    }
}
