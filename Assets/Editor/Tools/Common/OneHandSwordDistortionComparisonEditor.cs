using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(OneHandSwordDistortionStyle))]
public sealed class OneHandSwordDistortionComparisonEditor : Editor
{
    private const string StylePath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/VFX/Shockwaves/OHS_DistortionComparison.asset";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("다음 한손검 공격부터 적용됩니다. A는 퍼지는 파면, B는 실제 검끝의 왜곡 궤적입니다. 강공용 원형 프리팹은 공통입니다.", MessageType.Info);
        if (GUILayout.Button("A · 충격파형")) Apply((OneHandSwordDistortionStyle)target, SwordDistortionVersion.PressureWave);
        if (GUILayout.Button("B · 검끝 Trail형")) Apply((OneHandSwordDistortionStyle)target, SwordDistortionVersion.BladeTrail);
    }

    [MenuItem("Tools/OVERBURST/VFX/한손검 비교/A 충격파형")]
    private static void Wave() => Select(SwordDistortionVersion.PressureWave);

    [MenuItem("Tools/OVERBURST/VFX/한손검 비교/B 검끝 Trail형")]
    private static void Trail() => Select(SwordDistortionVersion.BladeTrail);

    private static void Select(SwordDistortionVersion version)
    {
        var style = AssetDatabase.LoadAssetAtPath<OneHandSwordDistortionStyle>(StylePath);
        if (!style) { Debug.LogError("한손검 비교 설정을 찾을 수 없습니다: " + StylePath); return; }
        Apply(style, version);
        Selection.activeObject = style;
    }

    private static void Apply(OneHandSwordDistortionStyle style, SwordDistortionVersion version)
    {
        Undo.RecordObject(style, "한손검 왜곡 비교 버전 변경");
        style.version = version;
        EditorUtility.SetDirty(style);
        if (!EditorApplication.isPlaying) AssetDatabase.SaveAssetIfDirty(style);
    }
}
