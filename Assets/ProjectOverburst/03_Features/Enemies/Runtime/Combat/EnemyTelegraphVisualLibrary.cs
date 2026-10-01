using UnityEngine;

// Project-owned reference map; the source Telegraph prefabs remain in ThirdParty.
[CreateAssetMenu(menuName = "OVERBURST/Enemies/Telegraph Visual Library")]
public sealed class EnemyTelegraphVisualLibrary : ScriptableObject
{
    [SerializeField] private GameObject cone;
    [SerializeField] private GameObject nova;
    [SerializeField] private GameObject rectangle;
    [SerializeField] private Material parryGlint;
    [SerializeField] private GameObject groundImpact;
    [SerializeField] private AudioClip strongRelease;
    [SerializeField] private AudioClip groundImpactSound;
    [Tooltip("패링 성공 중심에 재생하는 무지개 렌즈 플레어.")]
    [UnityEngine.Serialization.FormerlySerializedAs("parryShockwave")]
    [SerializeField] private GameObject parrySuccess;

    public GameObject Cone => cone;
    public GameObject Nova => nova;
    public GameObject Rectangle => rectangle;
    public Material ParryGlint => parryGlint;
    public GameObject GroundImpact => groundImpact;
    public AudioClip StrongRelease => strongRelease;
    public AudioClip GroundImpactSound => groundImpactSound;
    public GameObject ParrySuccess => parrySuccess;
}
