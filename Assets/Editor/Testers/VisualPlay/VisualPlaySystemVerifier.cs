#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.DebugTools;
using UnityEditor;
using UnityEngine;

/// <summary>시각 품질과 별개인 도구 검증. 실제 준비·조작·장면 실행·계정 반환을 관찰한다.</summary>
[InitializeOnLoad]
public static class VisualPlaySystemVerifier
{
    const string Key = "Overburst.VisualPlay.Verifier.";
    static string output;
    static int stage;
    static double deadline;
    static string lastProgress;
    static double panelCheckAt;
    static int panelCompleted;
    static string inputBefore, startSceneBefore;
    static readonly List<string> checks = new List<string>();
    static VisualPlaySystemVerifier()
    {
        output = SessionState.GetString(Key + "output", "");
        stage = SessionState.GetInt(Key + "stage", 0);
        deadline = double.TryParse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        inputBefore = SessionState.GetString(Key + "inputBefore", ""); startSceneBefore = SessionState.GetString(Key + "startSceneBefore", "");
        var savedChecks = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
        if (savedChecks != null) checks.AddRange(savedChecks);
        EditorApplication.update += Tick;
    }
    public static object Begin(string[] ids = null, bool allVariants = false)
    {
        if (!string.IsNullOrEmpty(output)) throw new InvalidOperationException("시각 도구 검증이 이미 진행 중입니다.");
        ids = ids ?? new[] { "VT01-01", "VT02-07", "VT03-01", "VT03-04", "VT04-01", "VT05-03", "VT06-04", "VT07-03", "VT08-02", "VT09-07", "VT10-04", "VT11-02", "VT12-04", "VT13-04", "VT14-03", "VT15-04", "VT16-03", "VT17-02", "VT18-01", "VT19-02", "VT20-02", "VT21-01", "VT22-01", "VT23-03" };
        inputBefore = EditorJsonUtility.ToJson(UnityEngine.InputSystem.InputSystem.settings);
        startSceneBefore = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        var result = VisualPlayEditorSession.Instance.StartVerification(ids, allVariants);
        if (!result.Success) throw new InvalidOperationException(result.Message);
        output = Path.Combine(VisualPlayEditorSession.OutputRoot, "Verifier_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(output); checks.Clear(); SessionState.EraseString(Key + "checks"); stage = 0; deadline = EditorApplication.timeSinceStartup + 1800;
        SessionState.SetString(Key + "output", output); SessionState.SetInt(Key + "stage", stage);
        SessionState.SetString(Key + "deadline", deadline.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetString(Key + "inputBefore", inputBefore); SessionState.SetString(Key + "startSceneBefore", startSceneBefore);
        File.WriteAllText(Path.Combine(output, "Plan.json"), JsonConvert.SerializeObject(new { ids, allVariants, purpose = "tool and representative playback verification; user visual acceptance NOT_RUN" }, Formatting.Indented));
        return new { status = "STARTED", output, definitions = ids.Length, count = VisualPlayEditorSession.Instance.State.Planned };
    }
    static void Tick()
    {
        if (string.IsNullOrEmpty(output)) return;
        try
        {
            var session = VisualPlayEditorSession.Instance; var state = session.State;
            string progress = state.Current + "|" + state.Phase + "|" + state.Completed + "|" + state.Waiting + "|" + state.Paused;
            if (progress != lastProgress)
            {
                lastProgress = progress;
                File.WriteAllText(Path.Combine(output, "Progress.json"), JsonConvert.SerializeObject(new { state, records = session.Records }, Formatting.Indented));
            }
            if (EditorApplication.timeSinceStartup > deadline) { session.Control(VisualPlayControl.Stop); Finish("FAIL", "도구 검증 제한 시간 초과"); return; }
            if (EditorApplication.isPlaying && (state.Waiting || (stage >= 2 && stage <= 6)))
            {
                if (stage == 0)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, "OverlayCompact.png"));
                    var physical = UnityEngine.InputSystem.Keyboard.current;
                    if (physical != null && physical.name.StartsWith("VisualPlay")) throw new Exception("자동 입력이 실제 키보드를 가렸습니다.");
                    session.Review(VisualPlayReview.Problem, "도구 검증 표시; 사용자 확인 아님");
                    if (state.Problems != 1 || state.Checked != 0) throw new Exception("시각 표시와 재생 상태가 혼합되었습니다.");
                    VisualPlayBridge.Note = "판정 버튼 없이 입력한 메모; 사용자 확인 아님";
                    if (session.Records.Last().note != VisualPlayBridge.Note) throw new Exception("메모만 입력한 내용이 기록되지 않았습니다.");
                    session.Control(VisualPlayControl.Replay); AddCheck("review separated, note-only saved, physical keyboard retained, replay requested");
                    stage = 1; SessionState.SetInt(Key + "stage", stage); return;
                }
                if (stage == 1 && session.Records.Count < 2) return;
                if (stage == 1)
                {
                    if (state.Completed != 0) throw new Exception("재시청이 진행 수를 증가시켰습니다.");
                    var latest = session.Records.Last();
                    if (latest.review != VisualPlayReview.Problem || latest.note != "판정 버튼 없이 입력한 메모; 사용자 확인 아님" || state.Problems != 1)
                        throw new Exception("재시청에서 문제 표시나 메모가 사라졌습니다.");
                    AddCheck("replay preserves problem and note");
                    session.Review(VisualPlayReview.Unreviewed, "도구 검증; 사용자 확인 아님");
                    session.Control(VisualPlayControl.Pause);
                    if (state.Waiting || state.Paused) throw new Exception("대기 중 계속 조작이 연결되지 않았습니다.");
                    session.Control(VisualPlayControl.Pause);
                    if (!state.Paused) throw new Exception("항목 뒤 멈춤 조작이 연결되지 않았습니다.");
                    session.Control(VisualPlayControl.Continue); AddCheck("replay preserves planned progress, pause/continue bound");
                    var overlay = UnityEngine.Object.FindFirstObjectByType<VisualPlayOverlay>();
                    if (overlay == null) throw new Exception("작은 재생 안내가 없어요");
                    var view = new SerializedObject(overlay);
                    var panel = (UnityEngine.RectTransform)((UnityEngine.GameObject)view.FindProperty("panel").objectReferenceValue).transform;
                    if (panel.sizeDelta != new Vector2(390, 112) || panel.anchorMin != new Vector2(1, 0) || panel.pivot != new Vector2(1, 0)) throw new Exception("재생 안내의 기본 크기·우측 하단 배치가 달라요");
                    ((UnityEngine.UI.Button)view.FindProperty("expand").objectReferenceValue).onClick.Invoke();
                    stage = 2; panelCheckAt = EditorApplication.timeSinceStartup + .6; SessionState.SetInt(Key + "stage", stage); return;
                }
                if (stage == 2)
                {
                    if (EditorApplication.timeSinceStartup < panelCheckAt) return;
                    var overlay = UnityEngine.Object.FindFirstObjectByType<VisualPlayOverlay>();
                    var view = new SerializedObject(overlay);
                    var panel = (UnityEngine.RectTransform)((UnityEngine.GameObject)view.FindProperty("panel").objectReferenceValue).transform;
                    if (panel.sizeDelta != new Vector2(390, 232)) throw new Exception("설명 펼치기가 연결되지 않았어요");
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, "OverlayExpanded.png"));
                    stage = 3; panelCheckAt = EditorApplication.timeSinceStartup + .6; SessionState.SetInt(Key + "stage", stage); return;
                }
                if (stage == 3)
                {
                    if (EditorApplication.timeSinceStartup < panelCheckAt) return;
                    var overlay = UnityEngine.Object.FindFirstObjectByType<VisualPlayOverlay>();
                    var view = new SerializedObject(overlay);
                    ((UnityEngine.UI.Button)view.FindProperty("expand").objectReferenceValue).onClick.Invoke();
                    panelCompleted = state.Completed; DebugHub.OpenTab(DebugTabs.VisualPlay);
                    stage = 4; panelCheckAt = EditorApplication.timeSinceStartup + 1; SessionState.SetInt(Key + "stage", stage); return;
                }
                if (stage == 4)
                {
                    if (EditorApplication.timeSinceStartup < panelCheckAt) return;
                    if (!DebugHub.IsOpen || !OverburstTimeEffectArbiter.IsPaused || state.Completed != panelCompleted) throw new Exception("F1 관찰 중 게임·재생 정지가 연결되지 않았어요");
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, "DebugPanel.png"));
                    stage = 5; panelCheckAt = EditorApplication.timeSinceStartup + .6; SessionState.SetInt(Key + "stage", stage); return;
                }
                if (stage == 5)
                {
                    if (EditorApplication.timeSinceStartup < panelCheckAt) return;
                    DebugHub.Close(); stage = 6; panelCheckAt = EditorApplication.timeSinceStartup + .6; SessionState.SetInt(Key + "stage", stage); return;
                }
                if (stage == 6)
                {
                    if (EditorApplication.timeSinceStartup < panelCheckAt) return;
                    if (OverburstTimeEffectArbiter.IsPaused) throw new Exception("F1을 닫은 뒤 게임 정지가 해제되지 않았어요");
                    AddCheck("compact bottom-right 390x112, expanded 390x232, F1 pauses and resumes playback");
                    stage = 7; SessionState.SetInt(Key + "stage", stage);
                }
                session.Control(VisualPlayControl.Continue);
            }
            if (!state.Running && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                bool ready = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
                    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable));
                if (!ready) throw new Exception("일반 Play 계정 선택이 반환되지 않았습니다.");
                if (inputBefore != EditorJsonUtility.ToJson(UnityEngine.InputSystem.InputSystem.settings)) throw new Exception("입력 설정이 반환되지 않았습니다.");
                if (startSceneBefore != AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene)) throw new Exception("Play 시작 씬이 반환되지 않았습니다.");
                if (UnityEngine.InputSystem.InputSystem.devices.Any(device => device.name.StartsWith("VisualPlay"))) throw new Exception("자동 입력 장치가 남았습니다.");
                AddCheck("input settings and play start scene restored, virtual devices removed");
                AddCheck("stopped/ready, actual account selected, isolated environment empty");
                bool failed = state.Completed != state.Planned || session.Records.GroupBy(record => record.planIndex).Select(group => group.Last()).Any(record => record.playback != "완료");
                Finish(failed ? "FAIL" : "PASS", failed ? "대표 장면 실패를 확인하세요" : null);
            }
        }
        catch (Exception error) { VisualPlayEditorSession.Instance.Control(VisualPlayControl.Stop); Finish("FAIL", error.Message); }
    }
    static void AddCheck(string value)
    {
        checks.Add(value); SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(checks));
    }
    static void Finish(string status, string error)
    {
        File.WriteAllText(Path.Combine(output, "Report.json"), JsonConvert.SerializeObject(new { status, error, checks,
            state = VisualPlayEditorSession.Instance.State, records = VisualPlayEditorSession.Instance.Records,
            userVisualAcceptance = "NOT_RUN", playerBuild = "NOT_RUN" }, Formatting.Indented));
        SessionState.EraseString(Key + "output"); SessionState.EraseInt(Key + "stage"); SessionState.EraseString(Key + "deadline"); output = "";
        SessionState.EraseString(Key + "inputBefore"); SessionState.EraseString(Key + "startSceneBefore"); SessionState.EraseString(Key + "checks");
    }
}
#endif
