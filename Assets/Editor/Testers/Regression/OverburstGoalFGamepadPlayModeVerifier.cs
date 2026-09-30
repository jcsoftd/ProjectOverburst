using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// GOAL F에서 실제 Gamepad 바인딩과 UI map 전환을 확인하는 Editor 전용 검증기다.
// 런타임 자산과 씬은 저장하지 않으며, 생성한 가상 장치와 장비 상태를 종료 전에 복구한다.
[InitializeOnLoad]
public static class OverburstGoalFGamepadPlayModeVerifier
{
    private const string ActiveKey =
        "OverburstGoalFGamepadPlayModeVerifier.Active";
    private const string ExitCodeKey =
        "OverburstGoalFGamepadPlayModeVerifier.ExitCode";
    private const string ResultKey =
        "OverburstGoalFGamepadPlayModeVerifier.Result";
    private const string ValidationSwordAssetPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/"
        + "OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const float TimeoutSeconds = 120f;

    private enum VerifyStep
    {
        WaitForHideout,
        Move,
        ReleaseMove,
        Attack,
        WaitForAttackEnd,
        UiNavigate,
        UiSubmit,
        Restore,
    }

    private static readonly List<string> runtimeErrors = new List<string>();
    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float timeoutAt;
    private static bool previousRunInBackground;
    private static Gamepad validationGamepad;
    private static Vector2 leftStick;
    private static bool westHeld;
    private static bool southHeld;
    private static bool dpadDownHeld;
    private static Vector3 actorHomePosition;
    private static Quaternion actorHomeRotation;
    private static Vector3 moveStartPosition;
    private static float moveDistance;
    private static bool attackObserved;
    private static bool navigateObserved;
    private static bool submitObserved;
    private static bool deviceSwitchObserved;
    private static bool gameplayWasEnabled;
    private static ItemData initialWeaponItem;
    private static bool equippedValidationWeapon;
    private static InputAction navigateAction;
    private static InputAction submitAction;

    static OverburstGoalFGamepadPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Application.logMessageReceived -= HandleLog;
        Application.logMessageReceived += HandleLog;
    }

    [MenuItem(
        "OVERBURST/Codex/Validate/Regression/"
        + "Validate GOAL F Gamepad Play Mode")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "PlayMode is already active or changing.");
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.name != PersistentSceneFlow.PersistentSceneName)
        {
            throw new InvalidOperationException(
                "PersistentScene must be active, current="
                + activeScene.name + ".");
        }

        if (activeScene.isDirty)
            throw new InvalidOperationException("PersistentScene is dirty.");

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(ExitCodeKey, 1);
        SessionState.SetString(ResultKey, string.Empty);
        runtimeErrors.Clear();
        EditorApplication.EnterPlaymode();
    }

    private static void HandleLog(
        string message,
        string stackTrace,
        LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        if (type != LogType.Error
            && type != LogType.Exception
            && type != LogType.Assert)
        {
            return;
        }

        if (runtimeErrors.Count < 20)
            runtimeErrors.Add("[" + type + "] " + message);
    }

    private static void HandlePlayModeStateChanged(
        PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            ResetRunState();
            validationGamepad = InputSystem.AddDevice<Gamepad>();
            validationGamepad.MakeCurrent();
            InputSystem.onBeforeUpdate -= DriveGamepad;
            InputSystem.onBeforeUpdate += DriveGamepad;
            step = VerifyStep.WaitForHideout;
            WaitFrames(30);
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            CleanupInput();
            Application.runInBackground = previousRunInBackground;
            EditorApplication.update -= UpdateVerification;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        string result = SessionState.GetString(ResultKey, string.Empty);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseInt(ExitCodeKey);
        SessionState.EraseString(ResultKey);
        if (exitCode == 0)
            Debug.Log("[OverburstGoalFGamepadPlayModeVerifier] PASS " + result);
        else
            Debug.LogError("[OverburstGoalFGamepadPlayModeVerifier] FAIL " + result);
    }

    private static void ResetRunState()
    {
        step = VerifyStep.WaitForHideout;
        waitUntilFrame = 0;
        timeoutAt = Time.realtimeSinceStartup + TimeoutSeconds;
        leftStick = Vector2.zero;
        westHeld = false;
        southHeld = false;
        dpadDownHeld = false;
        moveDistance = 0f;
        attackObserved = false;
        navigateObserved = false;
        submitObserved = false;
        deviceSwitchObserved = false;
        equippedValidationWeapon = false;
        initialWeaponItem = null;
        navigateAction = null;
        submitAction = null;
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying)
            return;

        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (Time.realtimeSinceStartup > timeoutAt)
            {
                throw new TimeoutException(
                    "GOAL F Gamepad verification timed out at " + step + ".");
            }

            if (Time.frameCount < waitUntilFrame)
                return;

            switch (step)
            {
                case VerifyStep.WaitForHideout: StepWaitForHideout(); break;
                case VerifyStep.Move: StepMove(); break;
                case VerifyStep.ReleaseMove: StepReleaseMove(); break;
                case VerifyStep.Attack: StepAttack(); break;
                case VerifyStep.WaitForAttackEnd: StepWaitForAttackEnd(); break;
                case VerifyStep.UiNavigate: StepUiNavigate(); break;
                case VerifyStep.UiSubmit: StepUiSubmit(); break;
                case VerifyStep.Restore: StepRestore(); break;
            }
        }
        catch (Exception exception)
        {
            SessionState.SetString(ResultKey, step + ": " + exception.Message);
            Finish(1);
        }
    }

    private static void StepWaitForHideout()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null
            || flow.IsSwitching
            || flow.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
        {
            return;
        }

        PlayerActorRuntime actor = CurrentActor();
        PlayerInputFacade facade = PlayerInputFacade.Current;
        Require(facade != null && facade.IsInitialized,
            "PlayerInputFacade is not initialized.");
        Require(facade.IsGameplayEnabled && facade.IsUiEnabled,
            "Gameplay/UI maps are not enabled.");
        Require(facade.TryGetUiAction("Navigate", out navigateAction)
            && navigateAction != null,
            "UI Navigate action is missing.");
        Require(facade.TryGetUiAction("Submit", out submitAction)
            && submitAction != null,
            "UI Submit action is missing.");
        navigateAction.performed -= HandleNavigate;
        navigateAction.performed += HandleNavigate;
        submitAction.performed -= HandleSubmit;
        submitAction.performed += HandleSubmit;

        actorHomePosition = actor.transform.position;
        actorHomeRotation = actor.transform.rotation;
        moveStartPosition = actor.transform.position;
        gameplayWasEnabled = facade.IsGameplayEnabled;

        PlayerEquipment equipment = actor.Equipment;
        Require(equipment != null, "PlayerEquipment is missing.");
        initialWeaponItem =
            equipment.GetWeaponSlotItem(equipment.ActiveWeaponSlotIndex);
        if (!equipment.CanCurrentWeaponUseMeleeSlash)
        {
            WeaponItemData sword =
                AssetDatabase.LoadAssetAtPath<WeaponItemData>(
                    ValidationSwordAssetPath);
            Require(sword != null, "Validation sword is missing.");
            Require(
                equipment.EquipWeaponItem(
                    new ItemData(sword, 1, ItemGrade.Common)),
                "Validation sword could not be equipped.");
            equippedValidationWeapon = true;
        }

        leftStick = Vector2.up;
        step = VerifyStep.Move;
        WaitFrames(2);
    }

    private static void StepMove()
    {
        PlayerActorRuntime actor = CurrentActor();
        PlayerInputFacade facade = PlayerInputFacade.Current;
        Require(facade != null, "PlayerInputFacade was lost.");
        moveDistance = FlatDistance(moveStartPosition, actor.transform.position);
        if (moveDistance < 0.15f)
            return;

        Require(facade.LastInputDevice == validationGamepad,
            "Last input device did not switch to the validation Gamepad.");
        deviceSwitchObserved = true;
        leftStick = Vector2.zero;
        step = VerifyStep.ReleaseMove;
        WaitFrames(20);
    }

    private static void StepReleaseMove()
    {
        PlayerActorRuntime actor = CurrentActor();
        Require(PlayerInputFacade.Current != null
            && PlayerInputFacade.Current.MoveValue.sqrMagnitude < 0.001f,
            "Gamepad Move remained active after release.");
        Require(actor.Movement != null && actor.Movement.IsGrounded,
            "Player is not grounded before Gamepad attack.");
        westHeld = true;
        step = VerifyStep.Attack;
        WaitFrames(1);
    }

    private static void StepAttack()
    {
        PlayerActorRuntime actor = CurrentActor();
        MeleeRuntime melee =
            actor.PlayerKit != null ? actor.PlayerKit.MeleeRuntime : null;
        Require(melee != null, "MeleeRuntime is missing.");
        if (!melee.IsAttackInProgress)
            return;

        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null
            && coordinator.CurrentAction == PlayerActionState.Attack,
            "Gamepad Attack did not reach PlayerActionState.Attack.");
        attackObserved = true;
        westHeld = false;
        step = VerifyStep.WaitForAttackEnd;
        WaitFrames(5);
    }

    private static void StepWaitForAttackEnd()
    {
        PlayerActorRuntime actor = CurrentActor();
        MeleeRuntime melee =
            actor.PlayerKit != null ? actor.PlayerKit.MeleeRuntime : null;
        Require(melee != null, "MeleeRuntime is missing.");
        if (melee.IsAttackInProgress)
            return;

        PlayerInputFacade facade = PlayerInputFacade.Current;
        Require(facade != null, "PlayerInputFacade was lost.");
        facade.DisableGameplay();
        dpadDownHeld = true;
        step = VerifyStep.UiNavigate;
        WaitFrames(2);
    }

    private static void StepUiNavigate()
    {
        Require(navigateObserved,
            "Gamepad dpad did not perform UI/Navigate.");
        Require(PlayerInputFacade.Current != null
            && PlayerInputFacade.Current.LastInputDevice == validationGamepad,
            "UI navigation did not keep Gamepad as the last device.");
        dpadDownHeld = false;
        southHeld = true;
        step = VerifyStep.UiSubmit;
        WaitFrames(2);
    }

    private static void StepUiSubmit()
    {
        Require(submitObserved,
            "Gamepad south button did not perform UI/Submit.");
        southHeld = false;
        step = VerifyStep.Restore;
        WaitFrames(3);
    }

    private static void StepRestore()
    {
        PlayerInputFacade facade = PlayerInputFacade.Current;
        Require(facade != null, "PlayerInputFacade was lost during restore.");
        if (gameplayWasEnabled && !facade.IsGameplayEnabled)
        {
            facade.EnableGameplay();
            WaitFrames(2);
            return;
        }

        PlayerActorRuntime actor = CurrentActor();
        ActorTeleportUtility.TeleportSafely(
            actor.transform,
            actorHomePosition,
            actorHomeRotation);
        if (equippedValidationWeapon)
        {
            PlayerEquipment equipment = actor.Equipment;
            Require(equipment != null,
                "PlayerEquipment is missing during restore.");
            bool restored = initialWeaponItem != null
                ? equipment.EquipWeaponItem(initialWeaponItem)
                : equipment.ClearWeaponSlot(equipment.ActiveWeaponSlotIndex);
            Require(restored,
                "Validation weapon could not be restored.");
            equippedValidationWeapon = false;
            WaitFrames(3);
            return;
        }

        Require(runtimeErrors.Count == 0,
            "Managed Error/Exception/Assert: "
            + (runtimeErrors.Count > 0 ? runtimeErrors[0] : "unknown"));
        Require(deviceSwitchObserved
            && attackObserved
            && navigateObserved
            && submitObserved,
            "Observation flags are incomplete.");
        Require(facade.IsGameplayEnabled == gameplayWasEnabled
            && facade.IsUiEnabled,
            "Input maps were not restored.");
        SessionState.SetString(
            ResultKey,
            "deviceSwitch=1 move=" + moveDistance.ToString("0.00")
            + " attack=1 uiNavigate=1 uiSubmit=1 mapsRestored=1 errors=0");
        Finish(0);
    }

    private static void HandleNavigate(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();
        if (value.y < -0.5f)
            navigateObserved = true;
    }

    private static void HandleSubmit(InputAction.CallbackContext context)
    {
        if (context.performed)
            submitObserved = true;
    }

    private static void DriveGamepad()
    {
        if (validationGamepad == null || !validationGamepad.added)
            return;
        GamepadState state = new GamepadState
        {
            leftStick = leftStick,
        };
        state = state.WithButton(GamepadButton.West, westHeld);
        state = state.WithButton(GamepadButton.South, southHeld);
        state = state.WithButton(GamepadButton.DpadDown, dpadDownHeld);
        InputState.Change(
            validationGamepad,
            state,
            InputUpdateType.Dynamic);
    }

    private static void CleanupInput()
    {
        if (navigateAction != null)
            navigateAction.performed -= HandleNavigate;
        if (submitAction != null)
            submitAction.performed -= HandleSubmit;
        navigateAction = null;
        submitAction = null;
        InputSystem.onBeforeUpdate -= DriveGamepad;
        if (validationGamepad != null && validationGamepad.added)
            InputSystem.RemoveDevice(validationGamepad);
        validationGamepad = null;
    }

    private static PlayerActorRuntime CurrentActor()
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        PlayerActorRuntime actor =
            context != null ? context.CurrentActor : null;
        Require(actor != null, "PlayerActor is not ready.");
        return actor;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a;
        delta.y = 0f;
        return delta.magnitude;
    }

    private static void WaitFrames(int count)
    {
        waitUntilFrame = Time.frameCount + Math.Max(1, count);
    }

    private static void Finish(int exitCode)
    {
        leftStick = Vector2.zero;
        westHeld = false;
        southHeld = false;
        dpadDownHeld = false;
        SessionState.SetInt(ExitCodeKey, exitCode);
        EditorApplication.update -= UpdateVerification;
        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(step + ": " + message);
    }
}
