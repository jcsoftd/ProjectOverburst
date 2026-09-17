using System;
using UnityEditor;
using UnityEngine;

public static class MeleeEvadeCancelContractValidationUtility
{
    [MenuItem("OVERBURST/Codex/Validate/Validate Melee Evade Cancel Contract")]
    public static void RunFromMenu()
    {
        RunValidationFromCommandLine();
    }

    public static void RunValidationFromCommandLine()
    {
        ValidateCase(false, false, false, "일반 대기 상태");
        ValidateCase(false, true, false, "콤보 설정만 남은 비공격 상태");
        ValidateCase(true, false, false, "비콤보 공격 상태");
        ValidateCase(true, true, true, "진행 중인 근접 콤보");
        if (!MeleeEvadeCancelPolicy.ResetComboOnCancel)
            throw new InvalidOperationException("[MeleeEvadeCancelValidation] 회피 취소는 콤보를 초기화해야 합니다.");

        Debug.Log("[MeleeEvadeCancelValidation] 실제 근접 콤보 전 구간 회피 취소·콤보 초기화 계약 통과");
    }

    private static void ValidateCase(
        bool isAttackInProgress,
        bool usesCombo,
        bool expected,
        string label)
    {
        bool actual = MeleeEvadeCancelPolicy.CanCancel(isAttackInProgress, usesCombo);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"[MeleeEvadeCancelValidation] {label} 결과 불일치: expected={expected}, actual={actual}");
        }
    }
}
