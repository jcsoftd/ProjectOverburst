using UnityEngine;

public enum FootstepMotionKind
{
    Walk = 0,
    Run = 1,
    Land = 2,
}

[CreateAssetMenu(menuName = "OVERBURST/Audio/Surface Profile", fileName = "SF_Surface")]
public sealed class SurfaceProfile : ScriptableObject
{
    [SerializeField] private string surfaceId = "Default";
    [SerializeField] private AudioClip[] walkClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] runClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] landingClips = System.Array.Empty<AudioClip>();
    [SerializeField, Range(0f, 1f)] private float volumeMin = 0.72f;
    [SerializeField, Range(0f, 1f)] private float volumeMax = 0.9f;
    [SerializeField, Range(0.5f, 1.5f)] private float pitchMin = 0.94f;
    [SerializeField, Range(0.5f, 1.5f)] private float pitchMax = 1.06f;

    public string SurfaceId => surfaceId;
    public float VolumeMin => Mathf.Min(volumeMin, volumeMax);
    public float VolumeMax => Mathf.Max(volumeMin, volumeMax);
    public float PitchMin => Mathf.Min(pitchMin, pitchMax);
    public float PitchMax => Mathf.Max(pitchMin, pitchMax);

    public void Configure(
        string configuredId,
        AudioClip[] configuredWalkClips,
        AudioClip[] configuredRunClips,
        AudioClip[] configuredLandingClips,
        Vector2 volumeRange,
        Vector2 pitchRange)
    {
        surfaceId = string.IsNullOrWhiteSpace(configuredId) ? name : configuredId;
        walkClips = configuredWalkClips ?? System.Array.Empty<AudioClip>();
        runClips = configuredRunClips ?? System.Array.Empty<AudioClip>();
        landingClips = configuredLandingClips ?? System.Array.Empty<AudioClip>();
        volumeMin = Mathf.Clamp01(Mathf.Min(volumeRange.x, volumeRange.y));
        volumeMax = Mathf.Clamp01(Mathf.Max(volumeRange.x, volumeRange.y));
        pitchMin = Mathf.Clamp(Mathf.Min(pitchRange.x, pitchRange.y), 0.5f, 1.5f);
        pitchMax = Mathf.Clamp(Mathf.Max(pitchRange.x, pitchRange.y), 0.5f, 1.5f);
    }

    public AudioClip PickClip(FootstepMotionKind kind)
    {
        AudioClip[] clips = kind == FootstepMotionKind.Run
            ? runClips
            : kind == FootstepMotionKind.Land
                ? landingClips
                : walkClips;
        if (clips == null || clips.Length == 0)
            return null;

        int start = Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip clip = clips[(start + i) % clips.Length];
            if (clip != null)
                return clip;
        }
        return null;
    }
}
