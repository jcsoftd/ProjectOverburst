using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class CrustaspikanTempoVerifier
{
    public static string VerifyNative(string output)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        var report = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(output,"applied-assets.json")));
        var settings = Resources.Load<CrustaspikanEncounterSettings>("Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan");
        if (settings == null || !settings.Validate(out _)) throw new InvalidOperationException("Native encounter validation failed.");
        var profile = settings.materials.actorDefinition.ActorPrefab.GetComponent<EnemyAnimationBridge>().PlaybackProfile;
        if (!profile.Validate(out _)) throw new InvalidOperationException("Native motion profile invalid.");
        int checks=2;
        foreach (var asset in settings.materials.attacks.Select(m => (UnityEngine.Object)m.ability)
            .Concat(new UnityEngine.Object[]{settings,profile}))
        {
            if (asset.name != Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(asset)))
                throw new InvalidOperationException("Native asset name changed: " + asset.name);
            checks++;
        }
        foreach (var row in report["attacks"])
        {
            var material=settings.FindMaterial((string)row["key"]);
            float speed=(float)row["speed"];
            if (!material.IsValid || !material.ability.UsesPacedTimeline
                || Mathf.Abs(material.ability.ResolveFirstImpactTime(speed)-(float)row["before"]["first"])>.0005f
                || Mathf.Abs(material.ability.ResolveLastImpactTime(speed)-(float)row["before"]["last"])>.0005f
                || material.ability.ResolveExecutionDuration(speed)>=(float)row["before"]["total"]-.02f)
                throw new InvalidOperationException("Native material pacing failed: "+material.name);
            checks++;
        }
        if (Mathf.Abs(settings.approachSpeed-3f)>.001f) throw new InvalidOperationException("Approach speed mismatch.");
        checks++;
        foreach (string id in new[]{"Turn90Left","Turn90Right","Turn180Left","Turn180Right"})
        {
            var b=profile.Find(id); float expected=id.Contains("180")?1.8f:1.2f;
            if (Mathf.Abs(profile.ResolveClip(b).length/b.rate-expected)>.001f || b.settleSeconds>.051f)
                throw new InvalidOperationException("Turn pacing mismatch: "+id);
            checks++;
        }
        File.WriteAllText(Path.Combine(output,"native-verification.json"),JsonConvert.SerializeObject(new{status="PASS_NATIVE",checks,utc=DateTime.UtcNow},Formatting.Indented));
        return "Native tempo checks passed: "+checks;
    }
    public static string StopPlay(string output)
    {
        string owned=Path.GetFullPath(Path.Combine(output,"Play01/IsolatedSave"));
        if (!EditorApplication.isPlaying || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)!=owned
            || SessionState.GetString("Overburst.CrustaspikanTempoAudit.output","")!=output)
            throw new InvalidOperationException("Owned Play required before stopping.");
        if (SessionState.GetBool("Overburst.CrustaspikanTempoAudit.stopPending",false))
            throw new InvalidOperationException("Stop already pending; inspect current state.");
        var completed=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(output,"Play01/result.json")));
        if ((string)completed["status"]=="RUNNING") throw new InvalidOperationException("Observation still running.");
        SessionState.SetBool("Overburst.CrustaspikanTempoAudit.stopPending",true);
        double notBefore=EditorApplication.timeSinceStartup+1d;
        bool encounterExited=false;
        EditorApplication.CallbackFunction stop=null;
        stop=()=>{
            if (EditorApplication.timeSinceStartup<notBefore)return;
            if (!encounterExited && EditorApplication.isPlaying && Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)==owned)
            {
                try
                {
                    UnityEngine.Object.FindFirstObjectByType<CrustaspikanTempoPlayObserver>()?.ReleaseOwnedResources();
                    UnityEngine.Object.FindFirstObjectByType<CrustaspikanEncounter>()?.Exit(true);
                }
                finally { encounterExited=true; notBefore=EditorApplication.timeSinceStartup+.2d; }
                return;
            }
            EditorApplication.update-=stop;
            SessionState.SetBool("Overburst.CrustaspikanTempoAudit.stopPending",false);
            if (!EditorApplication.isPlaying || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)!=owned)return;
            UnityEngine.Object.FindFirstObjectByType<CrustaspikanTempoPlayObserver>()?.ReleaseOwnedResources();
            EditorApplication.isPlaying=false;
        };
        EditorApplication.update+=stop;
        return "Stop scheduled once from Editor after the completed player observer returned.";
    }
    public static string StartPlay(string output)
    {
        string owned = Path.GetFullPath(Path.Combine(output, "Play01/IsolatedSave"));
        if (!EditorApplication.isPlaying || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) != owned
            || !UnityEditor.SessionState.GetBool("Overburst.CrustaspikanTempoAudit.pending", false))
            throw new InvalidOperationException("Own isolated Play required.");
        if (UnityEngine.Object.FindFirstObjectByType<CrustaspikanTempoPlayObserver>() != null || File.Exists(Path.Combine(output, "Play01/result.json")))
            throw new InvalidOperationException("Probe exists or finished: do not restart.");
        var root = new GameObject("Owned Crustaspikan Tempo Observer");
        UnityEngine.Object.DontDestroyOnLoad(root);
        root.AddComponent<CrustaspikanTempoPlayObserver>().Initialize(output);
        return "Tempo observation started; assets unchanged, live game clock and existing camera.";
    }
    public static string StartShutdownCheck(string output)
    {
        StartPlay(output);
        UnityEngine.Object.FindFirstObjectByType<CrustaspikanTempoPlayObserver>().ShutdownOnly=true;
        return "Short shutdown check started through actual boss portal and weak-spit summons.";
    }
}

[DefaultExecutionOrder(20000)]
public sealed class CrustaspikanTempoPlayObserver : MonoBehaviour
{
    public bool ShutdownOnly { get; set; }
    string output, segment = "boot";
    float bootAt, stageAt, sampleAt, captureAt, naturalAt;
    int stage, turnIndex, frame, attacksRequested, evadesRequested;
    bool finished, turnStarted, wasTurning, phaseRequested, hubTransitionRequested;
    float nextBootSample;
    PlayerActorRuntime player;
    CrustaspikanEncounter encounter;
    Gamepad pad, oldPad;
    Keyboard keyboard, oldKeyboard;
    RenderTexture rt;
    Texture2D pixels;
    Vector3 previousPlayer, previousBoss;
    float previousTime;
    Vector2 requestedStick;
    readonly List<object> samples = new List<object>();
    readonly List<object> images = new List<object>();
    readonly List<object> turns = new List<object>();
    readonly List<object> events = new List<object>();
    readonly List<object> bootStates = new List<object>();
    readonly float[] turnAngles = { -20f, 90f, 180f };
    readonly FieldInfo requestField = typeof(EnemyAnimationBridge).GetField("ownedRequest", BindingFlags.Instance | BindingFlags.NonPublic);
    object playerBaseline;
    Quaternion turnOrigin;
    float turnGameAt, turnRealAt;

    public void Initialize(string directory)
    {
        output = directory; bootAt = stageAt = Time.realtimeSinceStartup;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Application.runInBackground = true;
        oldPad = Gamepad.current; oldKeyboard = Keyboard.current;
        pad = InputSystem.AddDevice<Gamepad>("CrustaspikanTempoAuditPad");
        keyboard = InputSystem.AddDevice<Keyboard>("CrustaspikanTempoAuditKeyboard");
        rt = new RenderTexture(960, 540, 24); rt.Create();
        pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        Directory.CreateDirectory(Path.Combine(output, "Play01/Frames"));
        Write("RUNNING", null);
    }
    void Update()
    {
        if (finished) return;
        try
        {
            if (Time.realtimeSinceStartup - bootAt > 280f) throw new TimeoutException("Audit overall deadline.");
            Tick();
        }
        catch (Exception error) { Finish("FAIL_OBSERVATION", error.ToString()); }
    }
    void Tick()
    {
        if (stage == 0)
        {
            var flow = PersistentSceneFlow.Instance;
            var host = CrustaspikanEncounterHost.Current;
            player = PlayerContext.Instance?.CurrentActor;
            if (Time.realtimeSinceStartup >= nextBootSample)
            {
                nextBootSample = Time.realtimeSinceStartup + 1f;
                bootStates.Add(new { real = Time.realtimeSinceStartup - bootAt, subScene = flow?.CurrentSubSceneName,
                    switching = flow?.IsSwitching, playerReady = player != null, hostReady = host != null,
                    canEnter = host?.CanEnter, entranceReady = host?.Entrance != null,
                    scenes = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                        .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name).ToArray() });
                Write("RUNNING", null);
            }
            if (!hubTransitionRequested && player != null && flow != null && !flow.IsSwitching
                && flow.CurrentSubSceneName == PersistentSceneFlow.MainSceneName)
            {
                var hub = UnityEngine.Object.FindObjectsByType<HubScenePortal>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => p.TargetSceneName == PersistentSceneFlow.HideoutSceneName && p.IsInteractionAvailable(player));
                if (hub != null)
                {
                    Warp(hub.transform.position + Vector3.back);
                    if (hub.TryInteract(player) != InteractionExecutionResult.StartedTransition)
                        throw new InvalidOperationException("Actual main-to-hideout portal rejected.");
                    hubTransitionRequested = true; Log("actual-main-to-hideout-portal");
                    return;
                }
            }
            if (host == null || !host.CanEnter || host.Entrance == null || player == null)
            { if (Time.realtimeSinceStartup - stageAt > 80f) throw new TimeoutException("Actual hideout/player not ready."); return; }
            player = PlayerContext.Instance.CurrentActor;
            Warp(host.Entrance.transform.position + Vector3.back);
            if (host.Entrance.TryInteract(player) != InteractionExecutionResult.StartedTransition) throw new InvalidOperationException("Actual entry rejected.");
            encounter = host.ActiveEncounter; encounter.Brain.ReviewMode = true; stage = 1; stageAt = Time.realtimeSinceStartup; return;
        }
        if (stage == 1)
        {
            if (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); return; }
            if (Time.realtimeSinceStartup - stageAt < 1.5f) return;
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            playerBaseline = new { player.Health.CurrentHp, player.Health.MaxHp, walk = player.Movement.WalkMoveSpeed, run = player.Movement.RunMoveSpeed,
                currentWeapon = player.Equipment.CurrentWeaponData != null ? player.Equipment.CurrentWeaponData.name : "none",
                evade = player.GetComponent<PlayerEvadeController>()?.Profile?.combatDodge,
                camera = new { distance = QuarterViewCamera.ActiveInstance?.CurrentDistance, effective = QuarterViewCamera.ActiveInstance?.EffectiveDistance,
                    yaw = QuarterViewCamera.ActiveInstance?.CurrentYaw, pitch = QuarterViewCamera.ActiveInstance?.CurrentPitch } };
            encounter.Brain.ReviewMode = true;
            if (ShutdownOnly)
            {
                if (!encounter.Brain.StartPatternForReview("weak_spit")) throw new InvalidOperationException("Shutdown summon pattern rejected.");
                stage=6; segment="shutdown-with-summons"; stageAt=Time.realtimeSinceStartup; return;
            }
            PrepareTurn(); stage = 2; return;
        }
        if (stage==6)
        {
            Observe();
            if (encounter.AliveAdds>0 && encounter.Brain.CurrentPatternId=="")
            { Log("PASS-live-summons-before-normal-exit:"+encounter.AliveAdds); Finish("OBSERVED_SHUTDOWN_WITH_LIVE_SUMMONS",null); }
            else if (Time.realtimeSinceStartup-stageAt>20f) throw new TimeoutException("Shutdown summon preparation.");
            return;
        }
        if (stage == 2)
        {
            bool turning = encounter.Brain.Actor.Movement.IsOwnedTurning;
            if (turning && !turnStarted) { turnStarted = true; turnGameAt = Time.time; turnRealAt = Time.realtimeSinceStartup; }
            if (turnStarted && !turning && wasTurning)
            {
                float residual = Vector3.Angle(encounter.Brain.Actor.transform.forward, player.transform.position - encounter.Brain.Actor.transform.position);
                turns.Add(new { requestedDegrees = turnAngles[turnIndex], seconds = Time.time - turnGameAt, realSeconds = Time.realtimeSinceStartup - turnRealAt, residual });
                turnIndex++;
                if (turnIndex < turnAngles.Length) PrepareTurn();
                else
                {
                    BeginCancelCheck();
                }
            }
            if (Time.realtimeSinceStartup - stageAt > 9f) throw new TimeoutException("Controlled turn " + turnIndex);
            wasTurning = turning; Observe(); return;
        }
        if (stage == 4 || stage == 5) { TickCommitChecks(); Observe(); return; }
        float elapsed = Time.realtimeSinceStartup - naturalAt;
        string next = elapsed < 20f ? "near-front" : elapsed < 40f ? "circle-near" : elapsed < 55f ? "keep-distance" : "phase-two-close";
        if (next != segment) { segment = next; Log("segment-start"); }
        if (elapsed >= 75f) { Finish("OBSERVED_LIVE_PLAY", null); return; }
        if (segment == "phase-two-close" && !phaseRequested)
        {
            phaseRequested = true;
            if (encounter.Brain.Phase == 1)
            {
                encounter.Brain.Actor.Health.TakeDamage(new DamageInfo(encounter.Brain.Actor.Health.CurrentHp * .6f,
                    encounter.Brain.Actor.transform.position, source: null, triggersOnHitEffects: false, suppressDefaultHitVfx: true));
                Log("diagnostic-damage-to-request-phase-two");
            }
        }
        var boss = encounter.Brain.Actor;
        Vector3 radial = player.transform.position - boss.transform.position; radial.y = 0;
        float distance = radial.magnitude;
        Vector3 direction;
        if (segment == "near-front") direction = boss.transform.forward * 5.5f + boss.transform.position - player.transform.position;
        else
        {
            float targetRadius = segment == "keep-distance" ? 13f : 5.5f;
            Vector3 correction = radial.normalized * Mathf.Clamp((targetRadius - distance) * .9f, -1f, 1f);
            direction = segment == "circle-near" || segment == "keep-distance"
                ? Vector3.Cross(Vector3.up, radial.normalized) * .8f + correction : correction;
        }
        direction.y = 0;
        requestedStick = direction.magnitude > .15f ? ToInput(direction.normalized) : Vector2.zero;
        Vector3 toward = boss.transform.position - player.transform.position; toward.y = 0;
        bool attack = segment == "near-front" && elapsed % 1.5f < .12f && distance < 7f;
        var padState = new GamepadState { leftStick = requestedStick, rightStick = ToInput(toward.normalized) };
        if (attack) padState = padState.WithButton(GamepadButton.West);
        InputSystem.QueueStateEvent(pad, padState);
        if (attack && !attackHeld) attacksRequested++;
        attackHeld = attack;
        bool evade = segment != "near-front" && elapsed % 7f < .08f;
        InputSystem.QueueStateEvent(keyboard, evade ? new KeyboardState(Key.LeftShift) : new KeyboardState());
        if (evade && !evadeHeld) evadesRequested++;
        evadeHeld = evade;
        Observe();
    }
    int countBefore;
    bool cancelTargetMoved, attackAccepted, lockTargetMoved;
    Quaternion acceptedFacing;
    float maxCommittedFacingDelta;
    readonly FieldInfo cooldownField = typeof(CrustaspikanEncounterBrain).GetField("cooldowns", BindingFlags.Instance | BindingFlags.NonPublic);
    void BeginCancelCheck()
    {
        segment = "cancel-before-commit"; stage = 4; stageAt = Time.realtimeSinceStartup;
        countBefore = encounter.Brain.PatternCount;
        var boss = encounter.Brain.Actor;
        Warp(boss.transform.position + boss.Movement.PhysicalRotation * Quaternion.Euler(0,90,0) * Vector3.forward * 7f + Vector3.up * .1f);
        if (!encounter.Brain.StartPatternForReview("left_light")) throw new InvalidOperationException("Review pattern rejected.");
    }
    void TickCommitChecks()
    {
        var brain = encounter.Brain; var boss = brain.Actor;
        if (Time.realtimeSinceStartup - stageAt > 12f) throw new TimeoutException("Commit check stage " + stage);
        var cooldowns = (Dictionary<string,float>)cooldownField.GetValue(brain);
        if (stage == 4)
        {
            if (boss.Movement.IsOwnedTurning && !cancelTargetMoved)
            {
                cancelTargetMoved = true;
                Vector3 delta = player.transform.position - boss.transform.position;
                Warp(boss.transform.position + Quaternion.Euler(0,40,0) * delta);
            }
            if (cancelTargetMoved && brain.CurrentPatternId == "")
            {
                if (brain.PatternCount != countBefore || cooldowns.ContainsKey("left_light"))
                    throw new InvalidOperationException("Uncommitted cancellation consumed cooldown or pattern count.");
                Log("PASS-uncommitted-cancellation-no-cooldown");
                stage = 5; segment = "committed-facing-lock"; stageAt = Time.realtimeSinceStartup;
                Warp(boss.transform.position + boss.Movement.PhysicalRotation * Vector3.forward * 5.5f + Vector3.up * .1f);
                if (!brain.StartPatternForReview("left_light")) throw new InvalidOperationException("Review attack rejected.");
            }
            return;
        }
        var executor = boss.GetComponent<EnemyBossMaterialExecutor>();
        if (executor.IsExecuting && executor.CurrentMaterial?.runtimeClip.name == "LeftHandAttack")
        {
            if (!attackAccepted) { attackAccepted = true; acceptedFacing = boss.Movement.PhysicalRotation; }
            maxCommittedFacingDelta = Mathf.Max(maxCommittedFacingDelta, Quaternion.Angle(acceptedFacing,boss.Movement.PhysicalRotation));
            if (!lockTargetMoved && executor.NormalizedTime > .15f)
            {
                lockTargetMoved = true;
                Warp(boss.transform.position + Quaternion.Euler(0,40,0) * (player.transform.position-boss.transform.position));
            }
        }
        if (attackAccepted && !executor.IsExecuting && brain.CurrentPatternId == "")
        {
            if (!lockTargetMoved || maxCommittedFacingDelta > .5f || brain.PatternCount != countBefore+1
                || !cooldowns.TryGetValue("left_light",out float until) || until <= Time.time)
                throw new InvalidOperationException("Committed attack facing/count/cooldown contract failed.");
            Log("PASS-accepted-attack-facing-lock-and-cooldown");
            Warp(boss.transform.position + boss.Movement.PhysicalRotation * Vector3.forward * 5.5f + Vector3.up * .1f);
            brain.ReviewMode = false; naturalAt = Time.realtimeSinceStartup; segment = "near-front"; stage = 3;
            Log("natural-start");
        }
    }
    bool attackHeld, evadeHeld;
    Vector2 ToInput(Vector3 direction)
    {
        return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(direction, player.Movement.ResolveMoveDirection(Vector2.right)),
            Vector3.Dot(direction, player.Movement.ResolveMoveDirection(Vector2.up))), 1f);
    }
    void PrepareTurn()
    {
        segment = "turn-" + turnAngles[turnIndex]; stageAt = Time.realtimeSinceStartup; turnStarted = wasTurning = false;
        turnOrigin = encounter.Brain.Actor.Movement.PhysicalRotation;
        var destination = encounter.Brain.Actor.transform.position + turnOrigin * Quaternion.Euler(0, turnAngles[turnIndex], 0) * Vector3.forward * 7f;
        Warp(destination + Vector3.up * .1f); Log("controlled-turn-start");
    }
    void Warp(Vector3 position)
    {
        var cc = player.CharacterController; bool enabled = cc.enabled; cc.enabled = false;
        player.transform.position = position; cc.enabled = enabled; player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms();
    }
    void Observe()
    {
        var brain = encounter.Brain; var boss = brain.Actor; float now = Time.realtimeSinceStartup;
        if (now >= sampleAt)
        {
            sampleAt = now + .05f;
            var bridge = boss.AnimationBridge;
            var request = (EnemyMotionRequest)requestField.GetValue(bridge);
            var executor = boss.GetComponent<EnemyBossMaterialExecutor>(); var compound = brain.Composite;
            float dt = Time.time - previousTime;
            Vector3 playerVelocity = dt > .001f ? (player.transform.position - previousPlayer) / dt : Vector3.zero;
            Vector3 bossVelocity = dt > .001f ? (boss.transform.position - previousBoss) / dt : Vector3.zero;
            bridge.TryReadMotion(bridge.CurrentMotionHandle, out var pose);
            samples.Add(new { real = now - bootAt, game = Time.time, segment, phase = brain.Phase, pattern = brain.CurrentPatternId, brain.State,
                motion = request.MotionId, role = bridge.CurrentMotionRole.ToString(), motionState = bridge.CurrentMotionState.ToString(), progress = pose.Normalized,
                turning = boss.Movement.IsOwnedTurning, targetAngle = Vector3.Angle(boss.transform.forward, player.transform.position - boss.transform.position),
                distance = Vector3.Distance(boss.transform.position, player.transform.position), playerSpeed = new Vector2(playerVelocity.x,playerVelocity.z).magnitude,
                bossSpeed = new Vector2(bossVelocity.x,bossVelocity.z).magnitude, player = new[] { player.transform.position.x, player.transform.position.z },
                boss = new[] { boss.transform.position.x, boss.transform.position.z }, facadeMove = new[] { player.GetComponent<PlayerInputFacade>().MoveValue.x, player.GetComponent<PlayerInputFacade>().MoveValue.y },
                evade = player.GetComponent<PlayerEvadeController>()?.IsEvading ?? false, attack = player.GetComponent<MeleeRuntime>()?.IsAttackInProgress ?? false,
                heavyAttack = player.GetComponent<MeleeRuntime>()?.IsHeavyAttackInProgress ?? false,
                material = executor.IsExecuting ? executor.CurrentMaterial?.runtimeClip.name : compound.IsExecuting ? compound.CurrentMaterial?.runtimeClip.name : "",
                materialProgress = executor.IsExecuting ? executor.NormalizedTime : compound.NormalizedTime,
                damage = executor.DamageCount + compound.DamageCount, patternsStarted = brain.PatternCount, health = player.Health.CurrentHp,
                bossHp = boss.Health.CurrentHp, timeScale = Time.timeScale, deltaTime = Time.deltaTime, captureDelta = Time.captureDeltaTime });
            previousPlayer = player.transform.position; previousBoss = boss.transform.position; previousTime = Time.time;
        }
        if (now >= captureAt)
        {
            captureAt = now + .25f;
            var view = Camera.main; if (view == null) throw new InvalidOperationException("Actual game camera missing.");
            var oldTarget = view.targetTexture; var oldActive = RenderTexture.active;
            try
            {
                view.targetTexture = rt; view.Render(); RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0,0,960,540),0,0); pixels.Apply();
                string file = "Frames/" + frame.ToString("D5") + ".jpg";
                File.WriteAllBytes(Path.Combine(output,"Play01",file), pixels.EncodeToJPG(87));
                images.Add(new { file, segment, real = now - bootAt, game = Time.time }); frame++;
            }
            finally { view.targetTexture = oldTarget; RenderTexture.active = oldActive; }
        }
        if (now - lastWrite > 3f) { lastWrite = now; Write("RUNNING", null); }
    }
    float lastWrite;
    void Log(string text) => events.Add(new { real = Time.realtimeSinceStartup - bootAt, game = Time.time, segment, text });
    void Write(string status, string error)
    {
        File.WriteAllText(Path.Combine(output, "Play01/result.json"), JsonConvert.SerializeObject(new { status, error, utc = DateTime.UtcNow,
            playerBaseline, bootStates, turns, events, samples, images, attacksRequested, evadesRequested,
            maxCommittedFacingDelta, stoppedFromPlayerCallback = false, actualCamera = true, cameraOnlyRendering = true, overlayHudIncluded = false, forcedCaptureDelta = false,
            control = "Synthetic devices through actual input facade; natural boss selection; controlled turns and diagnostic phase damage are labeled",
            assetsModified = false, userManualFeel = "NOT_RUN", PlayerBuild = "NOT_RUN_NO_APPROVAL" }, Formatting.Indented));
    }
    void Finish(string status, string error)
    {
        if (finished) return; finished = true;
        try { Write(status,error); }
        finally
        {
            if (pad != null && pad.added) InputSystem.QueueStateEvent(pad,new GamepadState());
            if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            if (encounter != null && encounter.Brain != null) encounter.Brain.ReviewMode = true;
            // The caller stops Play from Editor after this player callback has returned.
        }
    }
    public void ReleaseOwnedResources() => Cleanup();
    void Cleanup()
    {
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        pad = null; keyboard = null;
        if (oldPad != null && oldPad.added) oldPad.MakeCurrent();
        if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
        if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        if (pixels != null) { Destroy(pixels); pixels = null; }
    }
    void OnDestroy() { Cleanup(); }
}
