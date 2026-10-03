public static class ElementGemEquipmentService
{
    public static bool EquipFromInventorySlot(int slot, string expectedInstanceId = null)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => EquipFromInventorySlot(slot, expectedInstanceId));
        var equipment = PlayerContext.Instance?.CurrentActorEquipment;
        var inventory = PlayerContext.Instance?.CurrentActorInventory;
        if (equipment == null || inventory == null) return false;
        var item = inventory.GetItemAt(slot);
        if (!(item?.baseData is ElementGemItemData) || (expectedInstanceId != null && item.runtimeInstanceId != expectedInstanceId)) return false;
        item.EnsureRuntimeState();
        var previous = equipment.EquippedElementGem;
        if (!inventory.TryReplaceOwnedItemAt(slot, item, previous)) return false;
        equipment.SetElementGem(item);
        return true;
    }
    public static bool UnequipToInventory()
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(UnequipToInventory);
        var equipment = PlayerContext.Instance?.CurrentActorEquipment;
        var inventory = PlayerContext.Instance?.CurrentActorInventory;
        if (equipment == null || inventory == null || equipment.EquippedElementGem == null) return false;
        int empty = inventory.FindFirstEmptySlot();
        if (empty < 0 || !inventory.TryReplaceOwnedItemAt(empty, null, equipment.EquippedElementGem)) return false;
        equipment.SetElementGem(null);
        return true;
    }
}
