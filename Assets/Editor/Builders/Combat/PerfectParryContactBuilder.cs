using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PerfectParryContactBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/Combat/VFX/PerfectParryContact";
    public const string ProfilePath = "Assets/ProjectOverburst/Resources/Combat/VFX/PerfectParryContactProfile.asset";
    private const string SoundRoot = "Assets/ThirdParty/11_사운드/Hack and Slash Sound Library/Audio/Weapon Clash/";
    [MenuItem("OVERBURST/제작/전투/완벽패링 접촉 자산 제작")]
    public static void Build()
    {
        RequireIdle(); Directory.CreateDirectory(Root); AssetDatabase.Refresh();
        Texture2D stroke = MakeStroke();
        Texture2D glow = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/06_VFX/Piloto Studio 1/Textures/SimpleCircleBlur.png");
        Texture2D spark = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/06_VFX/Piloto Studio 1/Textures/Spark_4_02.png");
        if (glow == null || spark == null) throw new InvalidOperationException("Selected source textures must be restored first.");
        var impact = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/06_VFX/Piloto Studio/Textures/LightFlash_Flare1.png");
        if (impact == null) throw new InvalidOperationException("Selected textured metal impact must be restored.");
        Material impactMat = Material("M_ContactImpact", impact);
        Material lineMat = Material("M_ContactStroke", stroke), glowMat = Material("M_ContactGlow", glow), sparkMat = Material("M_ContactSpark", spark);
        var profile = AssetDatabase.LoadAssetAtPath<PerfectParryContactProfile>(ProfilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<PerfectParryContactProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
        profile.upswingAfterimageMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ProjectOverburst/03_Features/Player/VFX/Materials/M_DashAfterimage.mat");
        profile.mainPrefab = Prefab(false, lineMat, impactMat, glowMat, sparkMat);
        profile.additionalPrefab = Prefab(true, lineMat, impactMat, glowMat, sparkMat);
        profile.impact = Sound("PerfectParry_Impact", "Blade Clash Heavy 01.wav", 0f, .18f, .006f, .045f, false);
        profile.ring = Sound("PerfectParry_Ring", "Blade Clash Bright Long 01.wav", .045f, .50f, .025f, .18f, false);
        profile.low = Sound("PerfectParry_Low", "Blade Clash Impact Blunt 01.wav", 0f, .14f, .006f, .06f, true);
        EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
    }
    [MenuItem("OVERBURST/비교/완벽패링/기존 연출로 롤백")]
    public static void UseLegacy() { SetEnhanced(false); }
    [MenuItem("OVERBURST/비교/완벽패링/새 접촉 연출 적용")]
    public static void UseEnhanced() { SetEnhanced(true); }
    public static void SetEnhanced(bool value)
    {
        RequireIdle(); var p = AssetDatabase.LoadAssetAtPath<PerfectParryContactProfile>(ProfilePath);
        if (p == null) throw new InvalidOperationException("Perfect parry profile missing.");
        Undo.RecordObject(p, "Perfect parry presentation"); p.enhancedPresentation = value;
        EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
    }
    private static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("An idle EditMode session is required.");
    }
    private static Texture2D MakeStroke()
    {
        // Authored tapered strike: an angular centre, warm shoulder and a feathered edge.
        var t = new Texture2D(256, 128, TextureFormat.RGBA32, false);
        try
        {
            var pixels = new Color[256 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 256; x++)
            {
                float u = (x + .5f) / 256, v = (y + .5f) / 128;
                float taper = Mathf.Pow(Mathf.Max(0, Mathf.Sin(u * Mathf.PI)), .85f);
                float centre = .50f + .018f * Mathf.Sin(u * Mathf.PI * 2);
                float width = .014f + .078f * taper;
                float edge = Mathf.Abs(v - centre) / width;
                float alpha = Mathf.Clamp01(1f - edge) * Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1 - u) * 12);
                float feather = Mathf.Exp(-edge * edge * 1.7f) * .22f * taper;
                pixels[y * 256 + x] = new Color(1, 1, 1, Mathf.Max(alpha, feather));
            }
            t.SetPixels(pixels); t.Apply(); string path = Root + "/T_ContactStroke.png";
            File.WriteAllBytes(path, t.EncodeToPNG()); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        finally { UnityEngine.Object.DestroyImmediate(t); }
    }
    private static Material Material(string name, Texture texture)
    {
        string path = Root + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", texture); m.SetColor("_BaseColor", new Color(1.8f, 1.8f, 1.8f, 1f));
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 2); m.SetFloat("_ZWrite", 0);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_Cull", (float)CullMode.Off); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_ALPHABLEND_ON"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3100;
        EditorUtility.SetDirty(m); return m;
    }
    private static ParticleSystem Particle(Transform parent, string name, Material material, bool spark)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>(); var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 1; main.startSpeed = 0;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = spark ? 8 : 1;
        main.gravityModifier = spark ? .45f : 0f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.useUnscaledTime = true;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(spark ? .7f : 1, spark ? .5f : .72f), new GradientAlphaKey(0, 1) });
        col.color = gradient;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, spark ? .85f : 1f), new Keyframe(.72f, 1), new Keyframe(1, spark ? .1f : .9f)));
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = material;
        r.renderMode = spark ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        r.velocityScale = spark ? .08f : 0f; r.lengthScale = spark ? 1.6f : 1f;
        r.minParticleSize = 0; r.maxParticleSize = .5f; r.sortingFudge = 2f;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); return ps;
    }
    private static GameObject Prefab(bool small, Material line, Material impact, Material glow, Material spark)
    {
        var go = new GameObject(small ? "PF_PerfectParryAdditional" : "PF_PerfectParryContact");
        try
        {
            var cue = go.AddComponent<PerfectParryContactVfx>(); cue.additional = small;
            if (!small)
            { cue.flash = Particle(go.transform, "WhiteStrike", line, false); var flashMain = cue.flash.main; flashMain.maxParticles = 2; cue.stroke = Particle(go.transform, "ReadableGoldStrike", impact, false); cue.glow = Particle(go.transform, "SoftContactHalo", glow, false); }
            cue.sparks = Particle(go.transform, "DelayedMetalSparks", spark, true);
            return PrefabUtility.SaveAsPrefabAsset(go, Root + "/" + go.name + ".prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    private static AudioClip Sound(string name, string source, float start, float duration, float attack, float fade, bool lowPass)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundRoot + source);
        if (clip == null) throw new InvalidOperationException("Missing selected source sound: " + source);
        var pcm = new float[clip.samples * clip.channels];
        if (!clip.GetData(pcm, 0)) throw new InvalidOperationException("PCM unavailable: " + source);
        int count = Mathf.Min(Mathf.RoundToInt(duration * clip.frequency), clip.samples - Mathf.RoundToInt(start * clip.frequency));
        string path = Root + "/" + name + ".wav";
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            int bytes = count * 2; writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(clip.frequency); writer.Write(clip.frequency * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes); float filtered = 0;
            for (int i = 0; i < count; i++)
            {
                float value = 0; int sample = i + Mathf.RoundToInt(start * clip.frequency);
                for (int c = 0; c < clip.channels; c++) value += pcm[sample * clip.channels + c] / clip.channels;
                if (lowPass) { filtered += (value - filtered) * .055f; value = filtered; }
                float time = i / (float)clip.frequency;
                float envelope = Mathf.Clamp01(time / attack) * Mathf.Clamp01((count / (float)clip.frequency - time) / fade);
                writer.Write((short)(Mathf.Clamp(value * envelope, -1, 1) * short.MaxValue));
            }
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}
