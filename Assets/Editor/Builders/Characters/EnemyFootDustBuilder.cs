using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Local-only authoring. Builds three shared effects and contact profiles without editing actor prefabs.
public static class EnemyFootDustBuilder
{
    private const string ThemeRoot = "Assets/ProjectOverburst/Resources/Enemies/Themes";
    private const string VfxRoot = "Assets/ProjectOverburst/03_Features/Enemies/VFX/FootDust";
    private const string PrefabRoot = "Assets/ProjectOverburst/Resources/Enemies/FootDust";
    private static readonly string[] EliteIds =
    {
        "SpiderBrood_Rostrokarck", "VenomBrood_Kupolobrach_Tint_Orange",
        "PrimalHunt_Occisodonte", "CavernMutants_Ursacetus"
    };
    private static readonly float[][] ElitePhases =
    {
        new[] { 0f, .25f, .50f, .75f },
        new[] { .18f, .40f, .60f, .80f },
        new[] { .13f, .63f },
        new[] { .47f, .90f }
    };

    [MenuItem("OVERBURST/Enemies/Foot Dust/Build Shared Effects And Contacts")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Foot dust authoring requires Edit mode.");
        EnsureFolder(VfxRoot);
        EnsureFolder(PrefabRoot);
        EnsureFolder(ThemeRoot + "/Footfalls");
        Material material = CreateMaterial();
        CreateParticlePrefabs(material);

        EnemyDefinition[] definitions = AssetDatabase.FindAssets("t:EnemyDefinition",
                new[] { ThemeRoot + "/Definitions" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(definition => definition != null && IsTheme(definition.EnemyId))
            .OrderBy(definition => definition.EnemyId).ToArray();
        if (definitions.Length != 21)
            throw new InvalidOperationException("Expected 21 theme definitions, found " + definitions.Length);

        var report = new List<string> { "id,weight,walkContacts,runContacts,walkSource,runSource" };
        foreach (EnemyDefinition definition in definitions)
        {
            if (definition.ActorPrefab == null || definition.AnimationProfile == null
                || definition.AnimationProfile.Walk == null || definition.AnimationProfile.Run == null)
                throw new InvalidOperationException("Missing actor/locomotion: " + definition.EnemyId);
            int elite = Array.IndexOf(EliteIds, definition.EnemyId);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(
                    definition.ActorPrefab.gameObject, preview);
                root.SetActive(false);
                EnemyActor actor = root.GetComponent<EnemyActor>();
                if (actor == null || actor.Animator == null)
                    throw new InvalidOperationException("Missing actor Animator: " + definition.EnemyId);
                AnimationClip walk = definition.AnimationProfile.Walk;
                AnimationClip run = definition.AnimationProfile.Run;
                string walkSource;
                EnemyFootfallContact[] walking = Analyze(actor, walk,
                    elite >= 0 ? ElitePhases[elite] : null, out walkSource);
                string runSource = walkSource;
                EnemyFootfallContact[] running = run == walk ? walking
                    : Analyze(actor, run, null, out runSource);

                EnemyHitWeightProfile hit = definition.MovementProfile != null
                    ? definition.MovementProfile.HitWeightProfile : null;
                EnemyHitWeight weight = hit != null ? hit.Weight
                    : elite >= 0 ? EnemyHitWeight.Heavy : EnemyHitWeight.Standard;
                string assetPath = ThemeRoot + "/Footfalls/" + definition.EnemyId + ".asset";
                EnemyFootfallProfile profile = AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(assetPath);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<EnemyFootfallProfile>();
                    AssetDatabase.CreateAsset(profile, assetPath);
                }
                profile.ConfigureDetailed(definition.EnemyId, walk, run, weight, walking, running);
                EditorUtility.SetDirty(profile);
                report.Add(definition.EnemyId + "," + weight + "," + walking.Length + ","
                    + running.Length + "," + walkSource + "," + runSource);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        AssetDatabase.SaveAssets();
        string reportPath = Path.GetFullPath(Path.Combine(Application.dataPath,
            "..", "..", "개인파일", "코덱스산출", "MonsterFootDust",
            "20260923_111748", "contact-profiles.csv"));
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllLines(reportPath, report);
        Debug.Log("[EnemyFootDustBuilder] Built 3 shared effects and 21 contact profiles. " + reportPath);
    }

    private static bool IsTheme(string id) => !string.IsNullOrEmpty(id) &&
        (id.StartsWith("SpiderBrood_", StringComparison.Ordinal)
        || id.StartsWith("VenomBrood_", StringComparison.Ordinal)
        || id.StartsWith("PrimalHunt_", StringComparison.Ordinal)
        || id.StartsWith("CavernMutants_", StringComparison.Ordinal));

    private static EnemyFootfallContact[] Analyze(EnemyActor actor, AnimationClip clip,
        float[] fixedPhases, out string source)
    {
        const int sampleCount = 120;
        clip.SampleAnimation(actor.Animator.gameObject, 0f);
        MonsterThemeStrideCalibration.ContactProbe[] probes =
            MonsterThemeStrideCalibration.CreateRuntimeProbes(actor);
        if (probes.Length == 0)
        {
            source = "ground-body-fallback";
            float[] phases = fixedPhases ?? new[] { 0f, .5f };
            return phases.Select(phase => new EnemyFootfallContact(phase, Vector3.zero)).ToArray();
        }

        var positions = new Vector3[probes.Length, sampleCount];
        var lowest = new float[probes.Length];
        for (int foot = 0; foot < probes.Length; foot++) lowest[foot] = float.PositiveInfinity;
        for (int frame = 0; frame < sampleCount; frame++)
        {
            clip.SampleAnimation(actor.Animator.gameObject, clip.length * frame / sampleCount);
            for (int foot = 0; foot < probes.Length; foot++)
            {
                Vector3 local = actor.transform.InverseTransformPoint(probes[foot].sample());
                positions[foot, frame] = local;
                if (local.y < lowest[foot]) lowest[foot] = local.y;
            }
        }

        if (fixedPhases != null)
        {
            source = "verified-elite-phase+sole";
            return fixedPhases.Select(phase =>
            {
                int frame = Mathf.Clamp(Mathf.RoundToInt(phase * sampleCount), 0, sampleCount - 1);
                int foot = LowestFoot(positions, lowest, frame);
                return new EnemyFootfallContact(phase, positions[foot, frame]);
            }).ToArray();
        }

        var candidates = new List<EnemyFootfallContact>(16);
        for (int foot = 0; foot < probes.Length; foot++)
        {
            for (int frame = 0; frame < sampleCount; frame++)
            {
                int before = (frame + sampleCount - 1) % sampleCount;
                bool low = positions[foot, frame].y <= lowest[foot] + .035f;
                bool wasLow = positions[foot, before].y <= lowest[foot] + .035f;
                if (low && !wasLow)
                    candidates.Add(new EnemyFootfallContact((float)frame / sampleCount,
                        positions[foot, frame]));
            }
        }
        candidates.Sort((a, b) => a.Phase.CompareTo(b.Phase));
        var grouped = new List<EnemyFootfallContact>(candidates.Count);
        foreach (EnemyFootfallContact contact in candidates)
        {
            if (grouped.Count == 0 || contact.Phase - grouped[grouped.Count - 1].Phase >= .085f)
                grouped.Add(contact);
        }
        if (grouped.Count > 1 && grouped[0].Phase + 1f - grouped[grouped.Count - 1].Phase < .085f)
            grouped.RemoveAt(grouped.Count - 1);
        if (grouped.Count > 6)
            grouped = Enumerable.Range(0, 6).Select(i => grouped[i * grouped.Count / 6]).ToList();
        if (grouped.Count >= 2)
        {
            source = "sampled-sole";
            return grouped.ToArray();
        }

        source = "phase-fallback+sole";
        int left = LowestFoot(positions, lowest, 0);
        int right = LowestFoot(positions, lowest, sampleCount / 2);
        return new[]
        {
            new EnemyFootfallContact(0f, positions[left, 0]),
            new EnemyFootfallContact(.5f, positions[right, sampleCount / 2])
        };
    }

    private static int LowestFoot(Vector3[,] positions, float[] lowest, int frame)
    {
        int best = 0;
        float score = float.PositiveInfinity;
        int previous = (frame + positions.GetLength(1) - 3) % positions.GetLength(1);
        for (int foot = 0; foot < lowest.Length; foot++)
        {
            float landed = Mathf.Max(0f, positions[foot, previous].y - positions[foot, frame].y);
            float current = positions[foot, frame].y - lowest[foot] - landed * 2f;
            if (current >= score) continue;
            best = foot;
            score = current;
        }
        return best;
    }

    private static Material CreateMaterial()
    {
        string texturePath = VfxRoot + "/T_EnemyFootDust.png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) == null)
        {
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, true);
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float u = (x + .5f) / 128f * 2f - 1f;
                float v = (y + .5f) / 128f * 2f - 1f;
                float center = Mathf.Exp(-(u * u * 2.2f + v * v * 2.8f));
                float lobeA = Mathf.Exp(-((u + .29f) * (u + .29f) * 8f + (v + .08f) * (v + .08f) * 5f));
                float lobeB = Mathf.Exp(-((u - .26f) * (u - .26f) * 7f + (v - .13f) * (v - .13f) * 6f));
                float edge = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                float noise = Mathf.PerlinNoise(x * .055f + 17f, y * .055f + 31f);
                float alpha = Mathf.Clamp01((center * .55f + lobeA * .28f + lobeB * .26f)
                    * edge * (.62f + noise * .55f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(true, false);
            File.WriteAllBytes(Path.Combine(Application.dataPath,
                texturePath.Substring("Assets/".Length)), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        string materialPath = VfxRoot + "/MAT_EnemyFootDust_Unlit.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material != null) return material;
        Material reference = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/ProjectOverburst/Resources/Feel/MAT_ContactDetails.mat");
        if (reference == null) throw new InvalidOperationException("Missing URP contact material.");
        material = new Material(reference) { name = "MAT_EnemyFootDust_Unlit" };
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        AssetDatabase.CreateAsset(material, materialPath);
        return material;
    }

    private static void CreateParticlePrefabs(Material material)
    {
        string[] names = { "PF_EnemyFootDust_Light", "PF_EnemyFootDust_Standard", "PF_EnemyFootDust_Heavy" };
        int[] capacities = { 128, 128, 160 };
        for (int i = 0; i < names.Length; i++)
        {
            string path = PrefabRoot + "/" + names[i] + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
            var root = new GameObject(names[i], typeof(ParticleSystem));
            try
            {
                ParticleSystem system = root.GetComponent<ParticleSystem>();
                var main = system.main;
                main.loop = true;
                main.playOnAwake = true;
                main.duration = 1f;
                main.startLifetime = .45f;
                main.startSpeed = 0f;
                main.startSize = .25f;
                main.startColor = Color.white;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.maxParticles = capacities[i];
                var emission = system.emission;
                emission.enabled = false;
                var shape = system.shape;
                shape.enabled = false;
                var color = system.colorOverLifetime;
                color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, .13f), new GradientAlphaKey(.8f, .48f),
                    new GradientAlphaKey(0f, 1f) });
                color.color = gradient;
                var size = system.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f,
                    new AnimationCurve(new Keyframe(0f, .55f), new Keyframe(.4f, 1f),
                        new Keyframe(1f, 1.35f)));
                ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
