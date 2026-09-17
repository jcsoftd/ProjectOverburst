using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// GOAL C Play 검증: Hideout의 추적/재바인딩/회전/가림/단일 Impulse/사망·리셋 생존을 검사한다.
[InitializeOnLoad]
public static class OverburstGoalCCameraPlayModeVerifier
{
    private const string ActiveKey = "OverburstGoalCCameraPlayModeVerifier.Active";
    private const string ResultKey = "OverburstGoalCCameraPlayModeVerifier.Result";
    private const string FailKey = "OverburstGoalCCameraPlayModeVerifier.Fail";
    private const float TimeoutSeconds = 180f;

    private enum Step
    {
        WaitForHideout,
        WaitForTemporaryTarget,
        WaitForActorRebind,
        WaitForYaw,
        WaitForOcclusion,
        WaitForOcclusionRecovery,
        SampleImpact,
        WaitForDeathReset,
    }

    private static readonly List<string> runtimeErrors = new List<string>();
    private static Step step;
    private static int waitUntilFrame;
    private static double startedAt;
    private static GameObject fixtureRoot;
    private static GameObject temporaryTarget;
    private static GameObject occluder;
    private static QuarterViewCamera quarter;
    private static OverburstCinemachineCameraRig rig;
    private static Camera outputCamera;
    private static PlayerActorRuntime actor;
    private static Vector3 impactBaselinePosition;
    private static float impactBaselineRoll;
    private static float maxImpactPositionDelta;
    private static float maxImpactRollDelta;
    private static int impactCountBefore;
    private static int sampleImpactUntilFrame;
    private static bool previousRunInBackground;

    static OverburstGoalCCameraPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("OVERBURST/Codex/Validate/Camera/Validate GOAL C Play Mode")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play Mode가 이미 실행 중이다.");
        EditorSceneManager.OpenScene(OverburstCinemachineCameraMigration.PersistentScenePath, OpenSceneMode.Single);
        if (EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("PersistentScene이 dirty다.");

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
            step = Step.WaitForHideout;
            waitUntilFrame = Time.frameCount + 30;
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
            Debug.Log("[OverburstGoalCCameraPlayModeVerifier] PASS " + result);
        else
            Debug.LogError("[OverburstGoalCCameraPlayModeVerifier] FAIL " + fail);
    }

    private static void Update()
    {
        if (!EditorApplication.isPlaying || Time.frameCount < waitUntilFrame)
            return;
        if (EditorApplication.timeSinceStartup - startedAt > TimeoutSeconds)
        {
            Fail("timeout step=" + step);
            return;
        }

        try
        {
            switch (step)
            {
                case Step.WaitForHideout:
                    BeginFromHideout();
                    break;
                case Step.WaitForTemporaryTarget:
                    VerifyTemporaryTargetAndRebind();
                    break;
                case Step.WaitForActorRebind:
                    VerifyActorRebindAndRotate();
                    break;
                case Step.WaitForYaw:
                    VerifyYawAndCreateOccluder();
                    break;
                case Step.WaitForOcclusion:
                    VerifyOcclusionAndRecover();
                    break;
                case Step.WaitForOcclusionRecovery:
                    BeginImpact();
                    break;
                case Step.SampleImpact:
                    SampleImpact();
                    break;
                case Step.WaitForDeathReset:
                    VerifyDeathResetAndFinish();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private static void BeginFromHideout()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null || flow.IsSwitching || flow.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            return;

        OverburstGoalCCameraValidator.ValidateRuntimeContracts();
        quarter = QuarterViewCamera.ActiveInstance;
        rig = quarter != null ? quarter.CinemachineRig : null;
        outputCamera = rig != null ? rig.OutputCamera : null;
        actor = PlayerContext.GetOrCreate()?.CurrentActor;
        if (quarter == null || rig == null || outputCamera == null || actor == null)
            return;

        Require(quarter.UsesCinemachine && rig.IsConfigured, "Cinemachine adapter is not configured.");
        Require(quarter.CurrentTarget == actor.transform, "Hideout camera is not bound to PlayerActor_01.");
        Require(outputCamera.orthographic, "Hideout output camera is not orthographic.");
        Require(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
            "runtime Camera count is not 1.");
        Require(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
            "runtime AudioListener count is not 1.");

        fixtureRoot = new GameObject("__OVERBURST_GOAL_C_RUNTIME_ONLY__");
        temporaryTarget = new GameObject("TemporaryCameraTarget");
        temporaryTarget.transform.SetParent(fixtureRoot.transform, false);
        temporaryTarget.transform.position = actor.transform.position + new Vector3(5f, 0f, 3f);
        quarter.SetTarget(temporaryTarget.transform);
        step = Step.WaitForTemporaryTarget;
        waitUntilFrame = Time.frameCount + 75;
    }

    private static void VerifyTemporaryTargetAndRebind()
    {
        Require(quarter.CurrentTarget == temporaryTarget.transform, "temporary SetTarget was not retained.");
        Require(Vector3.Distance(rig.FocusTarget.position, temporaryTarget.transform.position) < 0.08f,
            "temporary target tracking did not converge: " + Vector3.Distance(rig.FocusTarget.position, temporaryTarget.transform.position));

        PlayerContext context = PlayerContext.GetOrCreate();
        Require(context != null, "PlayerContext missing during rebind.");
        context.Bind(null);
        context.Bind(actor);
        step = Step.WaitForActorRebind;
        waitUntilFrame = Time.frameCount + 75;
    }

    private static void VerifyActorRebindAndRotate()
    {
        Require(quarter.CurrentTarget == actor.transform, "PlayerCameraBinder did not restore actor target.");
        Require(Vector3.Distance(rig.FocusTarget.position, actor.transform.position) < 0.08f,
            "actor rebind tracking did not converge: " + Vector3.Distance(rig.FocusTarget.position, actor.transform.position));
        quarter.SetYaw(90f);
        step = Step.WaitForYaw;
        waitUntilFrame = Time.frameCount + 5;
    }

    private static void VerifyYawAndCreateOccluder()
    {
        float yaw = outputCamera.transform.eulerAngles.y;
        Require(Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 0.5f, "SetYaw output mismatch: " + yaw);

        Vector3 cameraPosition = outputCamera.transform.position;
        Vector3 focusPosition = rig.FocusTarget.position;
        Vector3 line = focusPosition - cameraPosition;
        occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        occluder.name = "CameraOccluder";
        occluder.transform.SetParent(fixtureRoot.transform, true);
        occluder.layer = LayerMask.NameToLayer("Ground");
        occluder.transform.position = cameraPosition + line * 0.55f;
        occluder.transform.rotation = Quaternion.LookRotation(line.normalized, Vector3.up);
        occluder.transform.localScale = new Vector3(12f, 12f, 0.75f);
        Renderer renderer = occluder.GetComponent<Renderer>();
        if (renderer != null)
            renderer.enabled = false;
        Physics.SyncTransforms();
        step = Step.WaitForOcclusion;
        waitUntilFrame = Time.frameCount + 20;
    }

    private static void VerifyOcclusionAndRecover()
    {
        Require(rig.Deoccluder.CameraWasDisplaced(rig.VirtualCamera), "Deoccluder did not displace the camera for a blocking wall.");
        UnityEngine.Object.Destroy(occluder);
        occluder = null;
        quarter.SetYaw(0f);
        step = Step.WaitForOcclusionRecovery;
        waitUntilFrame = Time.frameCount + 45;
    }

    private static void BeginImpact()
    {
        Require(!rig.Deoccluder.CameraWasDisplaced(rig.VirtualCamera), "Deoccluder did not recover after wall removal.");
        impactBaselinePosition = outputCamera.transform.position;
        impactBaselineRoll = outputCamera.transform.eulerAngles.z;
        maxImpactPositionDelta = 0f;
        maxImpactRollDelta = 0f;
        impactCountBefore = rig.EmittedImpactCount;

        quarter.RequestCombatImpact(
            CombatCameraRequestKind.AttackHit,
            outputCamera.transform.right,
            Vector3.zero,
            false,
            0.35f,
            0.9f,
            3f,
            0.75f,
            0.16f,
            0.2f,
            2f,
            1f,
            3f);
        quarter.RequestCombatImpact(
            CombatCameraRequestKind.AttackHit,
            outputCamera.transform.right,
            Vector3.zero,
            false,
            0.35f,
            0.2f,
            0.5f,
            0.75f,
            0.16f,
            0.1f,
            1f,
            1f,
            3f);
        Require(rig.EmittedImpactCount == impactCountBefore + 1,
            "lower-priority request emitted a duplicate impulse: " + (rig.EmittedImpactCount - impactCountBefore));
        sampleImpactUntilFrame = Time.frameCount + 35;
        step = Step.SampleImpact;
        waitUntilFrame = Time.frameCount + 1;
    }

    private static void SampleImpact()
    {
        maxImpactPositionDelta = Mathf.Max(maxImpactPositionDelta,
            Vector3.Distance(outputCamera.transform.position, impactBaselinePosition));
        maxImpactRollDelta = Mathf.Max(maxImpactRollDelta,
            Mathf.Abs(Mathf.DeltaAngle(outputCamera.transform.eulerAngles.z, impactBaselineRoll)));
        if (Time.frameCount < sampleImpactUntilFrame)
        {
            waitUntilFrame = Time.frameCount + 1;
            return;
        }

        Require(maxImpactPositionDelta > 0.005f, "Cinemachine positional impulse was not observed.");
        Require(maxImpactRollDelta > 0.05f, "Cinemachine rotational impulse was not observed.");
        Require(rig.EmittedImpactCount == impactCountBefore + 1, "impact event count changed unexpectedly.");

        MethodInfo die = typeof(CombatHealth).GetMethod("Die", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(die != null && actor.Health != null, "CombatHealth death/reset fixture unavailable.");
        die.Invoke(actor.Health, new object[] { new DamageInfo(0f, actor.transform.position) });
        Require(actor.Health.IsDead, "player death state was not entered.");
        step = Step.WaitForDeathReset;
        waitUntilFrame = Time.frameCount + 3;
    }

    private static void VerifyDeathResetAndFinish()
    {
        Require(quarter.CurrentTarget == actor.transform && rig.FocusTarget != null,
            "camera target was lost on player death.");
        actor.Health.ResetHealth();
        Require(!actor.Health.IsDead, "player health reset failed.");
        Require(quarter.CurrentTarget == actor.transform, "camera target was lost on player reset.");
        if (runtimeErrors.Count > 0)
            throw new InvalidOperationException("runtime errors=" + runtimeErrors.Count + " first=" + runtimeErrors[0]);

        string result = string.Format(
            "hideout=PASS rebind=PASS yaw=PASS occlusion=PASS impulseEvents=1 impactPos={0:F3} impactRoll={1:F2} deathReset=PASS errors=0",
            maxImpactPositionDelta,
            maxImpactRollDelta);
        SessionState.SetString(ResultKey, result);
        Cleanup();
        EditorApplication.ExitPlaymode();
    }

    private static void Fail(string message)
    {
        SessionState.SetString(FailKey, message);
        Cleanup();
        EditorApplication.ExitPlaymode();
    }

    private static void Cleanup()
    {
        if (actor != null && actor.Health != null && actor.Health.IsDead)
            actor.Health.ResetHealth();
        if (quarter != null && actor != null)
        {
            quarter.SetTarget(actor.transform);
            quarter.SetYaw(0f);
        }
        if (rig != null)
            rig.CancelCombatImpact();
        if (fixtureRoot != null)
            UnityEngine.Object.Destroy(fixtureRoot);
        fixtureRoot = null;
        temporaryTarget = null;
        occluder = null;
        quarter = null;
        rig = null;
        outputCamera = null;
        actor = null;
    }

    private static void OnLog(string message, string stackTrace, LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying)
            return;
        if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
            runtimeErrors.Add(type + ": " + message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
