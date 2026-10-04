#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.DebugTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 관찰에 도달한 실제 VisualPlay와 실제 UI Stop을 사용한다. 게임 전체 검증으로 확대하지 않는다.
[InitializeOnLoad]
public static class VisualPlayStopWriteFailureVerifier
{
    const string Key = "Overburst.KAN5StopFailure.";
    const string VisualKey = "Overburst.VisualPlay.Session.";
    const string LockWorker = @"param([Parameter(Mandatory=$true)][string]$ControlFile)
$ErrorActionPreference='Stop'
$control=Get-Content -LiteralPath $ControlFile -Raw -Encoding UTF8 | ConvertFrom-Json
$caseDirectory=[IO.Path]::GetFullPath((Split-Path -Parent $ControlFile))
$resultPath=[IO.Path]::GetFullPath($control.resultPath)
if ([IO.Path]::GetFileName($resultPath) -ne 'Result.json' -or $control.ownerPid -le 0) { throw 'Unexpected owned result file or Editor.' }
if ($resultPath -notlike '*\개인파일\코덱스산출\Tests\VisualPlay\Run_*\Result.json') { throw 'Owned VisualPlay output required.' }
$stream=$null
try {
    $stream=[IO.File]::Open($resultPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    @{status='LOCKED';pid=$PID;resultPath=$resultPath;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $caseDirectory 'lock-ready.json') -Encoding utf8
    $deadline=[DateTime]::UtcNow.AddMinutes(15)
    while ([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath (Join-Path $caseDirectory 'release-lock'))) {
        if (!(Get-Process -Id $control.ownerPid -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 250
    }
} finally {
    if ($stream) { $stream.Dispose() }
    @{status='RELEASED';pid=$PID;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $caseDirectory 'lock-released.json') -Encoding utf8
}";
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static string Output => SessionState.GetString(Key + "output", "");
    static int Case => SessionState.GetInt(Key + "case", 0);
    static string CaseDirectory => Path.Combine(Output, Case == 0 ? "IOException" : "UnauthorizedAccessException");
    static string RunDirectory => SessionState.GetString(Key + "run", "");
    static string ResultPath => Path.Combine(RunDirectory, "Result.json");
    static string Note => SessionState.GetString(Key + "note", "");
    static bool Failed => !string.IsNullOrEmpty(SessionState.GetString(Key + "failure", ""));
    static Mouse mouse;
    static Vector2 point;
    static bool pressed;
    static int frame = -1, stageFrame;
    static string inputStage;
    static readonly Dictionary<InputActionMap, ReadOnlyArray<InputDevice>?> filters = new Dictionary<InputActionMap, ReadOnlyArray<InputDevice>?>();
    static VisualPlayStopWriteFailureVerifier()
    {
        if (Phase != 0) EditorApplication.update += Tick;
        else if (File.Exists(QueuePath)) EditorApplication.update += TryQueuedStart;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }
    static string QueuePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Jira/20261005_KAN_Goal/visual-stop-auto-start.json"));
    static void TryQueuedStart()
    {
        if (Phase != 0 || !File.Exists(QueuePath)) { EditorApplication.update -= TryQueuedStart; return; }
        var plan = JObject.Parse(File.ReadAllText(QueuePath));
        if ((string)plan["status"] != "QUEUED") { EditorApplication.update -= TryQueuedStart; return; }
        if (DateTime.UtcNow > plan["deadlineUtc"].ToObject<DateTime>().ToUniversalTime())
        { plan["status"] = "DEFERRED_TIMEOUT"; File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed) return;
        try
        {
            var result = Start((string)plan["output"]); plan["status"] = "STARTED"; plan["result"] = JToken.FromObject(result);
            File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart;
        }
        catch (Exception error)
        {
            if (Phase != 0 || Directory.Exists((string)plan["output"]))
            { plan["status"] = "FAILED_START"; plan["error"] = error.ToString(); File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart; }
        }
    }
    public static object Start(string outputDirectory)
    {
        RequireIdle();
        string output = Path.GetFullPath(outputDirectory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Fresh owned output required.");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "before.json"), JsonConvert.SerializeObject(new { scenes = Scenes(), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), input = EditorJsonUtility.ToJson(InputSystem.settings), background = Application.runInBackground, observe = VisualPlayBridge.ObserveSeconds, wait = VisualPlayBridge.WaitAfterScenario, full = VisualPlayBridge.FullContent, category = VisualPlayBridge.Category.Id, selected = VisualPlayBridge.Selected.Id }, Formatting.Indented));
        File.WriteAllText(Path.Combine(output, "HoldVisualPlayResult.ps1"), LockWorker, new System.Text.UTF8Encoding(true));
        SessionState.SetString(Key + "output", output); SessionState.SetInt(Key + "case", 0);
        SessionState.EraseString(Key + "failure"); Phase = 1;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        StartCase();
        return new { status = "STARTED", output, cases = 2, stopPath = "InputSystem mouse -> real EventSystem -> VisualPlayOverlay Stop" };
    }
    static void RequireIdle()
    {
        if (Phase != 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))) throw new InvalidOperationException("Healthy returned idle Editor required.");
        foreach (string key in new[] { "Overburst.WeakAttackPlayerLoop.plan", "Overburst.CombatPerformance.folder", "Overburst.CombatPerformance.phase", VisualKey + "plan", VisualKey + "deferredPlan", "Overburst.PlayerFootstepProductVerifier.Pending", "Overburst.CombatFacingVerifier.output", "Overburst.CrustaspikanMaterialVerifier.plan", "Overburst.KANGoal.BuildOwner" })
            if (!string.IsNullOrEmpty(SessionState.GetString(key, ""))) throw new InvalidOperationException("Foreign owner: " + key);
        if (SessionState.GetInt("Overburst.KAN14GUI.Phase", 0) != 0) throw new InvalidOperationException("GUI return pending.");
    }
    static void StartCase()
    {
        Directory.CreateDirectory(CaseDirectory);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 600).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetString(Key + "note", "KAN5_STOP_NOTE_" + Guid.NewGuid().ToString("N") + "_" + Case);
        VisualPlayBridge.FullContent = false; VisualPlayBridge.ObserveSeconds = 60f; VisualPlayBridge.WaitAfterScenario = true;
        var result = VisualPlayEditorSession.Instance.Start(VisualPlayScope.Individual, "C04", "VT04-02");
        if (!result.Success) throw new InvalidOperationException(result.ToString());
        string run = SessionState.GetString(VisualKey + "lastDirectory", "");
        if (!Path.GetFullPath(run).StartsWith(VisualPlayEditorSession.OutputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected VisualPlay run.");
        SessionState.SetString(Key + "run", run);
        Write("start.json", new { run, note = Note }); Phase = 1;
    }
    static bool OwnPlan()
    {
        foreach (string suffix in new[] { "plan", "deferredPlan" })
        {
            string json = SessionState.GetString(VisualKey + suffix, "");
            if (!string.IsNullOrEmpty(json)) return string.Equals((string)JObject.Parse(json)["directory"], RunDirectory, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
    static void Tick()
    {
        if (Phase == 0) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture)) throw new TimeoutException("Stop/write-failure verification deadline.");
            if (Phase == 1)
            {
                if (!OwnPlan()) throw new InvalidOperationException("Owned VisualPlay plan was lost before observation.");
                if (!EditorApplication.isPlaying || VisualPlayBridge.State.Phase != "관찰") return;
                if (VisualPlayEditorSession.Instance.Records.Count != 1 || VisualPlayEditorSession.Instance.Records[0].playback != "재생 중") throw new InvalidOperationException("A real observing record is required.");
                VisualPlayBridge.Note = Note;
                Write("observing.json", new { state = VisualPlayBridge.State, records = VisualPlayEditorSession.Instance.Records, note = VisualPlayBridge.Note });
                if (!File.Exists(ResultPath)) throw new InvalidOperationException("Pre-injection result file must exist.");
                Inject(); Phase = 2; inputStage = "awaitInjection";
            }
            if (Phase == 2)
            {
                if (!OwnPlan() || !EditorApplication.isPlaying) throw new InvalidOperationException("Observation ended before actual Stop click.");
                if (Case == 0 && !File.Exists(Path.Combine(CaseDirectory, "lock-ready.json"))) return;
                EditorApplication.QueuePlayerLoopUpdate();
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                if (inputStage == "awaitInjection")
                {
                    AssertWriteFails("injection");
                    VisualPlayBridge.Note = Note; VisualPlayBridge.Note = Note;
                    AssertRetained(false);
                    PrepareStopInput(); inputStage = "move"; stageFrame = frame;
                }
                else if (inputStage == "move" && frame - stageFrame >= 3) { pressed = true; inputStage = "press"; stageFrame = frame; }
                else if (inputStage == "press" && frame - stageFrame >= 3)
                {
                    pressed = false; Phase = 3;
                    Write("stop-input.json", new { frame, point = new[] { point.x, point.y }, source = "real InputSystem UI mouse press/release", persistentFailure = true });
                }
            }
            if (Phase == 3)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || VisualPlayBridge.State.Running || OwnPlan()) return;
                RestoreInput();
                if (!Failed)
                {
                    AssertWriteFails("afterReturn"); AssertRetained(true); AssertReturn();
                    Write("blocked-return.json", new { status = "PASS", state = VisualPlayBridge.State, records = VisualPlayEditorSession.Instance.Records, note = VisualPlayBridge.Note });
                }
                Unblock(); Phase = 4;
            }
            if (Phase == 4)
            {
                if (Case == 0 && !File.Exists(Path.Combine(CaseDirectory, "lock-released.json"))) return;
                if (!Failed)
                {
                    VisualPlayBridge.Note = Note;
                    var saved = JObject.Parse(File.ReadAllText(ResultPath));
                    if ((string)saved["records"][0]["note"] != Note || (string)saved["records"][0]["playback"] != "중단") throw new InvalidOperationException("Recovered result file lost interrupted record or note.");
                    Write("result.json", new { status = "PASS_SCOPED", resultFile = ResultPath, state = VisualPlayBridge.State, records = VisualPlayEditorSession.Instance.Records, gameValidation = "NOT_RUN" });
                }
                else Write("result.json", new { status = "FAIL", error = SessionState.GetString(Key + "failure", "") });
                if (!Failed && Case == 0) { SessionState.SetInt(Key + "case", 1); StartCase(); return; }
                Finish();
            }
        }
        catch (Exception error)
        {
            SessionState.SetString(Key + "failure", error.ToString()); Write("failure.json", new { error = error.ToString(), phase = Phase }); RestoreInput();
            if (OwnPlan()) VisualPlayEditorSession.Instance.Control(VisualPlayControl.Stop);
            Phase = 3;
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
    static void Inject()
    {
        if (Case == 1)
        {
            File.Move(ResultPath, Path.Combine(CaseDirectory, "Result.before.json")); Directory.CreateDirectory(ResultPath);
            return;
        }
        string control = Path.Combine(CaseDirectory, "control.json");
        File.WriteAllText(control, JsonConvert.SerializeObject(new { resultPath = ResultPath, ownerPid = System.Diagnostics.Process.GetCurrentProcess().Id }));
        string script = Path.Combine(Output, "HoldVisualPlayResult.ps1");
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), "System32/WindowsPowerShell/v1.0/powershell.exe"), "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -ControlFile \"" + control + "\"") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
        using (var child = System.Diagnostics.Process.Start(info)) Write("lock-process.json", new { pid = child.Id, executable = info.FileName, ownedControl = control });
    }
    static void AssertWriteFails(string stage)
    {
        Exception actual = null;
        try { File.WriteAllText(ResultPath, "This write must be rejected while the owned fault is active."); }
        catch (Exception error) { actual = error; }
        bool pass = Case == 0 ? actual is IOException : actual is UnauthorizedAccessException;
        Write(stage + "-write-failure.json", new { pass, actual = actual?.GetType().FullName, message = actual?.Message });
        if (!pass) throw new InvalidOperationException("The expected result-file failure is not active.");
    }
    static void AssertRetained(bool interrupted)
    {
        var record = VisualPlayEditorSession.Instance.Records.Single();
        var session = JArray.Parse(SessionState.GetString(VisualKey + "lastResults", "[]"));
        if (record.caseId != "VT04-02" || record.note != Note || VisualPlayBridge.Note != Note || (string)session[0]["note"] != Note || (interrupted && (record.playback != "중단" || (string)session[0]["playback"] != "중단"))) throw new InvalidOperationException("Backend, bridge or SessionState lost record/note.");
    }
    static void PrepareStopInput()
    {
        var overlay = UnityEngine.Object.FindFirstObjectByType<VisualPlayOverlay>();
        var stop = (Button)typeof(VisualPlayOverlay).GetField("stop", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(overlay);
        if (!stop.isActiveAndEnabled || !stop.interactable || DebugHub.IsOpen) throw new InvalidOperationException("Real visible Stop button required.");
        var rect = (RectTransform)stop.transform; var canvas = stop.GetComponentInParent<Canvas>();
        point = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center));
        var rays = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, rays);
        if (rays.FirstOrDefault().gameObject?.GetComponentInParent<Button>() != stop) throw new InvalidOperationException("Stop button is obstructed in actual UI raycast.");
        mouse = InputSystem.AddDevice<Mouse>("KAN5OwnedStopMouse");
        var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach (var map in new[] { module.point?.action?.actionMap, module.leftClick?.action?.actionMap }.Where(m => m != null).Distinct()) { filters[map] = map.devices; map.devices = new InputDevice[] { mouse }; }
        InputSystem.onBeforeUpdate += InputTick; pressed = false;
        Write("stop-raycast.json", new { point = new[] { point.x, point.y }, target = stop.name, maps = filters.Keys.Select(m => m.name).ToArray() });
    }
    static void InputTick()
    {
        if (InputState.currentUpdateType == InputUpdateType.Dynamic && mouse != null && mouse.added)
        { if (!mouse.enabled) InputSystem.EnableDevice(mouse); InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = (ushort)(pressed ? 1 : 0) }); }
    }
    static void RestoreInput()
    {
        InputSystem.onBeforeUpdate -= InputTick;
        foreach (var pair in filters) if (pair.Key.asset != null && pair.Key.devices.HasValue && pair.Key.devices.Value.Contains(mouse)) pair.Key.devices = pair.Value;
        filters.Clear(); if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); mouse = null; pressed = false;
    }
    static void Unblock()
    {
        if (Case == 0) File.WriteAllText(Path.Combine(CaseDirectory, "release-lock"), "release owned result lock");
        else if (Directory.Exists(ResultPath))
        {
            if (Directory.EnumerateFileSystemEntries(ResultPath).Any()) throw new InvalidOperationException("Owned result-path blocker is no longer empty.");
            Directory.Delete(ResultPath, false); File.Move(Path.Combine(CaseDirectory, "Result.before.json"), ResultPath);
        }
    }
    static void AssertReturn()
    {
        var before = JObject.Parse(File.ReadAllText(Path.Combine(Output, "before.json")));
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")) || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "")) || EditorUtility.scriptCompilationFailed || EditorJsonUtility.ToJson(InputSystem.settings) != (string)before["input"] || AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) != (string)before["startScene"] || !JToken.DeepEquals(JToken.FromObject(Scenes()), before["scenes"]) || InputSystem.devices.Any(d => d.name.StartsWith("KAN5Owned", StringComparison.Ordinal))) throw new InvalidOperationException("Editor/account/input/scenes were not fully returned.");
    }
    static object[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => (object)new { s.path, s.isDirty, s.rootCount }).ToArray();
    static void BeforeReload()
    {
        RestoreInput();
        if (Phase == 2) { SessionState.SetString(Key + "failure", "Unexpected reload before Stop click."); Phase = 3; }
    }
    static void Finish()
    {
        var before = JObject.Parse(File.ReadAllText(Path.Combine(Output, "before.json")));
        VisualPlayBridge.ObserveSeconds = (float)before["observe"]; VisualPlayBridge.WaitAfterScenario = (bool)before["wait"]; VisualPlayBridge.FullContent = (bool)before["full"];
        VisualPlayBridge.Category = VisualPlayCatalog.Categories.First(c => c.Id == (string)before["category"]); VisualPlayBridge.Selected = VisualPlayCatalog.Find((string)before["selected"]);
        File.WriteAllText(Path.Combine(Output, "result.json"), JsonConvert.SerializeObject(new { status = Failed ? "FAIL" : "PASS_SCOPED", error = SessionState.GetString(Key + "failure", ""), completedCases = Failed ? Case : 2, actualStopInput = !Failed, gameValidation = "NOT_RUN" }, Formatting.Indented));
        foreach (string suffix in new[] { "output", "run", "deadline", "note", "failure" }) SessionState.EraseString(Key + suffix);
        SessionState.EraseInt(Key + "case"); Phase = 0; EditorApplication.update -= Tick;
    }
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(CaseDirectory, name), JsonConvert.SerializeObject(value, Formatting.Indented));
}
#endif
