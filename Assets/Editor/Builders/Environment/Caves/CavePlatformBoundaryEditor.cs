using System;
using System.Linq;
using Overburst.Caves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(CavePlatformBoundary))]
public sealed class CavePlatformBoundaryEditor : Editor
{
    bool edit;
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.Add(new HelpBox("빨강: 막힌 경계 / 노랑: 연결 후보(기본 닫힘) / 초록: 실제 다리·계단이 연결되어 열림. 원본 프리팹 경계를 저장하면 생성용 사본과 기존 맵에도 반영됩니다.", HelpBoxMessageType.Info));
        var boundary = (CavePlatformBoundary)target;
        if (boundary.definition)
        {
            root.Add(new Button(() => PrefabStageUtility.OpenPrefab(AssetDatabase.GetAssetPath(boundary.definition.gameObject))) { text = "원본 플랫폼 경계 편집" });
            return root;
        }
        var toggle = new Toggle("Scene에서 경계점 조정");
        toggle.RegisterValueChangedCallback(e => { edit = e.newValue; SceneView.RepaintAll(); }); root.Add(toggle);
        root.Add(new Button(() => Frame(boundary)) { text = "위에서 경계 보기" });
        root.Add(new PropertyField(serializedObject.FindProperty("loops"), "경계 윤곽 (로컬 좌표)"));
        return root;
    }

    public static void Frame(CavePlatformBoundary boundary)
    {
        var points = boundary.Data.loops.SelectMany(l => l.points).Select(boundary.transform.TransformPoint).ToArray();
        if (points.Length == 0 || !SceneView.lastActiveSceneView) return;
        var bounds = new Bounds(points[0], Vector3.zero); foreach (var p in points) bounds.Encapsulate(p);
        SceneView.lastActiveSceneView.LookAt(bounds.center, Quaternion.Euler(90, 0, 0), Mathf.Max(bounds.size.x, bounds.size.z) * .7f, true, true);
        SceneView.lastActiveSceneView.drawGizmos = true;
    }

    void OnSceneGUI()
    {
        var boundary = (CavePlatformBoundary)target;
        if (!edit || boundary.definition) return;
        foreach (var loop in boundary.loops)
            for (int i = 0; i < loop.points.Length; i++)
            {
                var world = boundary.transform.TransformPoint(loop.points[i]);
                EditorGUI.BeginChangeCheck();
                Handles.color = Color.white;
                var next = Handles.FreeMoveHandle(world + Vector3.up * .15f, HandleUtility.GetHandleSize(world) * .035f, Vector3.zero, Handles.DotHandleCap);
                if (!EditorGUI.EndChangeCheck()) continue;
                Undo.RecordObject(boundary, "동굴 플랫폼 경계 조정");
                var local = boundary.transform.InverseTransformPoint(next - Vector3.up * .15f);
                local.y = loop.points[i].y; loop.points[i] = local;
                boundary.InvalidateCache();
                EditorUtility.SetDirty(boundary);
                PrefabUtility.RecordPrefabInstancePropertyModifications(boundary);
                if (boundary.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(boundary.gameObject.scene);
            }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
    static void Draw(CavePlatformBoundary boundary, GizmoType gizmoType)
    {
        var world = boundary.GetComponentInParent<CaveWorld>();
        var connections = world && world.generatedRoot ? world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>() : Array.Empty<CaveRigidConnection>();
        var oldZ = Handles.zTest; Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        foreach (var loop in boundary.Data.loops)
            for (int i = 0; i < loop.points.Length; i++)
            {
                var a = loop.points[i]; var b = loop.points[(i + 1) % loop.points.Length]; var mid = (a + b) * .5f;
                var wm = boundary.transform.TransformPoint(mid);
                Handles.color = new Color(1, .2f, .17f);
                foreach (var port in boundary.Data.ports)
                {
                    var d = mid - port.pivot;
                    float angle = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
                    float widthAngle = Mathf.Atan2(port.width * .5f, Mathf.Max(.5f, port.radius)) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.DeltaAngle(angle, port.heading)) <= port.halfAngle + widthAngle && Mathf.Abs(new Vector2(d.x, d.z).magnitude - port.radius) < 5)
                        Handles.color = new Color(1, .77f, .1f);
                }
                foreach (var connection in connections)
                    if ((connection.boundaryA == boundary || connection.boundaryB == boundary) && connection.Sample(wm, 10, 10, out _)) Handles.color = new Color(.15f, 1, .4f);
                Handles.DrawAAPolyLine(4, boundary.transform.TransformPoint(a) + Vector3.up * .15f, boundary.transform.TransformPoint(b) + Vector3.up * .15f);
            }
        for (int i = 0; i < boundary.Data.ports.Length; i++)
        {
            var port = boundary.Data.ports[i];
            bool connected = connections.Any(c => c.BoundaryOpen && ((c.boundaryA == boundary && c.portA == i) || (c.boundaryB == boundary && c.portB == i)));
            Handles.color = connected ? Color.green : Color.yellow;
            var position = boundary.transform.TransformPoint(port.Anchor(port.heading)) + Vector3.up * .5f;
            Handles.Label(position, $"{i + 1} · {(port.stone ? "계단" : "다리")} · {(connected ? "열림" : "연결 시 열림")}");
        }
        Handles.zTest = oldZ;
    }
}
