using System;
using UnityEngine;

// InventorySlotBridge partial: 무기 장착·해제·이동과 되돌리기. 필드와 Unity 수명주기는 InventorySlotBridge.cs에 있다.
public partial class InventorySlotBridge
{


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
        var item = GetEquippedWeapon(weaponSlotIndex);
        return playerEquipment != null && playerEquipment.TryUnequipToInventorySlot(inventory, inventorySlotIndex, weaponSlotIndex, item)
            ? SlotMoveResult.Success() : SlotMoveResult.Fail("Failed to return weapon to inventory.");
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
        if (!HasWeaponEquipment() || !TryCreateInventorySourceSnapshot(sourceSlot, "Weapon", out InventorySourceSnapshot source)) return false;
        bool equipped = playerEquipment.TryEquipFromInventorySlot(inventory, source.SlotIndex, source.Item, weaponSlotIndex);
        RefreshSlotsAfterDataChange();
        return equipped;
    }





    private bool UnequipWeaponToFirstAvailableSlot(int weaponSlotIndex)
    {
        return RunSlotDataMutation(() => UnequipWeaponToFirstAvailableSlotCore(weaponSlotIndex));
    }

    private bool UnequipWeaponToFirstAvailableSlotCore(int weaponSlotIndex)
    {
        if (inventory == null || !HasWeaponEquipment()) return false;
        var item = GetEquippedWeapon(weaponSlotIndex);
        if (item == null) return false;
        int emptySlot = inventory.FindFirstEmptySlot();
        if (emptySlot < 0) return false;
        var result = MoveWeaponSlotToInventorySlot(weaponSlotIndex, emptySlot);
        RefreshSlotsAfterDataChange();
        return result.Succeeded;
    }

    private bool HasWeaponEquipment()
    {
        return playerEquipment != null;
    }

    private ItemData GetEquippedWeapon(int weaponSlotIndex)
    {
        return playerEquipment != null ? playerEquipment.PeekWeaponSlotItem(weaponSlotIndex) : null;
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
