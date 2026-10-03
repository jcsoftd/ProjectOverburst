using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ShopUI partial: 거래 가능 판정·제안 추가와 제거·확정. 필드와 Unity 수명주기는 ShopUI.cs에 있다.
public partial class ShopUI
{
    public static bool TryGetOpenTooltipPriceContext(SlotUI slot, out MerchantDefinition merchant, out bool merchantSelling)
    {
        merchant = null;
        merchantSelling = false;
        if (openShop == null || !openShop.IsOpen || slot == null || slot.DisplayItem == null)
            return false;

        return openShop.TryGetTooltipPriceContext(slot, out merchant, out merchantSelling);
    }

    private bool TryHandleAllowedShopDrop(SlotUI originSlot, SlotUI targetSlot)
    {
        if (!IsOpen || originSlot == null || targetSlot == null || tradeService == null)
            return false;

        ShopContextMenuTarget originTarget;
        ShopContextMenuTarget targetTarget;
        TryGetShopContextTarget(originSlot, out originTarget);
        TryGetShopContextTarget(targetSlot, out targetTarget);
        bool originIsPlayerInventory = IsPlayerInventoryTradeSlot(originSlot);
        bool targetIsPlayerInventory = IsPlayerInventoryTradeSlot(targetSlot);

        if (originTarget == ShopContextMenuTarget.MerchantInventory && targetTarget == ShopContextMenuTarget.MerchantOffer)
        {
            string message;
            if (!CanAddMerchantOffer(originSlot, out message))
            {
                SetStatus(message);
                ShowFailurePopup(message);
                return true;
            }

            return ToggleOfferFromContextTarget(ShopContextMenuTarget.MerchantInventory, originSlot, 0);
        }

        if (originIsPlayerInventory && targetTarget == ShopContextMenuTarget.PlayerOffer)
            return ToggleOfferFromContextTarget(ShopContextMenuTarget.PlayerInventory, originSlot, 0);

        if (originTarget == ShopContextMenuTarget.MerchantOffer && targetTarget == ShopContextMenuTarget.MerchantInventory)
            return RemoveOfferFromSlot(MerchantTradeOfferSide.Merchant, originSlot);

        if (originTarget == ShopContextMenuTarget.PlayerOffer && targetIsPlayerInventory)
            return RemoveOfferFromSlot(MerchantTradeOfferSide.Player, originSlot);

        return false;
    }

    private bool IsAllowedShopDrop(SlotUI originSlot, SlotUI targetSlot)
    {
        if (!IsOpen || originSlot == null || targetSlot == null || tradeService == null)
            return false;

        ShopContextMenuTarget originTarget;
        ShopContextMenuTarget targetTarget;
        TryGetShopContextTarget(originSlot, out originTarget);
        TryGetShopContextTarget(targetSlot, out targetTarget);

        if (originTarget == ShopContextMenuTarget.MerchantInventory && targetTarget == ShopContextMenuTarget.MerchantOffer)
            return CanShopContextTrade(ShopContextMenuTarget.MerchantInventory, originSlot);

        if (IsPlayerInventoryTradeSlot(originSlot) && targetTarget == ShopContextMenuTarget.PlayerOffer)
            return CanShopContextTrade(ShopContextMenuTarget.PlayerInventory, originSlot);

        if (originTarget == ShopContextMenuTarget.MerchantOffer && targetTarget == ShopContextMenuTarget.MerchantInventory)
            return true;

        if (originTarget == ShopContextMenuTarget.PlayerOffer && IsPlayerInventoryTradeSlot(targetSlot))
            return true;

        return false;
    }

    private bool RemoveOfferFromSlot(MerchantTradeOfferSide side, SlotUI slot)
    {
        int index = side == MerchantTradeOfferSide.Merchant
            ? IndexOf(merchantOfferSlots, slot)
            : IndexOf(playerOfferSlots, slot);

        if (index < 0 || tradeService == null)
            return false;

        bool removed = tradeService.RemoveOffer(side, index);
        if (removed)
            SetStatus(side == MerchantTradeOfferSide.Merchant ? "상인 제안에서 제거했습니다."
                : WithInventorySortNotice("플레이어 제안에서 제거했습니다."));

        Refresh();
        return removed;
    }

    private bool IsPlayerInventoryTradeSlot(SlotUI slot)
    {
        return slot != null
            && slot.OwnerBridge is InventorySlotBridge
            && !slot.IsWeaponSlot
            && !slot.IsBagSlot
            && !slot.IsLocked
            && slot.DisplayItem != null;
    }

    private bool CanAddMerchantOffer(SlotUI slot, out string message)
    {
        ItemData item = slot != null ? slot.DisplayItem : null;
        return MerchantTemporaryArtifactStockPolicy.CanAddMerchantOffer(currentMerchant, item, out message);
    }

    private bool TryGetTooltipPriceContext(SlotUI slot, out MerchantDefinition merchant, out bool merchantSelling)
    {
        merchant = currentMerchant;
        merchantSelling = false;
        if (merchant == null || slot == null || slot.DisplayItem == null)
            return false;

        if (IndexOf(merchantInventorySlots, slot) >= 0 || IndexOf(merchantOfferSlots, slot) >= 0)
        {
            merchantSelling = true;
            return true;
        }

        if (IndexOf(playerOfferSlots, slot) >= 0 || IsPlayerInventoryTradeSlot(slot))
            return true;

        return false;
    }

    private bool HasOfferCapacity(MerchantTradeOfferSide side, int sourceSlotIndex)
    {
        if (tradeService == null)
            return false;

        MerchantTradeSession session = tradeService.Session;
        if (session.ContainsSource(side, sourceSlotIndex))
            return true;

        int count = side == MerchantTradeOfferSide.Merchant ? session.MerchantOffers.Count : session.PlayerOffers.Count;
        return count < session.MaxOffersPerSide;
    }

    private bool IsDirectTradeBlockedCurrency(ItemData item)
    {
        return item != null && item.baseData is CurrencyItemData;
    }

    private bool IsStackableTradeItem(ItemData item)
    {
        if (item == null || item.baseData is CurrencyItemData)
            return false;

        if (item.baseData is ConsumableItemData consumableData && consumableData.IsPermanentSingleItem)
            return false;

        string itemType = item.itemType;
        return itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem";
    }

    private void HandleConfirmClicked()
    {
        if (tradeService == null || failurePopupOpen || !IsTradeTabActive)
            return;

        MerchantTradeResult validation = tradeService.ValidateTrade();
        if (!validation.success)
        {
            SetStatus(validation.message);
            ShowFailurePopup(validation.message);
            Refresh();
            return;
        }

        MerchantTradeResult result = tradeService.ExecuteTrade();
        SetStatus(result.message);
        if (!result.success)
            ShowFailurePopup(result.message);

        Refresh();
    }

    private void HandleClearClicked()
    {
        if (!IsTradeTabActive)
            return;

        if (tradeService != null)
            tradeService.Session.Clear();

        SetStatus("거래창을 비웠습니다.");
        Refresh();
    }

    private void OpenPlayerInventoryWindow()
    {
        if (inventoryUI == null)
            return;

        previousInventoryVisible = inventoryUI.IsVisible;
        inventoryUI.SetVisible(true);
        inventoryUI.InputToggleLocked = true;
        inventoryToggleLockedByShop = true;
    }

    private void RestorePlayerInventoryWindow()
    {
        if (inventoryUI == null)
            return;

        if (inventoryToggleLockedByShop)
        {
            inventoryUI.InputToggleLocked = false;
            inventoryToggleLockedByShop = false;
        }

        inventoryUI.SetVisible(previousInventoryVisible);
    }
}
