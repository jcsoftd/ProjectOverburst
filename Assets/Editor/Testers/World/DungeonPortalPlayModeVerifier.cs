using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class DungeonPortalPlayModeVerifier
{
    private const string ActiveKey =
        "DungeonPortalPlayModeVerifier.Active";
    private const string BatchKey =
        "DungeonPortalPlayModeVerifier.Batch";
    private const string ExitCodeKey =
        "DungeonPortalPlayModeVerifier.ExitCode";
    private const string PersistentScenePath =
        "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const int ExtractSeed = 29501;
    private const int QuickExtractSeed = 29502;
    private const float TimeoutSeconds = 75f;

    private enum VerifyStep
    {
        WaitForHideout,
        WaitForDungeonExtract,
        WaitForHideoutAfterExtract,
        WaitForDungeonQuickExtract,
        WaitForHideoutAfterQuickExtract
    }

    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float timeoutAt;
    private static bool previousRunInBackground;

    static DungeonPortalPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -=
            HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged +=
            HandlePlayModeStateChanged;
    }

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Verify Hideout Portal PlayMode")]
    public static void RunFromMenu()
    {
        Begin(false);
    }

    public static void RunOnceFromCommandLine()
    {
        Begin(true);
    }

    private static void Begin(bool batchMode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "PlayMode is already active or changing.");
        }

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        EditorSceneManager.OpenScene(
            PersistentScenePath,
            OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
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
            step = VerifyStep.WaitForHideout;
            Wait(30);
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Application.runInBackground = previousRunInBackground;
            EditorApplication.update -= UpdateVerification;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        int exitCode =
            SessionState.GetInt(ExitCodeKey, 1);
        bool batchMode =
            SessionState.GetBool(BatchKey, false);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(BatchKey);
        SessionState.EraseInt(ExitCodeKey);

        if (batchMode)
            EditorApplication.Exit(exitCode);
        else if (exitCode == 0)
        {
            Debug.Log(
                "[DungeonPortalPlayModeVerifier] "
                + "Verification completed.");
        }
        else
        {
            Debug.LogError(
                "[DungeonPortalPlayModeVerifier] "
                + "Verification failed.");
        }
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying
            || Time.frameCount < waitUntilFrame)
        {
            return;
        }

        try
        {
            if (Time.realtimeSinceStartup > timeoutAt)
            {
                throw new TimeoutException(
                    "Dungeon portal verification timed out at "
                    + step);
            }

            PersistentSceneFlow flow =
                PersistentSceneFlow.Instance;
            if (flow == null)
                return;

            switch (step)
            {
                case VerifyStep.WaitForHideout:
                    if (!IsReady(
                            flow,
                            PersistentSceneFlow.HideoutSceneName))
                    {
                        return;
                    }

                    VerifySingleCameraAndListener();
                    PreparePlayerAtDungeonPortal();
                    Require(
                        FindHideoutEntry()
                            .LaunchDungeonRunWithSeed(ExtractSeed),
                        "Hideout dungeon portal launch failed.");
                    step = VerifyStep.WaitForDungeonExtract;
                    Wait(1);
                    break;

                case VerifyStep.WaitForDungeonExtract:
                    if (!IsReady(
                            flow,
                            PersistentSceneFlow.DungeonRunSceneName))
                    {
                        return;
                    }

                    VerifySingleCameraAndListener();
                    DungeonRunFlow extractRun =
                        VerifyDungeonRun(ExtractSeed);
                    Require(
                        extractRun.ActiveExitPortal.RequestExtract(),
                        "Dungeon exit portal extract request failed.");
                    step =
                        VerifyStep.WaitForHideoutAfterExtract;
                    Wait(1);
                    break;

                case VerifyStep.WaitForHideoutAfterExtract:
                    if (!IsReady(
                            flow,
                            PersistentSceneFlow.HideoutSceneName))
                    {
                        return;
                    }

                    VerifyHideoutReturn();
                    Require(
                        FindHideoutEntry()
                            .LaunchDungeonRunWithSeed(
                                QuickExtractSeed),
                        "Second dungeon portal launch failed.");
                    step =
                        VerifyStep.WaitForDungeonQuickExtract;
                    Wait(1);
                    break;

                case VerifyStep.WaitForDungeonQuickExtract:
                    if (!IsReady(
                            flow,
                            PersistentSceneFlow.DungeonRunSceneName))
                    {
                        return;
                    }

                    VerifySingleCameraAndListener();
                    DungeonRunFlow quickRun =
                        VerifyDungeonRun(QuickExtractSeed);
                    Require(
                        quickRun.DebugReturnInput != null
                        && quickRun.DebugReturnInput
                            .RequestQuickExtract(),
                        "Dungeon F8 quick extract request failed.");
                    step =
                        VerifyStep.WaitForHideoutAfterQuickExtract;
                    Wait(1);
                    break;

                case VerifyStep.WaitForHideoutAfterQuickExtract:
                    if (!IsReady(
                            flow,
                            PersistentSceneFlow.HideoutSceneName))
                    {
                        return;
                    }

                    VerifyHideoutReturn();
                    VerifySingleCameraAndListener();
                    Require(FindHideoutEntry() != null, "Dungeon portal was lost after repeated return.");
                    Debug.Log(
                        "[DungeonPortalPlayModeVerifier] PASS "
                        + "entry=1 extract=1 f8=1 "
                        + "returnPoint=1 singlePlayer=1 "
                        + "camera=1 listener=1 "
                        + "repeatedHideoutReturn=1");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void PreparePlayerAtDungeonPortal()
    {
        PlayerActorRuntime actor = PlayerContext.GetOrCreate()?.CurrentActor;
        Require(actor != null, "Player was not ready in Hideout.");
        Require(UnityEngine.Object.FindObjectsByType<PlayerActorRuntime>(FindObjectsSortMode.None).Length == 1,
            "Expected exactly one gameplay actor.");
        HubReturnPoint point = FindDungeonReturnPoint();
        ActorTeleportUtility.TeleportSafely(actor.transform, point.transform.position, point.transform.rotation);
        Require(FindHideoutEntry().IsPlayerInRange(), "Player is outside the dungeon portal range.");
    }

    private static DungeonRunFlow VerifyDungeonRun(int seed)
    {
        DungeonRunFlow runFlow =
            UnityEngine.Object
                .FindFirstObjectByType<DungeonRunFlow>();
        Require(runFlow != null,
            "DungeonRunFlow was not found.");
        Require(runFlow.ActiveSeed == seed,
            $"Dungeon seed mismatch: {runFlow.ActiveSeed}/{seed}");
        Require(
            runFlow.ReturnContext != null
            && runFlow.ReturnContext.TargetSceneName
                == PersistentSceneFlow.HideoutSceneName
            && runFlow.ReturnContext.ReturnPointId
                == DungeonPortalEntry.DefaultReturnPointId,
            "Dungeon portal return context was not preserved.");
        Require(
            runFlow.ActiveExitPortal != null
            && runFlow.ActiveExitPortal.RunFlow == runFlow,
            "Runtime dungeon exit portal was not bound.");
        Require(
            runFlow.RuntimeDungeon != null
            && runFlow.RuntimeDungeon.Generator != null
            && runFlow.RuntimeDungeon.Generator.CurrentDungeon
                != null,
            "Runtime dungeon generation result is missing.");

        DunGen.Dungeon dungeon =
            runFlow.RuntimeDungeon.Generator.CurrentDungeon;
        DunGen.Tile exitTile =
            dungeon.MainPathTiles[
                dungeon.MainPathTiles.Count - 1];
        Require(
            runFlow.ActiveExitPortal.transform
                .IsChildOf(exitTile.transform),
            "Runtime exit portal is not in the last main-path tile.");
        return runFlow;
    }

    private static void VerifyHideoutReturn()
    {
        VerifySingleCameraAndListener();
        Require(
            !SceneManager.GetSceneByName(
                PersistentSceneFlow.DungeonRunSceneName)
                .isLoaded,
            "Dungeon scene remained loaded after return.");
        PlayerActorRuntime actor = PlayerContext.GetOrCreate()?.CurrentActor;
        Require(actor != null && actor.ActorIndex == 0, "Single player was not preserved.");
        Require(UnityEngine.Object.FindObjectsByType<PlayerActorRuntime>(FindObjectsSortMode.None).Length == 1,
            "Companion actor appeared after return.");
        HubReturnPoint point = FindDungeonReturnPoint();
        Require(Vector3.Distance(actor.transform.position, point.transform.position) <= 0.9f,
            "Player missed the DungeonPortal return point.");

        Require(
            FindHideoutEntry().IsPlayerInRange(),
            "Returned leader is outside the dungeon portal range.");
    }

    private static DungeonPortalEntry FindHideoutEntry()
    {
        DungeonPortalEntry[] entries =
            UnityEngine.Object.FindObjectsByType<DungeonPortalEntry>(
                FindObjectsSortMode.None);
        for (int i = 0; i < entries.Length; i++)
        {
            DungeonPortalEntry entry = entries[i];
            if (entry != null
                && entry.gameObject.scene.name
                    == PersistentSceneFlow.HideoutSceneName)
            {
                return entry;
            }
        }

        throw new InvalidOperationException(
            "Hideout DungeonPortalEntry was not found.");
    }

    private static HubReturnPoint FindDungeonReturnPoint()
    {
        HubReturnPoint[] points =
            UnityEngine.Object.FindObjectsByType<HubReturnPoint>(
                FindObjectsSortMode.None);
        for (int i = 0; i < points.Length; i++)
        {
            HubReturnPoint point = points[i];
            if (point != null
                && point.gameObject.scene.name
                    == PersistentSceneFlow.HideoutSceneName
                && point.ReturnPointId
                    == DungeonPortalEntry.DefaultReturnPointId)
            {
                return point;
            }
        }

        throw new InvalidOperationException(
            "DungeonPortal HubReturnPoint was not found.");
    }

    private static void VerifySingleCameraAndListener()
    {
        Camera[] cameras =
            UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsSortMode.None);
        int activeMainCameras = 0;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null
                && camera.enabled
                && camera.gameObject.activeInHierarchy
                && camera.CompareTag("MainCamera"))
            {
                activeMainCameras++;
            }
        }

        AudioListener[] listeners =
            UnityEngine.Object.FindObjectsByType<AudioListener>(
                FindObjectsSortMode.None);
        int activeListeners = 0;
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener != null
                && listener.enabled
                && listener.gameObject.activeInHierarchy)
            {
                activeListeners++;
            }
        }

        Require(activeMainCameras == 1,
            $"Active Main Camera count={activeMainCameras}");
        Require(activeListeners == 1,
            $"Active AudioListener count={activeListeners}");
    }

    private static bool IsReady(
        PersistentSceneFlow flow,
        string sceneName)
    {
        return !flow.IsSwitching
            && flow.CurrentSubSceneName == sceneName;
    }

    private static void Wait(int frameDelay)
    {
        waitUntilFrame = Time.frameCount + frameDelay;
        timeoutAt = Time.realtimeSinceStartup + TimeoutSeconds;
    }

    private static void Finish(int exitCode)
    {
        SessionState.SetInt(ExitCodeKey, exitCode);
        EditorApplication.update -= UpdateVerification;
        EditorApplication.ExitPlaymode();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
