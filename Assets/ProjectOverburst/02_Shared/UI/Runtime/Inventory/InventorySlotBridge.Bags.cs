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
        if (!IsBagSlotIndexValid(bagSlotIndex) || !TryCreateInventorySourceSnapshot(sourceSlot, "Bag", out InventorySourceSnapshot source))
            return SlotMoveResult.Fail("Source bag is invalid.");
        return PlayerAccountInventoryService.TryEquipBagFromInventorySlot(inventory, source.SlotIndex, source.Item, bagSlotIndex, baseInventorySlotCount)
            ? SlotMoveResult.Success() : SlotMoveResult.Fail("Failed to exchange bag ownership.");
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
        if (!IsBagSlotIndexValid(bagSlotIndex)) return SlotMoveResult.Fail("Bag slot is invalid.");
        return PlayerAccountInventoryService.TryUnequipBagToInventorySlot(inventory, targetSlotIndex, equippedBags[bagSlotIndex], bagSlotIndex, baseInventorySlotCount)
            ? SlotMoveResult.Success() : SlotMoveResult.Fail("Failed to return equipped bag.");
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
        PlayerAccountInventoryService.Instance?.RefreshBagBonuses(playerMovement, playerHealth);
    }

    private void ResolveBagStatTargets()
    {
        // 현재 플레이어는 PlayerContext가 안다. 씬 전체 탐색은 PlayerContext가 없는 개발용 씬을 위한 예비로만 남긴다.
        PlayerContext context = PlayerContext.Instance;
        if (playerMovement == null)
            playerMovement = context != null && context.CurrentActorMovement != null ? context.CurrentActorMovement : FindFirstObjectByType<PlayerMovement>();

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
        return inventory != null ? inventory.UnlockedSlotCount : 0;
    }

    private int CalculateUnlockedInventorySlotCount(ItemData[] bags)
    {
        return inventory != null ? inventory.CalculateUnlockedSlotCount(bags,
            Overburst.Persistence.AccountGameplaySession.Current?.BaseUnlockedSlots ?? baseInventorySlotCount) : 0;
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
