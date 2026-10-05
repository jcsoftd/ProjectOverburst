using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;


// 실제 보스방에서 공격 목표 확정 직후 플레이어를 이동시켜 몸 방향·장판·투척 목표를 검사한다.
[InitializeOnLoad]
public static class CrustaspikanFacingLockVerifier
{
    private const string Key = "Overburst.CrustaspikanFacingLockVerifier.";
    private static readonly List<object> checks = new List<object>();
    private static readonly List<object> samples = new List<object>();
    private static readonly string[] patterns = {
        "right_light", "combo", "advance_combo", "donut",
        "weak_spit", "strong_spit", "rock_throw", "elite_throw", "weak_spit", "rock_throw", "weak_spit"
    };
    private static readonly float[] distances = { 7f, 8f, 12f, 8f, 12f, 12f, 12f, 12f, 12f, 12f, 12f };
    private static PlayerActorRuntime player;
    private static CrustaspikanEncounter encounter;
    private static EnemyBossMaterialExecutor basic;
    private static EnemyBossCompositePatternExecutor composite;
    private static EnemyStrongAttackWarning cue;
    private static string output;
    private static int stage, caseIndex, failures, measuredFrames, releasesBefore;
    private static bool running, baseline, cueSeen;
    private static float stageAt, caseAt, cueAt, nextSample, maxBodyAngle, maxCueAngle, maxAimDistance, maxCenterDistance;
    private static Quaternion committedRotation, cueRotation;
    private static Vector3 committedAim, cueCenter;
    private static InputSettings.BackgroundBehavior inputBefore;
    private static bool backgroundBefore;
    private static bool NoTelegraph => caseIndex == patterns.Length - 1;
    private static bool BasicFallback => caseIndex == 8 || caseIndex == 9;
    private static bool CompositeCase => caseIndex >= 4 && !BasicFallback;
    private static string Label => patterns[caseIndex] + (BasicFallback ? " basic fallback" : "") + (NoTelegraph ? " without telegraph" : "");

    static CrustaspikanFacingLockVerifier()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += Changed;
    }

    public static string Start(string directory, bool recordBaseline = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Editor must be idle.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Another isolated account owns the Editor.");
        output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetBool(Key + "baseline", recordBaseline);
        SessionState.SetBool(Key + "pending", true);
        SessionState.SetString(Key + "input", InputSystem.settings.backgroundBehavior.ToString());
        SessionState.SetBool(Key + "background", Application.runInBackground);
        WriteFile("before.json", new { project = Application.dataPath, pid = System.Diagnostics.Process.GetCurrentProcess().Id,
            startScene = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene),
            scenes = Scenes(), input = InputSystem.settings.backgroundBehavior.ToString(), Application.runInBackground });
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedSave"));
        return "Facing lock verification accepted";
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
                running = true; output = SessionState.GetString(Key + "output", ""); baseline = SessionState.GetBool(Key + "baseline", false);
                checks.Clear(); samples.Clear(); failures = 0; stage = 0; stageAt = Time.realtimeSinceStartup; caseIndex = 0;
                inputBefore = (InputSettings.BackgroundBehavior)Enum.Parse(typeof(InputSettings.BackgroundBehavior),
                    SessionState.GetString(Key + "input", "ResetAndDisableNonBackgroundDevices"));
                backgroundBefore = SessionState.GetBool(Key + "background", true);
                Application.runInBackground = true;
            }
            if (Time.realtimeSinceStartup - stageAt > 90f) throw new TimeoutException("Facing lock stage " + stage);
            Tick();
        }
        catch (Exception error) { Finish("FAIL", error.ToString()); }
    }

    private static void Tick()
    {
        if (stage == 0)
        {
            var host = CrustaspikanEncounterHost.Current;
            if (host == null || host.Entrance == null || !host.CanEnter) return;
            player = PlayerContext.Instance.CurrentActor;
            Teleport(host.Entrance.transform.position + Vector3.back);
            Check(host.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "actual portal entry");
            encounter = host.ActiveEncounter;
            Check(encounter != null && encounter.Brain != null, "actual player and boss exist");
            Next(); return;
        }
        if (stage == 1)
        {
            if (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); return; }
            if (Time.realtimeSinceStartup - stageAt < 1f) return;
            player.Health.SetMaxHp(20000f, true);
            PrepareCase(); Next(); return;
        }
        if (stage == 2)
        {
            var actor = encounter.Brain.Actor;
            bool executing = actor.AbilityController.IsExecuting;
            EnemyBossAttackMaterial attack = CompositeCase ? composite.CurrentMaterial : basic.CurrentMaterial;
            float progress = CompositeCase ? composite.NormalizedTime : basic.NormalizedTime;
            if (!cueSeen && executing && attack != null)
            {
                cue = actor.GetComponents<EnemyStrongAttackWarning>().FirstOrDefault(w => w.IsVisible);
                if (executing)
                {
                    cueSeen = true; cueAt = Time.time; committedRotation = actor.transform.rotation;
                    committedAim = Aim();
                    if (cue != null) { var visual = Visual(cue); cueRotation = visual.rotation; cueCenter = visual.position; }
                    Teleport(actor.transform.position + committedRotation * Vector3.right * distances[caseIndex]);
                    Check(Vector3.Angle(actor.transform.forward, player.transform.position - actor.transform.position) > 70f,
                        Label + " player moves across the committed direction");
                }
            }
            if (cueSeen && executing)
            {
                if (cue == null && !NoTelegraph)
                {
                    cue = actor.GetComponents<EnemyStrongAttackWarning>().FirstOrDefault(w => w.IsVisible);
                    if (cue != null) { var visual = Visual(cue); cueRotation = visual.rotation; cueCenter = visual.position; }
                }
                measuredFrames++;
                maxBodyAngle = Mathf.Max(maxBodyAngle, Quaternion.Angle(committedRotation, actor.transform.rotation));
                maxAimDistance = Mathf.Max(maxAimDistance, Vector3.Distance(committedAim, Aim()));
                if (cue != null && cue.IsVisible)
                {
                    var visual = Visual(cue);
                    maxCueAngle = Mathf.Max(maxCueAngle, Quaternion.Angle(cueRotation, visual.rotation));
                    if (attack != null && attack.delivery == EnemyBossMaterialDelivery.Boulder)
                        maxCenterDistance = Mathf.Max(maxCenterDistance, Vector3.ProjectOnPlane(cueCenter - visual.position, Vector3.up).magnitude);
                }
                if (Time.time >= nextSample)
                {
                    nextSample = Time.time + .1f;
                    samples.Add(new { label = Label, t = Time.time, progress, bodyAngle = Quaternion.Angle(committedRotation, actor.transform.rotation),
                        aimDistance = Vector3.Distance(committedAim, Aim()), cueVisible = cue != null && cue.IsVisible });
                }
            }
            if (cueSeen && !executing && encounter.Brain.CurrentPatternId == "")
            {
                int releases = CompositeCase ? composite.ReleaseCount : basic.ImpactCount;
                Check(measuredFrames >= 3, Label + " samples execution after player relocation", measuredFrames);
                Check(releases > releasesBefore, Label + " actual attack releases", new { before = releasesBefore, after = releases });
                Check(maxBodyAngle < 2f, Label + " body facing stays committed", maxBodyAngle);
                Check(maxAimDistance < .05f, Label + " aim point stays committed", maxAimDistance);
                if (!NoTelegraph)
                {
                    Check(cue != null, Label + " actually shows a telegraph");
                    Check(maxCueAngle < 2f, Label + " shown telegraph direction stays committed", maxCueAngle);
                }
                if (patterns[caseIndex] == "rock_throw" || patterns[caseIndex] == "elite_throw")
                    Check(maxCenterDistance < .05f, Label + " shown landing point stays committed", maxCenterDistance);
                if (NoTelegraph) Check(actor.GetComponents<EnemyStrongAttackWarning>().All(w => !w.IsVisible), Label + " no telegraph emitted");
                Check(string.IsNullOrEmpty(CompositeCase ? composite.LastFailure : basic.LastFailure), Label + " executor completes",
                    CompositeCase ? composite.LastFailure : basic.LastFailure);
                if (++caseIndex < patterns.Length) { PrepareCase(); stageAt = Time.realtimeSinceStartup; Write("RUNNING", ""); return; }
                caseIndex = patterns.Length - 1;
                Teleport(actor.transform.position + committedRotation * Vector3.left * 8f);
                caseAt = Time.time; Next(); return;
            }
            if (Time.time - caseAt > 32f)
                throw new TimeoutException(Label + " did not show/finish an attack; state=" + encounter.Brain.State);
            return;
        }
        if (stage == 3)
        {
            if (Time.time - caseAt < 2.5f) return;
            var actor = encounter.Brain.Actor;
            var delta = player.transform.position - actor.transform.position; delta.y = 0f;
            Check(Vector3.Angle(actor.transform.forward, delta) < 12f, "idle turns again after completed attack");
            Check(encounter.Brain.StartPatternForReview("combo"), "cancel regression starts a fresh attack");
            cueSeen = false; caseAt = Time.time; Next(); return;
        }
        if (stage == 4)
        {
            var actor = encounter.Brain.Actor;
            if (actor.GetComponents<EnemyStrongAttackWarning>().Any(w => w.IsVisible) && actor.AbilityController.IsExecuting)
            {
                committedRotation = actor.transform.rotation;
                Teleport(actor.transform.position + committedRotation * Vector3.right * 8f);
                actor.AbilityController.Cancel();
                Check(!actor.AbilityController.IsExecuting && !actor.Movement.IsActionLocked, "cancel releases attack and action lock");
                caseAt = Time.time; Next(); return;
            }
            if (Time.time - caseAt > 10f) throw new TimeoutException("Cancel probe did not reach a visible attack");
            return;
        }
        if (stage == 5)
        {
            if (Time.time - caseAt < 2.5f) return;
            var actor = encounter.Brain.Actor;
            var delta = player.transform.position - actor.transform.position; delta.y = 0f;
            Check(Vector3.Angle(actor.transform.forward, delta) < 12f, "cancelled attack releases facing for next decision");
            Check(actor.GetComponents<EnemyStrongAttackWarning>().All(w => !w.IsVisible), "cancel hides old telegraphs");
            Finish(failures == 0 ? "PASS" : baseline ? "BASELINE_REPRODUCED" : "FAIL", "");
        }
    }

    private static void PrepareCase()
    {
        encounter.Restart(); encounter.Brain.ReviewMode = true;
        var actor = encounter.Brain.Actor; var body = actor.GetComponent<Rigidbody>();
        body.position = encounter.ArenaCenter + Vector3.up * .05f; body.rotation = Quaternion.identity;
        actor.transform.SetPositionAndRotation(body.position, Quaternion.identity); Physics.SyncTransforms();
        Teleport(actor.transform.position + Vector3.forward * distances[caseIndex]);
        basic = actor.GetComponent<EnemyBossMaterialExecutor>(); composite = encounter.Brain.Composite;
        if (BasicFallback) composite.Configure(null);
        if (NoTelegraph)
            foreach (var attack in encounter.Brain.RuntimeMaterials.attacks)
                if (attack.runtimeClip.name == "SpitterShot2") attack.showTelegraph = false;
        cue = null; cueSeen = false; measuredFrames = 0;
        maxBodyAngle = maxCueAngle = maxAimDistance = maxCenterDistance = 0f;
        releasesBefore = CompositeCase ? composite.ReleaseCount : basic.ImpactCount;
        caseAt = Time.time; nextSample = Time.time;
        Check(encounter.Brain.StartPatternForReview(patterns[caseIndex]), Label + " starts actual assembly");
    }

    private static Vector3 Aim()
    {
        object owner = CompositeCase ? (object)composite : basic;
        string field = CompositeCase ? "aim" : "committedAim";
        return (Vector3)owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
    }
    private static Transform Visual(EnemyStrongAttackWarning warning)
        => ((GameObject)typeof(EnemyStrongAttackWarning).GetField("visual", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(warning)).transform;
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
        => WriteFile("result.json", new { status, baseline, stage, caseIndex, failures, checks, error, utc = DateTime.UtcNow });
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
            player = null; basic = null; composite = null; cue = null; encounter = null;
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
