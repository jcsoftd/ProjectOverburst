using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonTileAuthoringMarker : MonoBehaviour
{
    [SerializeField] private GameObject sourcePrefab;
    [SerializeField] private bool directSourceTile;
    [SerializeField] private bool standaloneProjectTile;
    [SerializeField, Min(1)] private int authoringVersion = 1;
    [SerializeField] private bool exitTile;
    [SerializeField, Min(0)] private int walkableSurfaceCount;
    [SerializeField, Min(0)] private int stairRampCount;

    public GameObject SourcePrefab => sourcePrefab;
    public bool IsDirectSourceTile => directSourceTile;
    public bool IsStandaloneProjectTile => standaloneProjectTile;
    public int AuthoringVersion => authoringVersion;
    public bool IsExitTile => exitTile;
    public int WalkableSurfaceCount => walkableSurfaceCount;
    public int StairRampCount => stairRampCount;

    public void Configure(
        GameObject source,
        int version,
        bool isExitTile,
        int walkableCount,
        int rampCount,
        bool isDirectSourceTile = false,
        bool isStandaloneProjectCopy = false)
    {
        sourcePrefab = source;
        directSourceTile = isDirectSourceTile;
        standaloneProjectTile = isStandaloneProjectCopy;
        authoringVersion = Mathf.Max(1, version);
        exitTile = isExitTile;
        walkableSurfaceCount = Mathf.Max(0, walkableCount);
        stairRampCount = Mathf.Max(0, rampCount);
    }
}
