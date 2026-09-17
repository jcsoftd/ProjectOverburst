using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DropTableEntry
{
    public BaseItemData itemData;
    [Range(0f, 1f)] public float dropChance = 1f;
    public int minStack = 1;
    public int maxStack = 1;
    public int minLevel = 1;
    public int maxLevel = 4;
    public ItemGrade minGrade = ItemGrade.Common;
    public ItemGrade maxGrade = ItemGrade.Mythic;
    public bool useVtpGradeRoll = true;

    public bool ShouldDrop()
    {
        return itemData != null && Random.value <= Mathf.Clamp01(dropChance); // 데이터와 확률 확인
    }

    public ItemData CreateItem()
    {
        int stack = Random.Range(Mathf.Max(1, minStack), Mathf.Max(minStack, maxStack) + 1); // 스택 롤
        return WorldItemDropFactory.CreateRuntimeItem(itemData, minLevel, maxLevel, minGrade, maxGrade, useVtpGradeRoll, stack); // 고유 인스턴스 생성
    }
}

[CreateAssetMenu(fileName = "NewDropTable", menuName = "Drops/Drop Table")]
public class DropTable : ScriptableObject
{
    [SerializeField] private DropTableEntry[] entries;

    public List<ItemData> RollDrops()
    {
        List<ItemData> results = new List<ItemData>(); // 드랍 결과
        if (entries == null)
            return results; // 빈 테이블

        for (int i = 0; i < entries.Length; i++)
        {
            DropTableEntry entry = entries[i];
            if (entry == null || !entry.ShouldDrop())
                continue; // 드랍 실패 또는 빈 항목

            ItemData item = entry.CreateItem();
            if (item != null)
                results.Add(item); // 결과 추가
        }

        return results;
    }
}
