using System;
using UnityEngine;

// InventorySlotBridge partial: 슬롯 클릭·드롭, 우클릭 메뉴, 상점 선택. 필드와 Unity 수명주기는 InventorySlotBridge.cs에 있다.
public partial class InventorySlotBridge
{
    public bool HandleSlotClick(SlotClickContext context)
    {
        if (context == null || context.Slot == null || context.Item == null || context.Slot.IsLocked)
            return false;

        if (TryHandleOpenShopPlayerSlot(context))
            return true;

        bool handled;
        StashSlotBridge openStashBridge = StashSlotBridge.FindOpenBridge(); // 열린 창고
        if (openStashBridge != null && !context.Slot.IsWeaponSlot && !context.Slot.IsBagSlot)
        {
            handled = openStashBridge.TryMoveInventorySlotToFirstAvailableStashSlot(context.Slot.SlotIndex); // 창고 우선 이동
            return handled;
        }

        if (context.Slot.IsBagSlot)
            handled = UnequipBagToFirstAvailableSlot(context.Slot.SlotIndex);
        else if (context.Item.itemType == "Bag")
            handled = EquipBagFromInventorySlot(context.Slot);
        else if (context.Item.baseData is ElementGemItemData && !context.Slot.IsWeaponSlot)
            handled = ElementGemEquipmentService.EquipFromInventorySlot(context.Slot.SlotIndex, context.Item.runtimeInstanceId);
        else if (context.Item.itemType == "Gear" && !context.Slot.IsWeaponSlot)
            handled = GearEquipmentService.EquipFromInventorySlot(context.Slot.SlotIndex);
        else if (context.Item.itemType != "Weapon")
            handled = false;
        else if (context.IsWeaponSlot)
            handled = UnequipWeaponToFirstAvailableSlot(context.Slot.SlotIndex);
        else
            handled = EquipWeaponFromInventorySlot(context.Slot);

        return handled;
    }

    public bool HandleSlotSingleClick(SlotClickContext context)
    {
        if (ShopUI.TryConsumeOpenPlayerInventorySingleClick())
            return true;

        return false;
    }

    public bool HandleSlotRightClick(SlotClickContext context)
    {
        return ShopUI.TryOpenPlayerInventoryShopContextMenu(context);
    }

    public bool HandleSlotDrop(SlotDropContext context)
    {
        if (context == null)
            return false;


        bool handled;
        if (context.TargetSlot != null && context.TargetSlot.IsBagSlot)
            handled = HandleDropToBagSlot(context.OriginSlot, context.TargetSlot); // 가방 장착
        else if (context.OriginSlot != null && context.OriginSlot.IsBagSlot)
            handled = HandleDropFromBagSlot(context.OriginSlot, context.TargetSlot); // 가방 해제
        else
            handled = HandleSlotDrop(context.OriginSlot, context.TargetSlot);

        return handled;
    }

    public bool HandleSlotDrop(SlotUI sourceSlot, SlotUI targetSlot)
    {
        if (sourceSlot == null || targetSlot == null || sourceSlot == targetSlot || sourceSlot.IsLocked || targetSlot.IsLocked)
            return false;

        bool handled;
        if (sourceSlot.IsWeaponSlot)
            handled = HandleWeaponSlotDrop(sourceSlot, targetSlot); // 무기 해제
        else if (targetSlot.IsWeaponSlot)
            handled = HandleDropToWeaponSlot(sourceSlot, targetSlot); // 무기 장착
        else
            handled = HandleInventorySlotDrop(sourceSlot, targetSlot);

        return handled;
    }

    public bool HandleExternalDrop(SlotUI sourceSlot)
    {
        return HandleExternalDropResult(sourceSlot, Vector2.zero).Succeeded;
    }

    public bool HandleExternalDrop(SlotUI sourceSlot, Vector2 screenPosition)
    {
        return HandleExternalDropResult(sourceSlot, screenPosition).Succeeded;
    }

    public InventoryActionResult HandleExternalDropResult(SlotUI sourceSlot, Vector2 screenPosition)
    {
        if (sourceSlot == null || sourceSlot.DisplayItem == null)
            return InventoryActionResult.Fail(InventoryActionFailureReason.InvalidSource, "드롭할 슬롯 아이템이 없습니다.");

        if (sourceSlot.IsBagSlot)
            return InventoryActionResult.Fail(InventoryActionFailureReason.BlockedSlotType, "장착 가방 슬롯 월드 드롭은 1차에서 지원하지 않습니다.");

        ItemData current = sourceSlot.IsWeaponSlot ? GetEquippedWeapon(sourceSlot.SlotIndex)
            : inventory != null ? inventory.GetItemAt(sourceSlot.SlotIndex) : null;
        if (!ReferenceEquals(current, sourceSlot.DisplayItem))
            return InventoryActionResult.Fail(InventoryActionFailureReason.InvalidSource, "슬롯 아이템이 변경되었습니다.");

        InventoryWorldDropRequest request = new InventoryWorldDropRequest
        {
            Inventory = inventory, // 드롭 주체
            InventoryUI = inventoryUI,
            SourceSlot = sourceSlot,
            PlayerEquipment = playerEquipment,
            FallbackTransform = transform,
            PickupGradeVfxSet = pickupGradeVfxSet,
            ScreenPosition = screenPosition,
            ForwardDistance = worldDropForwardDistance,
            SpawnHeight = worldDropSpawnHeight,
            GroundProbeHeight = worldDropGroundProbeHeight,
            GroundProbeDistance = worldDropGroundProbeDistance
        };

        InventoryActionResult result = RunSlotDataMutation(() => InventoryWorldDrop.TryDropInventorySlotToWorld(request)); // 월드 드롭

        if (result.Succeeded)
            RefreshSlotsAfterDataChange();

        return result;
    }

    public bool EquipWeaponFromContextMenu(SlotUI sourceSlot, int weaponSlotIndex)
    {
        return EquipWeaponFromInventorySlot(sourceSlot, weaponSlotIndex);
    }

    public bool EquipBagFromContextMenu(SlotUI sourceSlot)
    {
        return EquipBagFromInventorySlot(sourceSlot);
    }

    public bool UnequipBagFromContextMenu(int bagSlotIndex)
    {
        return UnequipBagToFirstAvailableSlot(bagSlotIndex);
    }

    public bool UnequipWeaponFromContextMenu(int weaponSlotIndex, out string failureMessage)
    {
        failureMessage = string.Empty;

        if (inventory == null || !HasWeaponEquipment())
        {
            failureMessage = "Inventory or PlayerEquipment reference is missing.";
            return false;
        }

        ItemData currentWeapon = GetEquippedWeapon(weaponSlotIndex);
        if (currentWeapon == null)
        {
            failureMessage = "Weapon slot is empty.";
            return false;
        }

        if (!inventory.ContainsItem(currentWeapon) && inventory.FindFirstEmptySlot() < 0)
        {
            failureMessage = "인벤토리에 빈 슬롯이 없어 장착해제할 수 없습니다.";
            return false;
        }

        bool result = UnequipWeaponToFirstAvailableSlot(weaponSlotIndex);
        if (!result)
        {
            failureMessage = "Weapon unequip failed.";
            return false;
        }

        RefreshCurrentWeaponStats();
        RefreshSlotsAfterDataChange();
        return true;
    }

    public ItemData GetWeaponSlotItemFromContextMenu(int weaponSlotIndex)
    {
        return GetEquippedWeapon(weaponSlotIndex);
    }

    public void RefreshSlotsFromContextMenu()
    {
        RefreshSlotsAfterDataChange();
    }

    public void SetShopTradeSelectedSlots(System.Collections.Generic.IList<int> selectedIndices)
    {
        if (inventorySlots == null)
            return;

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            SlotUI slot = inventorySlots[i];
            if (slot == null)
                continue;

            slot.SetContextSelected(ContainsIndex(selectedIndices, i));
        }
    }

    public void ClearShopTradeSelectedSlots()
    {
        SetShopTradeSelectedSlots(null);
    }

    private bool TryHandleOpenShopPlayerSlot(SlotClickContext context)
    {
        if (!IsShopTradeEligibleInventorySlot(context))
            return false;

        string message;
        return ShopUI.TryHandleOpenPlayerInventorySlot(context.Slot.SlotIndex, out message);
    }

    private bool IsShopTradeEligibleInventorySlot(SlotClickContext context)
    {
        return context != null
            && context.Slot != null
            && context.Item != null
            && !context.Slot.IsLocked
            && !context.Slot.IsWeaponSlot
            && !context.Slot.IsBagSlot;
    }

    private bool ContainsIndex(System.Collections.Generic.IList<int> selectedIndices, int index)
    {
        if (selectedIndices == null)
            return false;

        for (int i = 0; i < selectedIndices.Count; i++)
        {
            if (selectedIndices[i] == index)
                return true;
        }

        return false;
    }

    private bool HandleInventorySlotDrop(SlotUI sourceSlot, SlotUI targetSlot)
    {
        if (inventory == null || sourceSlot == null || targetSlot == null || sourceSlot.IsLocked || targetSlot.IsLocked)
            return false;

        bool moved = RunSlotDataMutation(() => inventory.MoveMergeOrSwapItems(sourceSlot.SlotIndex, targetSlot.SlotIndex)); // 이동/병합/교환

        if (moved)
            RefreshSlotsAfterDataChange();

        return moved;
    }
}
