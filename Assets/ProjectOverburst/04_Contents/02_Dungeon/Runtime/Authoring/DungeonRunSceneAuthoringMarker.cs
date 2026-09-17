using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonRunSceneAuthoringMarker : MonoBehaviour
{
    [SerializeField, Min(1)] private int authoringVersion = 1;

    public int AuthoringVersion => authoringVersion;

    public void Configure(int version)
    {
        authoringVersion = Mathf.Max(1, version);
    }
}
