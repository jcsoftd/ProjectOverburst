using UnityEngine;
using System.Collections.Generic;

public enum BagRandomOptionType
{
    MoveSpeedPercent,
    MaxStamina,
    MaxHp
}

[System.Serializable]
public class BagRandomOptionRoll
{
    public BagRandomOptionType optionType; // 옵션 종류
    public float value; // 실제 롤 수치
}

public static class BagRandomOptionRoller
{
    private static readonly BagRandomOptionType[] RollableOptions =
    {
        BagRandomOptionType.MoveSpeedPercent,
        BagRandomOptionType.MaxStamina,
        BagRandomOptionType.MaxHp
    };

    public static List<BagRandomOptionRoll> Roll(ItemGrade grade)
    {
        int optionCount = GetOptionCount(grade);
        List<BagRandomOptionRoll> rolls = new List<BagRandomOptionRoll>(optionCount);
        if (optionCount <= 0)
            return rolls;

        List<BagRandomOptionType> pool = new List<BagRandomOptionType>(RollableOptions);
        for (int i = 0; i < optionCount && pool.Count > 0; i++)
        {
            int selectedIndex = Random.Range(0, pool.Count);
            BagRandomOptionType optionType = pool[selectedIndex];
            pool.RemoveAt(selectedIndex);

            rolls.Add(new BagRandomOptionRoll
            {
                optionType = optionType,
                value = RollValue(optionType, grade)
            });
        }

        return rolls;
    }

    public static int GetOptionCount(ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon:
            case ItemGrade.Rare:
            case ItemGrade.Epic:
                return 1;
            case ItemGrade.Legendary:
            case ItemGrade.Artifact:
                return 2;
            case ItemGrade.Mythic:
                return 3;
            case ItemGrade.Common:
            case ItemGrade.Cursed:
            default:
                return 0;
        }
    }

    public static bool TryGetValueRange(BagRandomOptionType optionType, ItemGrade grade, out float minValue, out float maxValue)
    {
        minValue = 0f;
        maxValue = 0f;

        switch (optionType)
        {
            case BagRandomOptionType.MoveSpeedPercent:
                return TryGetMoveSpeedRange(grade, out minValue, out maxValue);
            case BagRandomOptionType.MaxStamina:
            case BagRandomOptionType.MaxHp:
                return TryGetFlatResourceRange(grade, out minValue, out maxValue);
            default:
                return false;
        }
    }

    private static float RollValue(BagRandomOptionType optionType, ItemGrade grade)
    {
        if (!TryGetValueRange(optionType, grade, out float minValue, out float maxValue))
            return 0f;

        if (optionType == BagRandomOptionType.MoveSpeedPercent)
            return Random.Range(minValue, maxValue);

        int minInt = Mathf.RoundToInt(minValue);
        int maxInt = Mathf.RoundToInt(maxValue);
        return Random.Range(minInt, maxInt + 1);
    }

    private static bool TryGetMoveSpeedRange(ItemGrade grade, out float minValue, out float maxValue)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon:
                minValue = 3f;
                maxValue = 6f;
                return true;
            case ItemGrade.Rare:
                minValue = 5f;
                maxValue = 8f;
                return true;
            case ItemGrade.Epic:
                minValue = 7f;
                maxValue = 11f;
                return true;
            case ItemGrade.Legendary:
                minValue = 10f;
                maxValue = 14f;
                return true;
            case ItemGrade.Artifact:
                minValue = 13f;
                maxValue = 17f;
                return true;
            case ItemGrade.Mythic:
                minValue = 16f;
                maxValue = 20f;
                return true;
            default:
                minValue = 0f;
                maxValue = 0f;
                return false;
        }
    }

    private static bool TryGetFlatResourceRange(ItemGrade grade, out float minValue, out float maxValue)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon:
                minValue = 3f;
                maxValue = 7f;
                return true;
            case ItemGrade.Rare:
                minValue = 6f;
                maxValue = 11f;
                return true;
            case ItemGrade.Epic:
                minValue = 10f;
                maxValue = 15f;
                return true;
            case ItemGrade.Legendary:
                minValue = 14f;
                maxValue = 20f;
                return true;
            case ItemGrade.Artifact:
                minValue = 19f;
                maxValue = 25f;
                return true;
            case ItemGrade.Mythic:
                minValue = 24f;
                maxValue = 30f;
                return true;
            default:
                minValue = 0f;
                maxValue = 0f;
                return false;
        }
    }
}

[CreateAssetMenu(fileName = "NewBag", menuName = "Items/Bag")]
public class BagItemData : BaseItemData
{
    [Header("가방 정보")]
    public int level = 1;                        // 가방 등급 단계 (1~8)
    public ItemGrade defaultGrade = ItemGrade.Common; // 기본 등급
    public int additionalSlots;                  // 추가 슬롯 수
}
