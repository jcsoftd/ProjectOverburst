using UnityEngine;

[DisallowMultipleComponent]
public sealed class MeleeElementSlashPlaybackSettings : MonoBehaviour
{
    [SerializeField] private bool overrideStartOffsetSeconds;
    [SerializeField, Min(0f)] private float startOffsetSeconds;

    public bool OverrideStartOffsetSeconds => overrideStartOffsetSeconds;
    public float StartOffsetSeconds => Mathf.Max(0f, startOffsetSeconds);

    public float ResolveStartOffsetSeconds(float fallbackSeconds)
    {
        return overrideStartOffsetSeconds
            ? StartOffsetSeconds
            : Mathf.Max(0f, fallbackSeconds);
    }
}
