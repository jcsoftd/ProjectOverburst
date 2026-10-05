using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class VolumetricBloodBuilder
{
    public const string Vendor = "Assets/ThirdParty/06_VFX/KriptoFX/VolumetricBloodFluids";
    public const string Root = "Assets/ProjectOverburst/Resources/Combat/VolumetricBlood";
    public const string CatalogPath = "Assets/ProjectOverburst/Resources/Combat/VolumetricBloodCatalog.asset";
    // Same-camera A/B reference at their existing defaults; keep the user's C multiplier at 1.
    static readonly float[] ScreenScales = { 2.4f, 2.3f, 4.8f, 5.7f, 7.7f, 5.5f, 2.8f, 1.9f, 2.3f, 3f, 2.9f, 2.4f, 1.25f, 4.3f, 2f, 6.45f, 2.1f, 38f, 24f };
    public static float DefaultScreenScale(int form) => ScreenScales[form];
    [MenuItem("OVERBURST/Combat/Blood Comparison/C 입체 혈흔 생성")]
    public static string Build()
    {
        PlayerCombatFacingVfxBuilder.RequireIdle();
        Folder(Root + "/Sprays"); Folder(Root + "/Decals");
        var catalog = AssetDatabase.LoadAssetAtPath<BloodEffectsPackCatalog>(CatalogPath);
        if (!catalog) { catalog = ScriptableObject.CreateInstance<BloodEffectsPackCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        catalog.volumetric = true;
        var sprays = new List<BloodEffectsPackCatalog.Spray>();
        var decals = new List<GameObject>();
        string[] names = Enumerable.Range(1,15).Select(i => (i == 13 ? "blood" : "Blood") + i).Concat(new[]{"Blood2_Left","Blood2_Right"}).ToArray();
        for (int i = 0; i < names.Length + 2; i++)
        {
            bool drip = i >= names.Length;
            string sourceName = drip ? (i == names.Length ? "Blood5" : "Blood6") : names[i];
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Vendor + "/Prefabs/" + sourceName + ".prefab");
            if (!source) throw new InvalidOperationException("C source missing: " + sourceName);
            // Read prefab assets without instantiating supplier ExecuteAlways lifecycle scripts.
            var animations = source.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c && c.GetType().Name == "BFX_ManualAnimationUpdate").ToArray();
            if (animations.Length == 0) throw new InvalidOperationException("VAT animation missing: " + sourceName);
            var root = new GameObject("PF_Volumetric_" + (drip ? "Drip" + (i - names.Length + 1) : sourceName));
            root.SetActive(false);
            try
            {
                var data = root.AddComponent<VolumetricBloodAnimationData>();
                var mapping = new Dictionary<Transform,Transform> { [source.transform] = root.transform };
                Transform CopyTransform(Transform original)
                {
                    if (mapping.TryGetValue(original, out var value)) return value;
                    var parent = CopyTransform(original.parent);
                    value = new GameObject(original.name).transform;
                    value.SetParent(parent, false); value.localPosition = original.localPosition;
                    value.localRotation = original.localRotation; value.localScale = original.localScale;
                    mapping.Add(original,value); return value;
                }
                var layers = new List<VolumetricBloodAnimationData.Layer>();
                float lifetime = .1f;
                foreach (var component in animations)
                {
                    var properties = new SerializedObject(component);
                    var original = component.GetComponent<MeshRenderer>();
                    var filter = component.GetComponent<MeshFilter>();
                    var mesh = filter ? filter.sharedMesh : null;
                    // blood13 has one stale mesh GUID; recover the mesh paired with its VAT material.
                    if (!mesh && original && original.sharedMaterial)
                    {
                        string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(original.sharedMaterial)).Replace("\\", "/");
                        mesh = AssetDatabase.FindAssets("t:Mesh", new[]{folder}).SelectMany(g=>AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g))).OfType<Mesh>().FirstOrDefault();
                    }
                    if (!original || !filter || !mesh || original.sharedMaterials.Any(m => !m || !m.shader)) throw new InvalidOperationException("C mesh/material missing: " + sourceName);
                    var target = CopyTransform(component.transform).gameObject;
                    target.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = target.AddComponent<MeshRenderer>(); renderer.sharedMaterials = original.sharedMaterials;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.allowOcclusionWhenDynamic = false;
                    float seconds = drip ? .5f : Mathf.Max(.1f,properties.FindProperty("TimeLimit").floatValue);
                    var curve = properties.FindProperty("AnimationSpeed").animationCurveValue;
                    layers.Add(new VolumetricBloodAnimationData.Layer { renderer = renderer,
                        speed = new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode },
                        frames = properties.FindProperty("FramesCount").floatValue, seconds = seconds,
                        offset = properties.FindProperty("OffsetFrames").floatValue });
                    lifetime = Mathf.Max(lifetime, seconds);
                }
                data.layers = layers.ToArray();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Sprays/" + root.name + ".prefab");
                sprays.Add(new BloodEffectsPackCatalog.Spray { label = drip ? "이동 핏방울 " + (i - names.Length + 1) : sourceName,
                    prefab = prefab, shapeMask = drip ? 0 : ShapeMask(i), flowing = drip, minimumPriority = drip ? 1 : 0, impactAccent = !drip && (i == 7 || i == 12),
                    scale = source.transform.localScale.x * DefaultScreenScale(i), lifetime = lifetime + .05f });
            }
            finally { Object.DestroyImmediate(root); }
            if (drip) { sprays[i].groundPrefab = decals[i == names.Length ? 4 : 5]; continue; }
            var originalDecal = source.GetComponentInChildren<DecalProjector>(true);
            // Blood5 is authored without a ground projector; use the adjacent native splatter.
            if (!originalDecal) originalDecal = AssetDatabase.LoadAssetAtPath<GameObject>(Vendor + "/Prefabs/Blood4.prefab").GetComponentInChildren<DecalProjector>(true);
            if (!originalDecal || !originalDecal.material || !originalDecal.material.shader.isSupported) throw new InvalidOperationException("URP C decal missing: " + sourceName);
            var mark = new GameObject("PF_VolumetricGround_" + sourceName); mark.SetActive(false);
            try
            {
                var projector = mark.AddComponent<DecalProjector>();
                projector.material = originalDecal.material; projector.size = originalDecal.size;
                var native = mark.AddComponent<VolumetricBloodGroundData>();
                var authored = originalDecal.GetComponents<MonoBehaviour>();
                var reveal = new SerializedObject(authored.First(c => c && c.GetType().Name == "BFX_ShaderProperies"));
                var landing = new SerializedObject(authored.First(c => c && c.GetType().Name == "BFX_DecalSettings"));
                native.cutout = CopyCurve(reveal.FindProperty("FloatCurve").animationCurveValue);
                native.curveSeconds = reveal.FindProperty("GraphTimeMultiplier").floatValue;
                native.intensity = reveal.FindProperty("GraphIntensityMultiplier").floatValue;
                native.landing = CopyCurve(landing.FindProperty("TimeByHeight").animationCurveValue);
                native.heightMax = landing.FindProperty("TimeHeightMax").floatValue;
                native.scaleMin = landing.FindProperty("TimeScaleMin").vector3Value;
                native.scaleMax = landing.FindProperty("TimeScaleMax").vector3Value;
                native.offsetMin = landing.FindProperty("TimeOffsetMin").vector3Value;
                native.offsetMax = landing.FindProperty("TimeOffsetMax").vector3Value;
                native.offset = source.transform.InverseTransformPoint(originalDecal.transform.position);
                var decalScale = originalDecal.transform.localScale;
                native.size = new Vector3(decalScale.x, decalScale.z, .07f);
                projector.drawDistance = 40f; projector.fadeScale = .8f;
                var ground = PrefabUtility.SaveAsPrefabAsset(mark,Root + "/Decals/" + mark.name + ".prefab");
                decals.Add(ground); sprays[i].groundPrefab = ground;
            }
            finally { Object.DestroyImmediate(mark); }
        }
        catalog.sprays = sprays.ToArray();
        catalog.sweepDecals = decals.Where((_,i)=>i%3!=2).ToArray();
        catalog.thrustDecals = decals.Where((_,i)=>i%3!=0).ToArray();
        catalog.downwardDecals = decals.Where((_,i)=>i%3!=1).ToArray();
        catalog.lethalDecals = decals.ToArray(); catalog.trailDecals = decals.Take(3).ToArray();
        catalog.sprayProfileShader = null; catalog.groundProfileShader = null;
        // This pack supplies world blood; screen wounds use the existing four B treatments.
        var screen = AssetDatabase.LoadAssetAtPath<BloodEffectsPackCatalog>(BloodEffectsPackBuilder.CatalogPath);
        catalog.screenSprite = screen.screenSprite; catalog.screenMaterials = screen.screenMaterials;
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
        OverburstGameMenuBuilder.UpgradeSettingsPresentation();
        return "C ready: 17 impact forms, 2 drip forms and 17 ground definitions";
    }
    // Classified from the authored forms: ribbons/fans for sweeps, narrow jets for thrusts,
    // upward and radial splashes for downward impacts. Large radial forms emphasize critical hits, heavy attacks and kills; ordinary hits retain their attack family.
    static int ShapeMask(int i)
    {
        int sweep = 1 << (int)CombatImpactShape.Sweep, thrust = 1 << (int)CombatImpactShape.Thrust;
        int downward = 1 << (int)CombatImpactShape.Downward;
        if (i == 7 || i == 12) return downward;
        if (i == 3 || i == 6 || i == 13) return sweep | downward;
        if (i == 2 || i == 4 || i == 5 || i == 14) return thrust;
        if (i == 0 || i == 8 || i == 11) return sweep | thrust;
        return sweep;
    }
    static AnimationCurve CopyCurve(AnimationCurve c) => new AnimationCurve(c.keys) { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode };
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0,path.LastIndexOf('/')); Folder(parent);
        AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
    }
}
