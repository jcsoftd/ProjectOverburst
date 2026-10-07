using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Uses actual spawned actors, physical turns, animation clocks, portal flow and combat executors.
[InitializeOnLoad]
public static class CrustaspikanMotionPlaybackVerifier
{
    const string Key = "Overburst.CrustaspikanMotionPlaybackVerifier.";
    sealed class Plan
    {
        public string output, phase, previousStart, input, suite;
        public JArray scenes;
        public bool background, enabledFlow;
        public float capture, scale, fixedStep;
        public double deadline;
    }
    static Plan plan;
    static readonly JArray checks = new JArray();
    static readonly List<Object> owned = new List<Object>();
    static EnemyMotor host;
    static PlayerActorRuntime player;
    static CrustaspikanEncounter room;
    static EnemyActor actor;
    static EnemyBossMaterialExecutor basic;
    static EnemyBossCompositePatternExecutor composite;
    static EnemyMotionPlaybackProfile profile;
    static EnemyLocomotionAnimator loco;
    static Camera camera;
    static RenderTexture rt;
    static Texture2D pixels;
    static string activeCase;
    static int captureIndex;
    static JObject verificationHashes;
    static Stack<IEnumerator> runningRoutines;
    static string Account => Path.Combine(plan.output, "Account");
    static bool OwnPlay => plan != null && string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);
    static JArray Scenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount }));
    static void SavePlan() => SessionState.SetString(Key + "plan", JsonConvert.SerializeObject(plan));
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(activeCase + ": " + message); }
    static void Record(string id, object detail = null)
    { checks.Add(new JObject { ["id"] = id, ["status"] = "PASS", ["detail"] = detail != null ? JToken.FromObject(detail) : JValue.CreateNull() }); Write("RUNNING", null); }
    static void Write(string status, string error)
    { File.WriteAllText(Path.Combine(plan.output, "result.json"), new JObject { ["status"] = status, ["activeCase"] = activeCase, ["checks"] = checks, ["hashes"] = verificationHashes, ["error"] = error, ["utc"] = DateTime.UtcNow }.ToString()); }
    static CrustaspikanMotionPlaybackVerifier()
    {
        string saved = SessionState.GetString(Key + "plan", ""); if (!string.IsNullOrEmpty(saved)) plan = JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Changed;
    }
    public static string Start(string directory, bool enabledFlow = false, string suite = null)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle(); Require(plan == null && !EditorUtility.scriptCompilationFailed, "No pending verification/compile error allowed.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Require(!Directory.Exists(directory), "Fresh evidence directory required."); Directory.CreateDirectory(directory);
        verificationHashes = CrustaspikanMotionPlaybackBuilder.VerificationHashes();
        File.WriteAllText(Path.Combine(directory, "source-hashes.json"), verificationHashes.ToString());
        plan = new Plan { output = directory, phase = "booting", enabledFlow = enabledFlow, suite = suite, scenes = Scenes(), previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            input = InputSystem.settings.backgroundBehavior.ToString(), background = Application.runInBackground, capture = Time.captureDeltaTime, scale = Time.timeScale, fixedStep = Time.fixedDeltaTime, deadline = EditorApplication.timeSinceStartup + 1500 };
        checks.Clear(); SavePlan(); File.WriteAllText(Path.Combine(directory, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        Application.runInBackground = true; IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return "Actual boss motion verification accepted.";
    }
    static void Tick()
    {
        if (plan == null) return;
        if (plan.phase == "deferred") return;
        if (plan.phase == "returning") { ReturnEditor(); return; }
        if (EditorApplication.timeSinceStartup > plan.deadline) { Finish("Verification deadline."); return; }
        if (plan.phase != "booting" || !EditorApplication.isPlaying || !OwnPlay) return;
        EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + "reloadLocked", true);
        verificationHashes = JObject.Parse(File.ReadAllText(Path.Combine(plan.output, "source-hashes.json")));
        plan.phase = "running"; SavePlan(); var root = new GameObject("Owned Crustaspikan motion verifier"); owned.Add(root); host = root.AddComponent<EnemyMotor>();
        host.StartCoroutine(Guarded(Run()));
    }
    static IEnumerator Guarded(IEnumerator first)
    {
        var stack = runningRoutines = new Stack<IEnumerator>(); stack.Push(first);
        while (stack.Count != 0)
        {
            object value = null; Exception error = null; bool more = false;
            try { more = stack.Peek().MoveNext(); if (more) value = stack.Peek().Current; } catch (Exception e) { error = e; }
            if (error != null) { string cleanup = DisposeRoutines(); Finish(error + cleanup); yield break; }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (value is IEnumerator nested) { stack.Push(nested); continue; }
            yield return value;
        }
        Finish(null);
    }
    static string DisposeRoutines()
    {
        string errors = "";
        if (runningRoutines != null) while (runningRoutines.Count > 0)
            try { (runningRoutines.Pop() as IDisposable)?.Dispose(); } catch (Exception e) { errors += "\nCleanup: " + e; }
        runningRoutines = null; return errors;
    }
    static IEnumerator Wait(Func<bool> predicate, float seconds, string message, bool unscaled = false)
    {
        float Clock() => unscaled ? Time.unscaledTime : Time.time;
        float deadline = Clock() + seconds; double watchdog = EditorApplication.timeSinceStartup + Math.Max(30, seconds * 5);
        while (!predicate() && Clock() < deadline && EditorApplication.timeSinceStartup < watchdog) yield return null;
        Require(predicate(), message);
    }
    static EnemyAnimationBridge Bridge => actor.AnimationBridge;
    static void Warp(Vector3 p)
    { bool enabled = player.CharacterController.enabled; player.CharacterController.enabled = false; player.transform.position = p; player.CharacterController.enabled = enabled; player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms(); }
    static void Pose(Vector3 p, Quaternion q)
    { var body = actor.GetComponent<Rigidbody>(); body.position = p; body.rotation = q; actor.transform.SetPositionAndRotation(p, q); Physics.SyncTransforms(); }
    static void Fresh(bool manual = true)
    {
        room.enabled = false; room.Restart(); room.Brain.ReviewMode = true;
        actor = room.Brain.Actor; basic = actor.GetComponent<EnemyBossMaterialExecutor>(); composite = actor.GetComponent<EnemyBossCompositePatternExecutor>(); loco = actor.GetComponent<EnemyLocomotionAnimator>();
        Require(Bridge.ConfigureMotionPlayback(profile) && Bridge.UsesOwnedMotion, "Owned profile rejected: " + Bridge.MotionConfigurationError);
        actor.AI.enabled = false; actor.Health.SetMaxHp(1000000, true); actor.Movement.StopMovement(); loco.enabled = !manual;
        Pose(room.ArenaCenter + Vector3.up * .05f, Quaternion.identity); Warp(actor.transform.position + Vector3.forward * 8);
        foreach (var c in actor.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(c, player.CharacterController);
        actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }
    static EnemyMotionHandle Begin(string id, EnemyMotionRole role, float rate = 1, bool hold = false, int group = 0, bool external = false)
    {
        var request = new EnemyMotionRequest { Owner = host, MotionId = id, Role = role, Rate = rate, HoldLastPose = hold, GroupId = group, ExternalCompletion = external };
        Require(Bridge.TryBeginMotion(request, out var handle, out var reason), "Begin rejected: " + id + " " + reason); return handle;
    }
    static void SetField(object obj, string name, object value)
    { var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); Require(f != null, "Missing test seam " + name); f.SetValue(obj, value); }
    static void MakeCapture()
    {
        var go = new GameObject("Owned motion observation camera"); owned.Add(go); camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 10; camera.farClipPlane = 160; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .055f, .065f);
        camera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
        rt = new RenderTexture(1280, 720, 24); rt.Create(); pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false); owned.Add(rt); owned.Add(pixels);
    }
    static void Capture(string folder)
    {
        var previous = RenderTexture.active;
        try
        {
            camera.transform.position = actor.transform.position + new Vector3(18, 13, 20); camera.transform.LookAt(actor.transform.position + Vector3.up * 4);
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); pixels.Apply();
            string path = Path.Combine(plan.output, "Frames", folder); Directory.CreateDirectory(path); File.WriteAllBytes(Path.Combine(path, (captureIndex++).ToString("D5") + ".png"), pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; }
    }
    static IEnumerator Run()
    {
        activeCase = "BOOT";
        yield return Wait(() => PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching && WorldSessionState.IsHideout && PlayerContext.Instance?.CurrentActor != null
            && PlayerContext.Instance.CurrentActor.CharacterController != null && AccountGameplaySession.Current != null
            && CrustaspikanEncounterHost.Current?.CanEnter == true && CrustaspikanEncounterHost.Current.Entrance != null, 180, "Actual isolated hideout boot failed.", true);
        player = PlayerContext.Instance.CurrentActor; player.PlayerKit.ApplyAuthority(ActorControlAuthority.AI); player.Health.SetMaxHp(1000000, true);
        Time.captureDeltaTime = 1f / 60; Time.timeScale = 1;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var savedProfile = AssetDatabase.LoadAssetAtPath<EnemyMotionPlaybackProfile>(CrustaspikanMotionPlaybackBuilder.ProfilePath);
        profile = savedProfile != null ? Object.Instantiate(savedProfile) : null; if (profile != null) owned.Add(profile);
        Require(profile != null && profile.Validate(out _), "Native draft not ready.");
        var entry = CrustaspikanEncounterHost.Current.Entrance;
        Warp(entry.transform.position + Vector3.back); Require(entry.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Actual portal rejected.");
        room = CrustaspikanEncounterHost.Current.ActiveEncounter; Require(room != null && room.Brain != null, "Actual room/boss missing.");
        if (plan.enabledFlow) { yield return EnabledFlow(); yield break; }
        room.EntranceCinematic?.Skip(); yield return null; Fresh(); MakeCapture();
        if (plan.suite == "turn-1x") { yield return OneXTurn(); yield break; }
        if (plan.suite == "preparation") { yield return Preparation(); yield break; }
        if (plan.suite == "low-sample") { yield return LowSampleAndFlight(); yield break; }
        if (plan.suite == "hit-response") { yield return HitResponse(); yield break; }
        if (plan.suite == "timing") { yield return Timing(); yield break; }
        if (plan.suite == "remaining") { yield return ReactionAndLife(); yield return Timing(); yield return ReactionEdges(); yield return HitResponse(); yield return LegacyDeath(); yield break; }
        yield return Configuration(); yield return Ownership(); yield return Turns(); yield return Continuous(); yield return ReactiveBackstep(); yield return Preparation(); yield return Attacks(); yield return ReactionAndLife(); yield return Timing(); yield return ReactionEdges(); yield return HitResponse(); yield return LegacyDeath();
    }
    static IEnumerator Configuration()
    {
        activeCase = "CFG";
        CrustaspikanMotionPlaybackBuilder.ValidateNative(profile, AssetDatabase.LoadAssetAtPath<AnimatorController>(CrustaspikanMotionPlaybackBuilder.ControllerPath), AssetDatabase.LoadAssetAtPath<GameObject>(CrustaspikanMotionPlaybackBuilder.PrefabPath));
        Record("CFG-01", new { bindings = profile.Bindings.Length });
        CrustaspikanMotionPlaybackBuilder.RequireOptInOff(AssetDatabase.LoadAssetAtPath<GameObject>(CrustaspikanMotionPlaybackBuilder.PrefabPath));
        var native = JObject.Parse(File.ReadAllText(CrustaspikanMotionPlaybackBuilder.Output + "/Evidence/native-draft.json"));
        Require((int)native["attacks"] == 16 && (string)native["status"] == "PASS_NATIVE_DRAFT_OPT_IN_OFF", "Native preservation proof missing."); Record("CFG-02", new { currentAttacks = 16, nativeProof = "native-draft.json" });
        foreach (EnemyMotionConsumers bit in new[] { EnemyMotionConsumers.ContinuousIntent, EnemyMotionConsumers.AttackContext, EnemyMotionConsumers.PreparedAvailability,
            EnemyMotionConsumers.PreparedCommit, EnemyMotionConsumers.CancelSnapshot, EnemyMotionConsumers.ReactionPose, EnemyMotionConsumers.Introduction, EnemyMotionConsumers.DeathAndFreeze })
        {
            var copy = Object.Instantiate(profile);
            try { var so = new SerializedObject(copy); so.FindProperty("validatedConsumers").intValue = (int)(EnemyMotionConsumers.All & ~bit); so.ApplyModifiedPropertiesWithoutUndo();
                Require(!Bridge.ConfigureMotionPlayback(copy) && Bridge.HasInvalidMotionProfile, "Missing consumer accepted: " + bit); }
            finally { Bridge.ConfigureMotionPlayback(profile); Object.Destroy(copy); }
        }
        var bad = Object.Instantiate(profile);
        try { bad.Bindings.First(b => b.motionId == "WalkLeft").state = "MissingPlaybackState"; Require(!Bridge.ConfigureMotionPlayback(bad), "Missing state accepted."); }
        finally { Bridge.ConfigureMotionPlayback(profile); Object.Destroy(bad); }
        Record("CFG-04", new { omittedConsumers = 8, missingStateRejected = true }); yield return null;
    }
    static IEnumerator Ownership()
    {
        activeCase = "OWN"; Fresh();
        int invalidations = 0, notifications = 0; bool gatedBegin = false, gatedHandoff = false, gatedRate = false, notificationBlocked = false; EnemyMotionHandle old = default;
        var request = new EnemyMotionRequest { Owner = host, Role = EnemyMotionRole.Attack, MotionId = "Attack_Attack1", Rate = 1, ExternalCompletion = true,
            OnInvalidated = result => { invalidations++; var nested = new EnemyMotionRequest { Owner = host, Role = EnemyMotionRole.Death, MotionId = "Death", Rate = 1 };
                gatedBegin = !Bridge.TryBeginMotion(nested, out _, out var reason) && reason == EnemyMotionReason.Transitioning;
                gatedHandoff = !Bridge.TryHandoff(result.Handle, nested, out _, out reason) && reason == EnemyMotionReason.Transitioning;
                gatedRate = !Bridge.SetOwnedPlaybackRate(result.Handle, 2); actor.Movement.ReleaseMotionLock(result.Handle); },
            OnTerminated = result => { notifications++; var nested = new EnemyMotionRequest { Owner = host, Role = EnemyMotionRole.Death, MotionId = "Death", Rate = 1 };
                notificationBlocked = !Bridge.TryBeginMotion(nested, out _, out _); } };
        Require(Bridge.TryBeginMotion(request, out old, out _), "Attack owner probe rejected."); actor.Movement.AcquireMotionLock(old, 20);
        yield return new WaitForSeconds(.25f);
        Require(!Bridge.TryBeginMotion(request, out _, out var busy) && busy == EnemyMotionReason.Busy && Bridge.CurrentMotionHandle == old, "Busy restarted owner."); Record("OWN-02");
        var reaction = Begin("ReactionPose", EnemyMotionRole.Reaction, hold: true); actor.Movement.AcquireMotionLock(reaction, 10);
        Require(gatedBegin && gatedHandoff && gatedRate && notificationBlocked && invalidations == 1 && notifications == 1, "Synchronous owner reentry escaped gate."); Record("OWN-07", new { invalidations, notifications });
        Require(!Bridge.SetOwnedPlaybackRate(old, 3) && !actor.Movement.RequestOwnedAttackDisplacement(old, Vector3.forward), "Stale owner writes accepted.");
        actor.Movement.ReleaseMotionLock(old); Require(Bridge.OwnsMotion(reaction) && actor.Movement.IsActionLocked && actor.Animator.speed > .99f, "Old cleanup released reaction."); Record("OWN-05");
        Require(Bridge.GetMotionResult(old).State == EnemyMotionState.Cancelled && Bridge.GetMotionResult(old).Snapshot.Valid, "Cancellation result/snapshot absent.");
        Require(Bridge.TrySampleOwnedPose(reaction, Animator.StringToHash("Base Layer.Material_Death"), .65f), "Actual death pose sample rejected.");
        Require(!Bridge.TryReadMotion(old, out _) && actor.Animator.speed == 0, "Stale read or new clock corruption."); Record("OWN-01", new { sequence = "attack -> manual reaction", oldRejected = true });
        Bridge.CancelMotion(reaction, EnemyMotionReason.OwnerCancelled); var support = Begin("Roar1", EnemyMotionRole.Support);
        yield return Wait(() => Bridge.GetMotionResult(support).IsTerminal, 8, "Normal support never ended."); Require(Bridge.GetMotionResult(support).State == EnemyMotionState.Completed, "Normal support not Completed.");
        var failed = Begin("Roar1", EnemyMotionRole.Support); yield return new WaitForSeconds(.2f); actor.Animator.Play("Locomotion", 0, 0); actor.Animator.Update(0);
        yield return Wait(() => Bridge.GetMotionResult(failed).IsTerminal, 2, "Unexpected exit not detected."); Require(Bridge.GetMotionResult(failed).State == EnemyMotionState.Failed, "Exit masqueraded as success."); Record("OWN-03");
        Fresh(); var material = basic.Collection.attacks.First(a => a.runtimeClip.name == "RightHandAttack"); Require(basic.TryStart(material.ability, 0, player.transform), "Recovery cancellation attack rejected.");
        yield return Wait(() => basic.HasEnteredMotion && basic.NormalizedTime > material.strikes.Last().contactEnd && basic.NormalizedTime < .95f, 12, "Recovery interval missing.");
        int impacts = basic.ImpactCount; Bridge.TryPlayOwnedPresentation("ReactionPose", EnemyMotionRole.Reaction, host, true, out var interrupt);
        yield return null; Require(basic.ExecutionResult.State == EnemyMotionState.Cancelled && basic.ImpactCount == impacts, "Late reaction completed/repeated strike."); Record("OWN-04"); Bridge.CancelMotion(interrupt, EnemyMotionReason.OwnerCancelled);
        var snapshot = basic.LastCancelledSnapshot; Require(snapshot.Valid && snapshot.Sample.Clip == material.runtimeClip && snapshot.ConsumedStrikeEnd >= material.strikes.Last().contactEnd - .001f, "Actual cancellation snapshot lost consumed strike.");
        int missing = Bridge.InvalidSnapshotCount; var unavailable = Begin("Attack_Attack1", EnemyMotionRole.Attack, external: true); actor.Animator.Play("Locomotion", 0, 0); actor.Animator.Update(0);
        Bridge.CancelMotion(unavailable, EnemyMotionReason.OwnerCancelled); Require(!Bridge.GetMotionResult(unavailable).Snapshot.Valid && Bridge.InvalidSnapshotCount == missing + 1, "Missing snapshot fabricated.");
        Record("REACT-04", new { actualClip = snapshot.Sample.Clip.name, normalized = snapshot.Sample.Normalized, consumedEnd = snapshot.ConsumedStrikeEnd, missingRecorded = true });
    }
    static IEnumerator Turns()
    {
        activeCase = "TURN"; var rows = new JArray();
        foreach (float angle in new[] { -30f, 30f, -90f, 90f, -179.9f, 179.9f })
        {
            Fresh(false); captureIndex = 0; Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward; Vector3 aim = actor.transform.position + direction * 10;
            actor.Movement.FacePosition(aim); yield return Wait(() => actor.Movement.IsOwnedTurning, 2, "Turn did not start: " + angle);
            var h = loco.FacingHandle; float begin = Time.time; bool rejected = false, clipSeen = false; float previous = 0, maxJump = 0; var frames = new JArray();
            while (actor.Movement.IsOwnedTurning && Time.time < begin + 7)
            {
                yield return null;
                if (Bridge.TryReadMotion(h, out var sample))
                {
                    clipSeen |= sample.Clip.name.StartsWith("Turn", StringComparison.Ordinal); float changed = Quaternion.Angle(Quaternion.identity, actor.Movement.PhysicalRotation);
                    maxJump = Mathf.Max(maxJump, Mathf.Abs(changed - previous)); previous = changed;
                    rejected |= !basic.CanStart(basic.Collection.attacks[0].ability, player.transform);
                    frames.Add(new JObject { ["t"] = Time.time - begin, ["normalized"] = sample.Normalized, ["clip"] = sample.Clip.name, ["angle"] = changed, ["weight"] = sample.Weight });
                }
                if (Time.frameCount % 2 == 0) Capture("Turn-" + angle.ToString("0"));
            }
            Require(loco.FacingResult.State == EnemyMotionState.Completed && clipSeen && rejected && maxJump < 8, "Turn failed/snapped/allowed attack: " + angle + " / " + loco.FacingResult.Reason);
            Require(Vector3.Angle(actor.Movement.PhysicalRotation * Vector3.forward, direction) < .3f, "Final physical direction mismatch.");
            rows.Add(new JObject { ["requested"] = angle, ["budget"] = loco.FacingBudget, ["elapsed"] = Time.time - begin, ["maxFrameAngle"] = maxJump, ["frames"] = frames });
        }
        File.WriteAllText(Path.Combine(plan.output, "turns.json"), rows.ToString()); Record("TURN-01", rows); Record("TURN-03", new { testedAngles = 6, rejectedDuringTurn = true }); Record("CFG-03", new { actualSourceCapture = "Frames/Turn-*", samples = "turns.json" });
        Fresh(false); actor.Movement.FacePosition(actor.transform.position); yield return new WaitForSeconds(.2f); Require(!actor.Movement.IsOwnedTurning, "Zero aim turned.");
        actor.Movement.FacePosition(actor.transform.position + Quaternion.Euler(0, 7, 0) * Vector3.forward * 8); yield return new WaitForSeconds(.2f); Require(!actor.Movement.IsOwnedTurning && Quaternion.Angle(Quaternion.identity, actor.Movement.PhysicalRotation) < .1f, "Tolerance snapped."); Record("TURN-02");
        Fresh(false); var target = actor.transform.position + Vector3.right * 10; actor.Movement.FacePosition(target); yield return Wait(() => actor.Movement.IsOwnedTurning, 2, "Crossing probe didn't turn.");
        var locked = loco.FacingHandle; Warp(actor.transform.position - Vector3.forward * 10); actor.Movement.FacePosition(player.transform.position);
        yield return Wait(() => !actor.Movement.IsOwnedTurning, 7, "Crossing probe stuck."); Require(loco.FacingResult.Handle == locked && Vector3.Angle(actor.transform.forward, Vector3.right) < .3f, "Moving target changed in-flight turn."); Record("TURN-04");
        yield return OneXTurn();
    }
    static IEnumerator OneXTurn()
    {
        activeCase = "TURN-05"; Fresh(false); var original = profile.Find("Turn90Left").rate; profile.Find("Turn90Left").rate = 1;
        var trace = new JArray();
        try
        {
            actor.Movement.FacePosition(actor.transform.position + Vector3.left * 10);
            yield return Wait(() => actor.Movement.IsOwnedTurning, 2, "1x turn didn't start.");
            Require(Mathf.Abs(loco.FacingBudget - 4.73f) < .02f, "1x turn budget mismatch.");
            var handle = loco.FacingHandle; float started = Time.time; float deadline = Time.time + 7; double watchdog = EditorApplication.timeSinceStartup + 45;
            while (actor.Movement.IsOwnedTurning && Time.time < deadline && EditorApplication.timeSinceStartup < watchdog)
            {
                yield return null; bool read = Bridge.TryReadMotion(handle, out var sample);
                var state = actor.Animator.GetCurrentAnimatorStateInfo(0); var next = actor.Animator.GetNextAnimatorStateInfo(0);
                trace.Add(new JObject { ["t"] = Time.time - started, ["unscaled"] = Time.unscaledTime, ["read"] = read, ["sampleNormalized"] = sample.Normalized,
                    ["clip"] = sample.Clip != null ? sample.Clip.name : null, ["motionState"] = Bridge.CurrentMotionState.ToString(), ["result"] = Bridge.GetMotionResult(handle).State.ToString(),
                    ["reason"] = Bridge.GetMotionResult(handle).Reason.ToString(), ["currentHash"] = state.fullPathHash, ["currentNormalized"] = state.normalizedTime,
                    ["nextHash"] = next.fullPathHash, ["nextNormalized"] = next.normalizedTime, ["transition"] = actor.Animator.IsInTransition(0),
                    ["yaw"] = actor.Movement.PhysicalRotation.eulerAngles.y, ["animatorSpeed"] = actor.Animator.speed, ["rate"] = actor.Animator.GetFloat("BossMotionRate"),
                    ["movementEnabled"] = actor.Movement.enabled, ["currentRequest"] = Bridge.CurrentMotionHandle.RequestId, ["handleRequest"] = handle.RequestId });
            }
            Require(!actor.Movement.IsOwnedTurning && loco.FacingResult.State == EnemyMotionState.Completed, "1x turn did not complete: " + loco.FacingResult.State + "/" + loco.FacingResult.Reason);
            Record("TURN-05", new { expectedBudget = 4.73, elapsed = Time.time - started });
        }
        finally { File.WriteAllText(Path.Combine(plan.output, "turn-1x.json"), trace.ToString()); profile.Find("Turn90Left").rate = original; }
    }
    static IEnumerator Continuous()
    {
        activeCase = "MOVE"; Fresh(); var h = Begin("Locomotion", EnemyMotionRole.Locomotion); var clips = new HashSet<string>();
        foreach (var direction in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right, new Vector2(.7f, .7f), Vector2.zero })
        {
            float until = Time.time + 1.65f;
            while (Time.time < until) { Require(Bridge.TryUpdateOwnedMotionIntent(h, new EnemyMotionIntent(direction, 1)), "Continuous intent rejected."); yield return null; if (Bridge.TryReadMotion(h, out var sample)) clips.Add(sample.Clip.name); }
            Require(Bridge.CurrentMotionHandle == h && !Bridge.GetMotionResult(h).IsTerminal, "Continuous owner completed/restarted.");
        }
        Require(Bridge.TryReadMotion(h, out var end) && end.Normalized > 3 && clips.Contains("WalkForward") && clips.Contains("WalkBackwards") && clips.Contains("WalkLeft") && clips.Contains("WalkRight") && clips.Contains("IdleBreathe"), "Continuous direction/cycle coverage missing.");
        Record("MOVE-04", new { cycles = end.Normalized, clips = clips.ToArray(), handle = h.RequestId }); Bridge.TryEndContinuousMotion(h);
        Fresh(false); int contacts = 0, groundContacts = 0; var keys = new HashSet<string>(); bool duplicate = false; Action<EnemyMotionContact> observe = c => { contacts++; duplicate |= !keys.Add(c.Handle.Generation + ":" + c.MotionId + ":" + c.Cycle + ":" + c.Index); }; Bridge.MotionContact += observe;
        var emitter = actor.GetComponent<EnemyEliteFootstepEmitter>(); Action<Vector3, float, Collider> ground = (point, distance, collider) => { groundContacts++; Require(collider != null && Mathf.Abs(point.y - room.ArenaCenter.y) < .25f, "Contact was not on the actual arena."); }; emitter.GroundContact += ground;
        var moves = new JArray();
        try
        {
            foreach (Vector3 direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, (Vector3.forward + Vector3.right).normalized })
            {
                actor.Movement.StopMovement(); Pose(room.ArenaCenter + Vector3.up * .05f, Quaternion.identity); actor.Movement.SetMoveFacingPolicy(true); var origin = actor.transform.position;
                actor.Movement.SetDestination(origin + direction * 6, .2f, direction.z < 0 ? EnemyLocomotionMode.Backpedal : EnemyLocomotionMode.Walk); yield return new WaitForSeconds(1.5f);
                Vector3 displacement = actor.transform.position - origin; Require(displacement.magnitude > .5f && Vector3.Angle(displacement, direction) < 15, "Physical directional movement failed.");
                Require(Quaternion.Angle(Quaternion.identity, actor.Movement.PhysicalRotation) < .3f, "Strafe/backward snapped facing.");
                moves.Add(new JObject { ["direction"] = direction.ToString(), ["distance"] = displacement.magnitude, ["x"] = actor.Animator.GetFloat("BossMoveX"), ["z"] = actor.Animator.GetFloat("BossMoveZ") });
            }
            Record("MOVE-01", moves); Record("MOVE-03", new { backwardsObserved = true, keptFacing = true });
            actor.Movement.StopMovement(); yield return new WaitForSeconds(.3f); int idleContacts = contacts; yield return new WaitForSeconds(.5f); Require(contacts == idleContacts, "Idle emitted foot contacts.");
            Pose(actor.transform.position + Vector3.right * 3, actor.Movement.PhysicalRotation); yield return null; Require(contacts - idleContacts <= 1 && !duplicate, "Teleport contact burst/duplicate.");
            var blocked = new JArray();
            foreach (bool crowd in new[] { false, true })
            {
                GameObject obstacle = null;
                try
                {
                    actor.Movement.StopMovement(); Pose(room.ArenaCenter + Vector3.up * .05f, Quaternion.identity); Warp(room.ArenaCenter - Vector3.forward * 15);
                    actor.Movement.SetMoveFacingPolicy(true);
                    var ownBody = actor.GetComponentsInChildren<CapsuleCollider>(true).First(c => c.enabled && !c.isTrigger);
                    float radius = Mathf.Max(ownBody.bounds.extents.x, ownBody.bounds.extents.z);
                    if (crowd)
                    {
                        obstacle = new GameObject("Owned stationary crowd obstruction"); var capsule = obstacle.AddComponent<CapsuleCollider>(); capsule.radius = radius * 2; capsule.height = 20; capsule.center = Vector3.up * 10;
                        obstacle.transform.position = actor.transform.position + Vector3.forward * (radius + capsule.radius + .1f);
                        obstacle.AddComponent<CombatHealth>(); obstacle.AddComponent<EnemyCrowdAgent>();
                    }
                    else
                    {
                        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube); obstacle.name = "Owned physical wall obstruction";
                        obstacle.transform.localScale = new Vector3(30, 20, 1); obstacle.transform.position = actor.transform.position + Vector3.forward * (radius + .7f) + Vector3.up * 10;
                    }
                    owned.Add(obstacle); Physics.SyncTransforms();
                    actor.Movement.SetDestination(actor.transform.position + Vector3.forward * 10, .2f, EnemyLocomotionMode.Walk); yield return new WaitForSeconds(2);
                    Vector3 at = actor.transform.position; int before = contacts; yield return new WaitForSeconds(.6f);
                    Vector3 delta = actor.transform.position - at; delta.y = 0;
                    float intent = new Vector2(actor.Animator.GetFloat("BossMoveX"), actor.Animator.GetFloat("BossMoveZ")).magnitude;
                    Require(delta.magnitude < .05f && intent < .15f && contacts == before, "Blocked physical movement retained gait/contacts: " + (crowd ? "crowd" : "wall"));
                    blocked.Add(new JObject { ["kind"] = crowd ? "crowd" : "wall", ["actualDistance"] = delta.magnitude, ["intent"] = intent, ["extraContacts"] = contacts - before });
                }
                finally { actor.Movement.StopMovement(); if (obstacle != null) Object.Destroy(obstacle); }
                yield return null;
            }
            Record("MOVE-02", new { idleContacts, afterTeleport = contacts, blocked });
            Require(contacts >= 3 && groundContacts >= 3 && !duplicate, "Foot contact markers/physical ground absent/duplicated."); Record("FX-01", new { contacts, groundContacts, unique = keys.Count });
        }
        finally { Bridge.MotionContact -= observe; emitter.GroundContact -= ground; actor.Movement.StopMovement(); }
    }
    static IEnumerator ReactiveBackstep()
    {
        activeCase = "MOVE-BACKSTEP"; Fresh(false); room.enabled = true; room.Brain.ReviewMode = true;
        var settings = (CrustaspikanEncounterSettings)typeof(CrustaspikanEncounterBrain).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(room.Brain);
        Require(Mathf.Abs(settings.evasionDistance - 3f) < .01f && Mathf.Abs(settings.evasionSeconds - 1f) < .01f, "Saved reactive backstep is not 3m/1s.");
        Warp(actor.transform.position + Vector3.forward * 7f);
        Vector3 start = actor.GetComponent<EnemyMotor>().Position; Quaternion facing = actor.Movement.PhysicalRotation;
        typeof(CrustaspikanEncounterBrain).GetMethod("Dodge", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(room.Brain, null);
        float began = Time.time, maxAngle = 0f; bool backwards = false;
        while (Time.time - began < settings.evasionSeconds + .15f)
        {
            yield return null; maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(facing, actor.Movement.PhysicalRotation));
            if (Bridge.CurrentMotionRole == EnemyMotionRole.Locomotion && Bridge.TryReadMotion(Bridge.CurrentMotionHandle, out var sample))
                backwards |= sample.Clip != null && sample.Clip.name == "WalkBackwards";
            if (Time.frameCount % 6 == 0) Capture("Reactive-backstep");
        }
        float distance = Vector3.Distance(start, actor.GetComponent<EnemyMotor>().Position);
        Require(backwards && distance > 2.3f && distance <= 3.2f && maxAngle < 1f, "Actual BT backstep did not keep its backward gait/facing.");
        room.enabled = false; actor.AbilityController.Cancel(); actor.Movement.StopMovement(); actor.Movement.SetMoveFacingPolicy(false);
        Warp(actor.transform.position + Vector3.right * 12f); actor.Movement.FacePosition(player.transform.position);
        yield return Wait(() => !actor.Movement.IsOwnedTurning && actor.Movement.IsFacingForAttack(player.transform.position, 8f), 8, "Backstep did not restore normal aim planning.");
        Record("MOVE-03-BT", new { plannedDistance = settings.evasionDistance, plannedSeconds = settings.evasionSeconds, actualDistance = distance, maxAngle, backwards, normalAimRestored = true });
    }

    static IEnumerator Preparation()
    {
        activeCase = "SUP"; Fresh(); int throws = composite.EliteThrowCount; var throwAbility = basic.Collection.attacks.First(a => a.delivery == EnemyBossMaterialDelivery.Boulder).ability;
        Require(composite.TryBeginPreparation(EnemyBossThrowPayload.Elite, 101, 0, player.transform), "Preparation rejected.");
        var held = basic.PlaybackHandle; var aim = player.transform.position; var facing = actor.Movement.PhysicalRotation;
        yield return Wait(() => composite.TryGetPreparedStartContext(out _), 10, "Preparation never reached Holding.");
        Require(!basic.IsExecuting && composite.HasPreparation && Bridge.CurrentMotionState == EnemyMotionState.Holding, "Holding availability wrong.");
        Require(composite.TryGetPreparedStartContext(out var context), "Prepared context missing."); Warp(actor.transform.position + Vector3.left * 12);
        var wrong = new EnemyMotionHandle(held.Lease, held.Generation, held.RequestId, 999, held.StepId); var wrongContext = new EnemyAbilityStartContext(wrong, aim, facing, held.Lease);
        Require(!composite.CanStart(throwAbility, player.transform, wrongContext), "Foreign group accepted.");
        Require(!actor.Melee.TryStartAttack(player.transform), "Legacy weak entered Holding."); Record("OWN-06", new { foreignGroupRejected = true, legacyWeakRejected = true });
        Require(!actor.AbilityController.TryStartAbility(basic.Collection.attacks[0].ability, player.transform, context) && composite.HasPreparation && composite.EliteThrowCount == throws && Bridge.CurrentMotionHandle == held, "Rejected candidate consumed payload/owner.");
        Require(composite.CanStart(throwAbility, player.transform, context) && actor.AbilityController.TryStartAbility(throwAbility, player.transform, context), "Prepared aim candidate/commit rejected after player relocation.");
        var committedAim = (Vector3)typeof(EnemyBossCompositePatternExecutor).GetField("aim", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(composite);
        File.WriteAllText(Path.Combine(plan.output, "preparation-handoff.json"), JsonConvert.SerializeObject(new {
            held = new { held.Lease, held.Generation, held.RequestId, held.GroupId, held.StepId },
            committed = new { composite.PlaybackHandle.Lease, composite.PlaybackHandle.Generation, composite.PlaybackHandle.RequestId, composite.PlaybackHandle.GroupId, composite.PlaybackHandle.StepId },
            savedAim = new[] { context.AimPosition.x, context.AimPosition.y, context.AimPosition.z },
            committedAim = new[] { committedAim.x, committedAim.y, committedAim.z }, bossY = actor.transform.position.y,
            displacement = Vector3.Distance(committedAim, context.AimPosition)
        }, Formatting.Indented));
        Require(composite.PlaybackHandle != held && composite.PlaybackHandle.GroupId == held.GroupId && Vector3.Distance(committedAim, context.AimPosition) < .01f, "Saved aim handoff lost.");
        Require(Quaternion.Angle(actor.Movement.PhysicalRotation, facing) < .3f && !composite.HasPreparation, "Handoff facing/payload transfer failed."); Record("SUP-04", new { group = held.GroupId, aim = context.AimPosition.ToString(), handed = composite.PlaybackHandle.RequestId }); actor.AbilityController.Cancel();
        Fresh(); bool previousAbilityDestroyed = throwAbility == null;
        throwAbility = basic.Collection.attacks.First(a => a.delivery == EnemyBossMaterialDelivery.Boulder).ability;
        Require(throwAbility != null, "Current lease throw material missing.");
        File.WriteAllText(Path.Combine(plan.output, "fixture-lease.json"), JsonConvert.SerializeObject(new { previousAbilityDestroyed, currentAbility = throwAbility.AbilityId, lease = actor.LeaseVersion }, Formatting.Indented));
        Require(composite.TryBeginPreparation(EnemyBossThrowPayload.Rock, 102, 0, player.transform), "Carry preparation rejected."); yield return Wait(() => composite.TryGetPreparedStartContext(out _), 10, "Carry never held.");
        Require(!composite.TryBeginCarry(Vector3.right) && composite.HasPreparation && profile.Find("CarryLeft") == null, "Missing lateral carry silently substituted."); Record("SUP-03");
        foreach (var direction in new[] { Vector3.forward, Vector3.back })
        {
            Require(composite.TryBeginCarry(direction), "Carry start rejected."); actor.Movement.SetMoveFacingPolicy(true); actor.Movement.SetDestination(actor.transform.position + direction * 4, .2f, direction.z < 0 ? EnemyLocomotionMode.Backpedal : EnemyLocomotionMode.Walk);
            float carryEnd = Time.time + 1f;
            while (Time.time < carryEnd) { yield return null; if (Time.frameCount % 6 == 0) Capture(direction.z < 0 ? "Carry-back" : "Carry-forward"); }
            Require(Bridge.TryReadMotion(Bridge.CurrentMotionHandle, out var sample) && sample.Clip.name.EndsWith("WithRock", StringComparison.Ordinal), "Carry clip missing.");
            actor.Movement.StopMovement(); Require(composite.TryEndCarry(), "Carry stop rejected."); yield return Wait(() => composite.TryGetPreparedStartContext(out _), 2, "Carry stop did not restore held end pose.");
            Require(Bridge.TryReadMotion(Bridge.CurrentMotionHandle, out var ending) && ending.Normalized >= .998f && ending.Clip.name == "UnearthRock", "Carry stop restarted extraction.");
        }
        bool ready = composite.TryGetPreparedStartContext(out var final);
        Vector3 delta = final.AimPosition - actor.transform.position; delta.y = 0;
        File.WriteAllText(Path.Combine(plan.output, "carry-handoff.json"), JsonConvert.SerializeObject(new {
            ready, preparation = composite.HasPreparation, basicExecuting = basic.IsExecuting, compositeExecuting = composite.IsExecuting,
            motionRole = Bridge.CurrentMotionRole.ToString(), motionState = Bridge.CurrentMotionState.ToString(),
            executorEligible = ready && composite.CanStart(throwAbility, player.transform, final),
            cooldownReady = actor.AbilityController.IsCooldownReady(throwAbility), cooldown = actor.AbilityController.GetRemainingCooldown(throwAbility),
            useConditions = EnemyAttackThreatGeometry.MatchesUseConditions(actor, throwAbility, delta.magnitude, actor.Health.NormalizedHp),
            range = delta.magnitude, boss = new[] { actor.transform.position.x, actor.transform.position.y, actor.transform.position.z },
            savedAim = new[] { final.AimPosition.x, final.AimPosition.y, final.AimPosition.z }, currentMaterial = basic.CurrentMaterial?.name
        }, Formatting.Indented));
        Require(ready && actor.AbilityController.TryStartAbility(throwAbility, player.transform, final), "Carry->throw rejected."); Record("SUP-01", new { forwardBackward = true, heldPose = true, directThrow = true }); actor.AbilityController.Cancel();
        Fresh(); var target = new GameObject("Owned preparation target"); owned.Add(target); target.transform.position = player.transform.position;
        Require(composite.TryBeginPreparation(EnemyBossThrowPayload.Rock, 103, 0, target.transform), "Target-loss prep rejected."); yield return new WaitForSeconds(.2f); target.SetActive(false); yield return new WaitForSeconds(.2f);
        Require(!composite.HasPreparation && !basic.IsExecuting && !actor.Movement.IsActionLocked, "Target loss retained held owner."); Record("SUP-02");
    }
    static IEnumerator Attacks()
    {
        activeCase = "ATK"; var rows = new JArray();
        foreach (string id in new[] { "right_light", "combo", "advance_combo", "donut", "weak_spit", "strong_spit", "rock_throw", "elite_throw" })
        {
            Fresh(false); room.enabled = true; room.Brain.ReviewMode = true; captureIndex = 0;
            Warp(actor.transform.position + Vector3.forward * (id == "advance_combo" ? 12 : id.Contains("spit") || id.Contains("throw") ? 12 : 7));
            int impacts = basic.ImpactCount, releases = composite.ReleaseCount; bool started = false; float maxAngle = 0; Quaternion committed = Quaternion.identity; EnemyMotionHandle handle = default;
            Require(room.Brain.StartPatternForReview(id), "Actual assembly rejected: " + id); float deadline = Time.time + 35; double watchdog = EditorApplication.timeSinceStartup + 180;
            while (Time.time < deadline && EditorApplication.timeSinceStartup < watchdog && (room.Brain.CurrentPatternId != "" || !started))
            {
                yield return null;
                if (!started && (basic.CurrentMaterial != null || composite.CurrentMaterial != null) && Bridge.CurrentMotionRole == EnemyMotionRole.Attack)
                { started = true; committed = actor.Movement.PhysicalRotation; handle = Bridge.CurrentMotionHandle; Warp(actor.transform.position + Vector3.right * 13); }
                if (started && (basic.IsExecuting || composite.IsExecuting)) maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(committed, actor.Movement.PhysicalRotation));
                if (started && Time.frameCount % 6 == 0) Capture("Attack-" + id);
            }
            Require(started && Time.time < deadline && EditorApplication.timeSinceStartup < watchdog && maxAngle < 2 && string.IsNullOrEmpty(basic.LastFailure) && string.IsNullOrEmpty(composite.LastFailure), "Assembly failed/retargeted: " + id + " / " + room.Brain.State);
            Require(basic.ImpactCount > impacts || composite.ReleaseCount > releases, "No actual attack event: " + id);
            rows.Add(new JObject { ["pattern"] = id, ["bodyAngle"] = maxAngle, ["impacts"] = basic.ImpactCount - impacts, ["releases"] = composite.ReleaseCount - releases, ["group"] = handle.GroupId });
            room.enabled = false;
        }
        Record("ATK-01", rows.Take(4).ToArray()); Record("ATK-02", rows.Skip(4).ToArray()); Record("ATK-03", rows); Record("FX-02", new { strikeProof = rows.Take(4).ToArray(), existingPresentationPreserved = true });
        yield return LowSampleAndFlight();
    }

    static IEnumerator LowSampleAndFlight()
    {
        activeCase = "ATK-LOW";
        Fresh(); Time.captureDeltaTime = .12f; var attack = basic.Collection.attacks.First(a => a.runtimeClip.name == "2HitComboAttack"); int count = basic.ImpactCount;
        Require(basic.TryStart(attack.ability, 0, player.transform), "Low-sample combo rejected.");
        var trace = new JArray(); float lowDeadline = Time.time + 20; double lowWatchdog = EditorApplication.timeSinceStartup + 100;
        while (basic.IsExecuting && Time.time < lowDeadline && EditorApplication.timeSinceStartup < lowWatchdog)
        {
            yield return null;
            bool sampled = Bridge.TryReadMotion(basic.PlaybackHandle, out var probe);
            trace.Add(new JObject { ["t"] = Time.time, ["fixed"] = Time.fixedTime, ["frame"] = Time.frameCount,
                ["normalized"] = basic.NormalizedTime, ["sampled"] = sampled, ["sourceClip"] = sampled ? probe.Clip?.name : "",
                ["sourceNormalized"] = sampled ? probe.Normalized : -1f, ["role"] = Bridge.CurrentMotionRole.ToString(),
                ["state"] = Bridge.CurrentMotionState.ToString(), ["impacts"] = basic.ImpactCount - count, ["deadline"] = basic.ExecutionDeadline,
                ["animatorSpeed"] = actor.Animator.speed, ["phaseRate"] = actor.Animator.GetFloat("BossMotionRate"), ["execution"] = basic.ExecutionResult.State.ToString(), ["failure"] = basic.LastFailure });
        }
        File.WriteAllText(Path.Combine(plan.output, "low-sample.json"), new JObject { ["trace"] = trace,
            ["expectedImpacts"] = attack.strikes.Length, ["observedImpacts"] = basic.ImpactCount - count,
            ["result"] = basic.ExecutionResult.State.ToString(), ["reason"] = basic.ExecutionResult.Reason.ToString(),
            ["failure"] = basic.LastFailure, ["remainingExecuting"] = basic.IsExecuting }.ToString());
        Require(!basic.IsExecuting, "Low-sample combo stuck.");
        Require(basic.ExecutionResult.State == EnemyMotionState.Completed && basic.ImpactCount - count == attack.strikes.Length, "Skipped/duplicate low-sample strikes."); Record("ATK-04", new { captureStep = .12, strikeCount = attack.strikes.Length, observed = basic.ImpactCount - count }); Time.captureDeltaTime = 1f / 60;
        Fresh(); var rock = basic.Collection.attacks.First(a => a.delivery == EnemyBossMaterialDelivery.Boulder); float saved = rock.flightSeconds; rock.flightSeconds = 5;
        try
        {
            Require(basic.TryStart(rock.ability, 0, player.transform), "Long-flight basic probe rejected."); yield return Wait(() => basic.PlaybackResult.State == EnemyMotionState.Completed && basic.IsExecuting, 12, "No body/flight separation interval.");
            Require(basic.ExecutionResult.State != EnemyMotionState.Completed && actor.Movement.HasCommittedMotionFacing, "Flight prematurely completed execution/facing."); Record("ATK-05", new { playback = basic.PlaybackResult.State.ToString(), execution = basic.ExecutionResult.State.ToString(), flights = basic.ActiveProjectileCount }); basic.Cancel();
        }
        finally { rock.flightSeconds = saved; }
    }
    static IEnumerator ReactionAndLife()
    {
        activeCase = "LIFE"; Fresh(); var temporary = actor.GetComponent<CrustaspikanTemporaryReaction>();
        Require(temporary.TryPlayGroggy(.8f), "Actual temporary reaction rejected."); yield return new WaitForSeconds(.35f); var old = temporary.ReactionHandle;
        uint lease = actor.LeaseVersion; room.Restart(); actor = room.Brain.Actor; basic = actor.GetComponent<EnemyBossMaterialExecutor>(); composite = actor.GetComponent<EnemyBossCompositePatternExecutor>(); loco = actor.GetComponent<EnemyLocomotionAnimator>();
        Require(actor.LeaseVersion > lease && !actor.GetComponent<CrustaspikanTemporaryReaction>().BlocksActions && actor.Animator.speed > .99f, "Pool reuse kept reaction clock.");
        Require(Bridge.ConfigureMotionPlayback(profile) && !Bridge.OwnsMotion(old) && !Bridge.SetOwnedPlaybackRate(old, 2) && !actor.Movement.IsActionLocked, "Stale lease retained writes/lock.");
        loco.enabled = false; var h = Begin("Roar1", EnemyMotionRole.Support); actor.gameObject.SetActive(false); yield return null; Require(!Bridge.CurrentMotionHandle.IsValid, "Disable retained handle.");
        Require(!actor.IsLeased, "Disabled actor did not return to the actual pool.");
        uint disabledLease = actor.LeaseVersion; Fresh();
        Require(actor.IsLeased && actor.LeaseVersion > disabledLease && !Bridge.OwnsMotion(h), "Actual reacquisition retained disabled lease.");
        var deathPreempted = Begin("Roar1", EnemyMotionRole.Support);
        actor.Health.TakeDamage(new DamageInfo(2000000, actor.transform.position, player.gameObject, Vector3.forward)); yield return null;
        yield return Wait(() => Bridge.CurrentMotionRole == EnemyMotionRole.Death && Bridge.TryReadMotion(Bridge.CurrentMotionHandle, out var deathSample)
            && deathSample.Clip == profile.ResolveClip(profile.Find("Death")), 2, "Actual boss death clip not observed.");
        File.WriteAllText(Path.Combine(plan.output, "life-death.json"), JsonConvert.SerializeObject(new {
            logicalDeath = actor.Health.IsDead, hp = actor.Health.CurrentHp, maxHp = actor.Health.MaxHp, leased = actor.IsLeased, lease = actor.LeaseVersion,
            active = actor.gameObject.activeInHierarchy, bridgeEnabled = Bridge.isActiveAndEnabled, animatorEnabled = actor.Animator.isActiveAndEnabled,
            oldOwned = Bridge.OwnsMotion(h), role = Bridge.CurrentMotionRole.ToString(), state = Bridge.CurrentMotionState.ToString(),
            profile = Bridge.PlaybackProfile?.name, ownsProfile = Bridge.UsesOwnedMotion, invalidProfile = Bridge.HasInvalidMotionProfile, configError = Bridge.MotionConfigurationError,
            rootMotion = actor.Animator.applyRootMotion, temporaryPhase = actor.GetComponent<CrustaspikanTemporaryReaction>().Phase.ToString(),
            deadFlag = typeof(EnemyAnimationBridge).GetField("isDead", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Bridge),
            clips = actor.Animator.GetCurrentAnimatorClipInfo(0).Select(c => new { name = c.clip.name, weight = c.weight }).ToArray()
        }, Formatting.Indented));
        Require(actor.Health.IsDead && actor.IsLeased && !Bridge.OwnsMotion(h) && !Bridge.OwnsMotion(deathPreempted) && Bridge.CurrentMotionRole == EnemyMotionRole.Death && !actor.Animator.applyRootMotion, "Logical death retained old owner."); Record("LIFE-01", new { staleLease = lease, disabledLease, newLease = actor.LeaseVersion, disable = true, actualPoolReacquisition = true, death = true });
    }
    static IEnumerator Timing()
    {
        activeCase = "TIME"; Fresh(); var turn = Begin("Turn90Left", EnemyMotionRole.Turn); Require(!Bridge.SetOwnedPlaybackRate(turn, 2), "Snapshot turn accepted live rate."); Bridge.CancelMotion(turn, EnemyMotionReason.OwnerCancelled);
        var support = Begin("Roar1", EnemyMotionRole.Support); Require(!Bridge.SetOwnedPlaybackRate(support, 2), "Snapshot support accepted live rate."); Bridge.CancelMotion(support, EnemyMotionReason.OwnerCancelled);
        var move = Begin("Locomotion", EnemyMotionRole.Locomotion); Require(Bridge.SetOwnedPlaybackRate(move, 2), "Live walk rate rejected."); yield return new WaitForSeconds(.2f); Require(Bridge.CurrentMotionHandle == move, "Rate change restarted loop."); Bridge.TryEndContinuousMotion(move);
        var attack = basic.Collection.attacks.First(a => a.runtimeClip.name == "RightHandAttack"); Require(basic.TryStart(attack.ability, 0, player.transform), "Live attack rate probe rejected.");
        yield return new WaitForSeconds(.2f); float before = basic.ExecutionDeadline; actor.Melee.SetRuntimeAttackSpeedMultiplier(.12f); yield return new WaitForSeconds(.2f);
        Require(basic.ExecutionDeadline > before + 10 && basic.IsExecuting, "Live slow attack did not recalculate deadline."); actor.Melee.SetRuntimeAttackSpeedMultiplier(1); basic.Cancel(); Record("TIME-02", new { supportSnapshot = true, turnSnapshot = true, continuousLive = true, attackDeadlineBefore = before });
        var stamps = new JArray();
        foreach (int fps in new[] { 30, 60, 144 })
        {
            Fresh(); Time.captureDeltaTime = 1f / fps; var h = Begin("Roar1", EnemyMotionRole.Support); yield return new WaitForSeconds(.2f); Require(Bridge.TryReadMotion(h, out var sample), "Clock sample unavailable.");
            float normalized = sample.Normalized; var menu = OverburstGameMenu.Instance;
            Require(menu != null && !OverburstGameMenu.IsOpen && !OverburstTimeEffectArbiter.IsPaused, "Actual menu pause fixture unavailable.");
            try
            {
                float openingDelta = Time.deltaTime; menu.Open(); yield return null;
                Require(Bridge.TryReadMotion(h, out var boundary) && boundary.Normalized - normalized <= openingDelta / sample.Clip.length + .0001f,
                    "Menu opening advanced beyond its already-started animation frame.");
                float pausedNormalized = boundary.Normalized; yield return new WaitForSecondsRealtime(.15f);
                bool pauseRead = Bridge.TryReadMotion(h, out var paused);
                File.WriteAllText(Path.Combine(plan.output, "menu-pause-" + fps + ".json"), JsonConvert.SerializeObject(new {
                    fps, open = OverburstGameMenu.IsOpen, paused = OverburstTimeEffectArbiter.IsPaused, inputBlocked = GameplayInputBlocker.IsGameplayInputBlocked,
                    pauseRead, beforeNormalized = normalized, openingBoundaryNormalized = pausedNormalized, afterNormalized = paused.Normalized, deltaNormalized = paused.Normalized - pausedNormalized, openingDelta,
                    beforeFrame = sample.Frame, afterFrame = paused.Frame, beforeScaled = sample.ScaledTime, afterScaled = paused.ScaledTime,
                    scale = Time.timeScale, deltaTime = Time.deltaTime, captureDelta = Time.captureDeltaTime,
                    animatorUpdate = actor.Animator.updateMode.ToString(), animatorSpeed = actor.Animator.speed,
                    currentMotion = Bridge.CurrentMotionState.ToString(), motionRole = Bridge.CurrentMotionRole.ToString()
                }, Formatting.Indented));
                Require(OverburstGameMenu.IsOpen && OverburstTimeEffectArbiter.IsPaused && GameplayInputBlocker.IsGameplayInputBlocked
                    && pauseRead && Mathf.Abs(paused.Normalized - pausedNormalized) < .0001f && Mathf.Abs(paused.ScaledTime - boundary.ScaledTime) < .0001f, "Actual menu did not pause the owned sample/input.");
            }
            finally { menu.Close(); }
            Require(!OverburstGameMenu.IsOpen && !OverburstTimeEffectArbiter.IsPaused && !GameplayInputBlocker.IsGameplayInputBlocked, "Menu did not resume gameplay.");
            Time.timeScale = 1; yield return Wait(() => Bridge.GetMotionResult(h).IsTerminal, 8, "Clock completion stuck."); Require(Bridge.GetMotionResult(h).State == EnemyMotionState.Completed, "Clock ended as failure.");
            Fresh(false); actor.Movement.FacePosition(actor.transform.position + Vector3.left * 10);
            yield return Wait(() => actor.Movement.IsOwnedTurning, 2, "Simulated-FPS turn did not start.");
            float turnStart = Time.time, turnBudget = loco.FacingBudget;
            yield return Wait(() => !actor.Movement.IsOwnedTurning, turnBudget + 1, "Simulated-FPS turn stuck.");
            Require(loco.FacingResult.State == EnemyMotionState.Completed && Vector3.Angle(actor.transform.forward, Vector3.left) < .3f,
                "Simulated-FPS turn completed with incorrect facing.");
            float turnElapsed = Time.time - turnStart;
            Fresh(); var hit = basic.Collection.attacks.First(a => a.runtimeClip.name == "RightHandAttack"); int impacts = basic.ImpactCount;
            Require(basic.TryStart(hit.ability, 0, player.transform), "Simulated-FPS attack rejected.");
            float attackDeadline = basic.ExecutionDeadline;
            Require(attackDeadline > Time.time && !float.IsInfinity(attackDeadline), "Simulated-FPS attack deadline invalid.");
            yield return Wait(() => !basic.IsExecuting, 15, "Simulated-FPS attack stuck.");
            Require(basic.ExecutionResult.State == EnemyMotionState.Completed && basic.ImpactCount - impacts == hit.strikes.Length,
                "Simulated-FPS attack missed/duplicated a strike or failed completion.");
            stamps.Add(new JObject { ["simulatedFPS"] = fps, ["actualMenuPause"] = true, ["frame"] = sample.Frame, ["fixed"] = sample.FixedTime,
                ["scaled"] = sample.ScaledTime, ["unscaled"] = sample.UnscaledTime, ["turnBudget"] = turnBudget, ["turnElapsed"] = turnElapsed,
                ["actualAttackStrikes"] = basic.ImpactCount - impacts, ["attackDeadline"] = attackDeadline });
        }
        Time.captureDeltaTime = 1f / 60; Record("TIME-01", stamps);
    }
    static IEnumerator EnabledFlow()
    {
        activeCase = "INTRO";
        Require(room.Brain.Actor.AnimationBridge.UsesOwnedMotion, "Saved opt-in prefab not consumed by actual portal.");
        var intro = room.EntranceCinematic; Require(intro != null && intro.IsPlaying, "Actual portal introduction missing.");
        int impacts = room.Brain.Actor.GetComponent<EnemyBossMaterialExecutor>().ImpactCount; float deadline = Time.unscaledTime + 25;
        while (intro.IsPlaying && Time.unscaledTime < deadline) { room.Brain.ReviewMode = true; yield return null; }
        Require(Time.unscaledTime < deadline && intro.RoarStarted && intro.RoarArrivalProgress >= .82f && intro.VisualRestored && intro.FloorGeometryRestored && !GameplayInputBlocker.IsGameplayInputBlocked
            && room.Brain.Actor.GetComponent<EnemyBossMaterialExecutor>().ImpactCount == impacts, "Introduction did not restore/no-damage handoff."); Record("INTRO-01", new { intro.RoarArrivalProgress, intro.RoarAudioStarted, intro.ImpactAudioStarted });
        actor = room.Brain.Actor; MakeCapture();
        var repeatProof = new JArray();
        for (int repeat = 0; repeat < 2; repeat++)
        {
            room.Brain.ReviewMode = true; var boss = room.Brain.Actor; actor = boss; captureIndex = 0;
            var bridge = boss.AnimationBridge; var gait = boss.GetComponent<EnemyLocomotionAnimator>();
            Warp(boss.transform.position + boss.transform.right * 8);
            yield return Wait(() => boss.Movement.IsOwnedTurning, 2, "Repeated portal did not enter native facing turn.");
            float turnAt = Time.time;
            while (boss.Movement.IsOwnedTurning && Time.time < turnAt + 8)
            { yield return null; if (Time.frameCount % 6 == 0) Capture("Saved-" + repeat + "-turn"); }
            Require(!boss.Movement.IsOwnedTurning && gait.FacingResult.State == EnemyMotionState.Completed
                && Vector3.Angle(boss.transform.forward, player.transform.position - boss.transform.position) < 1, "Repeated portal turn did not settle.");
            Warp(boss.transform.position + boss.transform.forward * 7);
            Require(room.Brain.StartPatternForReview("right_light"), "Reentry pattern rejected."); yield return Wait(() => room.Brain.CurrentPatternId == "", 20, "Reentry pattern stuck.");
            Require(boss.GetComponent<EnemyBossMaterialExecutor>().ImpactCount > 0 && bridge.UsesOwnedMotion, "Reentry lost owned attack.");
            var temporary = boss.GetComponent<CrustaspikanTemporaryReaction>();
            Require(temporary.TryPlayGroggy(.35f), "Repeated portal reaction rejected."); float reactionAt = Time.time; var phases = new HashSet<string>();
            while (temporary.BlocksActions && Time.time < reactionAt + 8)
            { yield return null; phases.Add(temporary.Phase.ToString()); if (Time.frameCount % 6 == 0) Capture("Saved-" + repeat + "-reaction"); }
            Require(!temporary.BlocksActions && phases.Contains("Recover"), "Repeated portal reaction failed to recover.");
            yield return Wait(() => bridge.CurrentMotionRole == EnemyMotionRole.Locomotion
                && bridge.TryReadMotion(bridge.CurrentMotionHandle, out var idle) && idle.Clip.name == "IdleBreathe", 3, "Repeated portal did not return to native idle.");
            Capture("Saved-" + repeat + "-idle");
            repeatProof.Add(new JObject { ["portal"] = repeat + 1, ["lease"] = boss.LeaseVersion, ["nativeTurn"] = true, ["actualAttack"] = true,
                ["reactionPhases"] = new JArray(phases), ["nativeIdle"] = true, ["ownedProfile"] = bridge.PlaybackProfile.name });
            room.Exit(true); yield return null;
            if (repeat == 0)
            {
                var entry = CrustaspikanEncounterHost.Current.Entrance; Warp(entry.transform.position + Vector3.back); Require(entry.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Second portal rejected."); room = CrustaspikanEncounterHost.Current.ActiveEncounter;
                room.EntranceCinematic?.Skip(); yield return null;
            }
        }
        Record("LIFE-02", repeatProof);
    }
    static IEnumerator ReactionEdges()
    {
        activeCase = "REACT-EDGES"; Fresh(); var temporary = actor.GetComponent<CrustaspikanTemporaryReaction>();
        Require(temporary.TryPlayGroggy(.8f), "Recovery freeze fixture rejected.");
        yield return Wait(() => temporary.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Recover, 8, "Actual recovery not reached.");
        var previous = temporary.ReactionHandle; float pose = temporary.SampledNormalizedTime; int impacts = basic.ImpactCount;
        Bridge.SetFrozen(true); yield return new WaitForSeconds(.3f);
        Require(temporary.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Recover && Mathf.Abs(temporary.SampledNormalizedTime - pose) < .0001f && Bridge.CurrentMotionRole == EnemyMotionRole.Frozen, "Freeze changed remaining recovery.");
        Bridge.SetFrozen(false); yield return null;
        Require(!Bridge.OwnsMotion(previous) && temporary.ReactionHandle != previous && Bridge.CurrentMotionRole == EnemyMotionRole.Reaction, "Thaw revived an invalid reaction handle.");
        var resumed = temporary.ReactionHandle;
        actor.Health.TakeDamage(new DamageInfo(1, actor.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 778001, playerAttackKind: PlayerAttackKind.Weak));
        yield return null; Require(temporary.ReactionHandle == resumed && temporary.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Recover && basic.ImpactCount == impacts, "Recover hit restarted reaction or released old damage.");
        yield return Wait(() => !temporary.BlocksActions, 8, "Recovery did not return after freeze/hit.");
        Fresh(); temporary = actor.GetComponent<CrustaspikanTemporaryReaction>(); Require(temporary.TryPlayGroggy(.8f), "Recovery death fixture rejected.");
        yield return Wait(() => temporary.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Recover, 8, "Death fixture did not recover.");
        previous = temporary.ReactionHandle; actor.Health.TakeDamage(new DamageInfo(2000000, actor.transform.position, player.gameObject, Vector3.forward)); yield return null;
        yield return Wait(() => Bridge.CurrentMotionRole == EnemyMotionRole.Death && Bridge.TryReadMotion(Bridge.CurrentMotionHandle, out var deathSample)
            && deathSample.Clip == profile.ResolveClip(profile.Find("Death")), 2, "Actual recovery-preempting death clip not observed.");
        Require(actor.Health.IsDead && !temporary.BlocksActions && !Bridge.OwnsMotion(previous) && Bridge.CurrentMotionRole == EnemyMotionRole.Death, "Death retained the recovering sampler.");
        Record("REACT-02-edges", new { freezeDuringRecover = true, remainingRecoveryResumed = true, actualHealthHit = true, deathDuringRecover = true });
    }
    static IEnumerator HitResponse()
    {
        activeCase = "HIT-BINDING"; Fresh(); var support = Begin("Roar1", EnemyMotionRole.Support);
        Bridge.PlayResolvedHit(new DamageInfo(1, actor.transform.position, player.gameObject, Vector3.forward));
        File.WriteAllText(Path.Combine(plan.output, "hit-binding.json"), JsonConvert.SerializeObject(new {
            consumer = "PlayResolvedHit -> PlayHit", actorLeased = actor.IsLeased, ownedProfileValid = Bridge.UsesOwnedMotion,
            missingGetHit = profile.Find("GetHit") == null, requiredGetHitReactionPresent = profile.Find("GetHitReaction") != null,
            roleAfter = Bridge.CurrentMotionRole.ToString(), previousSupportStillOwned = Bridge.OwnsMotion(support)
        }, Formatting.Indented));
        yield return Wait(() => Bridge.CurrentMotionRole == EnemyMotionRole.Reaction
            && Bridge.TryReadMotion(Bridge.CurrentMotionHandle, out var hit) && hit.Clip == profile.ResolveClip(profile.Find("GetHitReaction")),
            2, "Allowed resolved hit did not play the required native reaction binding.");
        var handle = Bridge.CurrentMotionHandle;
        Require(!Bridge.OwnsMotion(support), "Resolved hit retained the prior support owner.");
        yield return Wait(() => Bridge.GetMotionResult(handle).IsTerminal, 5, "Allowed resolved hit did not complete.");
        Require(Bridge.GetMotionResult(handle).State == EnemyMotionState.Completed && !actor.Movement.IsActionLocked,
            "Resolved hit failed to return its motion/lock.");
        Record("REACT-hit-binding", new { publicConsumer = "PlayResolvedHit", actualGetHitClip = true, preemptedSupport = true, completed = true });
    }
    static IEnumerator LegacyDeath()
    {
        activeCase = "LEGACY-DEATH";
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/SpiderBrood_Formickarce.asset");
        Require(definition?.IsValid == true && definition.ActorPrefab.AnimationBridge.PlaybackProfile == null, "Saved legacy definition/profile missing.");
        var service = EnemySpawnService.Current; Require(service != null, "Actual spawn service missing.");
        var catalog = ScriptableObject.CreateInstance<EnemyCatalog>(); owned.Add(catalog); catalog.Configure(new[] { definition });
        Require(service.RegisterAdditionalCatalog(catalog, out _), "Legacy definition registration rejected.");
        EnemyActor normal = null;
        try
        {
            var request = new EnemySpawnRequest(definition, room.ArenaCenter + Vector3.right * 9, Quaternion.identity, player.transform, null, player.transform, null, 1, 1, 779001);
            Require(service.TrySpawn(request, out normal) && !normal.AnimationBridge.UsesOwnedMotion && !normal.AnimationBridge.HasInvalidMotionProfile, "Normal Actor entered boss playback.");
            normal.AI.enabled = false; normal.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; uint lease = normal.LeaseVersion;
            normal.Health.TakeDamage(new DamageInfo(normal.Health.MaxHp + 1, normal.transform.position, player.gameObject, Vector3.forward));
            yield return Wait(() => normal.Animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == definition.AnimationProfile.Death && c.weight > .01f)
                || normal.Animator.GetNextAnimatorClipInfo(0).Any(c => c.clip == definition.AnimationProfile.Death && c.weight > .01f), 2, "Actual normal death clip not observed.");
            Require(normal.Health.IsDead, "Legacy logical death missing."); service.Release(normal); yield return null;
            var prior = normal; normal = null;
            Require(service.TrySpawn(request, out normal) && normal == prior && normal.LeaseVersion > lease && !normal.Health.IsDead && normal.Animator.speed > .99f
                && normal.AnimationBridge.PlaybackProfile == null && !normal.AnimationBridge.CurrentMotionHandle.IsValid, "Legacy death/pool reuse changed playback contract.");
            Record("LEGACY-01-death", new { definition = definition.EnemyId, actualDeathClip = true, reused = true, previousLease = lease, currentLease = normal.LeaseVersion });
        }
        finally { if (normal != null && normal.IsLeased) service.Release(normal); }
    }
    static void Finish(string error)
    {
        if (plan == null || plan.phase == "returning") return;
        try { string cleanup = DisposeRoutines(); if (cleanup.Length > 0) error = (error ?? "") + cleanup;
            if (error == null && checks.Count == 0) error = "No runtime case executed.";
            Write(error == null ? "PASS_SCOPED" : "FAIL", error); room?.Exit(true); room = null; if (rt != null) rt.Release(); }
        finally { plan.phase = "returning"; plan.deadline = EditorApplication.timeSinceStartup + 120; SavePlan(); if (OwnPlay) EditorApplication.isPlaying = false; }
    }
    static void Changed(PlayModeStateChange state)
    {
        if (plan == null) return;
        if (state == PlayModeStateChange.ExitingPlayMode && plan.phase != "returning" && plan.phase != "deferred") Finish("Play interrupted.");
        if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key + "reloadLocked", false)) { SessionState.SetBool(Key + "reloadLocked", false); EditorApplication.UnlockReloadAssemblies(); }
        if (state == PlayModeStateChange.EnteredEditMode && plan.phase != "returning" && plan.phase != "deferred") { plan.phase = "returning"; plan.deadline = EditorApplication.timeSinceStartup + 120; SavePlan(); }
    }
    static void ReturnEditor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) { DeferReturnIfExpired(); return; }
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if (!string.IsNullOrEmpty(env) && !string.Equals(env, Account, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(prepared) && !string.Equals(prepared, Account, StringComparison.OrdinalIgnoreCase)) { DeferReturnIfExpired(); return; }
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.Clear();
        Time.captureDeltaTime = plan.capture; Time.timeScale = plan.scale; if (Time.fixedDeltaTime != plan.fixedStep) Time.fixedDeltaTime = plan.fixedStep; Application.runInBackground = plan.background;
        InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)Enum.Parse(typeof(InputSettings.BackgroundBehavior), plan.input);
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(plan.previousStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart); IsolatedSavePlayGuard.UseRealAccount();
        bool returned = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "")) && JToken.DeepEquals(plan.scenes, Scenes());
        File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = returned ? "PASS" : "FAIL", ["beforeScenes"] = plan.scenes, ["afterScenes"] = Scenes(), ["guardChoice"] = IsolatedSavePlayGuard.RequiresAccountChoice,
            ["environment"] = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), ["prepared"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), ["startScene"] = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), ["ownedObjects"] = owned.Count }.ToString());
        SessionState.EraseString(Key + "plan"); plan = null; actor = null; basic = null; composite = null; loco = null; host = null; camera = null; rt = null; pixels = null; player = null; profile = null;
    }
    static void DeferReturnIfExpired()
    {
        if (EditorApplication.timeSinceStartup <= plan.deadline) return;
        File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = "DEFERRED_OTHER_EDITOR_OWNER", ["reason"] = "Return deadline elapsed; foreign scene/account settings preserved." }.ToString());
        plan.phase = "deferred"; SavePlan();
    }
    public static string ResumeReturn()
    {
        Require(plan != null && plan.phase == "deferred", "No deferred return.");
        plan.phase = "returning"; plan.deadline = EditorApplication.timeSinceStartup + 120; SavePlan(); return "Own return resumed; guard checks remain active.";
    }
}
