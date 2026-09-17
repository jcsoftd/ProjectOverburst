using UnityEngine;

[DefaultExecutionOrder(-32000)]
[DisallowMultipleComponent]
public sealed class DungeonTileShowroomMarker : MonoBehaviour
{
    [SerializeField]
    private int authoringVersion;

    [SerializeField]
    private int tileCount;

    public int AuthoringVersion => authoringVersion;
    public int TileCount => tileCount;

    public void Configure(int version, int authoredTileCount)
    {
        authoringVersion = version;
        tileCount = Mathf.Max(0, authoredTileCount);
    }

    private void Awake()
    {
        if (Application.isPlaying)
            gameObject.SetActive(false);
    }
}
