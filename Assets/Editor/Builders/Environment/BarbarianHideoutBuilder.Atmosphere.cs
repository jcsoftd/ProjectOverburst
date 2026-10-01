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
    public const string ExpandedPath = GeneratedRoot + "/PF_BarbarianCamp_Hideout_Expanded.prefab";
    public const string AtmosphereRoot = "Assets/ProjectOverburst/05_Art/Environment/BarbarianHideout";
    public const string AtmosphereProfile = AtmosphereRoot + "/BarbarianHideoutProfile.asset";
    static readonly Vector3 CampCenter = new Vector3(0, 0, 6);
    const float Expansion = 1.4f;

    public static string ExpandWithAtmosphere(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        RequireIdle();
        if (SceneManager.GetSceneByPath(HideoutPath).IsValid()) throw new InvalidOperationException("Close Hideout before applying.");
        if (!File.Exists(Path.Combine(output, "BeforeAtmosphere/HideoutScene.unity"))) throw new IOException("Current camp scene backup required.");
        if (!File.ReadAllBytes(Path.Combine(output, "BeforeAtmosphere/HideoutScene.unity")).SequenceEqual(File.ReadAllBytes(HideoutPath)))
            throw new InvalidOperationException("Camp scene changed after backup.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ExpandedPath) == null)
        {
            if (!AssetDatabase.CopyAsset(EnvironmentPath, ExpandedPath)) throw new IOException("Environment copy failed.");
            var root = PrefabUtility.LoadPrefabContents(ExpandedPath);
            try
            {
                var camp = root.transform.Find("TD_Barbarian_Camp_Scene");
                if (camp == null) throw new InvalidOperationException("Camp layout missing.");
                var children = camp.Cast<Transform>().ToArray();
                var anchors = children.Where(t => MajorStructure(t.name)).Select(t => t.position).ToArray();
                foreach (var child in children)
                {
                    string name = child.name.ToLowerInvariant();
                    bool perimeter = name.Contains("fence") || name.Contains("tree") || name.Contains("rock") || name.Contains("branch") || name.Contains("bush") || name.Contains("stone");
                    var anchor = perimeter || MajorStructure(name) ? child.position : anchors.OrderBy(p => PlanarDistance(p, child.position)).First();
                    child.position += ExpandPoint(anchor) - anchor;
                    if (name.Contains("fence")) child.localScale = Vector3.Scale(child.localScale, new Vector3(Expansion, 1, Expansion));
                }
                var ground = root.transform.Find("Camp Ground");
                ground.localScale *= 1.2f;
                foreach (var wall in root.transform.Cast<Transform>().Where(t => t.name.StartsWith("Camp Boundary ", StringComparison.Ordinal)).ToArray()) Object.DestroyImmediate(wall.gameObject);
                AddBoundary(root.transform, ground.GetComponent<Renderer>().bounds);
                AddCampExtensions(root, camp);
                AddCampAtmosphere(root);
                PrefabUtility.SaveAsPrefabAsset(root, ExpandedPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(HideoutPath, OpenSceneMode.Additive);
        try
        {
            var old = scene.GetRootGameObjects().Single(r => r.name == "Barbarian Camp Environment");
            var expanded = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ExpandedPath), scene);
            expanded.name = old.name; Object.DestroyImmediate(old);
            var services = scene.GetRootGameObjects().Single(r => r.name == "Hideout Services");
            foreach (var component in services.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is StashInteractable || component is GeneralGoodsMerchantInteractable || component is HubReturnPoint)
                    component.transform.position = ExpandPoint(component.transform.position);
            scene.GetRootGameObjects().Single(r => r.name == "Training Grounds").transform.position += Vector3.right * 15;
            scene.GetRootGameObjects().Single(r => r.name == "Test Pickup Origin").transform.position += Vector3.right * 15;
            SceneManager.SetActiveScene(scene);
            ApplyCampLighting(scene);
            var checks = ValidateLoaded(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Expanded camp save failed.");
            File.WriteAllText(Path.Combine(output, "atmosphere_result.json"), JsonConvert.SerializeObject(new {
                status = "PASS", expansion = Expansion, environment = ExpandedPath,
                originalGuid = AssetDatabase.AssetPathToGUID(EnvironmentPath), expandedGuid = AssetDatabase.AssetPathToGUID(ExpandedPath), checks = checks.Count,
                vendorFogEnabled = false, originalSunIntensity = .7f, addedParticles = 5
            }, Formatting.Indented));
            return "Expanded camp, original sun and added atmosphere applied.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static bool MajorStructure(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("tent") || n.Contains("canopy") || n.Contains("cart") || n == "boiler";
    }
    static void AddCampExtensions(GameObject root, Transform camp)
    {
        var extension = new GameObject("Camp Extensions"); extension.transform.SetParent(root.transform, false);
        Transform Template(string prefix) => camp.Cast<Transform>().First(t => t.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        void Place(string template, string name, Vector3 position, float yaw)
        {
            var source = Template(template);
            var copy = Object.Instantiate(source.gameObject, extension.transform, true);
            copy.name = name; copy.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        }
        Place("Redskin_tent_2", "Provision Tent", new Vector3(-9, .02f, 3), 20);
        Place("Redskin_tent_2", "Smith Tent", new Vector3(10, .02f, 1), -80);
        Place("Redskin_tent_2", "Supply Tent", new Vector3(-5, .02f, 17), 0);
        Place("Redskin_tent_2", "Guard Tent", new Vector3(6, .02f, 18), 180);
        Place("Canopy_little_1", "Provision Canopy", new Vector3(-12.5f, .02f, 6.5f), 90);
        Place("Canopy_little_2", "Guard Canopy", new Vector3(3, .02f, 16), 0);
        Place("Barrel_large", "Provision Barrel 1", new Vector3(-11, .02f, 2), 0);
        Place("Barrel_small", "Provision Barrel 2", new Vector3(-11.6f, .02f, 2), 20);
        Place("Box_large", "Smith Supply Crate", new Vector3(12, .02f, 2.5f), 10);
        Place("Box_small", "Smith Tool Crate", new Vector3(12.2f, .02f, 3.4f), -10);
        Place("Barrel_large", "Supply Barrel 1", new Vector3(-7, .02f, 18), 0);
        Place("Barrel_small", "Supply Barrel 2", new Vector3(-7.7f, .02f, 18), 30);
        Place("Box_large", "Guard Crate", new Vector3(8.2f, .02f, 17.5f), 15);
        Place("Basket_1", "Guard Basket", new Vector3(8.5f, .02f, 16.5f), 0);
    }
    static float PlanarDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return (a - b).sqrMagnitude; }
    static Vector3 ExpandPoint(Vector3 p)
    {
        var delta = p - CampCenter; delta.y = 0;
        return p + delta * (Expansion - 1);
    }
    static void ApplyCampLighting(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var sun = roots.Single(r => r.name == "Directional Light").GetComponent<Light>();
        // The supplied demo uses this warm directional light with a default sky.
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);
        sun.color = new Color(1, .95686275f, .8392157f); sun.intensity = .7f;
        sun.shadows = LightShadows.Soft; sun.lightmapBakeType = LightmapBakeType.Realtime;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.28f, .34f, .42f);
        RenderSettings.ambientEquatorColor = new Color(.18f, .21f, .25f);
        RenderSettings.ambientGroundColor = new Color(.08f, .075f, .065f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.31f, .35f, .40f);
        RenderSettings.fogStartDistance = 24; RenderSettings.fogEndDistance = 85;
        EnsureFolder(AtmosphereRoot);
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AtmosphereProfile);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, AtmosphereProfile);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(.4f); color.contrast.Override(8); color.saturation.Override(-10);
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
        var flame = Particle(effects.transform, "Campfire Flames", firePosition, .9f, 24, new Vector2(.2f, .45f), new Color(3.2f, .95f, .12f, .85f), glow);
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
    public static string RepairStoredParticleVelocity(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        RequireIdle();
        if (SceneManager.GetSceneByPath(HideoutPath).IsValid()) throw new InvalidOperationException("Close Hideout before repairing its prefab sources.");
        var paths = new[] {ExpandedPath, WorkshopPath};
        int count = 0;
        foreach (string path in paths)
        {
            string backup = Path.Combine(output,"BeforeParticleRepair",path);
            if (!File.Exists(backup)) throw new IOException("Particle prefab backup required.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var velocity = ps.velocityOverLifetime;
                    if (ps.name == "Campfire Embers")
                    {
                        velocity.x = new ParticleSystem.MinMaxCurve(-.2f,.2f);
                        velocity.y = new ParticleSystem.MinMaxCurve(.5f,1.1f);
                        velocity.z = new ParticleSystem.MinMaxCurve(-.2f,.2f);
                    }
                    else
                    {
                        Vector3 speed = Vector3.zero;
                        if (ps.name == "Campfire Flames" || ps.name == "Forge Fire") speed.y = .8f;
                        else if (ps.name == "Cauldron Smoke") speed = new Vector3(.07f,.25f,0);
                        else if (ps.name == "Camp Ground Mist") speed = new Vector3(.1f,0,-.04f);
                        else if (ps.name == "Camp Ambient Dust") speed = new Vector3(.08f,.035f,0);
                        velocity.x = speed.x; velocity.y = speed.y; velocity.z = speed.z;
                    }
                    velocity.orbitalX = 0; velocity.orbitalY = 0; velocity.orbitalZ = 0;
                    velocity.orbitalOffsetX = 0; velocity.orbitalOffsetY = 0; velocity.orbitalOffsetZ = 0;
                    velocity.radial = 0; velocity.speedModifier = 1;
                    count++;
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        if (count != 6) throw new InvalidOperationException("Expected six owned atmosphere particle systems.");
        File.WriteAllText(Path.Combine(output,"particle_repair_result.json"),JsonConvert.SerializeObject(new{status="PASS",particles=count},Formatting.Indented));
        return "Six particle velocity modes repaired and saved.";
    }

}
