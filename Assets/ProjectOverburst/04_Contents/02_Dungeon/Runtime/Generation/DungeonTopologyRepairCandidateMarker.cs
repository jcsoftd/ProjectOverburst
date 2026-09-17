using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class DungeonTopologyRepairCandidateMarker : MonoBehaviour
{
    [SerializeField] private GameObject sourceFamilyPrefab;
    [SerializeField] private string topologyId;
    [SerializeField, Min(1)] private int authoringVersion = 1;

    public GameObject SourceFamilyPrefab => sourceFamilyPrefab;
    public string TopologyId => topologyId?.Trim() ?? string.Empty;
    public int AuthoringVersion => Mathf.Max(1, authoringVersion);

    public void Configure(
        GameObject configuredSourceFamilyPrefab,
        string configuredTopologyId,
        int configuredAuthoringVersion)
    {
        sourceFamilyPrefab = configuredSourceFamilyPrefab;
        topologyId = configuredTopologyId?.Trim() ?? string.Empty;
        authoringVersion = Mathf.Max(1, configuredAuthoringVersion);
    }
}
