using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MeleeComboDefinition))]
public sealed class MeleeComboDefinitionEditor : Editor
{
    private string lastBakeReport;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        MeleeComboDefinition combo = (MeleeComboDefinition)target;
        DrawAttackIdValidation(combo);
        EditorGUILayout.Space(8f);
        DrawSeparator();
        EditorGUILayout.LabelField("공격 궤적 베이크", EditorStyles.boldLabel);
        DrawTrajectoryStatus(combo);
        DrawSlopeSummary(combo);

        bool canBake = MeleeAttackVfxSlopeBakeUtility.CanBake(combo, out string profileName);
        if (!canBake)
        {
            EditorGUILayout.HelpBox(
                "이 콤보에는 공격 궤적 베이크 프로필이 등록되어 있지 않습니다.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("베이크 프로필", profileName);
        }

        using (new EditorGUI.DisabledScope(!canBake))
        {
            if (GUILayout.Button("선택한 콤보 공격 궤적 다시 베이크", GUILayout.Height(28f)))
                ConfirmAndBake(combo);
        }

        if (!string.IsNullOrEmpty(lastBakeReport))
            EditorGUILayout.HelpBox(lastBakeReport, MessageType.None);
    }

    private static void DrawTrajectoryStatus(MeleeComboDefinition combo)
    {
        MeleeAttackTrajectoryBakeStatus status =
            MeleeAttackVfxSlopeBakeUtility.GetBakeStatus(combo, out string message);
        MessageType type = status == MeleeAttackTrajectoryBakeStatus.Current
            ? MessageType.Info
            : status == MeleeAttackTrajectoryBakeStatus.Failed
                ? MessageType.Error
                : MessageType.Warning;
        string label;
        switch (status)
        {
            case MeleeAttackTrajectoryBakeStatus.Current:
                label = "정상";
                break;
            case MeleeAttackTrajectoryBakeStatus.Stale:
                label = "오래됨";
                break;
            case MeleeAttackTrajectoryBakeStatus.Failed:
                label = "실패";
                break;
            default:
                label = "누락";
                break;
        }

        EditorGUILayout.HelpBox("상태: " + label + "\n" + message, type);
    }

    private static void DrawAttackIdValidation(MeleeComboDefinition combo)
    {
        string error = "Combo definition is missing.";
        if (combo != null && combo.TryGetStableAttackIds(out string[] attackIds, out error))
        {
            EditorGUILayout.HelpBox(
                "Attack ID 검증 완료: " + attackIds.Length + "개 / 콤보별 보석 저장 신원으로 사용 가능",
                MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            "Attack ID 검증 실패: " + (string.IsNullOrEmpty(error) ? "알 수 없는 오류" : error),
            MessageType.Error);
    }

    private static void DrawSlopeSummary(MeleeComboDefinition combo)
    {
        if (combo == null || combo.steps == null)
            return;

        int slashCount = 0;
        for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
        {
            MeleeComboStepData step = combo.steps[stepIndex];
            if (step.attackPhases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
            {
                AttackPhaseData phase = step.attackPhases[phaseIndex];
                if (!MeleeAttackVfxSlopeBakeUtility.TryGetSlashCueKey(phase, out string cueKey))
                    continue;

                slashCount++;
                string label = string.IsNullOrEmpty(step.attackName)
                    ? step.attackId
                    : step.attackName;
                if (step.attackPhases.Length > 1)
                    label += " / Phase " + (phaseIndex + 1);

                string value = phase.useBakedVfxSwingSlope
                    ? phase.ResolveVfxSwingSlope(phase.bakedVfxSwingSlopeDegrees)
                        .ToString("+0.###;-0.###;0") + " deg"
                    : "미베이크";
                string policy = phase.vfxSwingSettings.orientation
                    + " / " + phase.vfxSwingSettings.bakeMask;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(label, GUILayout.MinWidth(160f));
                EditorGUILayout.LabelField(cueKey, GUILayout.Width(110f));
                EditorGUILayout.LabelField(value, GUILayout.Width(80f));
                EditorGUILayout.LabelField(policy, GUILayout.MinWidth(220f));
                EditorGUILayout.EndHorizontal();
            }
        }

        if (slashCount == 0)
            EditorGUILayout.HelpBox("베기 VFX Cue가 없습니다.", MessageType.Warning);
    }

    private void ConfirmAndBake(MeleeComboDefinition combo)
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "공격 궤적 다시 베이크",
            "현재 콤보의 전체 WeaponTip 원시 궤적과 판정·VFX 파생 데이터를 원자적으로 다시 생성합니다. 계속할까요?",
            "다시 베이크",
            "취소");
        if (!confirmed)
            return;

        try
        {
            lastBakeReport = MeleeAttackVfxSlopeBakeUtility.BakeSelectedCombo(combo);
            serializedObject.Update();
            Repaint();
        }
        catch (Exception exception)
        {
            lastBakeReport = null;
            Debug.LogException(exception, combo);
            EditorUtility.DisplayDialog(
                "공격 궤적 베이크 실패",
                exception.Message,
                "확인");
        }
    }

    private static void DrawSeparator()
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
    }
}
