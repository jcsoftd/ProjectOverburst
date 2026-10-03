using System;
using Overburst.DebugTools;
using UnityEditor;
using UnityEngine;

// One-shot next-Play selection only. Never starts/stops Play or changes account/scene/input.
[InitializeOnLoad]
public static class CombatEffectABVerifier
{
    const string Root = "OVERBURST/테스트/전투 끊김/효과 A-B/";
    static CombatEffectABVerifier()
    {
        EditorApplication.playModeStateChanged -= OnPlayState;
        EditorApplication.playModeStateChanged += OnPlayState;
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.EraseString(CombatEffectDiagnosticControls.ActiveKey);
            SessionState.EraseString(CombatEffectDiagnosticControls.ActiveAccountKey);
            CombatEffectDiagnosticControls.ResetToBaseline();
        }
    }

    public static void ArmNext(string scenario, string expectedSaveDirectory = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 EditMode에서 다음 Play 조건을 선택하세요.");
        if (!CombatEffectDiagnosticControls.TryParse(scenario, out _))
            throw new ArgumentException("Unknown effect A/B case.", nameof(scenario));
        if (!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("다른 격리 계정이 준비되어 있어 조건을 예약하지 않았어요.");
        string account = string.IsNullOrEmpty(expectedSaveDirectory) ? "" : System.IO.Path.GetFullPath(expectedSaveDirectory);
        SessionState.SetString(CombatEffectDiagnosticControls.PendingKey, scenario);
        SessionState.SetString(CombatEffectDiagnosticControls.PendingAccountKey, account);
        Debug.Log("[CombatEffectAB] 다음 사용자 Play 조건: " + scenario + " · 기록은 기존 진단 메뉴에서 시작 · 계정/씬/자동 Play 변경 없음");
    }

    [MenuItem(Root + "다음 Play/기본 6종 ON")] static void Baseline() => ArmNext("Baseline");
    [MenuItem(Root + "다음 Play/혈흔 분출만 OFF")] static void NoBlood() => ArmNext("NoBloodSpray");
    [MenuItem(Root + "다음 Play/바닥 데칼만 OFF")] static void NoDecals() => ArmNext("NoGroundDecals");
    [MenuItem(Root + "다음 Play/원소 타격 VFX만 OFF")] static void NoElement() => ArmNext("NoElementHit");
    [MenuItem(Root + "다음 Play/적 상태 오라 표시만 OFF")] static void NoAura() => ArmNext("NoStatusAura");
    [MenuItem(Root + "다음 Play/명시적 타격음만 OFF")] static void NoAudio() => ArmNext("NoHitAudio");
    [MenuItem(Root + "다음 Play/Feel 충격 표현만 OFF")] static void NoFeel() => ArmNext("NoImpactFeel");
    [MenuItem(Root + "다음 Play/선택 6종 모두 OFF")] static void NoPresentation() => ArmNext("NoHitPresentation");

    [MenuItem(Root + "예약 취소·기본 복원")]
    public static void Cancel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("현재 Play 조건은 중간에 변경하지 않아요. 종료 뒤 복원됩니다.");
        SessionState.EraseString(CombatEffectDiagnosticControls.PendingKey);
        SessionState.EraseString(CombatEffectDiagnosticControls.PendingAccountKey);
        SessionState.EraseString(CombatEffectDiagnosticControls.ActiveKey);
        SessionState.EraseString(CombatEffectDiagnosticControls.ActiveAccountKey);
        CombatEffectDiagnosticControls.ResetToBaseline();
        // Capture may have been armed by its own menu, so preserve that separate reservation.
    }

    [MenuItem(Root + "정책 검사 (Play 없음)")]
    public static void Validate() => Debug.Log("[CombatEffectAB] " + CombatEffectDiagnosticControls.ValidateCore());

    static void OnPlayState(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            string next = SessionState.GetString(CombatEffectDiagnosticControls.PendingKey, "");
            string expected = SessionState.GetString(CombatEffectDiagnosticControls.PendingAccountKey, "");
            string actual = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
            SessionState.EraseString(CombatEffectDiagnosticControls.PendingKey);
            SessionState.EraseString(CombatEffectDiagnosticControls.PendingAccountKey);
            SessionState.EraseString(CombatEffectDiagnosticControls.ActiveKey);
            SessionState.EraseString(CombatEffectDiagnosticControls.ActiveAccountKey);
            if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
                && CombatEffectDiagnosticControls.TryParse(next, out _))
            {
                SessionState.SetString(CombatEffectDiagnosticControls.ActiveKey, next);
                SessionState.SetString(CombatEffectDiagnosticControls.ActiveAccountKey, expected);
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseString(CombatEffectDiagnosticControls.ActiveKey);
            SessionState.EraseString(CombatEffectDiagnosticControls.ActiveAccountKey);
            CombatEffectDiagnosticControls.ResetToBaseline();
        }
        // Keep the active policy until capture shutdown has written effect-ab.json.
        // Reload callbacks do not clear the current Play policy.
    }
}
