using System;
using UnityEngine;

// InventorySlotBridge partial: 가방 장착·해제·교체, 칸 수 계산, 가방 보너스. 필드와 Unity 수명주기는 InventorySlotBridge.cs에 있다.
public partial class InventorySlotBridge
{
    private bool EquipBagFromInventorySlot(SlotUI sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.IsWeaponSlot || sourceSlot.IsBagSlot || sourceSlot.IsLocked)
            return false;

        for (int i = 0; i < equippedBags.Length; i++)
        {
            if (equippedBags[i] == null)
                return EquipBagIntoSlot(sourceSlot, i); // 빈 가방칸
        }

        return equippedBags.Length > 0 && EquipBagIntoSlot(sourceSlot, 0); // 1칸 정책: 기존 가방 교체
    }

    private bool HandleDropToBagSlot(SlotUI sourceSlot, SlotUI targetBagSlot)
    {
        if (targetBagSlot == null || !targetBagSlot.IsBagSlot || sourceSlot == null || sourceSlot.IsLocked)
            return false;

        if (sourceSlot.IsBagSlot)
            return SwapBagSlots(sourceSlot.SlotIndex, targetBagSlot.SlotIndex); // 가방 교환

        return EquipBagIntoSlot(sourceSlot, targetBagSlot.SlotIndex);
    }

    private bool EquipBagIntoSlot(SlotUI sourceSlot, int bagSlotIndex)
    {
        SlotMoveResult result = RunSlotDataMutation(() => MoveInventoryBagToBagSlot(sourceSlot, bagSlotIndex));
        if (!result.Succeeded)
            return false;

        RefreshSlotsAfterDataChange();
        return true;
    }

    private SlotMoveResult MoveInventoryBagToBagSlot(SlotUI sourceSlot, int bagSlotIndex)
    {
        if (inventory == null || !IsBagSlotIndexValid(bagSlotIndex))
            return SlotMoveResult.Fail("Inventory or bag slot is invalid.");

        if (!TryCreateInventorySourceSnapshot(sourceSlot, "Bag", out InventorySourceSnapshot source))
            return SlotMoveResult.Fail("Source bag is invalid.");

        ItemData newBag = source.Item; // 새 가방
        if (!IsBagItem(newBag))
            return SlotMoveResult.Fail("Source item is not a bag.");

        ItemData oldBag = equippedBags[bagSlotIndex]; // 기존 가방
        ItemData[] proposedBags = CopyEquippedBags(); // 가상 장착
        proposedBags[bagSlotIndex] = newBag;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags); // 예상 칸

        // Exchange while both original ownership references are still available.
        if (!inventory.TryReplaceOwnedItemAt(source.SlotIndex, newBag, oldBag))
            return SlotMoveResult.Fail("Failed to exchange bag ownership.");

        equippedBags[bagSlotIndex] = newBag;
        inventory.SetUnlockedSlotCount(proposedUnlockedSlots);

        return SlotMoveResult.Success();
    }

    private bool HandleDropFromBagSlot(SlotUI sourceBagSlot, SlotUI targetSlot)
    {
        if (sourceBagSlot == null || !sourceBagSlot.IsBagSlot || targetSlot == null || targetSlot.IsWeaponSlot || targetSlot.IsBagSlot || targetSlot.IsLocked)
            return false;

        if (inventory == null || inventory.GetItemAt(targetSlot.SlotIndex) != null)
            return false;

        return UnequipBagToInventorySlot(sourceBagSlot.SlotIndex, targetSlot.SlotIndex);
    }

    private bool UnequipBagToFirstAvailableSlot(int bagSlotIndex)
    {
        if (!IsBagSlotIndexValid(bagSlotIndex) || equippedBags[bagSlotIndex] == null || inventory == null)
            return false;

        ItemData[] proposedBags = CopyEquippedBags(); // 해제 가정
        proposedBags[bagSlotIndex] = null;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags);
        int targetSlotIndex = inventory.FindFirstEmptySlotWithin(proposedUnlockedSlots); // 반환 칸

        if (targetSlotIndex < 0)
            return false;

        return UnequipBagToInventorySlot(bagSlotIndex, targetSlotIndex);
    }

    private bool UnequipBagToInventorySlot(int bagSlotIndex, int targetSlotIndex)
    {
        SlotMoveResult result = RunSlotDataMutation(() => MoveBagSlotToInventorySlot(bagSlotIndex, targetSlotIndex));
        if (!result.Succeeded)
            return false;

        RefreshSlotsAfterDataChange();
        return true;
    }

    private SlotMoveResult MoveBagSlotToInventorySlot(int bagSlotIndex, int targetSlotIndex)
    {
        if (!IsBagSlotIndexValid(bagSlotIndex) || equippedBags[bagSlotIndex] == null || inventory == null)
            return SlotMoveResult.Fail("Bag slot is invalid or empty.");

        ItemData bag = equippedBags[bagSlotIndex]; // 해제 가방
        ItemData[] proposedBags = CopyEquippedBags(); // 해제 가정
        proposedBags[bagSlotIndex] = null;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags); // 예상 칸

        if (targetSlotIndex < 0 || targetSlotIndex >= proposedUnlockedSlots || inventory.GetItemAt(targetSlotIndex) != null)
            return SlotMoveResult.Fail("Target inventory slot is invalid.");

        if (!inventory.TryReplaceOwnedItemAt(targetSlotIndex, null, bag))
            return SlotMoveResult.Fail("Failed to return equipped bag.");

        equippedBags[bagSlotIndex] = null;
        inventory.SetUnlockedSlotCount(proposedUnlockedSlots);

        return SlotMoveResult.Success();
    }

    private bool SwapBagSlots(int fromIndex, int toIndex)
    {
        if (!IsBagSlotIndexValid(fromIndex) || !IsBagSlotIndexValid(toIndex) || fromIndex == toIndex)
            return false;

        ItemData temp = equippedBags[fromIndex]; // swap 임시값
        equippedBags[fromIndex] = equippedBags[toIndex];
        equippedBags[toIndex] = temp;
        RefreshSlotsAfterDataChange();
        return true;
    }

    private void ApplyEquippedBagStatBonuses()
    {
        ResolveBagStatTargets();
        PlayerAccountInventoryService.Instance?.RefreshBagBonuses(playerMovement, playerStaminaController, playerHealth);
    }

    private void ResolveBagStatTargets()
    {
        // 현재 플레이어는 PlayerContext가 안다. 씬 전체 탐색은 PlayerContext가 없는 개발용 씬을 위한 예비로만 남긴다.
        PlayerContext context = PlayerContext.Instance;
        if (playerMovement == null)
            playerMovement = context != null && context.CurrentActorMovement != null ? context.CurrentActorMovement : FindFirstObjectByType<PlayerMovement>();

        if (playerStaminaController == null)
            playerStaminaController = context != null && context.CurrentActorStaminaController != null ? context.CurrentActorStaminaController : ResolvePlayerComponent<PlayerStaminaController>();

        if (playerHealth == null)
            playerHealth = context != null && context.CurrentActorHealth != null ? context.CurrentActorHealth : ResolvePlayerComponent<CombatHealth>();
    }

    private T ResolvePlayerComponent<T>() where T : Component
    {
        if (playerMovement != null)
        {
            T component = playerMovement.GetComponent<T>();
            if (component != null)
                return component;

            component = playerMovement.GetComponentInParent<T>();
            if (component != null)
                return component;

            component = playerMovement.GetComponentInChildren<T>();
            if (component != null)
                return component;
        }

        return FindFirstObjectByType<T>();
    }

    private int GetUnlockedInventorySlotCount()
    {
        return CalculateUnlockedInventorySlotCount(equippedBags);
    }

    private int CalculateUnlockedInventorySlotCount(ItemData[] bags)
    {
        int total = Mathf.Max(0, baseInventorySlotCount); // 기본 칸

        if (bags != null)
        {
            for (int i = 0; i < bags.Length; i++)
            {
                if (bags[i] != null && bags[i].baseData is BagItemData bagData)
                    total += Mathf.Max(0, bagData.additionalSlots); // 가방 보너스
            }
        }

        int maxSlots = inventorySlots != null && inventorySlots.Length > 0 ? inventorySlots.Length : inventory != null ? inventory.Capacity : total; // UI 한계
        return Mathf.Clamp(total, 0, maxSlots);
    }

    private ItemData[] CopyEquippedBags()
    {
        ItemData[] copy = new ItemData[EquippedBagSlotCount]; // 얕은 복사

        if (equippedBags == null)
            return copy;

        int copyCount = Mathf.Min(copy.Length, equippedBags.Length);
        for (int i = 0; i < copyCount; i++)
            copy[i] = equippedBags[i];

        return copy;
    }

    private bool IsBagItem(ItemData item)
    {
        return item != null && item.itemType == "Bag" && item.baseData is BagItemData;
    }

    private bool IsBagSlotIndexValid(int index)
    {
        return equippedBags != null && index >= 0 && index < equippedBags.Length;
    }
}
