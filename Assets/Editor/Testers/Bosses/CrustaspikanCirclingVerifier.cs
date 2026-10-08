using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class CrustaspikanCirclingVerifier
{
    public static string Start(string output, bool baseline)
    {
        string account = Path.GetFullPath(Path.Combine(output, "Play01/IsolatedSave"));
        if (!EditorApplication.isPlaying || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) != account
            || !SessionState.GetBool("Overburst.CrustaspikanTempoAudit.pending", false)
            || SessionState.GetString("Overburst.CrustaspikanTempoAudit.output", "") != output
            || File.Exists(Path.Combine(output, "Play01/result.json")) || UnityEngine.Object.FindFirstObjectByType<CrustaspikanCirclingObserver>() != null)
            throw new InvalidOperationException("Own new isolated D3 run required.");
        var root = new GameObject("Owned Crustaspikan Circling Observer");
        UnityEngine.Object.DontDestroyOnLoad(root); root.AddComponent<CrustaspikanCirclingObserver>().Initialize(output, baseline);
        return "D3 actual keyboard/mouse input and decision observation started once.";
    }
    public static string Native(string output)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        var ctor = typeof(CrustaspikanCombatContext).GetConstructors().Single(c => c.GetParameters().Length == 9);
        var context = (CrustaspikanCombatContext)ctor.Invoke(new object[] { Vector3.zero, Vector3.forward, Vector3.forward * 5f,
            Vector3.right * 4f, 0f, 0f, 0, 1, Vector3.right * 4f });
        float tangent = (float)typeof(CrustaspikanCombatContext).GetField("TangentialSpeed").GetValue(context);
        Check(tangent < .001f && context.RadialSpeed == 0f, "Shared physical movement must not imply circling.");
        context = (CrustaspikanCombatContext)ctor.Invoke(new object[] { Vector3.zero, Vector3.forward, Vector3.forward * 5f,
            new Vector3(3, 0, 2), 0f, 0f, 0, 1, Vector3.zero });
        Check(Mathf.Abs((float)typeof(CrustaspikanCombatContext).GetField("TangentialSpeed").GetValue(context) - 3f) < .001f
            && Mathf.Abs(context.RadialSpeed - 2f) < .001f, "Radial/tangential decomposition.");
        var weight = typeof(CrustaspikanCombatDecision).GetMethod("Weight"); int checks = 2;
        foreach (string family in new[] { "turn", "rear", "stomp", "area", "light", "combo", "pressure", "ranged", "summon" })
        {
            var p = new CrustaspikanEncounterSettings.Pattern { id = "test", family = family, weight = 10f };
            float old = (float)weight.Invoke(null, new object[] { p, context, "test", family, 2, false });
            float changed = (float)weight.Invoke(null, new object[] { p, context, "test", family, 2, true });
            float ratio = family == "turn" || family == "rear" || family == "stomp" || family == "area" ? 1.75f
                : family == "light" || family == "combo" ? .75f : 1f;
            Check(Mathf.Abs(changed - old * ratio) < .0001f, "Post-clamp/post-repeat weight: " + family); checks++;
        }
        var update = typeof(CrustaspikanCombatDecision).GetMethod("UpdateCirclingPressure");
        object[] state = { context, true, .05f, false, 0f };
        for (int i = 0; i < 4; i++) update.Invoke(null, state);
        Check(!(bool)state[3], "Brief motion must not enter."); update.Invoke(null, state);
        Check((bool)state[3], "Sustained .25s motion must enter."); checks += 2;
        state[0] = new CrustaspikanCombatContext(Vector3.zero, Vector3.forward, Vector3.forward * 5f, Vector3.zero, 0, 0, 0, 1);
        for (int i = 0; i < 4; i++) update.Invoke(null, state);
        Check((bool)state[3], "Brief rest must not exit."); update.Invoke(null, state);
        Check(!(bool)state[3], "Sustained .25s rest must exit."); checks += 2;
        state[3] = true; state[1] = false; update.Invoke(null, state); Check(!(bool)state[3], "Invalid/teleport sample resets."); checks++;
        state[3] = true; state[1] = true;
        state[0] = new CrustaspikanCombatContext(Vector3.zero, Vector3.forward, Vector3.forward * 13f, Vector3.right * 4f, 0, 0, 0, 1);
        update.Invoke(null, state); Check(!(bool)state[3], "Out-of-distance resets."); checks++;
        File.WriteAllText(Path.Combine(output, "native.json"), JsonConvert.SerializeObject(new { status = "PASS_NATIVE", checks, utc = DateTime.UtcNow, PlayerBuild = "FORBIDDEN" }, Formatting.Indented));
        return "D3 native relative motion/weight/filter checks: " + checks;
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

[DefaultExecutionOrder(20000)]
public sealed class CrustaspikanCirclingObserver : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    string output, caseId = "boot"; bool baseline, finished; float bootAt; IEnumerator flow;
    PlayerActorRuntime player; CrustaspikanEncounter encounter;
    Keyboard keyboard, oldKeyboard; Mouse mouse, oldMouse; PlayerInputFacade facade; InputDevice[] oldDevices;
    PlayerEvadeController evade; Vector3 evadeOrigin; int evadeStarts, evadeEnds; float evadeMoved;
    readonly List<object> cases = new List<object>(), samples = new List<object>(), events = new List<object>();
    CrustaspikanEncounterBrain Brain => encounter.Brain;
    EnemyActor Boss => Brain.Actor;
    bool Pressure => typeof(CrustaspikanEncounterBrain).GetField("circlingPressure", Private) is FieldInfo field && (bool)field.GetValue(Brain);
    static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    static float[] V(Vector3 a) => new[] { a.x, a.y, a.z };
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    object Call(string method, params object[] args) => typeof(CrustaspikanEncounterBrain).GetMethod(method, Private).Invoke(Brain, args);
    public void Initialize(string folder, bool original)
    {
        output = folder; baseline = original; bootAt = Time.realtimeSinceStartup; flow = Run();
        Application.runInBackground = true; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Write("RUNNING", null);
    }
    void Update()
    {
        if (finished) return;
        try
        {
            Check(Time.timeScale == 1f && Time.captureDeltaTime == 0f, "Live gameplay clock changed.");
            if (Time.realtimeSinceStartup - bootAt > 420f) throw new TimeoutException("D3 bounded deadline.");
            if (!flow.MoveNext()) Finish(baseline ? "REPRODUCED_BASELINE" : "PASS_PLAY", null);
        }
        catch (Exception error) { Finish(caseId == "boot" || caseId == "input-preparation" ? "FAIL_PREPARATION" : "FAIL_PLAY", error.ToString()); }
    }
    IEnumerator Run()
    {
        bool hub = false;
        while (encounter == null)
        {
            player = PlayerContext.Instance?.CurrentActor; var host = CrustaspikanEncounterHost.Current; var scene = PersistentSceneFlow.Instance;
            if (!hub && player != null && scene != null && !scene.IsSwitching && scene.CurrentSubSceneName == PersistentSceneFlow.MainSceneName)
            {
                var portal = UnityEngine.Object.FindObjectsByType<HubScenePortal>(FindObjectsSortMode.None).FirstOrDefault(p => p.TargetSceneName == PersistentSceneFlow.HideoutSceneName && p.IsInteractionAvailable(player));
                if (portal != null) { Warp(portal.transform.position + Vector3.back); Check(portal.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Actual hideout portal rejected."); hub = true; }
            }
            if (player != null && host != null && host.CanEnter && host.Entrance != null)
            {
                Warp(host.Entrance.transform.position + Vector3.back); Check(host.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Actual boss portal rejected.");
                encounter = host.ActiveEncounter; Brain.ReviewMode = true;
            }
            Check(Time.realtimeSinceStartup - bootAt < 80f, "Actual portal preparation timeout."); yield return null;
        }
        while (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); yield return null; }
        caseId = "input-preparation"; facade = player.GetComponent<PlayerInputFacade>(); evade = player.GetComponent<PlayerEvadeController>();
        Check(facade != null && facade.IsGameplayEnabled && !GameplayInputBlocker.IsGameplayInputBlocked && evade != null, "Actual gameplay input blocked.");
        oldKeyboard = Keyboard.current; oldMouse = Mouse.current;
        oldDevices = facade.RuntimeAsset.devices?.ToArray();
        keyboard = InputSystem.AddDevice<Keyboard>("CrustaspikanCirclingKeyboard"); mouse = InputSystem.AddDevice<Mouse>("CrustaspikanCirclingMouse");
        keyboard.MakeCurrent(); mouse.MakeCurrent(); facade.RuntimeAsset.devices = new InputDevice[] { keyboard, mouse };
        evade.OnEvadeStarted += EvadeStarted; evade.OnEvadeEnded += EvadeEnded;
        Send(Vector3.zero, false, !PlayerCombatModeController.IsSharedCombatModeActive()); yield return null; Send(Vector3.zero); float until = Time.time + .5f;
        while (Time.time < until) yield return null;
        // The X binding requests the normal mode controller; no direct evade/state calls.
        Check(PlayerCombatModeController.IsSharedCombatModeActive() && evade.CanEvadeInCurrentMode, "Combat mode/profile preparation failed.");
        Send(player.transform.forward, true); yield return null; Send(Vector3.zero);
        until = Time.time + 3f;
        while (evadeEnds == 0) { Check(Time.time < until, "Shift did not complete an actual evade."); yield return null; }
        Check(evadeStarts == 1 && evadeMoved > .2f && evade.LastEndWasCompleted, "Actual evade motion incomplete.");
        cases.Add(new { id = caseId, status = "PASS", scheme = "Keyboard&Mouse", inputMap = facade.GameplayMap.name,
            deviceRestriction = "runtime clone only", sourceBindingsModified = false, evadeStarts, evadeEnds, evadeMoved, completed = evade.LastEndWasCompleted }); Write("RUNNING", null);
        if (!baseline)
        {
            caseId = "stationary-actual-hit-control"; Check(encounter.TryRestart(), "Restart failed."); Brain.ReviewMode = true;
            Warp(Boss.transform.position + Boss.Movement.PhysicalRotation * Vector3.forward * 4f + Vector3.up * .1f);
            until = Time.time + 1f; while (Time.time < until) yield return null;
            float hp = player.Health.CurrentHp; int impacts = Boss.GetComponent<EnemyBossMaterialExecutor>().ImpactCount;
            string attack = encounter.Settings.patterns.First(p => p.family == "light").id;
            Check(Brain.StartPatternForReview(attack), "Stationary light attack refused."); until = Time.time + 8f;
            while (Brain.CurrentPatternId != "") { Check(Time.time < until, "Stationary attack did not complete."); yield return null; }
            Check(Boss.GetComponent<EnemyBossMaterialExecutor>().ImpactCount > impacts && player.Health.CurrentHp < hp, "Actual stationary strike must produce damage.");
            cases.Add(new { id = caseId, status = "PASS", attack, damage = hp - player.Health.CurrentHp }); Write("RUNNING", null);
        }
        foreach (float radius in baseline ? new[] { 5.5f } : new[] { 5.5f, 8f, 13f })
            foreach (int sign in baseline ? new[] { 1 } : new[] { 1, -1 })
            {
                var test = Circle(radius, sign); while (test.MoveNext()) yield return null;
            }
        if (!baseline)
        {
            caseId = "teleport-and-restart-reset"; Brain.ReviewMode = true;
            Warp(Boss.transform.position + Vector3.forward * 13f); yield return null; yield return null;
            Check(!Pressure, "Teleport retained pressure."); uint oldLease = Boss.LeaseVersion;
            Check(encounter.TryRestart(), "Lease restart failed."); Brain.ReviewMode = true; yield return null;
            Check(!Pressure, "New lease retained circling state.");
            cases.Add(new { id = caseId, status = "PASS", oldLease, newLease = Boss.LeaseVersion });
            Check(PlayerInputFacade.Current == facade && facade.IsGameplayEnabled, "Actual facade changed during tests.");
        }
    }
    IEnumerator Circle(float radius, int sign)
    {
        caseId = "circle-" + radius + "-" + sign; Send(Vector3.zero); Check(encounter.TryRestart(), "Circle restart failed."); Brain.ReviewMode = true;
        Warp(Boss.transform.position + Boss.Movement.PhysicalRotation * Vector3.forward * radius + Vector3.up * .1f);
        float settle = Time.time + .5f; while (Time.time < settle) yield return null;
        Brain.ReviewMode = false; float start = Time.time, sampleAt = 0f; int pressureSamples = 0, eligiblePressure = 0;
        float maxTangent = 0f, maxFacingDelta = 0f, maxAimDelta = 0f; string selected = "";
        Quaternion facing = default; Vector3 aim = default; bool executing = false; int attackSequence = -1, selections = 0;
        var basic = Boss.GetComponent<EnemyBossMaterialExecutor>(); int damageBefore = basic.DamageCount + Brain.Composite.DamageCount;
        float hp = player.Health.CurrentHp;
        while (Time.time - start < 30f)
        {
            Vector3 radial = player.transform.position - Boss.transform.position; radial.y = 0;
            Vector3 direction = Vector3.Cross(Vector3.up, radial.normalized) * sign - radial.normalized * (radial.magnitude - radius) * .65f;
            Send(direction);
            bool active = Boss.AbilityController.IsExecuting;
            if (active)
            {
                Vector3 currentAim = Brain.Composite.IsExecuting
                    ? (Vector3)typeof(EnemyBossCompositePatternExecutor).GetField("aim", Private).GetValue(Brain.Composite)
                    : (Vector3)typeof(EnemyBossMaterialExecutor).GetField("committedAim", Private).GetValue(basic);
                int currentSequence = Brain.Composite.IsExecuting ? (int)typeof(EnemyBossCompositePatternExecutor).GetField("sequence", Private).GetValue(Brain.Composite) : basic.ActiveAttackSequenceId;
                if (!executing || currentSequence != attackSequence) { facing = Boss.Movement.PhysicalRotation; aim = currentAim; attackSequence = currentSequence; }
                else { maxFacingDelta = Mathf.Max(maxFacingDelta, Quaternion.Angle(facing, Boss.Movement.PhysicalRotation)); maxAimDelta = Mathf.Max(maxAimDelta, Vector3.Distance(aim, currentAim)); }
            }
            executing = active;
            if (Brain.CurrentPatternId != "" && Brain.CurrentPatternId != selected)
            { selections++; events.Add(new { caseId, game = Time.time, pattern = Brain.CurrentPatternId, pressure = Pressure, lease = Boss.LeaseVersion }); }
            selected = Brain.CurrentPatternId;
            if (Time.time >= sampleAt)
            {
                sampleAt = Time.time + .1f; var context = Brain.Context;
                float tangent = typeof(CrustaspikanCombatContext).GetField("TangentialSpeed") is FieldInfo field ? (float)field.GetValue(context)
                    : (context.Velocity - context.Delta.normalized * context.RadialSpeed).magnitude;
                maxTangent = Mathf.Max(maxTangent, tangent); if (Pressure) pressureSamples++;
                var candidates = encounter.Settings.patterns.Where(p => (bool)Call("Eligible", p)).ToArray();
                float total = candidates.Sum(p => (float)Call("Weight", p));
                if (Pressure && candidates.Any(p => p.family == "turn" || p.family == "rear" || p.family == "stomp" || p.family == "area")) eligiblePressure++;
                samples.Add(new { caseId, game = Time.time, player = V(player.transform.position), boss = V(Boss.transform.position),
                    velocity = V(encounter.PlayerVelocity), context.Distance, context.RadialSpeed, tangent, pressure = Pressure,
                    Brain.CurrentPatternId, Brain.PatternCount, Brain.State, lease = Boss.LeaseVersion, handle = Boss.AnimationBridge.CurrentMotionHandle,
                    role = Boss.AnimationBridge.CurrentMotionRole.ToString(), active, input = V(direction), moveRead = new[] { facade.MoveValue.x, facade.MoveValue.y },
                    candidates = candidates.Select(p => new { p.id, p.family, weight = (float)Call("Weight", p), probability = total > 0 ? (float)Call("Weight", p) / total : 0 }).ToArray(),
                    impacts = basic.ImpactCount, bodyDamage = basic.DamageCount + Brain.Composite.DamageCount, hp = player.Health.CurrentHp });
            }
            yield return null;
        }
        Send(Vector3.zero); Brain.ReviewMode = true;
        Check(maxTangent > 2f, "Input did not produce actual tangential movement.");
        if (baseline) Check(pressureSamples == 0, "Baseline unexpectedly adapted.");
        else
        {
            if (radius <= 10f) Check(pressureSamples > 5 && eligiblePressure > 0, "Actual near-circle pressure/eligible counter candidates missing.");
            Check(maxFacingDelta < .1f && maxAimDelta < .001f, "Accepted attack tracked the moving player.");
        }
        cases.Add(new { id = caseId, status = baseline ? "REPRODUCED" : "PASS", radius, sign, seconds = Time.time - start,
            pressureSamples, eligiblePressure, maxTangent, selections, acceptedPatterns = Brain.PatternCount, impacts = basic.ImpactCount,
            bodyHitCount = basic.DamageCount + Brain.Composite.DamageCount - damageBefore, totalHpLost = hp - player.Health.CurrentHp,
            maxFacingDelta, maxAimDelta, noGuaranteedHitClaim = true }); Write("RUNNING", null);
    }
    void Send(Vector3 direction, bool shift = false, bool mode = false)
    {
        var keys = new List<Key>();
        float x = Vector3.Dot(direction.normalized, player.Movement.ResolveMoveDirection(Vector2.right));
        float y = Vector3.Dot(direction.normalized, player.Movement.ResolveMoveDirection(Vector2.up));
        if (x > .35f) keys.Add(Key.D); if (x < -.35f) keys.Add(Key.A); if (y > .35f) keys.Add(Key.W); if (y < -.35f) keys.Add(Key.S);
        if (shift) keys.Add(Key.LeftShift); if (mode) keys.Add(Key.X);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
        Vector3 screen = Camera.main != null ? Camera.main.WorldToScreenPoint(Boss.transform.position) : Vector3.zero;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(screen.x, screen.y) });
    }
    void Warp(Vector3 position)
    { var cc = player.CharacterController; bool enabled = cc.enabled; cc.enabled = false; player.transform.position = position; cc.enabled = enabled; player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms(); }
    void EvadeStarted(PlayerEvadeType type) { evadeStarts++; evadeOrigin = player.transform.position; events.Add(new { caseId, evade = "start", type = type.ToString(), id = evade.ActiveExecutionId, game = Time.time }); }
    void EvadeEnded(PlayerEvadeType type) { evadeEnds++; evadeMoved = FlatDistance(evadeOrigin, player.transform.position); events.Add(new { caseId, evade = "end", type = type.ToString(), completed = evade.LastEndWasCompleted, moved = evadeMoved, game = Time.time }); }
    void ReleaseInput()
    {
        if (evade != null) { evade.OnEvadeStarted -= EvadeStarted; evade.OnEvadeEnded -= EvadeEnded; }
        if (facade != null && facade.RuntimeAsset != null) facade.RuntimeAsset.devices = oldDevices;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        keyboard = null; mouse = null; if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent(); if (oldMouse != null && oldMouse.added) oldMouse.MakeCurrent();
    }
    void Write(string status, string error) => File.WriteAllText(Path.Combine(output, "Play01/result.json"), JsonConvert.SerializeObject(new {
        status, error, stage = "D3", baseline, utc = DateTime.UtcNow, cases, samples, events, PlayerBuild = "FORBIDDEN", userManualFeel = "NOT_RUN",
        scope = "Actual keyboard/mouse input, physical relative movement, real decisions and strikes; automatic feel sample, not manual approval."
    }, Formatting.Indented));
    void Finish(string status, string error)
    {
        if (finished) return; finished = true;
        try { Write(status, error); } finally { ReleaseInput(); if (encounter != null && encounter.Brain != null) encounter.Brain.ReviewMode = true; }
    }
    void OnDestroy() => ReleaseInput();
}
