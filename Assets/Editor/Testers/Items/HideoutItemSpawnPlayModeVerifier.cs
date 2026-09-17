using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HideoutItemSpawnPlayModeVerifier
{
    private const string ActiveKey = "HideoutItemSpawnPlayModeVerifier.Active";
    private const string BatchKey = "HideoutItemSpawnPlayModeVerifier.Batch";
    private const string ExitCodeKey = "HideoutItemSpawnPlayModeVerifier.ExitCode";
    private const string PersistentScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const int ExpectedWeaponCount = 20;
    private const int ExpectedComboGemCount = 30;
    private const int ExpectedFirstVisitBagCount = 7;
    private const float MaximumLeaderDistance = 25f;

    private enum VerifyStep
    {
        WaitForFirstHideout,
        CheckFirstHideout,
        WaitForDungeon,
        WaitForSecondHideout,
        CheckSecondHideout
    }

    private static readonly List<WorldItemPickup> pickups = new();
    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float timeoutAt;

    static HideoutItemSpawnPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Hideout Item Spawn PlayMode")]
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
            throw new InvalidOperationException("PlayMode is already active or changing.");

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            step = VerifyStep.WaitForFirstHideout;
            Wait(30, 20f);
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= UpdateVerification;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        bool batchMode = SessionState.GetBool(BatchKey, false);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(BatchKey);
        SessionState.EraseInt(ExitCodeKey);

        if (batchMode)
            EditorApplication.Exit(exitCode);
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying || Time.frameCount < waitUntilFrame)
            return;

        try
        {
            if (Time.realtimeSinceStartup > timeoutAt)
                throw new TimeoutException("Hideout item spawn verification timed out at " + step + ".");

            PersistentSceneFlow flow = PersistentSceneFlow.Instance;
            if (flow == null)
                return;

            switch (step)
            {
                case VerifyStep.WaitForFirstHideout:
                    if (!IsReady(flow, PersistentSceneFlow.HideoutSceneName))
                        return;

                    step = VerifyStep.CheckFirstHideout;
                    Wait(3, 5f);
                    break;

                case VerifyStep.CheckFirstHideout:
                    VerifyHideoutPickups(ExpectedFirstVisitBagCount);
                    flow.EnterDungeon(DungeonRunEntryRequest.Create(29503, PersistentSceneFlow.HideoutSceneName));
                    step = VerifyStep.WaitForDungeon;
                    Wait(1, 75f);
                    break;

                case VerifyStep.WaitForDungeon:
                    if (!IsReady(flow, PersistentSceneFlow.DungeonRunSceneName))
                        return;

                    VerifyHideoutPickupsWereUnloaded();
                    flow.SwitchHubScene(PersistentSceneFlow.HideoutSceneName);
                    step = VerifyStep.WaitForSecondHideout;
                    Wait(1, 75f);
                    break;

                case VerifyStep.WaitForSecondHideout:
                    if (!IsReady(flow, PersistentSceneFlow.HideoutSceneName))
                        return;

                    step = VerifyStep.CheckSecondHideout;
                    Wait(3, 5f);
                    break;

                case VerifyStep.CheckSecondHideout:
                    VerifyHideoutPickups(0);
                    Debug.Log("[HideoutItemSpawnPlayModeVerifier] PASS weapons=20 comboGems=30 bagsFirstVisit=7 sceneOwned=1 settled=1 reentry=1");
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

    private static bool IsReady(PersistentSceneFlow flow, string sceneName)
    {
        return !flow.IsSwitching && flow.CurrentSubSceneName == sceneName;
    }

    private static void VerifyHideoutPickups(int expectedBagCount)
    {
        WorldItemPickup.CopyActivePickups(pickups);
        Transform leader = PlayerContext.GetOrCreate() != null && PlayerContext.GetOrCreate().CurrentActor != null
            ? PlayerContext.GetOrCreate().CurrentActor.transform
            : null;
        Require(leader != null, "Player is missing in Hideout.");

        int weaponCount = 0;
        int comboGemCount = 0;
        int bagCount = 0;
        for (int i = 0; i < pickups.Count; i++)
        {
            WorldItemPickup pickup = pickups[i];
            if (pickup == null || pickup.RuntimeItem == null)
                continue;

            BaseItemData baseData = pickup.RuntimeItem.baseData;
            bool isAuthoredHideoutPickup = baseData is WeaponItemData
                || baseData is ElementComboGemItemData
                || baseData is BagItemData;
            if (!isAuthoredHideoutPickup)
                continue;

            if (baseData is WeaponItemData)
                weaponCount++;
            else if (baseData is ElementComboGemItemData)
                comboGemCount++;
            else
                bagCount++;

            Require(pickup.gameObject.scene.name == PersistentSceneFlow.HideoutSceneName,
                "Pickup is not owned by HideoutScene: " + pickup.name);
            Require(Vector3.Distance(pickup.transform.position, leader.position) <= MaximumLeaderDistance,
                "Pickup is outside the Hideout authored area: " + pickup.name);

            WorldItemDropMotion motion = pickup.GetComponent<WorldItemDropMotion>();
            Require(motion == null || motion.IsLanded,
                "Pickup is still running drop motion: " + pickup.name);
        }

        Require(weaponCount == ExpectedWeaponCount,
            "Hideout weapon pickup count mismatch: " + weaponCount + "/" + ExpectedWeaponCount);
        Require(comboGemCount == ExpectedComboGemCount,
            "Hideout combo gem count mismatch: " + comboGemCount + "/" + ExpectedComboGemCount);
        Require(bagCount == expectedBagCount,
            "Hideout bag pickup count mismatch: " + bagCount + "/" + expectedBagCount);
    }

    private static void VerifyHideoutPickupsWereUnloaded()
    {
        WorldItemPickup.CopyActivePickups(pickups);
        for (int i = 0; i < pickups.Count; i++)
        {
            WorldItemPickup pickup = pickups[i];
            if (pickup != null && pickup.gameObject.scene.name == PersistentSceneFlow.HideoutSceneName)
                throw new InvalidOperationException("Hideout pickup survived scene unload: " + pickup.name);
        }
    }

    private static void Wait(int frameDelay, float timeoutSeconds)
    {
        waitUntilFrame = Time.frameCount + frameDelay;
        timeoutAt = Time.realtimeSinceStartup + timeoutSeconds;
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
