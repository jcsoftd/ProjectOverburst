using UnityEngine;

public class InventoryQuickSlotBindingController : MonoBehaviour
{
    private const int FirstQuickSlotKey = 4;
    private const int QuickSlotCount = 4;
    private readonly ConsumableItemData[] boundConsumables = new ConsumableItemData[QuickSlotCount];

    public event System.Action BindingsChanged;

    public bool Bind(int key, ItemData item)
    {
        int index = ToIndex(key);
        if (index < 0 || item == null || item.itemType != "Consumable" || !(item.baseData is ConsumableItemData consumableData))
            return false;

        ClearDuplicateBinding(index, consumableData);
        boundConsumables[index] = consumableData; // 슬롯 이동/정렬에 안전한 에셋 참조
        BindingsChanged?.Invoke();
        return true;
    }

    public ConsumableItemData GetBoundConsumable(int key)
    {
        int index = ToIndex(key);
        return index >= 0 ? boundConsumables[index] : null;
    }

    public bool Clear(int key)
    {
        int index = ToIndex(key);
        if (index < 0 || boundConsumables[index] == null)
            return false;

        boundConsumables[index] = null;
        BindingsChanged?.Invoke();
        return true;
    }

    public bool ClearConsumable(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return false;

        bool changed = false;
        string itemId = GetStableItemId(consumableData);
        for (int i = 0; i < boundConsumables.Length; i++)
        {
            ConsumableItemData bound = boundConsumables[i];
            if (bound == null)
                continue;

            if (bound == consumableData || GetStableItemId(bound) == itemId)
            {
                boundConsumables[i] = null;
                changed = true;
            }
        }

        if (changed)
            BindingsChanged?.Invoke();

        return changed;
    }

    public bool ClearMissingConsumables(PlayerInventory inventory)
    {
        if (inventory == null)
            return false;

        bool changed = false;
        for (int i = 0; i < boundConsumables.Length; i++)
        {
            ConsumableItemData bound = boundConsumables[i];
            if (bound == null || inventory.CountItemsByBaseData(bound) > 0)
                continue;

            boundConsumables[i] = null;
            changed = true;
        }

        if (changed)
            BindingsChanged?.Invoke();

        return changed;
    }

    public string GetBoundItemDisplayName(int key)
    {
        ConsumableItemData item = GetBoundConsumable(key);
        if (item == null)
            return "비어있음";

        return item.itemName;
    }

    private static int ToIndex(int key)
    {
        int index = key - FirstQuickSlotKey;
        return index >= 0 && index < QuickSlotCount ? index : -1;
    }

    private void ClearDuplicateBinding(int targetIndex, ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return;

        string itemId = GetStableItemId(consumableData);
        for (int i = 0; i < boundConsumables.Length; i++)
        {
            if (i == targetIndex || boundConsumables[i] == null)
                continue;

            if (boundConsumables[i] == consumableData || GetStableItemId(boundConsumables[i]) == itemId)
                boundConsumables[i] = null;
        }
    }

    private static string GetStableItemId(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(consumableData.targetBuffId))
            return consumableData.targetBuffId;

        return consumableData.name;
    }
}
