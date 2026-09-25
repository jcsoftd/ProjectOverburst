public static class GearEquipmentService
{
    public static bool EquipFromInventorySlot(int inventorySlot, int preferredGearSlot = -1)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => EquipFromInventorySlot(inventorySlot, preferredGearSlot));
        PlayerContext context = PlayerContext.Instance;
        PlayerInventory inventory = context != null ? context.CurrentActorInventory : null;
        PlayerEquipment equipment = context != null ? context.CurrentActorEquipment : null;
        if (inventory == null || equipment == null) return false;
        ItemData item = inventory.GetItemAt(inventorySlot);
        if (item == null || !(item.baseData is GearItemData data)) return false;
        int gearSlot = preferredGearSlot >= 0 ? preferredGearSlot : DefaultSlot(data.kind, equipment);
        if (gearSlot < 0 || gearSlot >= 7 || !GearItemData.Fits(data.kind, (GearSlot)gearSlot)) return false;

        ItemData replaced = equipment.GetGearSlotItem(gearSlot);
        if (!inventory.TryReplaceOwnedItemAt(inventorySlot, item, replaced)) return false;
        if (equipment.EquipGearItemToSlot(item, gearSlot, out _)) return true;
        inventory.TryReplaceOwnedItemAt(inventorySlot, replaced, item);
        return false;
    }

    public static bool UnequipToInventory(int gearSlot)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => UnequipToInventory(gearSlot));
        PlayerContext context = PlayerContext.Instance;
        PlayerInventory inventory = context != null ? context.CurrentActorInventory : null;
        PlayerEquipment equipment = context != null ? context.CurrentActorEquipment : null;
        if (inventory == null || equipment == null) return false;
        ItemData item = equipment.GetGearSlotItem(gearSlot);
        int empty = inventory.FindFirstEmptySlot();
        if (item == null || empty < 0) return false;
        if (!inventory.TryReplaceOwnedItemAt(empty, null, item)) return false;
        if (equipment.ClearGearSlot(gearSlot, out _)) return true;
        inventory.TryReplaceOwnedItemAt(empty, item, null);
        return false;
    }

    private static int DefaultSlot(GearKind kind, PlayerEquipment equipment)
    {
        if (kind != GearKind.Earring) return kind == GearKind.Necklace ? 6 : (int)kind;
        return equipment.GetGearSlotItem(4) == null ? 4 : equipment.GetGearSlotItem(5) == null ? 5 : 4;
    }
}
