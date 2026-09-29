using UnityEngine;

// Project-owned reference map; the source Telegraph prefabs remain in ThirdParty.
[CreateAssetMenu(menuName = "OVERBURST/Enemies/Telegraph Visual Library")]
public sealed class EnemyTelegraphVisualLibrary : ScriptableObject
{
    [SerializeField] private GameObject cone;
    [SerializeField] private GameObject nova;
    [SerializeField] private GameObject rectangle;

    public GameObject Cone => cone;
    public GameObject Nova => nova;
    public GameObject Rectangle => rectangle;
}
