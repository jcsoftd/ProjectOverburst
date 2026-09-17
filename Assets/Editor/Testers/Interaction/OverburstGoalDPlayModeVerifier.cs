using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// GOAL D Play 검증: 실제 Hideout 플레이어/상점/창고/포탈과 보행·착지 발소리를 검사한다.
[InitializeOnLoad]
public static class OverburstGoalDPlayModeVerifier
{
    private const string ActiveKey = "OverburstGoalDPlayModeVerifier.Active";
    private const string ResultKey = "OverburstGoalDPlayModeVerifier.Result";
    private const string FailKey = "OverburstGoalDPlayModeVerifier.Fail";
    private const float TimeoutSeconds = 240f;

    private enum Step
    {
        WaitForHideout,
        FixtureSelection,
        SurfaceResolution,
        MerchantOpen,
        MerchantClose,
        StashOpen,
        StashClose,
        PortalSelection,
        RunStart,
        RunWait,
        RunSettle,
        RunStop,
        WalkToggle,
        WalkStart,
        WalkWait,
        WalkSettle,
        WalkStop,
        LandingStart,
        LandingWait,
        DungeonEnter,
        DungeonWait,
        DungeonRunToggle,
        DungeonRunStart,
        DungeonRunWait,
        DungeonRunSettle,
        DungeonWalkToggle,
        DungeonWalkStart,
        DungeonWalkWait,
        DungeonWalkSettle,
        DungeonLandingStart,
        DungeonLandingWait,
        DungeonReturn,
        WaitForHideoutReturn,
        Finish,
    }

    private static readonly List<string> runtimeErrors = new List<string>();
    private static readonly List<FixtureInteractable> fixtures = new List<FixtureInteractable>();
    private static Step step;
    private static int waitUntilFrame;
    private static double startedAt;
    private static int stepCountBefore;
    private static int landingCountBefore;
    private static int stoppedStepCount;
    private static int runSteps;
    private static int walkSteps;
    private static int promptSwitches;
    private static int hideoutLandingCount;
    private static int dungeonRunSteps;
    private static int dungeonWalkSteps;
    private static int dungeonLandingCount;
    private static Vector3 dungeonHomePosition;
    private static Quaternion dungeonHomeRotation;
    private static Vector3 homePosition;
    private static Quaternion homeRotation;
    private static GameObject fixtureRoot;
    private static PlayerActorRuntime actor;
    private static InteractionDirector director;
    private static InteractionPromptPresenter presenter;
    private static PlayerInteractionController controller;
    private static SurfaceResolver resolver;
    private static FootstepEmitter emitter;
    private static GeneralGoodsMerchantInteractable merchant;
    private static StashInteractable stash;
    private static DungeonPortalEntry entry;
    private static Keyboard keyboard;
    private static readonly HashSet<Key> heldKeys = new HashSet<Key>();
    private static bool keyboardDirty;
    private static bool previousRunInBackground;

    static OverburstGoalDPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("OVERBURST/Codex/Validate/Interaction/Validate GOAL D Play Mode")]
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
            WaitFrames(30);
            CreateKeyboard();
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
            Debug.Log("[OverburstGoalDPlayModeVerifier] PASS " + result);
        else
            Debug.LogError("[OverburstGoalDPlayModeVerifier] FAIL " + fail);
    }

    private static void Update()
    {
        DriveKeyboard();
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
                case Step.WaitForHideout: Begin(); break;
                case Step.FixtureSelection: VerifyFixtureSelection(); break;
                case Step.SurfaceResolution: VerifySurfaces(); break;
                case Step.MerchantOpen: OpenMerchant(); break;
                case Step.MerchantClose: CloseMerchant(); break;
                case Step.StashOpen: OpenStash(); break;
                case Step.StashClose: CloseStash(); break;
                case Step.PortalSelection: VerifyPortalSelection(); break;
                case Step.RunStart: StartRun(); break;
                case Step.RunWait: WaitForRun(); break;
                case Step.RunSettle: SettleRun(); break;
                case Step.RunStop: VerifyRunStop(); break;
                case Step.WalkToggle: ToggleWalk(); break;
                case Step.WalkStart: StartWalk(); break;
                case Step.WalkWait: WaitForWalk(); break;
                case Step.WalkSettle: SettleWalk(); break;
                case Step.WalkStop: VerifyWalkStop(); break;
                case Step.LandingStart: StartLanding(); break;
                case Step.LandingWait: WaitForLanding(); break;
                case Step.DungeonEnter: EnterDungeon(); break;
                case Step.DungeonWait: WaitForDungeon(); break;
                case Step.DungeonRunToggle: ToggleDungeonRun(); break;
                case Step.DungeonRunStart: StartDungeonRun(); break;
                case Step.DungeonRunWait: WaitForDungeonRun(); break;
                case Step.DungeonRunSettle: SettleDungeonRun(); break;
                case Step.DungeonWalkToggle: ToggleDungeonWalk(); break;
                case Step.DungeonWalkStart: StartDungeonWalk(); break;
                case Step.DungeonWalkWait: WaitForDungeonWalk(); break;
                case Step.DungeonWalkSettle: SettleDungeonWalk(); break;
                case Step.DungeonLandingStart: StartDungeonLanding(); break;
                case Step.DungeonLandingWait: WaitForDungeonLanding(); break;
                case Step.DungeonReturn: ReturnFromDungeon(); break;
                case Step.WaitForHideoutReturn: WaitForHideoutReturn(); break;
                case Step.Finish: FinishPass(); break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private static void Begin()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null || flow.IsSwitching || flow.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            return;
        actor = PlayerContext.GetOrCreate()?.CurrentActor;
        if (actor == null || actor.Health == null || actor.Health.IsDead || !actor.Movement.IsGrounded)
            return;

        director = actor.GetComponent<InteractionDirector>();
        presenter = actor.GetComponent<InteractionPromptPresenter>();
        controller = actor.GetComponent<PlayerInteractionController>();
        resolver = actor.GetComponent<SurfaceResolver>();
        emitter = actor.GetComponent<FootstepEmitter>();
        Require(director != null && presenter != null && controller != null, "통합 상호작용 컴포넌트가 없다.");
        Require(resolver != null && emitter != null, "발소리 컴포넌트가 없다.");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "시작 시 입력이 차단돼 있다.");
        homePosition = actor.transform.position;
        homeRotation = actor.transform.rotation;
        fixtureRoot = new GameObject("__OVERBURST_GOAL_D_RUNTIME_ONLY__");
        step = Step.FixtureSelection;
        WaitFrames(2);
    }

    private static void VerifyFixtureSelection()
    {
        FixtureInteractable priority = AddFixture("Priority", 12000, actor.transform.position + actor.transform.right * 2f, "B");
        FixtureInteractable angle = AddFixture("Angle", 11000, actor.transform.position + actor.transform.forward * 2f, "C");
        FixtureInteractable distance = AddFixture("Distance", 11000, actor.transform.position + actor.transform.forward * 1f, "D");
        FixtureInteractable stable = AddFixture("Stable", 11000, distance.Transform.position, "A");
        Require(ReferenceEquals(director.Refresh(actor), priority), "priority 선택 실패.");

        priority.Priority = 11000;
        Require(ReferenceEquals(director.Refresh(actor), stable), "angle/distance/stable ID 선택 실패.");
        InteractionExecutionResult result = controller.ExecuteSelectedForValidation();
        Require(result == InteractionExecutionResult.Succeeded, "선택 대상 실행 실패.");
        Require(stable.ExecutionCount == 1 && fixtures.Where(item => !ReferenceEquals(item, stable)).All(item => item.ExecutionCount == 0),
            "한 입력이 둘 이상의 후보를 실행했다.");

        presenter.Present(angle);
        Require(angle.PromptVisible, "첫 프롬프트가 보이지 않는다.");
        presenter.Present(stable);
        Require(!angle.PromptVisible && stable.PromptVisible, "프롬프트가 하나로 전환되지 않았다.");
        promptSwitches++;

        GameplayInputBlocker.Block(fixtureRoot);
        Require(director.Refresh(actor) == null, "입력 차단 중 일반 후보가 선택됐다.");
        stable.AllowsBlocked = true;
        Require(ReferenceEquals(director.Refresh(actor), stable), "입력 차단 중 닫기 허용 후보가 선택되지 않았다.");
        GameplayInputBlocker.Unblock(fixtureRoot);
        stable.AllowsBlocked = false;
        presenter.Clear();
        foreach (FixtureInteractable fixture in fixtures)
            InteractionRegistry.Unregister(fixture);
        fixtures.Clear();
        step = Step.SurfaceResolution;
        WaitFrames(2);
    }

    private static void VerifySurfaces()
    {
        foreach (string id in new[] { "Concrete", "Grass", "Gravel", "Wood" })
        {
            SurfaceProfile profile = AssetDatabase.LoadAssetAtPath<SurfaceProfile>(
                OverburstGoalDInteractionFootstepMigration.ProfileFolder + "/SF_" + id + ".asset");
            Require(profile != null, id + " 프로필 누락.");
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "Surface_" + id;
            surface.transform.SetParent(fixtureRoot.transform, false);
            SurfaceOverride overrideComponent = surface.AddComponent<SurfaceOverride>();
            overrideComponent.Configure(profile);
            Require(resolver.Resolve(surface.GetComponent<Collider>(), surface.transform.position) == profile,
                id + " explicit override 판정 실패.");
        }
        step = Step.MerchantOpen;
        WaitFrames(2);
    }

    private static void OpenMerchant()
    {
        merchant = UnityEngine.Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.isActiveAndEnabled && candidate.IsInteractionAvailable(actor));
        Require(merchant != null, "Hideout 상인을 찾지 못했다.");
        ActorTeleportUtility.TeleportSafely(actor.transform, merchant.transform.position, actor.transform.rotation);
        Require(merchant.TryInteract(actor) == InteractionExecutionResult.Succeeded, "상점 열기 실패.");
        Require(GameplayInputBlocker.IsGameplayInputBlocked && merchant.AllowsInteractionWhileInputBlocked,
            "상점이 입력 차단/자기 닫기 예외를 소유하지 않는다.");
        step = Step.MerchantClose;
        WaitFrames(3);
    }

    private static void CloseMerchant()
    {
        Require(merchant.TryInteract(actor) == InteractionExecutionResult.ClosedSession, "상점 닫기 실패.");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "상점 닫기 뒤 입력 차단이 남았다.");
        step = Step.StashOpen;
        WaitFrames(3);
    }

    private static void OpenStash()
    {
        stash = UnityEngine.Object.FindObjectsByType<StashInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.isActiveAndEnabled && candidate.IsInteractionAvailable(actor));
        Require(stash != null, "Hideout 창고를 찾지 못했다.");
        ActorTeleportUtility.TeleportSafely(actor.transform, stash.transform.position, actor.transform.rotation);
        Require(stash.TryInteract(actor) == InteractionExecutionResult.Succeeded, "창고 열기 실패.");
        Require(GameplayInputBlocker.IsGameplayInputBlocked && stash.AllowsInteractionWhileInputBlocked,
            "창고가 입력 차단/자기 닫기 예외를 소유하지 않는다.");
        step = Step.StashClose;
        WaitFrames(3);
    }

    private static void CloseStash()
    {
        Require(stash.TryInteract(actor) == InteractionExecutionResult.ClosedSession, "창고 닫기 실패.");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "창고 닫기 뒤 입력 차단이 남았다.");
        step = Step.PortalSelection;
        WaitFrames(3);
    }

    private static void VerifyPortalSelection()
    {
        entry = UnityEngine.Object.FindObjectsByType<DungeonPortalEntry>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.isActiveAndEnabled);
        Require(entry != null && entry.IsInteractionAvailable(actor), "Hideout 던전 포탈 진입 계약이 준비되지 않았다.");
        ActorTeleportUtility.TeleportSafely(actor.transform, entry.transform.position, actor.transform.rotation);
        Require(ReferenceEquals(director.Refresh(actor), entry), "포탈 위치에서 DungeonPortalEntry가 선택되지 않았다.");
        ActorTeleportUtility.TeleportSafely(actor.transform, homePosition, homeRotation);
        step = Step.RunStart;
        WaitFrames(10);
    }

    private static void StartRun()
    {
        Require(actor.Movement.IsGrounded, "달리기 시작 전 플레이어가 접지되지 않았다.");
        stepCountBefore = emitter.StepEmissionCount;
        Hold(Key.W);
        step = Step.RunWait;
        WaitFrames(120);
    }

    private static void WaitForRun()
    {
        Release(Key.W);
        runSteps = emitter.StepEmissionCount - stepCountBefore;
        Require(runSteps > 0 && emitter.LastMotionKind == FootstepMotionKind.Run, "실제 달리기 발소리가 발생하지 않았다.");
        step = Step.RunSettle;
        WaitFrames(30);
    }

    private static void SettleRun()
    {
        stoppedStepCount = emitter.StepEmissionCount;
        step = Step.RunStop;
        WaitFrames(30);
    }

    private static void VerifyRunStop()
    {
        Require(emitter.StepEmissionCount == stoppedStepCount, "정지 중 발소리가 추가 재생됐다.");
        ActorTeleportUtility.TeleportSafely(actor.transform, homePosition, homeRotation);
        step = Step.WalkToggle;
        PressEdge(Key.C);
        WaitFrames(3);
    }

    private static void ToggleWalk()
    {
        Release(Key.C);
        if (!actor.Movement.IsWalkMode)
        {
            PressEdge(Key.C);
            WaitFrames(3);
            return;
        }
        step = Step.WalkStart;
        WaitFrames(3);
    }

    private static void StartWalk()
    {
        Release(Key.C);
        Require(actor.Movement.IsWalkMode, "걷기 토글이 적용되지 않았다.");
        stepCountBefore = emitter.StepEmissionCount;
        Hold(Key.W);
        step = Step.WalkWait;
        WaitFrames(160);
    }

    private static void WaitForWalk()
    {
        Release(Key.W);
        walkSteps = emitter.StepEmissionCount - stepCountBefore;
        Require(walkSteps > 0 && emitter.LastMotionKind == FootstepMotionKind.Walk, "실제 걷기 발소리가 발생하지 않았다.");
        step = Step.WalkSettle;
        WaitFrames(30);
    }

    private static void SettleWalk()
    {
        stoppedStepCount = emitter.StepEmissionCount;
        step = Step.WalkStop;
        WaitFrames(30);
    }

    private static void VerifyWalkStop()
    {
        Require(emitter.StepEmissionCount == stoppedStepCount, "걷기 정지 뒤 발소리가 추가 재생됐다.");
        step = Step.LandingStart;
        WaitFrames(2);
    }

    private static void StartLanding()
    {
        landingCountBefore = emitter.LandingEmissionCount;
        ActorTeleportUtility.TeleportSafely(actor.transform, homePosition + Vector3.up * 5f, homeRotation);
        step = Step.LandingWait;
        WaitFrames(180);
    }

    private static void WaitForLanding()
    {
        Require(actor.Movement.IsGrounded, "5m 낙하 뒤 접지되지 않았다.");
        hideoutLandingCount = emitter.LandingEmissionCount - landingCountBefore;
        Require(hideoutLandingCount > 0 && emitter.LastMotionKind == FootstepMotionKind.Land,
            "실제 착지 발소리가 발생하지 않았다.");
        ActorTeleportUtility.TeleportSafely(actor.transform, homePosition, homeRotation);
        step = Step.DungeonEnter;
        WaitFrames(10);
    }

    private static void EnterDungeon()
    {
        entry = UnityEngine.Object.FindObjectsByType<DungeonPortalEntry>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.isActiveAndEnabled);
        Require(entry != null && entry.LaunchDungeonRunWithSeed(29503), "던전 발소리 검증 진입 실패.");
        step = Step.DungeonWait;
        WaitFrames(1);
    }

    private static void WaitForDungeon()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null || flow.IsSwitching || flow.CurrentSubSceneName != PersistentSceneFlow.DungeonRunSceneName)
            return;
        DungeonRunFlow run = UnityEngine.Object.FindFirstObjectByType<DungeonRunFlow>();
        actor = PlayerContext.GetOrCreate()?.CurrentActor;
        if (run == null || actor == null || actor.Movement == null || !actor.Movement.IsGrounded)
            return;
        emitter = actor.GetComponent<FootstepEmitter>();
        resolver = actor.GetComponent<SurfaceResolver>();
        Require(emitter != null && resolver != null, "DungeonRun에서 발소리 계층이 유실됐다.");
        dungeonHomePosition = actor.transform.position;
        dungeonHomeRotation = actor.transform.rotation;
        step = Step.DungeonRunToggle;
        WaitFrames(5);
    }

    private static void ToggleDungeonRun()
    {
        if (actor.Movement.IsWalkMode)
        {
            PressEdge(Key.C);
            WaitFrames(3);
            return;
        }
        Release(Key.C);
        step = Step.DungeonRunStart;
        WaitFrames(3);
    }

    private static void StartDungeonRun()
    {
        Release(Key.C);
        Require(!actor.Movement.IsWalkMode, "DungeonRun 달리기 모드 전환 실패.");
        stepCountBefore = emitter.StepEmissionCount;
        Hold(Key.W);
        step = Step.DungeonRunWait;
        WaitFrames(120);
    }

    private static void WaitForDungeonRun()
    {
        Release(Key.W);
        dungeonRunSteps = emitter.StepEmissionCount - stepCountBefore;
        Require(dungeonRunSteps > 0 && emitter.LastMotionKind == FootstepMotionKind.Run,
            "DungeonRun 실제 달리기 발소리가 발생하지 않았다.");
        step = Step.DungeonRunSettle;
        WaitFrames(30);
    }

    private static void SettleDungeonRun()
    {
        stoppedStepCount = emitter.StepEmissionCount;
        step = Step.DungeonWalkToggle;
        ActorTeleportUtility.TeleportSafely(actor.transform, dungeonHomePosition, dungeonHomeRotation);
        PressEdge(Key.C);
        WaitFrames(3);
    }

    private static void ToggleDungeonWalk()
    {
        Release(Key.C);
        if (!actor.Movement.IsWalkMode)
        {
            PressEdge(Key.C);
            WaitFrames(3);
            return;
        }
        step = Step.DungeonWalkStart;
        WaitFrames(3);
    }

    private static void StartDungeonWalk()
    {
        Release(Key.C);
        Require(actor.Movement.IsWalkMode, "DungeonRun 걷기 모드 전환 실패.");
        stepCountBefore = emitter.StepEmissionCount;
        Hold(Key.W);
        step = Step.DungeonWalkWait;
        WaitFrames(160);
    }

    private static void WaitForDungeonWalk()
    {
        Release(Key.W);
        dungeonWalkSteps = emitter.StepEmissionCount - stepCountBefore;
        Require(dungeonWalkSteps > 0 && emitter.LastMotionKind == FootstepMotionKind.Walk,
            "DungeonRun 실제 걷기 발소리가 발생하지 않았다.");
        step = Step.DungeonWalkSettle;
        WaitFrames(30);
    }

    private static void SettleDungeonWalk()
    {
        stoppedStepCount = emitter.StepEmissionCount;
        step = Step.DungeonLandingStart;
        WaitFrames(30);
    }

    private static void StartDungeonLanding()
    {
        Require(emitter.StepEmissionCount == stoppedStepCount, "DungeonRun 정지 중 발소리가 추가 재생됐다.");
        landingCountBefore = emitter.LandingEmissionCount;
        ActorTeleportUtility.TeleportSafely(actor.transform, dungeonHomePosition + Vector3.up * 5f, dungeonHomeRotation);
        step = Step.DungeonLandingWait;
        WaitFrames(180);
    }

    private static void WaitForDungeonLanding()
    {
        Require(actor.Movement.IsGrounded, "DungeonRun 5m 낙하 뒤 접지되지 않았다.");
        dungeonLandingCount = emitter.LandingEmissionCount - landingCountBefore;
        Require(dungeonLandingCount > 0 && emitter.LastMotionKind == FootstepMotionKind.Land,
            "DungeonRun 실제 착지 발소리가 발생하지 않았다.");
        step = Step.DungeonReturn;
        WaitFrames(5);
    }

    private static void ReturnFromDungeon()
    {
        DungeonRunFlow run = UnityEngine.Object.FindFirstObjectByType<DungeonRunFlow>();
        Require(run != null && run.ActiveExitPortal != null && run.ActiveExitPortal.RequestExtract(),
            "DungeonRun 발소리 검증 후 복귀 실패.");
        step = Step.WaitForHideoutReturn;
        WaitFrames(1);
    }

    private static void WaitForHideoutReturn()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null || flow.IsSwitching || flow.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            return;
        actor = PlayerContext.GetOrCreate()?.CurrentActor;
        Require(actor != null && actor.GetComponent<FootstepEmitter>() != null, "Hideout 복귀 뒤 플레이어/발소리 계층 유실.");
        step = Step.Finish;
        WaitFrames(10);
    }

    private static void FinishPass()
    {
        Require(runtimeErrors.Count == 0, "검증 중 Error/Exception/Assert: " + (runtimeErrors.Count > 0 ? runtimeErrors[0] : string.Empty));
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, "종료 시 입력 차단이 남았다.");
        SessionState.SetString(ResultKey,
            "selection=priority/angle/distance/stable, oneInput=1, promptSwitch=" + promptSwitches
            + ", merchant=open/close, stash=open/close, portal=selected"
            + ", runSteps=" + runSteps + ", walkSteps=" + walkSteps
            + ", landing=" + hideoutLandingCount
            + ", dungeonRunSteps=" + dungeonRunSteps + ", dungeonWalkSteps=" + dungeonWalkSteps
            + ", dungeonLanding=" + dungeonLandingCount
            + ", stoppedExtra=0, dungeonReturn=1, errors=0");
        Finish();
    }

    private static FixtureInteractable AddFixture(string name, int priority, Vector3 position, string stableId)
    {
        GameObject owner = new GameObject(name);
        owner.transform.SetParent(fixtureRoot.transform, true);
        owner.transform.position = position;
        FixtureInteractable fixture = new FixtureInteractable(owner.transform, priority, stableId);
        fixtures.Add(fixture);
        InteractionRegistry.Register(fixture);
        return fixture;
    }

    private static void CreateKeyboard()
    {
        DestroyKeyboard();
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        InputSystem.onBeforeUpdate -= HandleBeforeInputUpdate;
        InputSystem.onBeforeUpdate += HandleBeforeInputUpdate;
    }

    private static void DestroyKeyboard()
    {
        InputSystem.onBeforeUpdate -= HandleBeforeInputUpdate;
        if (keyboard != null && keyboard.added)
            InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        heldKeys.Clear();
        keyboardDirty = false;
    }

    private static void HandleBeforeInputUpdate() => DriveKeyboard();

    private static void DriveKeyboard()
    {
        if (!keyboardDirty || keyboard == null)
            return;
        Key[] keys = heldKeys.ToArray();
        InputState.Change(keyboard, new KeyboardState(keys), InputUpdateType.Dynamic);
        keyboardDirty = false;
    }

    private static void Hold(Key key)
    {
        if (heldKeys.Add(key))
            keyboardDirty = true;
    }

    private static void Release(Key key)
    {
        if (heldKeys.Remove(key))
            keyboardDirty = true;
    }

    private static void PressEdge(Key key)
    {
        heldKeys.Add(key);
        keyboardDirty = true;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying)
            return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeErrors.Add(condition);
    }

    private static void Cleanup()
    {
        foreach (FixtureInteractable fixture in fixtures)
            InteractionRegistry.Unregister(fixture);
        fixtures.Clear();
        presenter?.Clear();
        GameplayInputBlocker.Unblock(fixtureRoot);
        if (actor != null)
            ActorTeleportUtility.TeleportSafely(actor.transform, homePosition, homeRotation);
        if (fixtureRoot != null)
            UnityEngine.Object.DestroyImmediate(fixtureRoot);
        fixtureRoot = null;
        DestroyKeyboard();
    }

    private static void Fail(string message)
    {
        SessionState.SetString(FailKey, "step=" + step + " " + message);
        Finish();
    }

    private static void Finish()
    {
        heldKeys.Clear();
        keyboardDirty = true;
        DriveKeyboard();
        EditorApplication.ExitPlaymode();
    }

    private static void WaitFrames(int frames) => waitUntilFrame = Time.frameCount + Math.Max(1, frames);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FixtureInteractable : IInteractable
    {
        public FixtureInteractable(Transform owner, int priority, string stableId)
        {
            Transform = owner;
            Priority = priority;
            StableId = stableId;
        }

        public Transform Transform { get; }
        public int Priority { get; set; }
        public string StableId { get; }
        public bool Available { get; set; } = true;
        public bool AllowsBlocked { get; set; }
        public bool PromptVisible { get; private set; }
        public int ExecutionCount { get; private set; }
        public Component InteractionComponent => Transform;
        public Transform InteractionTransform => Transform;
        public int InteractionPriority => Priority;
        public string InteractionPrompt => "F : Fixture";
        public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
        public float InteractionRange => 20f;
        public string StableInteractionId => StableId;
        public bool AllowsInteractionWhileInputBlocked => AllowsBlocked;
        public bool WantsInteractionPrompt => true;
        public bool IsInteractionAvailable(PlayerActorRuntime actor) => Available;
        public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
        {
            ExecutionCount++;
            return InteractionExecutionResult.Succeeded;
        }
        public void SetInteractionPromptVisible(bool visible) => PromptVisible = visible;
    }
}
