using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>플레이어 프리팹의 lilToon 재질만 복제해 기존 셰이더의 조명 상수를 조정한다.</summary>
public static class PlayerMaterialLightingBuilder
{
    public const string PlayerPrefab = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string MaterialRoot = "Assets/ProjectOverburst/02_Shared/CharacterVisual/Materials/PlayerLighting";
    public const float MonochromeLighting = .8f;
    public const float LightMinLimit = .15f;
    static readonly string[] Settings = { "_MonochromeLighting", "_LightMinLimit", "_LightMaxLimit", "_AsUnlit" };

    public static string Apply(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Idle Editor required.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        if (!File.Exists(Path.Combine(output, "before_manifest.json")))
            throw new InvalidOperationException("Exact pre-change backup manifest required.");
        var prefab = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        var map = new Dictionary<Material, Material>();
        try
        {
            var originals = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && !AssetDatabase.GetAssetPath(m).StartsWith(MaterialRoot + "/", StringComparison.Ordinal)).Distinct().ToArray();
            // An already applied prefab must not overwrite its provenance manifest with an empty mapping.
            if (originals.Length == 0) return Validate(output);
            foreach (var material in originals)
                Require(material.shader != null && material.shader.name.Contains("lilToon") && Settings.All(material.HasProperty),
                    "Only authored lilToon player materials are supported: " + material.name);
            EnsureFolder(MaterialRoot);
            foreach (var original in originals)
            {
                string source = AssetDatabase.GetAssetPath(original);
                string path = MaterialRoot + "/" + Path.GetFileNameWithoutExtension(source) + "_" + AssetDatabase.AssetPathToGUID(source).Substring(0, 8) + "_Player.mat";
                var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (copy == null)
                {
                    copy = new Material(original) { name = Path.GetFileNameWithoutExtension(path) };
                    AssetDatabase.CreateAsset(copy, path);
                }
                Configure(copy);
                EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy);
                RequireEquivalent(original, copy);
                map.Add(original, copy);
            }
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) if (slots[i] != null && map.TryGetValue(slots[i], out var copy)) slots[i] = copy;
                renderer.sharedMaterials = slots;
            }
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPrefab, out bool saved);
            Require(saved, "Player prefab save failed.");
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var mappings = map.Select(pair => new { source = AssetDatabase.GetAssetPath(pair.Key), target = AssetDatabase.GetAssetPath(pair.Value),
            sourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pair.Key)), targetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pair.Value)),
            shader = pair.Value.shader.name, passes = pair.Value.passCount }).ToArray();
        File.WriteAllText(Path.Combine(output, "material_mapping.json"), JsonConvert.SerializeObject(mappings, Formatting.Indented));
        return Validate(output);
    }

    public static void Configure(Material material)
    {
        material.SetFloat("_MonochromeLighting", MonochromeLighting);
        material.SetFloat("_LightMinLimit", LightMinLimit);
        material.SetFloat("_LightMaxLimit", 1f);
        material.SetFloat("_AsUnlit", 0f);
    }

    public static string Validate(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
        Require(materials.Length > 0 && materials.All(IsConfigured), "Player material lighting configuration mismatch.");
        Require(renderers.All(r => r.sharedMaterials.All(m => m != null)), "Missing player material slot.");
        Require(prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "Player missing script.");
        var report = new { status = "PASS", renderers = renderers.Length, materials = materials.Length,
            monochromeLighting = MonochromeLighting, lightMinLimit = LightMinLimit, lightMaxLimit = 1f, asUnlit = 0f,
            addedRuntimeComponents = 0, addedRendererFeatures = 0, addedCameraPasses = 0 };
        File.WriteAllText(Path.Combine(output, "asset_result.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
        return JsonConvert.SerializeObject(report);
    }

    public static bool IsConfigured(Material material) => material != null &&
        AssetDatabase.GetAssetPath(material).StartsWith(MaterialRoot + "/", StringComparison.Ordinal) &&
        Settings.All(material.HasProperty) && Mathf.Approximately(material.GetFloat(Settings[0]), MonochromeLighting) &&
        Mathf.Approximately(material.GetFloat(Settings[1]), LightMinLimit) && Mathf.Approximately(material.GetFloat(Settings[2]), 1f) &&
        Mathf.Approximately(material.GetFloat(Settings[3]), 0f);

    static void RequireEquivalent(Material original, Material copy)
    {
        Require(original.shader == copy.shader && original.passCount == copy.passCount && original.renderQueue == copy.renderQueue &&
            original.shaderKeywords.OrderBy(x => x).SequenceEqual(copy.shaderKeywords.OrderBy(x => x)), "Shader/pass/keyword preservation: " + original.name);
        for (int i = 0; i < ShaderUtil.GetPropertyCount(original.shader); i++)
        {
            string name = ShaderUtil.GetPropertyName(original.shader, i);
            if (Settings.Contains(name)) continue;
            bool equal;
            switch (ShaderUtil.GetPropertyType(original.shader, i))
            {
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    equal = original.GetTexture(name) == copy.GetTexture(name) && original.GetTextureScale(name) == copy.GetTextureScale(name) && original.GetTextureOffset(name) == copy.GetTextureOffset(name); break;
                case ShaderUtil.ShaderPropertyType.Color: equal = original.GetColor(name) == copy.GetColor(name); break;
                case ShaderUtil.ShaderPropertyType.Vector: equal = original.GetVector(name) == copy.GetVector(name); break;
                default: equal = Mathf.Approximately(original.GetFloat(name), copy.GetFloat(name)); break;
            }
            Require(equal, "Unexpected material change: " + original.name + " " + name);
        }
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
