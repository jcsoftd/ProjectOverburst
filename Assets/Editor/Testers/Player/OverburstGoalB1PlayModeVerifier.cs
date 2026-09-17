using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// GOAL B1 Play Mode 검증기. Editor 전용이며 씬/에셋을 저장·수정하지 않는다.
// 임시 지형·리그는 런타임에만 만들고 종료 전 파괴한다. 정식 씬은 건드리지 않는다.
// 실제 Gameplay 입력 구동은 Play 세션 전용 가상 장치와 RuntimeAsset에만 적용한다.
// Codex가 실제 실행하며 PASS 로그와 측정값을 B1 매니페스트에 남긴다.
[InitializeOnLoad]
public static class OverburstGoalB1PlayModeVerifier
{
    private const string ActiveKey = "OverburstGoalB1PlayModeVerifier.Active";
    private const string ExitCodeKey = "OverburstGoalB1PlayModeVerifier.ExitCode";
    private const string FailMessageKey = "OverburstGoalB1PlayModeVerifier.FailMessage";
    private const string ValidationSwordAssetPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const float TimeoutSeconds = 300f;

    private static readonly Vector3 FixtureBase = new Vector3(1000f, 0f, 1000f);

    private enum VerifyStep
    {
        WaitForHideoutReady,
        StaticChecks,
        BuildFixture,
        FlatFps,
        SlopeClimb,
        SteepSlope,
        LedgeDrop,
        SnapDescent,
        StepUp,
        DropLanding,
        ActorMove,
        ActorAttack,
        ActorEvadeArrange,
        ActorEvade,
        LootApi,
        TeleportApi,
        DeathReset,
        Teardown,
        ErrorCheck,
        Restore,
    }

    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float stepDeadline;
    private static float timeoutAt;
    private static int lastFrameCount;
    private static float lastFrameAdvanceTime;

    private static readonly HashSet<Key> heldKeys = new HashSet<Key>();
    private static bool lmbHeld;
    private static bool keyboardSyncNeeded;
    private static bool mouseSyncNeeded;
    private static Keyboard validationKeyboard;
    private static Mouse validationMouse;

    private static readonly List<string> logErrors = new List<string>();
    private static int logErrorTotal;

    private static GameObject fixtureRoot;
    private static GameObject rigObject;
    private static CharacterController rigController;
    private static OverburstCharacterMotor3D rigMotor;
    private static CharacterMotorSettings rigSettings;
    private static int fixtureGroundLayer;

    private static Vector3 actorHomePosition;
    private static Quaternion actorHomeRotation;
    private static bool initialCombatMode;
    private static ItemData initialWeaponItem;
    private static bool equippedValidationWeapon;
    private static Vector3 stepPosition;
    private static Vector3 actorReleasePosition;
    private static Vector3 actorSettledPosition;
    private static int actorMovePhase;

    private static float flatDist30;
    private static float flatDist60;
    private static float flatDist120;
    private static float flatDiffPercent;
    private static float dropTime30;
    private static float dropTime60;
    private static float dropTime120;
    private static float dropImpactSpeed;
    private static float slopeGain;
    private static float steepGain;
    private static int snapAirFrames;
    private static Vector3 snapFirstAirPosition;
    private static float snapFirstAirGap;
    private static Vector3 snapFirstAirNormal;
    private static bool stepPassed;
    private static bool landObserved;
    private static bool observedAttackStates;
    private static bool observedEvadeState;
    private static bool previousRunInBackground;

    static OverburstGoalB1PlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Application.logMessageReceived -= HandleLog;
        Application.logMessageReceived += HandleLog;
    }

    [MenuItem("OVERBURST/Codex/Validate/Player/Validate GOAL B1 Play Mode")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("PlayMode is already active or changing.");
        var activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.name != PersistentSceneFlow.PersistentSceneName)
        {
            throw new InvalidOperationException(
                "This verifier requires the active scene to be PersistentScene, current=" + activeScene.name + ".");
        }
        if (activeScene.isDirty)
        {
            throw new InvalidOperationException(
                "PersistentScene is dirty. Save or revert it manually; the verifier never saves.");
        }
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(ExitCodeKey, 1);
        SessionState.SetString(FailMessageKey, string.Empty);
        logErrors.Clear();
        logErrorTotal = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void HandleLog(string message, string stackTrace, LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;
        logErrorTotal++;
        if (logErrors.Count < 25)
            logErrors.Add("[" + type + "] " + message);
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true; // 자동 검증 중 창 포커스와 무관하게 프레임 진행
            ResetRunState();
            CreateValidationInputDevices();
            step = VerifyStep.WaitForHideoutReady;
            WaitFrames(30);
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
            return;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            DestroyValidationInputDevices();
            Application.runInBackground = previousRunInBackground;
            EditorApplication.update -= UpdateVerification;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode)
            return;
        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        string failMessage = SessionState.GetString(FailMessageKey, string.Empty);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseInt(ExitCodeKey);
        SessionState.EraseString(FailMessageKey);
        if (exitCode == 0)
            Debug.Log("[OverburstGoalB1PlayModeVerifier] PASS " + failMessage);
        else
            Debug.LogError("[OverburstGoalB1PlayModeVerifier] FAIL " + failMessage);
    }

    private static void ResetRunState()
    {
        heldKeys.Clear();
        lmbHeld = false;
        keyboardSyncNeeded = false;
        mouseSyncNeeded = false;
        stepDeadline = 0f;
        actorMovePhase = 0;
        fixtureRoot = null;
        rigObject = null;
        rigController = null;
        rigMotor = null;
        flatDist30 = 0f;
        flatDist60 = 0f;
        flatDist120 = 0f;
        flatDiffPercent = 0f;
        dropTime30 = 0f;
        dropTime60 = 0f;
        dropTime120 = 0f;
        dropImpactSpeed = 0f;
        slopeGain = 0f;
        steepGain = 0f;
        snapAirFrames = 0;
        snapFirstAirPosition = Vector3.zero;
        snapFirstAirGap = 0f;
        snapFirstAirNormal = Vector3.up;
        stepPassed = false;
        landObserved = false;
        observedAttackStates = false;
        observedEvadeState = false;
        initialWeaponItem = null;
        equippedValidationWeapon = false;
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying)
            return;
        // 백그라운드 Editor에서도 실제 PlayerLoop/InputSystem 순서를 유지한다.
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (Time.frameCount < waitUntilFrame)
                return;
            if (Time.frameCount != lastFrameCount)
            {
                lastFrameCount = Time.frameCount;
                lastFrameAdvanceTime = Time.realtimeSinceStartup;
            }
            else if (Time.realtimeSinceStartup - lastFrameAdvanceTime > 15f)
            {
                throw new InvalidOperationException(
                    "ENVIRONMENT_STALLED: frameCount=" + Time.frameCount
                    + " isFocused=" + Application.isFocused
                    + ". Activate the Unity window and rerun; not a code failure.");
            }
            if (Time.realtimeSinceStartup > timeoutAt)
                throw new TimeoutException("GOAL B1 PlayMode verification timed out at " + step + ".");
            if (stepDeadline > 0f && Time.realtimeSinceStartup > stepDeadline)
                throw new TimeoutException(
                    "GOAL B1 PlayMode step timed out at " + step + ". " + DescribeCurrentStep());
            switch (step)
            {
                case VerifyStep.WaitForHideoutReady: StepHideoutReady(); break;
                case VerifyStep.StaticChecks: StepStaticChecks(); break;
                case VerifyStep.BuildFixture: StepBuildFixture(); break;
                case VerifyStep.FlatFps: StepFlatFps(); break;
                case VerifyStep.SlopeClimb: StepSlopeClimb(); break;
                case VerifyStep.SteepSlope: StepSteepSlope(); break;
                case VerifyStep.LedgeDrop: StepLedgeDrop(); break;
                case VerifyStep.SnapDescent: StepSnapDescent(); break;
                case VerifyStep.StepUp: StepStepUp(); break;
                case VerifyStep.DropLanding: StepDropLanding(); break;
                case VerifyStep.ActorMove: StepActorMove(); break;
                case VerifyStep.ActorAttack: StepActorAttack(); break;
                case VerifyStep.ActorEvadeArrange: StepActorEvadeArrange(); break;
                case VerifyStep.ActorEvade: StepActorEvade(); break;
                case VerifyStep.LootApi: StepLootApi(); break;
                case VerifyStep.TeleportApi: StepTeleportApi(); break;
                case VerifyStep.DeathReset: StepDeathReset(); break;
                case VerifyStep.Teardown: StepTeardown(); break;
                case VerifyStep.ErrorCheck: StepErrorCheck(); break;
                case VerifyStep.Restore: StepRestore(); break;
            }
        }
        catch (Exception exception)
        {
            SessionState.SetString(FailMessageKey, step + ": " + exception.Message);
            Finish(1);
        }
    }

    private static void StepHideoutReady()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        Require(flow != null, "PersistentSceneFlow.Instance is null.");
        if (flow.IsSwitching || flow.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            return;
        PlayerActorRuntime actor = CurrentActor();
        Require(validationKeyboard != null, "Validation keyboard is null.");
        Require(validationMouse != null, "Validation mouse is null.");
        PlayerInputFacade facade = actor.GetComponent<PlayerInputFacade>();
        Require(facade != null && facade.RuntimeAsset != null, "PlayerInputFacade runtime asset is missing.");
        facade.RuntimeAsset.devices = new InputDevice[] { validationKeyboard, validationMouse };
        actorHomePosition = actor.transform.position;
        actorHomeRotation = actor.transform.rotation;
        initialCombatMode = PlayerCombatModeController.GetOrCreate().IsCombatModeActive;
        PlayerEquipment equipment = actor.Equipment;
        Require(equipment != null, "PlayerEquipment is missing.");
        initialWeaponItem = equipment.GetWeaponSlotItem(equipment.ActiveWeaponSlotIndex);
        if (!equipment.CanCurrentWeaponUseMeleeSlash)
        {
            WeaponItemData swordData = AssetDatabase.LoadAssetAtPath<WeaponItemData>(ValidationSwordAssetPath);
            Require(swordData != null, "Validation sword asset is missing: " + ValidationSwordAssetPath + ".");
            Require(equipment.EquipWeaponItem(new ItemData(swordData, 1, ItemGrade.Common)),
                "Validation sword could not be equipped through PlayerEquipment.");
            equippedValidationWeapon = true;
        }
        lastFrameCount = Time.frameCount;
        lastFrameAdvanceTime = Time.realtimeSinceStartup;
        step = VerifyStep.StaticChecks;
        WaitFrames(5);
    }

    private static void StepStaticChecks()
    {
        OverburstPlayerMotorValidator.ValidateRuntimeContracts();
        step = VerifyStep.BuildFixture;
        WaitFrames(5);
    }

    private static void StepBuildFixture()
    {
        PlayerActorRuntime actor = CurrentActor();
        Require(actor.Movement != null && actor.Movement.Motor != null, "Player motor is missing.");
        rigSettings = actor.Movement.Motor.ActiveSettings;
        fixtureGroundLayer = FirstLayer(rigSettings.groundLayer.value);
        Require(fixtureGroundLayer >= 0, "Player motor ground layer mask is empty.");
        fixtureRoot = new GameObject("B1MotorFixture");
        AddBox("Slab", new Vector3(0f, -0.5f, 0f), new Vector3(400f, 1f, 60f), 0f);
        // 회전된 BoxCollider의 윗면 왼쪽 끝이 slab y=0과 정확히 만나도록 중심 높이를 계산한다.
        AddBox("Ramp20", new Vector3(30f, 3.634f, 0f), new Vector3(24f, 1f, 10f), 20f);
        AddBox("Steep60", new Vector3(80f, 4.946f, 0f), new Vector3(12f, 1f, 10f), 60f);
        AddBox("Ledge", new Vector3(120f, 3.5f, 0f), new Vector3(8f, 1f, 8f), 0f);
        AddBox("StepBox", new Vector3(60f, 0f, 0f), new Vector3(1f, 0.5f, 4f), 0f);

        rigObject = new GameObject("B1MotorRig");
        rigObject.transform.SetParent(fixtureRoot.transform, false);
        rigController = rigObject.AddComponent<CharacterController>();
        CharacterController actorController = actor.GetComponent<CharacterController>();
        Require(actorController != null, "Actor CharacterController is missing.");
        rigController.center = actorController.center;
        rigController.radius = actorController.radius;
        rigController.height = actorController.height;
        rigController.slopeLimit = actorController.slopeLimit;
        rigController.stepOffset = actorController.stepOffset;
        rigController.skinWidth = actorController.skinWidth;
        rigController.minMoveDistance = actorController.minMoveDistance;
        rigMotor = rigObject.AddComponent<OverburstCharacterMotor3D>();
        rigMotor.Configure(rigSettings);
        step = VerifyStep.FlatFps;
        WaitFrames(5);
    }

    private static void AddBox(string name, Vector3 center, Vector3 size, float rotZ)
    {
        GameObject box = new GameObject(name);
        box.layer = fixtureGroundLayer;
        box.transform.SetParent(fixtureRoot.transform, false);
        box.transform.position = FixtureBase + center;
        box.transform.rotation = Quaternion.Euler(0f, 0f, rotZ);
        BoxCollider collider = box.AddComponent<BoxCollider>();
        collider.size = size;
    }

    private static int FirstLayer(int mask)
    {
        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) != 0)
                return layer;
        }

        return -1;
    }

    private static Vector3 RigStart(float x, float y)
    {
        return FixtureBase + new Vector3(x, y, 0f);
    }

    private static void ResetRig(Vector3 position)
    {
        rigObject.transform.SetPositionAndRotation(position, Quaternion.identity);
        Physics.SyncTransforms();
        rigMotor.ResetMotion();
        MotorStepCommand settle = RunCommand(Vector3.zero);
        for (int i = 0; i < 10; i++)
        {
            rigMotor.ProbeGround(1f / 60f);
            rigMotor.Step(settle, 1f / 60f);
        }
    }

    private static MotorStepCommand RunCommand(Vector3 targetHorizontal)
    {
        return new MotorStepCommand
        {
            currentHorizontalVelocity = Vector3.zero,
            targetHorizontalVelocity = targetHorizontal,
            moveRate = 55f,
            airControl = 0.35f,
            jumpVelocity = 0f,
        };
    }

    private static void StepFlatFps()
    {
        float[] dts = { 1f / 30f, 1f / 60f, 1f / 120f };
        float[] dists = new float[3];
        for (int r = 0; r < 3; r++)
        {
            ResetRig(RigStart(0f, 0.3f));
            Vector3 horizontal = Vector3.zero;
            Vector3 start = rigObject.transform.position;
            int accelSteps = Mathf.RoundToInt(1f / dts[r]);
            for (int i = 0; i < accelSteps; i++)
            {
                rigMotor.ProbeGround(dts[r]);
                MotorStepCommand command = RunCommand(new Vector3(7.8f, 0f, 0f));
                command.currentHorizontalVelocity = horizontal;
                MotorStepResult result = rigMotor.Step(command, dts[r]);
                horizontal = result.horizontalVelocity;
            }
            int releaseSteps = Mathf.RoundToInt(1f / dts[r]);
            for (int i = 0; i < releaseSteps; i++)
            {
                rigMotor.ProbeGround(dts[r]);
                MotorStepCommand command = RunCommand(Vector3.zero);
                command.currentHorizontalVelocity = horizontal;
                command.moveRate = 70f;
                MotorStepResult result = rigMotor.Step(command, dts[r]);
                horizontal = result.horizontalVelocity;
            }
            Vector3 end = rigObject.transform.position;
            dists[r] = FlatDistance(start, end);
            Require(rigMotor.IsGrounded, "Flat fps rig left the ground at 1/" + Mathf.RoundToInt(1f / dts[r]) + "fps.");
        }
        flatDist30 = dists[0];
        flatDist60 = dists[1];
        flatDist120 = dists[2];
        float maxDist = Mathf.Max(flatDist30, Mathf.Max(flatDist60, flatDist120));
        float minDist = Mathf.Min(flatDist30, Mathf.Min(flatDist60, flatDist120));
        Require(maxDist > 1f, "Flat fps rig did not move: " + maxDist.ToString("0.00") + "m.");
        flatDiffPercent = maxDist > 0f ? (maxDist - minDist) / maxDist * 100f : 0f;
        Require(flatDiffPercent <= 1f, "30/60/120fps distances diverge: " + flatDiffPercent.ToString("0.00") + "%.");
        step = VerifyStep.SlopeClimb;
        WaitFrames(5);
    }

    private static void StepSlopeClimb()
    {
        ResetRig(RigStart(8f, 0.3f));
        Vector3 horizontal = Vector3.zero;
        float dt = 1f / 60f;
        // 램프 끝을 지나 낙하하기 전, 경사 위에 있는 시점의 상승량을 측정한다.
        for (int i = 0; i < 200; i++)
        {
            rigMotor.ProbeGround(dt);
            MotorStepCommand command = RunCommand(new Vector3(7.8f, 0f, 0f));
            command.currentHorizontalVelocity = horizontal;
            MotorStepResult result = rigMotor.Step(command, dt);
            horizontal = result.horizontalVelocity;
        }
        slopeGain = rigObject.transform.position.y - FixtureBase.y;
        Require(slopeGain > 5f, "20-degree ramp climb gained only " + slopeGain.ToString("0.00") + "m.");
        Require(rigMotor.IsGrounded, "Rig is not grounded after ramp climb.");
        step = VerifyStep.SteepSlope;
        WaitFrames(5);
    }

    private static void StepSteepSlope()
    {
        ResetRig(RigStart(70f, 0.3f));
        Vector3 horizontal = Vector3.zero;
        float dt = 1f / 60f;
        for (int i = 0; i < 180; i++)
        {
            rigMotor.ProbeGround(dt);
            MotorStepCommand command = RunCommand(new Vector3(7.8f, 0f, 0f));
            command.currentHorizontalVelocity = horizontal;
            MotorStepResult result = rigMotor.Step(command, dt);
            horizontal = result.horizontalVelocity;
        }
        steepGain = rigObject.transform.position.y - FixtureBase.y;
        Require(steepGain < 1f, "60-degree steep slope was climbed: +" + steepGain.ToString("0.00") + "m.");
        step = VerifyStep.LedgeDrop;
        WaitFrames(5);
    }

    private static void StepLedgeDrop()
    {
        // 발판 위에서 시작해, 첫 착지가 아니라 가장자리를 벗어난 뒤의 착지만 관찰한다.
        ResetRig(RigStart(118f, 4.05f));
        Vector3 horizontal = Vector3.zero;
        float dt = 1f / 60f;
        bool airborneSeen = false;
        bool landedSeen = false;
        bool landingEventSeen = false;
        float landingEventSpeed = 0f;
        for (int i = 0; i < 480 && !landedSeen; i++)
        {
            rigMotor.ProbeGround(dt);
            if (!rigMotor.IsGrounded)
                airborneSeen = true;
            MotorStepCommand command = RunCommand(new Vector3(7.8f, 0f, 0f));
            command.currentHorizontalVelocity = horizontal;
            MotorStepResult result = rigMotor.Step(command, dt);
            horizontal = result.horizontalVelocity;
            if (result.didLand)
            {
                landingEventSeen = true;
                landingEventSpeed = result.landingFallSpeed;
            }
            if (airborneSeen && rigMotor.IsGrounded)
                landedSeen = true;
        }
        Require(airborneSeen, "Ledge walk never left the ground.");
        Require(landedSeen, "Ledge fall never landed.");
        Require(landingEventSeen && landingEventSpeed < -1f,
            "Landing event/fall speed was not reported by the motor: " + landingEventSpeed.ToString("0.00") + ".");
        Require(rigMotor.IsGrounded, "Rig is not grounded after ledge landing.");
        Require(rigObject.transform.position.y - FixtureBase.y < 0.5f, "Ledge landing did not reach the slab.");
        landObserved = true;
        step = VerifyStep.SnapDescent;
        WaitFrames(5);
    }

    private static void StepSnapDescent()
    {
        ResetRig(RigStart(38f, 9f));
        Vector3 horizontal = Vector3.zero;
        float dt = 1f / 60f;
        snapAirFrames = 0;
        bool settled = false;
        for (int i = 0; i < 60; i++)
        {
            rigMotor.ProbeGround(dt);
            MotorStepCommand command = RunCommand(Vector3.zero);
            command.currentHorizontalVelocity = horizontal;
            MotorStepResult result = rigMotor.Step(command, dt);
            horizontal = result.horizontalVelocity;
            if (rigMotor.IsGrounded)
            {
                settled = true;
                break;
            }
        }
        Require(settled, "Rig did not settle on the ramp top.");
        for (int i = 0; i < 240; i++)
        {
            rigMotor.ProbeGround(dt);
            if (!rigMotor.IsGrounded)
            {
                if (snapAirFrames == 0)
                {
                    snapFirstAirPosition = rigObject.transform.position;
                    snapFirstAirGap = rigMotor.GroundGap;
                    snapFirstAirNormal = rigMotor.GroundContactNormal;
                }
                snapAirFrames++;
            }
            MotorStepCommand command = RunCommand(new Vector3(-7.8f, 0f, 0f));
            command.currentHorizontalVelocity = horizontal;
            MotorStepResult result = rigMotor.Step(command, dt);
            horizontal = result.horizontalVelocity;
        }
        Require(rigObject.transform.position.y < FixtureBase.y + 7f, "Ramp descent did not go downhill.");
        Require(snapAirFrames == 0,
            "Ground snap lost contact for " + snapAirFrames
            + " frames on descent. first=" + snapFirstAirPosition.ToString("F3")
            + " gap=" + snapFirstAirGap.ToString("F3")
            + " normal=" + snapFirstAirNormal.ToString("F3") + ".");
        step = VerifyStep.StepUp;
        WaitFrames(5);
    }

    private static void StepStepUp()
    {
        ResetRig(RigStart(55f, 0.3f));
        Vector3 horizontal = Vector3.zero;
        float dt = 1f / 60f;
        for (int i = 0; i < 180; i++)
        {
            rigMotor.ProbeGround(dt);
            MotorStepCommand command = RunCommand(new Vector3(7.8f, 0f, 0f));
            command.currentHorizontalVelocity = horizontal;
            MotorStepResult result = rigMotor.Step(command, dt);
            horizontal = result.horizontalVelocity;
        }
        stepPassed = rigObject.transform.position.x - FixtureBase.x > 61f;
        Require(stepPassed, "0.25m step was not climbed.");
        step = VerifyStep.DropLanding;
        WaitFrames(5);
    }

    private static void StepDropLanding()
    {
        float[] dts = { 1f / 30f, 1f / 60f, 1f / 120f };
        float[] times = new float[3];
        float worstImpact = 0f;
        for (int r = 0; r < 3; r++)
        {
            ResetRig(RigStart(150f, 12f));
            float time = 0f;
            float impact = 0f;
            bool landed = false;
            for (int i = 0; i < Mathf.RoundToInt(4f / dts[r]); i++)
            {
                rigMotor.ProbeGround(dts[r]);
                MotorStepCommand command = RunCommand(Vector3.zero);
                MotorStepResult result = rigMotor.Step(command, dts[r]);
                time += dts[r];
                impact = Mathf.Min(impact, rigMotor.VerticalVelocity);
                if (rigMotor.IsGrounded)
                {
                    landed = true;
                    break;
                }
            }
            Require(landed, "12m drop never landed at 1/" + Mathf.RoundToInt(1f / dts[r]) + "fps.");
            times[r] = time;
            worstImpact = Mathf.Min(worstImpact, impact);
        }
        dropTime30 = times[0];
        dropTime60 = times[1];
        dropTime120 = times[2];
        dropImpactSpeed = -worstImpact;
        float maxTime = Mathf.Max(dropTime30, Mathf.Max(dropTime60, dropTime120));
        float minTime = Mathf.Min(dropTime30, Mathf.Min(dropTime60, dropTime120));
        Require(maxTime > 0f && (maxTime - minTime) / maxTime * 100f <= 5f, "Drop times diverge across fps.");
        Require(dropImpactSpeed > 5f && dropImpactSpeed <= 60f, "Drop impact speed out of range: " + dropImpactSpeed.ToString("0.00") + ".");
        step = VerifyStep.ActorMove;
        WaitFrames(5);
    }

    private static void StepActorMove()
    {
        PlayerActorRuntime actor = CurrentActor();
        if (actorMovePhase == 0)
        {
            Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input is blocked before actor move.");
            stepPosition = actor.transform.position;
            Vector3 target = stepPosition + actor.transform.forward * 5f;
            Require(actor.Movement.BeginLootAutoMove(target),
                "BeginLootAutoMove rejected the runtime actor move path.");
            actorMovePhase = 1;
            WaitSeconds(8f);
            return;
        }
        if (actorMovePhase == 1)
        {
            if (FlatDistance(stepPosition, actor.transform.position) < 0.1f)
                return;
            actor.Movement.CancelLootAutoMove();
            actorReleasePosition = actor.transform.position;
            actorMovePhase = 2;
            WaitFrames(20);
            return;
        }
        if (actorMovePhase == 2)
        {
            actorSettledPosition = actor.transform.position;
            Require(FlatDistance(actorReleasePosition, actorSettledPosition) <= 0.65f,
                "Actor braking distance exceeded the preserved deceleration contract: "
                + FlatDistance(actorReleasePosition, actorSettledPosition).ToString("0.000") + "m.");
            actorMovePhase = 3;
            WaitFrames(15);
            return;
        }
        Require(FlatDistance(actorSettledPosition, actor.transform.position) < 0.03f,
            "Actor kept moving after the braking interval.");
        actorMovePhase = 0;
        step = VerifyStep.ActorAttack;
        WaitFrames(5);
    }

    private static void StepActorAttack()
    {
        PlayerActorRuntime actor = CurrentActor();
        MeleeRuntime melee = actor.PlayerKit != null ? actor.PlayerKit.MeleeRuntime : null;
        Require(melee != null, "MeleeRuntime is missing on the player kit.");
        PlayerMovement movement = actor.Movement;
        Require(movement != null && movement.IsGrounded, "Player is not grounded; melee attack cannot start.");
        Require(melee.CanUseCurrentWeapon, "Melee slash weapon is not equipped; melee attack cannot start.");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input is blocked before melee attack.");
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        if (!lmbHeld)
        {
            SetLmbHeld(true);
            WaitSeconds(6f);
        }
        if (!melee.IsAttackInProgress)
            return;
        Require(coordinator.CurrentAction == PlayerActionState.Attack,
            "Melee attack runs but Action is " + coordinator.CurrentAction + " instead of Attack.");
        Require(coordinator.CurrentLocomotion == PlayerLocomotionState.ControlledMove,
            "Melee attack runs but Locomotion is " + coordinator.CurrentLocomotion + " instead of ControlledMove.");
        observedAttackStates = true;
        SetLmbHeld(false);
        step = VerifyStep.ActorEvadeArrange;
        WaitFrames(10);
    }

    private static void StepActorEvadeArrange()
    {
        PlayerCombatModeController mode = PlayerCombatModeController.GetOrCreate();
        Require(mode != null, "PlayerCombatModeController is missing.");
        if (!mode.IsCombatModeActive)
        {
            PressEdge(Key.X);
            WaitSeconds(5f);
        }
        if (!mode.IsCombatModeActive)
            return;
        ReleaseKey(Key.X);
        step = VerifyStep.ActorEvade;
        WaitFrames(5);
    }

    private static void StepActorEvade()
    {
        PlayerActorRuntime actor = CurrentActor();
        PlayerMovement movement = actor.Movement;
        Require(movement != null, "PlayerMovement is missing.");
        PlayerEvadeController evade = actor.PlayerKit != null ? actor.PlayerKit.EvadeController : null;
        Require(evade != null, "PlayerEvadeController is missing.");
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        if (!movement.IsMeleeCombatLocomotionMode)
            throw new InvalidOperationException("Melee combat locomotion is unavailable; stance weapon or combat mode missing.");
        if (!observedEvadeState)
        {
            if (!evade.IsEvading)
            {
                PressEdge(Key.LeftShift);
                WaitSeconds(4f);
                return;
            }
            observedEvadeState = true;
            Require(coordinator.CurrentLocomotion == PlayerLocomotionState.Evading,
                "Evading but Locomotion is " + coordinator.CurrentLocomotion + " instead of Evading.");
            ReleaseKey(Key.LeftShift);
        }
        if (evade.IsEvading)
        {
            WaitSeconds(6f);
            return;
        }
        Require(coordinator.CurrentLocomotion != PlayerLocomotionState.Evading,
            "Stale Locomotion.Evading after evade settled.");
        step = VerifyStep.LootApi;
        WaitFrames(10);
    }

    private static void StepLootApi()
    {
        PlayerActorRuntime actor = CurrentActor();
        PlayerMovement movement = actor.Movement;
        Require(movement != null, "PlayerMovement is missing.");
        Vector3 target = actor.transform.position + actor.transform.forward * 5f;
        Require(movement.BeginLootAutoMove(target), "BeginLootAutoMove rejected the public path.");
        Require(movement.IsLootAutoMoveActive, "IsLootAutoMoveActive is false after BeginLootAutoMove.");
        movement.UpdateLootAutoMoveDestination(target);
        WaitFrames(30);
        Require(movement.IsLootAutoMoveActive, "Loot auto move did not survive 30 frames.");
        movement.CancelLootAutoMove();
        Require(!movement.IsLootAutoMoveActive, "CancelLootAutoMove did not clear the request.");
        step = VerifyStep.TeleportApi;
        WaitFrames(5);
    }

    private static void StepTeleportApi()
    {
        PlayerActorRuntime actor = CurrentActor();
        Vector3 before = actor.transform.position;
        Vector3 away = before + new Vector3(5f, 0f, 0f);
        ActorTeleportUtility.TeleportSafely(actor.transform, away, actor.transform.rotation);
        Require(FlatDistance(before, actor.transform.position) > 4f, "TeleportSafely did not move the actor.");
        ActorTeleportUtility.TeleportSafely(actor.transform, before, actor.transform.rotation);
        Require(FlatDistance(before, actor.transform.position) < 0.5f, "TeleportSafely did not restore the actor.");
        step = VerifyStep.DeathReset;
        WaitFrames(10);
    }

    private static void StepDeathReset()
    {
        PlayerActorRuntime actor = CurrentActor();
        CombatHealth health = actor.Health;
        Require(health != null, "CombatHealth is missing on the player actor.");
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        if (!health.IsDead)
        {
            health.TakeDamage(new DamageInfo(health.MaxHp * 10f, actor.transform.position));
            WaitSeconds(5f);
        }
        if (!health.IsDead)
            return;
        Require(coordinator.CurrentCondition == PlayerConditionState.Dead,
            "Lethal damage left Condition=" + coordinator.CurrentCondition + " instead of Dead.");
        health.ResetHealth();
        WaitFrames(10);
        Require(!health.IsDead, "ResetHealth did not revive the actor.");
        Require(coordinator.CurrentCondition == PlayerConditionState.Normal,
            "ResetHealth left Condition=" + coordinator.CurrentCondition + " instead of Normal.");
        Require(coordinator.CurrentAction == PlayerActionState.None,
            "ResetHealth left stale Action=" + coordinator.CurrentAction + ".");
        Require(coordinator.CurrentLocomotion != PlayerLocomotionState.Evading
            && coordinator.CurrentLocomotion != PlayerLocomotionState.ControlledMove,
            "ResetHealth left stale Locomotion=" + coordinator.CurrentLocomotion + ".");
        step = VerifyStep.Teardown;
        WaitFrames(2);
    }

    private static void StepTeardown()
    {
        ReleaseAllInputs();
        if (fixtureRoot != null)
        {
            UnityEngine.Object.Destroy(fixtureRoot);
            fixtureRoot = null;
            rigObject = null;
            rigController = null;
            rigMotor = null;
        }
        PlayerActorRuntime actor = PlayerContext.GetOrCreate() != null ? PlayerContext.GetOrCreate().CurrentActor : null;
        if (actor != null)
            ActorTeleportUtility.TeleportSafely(actor.transform, actorHomePosition, actorHomeRotation);
        if (actor != null && equippedValidationWeapon)
        {
            PlayerEquipment equipment = actor.Equipment;
            Require(equipment != null, "PlayerEquipment is missing during restore.");
            bool restoredWeapon = initialWeaponItem != null
                ? equipment.EquipWeaponItem(initialWeaponItem)
                : equipment.ClearWeaponSlot(equipment.ActiveWeaponSlotIndex);
            Require(restoredWeapon, "Validation weapon could not be restored to the original slot state.");
            equippedValidationWeapon = false;
            WaitFrames(5);
            return;
        }
        PlayerCombatModeController mode = PlayerCombatModeController.GetOrCreate();
        if (mode != null && mode.IsCombatModeActive != initialCombatMode)
        {
            PressEdge(Key.X);
            WaitSeconds(5f);
            if (mode.IsCombatModeActive != initialCombatMode)
                return;
            ReleaseKey(Key.X);
        }
        step = VerifyStep.ErrorCheck;
        WaitFrames(10);
    }

    private static void StepErrorCheck()
    {
        Require(logErrorTotal == 0,
            "Managed Error/Exception/Assert raised during the verifier flow: "
            + (logErrors.Count > 0 ? logErrors[0] : "unknown"));
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        Require(coordinator.CurrentCondition == PlayerConditionState.Normal, "Teardown left Condition=" + coordinator.CurrentCondition + ".");
        Require(coordinator.CurrentAction == PlayerActionState.None, "Teardown left Action=" + coordinator.CurrentAction + ".");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Teardown left gameplay input blocked.");
        step = VerifyStep.Restore;
        WaitFrames(2);
    }

    private static void StepRestore()
    {
        Require(observedAttackStates && observedEvadeState && landObserved && stepPassed,
            "Observation flags incomplete before finish.");
        SessionState.SetString(FailMessageKey,
            "flat=" + flatDist60.ToString("0.00")
            + " fpsDiff=" + flatDiffPercent.ToString("0.00")
            + " drop=" + dropTime60.ToString("0.00")
            + " impact=" + dropImpactSpeed.ToString("0.0")
            + " slope=+" + slopeGain.ToString("0.0")
            + " steep=+" + steepGain.ToString("0.00")
            + " snapAir=" + snapAirFrames
            + " step=1 land=1 regress=1 errors=0");
        Finish(0);
    }

    private static void HoldKey(Key key)
    {
        if (heldKeys.Add(key))
            keyboardSyncNeeded = true;
    }

    private static void ReleaseKey(Key key)
    {
        if (heldKeys.Remove(key))
            keyboardSyncNeeded = true;
    }

    private static void ReleaseAllInputs()
    {
        if (heldKeys.Count > 0)
        {
            heldKeys.Clear();
            keyboardSyncNeeded = true;
        }
        SetLmbHeld(false);
    }

    private static void SetLmbHeld(bool held)
    {
        if (lmbHeld != held)
        {
            lmbHeld = held;
            mouseSyncNeeded = true;
        }
    }

    private static void PressEdge(Key key)
    {
        HoldKey(key);
        keyboardSyncNeeded = true;
    }

    private static void DriveInputs()
    {
        if (keyboardSyncNeeded && validationKeyboard != null)
        {
            Key[] keys = new Key[heldKeys.Count];
            heldKeys.CopyTo(keys);
            InputState.Change(validationKeyboard, new KeyboardState(keys), InputUpdateType.Dynamic);
            keyboardSyncNeeded = false;
        }
        if (mouseSyncNeeded && validationMouse != null)
        {
            MouseState mouseState = new MouseState();
            try
            {
                mouseState.position = validationMouse.position.ReadValue();
            }
            catch (Exception)
            {
            }
            mouseState = mouseState.WithButton(MouseButton.Left, lmbHeld);
            InputState.Change(validationMouse, mouseState, InputUpdateType.Dynamic);
            mouseSyncNeeded = false;
        }
    }

    private static void CreateValidationInputDevices()
    {
        DestroyValidationInputDevices();
        validationKeyboard = InputSystem.AddDevice<Keyboard>();
        validationMouse = InputSystem.AddDevice<Mouse>();
        validationKeyboard.MakeCurrent();
        validationMouse.MakeCurrent();
        InputSystem.onBeforeUpdate -= HandleBeforeInputUpdate;
        InputSystem.onBeforeUpdate += HandleBeforeInputUpdate;
    }

    private static void DestroyValidationInputDevices()
    {
        InputSystem.onBeforeUpdate -= HandleBeforeInputUpdate;
        if (validationKeyboard != null && validationKeyboard.added)
            InputSystem.RemoveDevice(validationKeyboard);
        if (validationMouse != null && validationMouse.added)
            InputSystem.RemoveDevice(validationMouse);
        validationKeyboard = null;
        validationMouse = null;
    }

    private static void HandleBeforeInputUpdate()
    {
        DriveInputs();
    }

    private static PlayerActorRuntime CurrentActor()
    {
        PlayerActorRuntime actor = PlayerContext.GetOrCreate() != null ? PlayerContext.GetOrCreate().CurrentActor : null;
        Require(actor != null, "PlayerActor was lost during verification.");
        return actor;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a;
        delta.y = 0f;
        return delta.magnitude;
    }

    private static string DescribeCurrentStep()
    {
        if (step != VerifyStep.ActorMove)
            return string.Empty;

        PlayerActorRuntime actor = PlayerContext.GetOrCreate() != null
            ? PlayerContext.GetOrCreate().CurrentActor
            : null;
        PlayerMovement movement = actor != null ? actor.Movement : null;
        PlayerInputFacade facade = actor != null ? actor.GetComponent<PlayerInputFacade>() : null;
        return "phase=" + actorMovePhase
            + " actor=" + (actor != null)
            + " movement=" + (movement != null)
            + " enabled=" + (movement != null && movement.enabled)
            + " active=" + (movement != null && movement.isActiveAndEnabled)
            + " blocked=" + GameplayInputBlocker.IsGameplayInputBlocked
            + " moveValue=" + (facade != null ? facade.MoveValue.ToString("F3") : "null")
            + " autoMove=" + (movement != null && movement.IsLootAutoMoveActive)
            + " start=" + stepPosition.ToString("F3")
            + " current=" + (actor != null ? actor.transform.position.ToString("F3") : "null");
    }

    private static void WaitFrames(int frameDelay)
    {
        waitUntilFrame = Time.frameCount + Math.Max(1, frameDelay);
        stepDeadline = 0f;
        if (timeoutAt <= 0f)
            timeoutAt = Time.realtimeSinceStartup + TimeoutSeconds;
    }

    private static void WaitSeconds(float seconds)
    {
        waitUntilFrame = Time.frameCount + 2;
        if (stepDeadline <= 0f)
            stepDeadline = Time.realtimeSinceStartup + Math.Max(1f, seconds);
        if (timeoutAt <= 0f)
            timeoutAt = Time.realtimeSinceStartup + TimeoutSeconds;
    }

    private static void Finish(int exitCode)
    {
        ReleaseAllInputs();
        SessionState.SetInt(ExitCodeKey, exitCode);
        EditorApplication.update -= UpdateVerification;
        EditorApplication.ExitPlaymode();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(step + ": " + message);
    }
}
