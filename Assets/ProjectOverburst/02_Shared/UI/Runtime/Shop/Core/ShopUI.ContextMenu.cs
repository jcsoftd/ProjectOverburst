using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ShopUI partial: 우클릭 메뉴. 필드와 Unity 수명주기는 ShopUI.cs에 있다.
public partial class ShopUI
{
    public static bool TryOpenPlayerInventoryShopContextMenu(SlotClickContext context)
    {
        if (openShop == null || !openShop.IsOpen)
            return false;

        openShop.OpenPlayerInventoryContextMenu(context);
        return true;
    }

    private void OpenPlayerInventoryContextMenu(SlotClickContext context)
    {
        if (context == null || context.Slot == null || context.Item == null)
            return;

        if (!IsTradeTabActive)
        {
            SetStatus(GetInputBlockedByTabMessage());
            return;
        }

        EnsureContextMenu();
        if (contextMenu == null)
        {
            SetStatus("상점 거래 메뉴가 연결되지 않았습니다.");
            return;
        }

        contextMenu.Open(ShopContextMenuTarget.PlayerInventory, context.Slot, context.EventData);
    }

    public bool CanShopContextTrade(ShopContextMenuTarget target, SlotUI slot)
    {
        if (!IsOpen || slot == null || slot.DisplayItem == null || tradeService == null || slot.IsLocked)
            return false;

        if (!IsTradeTabActive)
            return false;

        if (IsDirectTradeBlockedCurrency(slot.DisplayItem))
            return false;

        switch (target)
        {
            case ShopContextMenuTarget.MerchantInventory:
                string message;
                return IndexOf(merchantInventorySlots, slot) >= 0
                    && CanAddMerchantOffer(slot, out message)
                    && HasOfferCapacity(MerchantTradeOfferSide.Merchant, IndexOf(merchantInventorySlots, slot));
            case ShopContextMenuTarget.PlayerInventory:
                return IsPlayerInventoryTradeSlot(slot) && HasOfferCapacity(MerchantTradeOfferSide.Player, slot.SlotIndex);
            default:
                return false;
        }
    }

    public bool CanShopContextSplitTrade(ShopContextMenuTarget target, SlotUI slot)
    {
        return CanShopContextTrade(target, slot)
            && slot.DisplayItem.stackCount > 1
            && IsStackableTradeItem(slot.DisplayItem);
    }

    public bool HandleShopContextTrade(ShopContextMenuTarget target, SlotUI slot)
    {
        return ToggleOfferFromContextTarget(target, slot, 0);
    }

    public bool HandleShopContextSplitTrade(ShopContextMenuTarget target, SlotUI slot, int amount)
    {
        if (!CanShopContextSplitTrade(target, slot))
        {
            SetStatus("나눠서 거래할 수 없는 아이템입니다.");
            return false;
        }

        int clamped = Mathf.Clamp(amount, 1, Mathf.Max(1, slot.DisplayItem.stackCount - 1));
        return ToggleOfferFromContextTarget(target, slot, clamped);
    }

    public bool HandleShopContextRemoveOffer(ShopContextMenuTarget target, SlotUI slot)
    {
        if (slot == null || tradeService == null)
            return false;

        if (target == ShopContextMenuTarget.MerchantOffer)
            return RemoveOfferFromSlot(MerchantTradeOfferSide.Merchant, slot);

        if (target == ShopContextMenuTarget.PlayerOffer)
            return RemoveOfferFromSlot(MerchantTradeOfferSide.Player, slot);

        return false;
    }

    public void ShowShopContextItemInfo(ItemData item)
    {
        if (item == null)
            return;

        TooltipManager.Instance?.ShowTooltip(item);
    }

    public void ShowShopContextStatus(string message)
    {
        SetStatus(message);
    }

    public string GetShopContextBlockedTradeMessage(ShopContextMenuTarget target, SlotUI slot)
    {
        ItemData item = slot != null ? slot.DisplayItem : null;
        if (item != null && item.baseData is CurrencyItemData currencyData)
        {
            return currencyData.currencyType == CurrencyType.Gold
                ? "Gold는 거래창에 올리지 않고 부족분 자동 결제로 사용합니다."
                : "재화 아이템은 1차 거래창에 올릴 수 없습니다.";
        }

        if (target == ShopContextMenuTarget.MerchantInventory || target == ShopContextMenuTarget.PlayerInventory)
        {
            string message;
            if (target == ShopContextMenuTarget.MerchantInventory && !CanAddMerchantOffer(slot, out message))
                return message;

            return "거래창이 가득 찼거나 거래할 수 없는 슬롯입니다.";
        }

        return string.Empty;
    }

    private bool ToggleOfferFromContextTarget(ShopContextMenuTarget target, SlotUI slot, int requestedStackCount)
    {
        if (slot == null || tradeService == null)
            return false;

        string message = string.Empty;
        bool changed = false;
        switch (target)
        {
            case ShopContextMenuTarget.MerchantInventory:
                int merchantIndex = IndexOf(merchantInventorySlots, slot);
                if (!CanAddMerchantOffer(slot, out message))
                {
                    SetStatus(message);
                    ShowFailurePopup(message);
                    Refresh();
                    return false;
                }

                if (merchantIndex >= 0)
                    changed = tradeService.ToggleMerchantOffer(merchantIndex, requestedStackCount, out message);
                break;
            case ShopContextMenuTarget.PlayerInventory:
                if (IsPlayerInventoryTradeSlot(slot))
                    changed = tradeService.TogglePlayerOffer(slot.SlotIndex, requestedStackCount, out message);
                break;
        }

        if (!string.IsNullOrWhiteSpace(message))
            SetStatus(changed && target == ShopContextMenuTarget.PlayerInventory
                ? WithInventorySortNotice(message) : message);

        Refresh();
        return changed;
    }

    private bool TryGetShopContextTarget(SlotUI slot, out ShopContextMenuTarget target)
    {
        if (IndexOf(merchantInventorySlots, slot) >= 0)
        {
            target = ShopContextMenuTarget.MerchantInventory;
            return true;
        }

        if (IndexOf(merchantOfferSlots, slot) >= 0)
        {
            target = ShopContextMenuTarget.MerchantOffer;
            return true;
        }

        if (IndexOf(playerOfferSlots, slot) >= 0)
        {
            target = ShopContextMenuTarget.PlayerOffer;
            return true;
        }

        target = default;
        return false;
    }

    private void EnsureContextMenu()
    {
        if (contextMenu == null)
            contextMenu = GetComponent<ShopContextMenuController>() ?? GetComponentInParent<ShopContextMenuController>(true);

        if (contextMenu == null)
        {
            Debug.LogError("[ShopUI] ShopContextMenuController reference is missing. Run the Shop Context UI objectizer.", this);
            return;
        }

        contextMenu.Init(this, koreanFontAsset);
    }
}
