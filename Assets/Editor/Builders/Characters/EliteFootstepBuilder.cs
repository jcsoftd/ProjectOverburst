using System;
using System.Collections.Generic;
using System.Linq;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;

// Local-only authoring tool. Supplier FBX clips and the combat/death Feel pool are untouched.
public static class EliteFootstepBuilder
{
    private const string ThemeRoot = "Assets/ProjectOverburst/Resources/Enemies/Themes";
    private const string FeelRoot = "Assets/ProjectOverburst/Resources/Feel";
    private static readonly string[] Ids = {
        "SpiderBrood_Rostrokarck", "VenomBrood_Kupolobrach_Tint_Orange",
        "PrimalHunt_Occisodonte", "CavernMutants_Ursacetus"
    };
    // Sampled from the four project-owned walk clips' foot/claw bone curves.
    private static readonly float[][] Contacts = {
        new[] { 0f, .25f, .50f, .75f },
        new[] { .18f, .40f, .60f, .80f },
        new[] { .13f, .63f },
        new[] { .47f, .90f }
    };

    [MenuItem("OVERBURST/Enemies/Elites/Build Footstep Feel")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit mode required");
        if (!AssetDatabase.IsValidFolder(ThemeRoot + "/Footfalls"))
            AssetDatabase.CreateFolder(ThemeRoot, "Footfalls");

        for (int i = 0; i < Ids.Length; i++)
        {
            string id = Ids[i];
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(ThemeRoot + "/Definitions/" + id + ".asset");
            if (definition == null || definition.ActorPrefab == null || definition.AnimationProfile?.Walk == null)
                throw new InvalidOperationException("Missing elite: " + id);
            string profilePath = ThemeRoot + "/Footfalls/" + id + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<EnemyFootfallProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            profile.Configure(id, definition.AnimationProfile.Walk, Contacts[i]);
            EditorUtility.SetDirty(profile);

            string prefabPath = AssetDatabase.GetAssetPath(definition.ActorPrefab);
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var actor = root.GetComponent<EnemyActor>();
                if (actor == null) throw new InvalidOperationException("Missing actor: " + id);
                var emitter = root.GetComponent<EnemyEliteFootstepEmitter>()
                    ?? root.AddComponent<EnemyEliteFootstepEmitter>();
                emitter.Configure(actor, profile);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        BuildFeelPool();
        AssetDatabase.SaveAssets();
        Debug.Log("[EliteFootstepBuilder] 4 elite footfall profiles and 3 Feel slots saved.");
    }

    private static void BuildFeelPool()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(FeelRoot + "/MAT_ContactDetails.mat");
        var ground = AssetDatabase.LoadAssetAtPath<SurfaceProfile>(
            "Assets/ProjectOverburst/06_Audio/SurfaceProfiles/SF_Concrete.asset");
        if (material == null || ground == null) throw new InvalidOperationException("Missing project-owned footstep dependencies");
        var clipProperty = new SerializedObject(ground).FindProperty("landingClips");
        AudioClip[] clips = Enumerable.Range(0, clipProperty.arraySize)
            .Select(index => clipProperty.GetArrayElementAtIndex(index).objectReferenceValue as AudioClip)
            .Where(clip => clip != null).ToArray();
        if (clips.Length == 0) throw new InvalidOperationException("Missing concrete landing clips");

        var root = new GameObject("PF_EnemyEliteFootstepFeel", typeof(EnemyEliteFootstepFeel));
        try
        {
            var slots = new List<EnemyEliteFootstepFeel.Slot>();
            for (int i = 0; i < 3; i++)
            {
                var item = new GameObject("EliteStep_" + i.ToString("00"),
                    typeof(ParticleSystem), typeof(MMF_Player), typeof(AudioSource));
                item.transform.SetParent(root.transform, false);
                var dust = item.GetComponent<ParticleSystem>();
                dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = dust.main;
                main.loop = main.playOnAwake = false;
                main.useUnscaledTime = true;
                main.duration = .35f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .42f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.3f, .9f);
                main.startSize = .12f;
                main.startColor = new Color(.42f, .37f, .31f, .32f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 12;
                var emission = dust.emission; emission.enabled = false;
                var shape = dust.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
                shape.radius = .3f; shape.angle = 65f;
                var color = dust.colorOverLifetime; color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(.8f, 0f), new GradientAlphaKey(0f, 1f) });
                color.color = gradient;
                var renderer = dust.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

                var audio = item.GetComponent<AudioSource>();
                audio.playOnAwake = false; audio.spatialBlend = 1f;
                audio.minDistance = 2f; audio.maxDistance = 14f; audio.dopplerLevel = 0f;
                var player = item.GetComponent<MMF_Player>();
                player.AutoPlayOnEnable = player.AutoPlayOnStart = player.AutoInitialization = false;
                player.InitializationMode = MMFeedbacks.InitializationModes.Script;
                player.StopFeedbacksOnDisable = true;
                player.ForceTimescaleMode = true;
                player.ForcedTimescaleMode = TimescaleModes.Unscaled;
                player.FeedbacksList = new List<MMF_Feedback> {
                    new MMF_Particles {
                        Label = "Elite foot contact dust", BoundParticleSystem = dust,
                        Mode = MMF_Particles.Modes.Emit, EmitCount = 7,
                        MoveToPosition = false, StopSystemOnInit = true, StopSystemOnReset = true,
                        StopSystemOnStopFeedback = true, DeclaredDuration = .35f,
                        Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled }
                    },
                    new MMF_AudioSource {
                        Label = "Elite ground step", TargetAudioSource = audio,
                        RandomSfx = clips, MinPitch = .56f, MaxPitch = .67f,
                        MinVolume = .09f, MaxVolume = .15f
                    }
                };
                slots.Add(new EnemyEliteFootstepFeel.Slot { player = player, dust = dust, audio = audio });
            }
            root.GetComponent<EnemyEliteFootstepFeel>().Configure(slots.ToArray());
            PrefabUtility.SaveAsPrefabAsset(root, FeelRoot + "/PF_EnemyEliteFootstepFeel.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
