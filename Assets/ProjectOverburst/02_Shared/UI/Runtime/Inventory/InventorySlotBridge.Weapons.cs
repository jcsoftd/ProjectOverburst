using System;
using UnityEngine;

// InventorySlotBridge partial: 무기 장착·해제·이동과 되돌리기. 필드와 Unity 수명주기는 InventorySlotBridge.cs에 있다.
public partial class InventorySlotBridge
{
    private bool NormalizeEquippedWeaponOwnership()
    {
        if (inventory == null || !HasWeaponEquipment() || allowEquippedWeaponInInventory || normalizingEquippedWeaponOwnership)
            return false;

        normalizingEquippedWeaponOwnership = true; // 재진입 방지
        bool changed = false;

        for (int i = 0; i < RequiredWeaponSlotCount; i++)
        {
            ItemData equippedWeapon = GetEquippedWeapon(i);
            if (equippedWeapon == null)
                continue;

            if (inventory.ClearAllMatchingItems(equippedWeapon)) // 장착품 중복 제거
                changed = true;
        }

        normalizingEquippedWeaponOwnership = false;
        return changed;
    }

    private void EnsureWeaponSlotCount()
    {
        if (weaponSlots == null || weaponSlots.Length < RequiredWeaponSlotCount)
        {
            Debug.LogWarning("InventorySlotBridge requires scene-placed WeaponSlot_01, WeaponSlot_02, WeaponSlot_03 references.", this);
            return;
        }

        for (int i = 0; i < RequiredWeaponSlotCount; i++)
        {
            if (weaponSlots[i] == null)
                Debug.LogWarning("InventorySlotBridge weapon slot reference is missing. Connect WeaponSlot_01~03 in order.", this);
        }
    }

    private bool HandleDropToWeaponSlot(SlotUI sourceSlot, SlotUI targetWeaponSlot)
    {
        if (sourceSlot == null || targetWeaponSlot == null || sourceSlot.DisplayItem == null)
            return false;

        if (sourceSlot.DisplayItem.itemType != "Weapon")
            return false;

        return EquipWeaponFromInventorySlot(sourceSlot, targetWeaponSlot.SlotIndex);
    }

    private bool HandleWeaponSlotDrop(SlotUI sourceSlot, SlotUI targetSlot)
    {
        if (!HasWeaponEquipment() || inventory == null || sourceSlot.DisplayItem == null || targetSlot.IsWeaponSlot || targetSlot.IsBagSlot || targetSlot.IsLocked)
            return false;

        ItemData targetItem = inventory.GetItemAt(targetSlot.SlotIndex); // 목표 칸

        if (targetItem == null)
        {
            SlotMoveResult result = RunSlotDataMutation(() => MoveWeaponSlotToInventorySlot(sourceSlot.SlotIndex, targetSlot.SlotIndex)); // 무기 해제
            if (!result.Succeeded)
                return false;

            RefreshSlotsAfterDataChange();
            return true;
        }

        return false;
    }

    private SlotMoveResult MoveWeaponSlotToInventorySlot(int weaponSlotIndex, int inventorySlotIndex)
    {
        if (!HasWeaponEquipment() || inventory == null)
            return SlotMoveResult.Fail("Inventory or equipment is missing.");

        ItemData currentWeapon = GetEquippedWeapon(weaponSlotIndex);
        if (currentWeapon == null)
            return SlotMoveResult.Fail("Weapon slot is empty.");

        if (inventorySlotIndex < 0 || inventorySlotIndex >= inventory.UnlockedSlotCount)
            return SlotMoveResult.Fail("Target inventory slot is invalid.");

        if (inventory.GetItemAt(inventorySlotIndex) != null)
            return SlotMoveResult.Fail("Target inventory slot is not empty.");

        allowEquippedWeaponInInventory = true; // 이동 중 예외

        if (!inventory.SetItemAt(inventorySlotIndex, currentWeapon))
        {
            allowEquippedWeaponInInventory = false;
            return SlotMoveResult.Fail("Failed to place weapon in inventory.");
        }

        if (!ClearEquippedWeapon(weaponSlotIndex))
        {
            inventory.ClearSlot(inventorySlotIndex); // 롤백
            allowEquippedWeaponInInventory = false;
            return SlotMoveResult.Fail("Failed to clear weapon slot.");
        }

        allowEquippedWeaponInInventory = false; // 예외 해제
        return SlotMoveResult.Success();
    }

    private bool EquipWeaponFromInventorySlot(SlotUI sourceSlot)
    {
        if (!HasWeaponEquipment())
            return false;

        int targetWeaponSlotIndex = GetDefaultTargetWeaponSlotIndex(); // 현재 리더
        return EquipWeaponFromInventorySlot(sourceSlot, targetWeaponSlotIndex);
    }

    private bool EquipWeaponFromInventorySlot(SlotUI sourceSlot, int weaponSlotIndex)
    {
        return RunSlotDataMutation(() => EquipWeaponFromInventorySlotCore(sourceSlot, weaponSlotIndex));
    }

    private bool EquipWeaponFromInventorySlotCore(SlotUI sourceSlot, int weaponSlotIndex)
    {
        if (inventory == null || !HasWeaponEquipment() || sourceSlot == null || sourceSlot.IsWeaponSlot || sourceSlot.IsBagSlot || sourceSlot.IsLocked)
            return false;

        if (!TryCreateInventorySourceSnapshot(sourceSlot, "Weapon", out InventorySourceSnapshot source))
            return false;

        ItemData item = source.Item; // 장착할 무기
        ItemData previousWeapon = GetEquippedWeapon(weaponSlotIndex); // 기존 무기
        if (!ClearInventorySource(source))
            return false;

        if (!EquipWeapon(weaponSlotIndex, item))
        {
            RestoreInventorySource(source); // 원본 복구
            RefreshSlotsAfterDataChange();
            return false;
        }

        ClearInventoryCopiesOfEquippedWeapon(item); // 중복 제거

        if (previousWeapon != null && !IsSameRuntimeItem(previousWeapon, item) && !inventory.ContainsItem(previousWeapon))
        {
            int returnIndex = inventory.GetItemAt(source.SlotIndex) == null ? source.SlotIndex : inventory.FindFirstEmptySlot(); // 반환 위치
            if (returnIndex < 0 || !inventory.SetItemAt(returnIndex, previousWeapon))
                return RollbackWeaponEquip(weaponSlotIndex, previousWeapon, source);
        }

        RefreshSlotsAfterDataChange();
        return true;
    }

    private bool ClearInventoryCopiesOfEquippedWeapon(ItemData equippedItem)
    {
        return inventory != null && inventory.ClearAllMatchingItems(equippedItem);
    }

    private bool RollbackWeaponEquip(int weaponSlotIndex, ItemData previousWeapon, InventorySourceSnapshot source)
    {
        if (previousWeapon != null)
            EquipWeapon(weaponSlotIndex, previousWeapon);
        else
            ClearEquippedWeapon(weaponSlotIndex);

        RestoreInventorySource(source);

        RefreshSlotsAfterDataChange();
        return false;
    }

    private bool UnequipWeaponToFirstAvailableSlot(int weaponSlotIndex)
    {
        return RunSlotDataMutation(() => UnequipWeaponToFirstAvailableSlotCore(weaponSlotIndex));
    }

    private bool UnequipWeaponToFirstAvailableSlotCore(int weaponSlotIndex)
    {
        if (inventory == null || !HasWeaponEquipment())
            return false;

        ItemData currentWeapon = GetEquippedWeapon(weaponSlotIndex); // 해제 무기

        if (currentWeapon == null)
            return false;

        if (inventory.ContainsItem(currentWeapon))
        {
            ClearEquippedWeapon(weaponSlotIndex);
            RefreshSlotsAfterDataChange();
            return true;
        }

        int emptySlot = inventory.FindFirstEmptySlot(); // 반환 칸
        if (emptySlot < 0)
            return false;

        SlotMoveResult result = MoveWeaponSlotToInventorySlot(weaponSlotIndex, emptySlot);
        if (!result.Succeeded)
            return false;

        RefreshSlotsAfterDataChange();
        return true;
    }

    private bool HasWeaponEquipment()
    {
        return playerEquipment != null;
    }

    private ItemData GetEquippedWeapon(int weaponSlotIndex)
    {
        return playerEquipment != null ? playerEquipment.GetWeaponSlotItem(weaponSlotIndex) : null;
    }

    private bool EquipWeapon(int weaponSlotIndex, ItemData item)
    {
        return playerEquipment != null && playerEquipment.EquipWeaponItemToSlot(item, weaponSlotIndex);
    }

    private bool ClearEquippedWeapon(int weaponSlotIndex)
    {
        return playerEquipment != null && playerEquipment.ClearWeaponSlot(weaponSlotIndex);
    }

    private bool IsActiveWeaponSlot(int weaponSlotIndex)
    {
        return playerEquipment != null && playerEquipment.IsActiveWeaponSlot(weaponSlotIndex);
    }

    private int GetDefaultTargetWeaponSlotIndex()
    {
        return playerEquipment != null ? playerEquipment.ActiveWeaponSlotIndex : 0;
    }

    private void RefreshCurrentWeaponStats()
    {
        if (playerEquipment != null)
            playerEquipment.RefreshCurrentWeaponStats();
    }
}
