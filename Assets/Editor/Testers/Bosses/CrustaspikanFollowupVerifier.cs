using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class CrustaspikanFollowupVerifier
{
    public static string StartD1(string output, bool baseline)
    {
        string account = Path.GetFullPath(Path.Combine(output, "Play01/IsolatedSave"));
        if (!EditorApplication.isPlaying || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) != account
            || !SessionState.GetBool("Overburst.CrustaspikanTempoAudit.pending", false)
            || SessionState.GetString("Overburst.CrustaspikanTempoAudit.output", "") != output)
            throw new InvalidOperationException("Own isolated Play required.");
        if (UnityEngine.Object.FindFirstObjectByType<CrustaspikanD1Observer>() != null
            || File.Exists(Path.Combine(output, "Play01/result.json")))
            throw new InvalidOperationException("Already started: inspect existing result.");
        var root = new GameObject("Owned Crustaspikan D1 Observer");
        UnityEngine.Object.DontDestroyOnLoad(root);
        root.AddComponent<CrustaspikanD1Observer>().Initialize(output, baseline);
        return "D1 actual portal/owned-motion observation started once.";
    }

    public static string VerifyD1Native(string output)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        var settings = Resources.Load<CrustaspikanEncounterSettings>("Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan");
        if (settings == null || !settings.Validate(out _)) throw new InvalidOperationException("Encounter invalid.");
        var profile = settings.materials.actorDefinition.ActorPrefab.GetComponent<EnemyAnimationBridge>().PlaybackProfile;
        if (!profile.Validate(out _)) throw new InvalidOperationException("Motion profile invalid.");
        var copy = UnityEngine.Object.Instantiate(settings);
        try
        {
            var move = new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Move, localDisplacement = Vector3.forward, seconds = 0f };
            copy.patterns = new[] { new CrustaspikanEncounterSettings.Pattern { id = "diagnostic", steps = new[] { move } } };
            if (copy.Validate(out _)) throw new InvalidOperationException("Nonzero move accepted with no deadline.");
            move.seconds = 1f; move.localDisplacement = new Vector3(float.NaN, 0, 1);
            if (copy.Validate(out _)) throw new InvalidOperationException("Nonfinite move accepted.");
            move.localDisplacement = Vector3.zero; move.seconds = 0f;
            if (!copy.Validate(out _)) throw new InvalidOperationException("No-op move must remain valid.");
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "native.json"), JsonConvert.SerializeObject(new {
            status = "PASS_NATIVE", checks = 5, utc = DateTime.UtcNow, PlayerBuild = "FORBIDDEN"
        }, Formatting.Indented));
        return "D1 native encounter/motion/negative-input checks passed: 5.";
    }
}

[DefaultExecutionOrder(20000)]
public sealed class CrustaspikanD1Observer : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    string output, caseId = "boot";
    bool baseline, finished;
    float bootAt;
    IEnumerator flow;
    PlayerActorRuntime player;
    CrustaspikanEncounter encounter;
    EnemyMovement changedMovement;
    bool originalMovementEnabled;
    float originalStatusMultiplier;
    uint changedLease;
    Vector3 sampledPosition;
    float sampledAt;
    readonly List<object> cases = new List<object>();
    readonly List<object> samples = new List<object>();
    readonly List<object> events = new List<object>();
    CrustaspikanEncounterBrain Brain => encounter.Brain;
    EnemyActor Boss => Brain.Actor;
    static FieldInfo Field(string id) => typeof(CrustaspikanEncounterBrain).GetField(id, Private);
    static object Call(CrustaspikanEncounterBrain brain, string id, params object[] args)
        => typeof(CrustaspikanEncounterBrain).GetMethod(id, Private).Invoke(brain, args);
    static float Distance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    static float[] VectorData(Vector3 value) => new[] { value.x, value.y, value.z };
    static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    void Event(string text) => events.Add(new { caseId, game = Time.time, real = Time.realtimeSinceStartup - bootAt, text,
        lease = encounter != null && encounter.Brain != null ? Boss.LeaseVersion : 0u,
        handle = encounter != null && encounter.Brain != null ? Boss.AnimationBridge.CurrentMotionHandle : default });
    public void Initialize(string folder, bool original)
    {
        output = folder; baseline = original; bootAt = Time.realtimeSinceStartup; flow = Run();
        Application.runInBackground = true;
        UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        Write("RUNNING", null);
    }
    void Update()
    {
        if (finished) return;
        try
        {
            Require(Mathf.Abs(Time.timeScale - 1f) < .001f && Time.captureDeltaTime == 0f, "Live clock changed.");
            if (Time.realtimeSinceStartup - bootAt > 300f) throw new TimeoutException("D1 overall deadline.");
            if (!flow.MoveNext()) Finish(baseline ? "REPRODUCED_BASELINE" : "PASS_PLAY", null);
        }
        catch (Exception error) { Finish(caseId == "boot" || caseId == "setup" ? "FAIL_PREPARATION" : "FAIL_PLAY", error.ToString()); }
    }
    IEnumerator Run()
    {
        bool hubRequested = false;
        while (encounter == null)
        {
            var host = CrustaspikanEncounterHost.Current;
            var sceneFlow = PersistentSceneFlow.Instance;
            player = PlayerContext.Instance?.CurrentActor;
            if (!hubRequested && player != null && sceneFlow != null && !sceneFlow.IsSwitching
                && sceneFlow.CurrentSubSceneName == PersistentSceneFlow.MainSceneName)
            {
                var portal = UnityEngine.Object.FindObjectsByType<HubScenePortal>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => p.TargetSceneName == PersistentSceneFlow.HideoutSceneName && p.IsInteractionAvailable(player));
                if (portal != null)
                {
                    Warp(portal.transform.position + Vector3.back);
                    Require(portal.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Actual hideout portal rejected.");
                    hubRequested = true; Event("actual-main-to-hideout-portal");
                }
            }
            if (player != null && host != null && host.CanEnter && host.Entrance != null)
            {
                Warp(host.Entrance.transform.position + Vector3.back);
                Require(host.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Actual boss portal rejected.");
                encounter = host.ActiveEncounter; Brain.ReviewMode = true; Event("actual-boss-entry");
            }
            if (Time.realtimeSinceStartup - bootAt > 80f) throw new TimeoutException("Actual portal/player boot not ready.");
            yield return null;
        }
        while (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); yield return null; }
        foreach (float angle in new[] { 90f, 180f })
        {
            var test = MoveCase("advance-during-turn-" + angle, angle, "advance");
            while (test.MoveNext()) yield return null;
        }
        if (baseline) yield break;
        foreach (string kind in new[] { "back", "side", "boundary", "disabled-motor", "status-lock", "cancel-before", "cancel-after", "groggy-before", "groggy-after", "carry-forward", "carry-back", "noop" })
        {
            var test = MoveCase(kind, kind == "cancel-before" || kind == "groggy-before" ? 90f : 0f, kind);
            while (test.MoveNext()) yield return null;
        }
        var geometry = GeometryCase(); while (geometry.MoveNext()) yield return null;
    }
    IEnumerator ResetCase(float angle, float radius)
    {
        caseId = "setup";
        Require(encounter.TryRestart(), "Actual restart failed: " + encounter.LastFailure);
        Brain.ReviewMode = true;
        Warp(Boss.transform.position + Boss.Movement.PhysicalRotation * Quaternion.Euler(0, angle, 0) * Vector3.forward * radius + Vector3.up * .1f);
        float deadline = Time.realtimeSinceStartup + 4f;
        if (Mathf.Abs(angle) > .1f)
        {
            while (!Boss.Movement.IsOwnedTurning)
            { Require(Time.realtimeSinceStartup < deadline, "Owned turn never started."); yield return null; }
        }
        else
        {
            float settle = Time.time + .3f;
            while (Time.time < settle || Boss.Movement.IsOwnedTurning) { Require(Time.realtimeSinceStartup < deadline, "Initial settle timed out."); yield return null; }
        }
    }
    IEnumerator MoveCase(string id, float angle, string kind)
    {
        var setup = ResetCase(angle, 10f); while (setup.MoveNext()) yield return null;
        caseId = id;
        if (kind == "boundary")
        {
            Vector3 forward = Boss.Movement.PhysicalRotation * Vector3.forward;
            Vector3 edge = encounter.ClampArena(encounter.ArenaCenter - forward * 100f, 5.1f);
            edge.y = Boss.transform.position.y;
            Boss.Movement.StopMovement(); Boss.transform.position = edge;
            var body = Boss.GetComponent<Rigidbody>(); if (body != null) body.position = edge;
            CombatTargetRegistry.NotifySpatialChanged(Boss.transform);
            Warp(edge + forward * 10f + Vector3.up * .1f);
            Event("diagnostic-boundary-position");
        }
        Vector3 origin = Boss.transform.position, moveOrigin = origin;
        Quaternion facing = Boss.Movement.PhysicalRotation;
        int count = Brain.PatternCount;
        float at = Time.time, limit = at + 18f, firstMoveAt = -1f, firstTimerAt = -1f;
        float maxDistance = 0f, maxFacing = 0f;
        float maxAttackFacing = 0f, maxAttackAimDelta = 0f;
        bool prematureCommit = false, prematureTimer = false, attack = false, carry = false, canceled = false, warped = false;
        bool attackSnapshot = false;
        Quaternion attackFacing = default;
        Vector3 attackAim = default;
        sampledPosition = origin; sampledAt = at;
        string terminal = "";
        EnemyAbilityStartContext prepared = default;
        if (kind == "advance") Require(Brain.StartPatternForReview("advance_combo"), "Actual advance pattern rejected.");
        else
        {
            var displacement = kind == "back" || kind == "carry-back" || kind == "boundary" ? Vector3.back
                : kind == "side" ? Vector3.right : kind == "noop" ? Vector3.zero : Vector3.forward;
            var steps = new List<CrustaspikanEncounterSettings.Step>();
            if (kind.StartsWith("carry")) steps.Add(new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Motion, materialOrMotion = "UnearthRock" });
            steps.Add(new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Move, localDisplacement = displacement, seconds = kind == "noop" ? 0f : 1.1f });
            if (kind.StartsWith("carry")) steps.Add(new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Attack, materialOrMotion = "ThrowRock" });
            var pattern = new CrustaspikanEncounterSettings.Pattern { id = "diagnostic-" + kind, label = "D1 " + kind, family = "diagnostic", cooldown = 6f, steps = steps.ToArray() };
            Field("current").SetValue(Brain, pattern); Call(Brain, "BeginPattern");
        }
        bool enabled = Boss.Movement.enabled;
        float statusMultiplier = Boss.Movement.StatusMoveSpeedMultiplier;
        changedMovement = Boss.Movement; originalMovementEnabled = enabled;
        originalStatusMultiplier = statusMultiplier; changedLease = Boss.LeaseVersion;
        if (kind == "disabled-motor") Boss.Movement.enabled = false;
        if (kind == "status-lock") Boss.Movement.SetStatusMoveSpeedMultiplier(0f);
        Event("pattern-requested");
        while (Brain.CurrentPatternId != "")
        {
            float moved = Distance(origin, Boss.transform.position);
            maxDistance = Mathf.Max(maxDistance, moved);
            int step = (int)Field("stepIndex").GetValue(Brain);
            bool movingStep = kind.StartsWith("carry") ? step == 1 : step == 0;
            float timerAt = (float)Field("stepStartedAt").GetValue(Brain);
            if (movingStep && moved >= .02f && firstMoveAt < 0f) { firstMoveAt = Time.time; Event("actual-move-start"); }
            if (movingStep && (bool)Field("stepStarted").GetValue(Brain) && timerAt > 0f && firstTimerAt < 0f) firstTimerAt = timerAt;
            if (!kind.StartsWith("carry") && firstMoveAt < 0f)
            {
                prematureCommit |= Brain.PatternCount > count;
                prematureTimer |= movingStep && (bool)Field("stepStarted").GetValue(Brain) && timerAt > 0f;
            }
            var bridge = Boss.AnimationBridge;
            attack |= bridge.CurrentMotionRole == EnemyMotionRole.Attack && Boss.AbilityController.IsExecuting;
            if (!baseline && bridge.CurrentMotionRole == EnemyMotionRole.Attack && Boss.AbilityController.IsExecuting)
            {
                Vector3 aim = CommittedAim();
                if (!attackSnapshot)
                {
                    attackSnapshot = true; attackFacing = Boss.Movement.PhysicalRotation; attackAim = aim;
                    if (kind == "advance")
                    { Warp(Boss.transform.position + attackFacing * Quaternion.Euler(0, 90, 0) * Vector3.forward * 10f + Vector3.up * .1f); Event("target-moved-after-attack-acceptance"); }
                }
                maxAttackFacing = Mathf.Max(maxAttackFacing, Quaternion.Angle(attackFacing, Boss.Movement.PhysicalRotation));
                maxAttackAimDelta = Mathf.Max(maxAttackAimDelta, Vector3.Distance(attackAim, aim));
            }
            if (bridge.CurrentMotionRole == EnemyMotionRole.Carry)
            {
                if (!carry)
                {
                    prepared = PreparedSnapshot();
                    carry = true; moveOrigin = Boss.transform.position; facing = Boss.Movement.PhysicalRotation;
                }
                if (!warped)
                {
                    Warp(origin + facing * Quaternion.Euler(0, 90, 0) * Vector3.forward * 10f + Vector3.up * .1f); warped = true;
                }
                var current = PreparedSnapshot();
                Require(Distance(prepared.AimPosition, current.AimPosition) < .005f && Quaternion.Angle(prepared.Facing, current.Facing) < .1f, "Prepared aim changed during carry.");
                maxFacing = Mathf.Max(maxFacing, Quaternion.Angle(facing, Boss.Movement.PhysicalRotation));
            }
            else if (kind == "back" || kind == "side") maxFacing = Mathf.Max(maxFacing, Quaternion.Angle(facing, Boss.Movement.PhysicalRotation));
            terminal = Brain.State;
            ObserveMove(origin);
            if ((kind == "cancel-before" && firstMoveAt < 0f) || (kind == "cancel-after" && moved >= .2f))
            {
                Call(Brain, "CancelPattern"); canceled = true; terminal = "explicit-cancel"; Event(terminal); break;
            }
            if ((kind == "groggy-before" && firstMoveAt < 0f) || (kind == "groggy-after" && moved >= .2f))
            {
                var settings = (CrustaspikanEncounterSettings)Field("settings").GetValue(Brain);
                Call(Brain, "AddPoise", settings.groggyMax);
                Require(Brain.IsGroggy && Brain.CurrentPatternId == "", "Actual groggy did not cancel movement.");
                canceled = true; terminal = "groggy-cancel"; Event(terminal); break;
            }
            Require(Time.time < limit, "Move case deadline: " + id);
            yield return null;
        }
        Boss.Movement.enabled = enabled; Boss.Movement.SetStatusMoveSpeedMultiplier(statusMultiplier);
        changedMovement = null;
        if (!canceled) terminal = Brain.State;
        bool cooldown = ((Dictionary<string, float>)Field("cooldowns").GetValue(Brain)).Any(pair => pair.Value > Time.time);
        if (baseline)
            Require(prematureCommit && prematureTimer && maxDistance < 1f, "Expected pre-fix turn/early timer defect was not reproduced.");
        else if (kind == "disabled-motor" || kind == "status-lock" || kind == "boundary")
            Require(maxDistance < .02f && Brain.PatternCount == count && !cooldown && Time.time - at < 1.3f, "Failed movement was falsely committed or hung.");
        else if (kind == "cancel-before" || kind == "groggy-before")
            Require(canceled && Brain.PatternCount == count && !cooldown && !Boss.Movement.HasDestination, "Cancel before move left side effects.");
        else if (kind == "cancel-after" || kind == "groggy-after")
            Require(canceled && Brain.PatternCount == count + 1 && cooldown && !Boss.Movement.HasDestination, "Cancel after real move lost commitment or command.");
        else if (kind == "noop")
            Require(Brain.PatternCount == count && !cooldown && maxDistance < .02f, "No-op move was committed.");
        else
        {
            Require(maxDistance >= (kind == "advance" ? 2f : .8f), "Destination not reached: " + maxDistance);
            Require(!prematureCommit && !prematureTimer, "Timer/count started before actual movement.");
            Require(firstMoveAt >= 0 && firstTimerAt >= firstMoveAt - .05f, "Move timer was not based on actual start.");
            Require(Brain.PatternCount == count + 1, "Move pattern commitment mismatch.");
            if (kind == "advance" || kind.StartsWith("carry")) Require(attack, "Follow-up attack was never accepted.");
            if (attackSnapshot) Require(maxAttackFacing < .1f && maxAttackAimDelta < .005f, "Committed attack aim/body followed moved target.");
            if (kind.StartsWith("carry")) Require(carry && maxFacing < 1f, "Carry facing/motion invalid.");
            if (kind == "back" || kind == "side") Require(maxFacing < 1f, "Back/side movement changed body facing.");
        }
        Vector3 end = Boss.transform.position;
        if (canceled)
        {
            Require(!(bool)Field("moveHasStarted").GetValue(Brain)
                && !(bool)typeof(EnemyMovement).GetField("keepMoveFacing", Private).GetValue(Boss.Movement), "Cancel retained move bookkeeping/facing policy.");
            float until = Time.time + .25f;
            while (Time.time < until) yield return null;
            Require(Distance(end, Boss.transform.position) < .05f, "Cancelled move continued travelling.");
        }
        bool resumed = false;
        int committedCount = Brain.PatternCount - count;
        if (kind.StartsWith("groggy"))
        {
            float recoveryDeadline = Time.time + 10f;
            var reaction = Boss.GetComponent<EnemyMovementReaction>();
            var temporary = Boss.GetComponent<CrustaspikanTemporaryReaction>();
            // The poise clock expires before the authored get-up and its final forced hit-stun.
            // Match the actual BT/movement gates before attempting the recovery control.
            while (Brain.IsGroggy || reaction.BlocksAttack || temporary.BlocksActions
                || Boss.Movement.IsActionLocked || Boss.Movement.IsStatusMovementLocked || Boss.AnimationBridge.IsFrozen)
            {
                ObserveMove(origin);
                Require(Time.time < recoveryDeadline, "Groggy reaction/action-lock recovery timeout.");
                yield return null;
            }
            Event("groggy-reaction-and-action-lock-released");
            Vector3 resumeOrigin = Boss.transform.position;
            Require(Brain.StartPatternForReview("advance_combo"), "Post-groggy advance rejected.");
            bool resumedAttack = false;
            float maxResumeMove = 0f;
            while (Brain.CurrentPatternId != "")
            {
                maxResumeMove = Mathf.Max(maxResumeMove, Distance(resumeOrigin, Boss.transform.position));
                resumedAttack |= Boss.AnimationBridge.CurrentMotionRole == EnemyMotionRole.Attack && Boss.AbilityController.IsExecuting;
                ObserveMove(resumeOrigin);
                Require(Time.time < recoveryDeadline + 12f, "Post-groggy movement/attack timeout.");
                yield return null;
            }
            Require(maxResumeMove >= 2f && resumedAttack, "Post-groggy movement/attack did not recover.");
            resumed = true; Event("post-groggy-advance-and-attack-completed");
        }
        cases.Add(new { id, status = baseline ? "REPRODUCED" : "PASS", terminal, seconds = Time.time - at,
            maxDistance, prematureCommit, prematureTimer, firstMoveAt, firstTimerAt, attack, carry, maxFacing, cooldown,
            committedCount, resumed, maxAttackFacing, maxAttackAimDelta, diagnostic = kind != "advance", commandIssuedIsNotDisplacement = true });
        Write("RUNNING", null);
    }
    IEnumerator GeometryCase()
    {
        foreach (float radius in new[] { 10f, 13f, 17f })
        {
            var setup = ResetCase(0f, radius); while (setup.MoveNext()) yield return null;
            caseId = "post-move-geometry-" + radius;
            var context = Call(Brain, "ReadContext");
            typeof(CrustaspikanEncounterBrain).GetProperty("Context").GetSetMethod(true).Invoke(Brain, new[] { context });
            var settings = (CrustaspikanEncounterSettings)Field("settings").GetValue(Brain);
            var pattern = settings.patterns.First(p => p.id == "advance_combo");
            bool eligible = (bool)Call(Brain, "Eligible", pattern);
            Require(eligible == (radius == 10f), "Advance post-move eligibility mismatch at " + radius);
            cases.Add(new { id = caseId, status = "PASS", eligible, radius, diagnostic = true });
        }
    }
    void Warp(Vector3 position)
    {
        var controller = player.CharacterController; bool enabled = controller.enabled; controller.enabled = false;
        player.transform.position = position; controller.enabled = enabled;
        player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms();
    }
    Vector3 CommittedAim()
    {
        var basic = Boss.GetComponent<EnemyBossMaterialExecutor>();
        if (Brain.Composite.IsExecuting) return (Vector3)typeof(EnemyBossCompositePatternExecutor).GetField("aim", Private).GetValue(Brain.Composite);
        return (Vector3)typeof(EnemyBossMaterialExecutor).GetField("committedAim", Private).GetValue(basic);
    }
    void ObserveMove(Vector3 origin)
    {
        var bridge = Boss.AnimationBridge;
        var basic = Boss.GetComponent<EnemyBossMaterialExecutor>();
        var compound = Brain.Composite;
        bridge.TryReadMotion(bridge.CurrentMotionHandle, out var pose);
        float delta = Time.time - sampledAt;
        float speed = delta > .0001f ? Distance(sampledPosition, Boss.transform.position) / delta : 0f;
        float[] aim = compound.HasPreparation ? VectorData(PreparedSnapshot().AimPosition)
            : Boss.AbilityController.IsExecuting ? VectorData(CommittedAim()) : null;
        samples.Add(new { caseId, game = Time.time, real = Time.realtimeSinceStartup - bootAt, lease = Boss.LeaseVersion,
            handle = bridge.CurrentMotionHandle, motion = pose.MotionId, progress = pose.Normalized,
            motionState = bridge.CurrentMotionState.ToString(), role = bridge.CurrentMotionRole.ToString(), turning = Boss.Movement.IsOwnedTurning,
            step = (int)Field("stepIndex").GetValue(Brain), stepStarted = (bool)Field("stepStarted").GetValue(Brain),
            timerAt = (float)Field("stepStartedAt").GetValue(Brain), until = (float)Field("stepUntil").GetValue(Brain),
            command = Boss.Movement.HasDestination, destination = VectorData((Vector3)Field("moveDestination").GetValue(Brain)),
            position = new[] { Boss.transform.position.x, Boss.transform.position.z }, yaw = Boss.Movement.PhysicalRotation.eulerAngles.y,
            target = new[] { player.transform.position.x, player.transform.position.z }, aim,
            moved = Distance(origin, Boss.transform.position), actualSpeed = speed, requestedSpeed = Boss.Movement.ActiveMoveSpeed,
            count = Brain.PatternCount, pattern = Brain.CurrentPatternId, Brain.State, execution = Boss.AbilityController.IsExecuting,
            material = compound.IsExecuting ? compound.CurrentMaterial?.runtimeClip?.name : basic.IsExecuting ? basic.CurrentMaterial?.runtimeClip?.name : "",
            impacts = basic.ImpactCount, releases = compound.ReleaseCount, damage = basic.DamageCount + compound.DamageCount,
            completed = basic.CompletedCount + compound.CompletedCount, timeScale = Time.timeScale });
        sampledPosition = Boss.transform.position; sampledAt = Time.time;
    }
    EnemyAbilityStartContext PreparedSnapshot()
    {
        Require(Brain.Composite.HasPreparation, "Carry lost preparation ownership.");
        var type = typeof(EnemyBossCompositePatternExecutor);
        return new EnemyAbilityStartContext(
            (EnemyMotionHandle)type.GetField("preparationHandle", Private).GetValue(Brain.Composite),
            (Vector3)type.GetField("preparedAim", Private).GetValue(Brain.Composite),
            (Quaternion)type.GetField("preparedFacing", Private).GetValue(Brain.Composite),
            (uint)type.GetField("preparedLease", Private).GetValue(Brain.Composite));
    }
    void Write(string status, string error)
    {
        File.WriteAllText(Path.Combine(output, "Play01/result.json"), JsonConvert.SerializeObject(new {
            status, error, baseline, utc = DateTime.UtcNow, cases, events, samples,
            scope = "Actual isolated portal/restarts, controlled target positions, owned motion and movement; diagnostic patterns labeled.",
            PlayerBuild = "FORBIDDEN", userManualFeel = "NOT_RUN", stoppedFromPlayerCallback = false, assetValuesModified = false
        }, Formatting.Indented));
    }
    void Finish(string status, string error)
    {
        if (finished) return; finished = true;
        try { Write(status, error); }
        finally
        {
            if (changedMovement != null && changedMovement.GetComponent<EnemyActor>().LeaseVersion == changedLease)
            { changedMovement.enabled = originalMovementEnabled; changedMovement.SetStatusMoveSpeedMultiplier(originalStatusMultiplier); }
            changedMovement = null;
            if (encounter != null && encounter.Brain != null) encounter.Brain.ReviewMode = true;
        }
    }
}
