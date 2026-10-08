using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MainTownPlacementAnchor))]
public sealed class MainTownPlacementAnchorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var anchor = (MainTownPlacementAnchor)target;
        EditorGUILayout.HelpBox("이 오브젝트의 위치와 회전을 바꾸면 연결된 NPC·서비스도 이동합니다. Scene 뷰의 Gizmos를 켜세요. 마커는 Game 뷰와 Player에 표시되지 않습니다.", MessageType.Info);
        if (GUILayout.Button("지형 높이에 맞추기")) anchor.SnapToTerrain();
        if (GUILayout.Button("연결된 배치 선택") && anchor.Placement != null)
            Selection.activeTransform = anchor.Placement;
    }
}
