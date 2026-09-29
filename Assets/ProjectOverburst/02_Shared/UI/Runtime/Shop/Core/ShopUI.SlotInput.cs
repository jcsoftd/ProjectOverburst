using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ShopUI partial: 상점·인벤토리 슬롯 클릭과 드래그. 필드와 Unity 수명주기는 ShopUI.cs에 있다.
public partial class ShopUI
{
    public static bool TryHandleOpenPlayerInventorySlot(int slotIndex, out string message)
    {
        message = string.Empty;
        if (openShop == null || !openShop.IsOpen)
            return false;

        return openShop.HandlePlayerInventorySlotClicked(slotIndex, out message);
    }

    public static bool TryConsumeOpenPlayerInventorySingleClick()
    {
        if (openShop == null || !openShop.IsOpen)
            return false;

        if (!openShop.IsTradeTabActive)
        {
            openShop.SetStatus(openShop.GetInputBlockedByTabMessage());
            return true;
        }

        openShop.SetStatus("거래 등록은 더블클릭, 드래그, 우클릭 메뉴로 처리합니다.");
        return true;
    }

    public bool HandleShopSlotClicked(SlotUI slot)
    {
        if (!IsOpen || slot == null || tradeService == null)
            return false;

        if (!IsTradeTabActive)
            return true;

        int index = IndexOf(merchantInventorySlots, slot);
        if (index >= 0)
        {
            string message;
            if (!CanAddMerchantOffer(slot, out message))
            {
                SetStatus(message);
                ShowFailurePopup(message);
                return true;
            }

            tradeService.ToggleMerchantOffer(index, out message);
            SetStatus(message);
            Refresh();
            return true;
        }

        index = IndexOf(merchantOfferSlots, slot);
        if (index >= 0)
        {
            if (tradeService.RemoveOffer(MerchantTradeOfferSide.Merchant, index))
                SetStatus("상인 제안에서 제거했습니다.");

            Refresh();
            return true;
        }

        index = IndexOf(playerOfferSlots, slot);
        if (index >= 0)
        {
            if (tradeService.RemoveOffer(MerchantTradeOfferSide.Player, index))
                SetStatus("플레이어 제안에서 제거했습니다.");

            Refresh();
            return true;
        }

        return false;
    }

    public bool HandleShopSlotSingleClicked(SlotUI slot)
    {
        if (!IsOpen || slot == null || !ContainsShopSlot(slot))
            return false;

        if (!IsTradeTabActive)
            return true;

        SetStatus("거래 등록은 더블클릭, 드래그, 우클릭 메뉴로 처리합니다.");
        return true;
    }

    public bool HandleShopSlotRightClicked(SlotUI slot, PointerEventData eventData)
    {
        if (!IsOpen || slot == null || !ContainsShopSlot(slot))
            return false;

        if (!IsTradeTabActive)
            return true;

        if (slot.DisplayItem == null)
            return true;

        ShopContextMenuTarget target;
        if (!TryGetShopContextTarget(slot, out target))
            return true;

        EnsureContextMenu();
        if (contextMenu == null)
        {
            SetStatus("상점 거래 메뉴가 연결되지 않았습니다.");
            return true;
        }

        contextMenu.Open(target, slot, eventData);
        return true;
    }

    public bool HandleShopSlotDrop(SlotUI originSlot, SlotUI targetSlot)
    {
        if (!ContainsShopSlot(originSlot) && !ContainsShopSlot(targetSlot))
            return false;

        if (!IsTradeTabActive)
            return true;

        if (TryHandleAllowedShopDrop(originSlot, targetSlot))
            return true;

        SetStatus("각 아이템은 자기 거래창으로만 드래그할 수 있습니다.");
        return true;
    }

    public bool CanConsumeShopSlotDrop(SlotUI originSlot, SlotUI targetSlot)
    {
        if (!IsTradeTabActive)
            return false;

        return IsAllowedShopDrop(originSlot, targetSlot);
    }

    public bool ContainsShopSlot(SlotUI slot)
    {
        return IndexOf(merchantInventorySlots, slot) >= 0
            || IndexOf(merchantOfferSlots, slot) >= 0
            || IndexOf(playerOfferSlots, slot) >= 0;
    }

    public void ClearShopSlotDragPreview()
    {
        ClearDragOverlays(merchantInventorySlots);
        ClearDragOverlays(merchantOfferSlots);
        ClearDragOverlays(playerOfferSlots);
    }

    private bool HandlePlayerInventorySlotClicked(int sourceSlotIndex, out string message)
    {
        message = string.Empty;
        if (!IsOpen || tradeService == null)
            return false;

        if (!IsTradeTabActive)
        {
            message = GetInputBlockedByTabMessage();
            SetStatus(message);
            return true;
        }

        tradeService.TogglePlayerOffer(sourceSlotIndex, out message);
        SetStatus(message);
        Refresh();
        return true;
    }

    private void ClearDragOverlays(SlotUI[] slots)
    {
        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
            slots[i]?.ClearDragOverlay();
    }
}
