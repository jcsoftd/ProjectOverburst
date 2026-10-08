using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>Editor-only placement handle. The linked service remains outside the EditorOnly hierarchy.</summary>
[ExecuteAlways]
public sealed class MainTownPlacementAnchor : MonoBehaviour
{
    [SerializeField] private string locationLabel;
    [SerializeField] private Transform placement;
    [SerializeField] private Color color = new Color(0.2f, 0.85f, 1f);
    [SerializeField, Min(0.2f)] private float displayRadius = 1f;
    public string LocationLabel => locationLabel;
    public Transform Placement => placement;

#if UNITY_EDITOR
    private void OnEnable()
    {
        EditorApplication.update -= ApplyPlacement;
        EditorApplication.update += ApplyPlacement;
    }

    private void OnDisable() => EditorApplication.update -= ApplyPlacement;
    private void OnDestroy() => EditorApplication.update -= ApplyPlacement;

    public void Configure(string label, Transform target, Color tint, float radius)
    {
        locationLabel = label; placement = target; color = tint; displayRadius = radius;
        ApplyPlacement();
    }

    public void ApplyPlacement()
    {
        if (this == null || !isActiveAndEnabled || EditorApplication.isPlayingOrWillChangePlaymode
            || placement == null || placement.gameObject.scene != gameObject.scene) return;
        if (placement.position == transform.position && placement.rotation == transform.rotation) return;
        placement.SetPositionAndRotation(transform.position, transform.rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(placement);
        if (gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    public void SnapToTerrain()
    {
        foreach (var terrain in Terrain.activeTerrains)
        {
            if (terrain.gameObject.scene != gameObject.scene) continue;
            var local = transform.position - terrain.transform.position;
            var size = terrain.terrainData.size;
            if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z) continue;
            Undo.RecordObject(transform, "Snap town location to terrain");
            var p = transform.position;
            p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
            transform.position = p;
            ApplyPlacement();
            break;
        }
    }

    private void OnDrawGizmos()
    {
        if (Camera.current == null || Camera.current.cameraType != CameraType.SceneView) return;
        Handles.color = color;
        var p = transform.position + Vector3.up * 0.08f;
        Handles.DrawWireDisc(p, Vector3.up, displayRadius);
        Handles.DrawLine(p, p + Vector3.up * 2.3f);
        Handles.ArrowHandleCap(0, p, transform.rotation, displayRadius, EventType.Repaint);
        Handles.Label(p + Vector3.up * 2.5f, locationLabel, EditorStyles.whiteBoldLabel);
    }
#endif
}
