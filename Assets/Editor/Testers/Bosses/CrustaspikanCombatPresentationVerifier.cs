using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;


// 실제 포탈·이동·공격·패링 예고와 반복 진입으로 보스의 접촉 표현을 검사한다.
[InitializeOnLoad]
public static class CrustaspikanCombatPresentationVerifier
{
    private const string Key = "Overburst.CrustaspikanCombatPresentationVerifier.";
    private static readonly List<object> checks = new List<object>();
    private static readonly List<object> samples = new List<object>();
    private static PlayerActorRuntime player;
    private static CrustaspikanEncounter encounter;
    private static EnemyBossMaterialExecutor basic;
    private static string output;
    private static int stage, caseIndex, failures;
    private static bool running, impactCaptured, cueSeen;
    private static float stageAt, caseAt;
    private static InputSettings.BackgroundBehavior inputBefore;
    private static bool backgroundBefore;

    static CrustaspikanCombatPresentationVerifier()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += Changed;
    }

    public static string Start(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Editor must be idle.");
        var compilation = typeof(EditorUtility).GetProperty("scriptCompilationFailed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (compilation != null && (bool)compilation.GetValue(null)) throw new InvalidOperationException("Native compilation failed; Play verification not started.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Another isolated account owns the Editor.");
        output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetBool(Key + "pending", true);
        SessionState.SetString(Key + "input", InputSystem.settings.backgroundBehavior.ToString());
        SessionState.SetBool(Key + "background", Application.runInBackground);
        WriteFile("before.json", new { project = Application.dataPath, pid = System.Diagnostics.Process.GetCurrentProcess().Id,
            startScene = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene),
            scenes = Scenes(), input = InputSystem.settings.backgroundBehavior.ToString(), Application.runInBackground });
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedSave"));
        return "Combat presentation verification accepted";
    }

    private static object[] Scenes() => Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i => { var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return (object)new { s.name, s.path, s.isDirty, roots = s.rootCount }; }).ToArray();

    private static void Changed(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key + "pending", false)
            && !SessionState.GetBool(Key + "reloadLocked", false))
        {
            EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + "reloadLocked", true);
        }
        if (state == PlayModeStateChange.ExitingPlayMode) UnlockReload();
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + "pending", false)) return;
        output = SessionState.GetString(Key + "output", "");
        string resultPath = Path.Combine(output, "result.json");
        if (!File.Exists(resultPath))
            WriteFile("result.json", new { status = "NOT_RUN_PLAY_CANCELLED", error = "Play ended before the verifier could enter its runtime stages." });
        else if (running)
        {
            Write("FAIL_INTERRUPTED", "Play ended before verification completed."); WriteFile("samples.json", samples);
        }
        running = false;
        SessionState.SetBool(Key + "pending", false);
        SessionState.SetBool(Key + "returnPending", true);
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 120f);
    }

    private static void ReturnAccount()
    {
        if (!SessionState.GetBool(Key + "returnPending", false)) return;
        string path = SessionState.GetString(Key + "output", "");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0f))
            { SessionState.SetBool(Key + "returnPending", false); File.WriteAllText(Path.Combine(path, "editor-return.json"), "{\"status\":\"DEFERRED_EDITOR_BUSY\"}"); }
            return;
        }
        string owned = Path.GetFullPath(Path.Combine(path, "IsolatedSave"));
        string[] dirs = { Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.ActiveDirectory,
            SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") };
        if (dirs.Any(d => !string.IsNullOrEmpty(d) && !string.Equals(Path.GetFullPath(d), owned, StringComparison.OrdinalIgnoreCase)))
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0f))
            { SessionState.SetBool(Key + "returnPending", false); File.WriteAllText(Path.Combine(path, "editor-return.json"), "{\"status\":\"DEFERRED_OTHER_OWNER\"}"); }
            return;
        }
        try
        {
            IsolatedSavePlayGuard.UseRealAccount();
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)Enum.Parse(typeof(InputSettings.BackgroundBehavior), SessionState.GetString(Key + "input", "ResetAndDisableNonBackgroundDevices"));
            Application.runInBackground = SessionState.GetBool(Key + "background", true);
            SessionState.SetBool(Key + "returnPending", false);
            File.WriteAllText(Path.Combine(path, "editor-return.json"), JsonConvert.SerializeObject(new { status = "RETURNED",
                env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), active = IsolatedSavePlayGuard.ActiveDirectory,
                blocked = IsolatedSavePlayGuard.RequiresAccountChoice, prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""),
                expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), pending = false, returnPending = false,
                reloadLocked = SessionState.GetBool(Key + "reloadLocked", false),
                scenes = Scenes(), startScene = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene),
                input = InputSystem.settings.backgroundBehavior.ToString(), Application.runInBackground }, Formatting.Indented));
        }
        catch (Exception error) { SessionState.SetBool(Key + "returnPending", false); Debug.LogException(error); }
    }


    private static void Update()
    {
        ReturnAccount();
        if (!EditorApplication.isPlaying || !SessionState.GetBool(Key + "pending", false)) return;
        try
        {
            if (!running)
            {
                running = true; output = SessionState.GetString(Key + "output", ""); impactCaptured = false;
                checks.Clear(); samples.Clear(); failures = 0; stage = 0; stageAt = Time.realtimeSinceStartup; caseIndex = 0;
                capturedDust = 0; walkingCaptureAt = -1f; particlePeak = audioPeak = 0;
                inputBefore = (InputSettings.BackgroundBehavior)Enum.Parse(typeof(InputSettings.BackgroundBehavior),
                    SessionState.GetString(Key + "input", "ResetAndDisableNonBackgroundDevices"));
                backgroundBefore = SessionState.GetBool(Key + "background", true);
                Application.runInBackground = true;
            }
            if (Time.realtimeSinceStartup - stageAt > 90f) throw new TimeoutException("Combat presentation stage " + stage);
            Tick();
        }
        catch (Exception error) { Finish("FAIL", error.ToString()); }
    }

    private static CrustaspikanCombatPresentation presentation;
    private static int feetBefore, dustBefore, soundBefore, impactsBefore;
    private static int cues, capturedDust;
    private static int particlePeak, audioPeak;
    private static float walkingCaptureAt, impactCaptureAt;
    private static bool caseEntered, expectsParry;
    private static float greatestFacingDrift;
    private static Quaternion attackFacing;
    private static readonly string[] polishPatterns = { "combo", "advance_combo", "donut", "left_smash", "left_stomp" };
    private static void Tick()
    {
        if (stage == 0)
        {
            var host = CrustaspikanEncounterHost.Current;
            if (host == null || host.Entrance == null || !host.CanEnter) return;
            player = PlayerContext.Instance.CurrentActor;
            Teleport(host.Entrance.transform.position + Vector3.back);
            Check(host.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "actual boss portal entry");
            encounter = host.ActiveEncounter;
            Check(encounter != null && encounter.Brain != null, "actual boss and player"); Next(); return;
        }
        if (stage == 1)
        {
            if (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); return; }
            if (Time.realtimeSinceStartup - stageAt < 1f) return;
            player.Health.SetMaxHp(20000f, true); encounter.Brain.ReviewMode = true; encounter.enabled = false;
            var actor = encounter.Brain.Actor;
            presentation = actor.GetComponent<CrustaspikanCombatPresentation>();
            basic = actor.GetComponent<EnemyBossMaterialExecutor>();
            Check(presentation != null && presentation.Profile != null, "saved presentation is consumed by actual spawn");
            var emitter = actor.GetComponent<EnemyEliteFootstepEmitter>();
            Check(emitter.Profile.EnemyId == actor.Definition.EnemyId && emitter.UsesCustomFeedback, "boss's own gait and custom feedback are connected");
            Check(actor.GetComponent<EnemyParryCueAnchor>().Head != null, "actual animated head anchor");
            Teleport(actor.transform.position + Vector3.right * 4f + Vector3.back * 9f);
            feetBefore = presentation.FootContactCount; dustBefore = presentation.DustBurstCount; soundBefore = presentation.AudioEmissionCount;
            actor.Movement.SetDestination(encounter.ClampArena(actor.transform.position + Vector3.forward * 13f, 3f), .3f, EnemyLocomotionMode.Walk, 1f);
            Next(); return;
        }
        if (stage == 2)
        {
            MeasureLiveFeedback();
            if (presentation.DustBurstCount > dustBefore && walkingCaptureAt < 0f) walkingCaptureAt = Time.time + .12f;
            if (walkingCaptureAt > 0f && Time.time >= walkingCaptureAt && capturedDust == 0)
            { capturedDust++; Capture("walking.png"); }
            if (Time.realtimeSinceStartup - stageAt < 5.5f) return;
            Check(presentation.FootContactCount - feetBefore >= 2, "walking produces repeated measured foot contacts", presentation.FootContactCount - feetBefore);
            Check(presentation.DustBurstCount > dustBefore && presentation.AudioEmissionCount > soundBefore, "walking produces ground dust and contact/bass audio");
            Check(particlePeak > 0 && audioPeak >= 2, "walking plays actual particles and layered audio", new { particlePeak, audioPeak });
            Check(presentation.SlotCapacity == 4, "ground effects use four bounded reusable slots");
            encounter.Brain.Actor.Movement.StopMovement(); feetBefore = presentation.FootContactCount;
            Next(); return;
        }
        if (stage == 3)
        {
            if (Time.realtimeSinceStartup - stageAt < 1.5f) return;
            Check(presentation.FootContactCount == feetBefore, "stopped actor produces no phantom footsteps");
            encounter.enabled = true;
            caseIndex = 0; PreparePolishCase(); Next(); return;
        }
        if (stage == 4)
        {
            var actor = encounter.Brain.Actor;
            if (actor.AbilityController.IsExecuting && basic.HasEnteredMotion)
            {
                if (!caseEntered) { caseEntered = true; feetBefore = presentation.FootContactCount; attackFacing = actor.transform.rotation; }
                MeasureLiveFeedback();
                greatestFacingDrift = Mathf.Max(greatestFacingDrift, Quaternion.Angle(attackFacing, actor.transform.rotation));
                foreach (var warning in actor.GetComponents<EnemyStrongAttackWarning>())
                {
                    bool played = (bool)typeof(EnemyStrongAttackWarning).GetField("signalPlayed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(warning);
                    if (!played || cueSeen) continue;
                    cueSeen = true; cues++;
                    var camera = Camera.main;
                    Vector3 position = warning.ResolveCuePosition(camera);
                    var viewport = camera.WorldToViewportPoint(position);
                    Check(viewport.z > 0f && viewport.x > .02f && viewport.x < .98f && viewport.y > .02f && viewport.y < .98f,
                        polishPatterns[caseIndex] + " glint lies inside combat viewport", new[] { viewport.x, viewport.y, viewport.z });
                    var root = actor.transform;
                    var anchor = root.GetComponent<EnemyParryCueAnchor>();
                    Vector3 head = anchor.Head.position + Vector3.up * .85f;
                    Vector3 onScreenHead = camera.WorldToViewportPoint(head);
                    Check(Vector2.Distance(new Vector2(viewport.x,viewport.y),new Vector2(onScreenHead.x,onScreenHead.y)) < .01f,
                        polishPatterns[caseIndex] + " cue follows real animated head without screen drift");
                    foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var ray = new Ray(camera.transform.position, (position-camera.transform.position).normalized);
                        if (renderer.bounds.IntersectRay(ray,out float front))
                            Check(Vector3.Distance(camera.transform.position,position) <= front + .02f, polishPatterns[caseIndex] + " cue in front of boss silhouette");
                    }
                    Capture("cue-" + polishPatterns[caseIndex] + ".png");
                    samples.Add(new { pattern = polishPatterns[caseIndex], time = basic.NormalizedTime,
                        cue = new[] { position.x, position.y, position.z }, viewport = new[] {viewport.x,viewport.y,viewport.z},
                        size = warning.ResolveCueSize(), finalStrike = basic.CurrentMaterial.strikes.Last().impact });
                }
            }
            if (presentation.GroundStrikeCount > impactsBefore && impactCaptureAt < 0f) impactCaptureAt = Time.time + .12f;
            if (impactCaptureAt > 0f && Time.time >= impactCaptureAt && !impactCaptured)
            { impactCaptured = true; Capture("impact-" + polishPatterns[caseIndex] + ".png"); }
            // A composed pattern can move before its ability starts. Wait for the entire pattern.
            if (actor.AbilityController.IsExecuting || !string.IsNullOrEmpty(encounter.Brain.CurrentPatternId) || Time.time - caseAt < .8f)
            { if (Time.time - caseAt > 20f) throw new TimeoutException("Polish case did not complete."); return; }
            Check(caseEntered, polishPatterns[caseIndex] + " entered the actual attack motion", basic.LastFailure);
            Check(presentation.GroundStrikeCount > impactsBefore, polishPatterns[caseIndex] + " actual strike produces contact effect", presentation.GroundStrikeCount - impactsBefore);
            Check(presentation.AudioEmissionCount > soundBefore, polishPatterns[caseIndex] + " actual release produces impact and bass audio");
            Check(particlePeak > 0 && audioPeak >= 2, polishPatterns[caseIndex] + " actual particles and layered audio play during attack", new {particlePeak, audioPeak});
            Check(presentation.FootContactCount == feetBefore, polishPatterns[caseIndex] + " attack does not emit walking contacts");
            Check(greatestFacingDrift < .05f, polishPatterns[caseIndex] + " committed attack facing remains fixed", greatestFacingDrift);
            Check(cueSeen == expectsParry, polishPatterns[caseIndex] + (expectsParry ? " actual parry window displayed the repositioned glint" : " dodge-only attack emits no misleading parry glint"));
            if (++caseIndex < polishPatterns.Length) { PreparePolishCase(); stageAt = Time.realtimeSinceStartup; Write("RUNNING", ""); return; }
            presentation.enabled = false;
            Check(presentation.GetComponentsInChildren<AudioSource>(true).All(a => !a.isPlaying), "disable stops owned sound tails");
            Check(presentation.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.name.Contains("GroundDust")).All(p => p.particleCount == 0), "disable clears owned contact dust");
            presentation.enabled = true;
            encounter.Exit(true); encounter = null; Next(); return;
        }
        if (stage == 5)
        {
            var host = CrustaspikanEncounterHost.Current;
            if (!host.CanEnter || Time.realtimeSinceStartup - stageAt < 1f) return;
            Teleport(host.Entrance.transform.position + Vector3.back);
            Check(host.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "second actual portal entry");
            encounter = host.ActiveEncounter; Next(); return;
        }
        if (stage == 6)
        {
            if (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); return; }
            Check(encounter.Brain.Actor.GetComponent<CrustaspikanCombatPresentation>().Profile != null, "repeat spawn preserves saved presentation");
            Check(encounter.Brain.Actor.GetComponent<EnemyEliteFootstepEmitter>().Profile.EnemyId == encounter.Brain.Actor.Definition.EnemyId, "repeat spawn preserves the correct gait");
            Finish(failures == 0 ? "PASS" : "FAIL", "");
        }
    }
    private static void PreparePolishCase()
    {
        encounter.Restart(); encounter.Brain.ReviewMode = true;
        var actor = encounter.Brain.Actor;
        var body = actor.GetComponent<Rigidbody>(); body.position = encounter.ArenaCenter + Vector3.up * .05f; body.rotation = Quaternion.identity;
        actor.transform.SetPositionAndRotation(body.position, Quaternion.identity); Physics.SyncTransforms();
        basic = actor.GetComponent<EnemyBossMaterialExecutor>(); presentation = actor.GetComponent<CrustaspikanCombatPresentation>();
        var material = encounter.Brain.RuntimeMaterials.attacks.First(a => a.runtimeClip.name == new[] {
            "2HitComboAttack", "2HitComboAttackForward", "2HandsSmashAttack", "LeftHandSmashAttack", "LeftFootStompAttack" }[caseIndex]);
        float distance = caseIndex == 2 ? 8f : caseIndex == 1 ? 8f : Mathf.Clamp(material.strikes.Last().localOrigin.z, 3f, 12f);
        Teleport(actor.transform.position + Vector3.forward * distance);
        if (material.strikes.Length > 1) Check(!material.IsStrikeParryable(0) && material.IsStrikeParryable(material.strikes.Length-1), "combo parry remains final strike only");
        impactsBefore = presentation.GroundStrikeCount; feetBefore = presentation.FootContactCount; soundBefore = presentation.AudioEmissionCount;
        cueSeen = false; impactCaptured = false; caseEntered = false; expectsParry = material.IsStrikeParryable(material.strikes.Length - 1);
        greatestFacingDrift = 0f; attackFacing = actor.transform.rotation; impactCaptureAt = -1f; particlePeak = audioPeak = 0;
        caseAt = Time.time;
        Check(encounter.Brain.StartPatternForReview(polishPatterns[caseIndex]), polishPatterns[caseIndex] + " starts actual assembled pattern");
    }
    private static void MeasureLiveFeedback()
    {
        particlePeak = Mathf.Max(particlePeak, presentation.GetComponentsInChildren<ParticleSystem>(true)
            .Where(p => p.name.Contains("GroundDust")).Sum(p => p.particleCount));
        audioPeak = Mathf.Max(audioPeak, presentation.GetComponentsInChildren<AudioSource>(true)
            .Count(a => a.isPlaying && a.volume > .001f));
    }
    private static void Capture(string file)
    { ScreenCapture.CaptureScreenshot(Path.Combine(output, file)); }
    private static void Teleport(Vector3 point)
    {
        bool enabled = player.CharacterController.enabled; player.CharacterController.enabled = false;
        player.transform.position = point; player.CharacterController.enabled = enabled;
        player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms();
    }

    private static void Check(bool pass, string name, object evidence = null)
    { checks.Add(new { pass, name, evidence }); if (!pass) failures++; }
    private static void Next() { stage++; stageAt = Time.realtimeSinceStartup; Write("RUNNING", ""); }
    private static void Write(string status, string error)
        => WriteFile("result.json", new { status, impactCaptured, stage, caseIndex, failures, checks, error, utc = DateTime.UtcNow });
    private static void WriteFile(string file, object value)
        => File.WriteAllText(Path.Combine(output, file), JsonConvert.SerializeObject(value, Formatting.Indented));
    private static void Finish(string status, string error)
    {
        running = false;
        try
        {
            Write(status, error); WriteFile("samples.json", samples);
            encounter?.Exit(true); encounter = null;
            InputSystem.settings.backgroundBehavior = inputBefore; Application.runInBackground = backgroundBefore;
        }
        finally
        {
            player = null; basic = null; encounter = null;
            checks.Clear(); samples.Clear();
            UnlockReload(); EditorApplication.ExitPlaymode();
        }
    }
    private static void UnlockReload()
    {
        if (!SessionState.GetBool(Key + "reloadLocked", false)) return;
        SessionState.SetBool(Key + "reloadLocked", false); EditorApplication.UnlockReloadAssemblies();
    }
}
