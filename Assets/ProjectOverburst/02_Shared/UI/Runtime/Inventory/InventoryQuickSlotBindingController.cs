using UnityEngine;

public class InventoryQuickSlotBindingController : MonoBehaviour
{
    public const int FirstKey = 1;
    public const int SlotCount = 7;

    private readonly ConsumableItemData[] boundConsumables = new ConsumableItemData[SlotCount];
    private readonly string[] boundFlaskIds = new string[SlotCount];
    private readonly IQuickSlotSkill[] boundSkills = new IQuickSlotSkill[SlotCount];
    private bool legacyFlasksImported;

    public event System.Action BindingsChanged;

    // Preserve the old 4-6 flask loadout when the player actor first becomes available.
    public void ImportLegacyFlasks()
    {
        if (legacyFlasksImported)
            return;
        PlayerFlaskController flasks = PlayerFlaskController.Current;
        if (flasks == null)
            return;

        legacyFlasksImported = true;
        for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
        {
            ItemData item = flasks.GetItem(i);
            int index = PlayerFlaskController.FirstKey - FirstKey + i;
            if (item != null && IsEmpty(index))
                boundFlaskIds[index] = item.runtimeInstanceId;
        }
        BindingsChanged?.Invoke();
    }

    public bool Bind(int key, ItemData item)
    {
        int index = ToIndex(key);
        if (index < 0 || item == null || item.itemType != "Consumable")
            return false;

        if (item.baseData is FlaskItemData)
        {
            FlaskInstanceState state = FlaskRuntime.State(item);
            if (state == null || state.equippedSlot < 0)
                return false;
            int previousKey = GetFlaskKey(item);
            if (previousKey != 0 && previousKey != key && !string.IsNullOrEmpty(boundFlaskIds[index]))
            {
                boundFlaskIds[previousKey - FirstKey] = boundFlaskIds[index];
                Set(index, null, item.runtimeInstanceId, null);
                return true;
            }
            ClearDuplicateFlask(index, item.runtimeInstanceId);
            Set(index, null, item.runtimeInstanceId, null);
            return true;
        }

        if (!(item.baseData is ConsumableItemData consumableData))
            return false;
        ClearDuplicateConsumable(index, consumableData);
        Set(index, consumableData, null, null);
        return true;
    }

    public int FindFreeFlaskKey()
    {
        int[] preferred = { 4, 5, 6, 1, 2, 3, 7 };
        foreach (int key in preferred)
            if (IsEmpty(key - FirstKey)) return key;
        return 0;
    }

    public int GetFlaskKey(ItemData item)
    {
        if (item == null) return 0;
        for (int i = 0; i < SlotCount; i++)
            if (boundFlaskIds[i] == item.runtimeInstanceId) return i + FirstKey;
        return 0;
    }

    public bool BindSkill(int key, IQuickSlotSkill skill)
    {
        int index = ToIndex(key);
        if (index < 0 || skill == null || (skill is Object owner && owner == null))
            return false;
        for (int i = 0; i < SlotCount; i++)
            if (i != index && ReferenceEquals(boundSkills[i], skill))
                ClearAt(i);
        Set(index, null, null, skill);
        return true;
    }

    public ConsumableItemData GetBoundConsumable(int key)
    {
        int index = ToIndex(key);
        return index >= 0 ? boundConsumables[index] : null;
    }

    public ItemData GetBoundFlask(int key)
    {
        int index = ToIndex(key);
        if (index < 0 || string.IsNullOrEmpty(boundFlaskIds[index]))
            return null;
        PlayerFlaskController flasks = PlayerFlaskController.Current;
        if (flasks == null)
            return null;
        for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
        {
            ItemData item = flasks.GetItem(i);
            if (item != null && item.runtimeInstanceId == boundFlaskIds[index])
                return item;
        }
        return null;
    }

    public IQuickSlotSkill GetBoundSkill(int key)
    {
        int index = ToIndex(key);
        if (index < 0)
            return null;
        IQuickSlotSkill skill = boundSkills[index];
        if (skill is Object owner && owner == null)
        {
            ClearAt(index);
            BindingsChanged?.Invoke();
            return null;
        }
        return skill;
    }

    public bool Clear(int key)
    {
        int index = ToIndex(key);
        if (index < 0 || IsEmpty(index))
            return false;
        ClearAt(index);
        BindingsChanged?.Invoke();
        return true;
    }

    public bool ClearFlask(ItemData item)
    {
        if (item == null)
            return false;
        bool changed = false;
        for (int i = 0; i < SlotCount; i++)
            if (boundFlaskIds[i] == item.runtimeInstanceId)
            {
                ClearAt(i);
                changed = true;
            }
        if (changed) BindingsChanged?.Invoke();
        return changed;
    }

    public bool ClearConsumable(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return false;
        bool changed = false;
        string itemId = GetStableItemId(consumableData);
        for (int i = 0; i < SlotCount; i++)
        {
            ConsumableItemData bound = boundConsumables[i];
            if (bound == null || (bound != consumableData && GetStableItemId(bound) != itemId))
                continue;
            ClearAt(i);
            changed = true;
        }
        if (changed) BindingsChanged?.Invoke();
        return changed;
    }

    public bool ClearMissingConsumables(PlayerInventory inventory)
    {
        if (inventory == null)
            return false;
        bool changed = false;
        for (int i = 0; i < SlotCount; i++)
        {
            ConsumableItemData bound = boundConsumables[i];
            if (bound == null || inventory.CountItemsByBaseData(bound) > 0)
                continue;
            ClearAt(i);
            changed = true;
        }
        if (changed) BindingsChanged?.Invoke();
        return changed;
    }

    public bool ClearMissingFlasks()
    {
        if (PlayerFlaskController.Current == null)
            return false;
        bool changed = false;
        for (int i = 0; i < SlotCount; i++)
            if (!string.IsNullOrEmpty(boundFlaskIds[i]) && GetBoundFlask(i + FirstKey) == null)
            {
                ClearAt(i);
                changed = true;
            }
        if (changed) BindingsChanged?.Invoke();
        return changed;
    }

    public string GetBoundItemDisplayName(int key)
    {
        int index = ToIndex(key);
        if (index < 0)
            return "비어있음";
        ItemData flask = GetBoundFlask(key);
        if (flask != null)
            return flask.itemName;
        if (boundConsumables[index] != null)
            return boundConsumables[index].itemName;
        return GetBoundSkill(key)?.DisplayName ?? "비어있음";
    }

    private static int ToIndex(int key)
    {
        int index = key - FirstKey;
        return index >= 0 && index < SlotCount ? index : -1;
    }

    private bool IsEmpty(int index) => boundConsumables[index] == null && string.IsNullOrEmpty(boundFlaskIds[index]) && boundSkills[index] == null;

    private void ClearAt(int index)
    {
        boundConsumables[index] = null;
        boundFlaskIds[index] = null;
        boundSkills[index] = null;
    }

    private void Set(int index, ConsumableItemData consumable, string flaskId, IQuickSlotSkill skill)
    {
        boundConsumables[index] = consumable;
        boundFlaskIds[index] = flaskId;
        boundSkills[index] = skill;
        BindingsChanged?.Invoke();
    }

    private void ClearDuplicateFlask(int targetIndex, string flaskId)
    {
        for (int i = 0; i < SlotCount; i++)
            if (i != targetIndex && boundFlaskIds[i] == flaskId)
                ClearAt(i);
    }

    private void ClearDuplicateConsumable(int targetIndex, ConsumableItemData consumableData)
    {
        string itemId = GetStableItemId(consumableData);
        for (int i = 0; i < SlotCount; i++)
            if (i != targetIndex && boundConsumables[i] != null
                && (boundConsumables[i] == consumableData || GetStableItemId(boundConsumables[i]) == itemId))
                ClearAt(i);
    }

    private static string GetStableItemId(ConsumableItemData consumableData)
    {
        if (consumableData == null) return string.Empty;
        return !string.IsNullOrWhiteSpace(consumableData.targetBuffId) ? consumableData.targetBuffId : consumableData.name;
    }
}
