using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;

[InitializeOnLoad]
public static class PlayerKnockdownVerifier
{
    const string Key = "PlayerKnockdownVerifier";
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly List<object> measurements = new List<object>();
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static int frame;
    static double deadline;
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    public static string Progress => SessionState.GetString(Key + ".progress", "");
    static PlayerKnockdownVerifier() { EditorApplication.playModeStateChanged += Changed; }

    static void Check(bool ok, string name)
    { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
    static void StepName(string name) => SessionState.SetString(Key + ".progress", name);
    static void Save(string name, string status) => File.WriteAllText(Path.Combine(Output, name),
        JsonConvert.SerializeObject(new { status, checks, measurements, errors }, Formatting.Indented));

    public static void VerifyAssets(string output)
    {
        Directory.CreateDirectory(output); SessionState.SetString(Key + ".output", output);
        checks.Clear(); measurements.Clear(); errors.Clear();
        var prefab = PrefabUtility.LoadPrefabContents(PlayerKnockdownBuilder.PrefabPath);
        var graph = default(PlayableGraph);
        try
        {
            Check(!prefab.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0), "Player prefab has no missing script");
            var set = AssetDatabase.LoadAssetAtPath<PlayerKnockdownAnimationSet>(PlayerKnockdownBuilder.SetPath);
            Check(prefab.GetComponent<PlayerKnockdownController>()?.AnimationSet == set && set != null, "Saved prefab points to the saved animation set");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerKnockdownBuilder.ControllerPath);
            Check(controller.layers.Last().name == PlayerKnockdownAnimationSet.LayerName, "Reaction is the highest full-body override layer");
            var layer = controller.layers.Last();
            Check(layer.avatarMask == null && layer.defaultWeight == 0 && !layer.iKPass, "Reaction layer defaults to inactive with authored full-body pose");
            Check(layer.stateMachine.states.All(s => s.state.transitions.Length == 0), "Reaction states have no automatic transitions");
            var hold = layer.stateMachine.states.First(s => s.state.name == PlayerKnockdownAnimationSet.HoldState).state;
            Check(hold.timeParameterActive && hold.timeParameter == PlayerKnockdownAnimationSet.HoldTimeParameter && hold.speed == 0, "Only the hold state freezes its clip time");
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Check(animator != null && animator.isHuman, "Current player has a valid humanoid avatar");
            prefab.SetActive(true); animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false; animator.Rebind();
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
            var bones = new[] { HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightFoot, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
            Vector3[] Pose(AnimationClip clip, float time)
            {
                graph = PlayableGraph.Create("Owned knockdown compatibility sample"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var p = AnimationClipPlayable.Create(graph, clip); p.SetApplyFootIK(false); p.SetApplyPlayableIK(false);
                var outputPort = AnimationPlayableOutput.Create(graph, "Source", animator); outputPort.SetSourcePlayable(p);
                graph.Play(); p.SetTime(time); graph.Evaluate(0);
                Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                float hipHeight = prefab.transform.InverseTransformPoint(hips).y;
                Check(hipHeight < .35f,
                    "Sampled down pose has a low hip: " + clip.name + " height " + hipHeight);
                var points = bones.Select(b => animator.GetBoneTransform(b).position - hips).ToArray();
                graph.Destroy(); graph = default; return points;
            }
            foreach (var fall in set.falls)
            {
                Check(fall.IsValid, "Humanoid fall clip: " + fall.id);
                var end = Pose(fall.clip, fall.clip.length * .9999f);
                foreach (var rise in set.directionalRises)
                {
                    Check(rise.IsValid && rise.poseId == fall.poseId, "Registered compatible pose: " + fall.id + " -> " + rise.id);
                    var start = Pose(rise.clip, 0); float max = end.Zip(start, (a, b) => Vector3.Distance(a, b)).Max();
                    measurements.Add(new { fall = fall.id, rise = rise.id, maxRelativeBoneError = max });
                    Check(max < .015f, "Measured pose continuity: " + fall.id + " -> " + rise.id);
                }
            }
            foreach (var motion in set.falls.Concat(set.directionalRises))
            {
                float previous = 0;
                for (int i = 0; i <= 100; i++)
                {
                    float value = motion.travel.Evaluate(i / 100f);
                    Check(value >= previous - .001f && value >= -.001f && value <= 1.001f, "Bounded monotonic travel " + motion.id + " sample " + i);
                    previous = value;
                }
            }
            string pose = set.defaultRise.poseId;
            Check(set.SelectRise(pose, Vector2.zero) == set.defaultRise, "Released input selects default rise");
            foreach (var direction in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                Check(Vector2.Dot(set.SelectRise(pose, direction).direction, direction) > .99f, "Cardinal selection " + direction);
            foreach (var direction in new[] { new Vector2(1,1), new Vector2(-1,1), new Vector2(1,-1), new Vector2(-1,-1) })
            {
                var expected = set.SelectRise(pose, direction);
                Check(expected.direction == (direction.y > 0 ? Vector2.up : Vector2.down), "Diagonal uses stable registered tie order " + direction);
                Check(set.SelectRise(pose, direction + new Vector2(.000001f, 0)) == expected,
                    "Coordinate rounding cannot change the diagonal tie " + direction);
            }
            Check(set.SelectRise("unsupported-pose", Vector2.left) == null, "Incompatible pose cannot select a rise");
            Save("asset-results.json", "PASS");
        }
        catch (Exception e) { errors.Add(e.ToString()); Save("asset-results.json", "FAIL"); throw; }
        finally { if (graph.IsValid()) graph.Destroy(); PrefabUtility.UnloadPrefabContents(prefab); }
    }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        Directory.CreateDirectory(output); SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        StepName("Product boot"); AssetDatabase.DisallowAutoRefresh(); SessionState.SetBool(Key + ".refresh", true);
        EditorApplication.EnterPlaymode();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeBoot()
    {
        if (SessionState.GetBool(Key, false)) Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable,
            IsolatedSavePlayGuard.ValidateDirectory(Path.Combine(Output, "IsolatedAccount")));
    }
    static void Changed(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); measurements.Clear(); errors.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 300;
            SessionState.SetBool(Key + ".background", Application.runInBackground); SessionState.SetInt(Key + ".fps", Application.targetFrameRate);
            Application.runInBackground = true; Application.targetFrameRate = 60;
            EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + ".reload", true);
            work.Clear(); work.Push(VerifyPlay()); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Application.targetFrameRate = SessionState.GetInt(Key + ".fps", -1);
            if (SessionState.GetBool(Key + ".reload", false)) { EditorApplication.UnlockReloadAssemblies(); SessionState.EraseBool(Key + ".reload"); }
            if (Status == "RUNNING") { SessionState.SetString(Key + ".status", "ABORTED"); Save("play-results.json", "ABORTED"); }
        }
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            string start = SessionState.GetString(Key + ".startScene", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            SessionState.SetBool(Key, false);
            if (SessionState.GetBool(Key + ".refresh", false)) { AssetDatabase.AllowAutoRefresh(); SessionState.EraseBool(Key + ".refresh"); }
        }
    }
    static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException("Bounded Play verification deadline");
            while (work.Count > 0)
            {
                var iterator = work.Peek();
                if (!iterator.MoveNext()) { work.Pop(); continue; }
                if (iterator.Current is IEnumerator nested) { work.Push(nested); continue; }
                return;
            }
            Check(errors.Count == 0, "No runtime errors: " + string.Join(" | ", errors)); Finish("PASS");
        }
        catch (Exception e) { errors.Add(e.ToString()); Finish("FAIL"); }
    }
    static void Finish(string status)
    { SessionState.SetString(Key + ".status", status); Save("play-results.json", status); EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); }
    static IEnumerator Wait(float seconds) { float end = Time.time + seconds; while (Time.time < end) yield return null; }
    static IEnumerator Until(Func<bool> condition, float timeout, string name)
    { float end = Time.time + timeout; while (!condition() && Time.time < end) yield return null; Check(condition(), name); }
    static void Warp(PlayerActorRuntime actor, Vector3 position)
    {
        actor.CharacterController.enabled = false; actor.transform.SetPositionAndRotation(position, Quaternion.identity);
        actor.CharacterController.enabled = true; actor.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms();
    }
    static void Grade(EnemyRank rank, EnemyGradeType grade)
        => typeof(EnemyRank).GetProperty("GradeType").GetSetMethod(true).Invoke(rank, new object[] { grade });
    static void Set(object target, string field, object value)
        => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static void Stick(Gamepad pad, Vector2 value) => InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = value });
    static Vector2 CameraInput(PlayerMovement movement, Vector2 local)
    {
        Vector3 world = movement.transform.TransformDirection(new Vector3(local.x, 0, local.y));
        return new Vector2(Vector3.Dot(world, movement.ResolveMoveDirection(Vector2.right)), Vector3.Dot(world, movement.ResolveMoveDirection(Vector2.up)));
    }
    static void Capture(string name)
    {
        var camera = Camera.main; if (camera == null) throw new InvalidOperationException("Game camera missing");
        var target = RenderTexture.GetTemporary(1280, 720, 24); var prior = RenderTexture.active; Texture2D image = null;
        try
        {
            var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = target;
            image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), image.EncodeToPNG());
        }
        finally { RenderTexture.active = prior; RenderTexture.ReleaseTemporary(target); if (image != null) UnityEngine.Object.DestroyImmediate(image); }
    }

    static IEnumerator VerifyPlay()
    {
        GameObject source = null, wall = null, otherOwner = null, platform = null; Gamepad pad = null; Keyboard keyboard = null;
        EnemyAbilitySet singleAbilitySet = null;
        EnemyThemeDebugArena[] protectedArenas = Array.Empty<EnemyThemeDebugArena>();
        EnemyAbilityDefinition ability = null; EnemyActor enemy = null; EnemySpawnService spawn = null;
        PlayerActorRuntime actor = null; float originalScale = Time.timeScale;
        try
        {
            yield return Until(() => PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching
                && PersistentSceneFlow.Instance.CurrentSubSceneName == "HideoutScene" && Overburst.Persistence.AccountBootstrap.Ready, 40, "PersistentScene product boot completed");
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "Product uses isolated account");
            actor = PlayerContext.GetOrCreate().CurrentActor;
            Check(actor != null && actor.GetComponent<PlayerKnockdownController>() != null, "Product spawned the modified player prefab");
            var arena = EnemyThemeTrialHarness.Current; Check(arena != null, "Arena fixture available"); if (!arena.InArena) arena.ToggleArena();
            yield return Wait(.5f);
            var reaction = actor.GetComponent<PlayerKnockdownController>(); var health = actor.Health; var movement = actor.Movement;
            var state = actor.GetComponent<PlayerStateCoordinator>(); var melee = actor.GetComponent<MeleeRuntime>(); var animator = actor.GetComponentInChildren<Animator>();
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Equip actual greatsword runtime");
            var origin = actor.transform.position; pad = InputSystem.AddDevice<Gamepad>(); Stick(pad, Vector2.zero);
            source = new GameObject("Owned knockdown source fixture"); var rank = source.AddComponent<EnemyRank>(); Grade(rank, EnemyGradeType.Elite);
            ability = ScriptableObject.CreateInstance<EnemyAbilityDefinition>(); Set(ability, "telegraphedStrongAttack", true);
            health.SetMaxHp(1000000, true);
            int sequence = 20000;
            DamageInfo Hit() => new DamageInfo(10, actor.transform.position, source, Vector3.back,
                sourceAttackSequenceId: ++sequence, enemyAbility: ability);
            void Reset() { Stick(pad, Vector2.zero); reaction.ResetReaction(); health.ResetHealth(); Warp(actor, origin); }
            StepName("Damage exclusions");
            foreach (var grade in new[] { EnemyGradeType.Normal, EnemyGradeType.Boss })
            { Grade(rank, grade); health.TakeDamage(Hit()); Check(!reaction.IsActive, grade + " cannot knock down the player"); }
            Grade(rank, EnemyGradeType.Elite);
            Set(ability, "telegraphedStrongAttack", false); health.TakeDamage(Hit()); Check(!reaction.IsActive, "Elite normal melee preserves existing reaction");
            Set(ability, "telegraphedStrongAttack", true); Set(ability, "executionMode", EnemyAbilityExecutionMode.Projectile);
            health.TakeDamage(Hit()); Check(!reaction.IsActive, "Strong projectile cannot knock down"); Set(ability, "executionMode", EnemyAbilityExecutionMode.MeleeArc);
            var tick = Hit(); tick.isDamageOverTime = true; health.TakeDamage(tick); Check(!reaction.IsActive, "Damage over time cannot knock down");
            tick = Hit(); tick.triggersOnHitEffects = false; health.TakeDamage(tick); Check(!reaction.IsActive, "Derived hit cannot knock down");
            tick = Hit(); tick.damage = 0; health.TakeDamage(tick); Check(!reaction.IsActive, "Zero resolved damage cannot knock down");
            Reset(); Grade(rank, EnemyGradeType.GreaterElite); health.TakeDamage(Hit()); Check(reaction.IsActive, "GreaterElite heavy also knocks down");
            Reset(); Grade(rank, EnemyGradeType.Elite);
            var originalSet = reaction.AnimationSet; Set(reaction, "animationSet", null);
            try { health.TakeDamage(Hit()); Check(!reaction.IsActive && state.CurrentCondition == PlayerConditionState.Normal, "Missing animation data never creates a stun lock"); }
            finally { Set(reaction, "animationSet", originalSet); }

            StepName("Evade cancels before resolved damage"); Reset(); keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.LeftShift));
            var evade = actor.GetComponent<PlayerEvadeController>();
            float evadeDeadline = Time.time + 1;
            while (!evade.IsInvincible && Time.time < evadeDeadline) yield return null;
            var facade = actor.GetComponent<PlayerInputFacade>();
            Check(evade.IsInvincible, "Evade starts from actual input: held=" + facade.EvadeHeld
                + " pending=" + facade.CombatInputs.HasEvade + " condition=" + state.CurrentCondition
                + " gameplay=" + facade.IsGameplayEnabled + " uiBlocked=" + GameplayInputBlocker.IsGameplayInputBlocked);
            float evadeHp = health.CurrentHp; health.TakeDamage(Hit());
            Check(health.CurrentHp == evadeHp && !reaction.IsActive, "Invincible evade cancels HP damage and knockdown together");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); evade.CancelForKnockdown(); yield return Wait(.2f);

            StepName("Attack interruption and stable hold"); Reset(); PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            yield return Wait(1.2f); melee.SetManualInputEnabled(true);
            Check(melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput, null, Vector3.forward), out _) == WeaponActionResult.Accepted, "Actual weak attack started");
            var initial = Hit(); health.TakeDamage(initial);
            Check(reaction.Phase == PlayerKnockdownPhase.Falling && !melee.IsAttackInProgress && state.CurrentCondition == PlayerConditionState.Stunned, "Elite heavy cancels actual attack before starting the fall");
            Check(!movement.BeginLootAutoMove(origin + Vector3.forward), "Automatic loot movement rejected while down");
            yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Grounded, 3, "Fall reaches grounded hold");
            Capture("grounded"); float before = health.CurrentHp;
            health.TakeDamage(initial); Check(health.CurrentHp < before && reaction.Phase == PlayerKnockdownPhase.Grounded, "Additional damage applies without restarting the hold");
            int reactionLayer = animator.GetLayerIndex(PlayerKnockdownAnimationSet.LayerName); float poseTime = animator.GetCurrentAnimatorStateInfo(reactionLayer).normalizedTime;
            OverburstTimeEffectArbiter.SetPaused(true); var pausedPosition = actor.transform.position;
            for (int i = 0; i < 12; i++) yield return null;
            Check(reaction.Phase == PlayerKnockdownPhase.Grounded && Vector3.Distance(pausedPosition, actor.transform.position) < .002f
                && Mathf.Abs(poseTime - animator.GetCurrentAnimatorStateInfo(reactionLayer).normalizedTime) < .001f, "Pause freezes phase, pose and motor displacement together");
            OverburstTimeEffectArbiter.SetPaused(false);
            yield return Until(() => !reaction.IsActive, 3, "No input automatically returns to Ready");
            before = health.CurrentHp; health.TakeDamage(Hit());
            Check(!reaction.IsActive && health.CurrentHp < before, "Recovery protection blocks only knockdown, never HP damage");
            yield return Wait(.65f); health.TakeDamage(initial); Check(!reaction.IsActive, "Same attack sequence remains rejected after protection expires");

            StepName("Direction input through real input facade");
            foreach (var direction in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right,
                new Vector2(1,1), new Vector2(-1,1), new Vector2(1,-1), new Vector2(-1,-1) })
            {
                Reset(); yield return Wait(.1f); health.TakeDamage(Hit());
                yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Grounded, 3, "Directional fall reaches hold " + direction);
                Stick(pad, CameraInput(movement, direction.normalized)); yield return null; yield return null;
                Check(Vector2.Dot(reaction.PendingRiseInput, direction.normalized) > .95f, "Camera-relative Move action resolves in lying frame " + direction);
                var expected = reaction.AnimationSet.SelectRise(reaction.AnimationSet.defaultRise.poseId, direction);
                yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Rising, 1, "Directional rise starts " + direction);
                Check(reaction.ActiveMotionId == expected.id, "Nearest compatible rise selected " + direction);
                var start = actor.transform.position; var chosen = reaction.ActiveMotionId; Stick(pad, CameraInput(movement, -direction.normalized));
                yield return Wait(.15f); Check(reaction.ActiveMotionId == chosen, "Rise cannot switch mid-animation " + direction);
                if (direction == Vector2.left) Capture("rise_left"); if (direction == Vector2.right) Capture("rise_right");
                Stick(pad, Vector2.zero);
                yield return Until(() => !reaction.IsActive, 2, "Directional rise completes " + direction);
                var delta = actor.transform.position - start; delta.y = 0;
                Check(delta.magnitude <= reaction.AnimationSet.riseDistance + .035f, "Rise planar movement remains bounded " + direction);
                measurements.Add(new { direction = direction.ToString(), rise = chosen, distance = delta.magnitude });
                Check(state.CurrentCondition == PlayerConditionState.Normal && state.CurrentAction != PlayerActionState.Attack, "Owned locks released " + direction);
            }

            StepName("Release input, wall, airborne and lifecycle"); Reset(); yield return Wait(.1f); health.TakeDamage(Hit());
            yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Grounded, 3, "Release fixture reaches hold");
            Stick(pad, CameraInput(movement, Vector2.right)); yield return null; yield return null; Stick(pad, Vector2.zero); yield return null; yield return null;
            yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Rising, 1, "Released direction starts default rise");
            Check(reaction.ActiveMotionId == reaction.AnimationSet.defaultRise.id, "Latest released input discards the previous direction");
            yield return Until(() => !reaction.IsActive, 2, "Released default rise completes");
            Reset(); wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Owned reaction wall";
            wall.transform.position = origin + new Vector3(-.55f, 1, -.5f); wall.transform.localScale = new Vector3(.15f, 3, 4); Physics.SyncTransforms();
            health.TakeDamage(Hit()); yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Grounded, 3, "Wall fixture reaches hold");
            Stick(pad, CameraInput(movement, Vector2.left)); yield return null; yield return null;
            yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Rising, 1, "Wall-blocked rise starts"); var wallStart = actor.transform.position; Stick(pad, Vector2.zero);
            yield return Until(() => !reaction.IsActive, 2, "Wall-blocked rise still completes");
            Check(wallStart.x - actor.transform.position.x < .30f && actor.CharacterController.enabled, "Wall truncates rise travel without disabling the collider");
            UnityEngine.Object.Destroy(wall); wall = null;
            Reset(); Warp(actor, origin + Vector3.up * 30); yield return null; while ((sequence + 1) % 3 != 2) sequence++;
            health.TakeDamage(Hit()); yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Grounded, 3, "Airborne fall reaches the held end pose");
            Check(!movement.IsGrounded, "Held pose can wait while the motor is still falling");
            yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Rising, 4, "Airborne reaction lands before rising"); Check(movement.IsGrounded, "Rise begins only after grounded hold");
            yield return Until(() => !reaction.IsActive, 2, "Airborne lifecycle completes");
            Reset(); platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.name = "Owned reaction moving platform";
            platform.layer = LayerMask.NameToLayer("Ground");
            platform.transform.position = origin + Vector3.up * 1.5f; platform.transform.localScale = new Vector3(8, 1, 8);
            var platformBody = platform.AddComponent<Rigidbody>(); platformBody.isKinematic = true;
            Warp(actor, origin + Vector3.up * 2); yield return Wait(.3f);
            Check(movement.Motor.CurrentPlatform == platform.transform, "Actual motor attaches to the moving platform: grounded="
                + movement.IsGrounded + " support=" + movement.Motor.CurrentPlatform + " position=" + actor.transform.position);
            health.TakeDamage(Hit());
            while (reaction.Phase == PlayerKnockdownPhase.Falling)
            {
                platform.transform.position += Vector3.right * (.3f * Time.deltaTime);
                platform.transform.Rotate(0, 20 * Time.deltaTime, 0); Physics.SyncTransforms(); yield return null;
            }
            Check(reaction.Phase == PlayerKnockdownPhase.Grounded && movement.IsGrounded, "Falling preserves moving-platform grounding");
            Stick(pad, CameraInput(movement, Vector2.left)); yield return null; yield return null;
            yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Rising, 1, "Platform-relative rise starts");
            Vector3 PlatformPosition() => Quaternion.Inverse(platform.transform.rotation) * (actor.transform.position - platform.transform.position);
            var platformStart = PlatformPosition(); var platformRise = reaction.ActiveMotionId; Stick(pad, Vector2.zero);
            while (reaction.IsActive)
            {
                platform.transform.position += Vector3.right * (.3f * Time.deltaTime);
                platform.transform.Rotate(0, 50 * Time.deltaTime, 0); Physics.SyncTransforms(); yield return null;
            }
            var platformDelta = PlatformPosition() - platformStart;
            Check(platformRise == "rise_left_up" && platformDelta.x < -.25f && Mathf.Abs(platformDelta.z) < .08f,
                "Rise follows the rotating body frame while keeping its selected animation: " + platformDelta);
            measurements.Add(new { fixture = "rotating-platform", rise = platformRise, deltaMeters = platformDelta.ToString("F4") });
            UnityEngine.Object.Destroy(platform); platform = null; Reset(); yield return Wait(.1f);
            platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.name = "Owned reaction slope";
            platform.layer = LayerMask.NameToLayer("Ground");
            platform.transform.position = origin + Vector3.up * 1.5f; platform.transform.localScale = new Vector3(8, 1, 8);
            platform.transform.rotation = Quaternion.Euler(12, 0, 0); Physics.SyncTransforms(); Warp(actor, origin + Vector3.up * 2.1f); yield return Wait(.3f);
            health.TakeDamage(Hit()); yield return Until(() => reaction.Phase == PlayerKnockdownPhase.Grounded, 3, "Slope fall reaches hold");
            yield return null;
            float slopeError = Vector3.Angle(animator.transform.up, movement.GroundNormal);
            Check(movement.IsGrounded && slopeError < 2 && Vector3.Angle(actor.transform.up, Vector3.up) < .1f,
                "Down visual follows the slope normal with an upright collision capsule");
            measurements.Add(new { fixture = "slope", visualNormalErrorDegrees = slopeError });
            Capture("slope_hold");
            yield return Until(() => !reaction.IsActive, 2, "Slope rise completes");
            Check(Vector3.Angle(animator.transform.up, Vector3.up) < 1, "Rise restores the authored upright visual root");
            UnityEngine.Object.Destroy(platform); platform = null;
            Reset(); yield return Wait(.1f); health.TakeDamage(Hit());
            otherOwner = new GameObject("Owned independent stun fixture"); state.RequestStun(otherOwner); reaction.ResetReaction();
            Check(state.CurrentCondition == PlayerConditionState.Stunned, "Reaction cleanup preserves another stun owner"); state.ReleaseStun(otherOwner);
            state.RequestCondition(otherOwner, PlayerConditionState.InputBlocked); health.TakeDamage(Hit()); reaction.ResetReaction();
            Check(state.CurrentCondition == PlayerConditionState.InputBlocked, "Reaction cleanup preserves an independent UI block"); state.ReleaseCondition(otherOwner);
            UnityEngine.Object.Destroy(otherOwner); otherOwner = null;
            health.TakeDamage(Hit()); movement.ResetMotionAfterTeleport(); Check(!reaction.IsActive && state.CurrentCondition == PlayerConditionState.Normal, "Teleport releases reaction state");
            health.TakeDamage(Hit()); health.ResetHealth(); Check(!reaction.IsActive, "Health reset releases reaction state");
            health.TakeDamage(Hit()); reaction.enabled = false; Check(!reaction.IsActive && state.CurrentCondition == PlayerConditionState.Normal, "Disable releases only owned state"); reaction.enabled = true;
            health.TakeDamage(Hit()); Check(reaction.IsActive, "Re-enabled controller subscribes once and accepts the next hit"); reaction.ResetReaction();
            health.TakeDamage(Hit());
            Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "External equipment restoration replaces the weapon root");
            yield return null; yield return null;
            Check(!reaction.IsActive, "External weapon-root replacement releases the reaction safely");
            Reset(); yield return Wait(1.2f); melee.SetManualInputEnabled(true);
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "Actual heavy attack started");
            health.TakeDamage(Hit());
            Check(reaction.IsActive && !melee.IsHeavyAttackInProgress, "Elite hit cancels an actual heavy and its parry window");
            Reset();

            StepName("Actual deployed elite attack"); Reset(); yield return Wait(.2f);
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "Product enemy spawn service");
            foreach (var table in arena.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), "Register product catalog: " + error);
            foreach (var execution in new[] { EnemyAbilityExecutionMode.MeleeArc, EnemyAbilityExecutionMode.AreaSlam, EnemyAbilityExecutionMode.Charge })
            {
                Reset(); yield return Wait(.2f);
                var definition = arena.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null && d.Grade.GradeType == EnemyGradeType.Elite
                    && Enumerable.Range(0, d.AbilitySet.Count).Any(i => d.AbilitySet.GetAbility(i).IsMeleeStrongAttack && d.AbilitySet.GetAbility(i).ExecutionMode == execution));
                int abilityIndex = Enumerable.Range(0, definition.AbilitySet.Count).First(i => definition.AbilitySet.GetAbility(i).IsMeleeStrongAttack && definition.AbilitySet.GetAbility(i).ExecutionMode == execution);
                var actualAbility = definition.AbilitySet.GetAbility(abilityIndex);
                float distance = Mathf.Max(1.2f, actualAbility.MinimumRange + .2f);
                Check(spawn.TrySpawn(new EnemySpawnRequest(definition, origin + Vector3.forward * distance, Quaternion.LookRotation(Vector3.back), actor.transform), out enemy), "Spawn deployed elite " + execution);
                enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(1000000, true); yield return Wait(.3f);
                singleAbilitySet = ScriptableObject.CreateInstance<EnemyAbilitySet>(); Set(singleAbilitySet, "abilitySetId", "OwnedKnockdownVerification"); Set(singleAbilitySet, "abilities", new[] { actualAbility });
                enemy.AbilityController.Configure(singleAbilitySet, enemy.RuntimeStats.DamageMultiplier, 1);
                enemy.AbilityController.BeginStrongOnlyPass();
                DamageInfo confirmed = default; void Resolved(CombatHealth h, DamageInfo info, float actual, bool fatal) { if (info.source == enemy.gameObject) confirmed = info; }
                health.OnDamageResolved += Resolved;
                try
                {
                    Check(enemy.AbilityController.TryStart(actor.transform), "Actual elite heavy starts through ability routing: " + execution);
                    yield return Until(() => reaction.IsActive, 8, "Actual collider-resolved elite heavy knocks down: " + execution);
                    Check(confirmed.sourceAttackSequenceId > 0 && confirmed.enemyAbility == actualAbility, "Executor propagates sequence and ability: " + execution);
                    Capture("actual_elite_" + execution);
                    yield return Until(() => !reaction.IsActive, 4, "Actual elite recovery completes: " + execution);
                    if (execution == EnemyAbilityExecutionMode.MeleeArc && actualAbility.IsParryable)
                    {
                        enemy.AbilityController.Cancel(); enemy.AbilityController.Configure(singleAbilitySet, enemy.RuntimeStats.DamageMultiplier, 1);
                        enemy.AbilityController.BeginStrongOnlyPass(); Reset(); yield return Wait(1.2f);
                        Check(enemy.AbilityController.TryStart(actor.transform), "Real elite begins a parryable attack");
                        yield return Until(() => enemy.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()), 5, "Actual enemy enters parry threat window");
                        var parry = actor.GetComponent<PlayerParryController>(); int successes = parry.SuccessCount; float hp = health.CurrentHp; melee.SetManualInputEnabled(true);
                        Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "Heavy opens the actual parry window");
                        yield return Until(() => parry.SuccessCount > successes, 1, "Actual enemy heavy is parried");
                        Check(health.CurrentHp == hp && !reaction.IsActive, "Successful parry preserves HP and excludes knockdown");
                        health.TakeDamage(Hit());
                        Check(reaction.IsActive && !melee.IsHeavyAttackInProgress && animator.updateMode == AnimatorUpdateMode.Normal,
                            "Non-parryable follow-up cancels the parry bridge and restores the game-time animator clock");
                        yield return Until(() => !reaction.IsActive, 5, "Knockdown recovers after cancelling the active parry bridge");
                    }
                }
                finally { health.OnDamageResolved -= Resolved; }
                spawn.Release(enemy); enemy = null; UnityEngine.Object.Destroy(singleAbilitySet); singleAbilitySet = null;
            }
            StepName("Fatal priority and final recovery"); Reset(); yield return Wait(.1f); health.TakeDamage(Hit());
            protectedArenas = UnityEngine.Object.FindObjectsByType<EnemyThemeDebugArena>(FindObjectsSortMode.None);
            foreach (var protectedArena in protectedArenas) protectedArena.ReleasePlayerProtection();
            Check(!health.IsDeathFromDamagePrevented, "Fatal fixture releases the debug arena's survival lease");
            var fatalHit = Hit(); fatalHit.damage = 1000000000; health.TakeDamage(fatalHit);
            Check(health.IsDead && !reaction.IsActive && state.CurrentCondition == PlayerConditionState.Dead, "Fatal damage immediately wins over knockdown");
            health.ResetHealth(); yield return null;
            Check(!reaction.IsActive && state.CurrentCondition == PlayerConditionState.Normal, "Death reset returns to clean Ready state");
            Capture("ready");
        }
        finally
        {
            Time.timeScale = originalScale;
            OverburstTimeEffectArbiter.SetPaused(false);
            foreach (var protectedArena in protectedArenas) if (protectedArena != null && actor != null) protectedArena.ProtectPlayer(actor.Health);
            if (actor != null) actor.GetComponent<PlayerKnockdownController>()?.ResetReaction();
            if (enemy != null && spawn != null) spawn.Release(enemy);
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (source != null) UnityEngine.Object.Destroy(source); if (wall != null) UnityEngine.Object.Destroy(wall);
            if (otherOwner != null) UnityEngine.Object.Destroy(otherOwner); if (ability != null) UnityEngine.Object.Destroy(ability);
            if (platform != null) UnityEngine.Object.Destroy(platform); if (singleAbilitySet != null) UnityEngine.Object.Destroy(singleAbilitySet);
        }
    }
}
