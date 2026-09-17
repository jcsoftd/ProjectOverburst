#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class MeleeHitFeedbackSetupUtility
{
    private const string RootFolder = "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/Feedback";
    private const string OneHandComboPath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/Combos/OneHandSwordPrimaryCombo.asset";
    private const string GreatswordComboPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Combos/GreatswordComboSet01.asset";

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Melee Hit Feedback")]
    public static void SetupFromMenu()
    {
        Setup(false);
        Debug.Log("[MeleeHitFeedbackSetup] 3타 히트 스톱·카메라 프로필 연결 완료");
    }

    [MenuItem("OVERBURST/Codex/Setup/Combat/Reset Melee Hit Feedback Defaults")]
    public static void ResetFromMenu()
    {
        if (!EditorUtility.DisplayDialog("근접 타격 피드백 초기화", "기존 프로필 수동 튜닝을 시작값으로 덮어씁니다. 계속할까요?", "초기화", "취소"))
            return;
        Setup(true);
        Debug.Log("[MeleeHitFeedbackSetup] 명시적 기본값 초기화 완료");
    }

    public static void RunOnceFromCommandLine()
    {
        Setup(false);
        Debug.Log("[MeleeHitFeedbackSetup] COMMAND_LINE_OK");
    }

    private static void Setup(bool resetExisting)
    {
        EnsureFolder(RootFolder);

        CombatHitFeedbackProfile[] oneHandProfiles =
        {
            EnsureProfile("OHS_Hit01", 0.020f, 0.070f, 0.035f, 0.15f, resetExisting),
            EnsureProfile("OHS_Hit02", 0.025f, 0.080f, 0.045f, 0.20f, resetExisting),
            EnsureProfile("OHS_Hit03", 0.045f, 0.100f, 0.075f, 0.35f, resetExisting)
        };
        CombatHitFeedbackProfile[] greatswordProfiles =
        {
            EnsureProfile("GRS_Hit01", 0.035f, 0.090f, 0.055f, 0.25f, resetExisting),
            EnsureProfile("GRS_Hit02", 0.040f, 0.100f, 0.070f, 0.30f, resetExisting),
            EnsureProfile("GRS_Hit03", 0.055f, 0.120f, 0.100f, 0.45f, resetExisting)
        };

        WireComboProfiles(OneHandComboPath, oneHandProfiles);
        WireComboProfiles(GreatswordComboPath, greatswordProfiles);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static CombatHitFeedbackProfile EnsureProfile(
        string name,
        float hitStopDuration,
        float cameraDuration,
        float cameraPositionAmplitude,
        float cameraRollAmplitude,
        bool resetExisting)
    {
        string path = RootFolder + "/" + name + ".asset";
        CombatHitFeedbackProfile profile = AssetDatabase.LoadAssetAtPath<CombatHitFeedbackProfile>(path);
        bool created = false;
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<CombatHitFeedbackProfile>();
            AssetDatabase.CreateAsset(profile, path);
            created = true;
        }

        if (!created && !resetExisting)
            return profile; // 기존 수동 튜닝 보존

        SerializedObject serialized = new SerializedObject(profile);
        serialized.FindProperty("hitStopDuration").floatValue = hitStopDuration;
        serialized.FindProperty("hitStopTimeScale").floatValue = 0.05f;
        serialized.FindProperty("cameraDuration").floatValue = cameraDuration;
        serialized.FindProperty("cameraPositionAmplitude").floatValue = cameraPositionAmplitude;
        serialized.FindProperty("cameraRollAmplitude").floatValue = cameraRollAmplitude;
        serialized.FindProperty("criticalStrengthMultiplier").floatValue = 1.2f;
        serialized.FindProperty("maximumHitStopDuration").floatValue = 0.06f;
        serialized.FindProperty("cameraKickReturnRatio").floatValue = 0.72f;
        serialized.FindProperty("cameraMicroShakeDuration").floatValue = 0.045f;
        serialized.FindProperty("cameraMicroShakeAmplitude").floatValue = 0.12f;
        serialized.FindProperty("cameraPriority").floatValue = 1f;
        serialized.FindProperty("cameraPositionSafetyLimit").floatValue = 0.16f;
        serialized.FindProperty("cameraRollSafetyLimit").floatValue = 3f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void WireComboProfiles(string comboPath, CombatHitFeedbackProfile[] profiles)
    {
        MeleeComboDefinition combo = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(comboPath);
        if (combo == null)
            throw new InvalidOperationException("콤보 에셋을 찾을 수 없습니다: " + comboPath);

        SerializedObject serialized = new SerializedObject(combo);
        SerializedProperty steps = serialized.FindProperty("steps");
        if (steps == null || steps.arraySize != profiles.Length)
            throw new InvalidOperationException("3타 콤보 구성이 아닙니다: " + comboPath);

        for (int stepIndex = 0; stepIndex < steps.arraySize; stepIndex++)
        {
            SerializedProperty phases = steps.GetArrayElementAtIndex(stepIndex)
                .FindPropertyRelative("attackPhases");
            for (int phaseIndex = 0; phaseIndex < phases.arraySize; phaseIndex++)
            {
                SerializedProperty profileProperty = phases.GetArrayElementAtIndex(phaseIndex)
                    .FindPropertyRelative("impact")
                    .FindPropertyRelative("hitFeedbackProfile");
                profileProperty.objectReferenceValue = profiles[stepIndex];
            }
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(combo);
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
