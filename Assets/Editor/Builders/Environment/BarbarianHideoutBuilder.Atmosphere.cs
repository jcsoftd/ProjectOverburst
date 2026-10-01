using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class BarbarianHideoutBuilder
{
    public const string AtmosphereRoot = "Assets/ProjectOverburst/05_Art/Environment/BarbarianHideout";
    public const string AtmosphereProfile = AtmosphereRoot + "/BarbarianHideoutProfile.asset";
    static readonly Vector3 CampCenter = new Vector3(0, 0, 6);
    static void ApplyCampLighting(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var sun = roots.Single(r => r.name == "Directional Light").GetComponent<Light>();
        // The supplied demo uses this warm directional light with a default sky.
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);
        sun.color = new Color(1, .95686275f, .8392157f); sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft; sun.lightmapBakeType = LightmapBakeType.Realtime;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.65f, .68f, .72f);
        RenderSettings.ambientEquatorColor = new Color(.5f, .51f, .53f);
        RenderSettings.ambientGroundColor = new Color(.3f, .27f, .24f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.43f, .45f, .47f);
        RenderSettings.fogStartDistance = 55; RenderSettings.fogEndDistance = 120;
        EnsureFolder(AtmosphereRoot);
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AtmosphereProfile);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, AtmosphereProfile);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(.8f); color.contrast.Override(8); color.saturation.Override(-10);
            var balance = profile.Add<WhiteBalance>(true); balance.temperature.Override(-8);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(.24f); bloom.threshold.Override(1.1f); bloom.scatter.Override(.55f);
            var vignette = profile.Add<Vignette>(true); vignette.intensity.Override(.12f); vignette.smoothness.Override(.65f);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
        }
        var volume = roots.Single(r => r.name == "Global Volume").GetComponent<Volume>();
        volume.sharedProfile = profile; volume.isGlobal = true; volume.weight = 1; volume.priority = 10;
    }
    static Texture2D SoftTexture()
    {
        string path = AtmosphereRoot + "/TX_CampAtmosphereSoft.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Camp atmosphere soft particle", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float radius = new Vector2((x + .5f) / 32 - 1, (y + .5f) / 32 - 1).magnitude;
            float alpha = Mathf.Pow(Mathf.Clamp01(1 - radius), 2) * Mathf.Lerp(.7f, 1f, Mathf.PerlinNoise(x * .14f, y * .14f));
            texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        texture.Apply(); AssetDatabase.CreateAsset(texture, path); return texture;
    }
    static Material ParticleMaterial(string name, bool additive)
    {
        string path = AtmosphereRoot + "/MAT_" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) throw new InvalidOperationException("URP particle shader missing.");
        var mat = new Material(shader) { name = name, renderQueue = (int)RenderQueue.Transparent };
        mat.SetTexture("_BaseMap", SoftTexture()); mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", additive ? 2 : 0);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0); mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.SetOverrideTag("RenderType", "Transparent");
        AssetDatabase.CreateAsset(mat, path); return mat;
    }
    static ParticleSystem Particle(Transform parent, string name, Vector3 position, float lifetime, float rate, Vector2 size, Color color, Material material)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = position;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.prewarm = true; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.duration = lifetime;
        main.startLifetime = lifetime; main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y); main.startColor = color;
        main.maxParticles = Mathf.CeilToInt(lifetime * rate) + 16;
        var emission = ps.emission; emission.rateOverTime = rate;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = Vector3.one * .3f;
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        var alpha = new Gradient(); alpha.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(.6f, .7f), new GradientAlphaKey(0, 1) });
        var over = ps.colorOverLifetime; over.enabled = true; over.color = alpha;
        return ps;
    }
    static void AddCampAtmosphere(GameObject root)
    {
        EnsureFolder(AtmosphereRoot);
        var effects = new GameObject("Camp Atmosphere"); effects.transform.SetParent(root.transform, false);
        var soft = ParticleMaterial("CampMist", false); var glow = ParticleMaterial("CampFire", true);
        var boiler = root.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == "Boiler");
        var firePosition = boiler.bounds.center; firePosition.y = .25f;
        var flame = Particle(effects.transform, "Campfire Flames", firePosition, .9f, 24, new Vector2(.2f, .45f), new Color(3.2f, .65f, .08f, .85f), glow);
        var flameVelocity = flame.velocityOverLifetime; flameVelocity.enabled = true; flameVelocity.space = ParticleSystemSimulationSpace.World; flameVelocity.x = 0; flameVelocity.y = .8f; flameVelocity.z = 0;
        var flameShape = flame.shape; flameShape.shapeType = ParticleSystemShapeType.Cone; flameShape.angle = 16; flameShape.radius = .22f;
        flame.transform.rotation = Quaternion.Euler(-90, 0, 0);
        var flameNoise = flame.noise; flameNoise.enabled = true; flameNoise.strength = .06f; flameNoise.frequency = 1.4f;
        var smokePosition = firePosition; smokePosition.y = 1.7f;
        var smoke = Particle(effects.transform, "Cauldron Smoke", smokePosition, 6, 2, new Vector2(.25f, .5f), new Color(.5f, .53f, .55f, .22f), soft);
        var smokeVelocity = smoke.velocityOverLifetime; smokeVelocity.enabled = true; smokeVelocity.space = ParticleSystemSimulationSpace.World; smokeVelocity.x = .07f; smokeVelocity.y = .25f; smokeVelocity.z = 0;
        var smokeSize = smoke.sizeOverLifetime; smokeSize.enabled = true; smokeSize.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .5f), new Keyframe(1, 3)));
        var ember = Particle(effects.transform, "Campfire Embers", firePosition, 2.2f, 5, new Vector2(.025f, .06f), new Color(4, 1.3f, .15f, .8f), glow);
        var emberVelocity = ember.velocityOverLifetime; emberVelocity.enabled = true; emberVelocity.space = ParticleSystemSimulationSpace.World; emberVelocity.x = new ParticleSystem.MinMaxCurve(-.2f, .2f); emberVelocity.y = new ParticleSystem.MinMaxCurve(.5f, 1.1f); emberVelocity.z = new ParticleSystem.MinMaxCurve(-.2f,.2f);
        var lightObject = new GameObject("Campfire Warm Light"); lightObject.transform.SetParent(effects.transform, false); lightObject.transform.position = firePosition + Vector3.up * .5f;
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(1, .42f, .1f); light.intensity = 4.5f; light.range = 6; light.shadows = LightShadows.None;
        var mist = Particle(effects.transform, "Camp Ground Mist", CampCenter + Vector3.up * .2f, 18, 4, new Vector2(6, 11), new Color(.63f, .68f, .73f, .035f), soft);
        var mistShape = mist.shape; mistShape.scale = new Vector3(44, .15f, 38);
        var mistMain = mist.main; mistMain.startSize3D = true; mistMain.startSizeX = new ParticleSystem.MinMaxCurve(6, 11); mistMain.startSizeY = new ParticleSystem.MinMaxCurve(.5f, 1.4f); mistMain.startSizeZ = 1;
        var mistVelocity = mist.velocityOverLifetime; mistVelocity.enabled = true; mistVelocity.space = ParticleSystemSimulationSpace.World; mistVelocity.x = .1f; mistVelocity.y = 0; mistVelocity.z = -.04f;
        var dust = Particle(effects.transform, "Camp Ambient Dust", CampCenter + Vector3.up, 10, 5, new Vector2(.025f, .055f), new Color(.76f, .71f, .6f, .18f), soft);
        var dustShape = dust.shape; dustShape.scale = new Vector3(32, 2, 28);
        var dustVelocity = dust.velocityOverLifetime; dustVelocity.enabled = true; dustVelocity.space = ParticleSystemSimulationSpace.World; dustVelocity.x = .08f; dustVelocity.y = .035f; dustVelocity.z = 0;
    }

}
