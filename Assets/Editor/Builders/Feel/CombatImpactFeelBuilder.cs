using System;
using System.Collections.Generic;
using System.Linq;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;

public static class CombatImpactFeelBuilder
{
    private const string Root = "Assets/ProjectOverburst/Resources/Feel";

    // 2026-10-01: 일반 재적용은 없는 것만 만든다. 이미 있는 공용 풀 프리팹과 액터별 사망 연출(밀림·높이·시간·착지 시점·Feel 목록)은
    // 조정값의 원본이라 다시 쓰지 않는다. 치명타 카메라 배율은 옛 기본값 1.2에 머문 프로필만 1.35로 옮긴다(1회성 이관).
    [MenuItem("OVERBURST/Codex/Migrate/Feel/Apply Contact And Death Presentation (Missing Only)")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/PF_CombatImpactFeel.prefab") == null) BuildImpactPool();
        int configured = 0, preserved = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { MonsterThemeCombatBuilder.Root }))
        {
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition.ActorPrefab == null) continue;
            string path = AssetDatabase.GetAssetPath(definition.ActorPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (ConfigureActor(root.GetComponent<EnemyActor>(), definition)) { PrefabUtility.SaveAsPrefabAsset(root, path); configured++; }
                else preserved++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        string[] names = { "OHS_Hit01", "OHS_Hit02", "OHS_Hit03", "GRS_Hit01", "GRS_Hit02", "GRS_Hit03" };
        // 일반 타격 히트스탑은 삭제됐다. 치명타 카메라 충격 배율만 맞춘다.
        var migrated = new List<CombatHitFeedbackProfile>();
        for (int i = 0; i < names.Length; i++)
        {
            string path = $"Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/Feedback/{names[i]}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<CombatHitFeedbackProfile>(path);
            if (profile == null) { Debug.Log("[CombatImpactFeel] skipped missing (retired) feedback profile: " + path); continue; }
            var serialized = new SerializedObject(profile);
            var multiplier = serialized.FindProperty("criticalStrengthMultiplier");
            if (!Mathf.Approximately(multiplier.floatValue, 1.2f)) continue;
            multiplier.floatValue = 1.35f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            migrated.Add(profile);
        }
        foreach (var profile in migrated) AssetDatabase.SaveAssetIfDirty(profile);
        Debug.Log("[CombatImpactFeel] death presentation configured=" + configured + " preserved=" + preserved
            + "; critical multiplier migrated=" + migrated.Count);
    }

    // 사망 연출이 없거나 연결이 끊긴 액터만 설정하고 true를 돌려준다. 이미 연결된 액터의 조정값은 보존하고 false.
    public static bool ConfigureActor(EnemyActor actor, EnemyDefinition definition)
    {
        var visual = actor.VisualRoot.Find("Authored model scale");
        if (visual == null || definition.MovementProfile.HitWeightProfile == null) throw new InvalidOperationException(actor.name);
        var existing = actor.GetComponent<EnemyDeathPresentation>();
        Transform existingChild = actor.transform.Find("Death Feel");
        var existingFeel = existingChild != null ? existingChild.GetComponent<MMF_Player>() : null;
        if (existing != null && existingFeel != null)
        {
            var wiring = new SerializedObject(existing);
            if (wiring.FindProperty("visualRoot").objectReferenceValue == visual
                && wiring.FindProperty("feedback").objectReferenceValue == existingFeel
                && existingFeel.FeedbacksList != null && existingFeel.FeedbacksList.Count > 0)
                return false;
        }
        var presentation = existing ?? actor.gameObject.AddComponent<EnemyDeathPresentation>();
        Transform child = existingChild;
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
            : id == "PrimalHunt_Occisodonte" ? .90f : id == "CavernMutants_Ursacetus" ? .62f : .5f;
        // 2026-09-23 몬스터 품질 GOAL 값(일반/강한 처치 밀림, 강한 처치 높이, 이동 시간). 21종 프리팹과 동일.
        presentation.Configure(surface, visual, player, light ? .52f : heavy ? .10f : .27f,
            light ? .85f : heavy ? .20f : .46f, light ? .09f : heavy ? .025f : .055f,
            light ? .19f : heavy ? .26f : .23f, landing);
        return true;
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
