using UnityEngine;

[System.Serializable]
public class PickupGradeVfxEntry // 등급별 VFX
{
    public ItemGrade grade;
    public GameObject prefab;
}

[CreateAssetMenu(fileName = "PickupGradeVfxSet", menuName = "VFX/Pickup Grade VFX Set")]
public class PickupGradeVfxSet : ScriptableObject // 픽업 VFX 매핑
{
    public const string DefaultResourcePath = "Items/VFX/PickupGradeVfxSet";
    private static PickupGradeVfxSet defaultSet;
    private static Material emergencyMaterial;
    public static PickupGradeVfxSet Default => defaultSet != null ? defaultSet
        : defaultSet = Resources.Load<PickupGradeVfxSet>(DefaultResourcePath);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDefault() { defaultSet = null; emergencyMaterial = null; }

    [SerializeField] private GameObject fallbackPrefab;
    [SerializeField] private PickupGradeVfxEntry[] entries;

    public GameObject GetPrefab(ItemGrade grade)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].grade == grade)
                    return entries[i].prefab != null ? entries[i].prefab : fallbackPrefab;
            }
        }

        return fallbackPrefab; // 기본 VFX
    }

    private GameObject ExactPrefab(ItemGrade grade)
    {
        if (entries != null)
            foreach (var entry in entries)
                if (entry != null && entry.grade == grade && entry.prefab != null) return entry.prefab;
        return null;
    }

    public static GameObject SpawnRequired(PickupGradeVfxSet preferred, ItemGrade grade, Transform anchor)
    {
        if (anchor == null) throw new System.ArgumentNullException(nameof(anchor));
        var prefab = preferred != null ? preferred.ExactPrefab(grade) : null;
        if (prefab == null && Default != null) prefab = Default.ExactPrefab(grade);
        if (prefab != null) return VfxPrefabFactory.SpawnFollowing(prefab, anchor);

        // Even broken/missing optional authoring cannot silently remove the loot grade signal.
        var effect = new GameObject("LootGradeFallback_" + grade);
        var line = effect.AddComponent<LineRenderer>();
        if (emergencyMaterial == null)
            emergencyMaterial = new Material(Shader.Find("Sprites/Default")) { name = "LootGradeFallback" };
        line.sharedMaterial = emergencyMaterial;
        line.useWorldSpace = false; line.loop = true; line.positionCount = 48; line.widthMultiplier = .065f;
        line.startColor = line.endColor = GradeConfig.GetGradeColor(grade);
        for (int i = 0; i < line.positionCount; i++)
        {
            float a = i * Mathf.PI * 2 / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * .42f, .08f, Mathf.Sin(a) * .42f));
        }
        effect.AddComponent<VfxFollowTarget>().Initialize(anchor, Vector3.zero, false, true);
        return effect;
    }
}
