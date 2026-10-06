using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Combat/Perfect Parry Contact")]
public sealed class PerfectParryContactProfile : ScriptableObject
{
    [Tooltip("Disable to restore the complete legacy perfect-parry presentation.")]
    public bool enhancedPresentation = true;
    public GameObject mainPrefab, additionalPrefab;
    public AudioClip impact, ring, low;
    [Range(0f, 1f)] public float impactVolume = .90f, ringVolume = .50f, lowVolume = .65f;
    [Min(.1f)] public float readableSeconds = .30f;
    [Min(.1f)] public float totalSeconds = .75f;
    [Min(0f)] public float sparkDelay = .08f;
    [Min(.1f)] public float strokeSize = 1.10f;
    [Range(0f, 1f)] public float bladeMin = .45f, bladeMax = .85f;
    public Vector3 localOffset;
    public bool upswingAfterimage = true;
    public Material upswingAfterimageMaterial;
    [Range(0f, 1f)] public float upswingAfterimageOpacity = .42f;
    [Min(.1f)] public float upswingAfterimageLifetime = .28f;
    public bool IsReady => enhancedPresentation && mainPrefab != null && additionalPrefab != null
        && mainPrefab.GetComponent<PerfectParryContactVfx>() != null
        && additionalPrefab.GetComponent<PerfectParryContactVfx>() != null
        && impact != null && ring != null && low != null
        && (!upswingAfterimage || upswingAfterimageMaterial != null);
    private static PerfectParryContactProfile cached;
    public static PerfectParryContactProfile Current => cached != null ? cached
        : cached = Resources.Load<PerfectParryContactProfile>("Combat/VFX/PerfectParryContactProfile");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { cached = null; }
}
