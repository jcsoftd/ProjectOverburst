using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class OverburstGoalEFeelMigration
{
    private const string RootFolder = "Assets/ProjectOverburst/Resources/Feel";
    private const string PrefabPath = RootFolder + "/PF_OverburstFeelHub.prefab";
    private const string MaterialPath = RootFolder + "/MAT_OverburstFeelParticles.mat";

    [MenuItem("OVERBURST/Codex/Migrate/Feel/Apply GOAL E Feel Presentation")]
    public static void ApplyFromMenu()
    {
        if (IsCurrentPrefabValid())
        {
            Debug.Log($"[OverburstGoalEFeelMigration] NO_CHANGE prefab={PrefabPath} emitters=23 Feel=5.9.1");
            return;
        }

        string before = File.Exists(PrefabPath) ? HashFile(PrefabPath) : string.Empty;
        EnsureFolder("Assets/ProjectOverburst/Resources");
        EnsureFolder(RootFolder);
        Material material = EnsureParticleMaterial();
        BuildPrefab(material);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        string after = HashFile(PrefabPath);
        Debug.Log($"[OverburstGoalEFeelMigration] {(before == after ? "NO_CHANGE" : "APPLIED")}"
            + $" prefab={PrefabPath} emitters=23 Feel=5.9.1");
    }

    private static bool IsCurrentPrefabValid()
    {
        if (!File.Exists(PrefabPath))
            return false;
        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            OverburstFeelFeedbackHub hub = prefab.GetComponent<OverburstFeelFeedbackHub>();
            OverburstFeelEmitter[] emitters = prefab.GetComponentsInChildren<OverburstFeelEmitter>(true);
            if (hub == null || emitters.Length != 23)
                return false;

            int[] expected = { 8, 6, 4, 2, 2, 1 };
            int[] actual = new int[expected.Length];
            for (int i = 0; i < emitters.Length; i++)
            {
                OverburstFeelEmitter emitter = emitters[i];
                if (emitter == null || emitter.Player == null || emitter.Player.FeedbacksList == null
                    || emitter.Player.FeedbacksList.Count != 1)
                    return false;
                actual[(int)emitter.Cue]++;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                    return false;
            }
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    private static void BuildPrefab(Material material)
    {
        GameObject root = new GameObject("PF_OverburstFeelHub", typeof(OverburstFeelFeedbackHub));
        try
        {
            List<OverburstFeelEmitter> emitters = new List<OverburstFeelEmitter>(23);
            AddWorldPool(root.transform, emitters, OverburstFeelCue.WeakHit, 8, material,
                new Color(1f, 0.73f, 0.24f, 1f), 6, 0.18f, 0.19f, 1.7f, 0.065f, 0.10f);
            AddWorldPool(root.transform, emitters, OverburstFeelCue.StrongHit, 6, material,
                new Color(1f, 0.35f, 0.08f, 1f), 12, 0.25f, 0.28f, 2.8f, 0.11f, 0.14f);
            AddWorldPool(root.transform, emitters, OverburstFeelCue.Death, 4, material,
                new Color(0.92f, 0.12f, 0.05f, 1f), 18, 0.42f, 0.46f, 3.2f, 0.15f, 0.22f);
            AddWorldPool(root.transform, emitters, OverburstFeelCue.Evade, 2, material,
                new Color(0.22f, 0.86f, 1f, 0.9f), 10, 0.26f, 0.28f, 1.5f, 0.075f, 0.35f);
            AddWorldPool(root.transform, emitters, OverburstFeelCue.Interaction, 2, material,
                new Color(1f, 0.86f, 0.25f, 0.9f), 7, 0.28f, 0.30f, 0.9f, 0.07f, 0.18f);
            emitters.Add(CreateUiEmitter(root.transform));

            root.GetComponent<OverburstFeelFeedbackHub>().Configure(emitters.ToArray());
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved == null)
                throw new InvalidOperationException("Failed to save GOAL E Feel hub prefab.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void AddWorldPool(
        Transform parent,
        List<OverburstFeelEmitter> emitters,
        OverburstFeelCue cue,
        int poolSize,
        Material material,
        Color color,
        int particleCount,
        float duration,
        float lifetime,
        float speed,
        float size,
        float radius)
    {
        GameObject poolRoot = new GameObject(cue + "Pool");
        poolRoot.transform.SetParent(parent, false);
        for (int i = 0; i < poolSize; i++)
        {
            GameObject slot = new GameObject($"{cue}_{i + 1:00}",
                typeof(ParticleSystem), typeof(MMF_Player), typeof(OverburstFeelEmitter));
            slot.transform.SetParent(poolRoot.transform, false);
            ParticleSystem particles = slot.GetComponent<ParticleSystem>();
            ConfigureParticles(particles, material, color, particleCount, duration, lifetime, speed, size, radius);

            MMF_Player player = ConfigurePlayer(slot.GetComponent<MMF_Player>());
            MMF_Particles feedback = new MMF_Particles
            {
                Label = cue + " pooled particles",
                BoundParticleSystem = particles,
                Mode = MMF_Particles.Modes.Emit,
                EmitCount = particleCount,
                MoveToPosition = true,
                StopSystemOnInit = true,
                StopSystemOnReset = true,
                StopSystemOnStopFeedback = true,
                DeclaredDuration = duration,
                Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled }
            };
            player.FeedbacksList = new List<MMF_Feedback> { feedback };

            OverburstFeelEmitter emitter = slot.GetComponent<OverburstFeelEmitter>();
            emitter.Configure(cue, player, particles, null);
            emitters.Add(emitter);
        }
    }

    private static OverburstFeelEmitter CreateUiEmitter(Transform parent)
    {
        GameObject canvasObject = new GameObject("UiConfirm_01", typeof(RectTransform), typeof(Canvas),
            typeof(MMF_Player), typeof(OverburstFeelEmitter), typeof(OverburstFeelUiBridge));
        canvasObject.transform.SetParent(parent, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 5000;

        GameObject imageObject = new GameObject("ConfirmFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = imageObject.GetComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.clear;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.25f, 0.82f, 1f), 0f),
                new GradientColorKey(new Color(0.25f, 0.82f, 1f), 0.25f),
                new GradientColorKey(new Color(0.08f, 0.22f, 0.35f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.10f, 0.18f),
                new GradientAlphaKey(0f, 1f)
            });

        MMF_Player player = ConfigurePlayer(canvasObject.GetComponent<MMF_Player>());
        MMF_Image feedback = new MMF_Image
        {
            Label = "UI confirm flash",
            BoundImage = image,
            Mode = MMF_Image.Modes.OverTime,
            Duration = 0.18f,
            ModifyColor = true,
            ColorOverTime = gradient,
            AllowAdditivePlays = false,
            EnableOnPlay = true,
            DisableOnInit = false,
            DisableOnSequenceEnd = false,
            DisableOnStop = false,
            Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled }
        };
        player.FeedbacksList = new List<MMF_Feedback> { feedback };

        OverburstFeelEmitter emitter = canvasObject.GetComponent<OverburstFeelEmitter>();
        emitter.Configure(OverburstFeelCue.UiConfirm, player, null, image);
        return emitter;
    }

    private static MMF_Player ConfigurePlayer(MMF_Player player)
    {
        player.AutoPlayOnEnable = false;
        player.AutoPlayOnStart = false;
        player.AutoInitialization = false;
        player.InitializationMode = MMFeedbacks.InitializationModes.Script;
        player.StopFeedbacksOnDisable = true;
        player.RestoreInitialValuesOnDisable = true;
        player.ForceTimescaleMode = true;
        player.ForcedTimescaleMode = TimescaleModes.Unscaled;
        return player;
    }

    private static void ConfigureParticles(
        ParticleSystem particles,
        Material material,
        Color color,
        int count,
        float duration,
        float lifetime,
        float speed,
        float size,
        float radius)
    {
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.1f, duration);
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(16, count * 2);
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f)));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 0.15f;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static Material EnsureParticleMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            throw new InvalidOperationException("URP Particles/Unlit shader was not found.");
        material = new Material(shader) { name = "MAT_OverburstFeelParticles", renderQueue = 3000 };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 1f);
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Invalid asset folder: " + path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static string HashFile(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    }
}
