using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// GOAL A2 Play Mode 검증기. Editor 전용이며 씬/에셋을 저장·수정하지 않는다.
// SessionState + playModeStateChanged/update 패턴을 사용하고, 종료 전 런타임 전용 상태를 복원한다.
// 실제 Gameplay 입력 구동에는 InputSystem.QueueStateEvent만 사용한다(본 검증기 승인 범위).
// 실행하지 말 것. Codex가 리뷰 뒤 실행한다.
[InitializeOnLoad]
public static class OverburstGoalA2PlayModeVerifier
{
    private const string ActiveKey = "OverburstGoalA2PlayModeVerifier.Active";
    private const string ExitCodeKey = "OverburstGoalA2PlayModeVerifier.ExitCode";
    private const string FailMessageKey = "OverburstGoalA2PlayModeVerifier.FailMessage";
    private const string InputAssetPath = "Assets/ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions";
    private const string ValidationSwordAssetPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const float TimeoutSeconds = 180f;

    private enum VerifyStep
    {
        WaitForHideoutReady,
        MoveHold,
        MoveRelease,
        MeleeAttack,
        MeleeDisableCheck,
        CombatEvadeArrange,
        CombatEvade,
        StashOpen,
        StashClose,
        InventoryOpen,
        InventoryBlockedInputs,
        InventoryClose,
        DeathReset,
        ErrorCheck,
        Restore,
    }

    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float stepDeadline;
    private static float timeoutAt;

    private static readonly HashSet<Key> heldKeys = new HashSet<Key>();
    private static bool lmbHeld;
    private static bool keyboardSyncNeeded;
    private static bool mouseSyncNeeded;
    private static Keyboard validationKeyboard;
    private static Mouse validationMouse;
    private static bool previousRunInBackground;

    private static readonly List<string> logErrors = new List<string>();
    private static int logErrorTotal;

    private static float moveDistance;
    private static float blockedMoveDistance;
    private static bool observedAttackStates;
    private static bool observedEvadeState;
    private static bool observedStashStates;
    private static bool observedNonLethalHitCleanup;
    private static bool observedDeathReset;
    private static bool initialCombatMode;
    private static Vector3 actorHomePosition;
    private static Quaternion actorHomeRotation;
    private static Vector3 stepPosition;
    private static bool stepPositionSet;
    private static int stepPolls;
    private static ItemData initialWeaponItem;
    private static bool equippedValidationWeapon;

    static OverburstGoalA2PlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Application.logMessageReceived -= HandleLog;
        Application.logMessageReceived += HandleLog;
    }

    [MenuItem("OVERBURST/Codex/Validate/Input/Validate GOAL A2 Play Mode")]
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
            Application.runInBackground = true;
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
            Debug.Log("[OverburstGoalA2PlayModeVerifier] PASS " + failMessage);
        else
            Debug.LogError("[OverburstGoalA2PlayModeVerifier] FAIL " + failMessage);
    }

    private static void ResetRunState()
    {
        heldKeys.Clear();
        lmbHeld = false;
        keyboardSyncNeeded = false;
        mouseSyncNeeded = false;
        stepDeadline = 0f;
        moveDistance = 0f;
        blockedMoveDistance = 0f;
        observedAttackStates = false;
        observedEvadeState = false;
        observedStashStates = false;
        observedNonLethalHitCleanup = false;
        observedDeathReset = false;
        stepPositionSet = false;
        stepPolls = 0;
        initialWeaponItem = null;
        equippedValidationWeapon = false;
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying)
            return;
        // Hera/백그라운드 Editor에서는 Game View가 열린 상태여도 Player Loop가 자동으로
        // 진행되지 않을 수 있다. 실제 런타임 Update/InputSystem을 우회하지 않고 다음 틱만 요청한다.
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (Time.realtimeSinceStartup > timeoutAt)
                throw new TimeoutException("GOAL A2 PlayMode verification timed out at " + step + ".");
            if (Time.frameCount < waitUntilFrame)
                return;
            if (stepDeadline > 0f && Time.realtimeSinceStartup > stepDeadline)
                throw new TimeoutException("GOAL A2 PlayMode step timed out at " + step + ".");
            switch (step)
            {
                case VerifyStep.WaitForHideoutReady: StepHideoutReady(); break;
                case VerifyStep.MoveHold: StepMoveHold(); break;
                case VerifyStep.MoveRelease: StepMoveRelease(); break;
                case VerifyStep.MeleeAttack: StepMeleeAttack(); break;
                case VerifyStep.MeleeDisableCheck: StepMeleeDisableCheck(); break;
                case VerifyStep.CombatEvadeArrange: StepCombatEvadeArrange(); break;
                case VerifyStep.CombatEvade: StepCombatEvade(); break;
                case VerifyStep.StashOpen: StepStashOpen(); break;
                case VerifyStep.StashClose: StepStashClose(); break;
                case VerifyStep.InventoryOpen: StepInventoryOpen(); break;
                case VerifyStep.InventoryBlockedInputs: StepInventoryBlockedInputs(); break;
                case VerifyStep.InventoryClose: StepInventoryClose(); break;
                case VerifyStep.DeathReset: StepDeathReset(); break;
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

    // ---- Hideout 준비 + 항목 1 ----
    private static void StepHideoutReady()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        Require(flow != null, "PersistentSceneFlow.Instance is null.");
        if (flow.IsSwitching || flow.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            return;
        PlayerActorRuntime actor = PlayerContext.GetOrCreate() != null ? PlayerContext.GetOrCreate().CurrentActor : null;
        Require(actor != null, "PlayerActor is not ready in Hideout.");
        Require(Keyboard.current != null, "Keyboard.current is null; cannot drive Gameplay actions.");
        Require(validationMouse != null, "Validation mouse is null; cannot drive attack input.");
        PlayerInputFacade[] facades = UnityEngine.Object.FindObjectsByType<PlayerInputFacade>(FindObjectsSortMode.None);
        Require(facades.Length == 1, "Expected exactly one PlayerInputFacade, found " + facades.Length + ".");
        PlayerStateCoordinator[] coordinators = UnityEngine.Object.FindObjectsByType<PlayerStateCoordinator>(FindObjectsSortMode.None);
        Require(coordinators.Length == 1, "Expected exactly one PlayerStateCoordinator, found " + coordinators.Length + ".");
        Require(facades[0].SourceAsset != null
            && string.Equals(AssetDatabase.GetAssetPath(facades[0].SourceAsset), InputAssetPath, StringComparison.OrdinalIgnoreCase),
            "PlayerInputFacade source asset is not " + InputAssetPath + ".");
        InputDevice[] validationDevices = new InputDevice[] { validationKeyboard, validationMouse };
        Require(facades[0].RuntimeAsset != null, "PlayerInputFacade runtime asset is missing.");
        facades[0].RuntimeAsset.devices = validationDevices;
        Require(facades[0].IsGameplayEnabled && facades[0].IsUiEnabled, "Gameplay/UI maps are not enabled.");
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
            ItemData validationWeapon = new ItemData(swordData, 1, ItemGrade.Common);
            Require(equipment.EquipWeaponItem(validationWeapon),
                "Validation sword could not be equipped through PlayerEquipment.");
            equippedValidationWeapon = true;
        }
        step = VerifyStep.MoveHold;
        WaitFrames(5);
    }

    // ---- 항목 2: W 이동 ----
    private static void StepMoveHold()
    {
        PlayerActorRuntime actor = CurrentActor();
        if (!stepPositionSet)
        {
            Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input is blocked before W move.");
            stepPosition = actor.transform.position;
            stepPositionSet = true;
            HoldKey(Key.W);
            WaitSeconds(6f);
        }
        moveDistance = FlatDistance(stepPosition, actor.transform.position);
        if (moveDistance < 0.15f)
            return;
        stepPositionSet = false;
        step = VerifyStep.MoveRelease;
        WaitFrames(2);
    }

    private static void StepMoveRelease()
    {
        PlayerActorRuntime actor = CurrentActor();
        if (!stepPositionSet)
        {
            ReleaseKey(Key.W);
            stepPositionSet = true;
            stepPolls = 0;
            WaitFrames(15);
            return;
        }
        if (stepPolls == 0)
        {
            // PlayerMovement의 정상 감속 구간을 지난 뒤 별도 관찰 창에서 잔류 이동을 검사한다.
            stepPosition = actor.transform.position;
            stepPolls = 1;
            WaitFrames(10);
            return;
        }
        float releaseDrift = FlatDistance(stepPosition, actor.transform.position);
        Require(releaseDrift < 0.05f, "Actor kept moving after W release; settled drift=" + releaseDrift + ".");
        stepPositionSet = false;
        stepPolls = 0;
        step = VerifyStep.MeleeAttack;
        WaitFrames(5);
    }

    // ---- 항목 3: LMB 근접 공격 + 비활성화 정리 ----
    private static void StepMeleeAttack()
    {
        PlayerActorRuntime actor = CurrentActor();
        MeleeRuntime melee = actor.PlayerKit != null ? actor.PlayerKit.MeleeRuntime : null;
        Require(melee != null, "MeleeRuntime is missing on the player kit.");
        PlayerMovement movement = actor.Movement;
        Require(movement != null && movement.IsGrounded, "Player is not grounded; melee attack cannot start.");
        Require(melee.CanUseCurrentWeapon, "Melee slash weapon is not equipped; melee attack cannot start.");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input is blocked before melee attack.");
        PlayerEvadeController evade = actor.PlayerKit != null ? actor.PlayerKit.EvadeController : null;
        Require(evade == null || !evade.IsEvading, "Player is evading; melee attack cannot start.");
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        if (!lmbHeld)
        {
            SetLmbHeld(true);
            WaitSeconds(5f);
        }
        if (!melee.IsAttackInProgress)
            return;
        Require(coordinator.CurrentAction == PlayerActionState.Attack,
            "Melee attack runs but Action is " + coordinator.CurrentAction + " instead of Attack.");
        Require(coordinator.CurrentLocomotion == PlayerLocomotionState.ControlledMove,
            "Melee attack runs but Locomotion is " + coordinator.CurrentLocomotion + " instead of ControlledMove.");
        observedAttackStates = true;
        step = VerifyStep.MeleeDisableCheck;
        WaitFrames(2);
    }

    private static void StepMeleeDisableCheck()
    {
        PlayerActorRuntime actor = CurrentActor();
        MeleeRuntime melee = actor.PlayerKit != null ? actor.PlayerKit.MeleeRuntime : null;
        Require(melee != null, "MeleeRuntime is missing on the player kit.");
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        if (melee.enabled)
        {
            melee.enabled = false;
            WaitFrames(3);
            return;
        }
        Require(!melee.IsAttackInProgress, "Attack persists after MeleeRuntime disable.");
        Require(coordinator.CurrentAction == PlayerActionState.None,
            "Melee disable left stale Action=" + coordinator.CurrentAction + ".");
        Require(coordinator.CurrentLocomotion != PlayerLocomotionState.ControlledMove,
            "Melee disable left stale Locomotion=" + coordinator.CurrentLocomotion + ".");
        melee.enabled = true;
        SetLmbHeld(false);
        if (melee.IsAttackInProgress)
        {
            WaitFrames(2);
            return;
        }
        step = VerifyStep.CombatEvadeArrange;
        WaitFrames(10);
    }

    // ---- 항목 4: X 전투모드 + Shift 회피 ----
    private static void StepCombatEvadeArrange()
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
        step = VerifyStep.CombatEvade;
        WaitFrames(5);
    }

    private static void StepCombatEvade()
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
        if (!evade.IsEvading && !observedEvadeState)
        {
            PressEdge(Key.LeftShift);
            WaitSeconds(4f);
        }
        if (!evade.IsEvading && !observedEvadeState)
            return;
        if (evade.IsEvading)
        {
            observedEvadeState = true;
            Require(coordinator.CurrentLocomotion == PlayerLocomotionState.Evading,
                "Evading but Locomotion is " + coordinator.CurrentLocomotion + " instead of Evading.");
            ReleaseKey(Key.LeftShift);
            WaitSeconds(6f);
            return;
        }
        Require(coordinator.CurrentLocomotion != PlayerLocomotionState.Evading,
            "Stale Locomotion.Evading after evade settled.");
        step = VerifyStep.StashOpen;
        WaitFrames(10);
    }

    // ---- 항목 5: F 보관함 ----
    private static void StepStashOpen()
    {
        StashInteractable stash = FindHideoutStash();
        StashUI stashUI = FindStashUI();
        Require(stashUI != null, "StashUI was not found.");
        PlayerActorRuntime actor = CurrentActor();
        if (!stepPositionSet)
        {
            Require(!stashUI.IsOpen, "StashUI is already open before F open.");
            Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input is blocked before stash open.");
            stepPosition = actor.transform.position;
            stepPositionSet = true;
            ActorTeleportUtility.TeleportSafely(actor.transform, StashApproachPoint(stash), actor.transform.rotation);
            WaitFrames(5);
            return;
        }
        Require(stash.IsPlayerInRange(), "Teleported player is outside the stash range.");
        PressEdge(Key.F);
        WaitSeconds(5f);
        if (!stashUI.IsOpen)
            return;
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        Require(GameplayInputBlocker.IsGameplayInputBlocked, "StashUI open did not block gameplay input.");
        Require(coordinator.CurrentCondition == PlayerConditionState.InputBlocked,
            "StashUI open left Condition=" + coordinator.CurrentCondition + " instead of InputBlocked.");
        Require(coordinator.CurrentAction == PlayerActionState.Interacting,
            "StashUI open left Action=" + coordinator.CurrentAction + " instead of Interacting.");
        observedStashStates = true;
        ReleaseKey(Key.F);
        step = VerifyStep.StashClose;
        WaitFrames(2);
    }

    private static void StepStashClose()
    {
        StashUI stashUI = FindStashUI();
        Require(stashUI != null, "StashUI was not found.");
        PlayerActorRuntime actor = CurrentActor();
        PressEdge(Key.F);
        WaitSeconds(5f);
        if (stashUI.IsOpen)
            return;
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input stayed blocked after stash close.");
        Require(coordinator.CurrentCondition == PlayerConditionState.Normal,
            "Stash close left Condition=" + coordinator.CurrentCondition + " instead of Normal.");
        Require(coordinator.CurrentAction == PlayerActionState.None,
            "Stash close left Action=" + coordinator.CurrentAction + " instead of None.");
        ReleaseKey(Key.F);
        ActorTeleportUtility.TeleportSafely(actor.transform, stepPosition, actor.transform.rotation);
        stepPositionSet = false;
        step = VerifyStep.InventoryOpen;
        WaitFrames(10);
    }

    // ---- 항목 6: Tab 인벤토리 + 차단 ----
    private static void StepInventoryOpen()
    {
        InventoryUI inventory = FindInventoryUI();
        Require(inventory != null, "InventoryUI was not found.");
        if (!inventory.IsVisible)
        {
            PressEdge(Key.Tab);
            WaitSeconds(5f);
        }
        if (!inventory.IsVisible)
            return;
        Require(GameplayInputBlocker.IsGameplayInputBlocked, "InventoryUI open did not block gameplay input.");
        ReleaseKey(Key.Tab);
        step = VerifyStep.InventoryBlockedInputs;
        WaitFrames(2);
    }

    private static void StepInventoryBlockedInputs()
    {
        PlayerActorRuntime actor = CurrentActor();
        InventoryUI inventory = FindInventoryUI();
        Require(inventory != null && inventory.IsVisible, "InventoryUI closed during blocked-input check.");
        MeleeRuntime melee = actor.PlayerKit != null ? actor.PlayerKit.MeleeRuntime : null;
        StashUI stashUI = FindStashUI();
        if (!stepPositionSet)
        {
            // 차단 중 F가 닫힌 보관함을 열지 못하는지 의미 있게 보려면 보관함 앞에서 검증한다.
            StashInteractable stash = FindHideoutStash();
            ActorTeleportUtility.TeleportSafely(actor.transform, StashApproachPoint(stash), actor.transform.rotation);
            Require(stash.IsPlayerInRange(), "Teleported player is outside the stash range for the blocked check.");
            stepPosition = actor.transform.position;
            stepPositionSet = true;
            stepPolls = 0;
            HoldKey(Key.W);
            SetLmbHeld(true);
            PressEdge(Key.F);
            WaitFrames(2);
            return;
        }
        blockedMoveDistance = FlatDistance(stepPosition, actor.transform.position);
        Require(blockedMoveDistance < 0.1f, "Blocked W moved the actor by " + blockedMoveDistance + ".");
        Require(melee == null || !melee.IsAttackInProgress, "Blocked LMB started a melee attack.");
        Require(stashUI == null || !stashUI.IsOpen, "Blocked F opened the stash.");
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        if (coordinator != null)
        {
            Require(coordinator.CurrentAction != PlayerActionState.Attack, "Blocked LMB reached Action.Attack.");
        }
        if (stepPolls < 60)
        {
            stepPolls++;
            WaitFrames(1);
            return;
        }
        ReleaseKey(Key.W);
        SetLmbHeld(false);
        ReleaseKey(Key.F);
        stepPositionSet = false;
        stepPolls = 0;
        step = VerifyStep.InventoryClose;
        WaitFrames(5);
    }

    private static void StepInventoryClose()
    {
        InventoryUI inventory = FindInventoryUI();
        Require(inventory != null, "InventoryUI was not found.");
        if (inventory.IsVisible)
        {
            PressEdge(Key.Tab);
            WaitSeconds(5f);
        }
        if (inventory.IsVisible)
            return;
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input stayed blocked after inventory close.");
        Require(coordinator.CurrentCondition == PlayerConditionState.Normal,
            "Inventory close left Condition=" + coordinator.CurrentCondition + " instead of Normal.");
        ReleaseKey(Key.Tab);
        step = VerifyStep.DeathReset;
        WaitFrames(10);
    }

    // ---- 항목 7: 사망/리셋 ----
    private static void StepDeathReset()
    {
        PlayerActorRuntime actor = CurrentActor();
        CombatHealth health = actor.Health;
        Require(health != null, "CombatHealth is missing on the player actor.");
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        Require(coordinator != null, "PlayerStateCoordinator.Current is null.");
        if (stepPolls == 0)
        {
            health.ResetHealth();
            health.TakeDamage(new DamageInfo(Mathf.Max(1f, health.MaxHp * 0.1f), actor.transform.position));
            stepPolls = 1;
            WaitFrames(5);
            return;
        }
        if (stepPolls == 1)
        {
            Require(!health.IsDead, "Non-lethal hit unexpectedly killed the actor.");
            Require(coordinator.CurrentCondition == PlayerConditionState.Normal,
                "Non-lethal hit left Condition=" + coordinator.CurrentCondition + ".");
            Require(coordinator.CurrentAction == PlayerActionState.None,
                "Non-lethal hit left Action=" + coordinator.CurrentAction + ".");
            Require(coordinator.CurrentLocomotion != PlayerLocomotionState.Evading
                && coordinator.CurrentLocomotion != PlayerLocomotionState.ControlledMove,
                "Non-lethal hit left stale Locomotion=" + coordinator.CurrentLocomotion + ".");
            observedNonLethalHitCleanup = true;
            health.ResetHealth();
            stepPolls = 2;
            WaitFrames(5);
            return;
        }
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
        observedDeathReset = true;
        step = VerifyStep.ErrorCheck;
        WaitFrames(2);
    }

    // ---- 항목 8: 관리 오류 없음 ----
    private static void StepErrorCheck()
    {
        Require(logErrorTotal == 0,
            "Managed Error/Exception/Assert raised during the verifier flow: "
            + (logErrors.Count > 0 ? logErrors[0] : "unknown"));
        step = VerifyStep.Restore;
        WaitFrames(2);
    }

    // ---- 복원 후 종료 ----
    private static void StepRestore()
    {
        ReleaseAllInputs();
        InventoryUI inventory = FindInventoryUI();
        if (inventory != null && inventory.IsVisible)
        {
            inventory.Toggle();
            WaitFrames(5);
            return;
        }
        StashUI stashUI = FindStashUI();
        if (stashUI != null && stashUI.IsOpen)
        {
            stashUI.Close();
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
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        if (coordinator == null
            || coordinator.CurrentCondition != PlayerConditionState.Normal
            || coordinator.CurrentAction != PlayerActionState.None
            || GameplayInputBlocker.IsGameplayInputBlocked)
        {
            WaitFrames(10);
            coordinator = PlayerStateCoordinator.Current;
            Require(coordinator != null, "PlayerStateCoordinator.Current is null during restore.");
            Require(coordinator.CurrentCondition == PlayerConditionState.Normal, "Restore left Condition=" + coordinator.CurrentCondition + ".");
            Require(coordinator.CurrentAction == PlayerActionState.None, "Restore left Action=" + coordinator.CurrentAction + ".");
            Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Restore left gameplay input blocked.");
        }
        SessionState.SetString(FailMessageKey,
            "move=" + moveDistance.ToString("0.00")
            + " blockedMove=" + blockedMoveDistance.ToString("0.00")
            + " attack=Attack+ControlledMove"
            + " evade=Evading"
            + " stash=InputBlocked+Interacting"
            + " hitCleanup=1"
            + " deathReset=1"
            + " errors=0");
        Require(observedAttackStates && observedEvadeState && observedStashStates
            && observedNonLethalHitCleanup && observedDeathReset,
            "Observation flags incomplete before finish.");
        Finish(0);
    }

    // ---- 입력 구동 (검증기 승인 범위) ----
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
        Keyboard keyboard = validationKeyboard != null ? validationKeyboard : Keyboard.current;
        if (keyboardSyncNeeded && keyboard != null)
        {
            Key[] keys = new Key[heldKeys.Count];
            heldKeys.CopyTo(keys);
            InputState.Change(keyboard, new KeyboardState(keys), InputUpdateType.Dynamic);
            keyboardSyncNeeded = false;
        }
        Mouse mouse = validationMouse;
        if (mouseSyncNeeded && mouse != null)
        {
            MouseState mouseState = new MouseState();
            try
            {
                mouseState.position = mouse.position.ReadValue();
            }
            catch (Exception)
            {
            }
            mouseState = mouseState.WithButton(MouseButton.Left, lmbHeld);
            InputState.Change(mouse, mouseState, InputUpdateType.Dynamic);
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

    // ---- 조회 ----
    private static PlayerActorRuntime CurrentActor()
    {
        PlayerActorRuntime actor = PlayerContext.GetOrCreate() != null ? PlayerContext.GetOrCreate().CurrentActor : null;
        Require(actor != null, "PlayerActor was lost during verification.");
        return actor;
    }

    private static StashInteractable FindHideoutStash()
    {
        StashInteractable[] candidates = UnityEngine.Object.FindObjectsByType<StashInteractable>(FindObjectsSortMode.None);
        for (int i = 0; i < candidates.Length; i++)
        {
            StashInteractable candidate = candidates[i];
            if (candidate != null && candidate.isActiveAndEnabled
                && candidate.gameObject.scene.name == PersistentSceneFlow.HideoutSceneName)
                return candidate;
        }
        throw new InvalidOperationException("No active StashInteractable in HideoutScene.");
    }

    private static StashUI FindStashUI()
    {
        StashUI[] candidates = UnityEngine.Object.FindObjectsByType<StashUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < candidates.Length; i++)
        {
            StashUI candidate = candidates[i];
            if (candidate != null && candidate.HasUsableCanvasRoot
                && candidate.gameObject.scene.name == PersistentSceneFlow.PersistentSceneName)
                return candidate;
        }
        return null;
    }

    private static InventoryUI FindInventoryUI()
    {
        return UnityEngine.Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
    }

    private static Vector3 StashApproachPoint(StashInteractable stash)
    {
        Vector3 point = stash.transform.position + new Vector3(1.2f, 0f, 1.2f);
        point.y = stash.transform.position.y;
        return point;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a;
        delta.y = 0f;
        return delta.magnitude;
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
