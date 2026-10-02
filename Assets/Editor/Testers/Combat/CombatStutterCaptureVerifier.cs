using Overburst.DebugTools;
using UnityEditor;
using UnityEngine;

// Diagnostic controls only: no auto-Play, fixture spawning, input, account selection or scene saves.
[InitializeOnLoad]
public static class CombatStutterCaptureVerifier
{
    const string Root = "OVERBURST/테스트/전투 끊김/";
    static CombatStutterCaptureVerifier()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= StopBeforeReload;
        AssemblyReloadEvents.beforeAssemblyReload += StopBeforeReload;
        EditorApplication.playModeStateChanged -= OnPlayState;
        EditorApplication.playModeStateChanged += OnPlayState;
    }
    [MenuItem(Root + "다음 Play에서 관찰 시작")]
    static void Arm()
    {
        SessionState.SetBool(CombatStutterCapture.ArmKey, true);
        Debug.Log("[CombatStutterCapture] 다음 사용자 Play에서120초 관찰. 자동 Play/계정 전환 없음.");
    }
    [MenuItem(Root + "다음 Play 예약 해제")]
    static void Disarm() { SessionState.EraseBool(CombatStutterCapture.ArmKey); }
    [MenuItem(Root + "현재 Play 관찰 시작")]
    static void Start() { Report(CombatStutterCapture.Start()); }
    [MenuItem(Root + "현재 Play 관찰 시작", true)]
    static bool CanStart() => EditorApplication.isPlaying && !CombatStutterCapture.Running;
    [MenuItem(Root + "현재 Play Profiler 포함 시작")]
    static void StartWithProfiler() { Report(CombatStutterCapture.Start(binaryProfiler: true)); }
    [MenuItem(Root + "현재 Play Profiler 포함 시작", true)]
    static bool CanStartWithProfiler() => CanStart();
    [MenuItem(Root + "기록 종료·저장")]
    static void Stop() { Report(CombatStutterCapture.Stop()); }
    [MenuItem(Root + "기록 종료·저장", true)]
    static bool CanStop() => CombatStutterCapture.Running;
    [MenuItem(Root + "기록 로직 검사 (Play 없음)")]
    public static void Validate() { Debug.Log("[CombatStutterCapture] " + CombatStutterCapture.ValidateCore()); }
    [MenuItem(Root + "최근 결과 폴더 열기")]
    static void OpenResults()
    {
        if (!string.IsNullOrEmpty(CombatStutterCapture.LastFolder)) EditorUtility.RevealInFinder(CombatStutterCapture.LastFolder);
    }
    static void StopBeforeReload() { if (CombatStutterCapture.Running) CombatStutterCapture.Stop(); }
    static void OnPlayState(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) StopBeforeReload();
    }
    static void Report(DebugResult result)
    {
        if (result.Success) Debug.Log("[CombatStutterCapture] " + result.Message);
        else Debug.LogWarning("[CombatStutterCapture] " + result.Message);
    }
}
