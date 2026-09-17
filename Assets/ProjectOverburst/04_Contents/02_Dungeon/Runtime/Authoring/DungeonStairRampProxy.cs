using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonStairRampProxy : MonoBehaviour
{
    [SerializeField] private string sourceHierarchyPath;
    [SerializeField, Min(0f)] private float slopeAngle;

    public string SourceHierarchyPath => sourceHierarchyPath;
    public float SlopeAngle => slopeAngle;

    public void Configure(string hierarchyPath, float angle)
    {
        sourceHierarchyPath = hierarchyPath ?? string.Empty;
        slopeAngle = Mathf.Max(0f, angle);
    }
}
