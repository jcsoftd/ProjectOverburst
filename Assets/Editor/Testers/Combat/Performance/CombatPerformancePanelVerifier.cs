#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.DebugTools;
using Overburst.DebugTools.Performance;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>격리 Play에서 정식 패널 버튼과 관찰 수명·저장·반복 진입을 검사한다. Play·계정 반환은 호출자가 소유한다.</summary>
public static class CombatPerformancePanelVerifier
{
    static readonly string[] Ids = { "system.combatPerf.start", "system.combatPerf.status", "system.combatPerf.finish",
        "system.combatPerf.result", "system.combatPerf.reveal", "system.combatPerf.window" };
    static readonly List<string> checks = new List<string>();
    static readonly List<string> folders = new List<string>();
    static double deadline, next;
    static int phase, cycle;
    static string output, account, startScene, input, weapon;
    static float hp, timeScale;
    static bool background, windowWasOpen;
    static string[] objects;

    public static void RunInCurrentPlay(string directory)
    {
        if (!Application.isPlaying || CombatPerformanceRunner.Current != null || CombatPerformanceSession.Busy || output != null)
            throw new InvalidOperationException("Idle isolated Play required.");
        directory = CombatPerformancePaths.RequireOutput(directory);
        account = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if (!string.Equals(account, Path.Combine(directory, "IsolatedAccount"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This verifier requires its own isolated account.");
        if (DebugHub.Instance == null || PlayerContext.GetOrCreate().CurrentActor == null || EventSystem.current == null)
            throw new InvalidOperationException("Product player, debug hub and EventSystem must be ready.");
        checks.Clear(); folders.Clear(); output = directory; cycle = 0; phase = 0;
        var player = PlayerContext.GetOrCreate().CurrentActor;
        hp = player.GetComponent<CombatHealth>().CurrentHp; weapon = player.Equipment.CurrentWeaponItem?.runtimeInstanceId;
        input = InputFingerprint();
        startScene = UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        background = Application.runInBackground; timeScale = Time.timeScale; windowWasOpen = DebugHub.IsOpen;
        objects = DebugHub.Instance.GetComponentsInChildren<Transform>(true).Select(t => t.GetInstanceID().ToString()).OrderBy(id => id).ToArray();
        deadline = EditorApplication.timeSinceStartup + 90; next = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Interrupted;
    }
    static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
    static string InputFingerprint() => CombatPerformancePaths.Hash(EditorJsonUtility.ToJson(UnityEngine.InputSystem.InputSystem.settings));
    static Button Control(string id, int index = 0)
    {
        DebugHub.OpenTab(DebugTabs.SystemTab); Canvas.ForceUpdateCanvases();
        var row = DebugHub.Instance.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Row " + id && t.gameObject.activeInHierarchy);
        var button = row.GetComponentsInChildren<Button>(true).Single(b => b.name == "Button " + index);
        // Refresh uses the same model as the native widget. Allow the hub's next UI refresh to apply interactability before a click.
        return button;
    }
    static void Click(Button button)
    {
        if (!button.gameObject.activeInHierarchy || !button.interactable) throw new InvalidOperationException("Native button unavailable: " + button.name);
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }
    static void Tick()
    {
        try
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Panel Play ended or exceeded 90 seconds.");
            if (EditorApplication.timeSinceStartup < next) return;
            var player = PlayerContext.GetOrCreate().CurrentActor;
            switch (phase)
            {
                case 0:
                    foreach (string id in Ids) Require(DebugRegistry.Find(id) != null && DebugHub.Instance.GetComponent<DebugHubView>().ItemIds.Contains(id), "Authored registered row: " + id);
                    Require(CombatPerformancePanel.Backend != null && CombatPerformancePanel.CanStart, "Editor backend and ready state");
                    DebugHub.OpenTab(DebugTabs.SystemTab); phase = 1; next = EditorApplication.timeSinceStartup + .5; break;
                case 1:
                    Click(Control(Ids[0], cycle));
                    Require(CombatPerformancePanel.Observing && !DebugHub.IsOpen, "Native start closes panel, cycle " + cycle);
                    int expected = cycle == 0 ? 120 : cycle == 1 ? 300 : CombatPerformancePanel.ManualLimitSeconds;
                    Require(CombatPerformanceRunner.Current.Run.profile.observationSeconds == expected, "Duration selection " + expected);
                    Require(!CombatPerformancePanel.CanStart && !CombatPerformancePanel.Start(1).Success
                        && !DebugPerfRecorder.Start(10).Success, "Concurrent recorder rejected");
                    phase = 2; next = EditorApplication.timeSinceStartup + 3; break;
                case 2:
                    Require(CombatPerformancePanel.Status.Contains("현재 전투 측정"), "Observation progress");
                    DebugHub.OpenTab(DebugTabs.SystemTab); phase = 3; next = EditorApplication.timeSinceStartup + .5; break;
                case 3:
                    Click(Control(Ids[2]));
                    Require(Application.isPlaying && !CombatPerformancePanel.Running && !CombatPerformanceSession.Busy, "Finish preserves current Play");
                    var run = CombatPerformanceRunner.LastRun;
                    Require(run != null && run.status == "COMPLETE" && run.profile.mode == CombatPerformanceMode.Observation && run.segments.Count == 1, "Manual completion saves current chunk");
                    Require(run.segments[0].frames > 0 && File.Exists(Path.Combine(run.output, "report.md"))
                        && File.Exists(Path.Combine(run.output, run.segments[0].folder, "frames.csv")), "Measured frames and report persisted");
                    Require(CombatPerformanceReport.Read(run.output).status == "COMPLETE" && CombatPerformancePanel.HasResult
                        && CombatPerformancePanel.Summary.Contains("p95"), "Recent result available");
                    Require(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) == account
                        && player.Equipment.CurrentWeaponItem?.runtimeInstanceId == weapon && player.GetComponent<CombatHealth>().CurrentHp == hp
                        && Time.timeScale == timeScale && Application.runInBackground == background, "Observation preserves account, weapon, HP and settings");
                    Require(InputFingerprint() == input
                        && AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene) == startScene, "Input and start scene preserved");
                    var remaining = new HashSet<string>(DebugHub.Instance.GetComponentsInChildren<Transform>(true).Select(t => t.GetInstanceID().ToString()));
                    Require(objects.All(remaining.Contains), "Authored UI objects reused");
                    folders.Add(run.output); cycle++;
                    if (cycle < 3) { phase = 1; next = EditorApplication.timeSinceStartup + .5; }
                    else { Require(CombatPerformancePanel.Start(2).Success, "Timed observation starts"); phase = 4; next = EditorApplication.timeSinceStartup + 4; }
                    break;
                case 4:
                    Require(!CombatPerformancePanel.Running && Application.isPlaying && CombatPerformanceRunner.LastRun.status == "COMPLETE", "Timed observation completes without ending Play");
                    folders.Add(CombatPerformanceRunner.LastRun.output); Finish(null); break;
            }
        }
        catch (Exception error) { Finish(error); }
    }
    static void Interrupted() => Finish(new InvalidOperationException("Assembly reload interrupted validation."));
    static void Finish(Exception error)
    {
        if (output == null) return;
        EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Interrupted;
        if (CombatPerformancePanel.Observing) CombatPerformancePanel.Finish();
        if (Application.isPlaying) { if (windowWasOpen) DebugHub.Open(); else DebugHub.Close(); }
        try
        {
            CombatPerformancePaths.SaveJson(Path.Combine(output, "panel-play.json"), new Result { status = error == null ? "PASS" : "FAIL",
                reason = error?.ToString() ?? "", checks = checks.ToArray(), folders = folders.ToArray(), utc = DateTime.UtcNow.ToString("O") });
        }
        finally { output = null; }
    }
    [Serializable] sealed class Result { public string status, reason, utc; public string[] checks, folders; }
}
#endif
