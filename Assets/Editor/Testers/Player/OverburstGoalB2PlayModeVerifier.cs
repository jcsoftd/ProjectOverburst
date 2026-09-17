using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// GOAL B2 Play 검증. 런타임 임시 리그만 생성하고 정식 씬/에셋은 저장하지 않는다.
[InitializeOnLoad]
public static class OverburstGoalB2PlayModeVerifier
{
    private const string ActiveKey = "OverburstGoalB2PlayModeVerifier.Active";
    private const string ResultKey = "OverburstGoalB2PlayModeVerifier.Result";
    private const string FailKey = "OverburstGoalB2PlayModeVerifier.Fail";
    private const float TimeoutSeconds = 300f;
    private static readonly Vector3 FixtureBase = new Vector3(1400f, 0f, 1400f);

    private static readonly List<string> runtimeErrors = new List<string>();
    private static GameObject fixtureRoot;
    private static double startedAt;
    private static int readyFrame;
    private static bool ran;
    private static bool previousRunInBackground;

    static OverburstGoalB2PlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("OVERBURST/Codex/Validate/Player/Validate GOAL B2 Play Mode")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play Mode가 이미 실행 중이다.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != PersistentSceneFlow.PersistentSceneName)
            throw new InvalidOperationException("PersistentScene에서 실행해야 한다. current=" + scene.name);
        if (scene.isDirty)
            throw new InvalidOperationException("PersistentScene이 dirty다. 검증기는 저장하지 않는다.");

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetString(ResultKey, string.Empty);
        SessionState.SetString(FailKey, string.Empty);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            runtimeErrors.Clear();
            startedAt = EditorApplication.timeSinceStartup;
            readyFrame = Time.frameCount + 30;
            ran = false;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Cleanup();
            Application.runInBackground = previousRunInBackground;
            EditorApplication.update -= Update;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        string fail = SessionState.GetString(FailKey, string.Empty);
        string result = SessionState.GetString(ResultKey, string.Empty);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseString(FailKey);
        SessionState.EraseString(ResultKey);
        if (string.IsNullOrEmpty(fail))
            Debug.Log("[OverburstGoalB2PlayModeVerifier] PASS " + result);
        else
            Debug.LogError("[OverburstGoalB2PlayModeVerifier] FAIL " + fail);
    }

    private static void Update()
    {
        if (ran)
            return;
        if (EditorApplication.timeSinceStartup - startedAt > TimeoutSeconds)
        {
            Fail("timeout");
            return;
        }
        if (Time.frameCount < readyFrame)
            return;

        PlayerActorRuntime actor = UnityEngine.Object.FindFirstObjectByType<PlayerActorRuntime>();
        if (actor == null || !actor.gameObject.activeInHierarchy)
            return;

        ran = true;
        try
        {
            string result = RunAll(actor.gameObject);
            if (runtimeErrors.Count > 0)
                throw new InvalidOperationException("runtime errors=" + runtimeErrors.Count + " first=" + runtimeErrors[0]);
            SessionState.SetString(ResultKey, result);
            Cleanup();
            EditorApplication.ExitPlaymode();
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private static string RunAll(GameObject actor)
    {
        OverburstGoalB2MovementValidator.ValidateRuntimeContracts();
        OverburstPlayerMotorValidator.ValidateRuntimeContracts();

        PlayerMovement movement = actor.GetComponent<PlayerMovement>();
        PlayerLocomotion locomotion = actor.GetComponent<PlayerLocomotion>();
        CombatMotionDriver combat = actor.GetComponent<CombatMotionDriver>();
        OverburstCharacterMotor3D actorMotor = actor.GetComponent<OverburstCharacterMotor3D>();
        Require(movement != null && locomotion != null && combat != null && actorMotor != null,
            "Hideout actor B2 components missing.");
        Require(movement.Locomotion == locomotion && movement.CombatMotion == combat && movement.Motor == actorMotor,
            "Hideout actor B2 references invalid.");

        fixtureRoot = new GameObject("__OVERBURST_GOAL_B2_RUNTIME_ONLY__");
        fixtureRoot.transform.position = FixtureBase;
        int groundLayer = LayerMask.NameToLayer("Default");
        if (groundLayer < 0)
            groundLayer = 0;

        PlatformMetrics platform = VerifyMovingPlatform(groundLayer);
        ForceMetrics force = VerifyExternalForce(groundLayer);
        HeightMetrics height = VerifyHeightFoundation(groundLayer);
        CombatMetrics overlap = VerifyCombatMotion();

        return string.Format(
            "platformPos={0:F3} platformRot={1:F2} exit={2:F2} "
            + "force30={3:F3} force60={4:F3} force120={5:F3} forceDiff={6:F3} clear={7:F3} "
            + "foot={8:F4} headroom={9} expand={10} overlapToward={11:F3} overlapAway={12:F3} cast={13:F3} errors=0",
            platform.positionError,
            platform.rotationError,
            platform.exitSpeed,
            force.distance30,
            force.distance60,
            force.distance120,
            force.maxDifference,
            force.clearMagnitude,
            height.footError,
            height.blocked ? 1 : 0,
            height.expanded ? 1 : 0,
            overlap.towardMagnitude,
            overlap.awayMagnitude,
            overlap.castMagnitude);
    }

    private static PlatformMetrics VerifyMovingPlatform(int groundLayer)
    {
        GameObject platform = CreateBox("MovingPlatform", FixtureBase + new Vector3(0f, 0.25f, 0f), new Vector3(10f, 0.5f, 10f), groundLayer);
        OverburstCharacterMotor3D motor = CreateMotorRig("PlatformRig", FixtureBase + new Vector3(2f, 1.5f, 0f), groundLayer);
        const float dt = 1f / 60f;
        motor.AttachToPlatform(platform.transform);
        Vector3 localAnchor = platform.transform.InverseTransformPoint(motor.transform.position);
        Vector3 initialForward = motor.transform.forward;
        for (int i = 0; i < 60; i++)
        {
            platform.transform.position += Vector3.right * (1.2f * dt);
            platform.transform.rotation = Quaternion.AngleAxis(60f * dt, Vector3.up) * platform.transform.rotation;
            Physics.SyncTransforms();
            motor.ProbeGround(dt);
            motor.Step(HoldCommand(), dt);
        }

        Vector3 expected = platform.transform.TransformPoint(localAnchor);
        float positionError = Vector3.Distance(expected, motor.transform.position);
        Vector3 expectedForward = Quaternion.AngleAxis(60f, Vector3.up) * initialForward;
        float rotationError = Vector3.Angle(expectedForward, motor.transform.forward);
        Require(positionError < 0.12f, "moving platform position error=" + positionError);
        Require(rotationError < 1.5f, "moving platform rotation error=" + rotationError);

        platform.transform.position += Vector3.right * (3f * dt);
        Physics.SyncTransforms();
        motor.ProbeGround(dt);
        float trackedSpeed = motor.PlatformVelocity.magnitude;
        motor.DetachFromPlatform(true);
        float exitSpeed = motor.ExternalVelocity.magnitude;
        Require(trackedSpeed > 2.5f && exitSpeed > 2.5f, "platform exit velocity missing: " + exitSpeed);
        Vector3 beforeExitStep = motor.transform.position;
        motor.Step(HoldCommand(), dt);
        Require(Vector3.Distance(beforeExitStep, motor.transform.position) > 0.025f, "platform exit motion missing.");

        OverburstCharacterMotor3D jumpRig = CreateMotorRig(
            "PlatformJumpExitRig",
            platform.transform.position + new Vector3(0f, 1.25f, 0f),
            groundLayer);
        jumpRig.AttachToPlatform(platform.transform);
        platform.transform.position += Vector3.right * (3f * dt);
        Physics.SyncTransforms();
        jumpRig.ProbeGround(dt);
        Vector3 beforeJump = jumpRig.transform.position;
        MotorStepCommand jump = HoldCommand();
        jump.jumpVelocity = 5f;
        jumpRig.Step(jump, dt);
        float jumpHorizontal = Mathf.Abs(jumpRig.transform.position.x - beforeJump.x);
        Require(jumpHorizontal > 0.025f && jumpHorizontal < 0.075f,
            "platform jump exit applied displacement twice=" + jumpHorizontal);
        Require(jumpRig.ExternalVelocity.magnitude > 2.5f, "platform jump did not retain exit velocity.");
        return new PlatformMetrics(positionError, rotationError, exitSpeed);
    }

    private static ForceMetrics VerifyExternalForce(int groundLayer)
    {
        float d30 = SimulateForce(30, groundLayer, 0);
        float d60 = SimulateForce(60, groundLayer, 1);
        float d120 = SimulateForce(120, groundLayer, 2);
        float max = Mathf.Max(Mathf.Abs(d30 - d60), Mathf.Abs(d30 - d120), Mathf.Abs(d60 - d120));
        Require(max < 0.12f, "external force fps difference=" + max);

        OverburstCharacterMotor3D clearRig = CreateMotorRig("ForceClearRig", FixtureBase + new Vector3(0f, 1f, 18f), groundLayer);
        clearRig.AddExternalForce(new Vector3(5f, 2f, 0f));
        clearRig.ClearExternalForces();
        float clear = clearRig.ExternalVelocity.magnitude;
        Require(clear < 0.0001f, "external force clear failed=" + clear);
        return new ForceMetrics(d30, d60, d120, max, clear);
    }

    private static float SimulateForce(int fps, int lane, int groundLayer)
    {
        Vector3 start = FixtureBase + new Vector3(lane * 6f - 6f, 1f, 12f);
        CreateBox("ForceFloor" + fps, start + Vector3.down * 1.25f, new Vector3(5f, 0.5f, 5f), groundLayer);
        OverburstCharacterMotor3D motor = CreateMotorRig("ForceRig" + fps, start, groundLayer);
        motor.AddExternalForce(Vector3.right * 10f);
        float dt = 1f / fps;
        for (int i = 0; i < fps; i++)
        {
            Physics.SyncTransforms();
            motor.ProbeGround(dt);
            motor.Step(HoldCommand(), dt);
        }
        Require(motor.ExternalVelocity.magnitude < 2.1f, "external decay failed at " + fps + "fps.");
        return motor.transform.position.x - start.x;
    }

    private static HeightMetrics VerifyHeightFoundation(int groundLayer)
    {
        Vector3 start = FixtureBase + new Vector3(12f, 1f, 12f);
        CreateBox("HeightFloor", start + Vector3.down * 1.25f, new Vector3(5f, 0.5f, 5f), groundLayer);
        OverburstCharacterMotor3D motor = CreateMotorRig("HeightRig", start, groundLayer);
        CharacterController controller = motor.Controller;
        float standing = motor.StandingHeight;
        float footBefore = WorldFoot(controller);
        Require(motor.SetHeightWithHeadroom(standing * 0.5f), "height shrink failed.");
        float footError = Mathf.Abs(WorldFoot(controller) - footBefore);
        Require(footError < 0.002f, "height foot moved=" + footError);

        GameObject ceiling = CreateBox(
            "HeadroomBlocker",
            new Vector3(start.x, footBefore + standing * 0.75f, start.z),
            new Vector3(2f, 0.3f, 2f),
            groundLayer);
        Physics.SyncTransforms();
        bool blocked = !motor.SetHeightWithHeadroom(standing);
        Require(blocked, "headroom expansion was not blocked.");
        UnityEngine.Object.DestroyImmediate(ceiling);
        Physics.SyncTransforms();
        bool expanded = motor.SetHeightWithHeadroom(standing);
        Require(expanded && Mathf.Approximately(motor.CurrentHeight, standing), "height expansion failed after blocker removal.");
        return new HeightMetrics(footError, blocked, expanded);
    }

    private static CombatMetrics VerifyCombatMotion()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        Require(enemyLayer >= 0, "Enemy layer missing.");
        GameObject rig = new GameObject("CombatMotionRig");
        rig.transform.SetParent(fixtureRoot.transform, false);
        rig.transform.position = FixtureBase + new Vector3(0f, 1f, 30f);
        CharacterController controller = rig.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.5f;
        controller.center = Vector3.zero;
        OverburstCharacterMotor3D motor = rig.AddComponent<OverburstCharacterMotor3D>();
        motor.Configure(DefaultSettings(~0));
        rig.AddComponent<CombatHealth>();
        CombatTarget playerTarget = CombatTarget.EnsureConfigured(rig, CombatTeam.PlayerParty, false);
        CombatMotionDriver driver = rig.AddComponent<CombatMotionDriver>();
        driver.Bind(null, motor, playerTarget, 0.03f);

        GameObject enemy = CreateBox("OverlapEnemy", rig.transform.position + Vector3.right * 0.7f, Vector3.one, enemyLayer);
        enemy.AddComponent<CombatHealth>();
        CombatTarget.EnsureConfigured(enemy, CombatTeam.Enemy, false);
        Physics.SyncTransforms();
        Vector3 toward = driver.ResolveSafeDisplacement(Vector3.right * 0.5f);
        Vector3 away = driver.ResolveSafeDisplacement(Vector3.left * 0.5f);
        Require(toward.magnitude < 0.0001f, "overlap toward movement allowed=" + toward.magnitude);
        Require(away.magnitude > 0.45f, "overlap escape movement blocked=" + away.magnitude);

        enemy.transform.position = rig.transform.position + Vector3.right * 2f;
        Physics.SyncTransforms();
        Vector3 cast = driver.ResolveSafeDisplacement(Vector3.right * 3f);
        Require(cast.magnitude > 0.1f && cast.magnitude < 3f, "enemy capsule cast distance invalid=" + cast.magnitude);
        return new CombatMetrics(toward.magnitude, away.magnitude, cast.magnitude);
    }

    private static OverburstCharacterMotor3D CreateMotorRig(string name, Vector3 position, int groundLayer)
    {
        GameObject rig = new GameObject(name);
        rig.transform.SetParent(fixtureRoot.transform, false);
        rig.transform.position = position;
        CharacterController controller = rig.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.5f;
        controller.center = Vector3.zero;
        controller.slopeLimit = 45f;
        controller.stepOffset = 0.3f;
        OverburstCharacterMotor3D motor = rig.AddComponent<OverburstCharacterMotor3D>();
        motor.Configure(DefaultSettings(1 << groundLayer));
        return motor;
    }

    private static CharacterMotorSettings DefaultSettings(int groundMask)
    {
        return new CharacterMotorSettings
        {
            skinWidthRadiusRatio = 0.1f,
            slopeLimit = 45f,
            stepOffset = 0.3f,
            minMoveDistance = 0f,
            groundCheckRadius = 0.22f,
            groundLayer = groundMask,
            groundProbeStartOffset = 0.08f,
            groundSnapDistance = 0.32f,
            groundNormalSharpness = 22f,
            gravity = -25f,
            groundStickVelocity = -2f,
            maxFallSpeed = 60f,
            externalVelocityDecay = 8f,
            maxExternalSpeed = 20f,
        };
    }

    private static MotorStepCommand HoldCommand()
    {
        return new MotorStepCommand
        {
            currentHorizontalVelocity = Vector3.zero,
            targetHorizontalVelocity = Vector3.zero,
            moveRate = 70f,
            airControl = 0.35f,
            jumpVelocity = 0f,
        };
    }

    private static GameObject CreateBox(string name, Vector3 position, Vector3 size, int layer)
    {
        GameObject box = new GameObject(name);
        box.transform.SetParent(fixtureRoot.transform, false);
        box.transform.position = position;
        box.layer = layer;
        BoxCollider collider = box.AddComponent<BoxCollider>();
        collider.size = size;
        return box;
    }

    private static float WorldFoot(CharacterController controller)
    {
        return controller.transform.TransformPoint(controller.center).y
            - controller.height * Mathf.Abs(controller.transform.lossyScale.y) * 0.5f;
    }

    private static void OnLog(string message, string stackTrace, LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false)
            || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert))
            return;
        if (runtimeErrors.Count < 20)
            runtimeErrors.Add("[" + type + "] " + message);
    }

    private static void Fail(string message)
    {
        ran = true;
        SessionState.SetString(FailKey, message);
        Cleanup();
        EditorApplication.ExitPlaymode();
    }

    private static void Cleanup()
    {
        if (fixtureRoot != null)
            UnityEngine.Object.DestroyImmediate(fixtureRoot);
        fixtureRoot = null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private readonly struct PlatformMetrics
    {
        public readonly float positionError;
        public readonly float rotationError;
        public readonly float exitSpeed;
        public PlatformMetrics(float p, float r, float e) { positionError = p; rotationError = r; exitSpeed = e; }
    }

    private readonly struct ForceMetrics
    {
        public readonly float distance30, distance60, distance120, maxDifference, clearMagnitude;
        public ForceMetrics(float a, float b, float c, float d, float e)
        { distance30 = a; distance60 = b; distance120 = c; maxDifference = d; clearMagnitude = e; }
    }

    private readonly struct HeightMetrics
    {
        public readonly float footError;
        public readonly bool blocked, expanded;
        public HeightMetrics(float f, bool b, bool e) { footError = f; blocked = b; expanded = e; }
    }

    private readonly struct CombatMetrics
    {
        public readonly float towardMagnitude, awayMagnitude, castMagnitude;
        public CombatMetrics(float t, float a, float c) { towardMagnitude = t; awayMagnitude = a; castMagnitude = c; }
    }
}
