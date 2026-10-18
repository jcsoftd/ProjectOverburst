using UnityEngine;

public class InventoryQuickSlotBindingController : MonoBehaviour
{
    public const int FirstKey = 1;
    public const int SlotCount = 10;

    private ConsumableItemData[] boundConsumables => PlayerAccountInventoryService.Loadout.QuickConsumables;
    private string[] boundFlaskIds => PlayerAccountInventoryService.Loadout.QuickFlaskIds;
    private IQuickSlotSkill[] boundSkills => PlayerAccountInventoryService.Loadout.QuickSkills;
    private bool legacyFlasksImported
    {
        get => PlayerAccountInventoryService.Loadout.LegacyFlasksImported;
        set => PlayerAccountInventoryService.Loadout.LegacyFlasksImported = value;
    }

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
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseBindingsChanged);
    }

    public bool Bind(int key, ItemData item)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => Bind(key, item));
        int index = ToIndex(key);
        if (index < 0 || item == null || item.itemType != "Consumable")
            return false;

        if (item.baseData is FlaskItemData)
        {
            FlaskInstanceState state = FlaskRuntime.State(item);
            if (state == null || state.equippedSlot < 0)
                return false;
            int previousKey = GetFlaskKey(item);
            if (previousKey != 0) return MoveOrSwap(previousKey, key);
            Set(index, null, item.runtimeInstanceId, null);
            return true;
        }

        if (!(item.baseData is ConsumableItemData consumableData))
            return false;
        string itemId = GetStableItemId(consumableData);
        for (int i = 0; i < SlotCount; i++)
            if (boundConsumables[i] != null && GetStableItemId(boundConsumables[i]) == itemId)
                return MoveOrSwap(i + FirstKey, key);
        Set(index, consumableData, null, null);
        return true;
    }

    public object GetBinding(int key)
    {
        int index = ToIndex(key);
        if (index < 0) return null;
        return (object)boundConsumables[index] ?? (object)boundFlaskIds[index] ?? boundSkills[index];
    }

    public bool MoveOrSwap(int fromKey, int toKey)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => MoveOrSwap(fromKey, toKey));
        int from = ToIndex(fromKey), to = ToIndex(toKey);
        if (from < 0 || to < 0 || IsEmpty(from)) return false;
        if (from == to) return true;
        if ((!string.IsNullOrEmpty(boundFlaskIds[from]) || !string.IsNullOrEmpty(boundFlaskIds[to]))
            && !PlayerFlaskController.CanChangeLoadout) return false;
        var consumable = boundConsumables[from]; var flask = boundFlaskIds[from]; var skill = boundSkills[from];
        boundConsumables[from] = boundConsumables[to]; boundFlaskIds[from] = boundFlaskIds[to]; boundSkills[from] = boundSkills[to];
        boundConsumables[to] = consumable; boundFlaskIds[to] = flask; boundSkills[to] = skill;
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseBindingsChanged);
        return true;
    }

    public int FindFreeFlaskKey()
    {
        int[] preferred = { 4, 5, 6, 1, 2, 3, 7, 8, 9, 10 };
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
            if (ReferenceEquals(boundSkills[i], skill))
                return MoveOrSwap(i + FirstKey, key);
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
            Overburst.Persistence.AccountGameplaySession.Notify(RaiseBindingsChanged);
            return null;
        }
        return skill;
    }

    public bool Clear(int key)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => Clear(key));
        int index = ToIndex(key);
        if (index < 0 || IsEmpty(index))
            return false;
        ClearAt(index);
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseBindingsChanged);
        return true;
    }

    public bool ClearFlask(ItemData item)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => ClearFlask(item));
        if (item == null)
            return false;
        bool changed = false;
        for (int i = 0; i < SlotCount; i++)
            if (boundFlaskIds[i] == item.runtimeInstanceId)
            {
                ClearAt(i);
                changed = true;
            }
        if (changed) Overburst.Persistence.AccountGameplaySession.Notify(RaiseBindingsChanged);
        return changed;
    }

    public bool ClearConsumable(ConsumableItemData consumableData)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => ClearConsumable(consumableData));
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
        if (changed) Overburst.Persistence.AccountGameplaySession.Notify(RaiseBindingsChanged);
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
        if (changed) Overburst.Persistence.AccountGameplaySession.Notify(RaiseBindingsChanged);
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
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseBindingsChanged);
    }

    private static string GetStableItemId(ConsumableItemData consumableData)
    {
        if (consumableData == null) return string.Empty;
        return !string.IsNullOrWhiteSpace(consumableData.targetBuffId) ? consumableData.targetBuffId : consumableData.name;
    }

    private void RaiseBindingsChanged() => BindingsChanged?.Invoke();
}
