using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class BarbarianCampUrpRepair
{
    public const string Root = "Assets/ThirdParty/04_환경맵/TopDown Barbarian Camp";

    public static string Apply(string output)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        var paths = AssetDatabase.FindAssets("t:Material", new[] {Root + "/Materials"})
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
        if (paths.Length != 7) throw new InvalidOperationException("Expected seven vendor materials.");
        foreach (var path in paths)
            foreach (var file in new[] {path, path + ".meta"})
                if (!File.Exists(Path.Combine(output, "Before", file)) ||
                    !File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Before", file))))
                    throw new IOException("Exact current backup required: " + file);

        var rows = new List<object>();
        var originals = new Dictionary<Material, Material>();
        try
        {
            foreach (var path in paths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                originals.Add(material, new Material(material));
                string before = material.shader.name;
                if (before == "Standard")
                    MaterialUpgrader.Upgrade(material, new StandardUpgrader(before), MaterialUpgrader.UpgradeFlags.None);
                else if (before == "Autodesk Interactive")
                    MaterialUpgrader.Upgrade(material, new AutodeskInteractiveUpgrader(before), MaterialUpgrader.UpgradeFlags.None);
                else if (before == "Universal Render Pipeline/Lit" && material.name == "Stuff" && material.GetTexture("_BaseMap") == null)
                {
                    // Restore the package's atlas and legacy cutout policy before the official conversion.
                    var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/T_Stuff_a.png");
                    if (atlas == null) throw new IOException("Vendor Stuff atlas missing.");
                    material.shader = Shader.Find("Standard");
                    material.SetTexture("_MainTex", atlas);
                    if (material.GetFloat("_Mode") == 1) material.EnableKeyword("_ALPHATEST_ON");
                    MaterialUpgrader.Upgrade(material, new StandardUpgrader("Standard"), MaterialUpgrader.UpgradeFlags.None);
                }
                else if (material.GetTag("RenderPipeline", false, "") != "UniversalPipeline")
                    throw new InvalidOperationException("Unsupported material conversion: " + path);

                if (material.shader == null || material.GetTag("RenderPipeline", false, "") != "UniversalPipeline")
                    throw new InvalidOperationException("URP conversion failed: " + path);
                string colorMap = material.shader.name.Contains("Autodesk Interactive") ? "_MainTex" : "_BaseMap";
                if (material.GetTexture(colorMap) == null) throw new InvalidOperationException("Albedo lost: " + path);
                if (material.shader.name == "Universal Render Pipeline/Lit")
                    BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
                EditorUtility.SetDirty(material);
                rows.Add(new {path, before, after = material.shader.name, guid = AssetDatabase.AssetPathToGUID(path),
                    albedo = AssetDatabase.GetAssetPath(material.GetTexture(colorMap)), renderQueue = material.renderQueue});
            }
            foreach (var material in originals.Keys) AssetDatabase.SaveAssetIfDirty(material);
        }
        catch
        {
            foreach (var pair in originals)
            {
                pair.Key.shader = pair.Value.shader;
                pair.Key.CopyPropertiesFromMaterial(pair.Value);
                EditorUtility.SetDirty(pair.Key); AssetDatabase.SaveAssetIfDirty(pair.Key);
            }
            throw;
        }
        finally {foreach (var material in originals.Values) UnityEngine.Object.DestroyImmediate(material);}
        File.WriteAllText(Path.Combine(output, "apply_result.json"), JsonConvert.SerializeObject(new
            {status = "PASS", materials = rows, newMaterials = 0, sceneOrPrefabSaved = false}, Formatting.Indented));
        return "Seven vendor materials repaired in place; scenes, meshes and prefab structures untouched.";
    }

    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("Stopped Editor and real account route required.");
        if (!(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            throw new InvalidOperationException("Configured URP required; do not change global pipeline settings.");
    }

    public static string RepairShowcase(string output)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        string path = Root + "/Showcase.unity";
        string backup = Path.Combine(output, "Before", path);
        if (!File.Exists(backup) || !File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(backup)))
            throw new IOException("Exact Showcase backup required.");
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (!opened && scene.isDirty) throw new InvalidOperationException("Preserve unsaved Showcase edits.");
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var defaultMaterial = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial;
        if (defaultMaterial == null || defaultMaterial.GetTag("RenderPipeline", false, "") != "UniversalPipeline")
            throw new InvalidOperationException("URP default material missing.");
        int changed = 0;
        try
        {
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
            {
                var materials = renderer.sharedMaterials; bool replace = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || AssetDatabase.GetAssetPath(materials[i]) != "Resources/unity_builtin_extra") continue;
                    if (materials[i].GetTag("RenderPipeline", false, "") == "UniversalPipeline") continue;
                    materials[i] = defaultMaterial; replace = true; changed++;
                }
                if (replace) renderer.sharedMaterials = materials;
            }
            if (changed > 0 && !EditorSceneManager.SaveScene(scene)) throw new IOException("Showcase material save failed.");
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
        File.WriteAllText(Path.Combine(output, "showcase_repair.json"), JsonConvert.SerializeObject(new
            {status = "PASS", path, changedSlots = changed, defaultMaterial = AssetDatabase.GetAssetPath(defaultMaterial)}, Formatting.Indented));
        return "Showcase builtin floor materials replaced with the existing URP default: " + changed;
    }
}
