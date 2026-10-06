using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Creates owned data and connects the boss prefab; leaves supplier and regular-enemy effects untouched.
public static class CrustaspikanCombatPresentationBuilder
{
    private const string MaterialsRoot = "Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanMaterials";
    private const string PresentationRoot = MaterialsRoot + "/Presentation";
    private const string FootfallPath = "Assets/ProjectOverburst/Resources/Enemies/Themes/Footfalls/CrustaspikanMaterials.asset";
    private const string PrefabPath = MaterialsRoot + "/PF_CrustaspikanMaterials.prefab";

    [MenuItem("OVERBURST/Monsters/Crustaspikan/Build Combat Presentation")]
    public static void MenuBuild() => Build(Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../개인파일/코덱스산출/Boss/20261006_CrustaspikanWeightAndParryCue")));

    public static string Build(string output)
    {
        Idle(); Directory.CreateDirectory(output);
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(MaterialsRoot + "/BMC_Crustaspikan.asset");
        var definition = collection.actorDefinition;
        var settings = AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(
            "Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan.asset");
        if (!AssetDatabase.IsValidFolder(PresentationRoot)) AssetDatabase.CreateFolder(MaterialsRoot, "Presentation");
        var contacts = AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(FootfallPath);
        if (contacts == null)
        {
            contacts = MeasureToeContacts(definition, output);
            var data = new SerializedObject(contacts); data.FindProperty("groundStepTier").enumValueIndex = (int)EnemyGroundStepTier.Elite;
            data.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(contacts);
        }
        if (contacts == null || !contacts.IsValid || contacts.EnemyId != definition.EnemyId || contacts.LocomotionClip != definition.AnimationProfile.Walk)
            throw new InvalidOperationException("The boss needs its own measured footfall profile.");
        var dust = Dust();
        string profilePath = PresentationRoot + "/CP_Crustaspikan.asset";
        var profile = AssetDatabase.LoadAssetAtPath<CrustaspikanCombatPresentationProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<CrustaspikanCombatPresentationProfile>();
            profile.footsteps = new[] { "01e53c3f9df5af74e94030f336e5908d", "47b43bcb8f276624aa937d7406945c4b",
                "7344d419bbeecb0468a15407736f264e", "4910d625f4c9c0243b9fdea92c00cc97" }
                .Select(guid => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
            profile.groundImpact = settings.entrance.impactClip; profile.rumble = settings.entrance.rumbleClip; profile.dustPrefab = dust;
            profile.groundStrikes = collection.attacks.Where(a => a.delivery == EnemyBossMaterialDelivery.Melee
                && (a.runtimeClip.name.Contains("Smash") || a.runtimeClip.name.Contains("Stomp") || a.runtimeClip.name.Contains("Combo")))
                .SelectMany(a => Enumerable.Range(0, a.strikes.Length).Select(phase => new CrustaspikanCombatPresentationProfile.GroundStrike {
                    materialId = a.materialId, phase = phase, contactBones = ContactBones(a.runtimeClip.name, phase),
                    strength = a.runtimeClip.name.Contains("2Hands") ? 2.3f : a.runtimeClip.name.Contains("Smash") ? 1.8f : 1.4f
                })).ToArray();
            if (profile.footsteps.Any(c => c == null) || profile.groundImpact == null || profile.rumble == null)
                throw new InvalidOperationException("Required existing concrete/slam/bass clips are missing.");
            AssetDatabase.CreateAsset(profile, profilePath); AssetDatabase.SaveAssetIfDirty(profile);
        }
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var actor = prefab.GetComponent<EnemyActor>();
            var emitter = prefab.GetComponent<EnemyEliteFootstepEmitter>();
            emitter.Configure(actor, contacts);
            var data = new SerializedObject(emitter);
            data.FindProperty("customFeedbackDistance").floatValue = profile.audibleDistance;
            data.ApplyModifiedPropertiesWithoutUndo();
            var presentation = prefab.GetComponent<CrustaspikanCombatPresentation>() ?? prefab.AddComponent<CrustaspikanCombatPresentation>();
            presentation.Configure(profile);
            var head = prefab.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Crustaspikan_ Head");
            var anchor = prefab.GetComponent<EnemyParryCueAnchor>() ?? prefab.AddComponent<EnemyParryCueAnchor>();
            anchor.Configure(head, .85f, 3.2f);
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) != 0) throw new InvalidOperationException("Missing boss script.");
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (saved.GetComponent<EnemyEliteFootstepEmitter>().Profile != contacts || saved.GetComponent<CrustaspikanCombatPresentation>().Profile != profile
            || saved.GetComponent<EnemyParryCueAnchor>().Head == null) throw new InvalidOperationException("Saved connections differ.");
        File.WriteAllText(Path.Combine(output, "build-result.json"), JsonConvert.SerializeObject(new { status = "APPLIED_NATIVE_PENDING_PLAY",
            prefab = PrefabPath, footfalls = FootfallPath, contacts = contacts.ContactCount, profile = profilePath,
            groundStrikes = profile.groundStrikes.Length, dust = AssetDatabase.GetAssetPath(dust),
            footstepSounds = profile.footsteps.Select(AssetDatabase.GetAssetPath).ToArray(),
            impact = AssetDatabase.GetAssetPath(profile.groundImpact), rumble = AssetDatabase.GetAssetPath(profile.rumble),
            pid = System.Diagnostics.Process.GetCurrentProcess().Id, utc = DateTime.UtcNow }, Formatting.Indented));
        return "Boss ground contacts and head cue saved";
    }
    private static string[] ContactBones(string clip, int phase)
    {
        if (clip.Contains("2Hands")) return new[] { "Crustaspikan_ L Finger01", "Crustaspikan_ R Finger01" };
        if (clip.Contains("Combo")) return new[] { phase == 0 ? "Crustaspikan_ R Finger01" : "Crustaspikan_ L Finger01" };
        if (clip.Contains("Foot")) return new[] { clip.Contains("Left") ? "Crustaspikan_ L Toe0" : "Crustaspikan_ R Toe0" };
        return new[] { clip.Contains("Left") ? "Crustaspikan_ L Finger01" : "Crustaspikan_ R Finger01" };
    }
    private static EnemyFootfallProfile MeasureToeContacts(EnemyDefinition definition, string output)
    {
        var clip = definition.AnimationProfile.Walk;
        if (definition.AnimationProfile.Run != clip) throw new InvalidOperationException("A separate run gait needs separate measurement.");
        var scene = EditorSceneManager.NewPreviewScene(); var graph = default(PlayableGraph);
        try
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(definition.ActorPrefab.gameObject, scene);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var animator = root.GetComponent<EnemyActor>().Animator;
            animator.enabled = true; animator.fireEvents = false; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var toes = new[] { "Crustaspikan_ L Toe0", "Crustaspikan_ R Toe0" }
                .Select(name => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name)).ToArray();
            var localPosition = animator.transform.localPosition; var localRotation = animator.transform.localRotation; var localScale = animator.transform.localScale;
            graph = PlayableGraph.Create("Crustaspikan measured toe contacts"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
            var channel = AnimationPlayableOutput.Create(graph, "Native gait", animator); channel.SetSourcePlayable(playable); graph.Play();
            const int count = 240; var frames = new Vector3[count, 2];
            for (int frame = 0; frame < count; frame++)
            {
                playable.SetTime(clip.length * frame / count); graph.Evaluate(0);
                animator.transform.localPosition = localPosition; animator.transform.localRotation = localRotation; animator.transform.localScale = localScale;
                for (int foot = 0; foot < 2; foot++) frames[frame, foot] = root.transform.InverseTransformPoint(toes[foot].position);
            }
            var contacts = new EnemyFootfallContact[2]; var evidence = new object[2];
            for (int foot = 0; foot < 2; foot++)
            {
                float low = float.PositiveInfinity, high = float.NegativeInfinity; int liftedAt = 0;
                for (int frame = 0; frame < count; frame++)
                { low = Mathf.Min(low, frames[frame, foot].y); if (frames[frame, foot].y > high) { high = frames[frame, foot].y; liftedAt = frame; } }
                if (high - low < .08f) throw new InvalidOperationException("Native gait did not move the toe: " + toes[foot].name);
                float threshold = low + Mathf.Max(.035f, (high - low) * .08f); int contact = -1;
                // Start at maximum lift, then take the first descending landing. Ignore planted toe-roll recrossings.
                for (int offset = 1; offset < count; offset++)
                {
                    int frame = (liftedAt + offset) % count, previous = (frame + count - 1) % count;
                    if (frames[frame, foot].y <= threshold && frames[previous, foot].y > threshold) { contact = frame; break; }
                }
                if (contact < 0) throw new InvalidOperationException("No measured landing for " + toes[foot].name);
                float phase = (float)contact / count; Vector3 position = frames[contact, foot];
                contacts[foot] = new EnemyFootfallContact(phase, position);
                evidence[foot] = new { bone = toes[foot].name, minimumY = low, maximumY = high, phase, sourceSeconds = phase * clip.length,
                    actorLocalPosition = new[] { position.x, position.y, position.z } };
            }
            contacts = contacts.OrderBy(c => c.Phase).ToArray();
            var profile = ScriptableObject.CreateInstance<EnemyFootfallProfile>();
            profile.ConfigureDetailed(definition.EnemyId, clip, clip, EnemyHitWeight.Heavy, contacts, contacts);
            if (!profile.IsValid) { UnityEngine.Object.DestroyImmediate(profile); throw new InvalidOperationException("Measured contacts are invalid."); }
            AssetDatabase.CreateAsset(profile, FootfallPath); AssetDatabase.SaveAssetIfDirty(profile);
            File.WriteAllText(Path.Combine(output, "footfall-measurement.json"), JsonConvert.SerializeObject(new { status = "NATIVE_MEASURED",
                definition = definition.EnemyId, clip = AssetDatabase.GetAssetPath(clip), clip.length, clip.frameRate, samplesPerCycle = count,
                criterion = "first descending low-band crossing after maximum toe lift", contacts = evidence }, Formatting.Indented));
            return profile;
        }
        finally { if (graph.IsValid()) graph.Destroy(); EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static GameObject Dust()
    {
        string path = PresentationRoot + "/PF_CrustaspikanGroundDust.prefab";
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (saved != null) return saved;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/Resources/Enemies/FootDust/PF_EnemyFootDust_Heavy.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "PF_CrustaspikanGroundDust";
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main; main.loop = false; main.playOnAwake = false; main.duration = .65f;
                main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 64;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.48f, .78f);
                main.startSize = new ParticleSystem.MinMaxCurve(.75f, 1.55f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.5f);
                main.startColor = new Color(.6f, .56f, .49f, .7f);
                var emission = system.emission; emission.enabled = true; emission.rateOverTime = 0; emission.rateOverDistance = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)16) });
                var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .5f; shape.rotation = new Vector3(-90f, 0f, 0f);
                var collision = system.collision; collision.enabled = false;
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            saved = PrefabUtility.SaveAsPrefabAsset(root, path); return saved;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Idle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))) throw new InvalidOperationException("Idle real-account Editor required.");
    }
}
