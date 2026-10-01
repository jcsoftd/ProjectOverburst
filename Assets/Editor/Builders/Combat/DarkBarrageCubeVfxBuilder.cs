using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DarkBarrageCubeVfxBuilder
{
    public const string SourceRoot = "Assets/ThirdParty/06_VFX/UniqueVFXUltra/UniqueProjectilesVol_5/Prefabs/";
    public const string AssetRoot = "Assets/ProjectOverburst/Resources/Combat/VFX/DarkCube03";
    public const string ProjectilePath = AssetRoot + "/PF_VFX_DarkBarrage_Cube03_Projectile.prefab";
    public const string HitPath = AssetRoot + "/PF_VFX_DarkBarrage_Cube03_Hit.prefab";
    public const string LandingPath = AssetRoot + "/PF_GRS_HeavyShockwave_Dark.prefab";
    public const string ShockwaveDefinitionPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/VFX/Shockwaves/DF_GRS_HeavyShockwave.asset";
    public const string HeavyPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset";
    public const string Output = "../개인파일/코덱스산출/VFX/20261001_DarkCubeBarrage";
    public const float ProjectileScale = .28f; // First game variant .35 x .8 (user: projectile 20% smaller).

    [MenuItem("OVERBURST/Builders/Combat/어둠 Cube03 탄막 연결")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Idle Editor required");
        var definition = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(HeavyPath);
        if (definition == null || EditorUtility.IsDirty(definition))
            throw new InvalidOperationException("Heavy definition is missing or has unsaved changes");
        var shockwave = AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(ShockwaveDefinitionPath);
        if (shockwave == null || EditorUtility.IsDirty(shockwave))
            throw new InvalidOperationException("Landing definition is missing or has unsaved changes");
        EnsureFolder(AssetRoot + "/Materials");
        var projectile = Create("Projectiles/vfx_Projectile_Cube03.prefab", ProjectilePath, false, ProjectileScale);
        var hit = Create("Hits/vfx_Hit_Cube03.prefab", HitPath, true, .4f);
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("elementVfx.darkBarrageProjectile").objectReferenceValue = projectile;
        serialized.FindProperty("elementVfx.darkBarrageHit").objectReferenceValue = hit;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(definition);
        var landing = CreateLanding(shockwave.neutralPrefab);
        var landingSerialized = new SerializedObject(shockwave);
        landingSerialized.FindProperty("darkPrefab").objectReferenceValue = landing;
        landingSerialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(shockwave);
    }

    private static GameObject CreateLanding(GameObject source)
    {
        if (source == null) throw new InvalidOperationException("Landing source missing");
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(LandingPath);
        if (existing != null && EditorUtility.IsDirty(existing))
            throw new InvalidOperationException("Unsaved dark landing prefab");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = Path.GetFileNameWithoutExtension(LandingPath);
            // Preserve the authored pressure wave, playback and particle timing; only its material changes.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                {
                    if (original == null) return null;
                    string path = AssetRoot + "/Materials/" + original.name + "_Dark.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material != null && EditorUtility.IsDirty(material))
                        throw new InvalidOperationException("Unsaved dark landing material");
                    if (material == null) { material = new Material(original); AssetDatabase.CreateAsset(material, path); }
                    else material.CopyPropertiesFromMaterial(original);
                    material.name = original.name + "_Dark";
                    if (!material.HasProperty("_Colour")) throw new InvalidOperationException("Landing color property missing");
                    material.SetColor("_Colour", new Color(.48f, .012f, .04f, original.GetColor("_Colour").a));
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                    return material;
                }).ToArray();
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, LandingPath, out bool saved);
            if (!saved || prefab == null) throw new InvalidOperationException("Dark landing save failed");
            return prefab;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static GameObject Create(string sourceRelative, string destination, bool hit, float scale)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoot + sourceRelative);
        if (source == null) throw new InvalidOperationException("Cube03 source missing: " + sourceRelative);
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(destination);
        if (existing != null && EditorUtility.IsDirty(existing))
            throw new InvalidOperationException("Unsaved VFX prefab: " + destination);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = Path.GetFileNameWithoutExtension(destination);
            root.transform.localScale = Vector3.one * scale;
            // Movement, collision and impact are owned by DarkBarrageScheduler, including pooled reuse.
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && script.GetType().Name == "ProjectileMoveScript") Object.DestroyImmediate(script);
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
            foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.useUnscaledTime = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                if (hit) main.loop = false;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && !renderer.enabled) { var emission = ps.emission; emission.enabled = false; }
                Color tint = ps.name.StartsWith("Head") ? new Color(hit ? .42f : .32f, .006f, .028f)
                    : ps.name == "Cube_01" ? new Color(.15f, .002f, .014f)
                    : ps.name.EndsWith("02") ? new Color(.10f, .001f, .009f)
                    : new Color(.24f, .004f, .021f);
                main.startColor = Recolor(main.startColor, tint);
                var lifetime = ps.colorOverLifetime;
                if (lifetime.enabled) lifetime.color = Recolor(lifetime.color, Color.white);
                var speed = ps.colorBySpeed;
                if (speed.enabled) speed.color = Recolor(speed.color, Color.white);
                var trails = ps.trails;
                if (trails.enabled) { trails.colorOverLifetime = Recolor(trails.colorOverLifetime, Color.white); trails.colorOverTrail = Recolor(trails.colorOverTrail, Color.white); }
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (var trail in root.GetComponentsInChildren<TrailRenderer>(true))
            {
                trail.colorGradient = Recolor(trail.colorGradient, trail.name.EndsWith("02")
                    ? new Color(.065f, .001f, .008f) : new Color(.25f, .004f, .024f));
                trail.widthMultiplier *= scale;
                trail.autodestruct = false;
                trail.emitting = false;
                trail.Clear();
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(Material).ToArray();
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            { light.color = new Color(.4f, .006f, .028f); light.intensity = Mathf.Min(light.intensity, 2f); }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, destination, out bool saved);
            if (!saved || prefab == null) throw new InvalidOperationException("VFX save failed");
            return prefab;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static Material Material(Material source)
    {
        if (source == null) return null;
        string path = AssetRoot + "/Materials/" + source.name + "_Dark.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null && EditorUtility.IsDirty(material)) throw new InvalidOperationException("Unsaved VFX material: " + path);
        if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
        else material.CopyPropertiesFromMaterial(source);
        material.name = source.name + "_Dark";
        // Particle and trail vertex colors supply the crimson hue. Lower the supplier's white HDR gain.
        if (material.HasProperty("_Color"))
        {
            Color old = source.GetColor("_Color");
            float gain = Mathf.Clamp(Mathf.Max(old.r, Mathf.Max(old.g, old.b)), 1f, 6f);
            material.SetColor("_Color", new Color(gain, gain, gain, old.a));
        }
        if (material.HasProperty("_IntersectionColor")) material.SetColor("_IntersectionColor", new Color(.22f, .004f, .02f, 1f));
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    private static Color Recolor(Color color, Color tint)
    {
        float brightness = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        return new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, color.a);
    }

    private static Gradient Recolor(Gradient source, Color tint)
    {
        var gradient = new Gradient { mode = source.mode };
        gradient.SetKeys(source.colorKeys.Select(k => new GradientColorKey(Recolor(k.color, tint), k.time)).ToArray(), source.alphaKeys);
        return gradient;
    }

    private static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient value, Color tint)
    {
        switch (value.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Recolor(value.color, tint));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Recolor(value.colorMin, tint), Recolor(value.colorMax, tint));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Recolor(value.gradientMin, tint), Recolor(value.gradientMax, tint));
            default:
                var mapped = new ParticleSystem.MinMaxGradient(Recolor(value.gradient, tint));
                mapped.mode = value.mode;
                return mapped;
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
