using System;
using UnityEngine;

// InventorySlotBridge partial: 드래그 미리보기와 가방 용량 미리보기. 필드와 Unity 수명주기는 InventorySlotBridge.cs에 있다.
public partial class InventorySlotBridge
{
    public void BeginDragPreview(SlotUI originSlot)
    {
        previewOriginSlot = originSlot; // 드래그 원본
        ShowDragPreviewForTarget(null);
    }

    public void ShowDragPreviewForTarget(SlotUI targetSlot)
    {
        if (previewOriginSlot == null)
        {
            previewOriginSlot = DragSlot.OriginSlot; // 다른 bridge에서 시작한 드래그 보정
            if (previewOriginSlot == null)
                return;
        }

        ClearAllDragOverlays(); // 이전 preview
        ApplyBagCapacityPreview(targetSlot); // 가방 칸 변화

        if (CanPreviewDropToTarget(targetSlot))
            targetSlot.SetDragOverlay(SlotDragOverlayState.WillUnlock); // 드롭 가능 표시
    }

    public void ClearDragPreview()
    {
        previewOriginSlot = null;
        ClearAllDragOverlays();
    }

    private void ApplyBagCapacityPreview(SlotUI targetSlot)
    {
        if (targetSlot == null || inventorySlots == null)
            return;

        ItemData[] proposedBags; // 가상 가방
        int ignoredInventorySlotIndex = -1; // 이동 원본

        if (!TryBuildProposedBagsForPreview(targetSlot, out proposedBags, out ignoredInventorySlotIndex))
            return;

        int currentUnlockedSlots = GetUnlockedInventorySlotCount();
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags);

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i] == null)
                continue;

            bool currentlyUnlocked = i < currentUnlockedSlots;
            bool willBeUnlocked = i < proposedUnlockedSlots;

            if (!currentlyUnlocked && willBeUnlocked)
                inventorySlots[i].SetDragOverlay(SlotDragOverlayState.WillUnlock);
            else if (currentlyUnlocked && !willBeUnlocked)
                inventorySlots[i].SetDragOverlay(SlotDragOverlayState.WillLock);
        }
    }

    private bool TryBuildProposedBagsForPreview(SlotUI targetSlot, out ItemData[] proposedBags, out int ignoredInventorySlotIndex)
    {
        proposedBags = null;
        ignoredInventorySlotIndex = -1;

        if (previewOriginSlot == null || targetSlot == null)
            return false;

        proposedBags = CopyEquippedBags();

        if (targetSlot.IsBagSlot && IsBagItem(previewOriginSlot.DisplayItem) && !previewOriginSlot.IsBagSlot)
        {
            if (!IsBagSlotIndexValid(targetSlot.SlotIndex))
                return false;

            proposedBags[targetSlot.SlotIndex] = previewOriginSlot.DisplayItem; // 장착 가정
            ignoredInventorySlotIndex = previewOriginSlot.SlotIndex; // 원본 제외
            return true;
        }

        if (!targetSlot.IsWeaponSlot && !targetSlot.IsBagSlot && previewOriginSlot.IsBagSlot)
        {
            if (!IsBagSlotIndexValid(previewOriginSlot.SlotIndex))
                return false;

            proposedBags[previewOriginSlot.SlotIndex] = null; // 해제 가정
            return true;
        }

        return false;
    }

    private void ClearAllDragOverlays()
    {
        ClearDragOverlays(inventorySlots);
        ClearDragOverlays(weaponSlots);
        ClearDragOverlays(bagSlots);
    }

    private void ClearDragOverlays(SlotUI[] slots)
    {
        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].ClearDragOverlay();
        }
    }

    private bool CanPreviewEquipBagIntoSlot(SlotUI sourceSlot, int bagSlotIndex)
    {
        if (inventory == null || sourceSlot == null || !IsBagSlotIndexValid(bagSlotIndex))
            return false;

        ItemData newBag = sourceSlot.DisplayItem;

        if (!IsBagItem(newBag))
            return false;

        ItemData oldBag = equippedBags[bagSlotIndex];
        ItemData[] proposedBags = CopyEquippedBags();
        proposedBags[bagSlotIndex] = newBag;
        return inventory.CanReplaceOwnedItemAt(sourceSlot.SlotIndex, newBag, oldBag);
    }

    private bool CanPreviewDropToTarget(SlotUI targetSlot)
    {

        SlotUI sourceSlot = previewOriginSlot != null ? previewOriginSlot : DragSlot.OriginSlot;

        if (targetSlot == null || targetSlot.IsLocked)
            return false;

        if (sourceSlot == null || sourceSlot == targetSlot || sourceSlot.IsLocked || sourceSlot.DisplayItem == null)
            return false;

        if (targetSlot.IsBagSlot)
            return sourceSlot.IsBagSlot || CanPreviewEquipBagIntoSlot(sourceSlot, targetSlot.SlotIndex);

        if (sourceSlot.IsBagSlot)
            return CanPreviewUnequipBagToTarget(sourceSlot, targetSlot);

        if (targetSlot.IsWeaponSlot)
            return CanPreviewWeaponSlotTarget(sourceSlot, targetSlot);

        if (sourceSlot.IsWeaponSlot)
            return targetSlot.DisplayItem == null;

        return !targetSlot.IsBagSlot && !targetSlot.IsWeaponSlot;
    }

    private bool CanPreviewUnequipBagToTarget(SlotUI sourceBagSlot, SlotUI targetSlot)
    {
        if (sourceBagSlot == null || !sourceBagSlot.IsBagSlot || targetSlot == null || targetSlot.IsWeaponSlot || targetSlot.IsBagSlot || targetSlot.IsLocked)
            return false;

        if (inventory == null || targetSlot.DisplayItem != null)
            return false;

        ItemData[] proposedBags = CopyEquippedBags();
        if (!IsBagSlotIndexValid(sourceBagSlot.SlotIndex))
            return false;

        proposedBags[sourceBagSlot.SlotIndex] = null;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags);
        return targetSlot.SlotIndex < proposedUnlockedSlots;
    }

    private bool CanPreviewWeaponSlotTarget(SlotUI sourceSlot, SlotUI targetWeaponSlot)
    {
        if (sourceSlot == null || targetWeaponSlot == null || sourceSlot.DisplayItem == null)
            return false;

        if (sourceSlot.DisplayItem.itemType == "Weapon")
            return true;

        return false;
    }
}
