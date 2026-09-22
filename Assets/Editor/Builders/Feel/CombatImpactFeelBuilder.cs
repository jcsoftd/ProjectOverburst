using System;
using System.Collections.Generic;
using System.Linq;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;

public static class CombatImpactFeelBuilder
{
    private const string Root = "Assets/ProjectOverburst/Resources/Feel";

    [MenuItem("OVERBURST/Codex/Migrate/Feel/Apply Contact And Death Presentation")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
        BuildImpactPool();
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { MonsterThemeCombatBuilder.Root }))
        {
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition.ActorPrefab == null) continue;
            string path = AssetDatabase.GetAssetPath(definition.ActorPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try { ConfigureActor(root.GetComponent<EnemyActor>(), definition); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        string[] names = { "OHS_Hit01", "OHS_Hit02", "OHS_Hit03", "GRS_Hit01", "GRS_Hit02", "GRS_Hit03" };
        float[] durations = { .025f, .03f, .05f, .035f, .04f, .055f };
        for (int i = 0; i < names.Length; i++)
        {
            string path = $"Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/Feedback/{names[i]}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<CombatHitFeedbackProfile>(path);
            if (profile == null) throw new InvalidOperationException(path);
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("hitStopDuration").floatValue = durations[i];
            serialized.FindProperty("criticalStrengthMultiplier").floatValue = 1.35f;
            serialized.FindProperty("maximumHitStopDuration").floatValue = .075f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        AssetDatabase.SaveAssets();
    }

    public static void ConfigureActor(EnemyActor actor, EnemyDefinition definition)
    {
        var visual = actor.VisualRoot.Find("Authored model scale");
        if (visual == null || definition.MovementProfile.HitWeightProfile == null) throw new InvalidOperationException(actor.name);
        var presentation = actor.GetComponent<EnemyDeathPresentation>() ?? actor.gameObject.AddComponent<EnemyDeathPresentation>();
        Transform child = actor.transform.Find("Death Feel");
        if (child == null) { child = new GameObject("Death Feel").transform; child.SetParent(actor.transform, false); }
        var player = child.GetComponent<MMF_Player>() ?? child.gameObject.AddComponent<MMF_Player>();
        ConfigurePlayer(player, false);
        player.FeedbacksList = new List<MMF_Feedback> { new MMF_Position {
            Label = "Death recoil - scaled with the authored death animation",
            AnimatePositionTarget = visual.gameObject, Mode = MMF_Position.Modes.AlongCurve,
            Space = MMF_Position.Spaces.Local, RelativePosition = true, DeterminePositionsOnPlay = true,
            AnimateX = true, AnimateY = true, AnimateZ = true, RemapCurveZero = 0, RemapCurveOne = 1,
            Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Scaled }
        }};
        string id = definition.EnemyId;
        var surface = id.StartsWith("SpiderBrood_") ? CombatImpactSurface.Shell
            : id.StartsWith("VenomBrood_") ? CombatImpactSurface.Venom : CombatImpactSurface.Flesh;
        var weight = definition.MovementProfile.HitWeightProfile.Weight;
        bool light = weight == EnemyHitWeight.Light, heavy = weight == EnemyHitWeight.Heavy;
        // Reviewed from each authored death clip's body-to-ground contact, not its midpoint.
        float landing = id == "SpiderBrood_Rostrokarck" ? .84f
            : id == "VenomBrood_Kupolobrach_Tint_Orange" ? .86f
            : id == "PrimalHunt_Occisodonte" ? .90f : .5f;
        presentation.Configure(surface, visual, player, light ? .2f : heavy ? .025f : .08f,
            light ? .36f : heavy ? .04f : .14f, light ? .055f : 0, light ? .32f : .24f, landing);
    }

    private static void ConfigurePlayer(MMF_Player player, bool unscaled)
    {
        player.AutoPlayOnEnable = player.AutoPlayOnStart = player.AutoInitialization = false;
        player.InitializationMode = MMFeedbacks.InitializationModes.Script;
        player.StopFeedbacksOnDisable = true;
        player.ForceTimescaleMode = true;
        player.ForcedTimescaleMode = unscaled ? TimescaleModes.Unscaled : TimescaleModes.Scaled;
    }

    private static void BuildImpactPool()
    {
        var root = new GameObject("PF_CombatImpactFeel", typeof(CombatImpactFeel));
        try
        {
            var material = EnsureMaterial();
            var slots = new List<CombatImpactFeel.Slot>();
            var ground = AssetDatabase.LoadAssetAtPath<SurfaceProfile>("Assets/ProjectOverburst/06_Audio/SurfaceProfiles/SF_Concrete.asset");
            var clipProperty = new SerializedObject(ground).FindProperty("landingClips");
            var clips = Enumerable.Range(0, clipProperty.arraySize).Select(i => clipProperty.GetArrayElementAtIndex(i).objectReferenceValue as AudioClip).Where(c => c != null).ToArray();
            if (clips.Length == 0) throw new InvalidOperationException("Landing clips missing");
            foreach (CombatImpactSurface surface in Enum.GetValues(typeof(CombatImpactSurface)))
            for (int i = 0; i < (surface == CombatImpactSurface.Ground ? 4 : 16); i++)
            {
                bool landing = surface == CombatImpactSurface.Ground;
                var item = new GameObject($"{surface}_{i:00}", typeof(ParticleSystem), typeof(MMF_Player));
                item.transform.SetParent(root.transform, false);
                var particles = item.GetComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.loop = main.playOnAwake = false; main.useUnscaledTime = true;
                main.duration = landing ? .65f : .28f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(landing ? .3f : .09f, landing ? .6f : .24f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(landing ? .5f : 1.2f, landing ? 1.1f : 2.8f);
                main.startSize = landing ? .22f : .055f;
                main.startColor = surface == CombatImpactSurface.Shell ? new Color(.75f,.63f,.39f)
                    : surface == CombatImpactSurface.Venom ? new Color(.55f,.68f,.16f)
                    : landing ? new Color(.42f,.37f,.31f,.5f) : new Color(.68f,.2f,.12f);
                main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = landing ? 20 : 12;
                main.gravityModifier = landing ? .02f : .3f;
                var emission = particles.emission; emission.enabled = false;
                var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
                shape.radius = landing ? .55f : .025f; shape.angle = landing ? 78 : 34;
                var color = particles.colorOverLifetime; color.enabled = true;
                var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1) },
                    new[] { new GradientAlphaKey(1,0),new GradientAlphaKey(0,1) }); color.color = gradient;
                var size = particles.sizeOverLifetime; size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0));
                var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
                renderer.renderMode = landing ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 1.5f; renderer.velocityScale = .04f;
                var player = item.GetComponent<MMF_Player>(); ConfigurePlayer(player, true);
                player.FeedbacksList = new List<MMF_Feedback> { new MMF_Particles {
                    Label = landing ? "Corpse ground contact dust" : "Directional surface fragments",
                    BoundParticleSystem = particles, Mode = MMF_Particles.Modes.Emit, EmitCount = landing ? 18 : 8,
                    MoveToPosition = false, StopSystemOnInit = true, StopSystemOnReset = true, StopSystemOnStopFeedback = true,
                    DeclaredDuration = landing ? .65f : .28f,
                    Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled }
                }};
                AudioSource audio = null;
                ParticleSystem flash = null;
                if (!landing)
                {
                    var flashObject = new GameObject("Critical contact flash", typeof(ParticleSystem));
                    flashObject.transform.SetParent(item.transform, false);
                    flash = flashObject.GetComponent<ParticleSystem>();
                    flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var flashMain = flash.main; flashMain.loop = flashMain.playOnAwake = false;
                    flashMain.useUnscaledTime = true; flashMain.startLifetime = .085f; flashMain.startSpeed = 0;
                    flashMain.startSize3D = true; flashMain.startSizeX = .42f; flashMain.startSizeY = .13f; flashMain.startSizeZ = .13f;
                    flashMain.startColor = new Color(1f,.91f,.67f); flashMain.maxParticles = 1;
                    var flashEmission = flash.emission; flashEmission.enabled = false;
                    var flashShape = flash.shape; flashShape.enabled = false;
                    var flashColor = flash.colorOverLifetime; flashColor.enabled = true; flashColor.color = gradient;
                    flash.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                    player.FeedbacksList.Add(new MMF_Particles { Label = "Critical only - contact flash", BoundParticleSystem = flash,
                        Mode = MMF_Particles.Modes.Emit, EmitCount = 1, MoveToPosition = false, StopSystemOnInit = true,
                        StopSystemOnReset = true, StopSystemOnStopFeedback = true, DeclaredDuration = .085f,
                        Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled } });
                }
                if (landing)
                {
                    audio = item.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 1;
                    audio.minDistance = 6; audio.maxDistance = 40; audio.dopplerLevel = 0; audio.clip = clips[0];
                    player.FeedbacksList.Add(new MMF_AudioSource { Label = "Heavy body contact", TargetAudioSource = audio,
                        RandomSfx = clips, MinPitch = .64f, MaxPitch = .72f, MinVolume = .22f, MaxVolume = .28f });
                }
                slots.Add(new CombatImpactFeel.Slot { surface = surface, player = player, particles = particles, criticalFlash = flash, audio = audio });
            }
            root.GetComponent<CombatImpactFeel>().Configure(slots.ToArray());
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/PF_CombatImpactFeel.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Material EnsureMaterial()
    {
        string path = Root + "/MAT_ContactDetails.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var texture = new Texture2D(32,32,TextureFormat.RGBA32,false) { name = "ContactSoftDot", wrapMode = TextureWrapMode.Clamp };
        for (int y=0;y<32;y++) for(int x=0;x<32;x++)
        {
            float d = new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;
            texture.SetPixel(x,y,new Color(1,1,1,Mathf.SmoothStep(1,0,Mathf.Clamp01(d))));
        }
        texture.Apply(); AssetDatabase.CreateAsset(texture, Root + "/ContactSoftDot.asset");
        material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name="ContactDetails", renderQueue=3000 };
        material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor",Color.white);
        material.SetFloat("_Surface",1); material.SetFloat("_Blend",0); material.SetFloat("_ZWrite",0);
        material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        AssetDatabase.CreateAsset(material,path); return material;
    }
}
