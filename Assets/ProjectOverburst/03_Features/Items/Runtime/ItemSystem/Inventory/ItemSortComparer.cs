using System.Collections.Generic;

public static class ItemSortComparer // 아이템 정렬
{
    public static readonly ItemSortMode[] Modes = // UI 순서
    {
        ItemSortMode.Default,
        ItemSortMode.Grade,
        ItemSortMode.Acquired,
        ItemSortMode.Price
    };

    public static readonly ItemSortMode[] InventoryModes = // 인벤토리 UI 순서
    {
        ItemSortMode.Grade,
        ItemSortMode.Acquired,
        ItemSortMode.Price,
        ItemSortMode.None
    };

    public static string GetDisplayName(ItemSortMode mode)
    {
        switch (mode)
        {
            case ItemSortMode.None:
                return "정렬X";
            case ItemSortMode.Grade:
                return "등급순";
            case ItemSortMode.Acquired:
                return "획득순";
            case ItemSortMode.Price:
                return "가격순";
            default:
                return "기본";
        }
    }

    public static string GetDescription(ItemSortMode mode)
    {
        if (mode == ItemSortMode.None)
            return "정렬 안 함";

        if (mode == ItemSortMode.Default)
            return "기본 = 무기/콤보 보석/기타 + 등급순";

        return "현재 정렬: " + GetDisplayName(mode);
    }

    public static string GetDirectionDisplayName(ItemSortDirection direction)
    {
        return direction == ItemSortDirection.Ascending ? "오름차순" : "내림차순";
    }

    public static ItemSortDirection ToggleDirection(ItemSortDirection direction)
    {
        return direction == ItemSortDirection.Ascending ? ItemSortDirection.Descending : ItemSortDirection.Ascending;
    }

    public static int GetModeIndex(ItemSortMode mode)
    {
        for (int i = 0; i < Modes.Length; i++)
        {
            if (Modes[i] == mode)
                return i;
        }

        return 0;
    }

    public static ItemSortMode GetModeByIndex(int index)
    {
        if (index < 0 || index >= Modes.Length)
            return ItemSortMode.Default;

        return Modes[index];
    }

    public static int GetInventoryModeIndex(ItemSortMode mode)
    {
        for (int i = 0; i < InventoryModes.Length; i++)
        {
            if (InventoryModes[i] == mode)
                return i;
        }

        return 0;
    }

    public static ItemSortMode GetInventoryModeByIndex(int index)
    {
        if (index < 0 || index >= InventoryModes.Length)
            return ItemSortMode.Grade;

        return InventoryModes[index];
    }

    public static void Sort(List<ItemData> items, int startIndex, int count, ItemSortMode mode, ItemSortDirection direction)
    {
        if (mode == ItemSortMode.None)
            return;

        if (items == null || count <= 1)
            return;

        int safeStart = System.Math.Max(0, startIndex); // 시작 보정
        int safeEnd = System.Math.Min(items.Count, safeStart + count); // 끝 보정
        if (safeEnd - safeStart <= 1)
            return;

        List<ItemData> sorted = new List<ItemData>(safeEnd - safeStart); // 정렬 범위
        for (int i = safeStart; i < safeEnd; i++)
        {
            if (mode == ItemSortMode.Acquired && items[i] != null)
                items[i].EnsureAcquisitionOrder(); // 구 아이템 보정

            sorted.Add(items[i]); // 참조 복사
        }

        sorted.Sort((left, right) => Compare(left, right, mode, direction)); // 공통 비교

        for (int i = 0; i < sorted.Count; i++)
            items[safeStart + i] = sorted[i]; // 참조 재배치
    }

    public static int Compare(ItemData left, ItemData right, ItemSortMode mode, ItemSortDirection direction)
    {
        if (left == null && right == null)
            return 0;
        if (left == null)
            return 1; // null 뒤
        if (right == null)
            return -1; // null 뒤

        switch (mode)
        {
            case ItemSortMode.None:
                return 0;
            case ItemSortMode.Grade:
                return CompareGradeMode(left, right, direction);
            case ItemSortMode.Acquired:
                return CompareAcquiredMode(left, right, direction);
            case ItemSortMode.Price:
                return ComparePriceMode(left, right, direction);
            default:
                return CompareDefaultMode(left, right, direction);
        }
    }

    private static int CompareDefaultMode(ItemData left, ItemData right, ItemSortDirection direction)
    {
        int result = CompareDefaultGroupPriority(left, right, direction); // 기본 그룹 우선
        if (result != 0)
            return result;

        result = CompareGrade(left, right, direction); // 방향별 등급
        if (result != 0)
            return result;

        return CompareName(left, right, direction);
    }

    private static int CompareGradeMode(ItemData left, ItemData right, ItemSortDirection direction)
    {
        int result = CompareGrade(left, right, direction); // 등급 우선
        if (result != 0)
            return result;

        result = CompareTypePriority(left, right, direction); // 종류 보조
        if (result != 0)
            return result;

        return CompareName(left, right);
    }

    private static int CompareAcquiredMode(ItemData left, ItemData right, ItemSortDirection direction)
    {
        int result = CompareAcquisitionOrder(left, right, direction); // 획득 우선
        if (result != 0)
            return result;

        result = CompareGrade(left, right, ItemSortDirection.Descending); // 등급 보조
        if (result != 0)
            return result;

        return CompareName(left, right);
    }

    private static int ComparePriceMode(ItemData left, ItemData right, ItemSortDirection direction)
    {
        int result = direction == ItemSortDirection.Ascending // 가격 우선
            ? GetSellPrice(left).CompareTo(GetSellPrice(right))
            : GetSellPrice(right).CompareTo(GetSellPrice(left));
        if (result != 0)
            return result;

        result = CompareGrade(left, right, direction); // 등급 보조
        if (result != 0)
            return result;

        result = CompareTypePriority(left, right, direction); // 종류 보조
        if (result != 0)
            return result;

        return CompareName(left, right);
    }

    private static int CompareTypePriority(ItemData left, ItemData right, ItemSortDirection direction)
    {
        return direction == ItemSortDirection.Ascending
            ? GetTypePriority(right).CompareTo(GetTypePriority(left))
            : GetTypePriority(left).CompareTo(GetTypePriority(right));
    }

    private static int CompareGrade(ItemData left, ItemData right, ItemSortDirection direction)
    {
        return direction == ItemSortDirection.Ascending
            ? ((int)left.grade).CompareTo((int)right.grade)
            : ((int)right.grade).CompareTo((int)left.grade);
    }

    private static int CompareDefaultGroupPriority(ItemData left, ItemData right, ItemSortDirection direction)
    {
        return direction == ItemSortDirection.Ascending
            ? GetDefaultGroupPriority(right).CompareTo(GetDefaultGroupPriority(left))
            : GetDefaultGroupPriority(left).CompareTo(GetDefaultGroupPriority(right));
    }

    private static int CompareAcquisitionOrder(ItemData left, ItemData right, ItemSortDirection direction)
    {
        left.EnsureAcquisitionOrder();
        right.EnsureAcquisitionOrder();

        return direction == ItemSortDirection.Ascending
            ? left.acquisitionOrder.CompareTo(right.acquisitionOrder)
            : right.acquisitionOrder.CompareTo(left.acquisitionOrder);
    }

    private static int CompareName(ItemData left, ItemData right)
    {
        int result = string.Compare(GetItemName(left), GetItemName(right), System.StringComparison.CurrentCulture); // 이름 보조
        if (result != 0)
            return result;

        return string.Compare(GetRuntimeKey(left), GetRuntimeKey(right), System.StringComparison.Ordinal); // id 보조
    }

    private static int CompareName(ItemData left, ItemData right, ItemSortDirection direction)
    {
        return direction == ItemSortDirection.Ascending
            ? CompareName(right, left)
            : CompareName(left, right);
    }

    private static int GetTypePriority(ItemData item)
    {
        if (item == null)
            return int.MaxValue;

        switch (item.itemType)
        {
            case "Weapon":
                return 0;
            case "ComboGem":
                return 1;
            case "Bag":
                return 2;
            case "Consumable":
                return 3;
            case "Currency":
                return 4;
            case "QuestItem":
                return 5;
            case "Junk":
                return 6;
            default:
                return 7;
        }
    }

    private static int GetDefaultGroupPriority(ItemData item)
    {
        if (item == null)
            return int.MaxValue;

        switch (item.itemType)
        {
            case "Weapon":
                return 0;
            case "ComboGem":
                return 1;
            default:
                return 2;
        }
    }

    private static int GetSellPrice(ItemData item)
    {
        return item != null && item.baseData != null ? item.baseData.sellPrice : 0;
    }

    private static string GetItemName(ItemData item)
    {
        return item != null ? item.itemName : string.Empty;
    }

    private static string GetRuntimeKey(ItemData item)
    {
        return item != null ? item.runtimeInstanceId : string.Empty;
    }
}
