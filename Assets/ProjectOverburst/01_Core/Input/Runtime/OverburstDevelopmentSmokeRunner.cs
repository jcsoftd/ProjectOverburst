#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// Explicit, local development-build verification. No behavior without the command-line flag.
public sealed class OverburstDevelopmentSmokeRunner : MonoBehaviour
{
    [Serializable]
    private sealed class Report
    {
        public string status;
        public string unityVersion;
        public string playerPath;
        public string scene;
        public float movementDistance;
        public float blockedMovementDistance;
        public bool inventoryInput;
        public bool combatToggle;
        public string[] errors;
    }

    private string outputDirectory;
    private readonly List<string> errors = new List<string>();
    private readonly Report report = new Report();
    private bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartWhenRequested()
    {
        if (Application.isEditor || !Debug.isDebugBuild)
            return;
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-overburstSmokeOutput");
        if (index < 0)
            return;
        if (index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]) || Directory.Exists(args[index + 1]))
        {
            Debug.LogError("OVERBURST smoke output must be a new absolute directory.");
            Application.Quit(2);
            return;
        }
        var runner = new GameObject("OVERBURST Development Smoke").AddComponent<OverburstDevelopmentSmokeRunner>();
        DontDestroyOnLoad(runner.gameObject);
        runner.outputDirectory = args[index + 1];
        Directory.CreateDirectory(runner.outputDirectory);
        Application.runInBackground = true;
        Application.logMessageReceived += runner.OnLog;
        runner.StartCoroutine(runner.Drive());
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
    }

    private IEnumerator Drive()
    {
        IEnumerator routine = Verify();
        while (true)
        {
            bool next;
            try { next = routine.MoveNext(); }
            catch (Exception exception) { errors.Add(exception.ToString()); break; }
            if (!next) break;
            yield return routine.Current;
        }
        Finish();
    }

    private IEnumerator Verify()
    {
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        float deadline = Time.realtimeSinceStartup + 90f;
        PlayerActorRuntime actor = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            PlayerContext context = PlayerContext.GetOrCreate();
            actor = context != null ? context.CurrentActor : null;
            if (actor != null && SceneManager.GetSceneByName("HideoutScene").isLoaded) break;
            yield return null;
        }
        Require(actor != null && SceneManager.GetSceneByName("HideoutScene").isLoaded, "Hideout/player startup timeout");
        Require(Keyboard.current != null, "Keyboard unavailable");
        yield return new WaitForSeconds(1f);
        report.scene = "HideoutScene";
        Capture("startup.png");
        yield return new WaitForSeconds(1f);

        Vector3 before = actor.transform.position;
        SetKeys(Key.W);
        yield return new WaitForSeconds(0.4f);
        SetKeys();
        yield return new WaitForSeconds(0.15f);
        report.movementDistance = Vector3.ProjectOnPlane(actor.transform.position - before, Vector3.up).magnitude;
        Require(report.movementDistance > 0.15f, "W input did not move the actor");

        InventoryUI inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        Require(inventory != null && !inventory.IsVisible, "Unexpected inventory startup state");
        SetKeys(Key.Tab);
        yield return null;
        SetKeys();
        yield return new WaitForSeconds(0.25f);
        Require(inventory.IsVisible && GameplayInputBlocker.IsGameplayInputBlocked, "Tab did not open/block inventory");
        Capture("inventory.png");
        before = actor.transform.position;
        SetKeys(Key.W);
        yield return new WaitForSeconds(0.3f);
        SetKeys();
        report.blockedMovementDistance = Vector3.ProjectOnPlane(actor.transform.position - before, Vector3.up).magnitude;
        Require(report.blockedMovementDistance < 0.1f, "Movement was not blocked by inventory");
        SetKeys(Key.Tab);
        yield return null;
        SetKeys();
        yield return new WaitForSeconds(0.25f);
        Require(!inventory.IsVisible && !GameplayInputBlocker.IsGameplayInputBlocked, "Tab did not close/unblock inventory");
        report.inventoryInput = true;

        PlayerCombatModeController mode = PlayerCombatModeController.GetOrCreate();
        bool initialMode = mode.IsCombatModeActive;
        SetKeys(Key.X);
        yield return null;
        SetKeys();
        yield return new WaitForSeconds(0.25f);
        Require(mode.IsCombatModeActive != initialMode, "X combat-mode input did not toggle");
        SetKeys(Key.X);
        yield return null;
        SetKeys();
        yield return new WaitForSeconds(0.25f);
        Require(mode.IsCombatModeActive == initialMode, "X combat mode did not restore");
        report.combatToggle = true;
        Capture("final.png");
        yield return new WaitForSeconds(1f);
        foreach (string name in new[] { "startup.png", "inventory.png", "final.png" })
            Require(File.Exists(Path.Combine(outputDirectory, name)), "Screenshot missing: " + name);
    }

    private void Capture(string fileName) => ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, fileName));
    private static void SetKeys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private void Finish()
    {
        if (finished) return;
        finished = true;
        if (Keyboard.current != null) SetKeys();
        Application.logMessageReceived -= OnLog;
        report.status = errors.Count == 0 ? "PASS" : "FAIL";
        report.unityVersion = Application.unityVersion;
        report.playerPath = Application.dataPath;
        report.errors = errors.ToArray();
        File.WriteAllText(Path.Combine(outputDirectory, "player-smoke.json"), JsonUtility.ToJson(report, true));
        Debug.Log("[OVERBURST Player Smoke] " + report.status);
        Application.Quit(errors.Count == 0 ? 0 : 2);
    }
}
#endif
