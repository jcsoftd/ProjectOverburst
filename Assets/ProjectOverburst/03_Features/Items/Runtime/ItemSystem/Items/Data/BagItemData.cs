using UnityEngine;
using System.Collections.Generic;

public enum BagRandomOptionType
{
    MoveSpeedPercent,
    MaxStamina, // 2026-09-30 스태미너 삭제. 구 저장 호환용 값이라 순서를 바꾸지 않는다. 굴리지 않고, 불러올 때 MaxHp로 바꾼다.
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
        BagRandomOptionType.MaxHp
    };

    // 저장된 스태미너 옵션을 최대 체력으로 바꾼다. 스태미너와 체력 범위가 같아 수치는 그대로 쓰고,
    // 같은 가방에 체력 옵션이 이미 있으면 한 줄로 합친다. 합쳐져 옵션 수가 모자라면 빠진 종류만 새로 굴린다.
    public static bool MigrateLegacyOptions(List<BagRandomOptionRoll> options, ItemGrade grade)
    {
        if (options == null)
            return false;

        BagRandomOptionRoll hp = null;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] != null && options[i].optionType == BagRandomOptionType.MaxHp)
            {
                hp = options[i];
                break;
            }
        }

        bool changed = false;
        for (int i = options.Count - 1; i >= 0; i--)
        {
            BagRandomOptionRoll option = options[i];
            if (option == null || option.optionType != BagRandomOptionType.MaxStamina)
                continue;

            changed = true;
            if (hp != null)
            {
                hp.value += Mathf.Max(0f, option.value);
                options.RemoveAt(i);
            }
            else
            {
                option.optionType = BagRandomOptionType.MaxHp;
                hp = option;
            }
        }

        if (changed)
            FillMissing(options, grade);
        return changed;
    }

    private static void FillMissing(List<BagRandomOptionRoll> options, ItemGrade grade)
    {
        int expected = GetOptionCount(grade);
        for (int t = 0; t < RollableOptions.Length && options.Count < expected; t++)
        {
            BagRandomOptionType type = RollableOptions[t];
            bool present = false;
            for (int i = 0; i < options.Count; i++)
                present |= options[i] != null && options[i].optionType == type;
            if (!present)
                options.Add(new BagRandomOptionRoll { optionType = type, value = RollValue(type, grade) });
        }
    }

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
        // 옵션 종류가 이동속도·최대 체력 둘뿐이라 신화도 2개까지만 굴린다.
        return Mathf.Min(GetGradeOptionCount(grade), RollableOptions.Length);
    }

    private static int GetGradeOptionCount(ItemGrade grade)
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
