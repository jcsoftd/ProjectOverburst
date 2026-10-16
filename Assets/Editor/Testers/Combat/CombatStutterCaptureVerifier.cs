using Overburst.DebugTools;
using UnityEditor;
using UnityEngine;

// Diagnostic controls only: no auto-Play, fixture spawning, input, account selection or scene saves.
[InitializeOnLoad]
public static class CombatStutterCaptureVerifier
{
    const string Root = "OVERBURST/테스트/전투 끊김/";
    public const string NextRunKey = "OVERBURST.CombatStutterCapture.NextRunProfiler";
    static double nextRunCheck;
    static CombatStutterCaptureVerifier()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= StopBeforeReload;
        AssemblyReloadEvents.beforeAssemblyReload += StopBeforeReload;
        EditorApplication.playModeStateChanged -= OnPlayState;
        EditorApplication.playModeStateChanged += OnPlayState;
        if (SessionState.GetBool(NextRunKey, false)) EditorApplication.update += AwaitRun;
    }
    [MenuItem(Root + "다음 Play에서 관찰 시작")]
    static void Arm()
    {
        SessionState.SetBool(CombatStutterCapture.ArmKey, true);
        Debug.Log("[CombatStutterCapture] 다음 사용자 Play에서120초 관찰. 자동 Play/계정 전환 없음.");
    }
    [MenuItem(Root + "다음 지도 전투 Profiler 기록 예약")]
    public static void ArmNextRunWithProfiler()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || UnityEngine.Profiling.Profiler.enabled || UnityEngine.Profiling.Profiler.enableBinaryLog)
            throw new System.InvalidOperationException("Use an idle Editor with no existing profiler capture.");
        SessionState.EraseBool(CombatStutterCapture.ArmKey);
        SessionState.SetBool(NextRunKey, true); nextRunCheck = 0;
        EditorApplication.update -= AwaitRun; EditorApplication.update += AwaitRun;
        Debug.Log("[CombatStutterCapture] 다음 사용자 Play의 지도 입장 완료부터 최대120초 상세 기록. Play 종료 시 저장.");
    }
    static void AwaitRun()
    {
        if (!SessionState.GetBool(NextRunKey, false)) { EditorApplication.update -= AwaitRun; return; }
        if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.timeSinceStartup < nextRunCheck) return;
        nextRunCheck = EditorApplication.timeSinceStartup + .25;
        if (WorldSessionState.Phase != WorldPhase.Run || PersistentSceneFlow.Instance == null
            || PersistentSceneFlow.Instance.IsSwitching || GameplayInputBlocker.IsGameplayInputBlocked) return;
        var gate = Object.FindFirstObjectByType<RunWorldGate>();
        if (gate == null || !gate.IsReady || gate.SpawnBudget == null) return;
        SessionState.EraseBool(NextRunKey); EditorApplication.update -= AwaitRun;
        Report(CombatStutterCapture.Start(120, 50, true));
    }
    [MenuItem(Root + "다음 Play 예약 해제")]
    static void Disarm() { SessionState.EraseBool(CombatStutterCapture.ArmKey); SessionState.EraseBool(NextRunKey); EditorApplication.update -= AwaitRun; }
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
        if (state == PlayModeStateChange.ExitingPlayMode)
        { SessionState.EraseBool(NextRunKey); EditorApplication.update -= AwaitRun; StopBeforeReload(); }
    }
    static void Report(DebugResult result)
    {
        if (result.Success) Debug.Log("[CombatStutterCapture] " + result.Message);
        else Debug.LogWarning("[CombatStutterCapture] " + result.Message);
    }
}
