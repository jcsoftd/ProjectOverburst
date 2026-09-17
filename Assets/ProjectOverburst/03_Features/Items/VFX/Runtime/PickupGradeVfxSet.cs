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
    [SerializeField] private GameObject fallbackPrefab;
    [SerializeField] private PickupGradeVfxEntry[] entries;

    public GameObject GetPrefab(ItemGrade grade)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].grade == grade)
                    return entries[i].prefab; // null은 해당 등급 VFX 명시적 비활성
            }
        }

        return fallbackPrefab; // 기본 VFX
    }
}
