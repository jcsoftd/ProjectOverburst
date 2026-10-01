using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DarkBarrageSlamVfxBuilder
{
    public const string SourcePath = "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/ARPG Realistic Demon VS Angels/Medium/Demon_Explotion.prefab";
    public const string Root = "Assets/ProjectOverburst/Resources/Combat/VFX/DarkDemonSlam";
    public const string PrefabPath = Root + "/PF_VFX_DarkBarrage_Demon_Slam.prefab";
    // The authored 25 m ground decal becomes 7.2 m across, matching the existing 3.6 m reference radius.
    public const float PrefabScale = .288f;

    [MenuItem("OVERBURST/Builders/Combat/어둠 Demon 착지 연결")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Idle Editor required");
        var heavy = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(DarkBarrageCubeVfxBuilder.HeavyPath);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (heavy == null || source == null || EditorUtility.IsDirty(heavy) || (existing != null && EditorUtility.IsDirty(existing)))
            throw new InvalidOperationException("Source missing or destination has unsaved changes");
        EnsureFolder(Root + "/Materials");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = Path.GetFileNameWithoutExtension(PrefabPath);
            instance.transform.localScale = Vector3.one * PrefabScale;
            foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                // Preserve the supplier's transparent distortion layers and their material references.
                bool distortion = renderer != null && renderer.sharedMaterial != null
                    && renderer.sharedMaterial.shader.name == "Piloto Studio/Piloto Warp";
                var main = particles.main;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.useUnscaledTime = false;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                if (!distortion)
                {
                    bool smoke = IsSmoke(particles.name);
                    Color tint = smoke ? new Color(.55f, .012f, .035f) : new Color(.8f, .008f, .065f);
                    main.startColor = Recolor(main.startColor, tint);
                    var lifetime = particles.colorOverLifetime;
                    if (lifetime.enabled) lifetime.color = Recolor(lifetime.color, tint);
                    var speed = particles.colorBySpeed;
                    if (speed.enabled) speed.color = Recolor(speed.color, tint);
                    if (renderer != null)
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(CloneMaterial).ToArray();
                }
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath, out bool saved);
            if (!saved || prefab == null) throw new InvalidOperationException("Slam prefab save failed");
            var serialized = new SerializedObject(heavy);
            serialized.FindProperty("elementVfx.darkBarrageSlam").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(heavy);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static bool IsSmoke(string name) => name.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0;

    private static Material CloneMaterial(Material source)
    {
        if (source == null) return null;
        string path = Root + "/Materials/" + source.name + "_Dark.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null && EditorUtility.IsDirty(material)) throw new InvalidOperationException("Unsaved material: " + path);
        if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
        else material.CopyPropertiesFromMaterial(source);
        material.name = source.name + "_Dark";
        bool smoke = IsSmoke(source.name);
        foreach (string property in new[] { "_WhiteColor", "_MidColor", "_LastColor" })
        {
            if (!source.HasProperty(property)) continue;
            Color value = source.GetColor(property);
            float brightness = Mathf.Min(2.5f, Mathf.Max(value.r, Mathf.Max(value.g, value.b)));
            Color tint = smoke ? new Color(.25f, .006f, .018f) : new Color(.75f, .006f, .055f);
            if (property == "_MidColor") tint *= .65f;
            if (property == "_LastColor") tint *= .2f;
            material.SetColor(property, new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, value.a));
        }
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    private static Color Recolor(Color source, Color tint)
    {
        float brightness = Mathf.Max(source.r, Mathf.Max(source.g, source.b));
        return new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, source.a);
    }

    private static Gradient Recolor(Gradient source, Color tint)
    {
        var result = new Gradient { mode = source.mode };
        result.SetKeys(source.colorKeys.Select(k => new GradientColorKey(Recolor(k.color, tint), k.time)).ToArray(), source.alphaKeys);
        return result;
    }

    private static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient value, Color tint)
    {
        switch (value.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Recolor(value.color, tint));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Recolor(value.colorMin, tint), Recolor(value.colorMax, tint));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Recolor(value.gradientMin, tint), Recolor(value.gradientMax, tint));
            default:
                var result = new ParticleSystem.MinMaxGradient(Recolor(value.gradient, tint));
                result.mode = value.mode;
                return result;
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
