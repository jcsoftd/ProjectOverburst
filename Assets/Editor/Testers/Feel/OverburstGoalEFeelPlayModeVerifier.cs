using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class OverburstGoalEFeelPlayModeVerifier
{
    private const string ActiveKey = "OverburstGoalEFeelPlayModeVerifier.Active";
    private const string ResultKey = "OverburstGoalEFeelPlayModeVerifier.Result";
    private const string FailKey = "OverburstGoalEFeelPlayModeVerifier.Fail";
    private const string PersistentScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const float TimeoutSeconds = 90f;

    private enum Step
    {
        Bootstrap,
        Presets,
        CombatDedupe,
        Stress10,
        Stress41,
        TimeHitStop,
        TimePerfectEvade,
        TimeResumeHitStop,
        TimeRestore,
        Cleanup,
        Finish
    }

    private static readonly List<string> runtimeErrors = new List<string>();
    private static Step step;
    private static int waitUntilFrame;
    private static double startedAt;
    private static double stageStartedAt;
    private static bool previousRunInBackground;
    private static GameObject fixtureRoot;
    private static CombatHitFeedbackProfile profile;
    private static OverburstFeelFeedbackHub hub;
    private static int emitterCount;
    private static float baselineTimeScale;
    private static float baselineFixedDeltaTime;

    static OverburstGoalEFeelPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("OVERBURST/Codex/Validate/Feel/Validate GOAL E Play Mode")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play Mode가 이미 실행 중이다.");
        EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
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
            step = Step.Bootstrap;
            WaitFrames(30);
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            CleanupRuntime();
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
            Debug.Log("[OverburstGoalEFeelPlayModeVerifier] PASS " + result);
        else
            Debug.LogError("[OverburstGoalEFeelPlayModeVerifier] FAIL " + fail);
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
                case Step.Bootstrap: BootstrapAndWarm(); break;
                case Step.Presets: VerifyPresets(); break;
                case Step.CombatDedupe: VerifyCombatDedupe(); break;
                case Step.Stress10: VerifyStress10(); break;
                case Step.Stress41: VerifyStress41(); break;
                case Step.TimeHitStop: BeginTimeHitStop(); break;
                case Step.TimePerfectEvade: BeginPerfectEvade(); break;
                case Step.TimeResumeHitStop: VerifyHitStopResume(); break;
                case Step.TimeRestore: VerifyTimeRestore(); break;
                case Step.Cleanup: VerifyCleanup(); break;
                case Step.Finish: FinishPass(); break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private static void BootstrapAndWarm()
    {
        fixtureRoot = new GameObject("GOAL_E_Fixture");
        profile = ScriptableObject.CreateInstance<CombatHitFeedbackProfile>();
        Require(OverburstFeelFeedbackHub.Request(OverburstFeelCue.WeakHit, Vector3.zero), "hub bootstrap failed");
        hub = OverburstFeelFeedbackHub.Instance;
        Require(hub != null, "hub instance missing");
        Require(OverburstFeelFeedbackHub.InstanceCount == 1, "hub must be singleton");
        emitterCount = UnityEngine.Object.FindObjectsByType<OverburstFeelEmitter>(FindObjectsSortMode.None).Length;
        Require(emitterCount == 23, "runtime emitter count expected 23 actual=" + emitterCount);
        OverburstFeelFeedbackHub.StopAllActive();
        hub.ResetCountersForValidation();

        foreach (OverburstFeelCue cue in Enum.GetValues(typeof(OverburstFeelCue)))
            Require(OverburstFeelFeedbackHub.Request(cue, Vector3.zero), "preset request failed " + cue);
        step = Step.Presets;
        WaitFrames(3);
    }

    private static void VerifyPresets()
    {
        foreach (OverburstFeelCue cue in Enum.GetValues(typeof(OverburstFeelCue)))
            Require(hub.GetPlayCount(cue) == 1, cue + " preset did not play exactly once");
        Require(hub.TotalPlayCount == 6, "six representative presets expected");
        OverburstFeelFeedbackHub.StopAllActive();
        hub.ResetCountersForValidation();
        step = Step.CombatDedupe;
        WaitFrames(2);
    }

    private static void VerifyCombatDedupe()
    {
        CombatHitFeedbackRequest normal = CreateRequest(1001, false, false);
        CombatHitFeedbackService.Request(normal);
        CombatHitFeedbackService.Request(normal);
        CombatHitFeedbackService.Request(CreateRequest(1001, true, false));
        CombatHitFeedbackService.Request(CreateRequest(1001, false, true));
        Require(hub.GetPlayCount(OverburstFeelCue.WeakHit) == 1, "same attack weak hit was not deduped");
        Require(hub.GetPlayCount(OverburstFeelCue.StrongHit) == 1, "critical upgrade did not route once");
        Require(hub.GetPlayCount(OverburstFeelCue.Death) == 1, "same attack lethal upgrade did not route once");
        Require(hub.TotalPlayCount == 3, "combat approved playback count expected 3 actual=" + hub.TotalPlayCount);
        OverburstTimeEffectArbiter.ClearAll();
        OverburstFeelFeedbackHub.StopAllActive();
        hub.ResetCountersForValidation();
        for (int i = 0; i < 10; i++)
            OverburstFeelFeedbackHub.Request(OverburstFeelCue.WeakHit, new Vector3(i * 0.1f, 0f, 0f));
        step = Step.Stress10;
        WaitFrames(2);
    }

    private static void VerifyStress10()
    {
        Require(hub.GetPlayCount(OverburstFeelCue.WeakHit) == 10, "10-target sample count mismatch");
        Require(hub.GetPoolSize(OverburstFeelCue.WeakHit) == 8, "weak pool size changed");
        Require(UnityEngine.Object.FindObjectsByType<OverburstFeelEmitter>(FindObjectsSortMode.None).Length == emitterCount,
            "10-target sample instantiated emitters");
        OverburstFeelFeedbackHub.StopAllActive();
        hub.ResetCountersForValidation();
        for (int i = 0; i < 41; i++)
            OverburstFeelFeedbackHub.Request(OverburstFeelCue.StrongHit, new Vector3(i % 9, 0f, i / 9));
        step = Step.Stress41;
        WaitFrames(2);
    }

    private static void VerifyStress41()
    {
        Require(hub.GetPlayCount(OverburstFeelCue.StrongHit) == 41, "41-target sample count mismatch");
        Require(hub.GetPoolSize(OverburstFeelCue.StrongHit) == 6, "strong pool size changed");
        Require(UnityEngine.Object.FindObjectsByType<OverburstFeelEmitter>(FindObjectsSortMode.None).Length == emitterCount,
            "41-target sample instantiated emitters");
        Require(OverburstFeelFeedbackHub.InstanceCount == 1, "stress sample duplicated hub");
        OverburstFeelFeedbackHub.StopAllActive();
        baselineTimeScale = Time.timeScale;
        baselineFixedDeltaTime = Time.fixedDeltaTime;
        step = Step.TimeHitStop;
        WaitFrames(2);
    }

    private static void BeginTimeHitStop()
    {
        Require(OverburstTimeEffectArbiter.Request(fixtureRoot, OverburstTimeEffectKind.HitStop, 0.6f, 0.32f),
            "hitstop request rejected");
        Require(Mathf.Abs(Time.timeScale - 0.6f) < 0.001f, "hitstop scale mismatch");
        Require(OverburstTimeEffectArbiter.ActiveKind == OverburstTimeEffectKind.HitStop, "hitstop ownership mismatch");
        stageStartedAt = EditorApplication.timeSinceStartup;
        step = Step.TimePerfectEvade;
        WaitFrames(2);
    }

    private static void BeginPerfectEvade()
    {
        Require(OverburstTimeEffectArbiter.Request(fixtureRoot, OverburstTimeEffectKind.PerfectEvade, 0.4f, 0.12f),
            "perfect evade request rejected");
        Require(Mathf.Abs(Time.timeScale - 0.4f) < 0.001f, "perfect evade did not override hitstop");
        Require(OverburstTimeEffectArbiter.ActiveKind == OverburstTimeEffectKind.PerfectEvade,
            "perfect evade priority mismatch");
        stageStartedAt = EditorApplication.timeSinceStartup;
        step = Step.TimeResumeHitStop;
        WaitFrames(2);
    }

    private static void VerifyHitStopResume()
    {
        if (EditorApplication.timeSinceStartup - stageStartedAt < 0.16d)
        {
            WaitFrames(1);
            return;
        }
        Require(OverburstTimeEffectArbiter.ActiveKind == OverburstTimeEffectKind.HitStop,
            "hitstop did not resume after perfect evade");
        Require(Mathf.Abs(Time.timeScale - 0.6f) < 0.001f, "resumed hitstop scale mismatch");
        stageStartedAt = EditorApplication.timeSinceStartup;
        step = Step.TimeRestore;
        WaitFrames(2);
    }

    private static void VerifyTimeRestore()
    {
        if (EditorApplication.timeSinceStartup - stageStartedAt < 0.22d)
        {
            WaitFrames(1);
            return;
        }
        Require(!OverburstTimeEffectArbiter.IsActive, "time requests remained active");
        Require(Mathf.Abs(Time.timeScale - baselineTimeScale) < 0.001f, "time scale was not restored");
        Require(Mathf.Abs(Time.fixedDeltaTime - baselineFixedDeltaTime) < 0.0001f, "fixed delta was not restored");
        step = Step.Cleanup;
        WaitFrames(2);
    }

    private static void VerifyCleanup()
    {
        OverburstFeelFeedbackHub.Request(OverburstFeelCue.Death, Vector3.zero);
        hub.enabled = false;
        Require(hub.CountLiveParticles() == 0, "disable left particles active");
        hub.enabled = true;
        OverburstTimeEffectArbiter.Request(fixtureRoot, OverburstTimeEffectKind.HitStop, 0.5f, 1f);
        OverburstTimeEffectArbiter.ClearAll();
        Require(!OverburstTimeEffectArbiter.IsActive, "ClearAll left time request active");
        Require(Mathf.Abs(Time.timeScale - baselineTimeScale) < 0.001f, "ClearAll did not restore baseline");
        step = Step.Finish;
        WaitFrames(2);
    }

    private static CombatHitFeedbackRequest CreateRequest(int sequenceId, bool critical, bool lethal)
    {
        return new CombatHitFeedbackRequest(
            fixtureRoot,
            sequenceId,
            profile,
            critical,
            default,
            new Vector3(sequenceId * 0.001f, 0f, 0f),
            true,
            isLethal: lethal);
    }

    private static void FinishPass()
    {
        Require(runtimeErrors.Count == 0, "managed errors=" + string.Join(" | ", runtimeErrors));
        SessionState.SetString(ResultKey,
            "presets=6 combatApproved=3 dedupe=1 criticalUpgrade=1 lethal=1"
            + " sample10=10/8pool sample41=41/6pool instantiatedDuringHits=0"
            + " time=hitstop>perfectEvade>hitstop>restore cleanup=1 errors=0");
        EditorApplication.ExitPlaymode();
    }

    private static void CleanupRuntime()
    {
        OverburstFeelFeedbackHub.StopAllActive();
        OverburstTimeEffectArbiter.ClearAll();
        if (profile != null)
            UnityEngine.Object.DestroyImmediate(profile);
        if (fixtureRoot != null)
            UnityEngine.Object.DestroyImmediate(fixtureRoot);
        profile = null;
        fixtureRoot = null;
        hub = null;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying)
            return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeErrors.Add(condition);
    }

    private static void Fail(string message)
    {
        SessionState.SetString(FailKey, "step=" + step + " " + message);
        EditorApplication.ExitPlaymode();
    }

    private static void WaitFrames(int frames)
    {
        waitUntilFrame = Time.frameCount + Math.Max(1, frames);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
