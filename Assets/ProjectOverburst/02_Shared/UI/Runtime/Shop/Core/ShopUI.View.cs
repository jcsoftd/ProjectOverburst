using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ShopUI partial: 상단 패널·요약·상태 문구·실패 팝업 표시. 필드와 Unity 수명주기는 ShopUI.cs에 있다.
public partial class ShopUI
{
    private void RefreshHeader()
    {
        SyncMerchantTopPanelViewReferences();
        merchantTopPanelPresenter.Refresh(merchantTopPanelView, activeTab, currentMerchant, tradeService);
        reputationExpBarFill = merchantTopPanelView.ReputationExpBarFill;
    }

    private void SyncMerchantTopPanelViewReferences()
    {
        merchantTopPanelView.HeaderTitleText = titleText;
        merchantTopPanelView.MerchantNameText = merchantNameText;
        merchantTopPanelView.MerchantGoldText = merchantGoldText;
        merchantTopPanelView.MerchantDescriptionText = merchantDescriptionText;
        merchantTopPanelView.Root = merchantTopPanelRoot;
        merchantTopPanelView.FallbackSearchRoot = shopPanel;
        merchantTopPanelView.MerchantPortraitPlaceholderText = merchantPortraitPlaceholderText;
        merchantTopPanelView.MerchantPortraitCategoryText = merchantPortraitCategoryText;
        merchantTopPanelView.ReputationLevelText = reputationLevelText;
        merchantTopPanelView.ReputationExpBarFill = reputationExpBarFill;
        merchantTopPanelView.ReputationExpPercentText = reputationExpPercentText;
        merchantTopPanelView.ReputationGradeText = reputationGradeText;
        merchantTopPanelView.ReputationEffectsTitleText = reputationEffectsTitleText;
        merchantTopPanelView.ReputationDiscountText = reputationDiscountText;
        merchantTopPanelView.ReputationStockGradeText = reputationStockGradeText;
        merchantTopPanelView.MerchantGoldInfoText = merchantGoldInfoText;
    }

    private void SyncTradeSummaryViewReferences()
    {
        tradeSummaryView.MerchantValueText = merchantValueText;
        tradeSummaryView.PlayerValueText = playerValueText;
        tradeSummaryView.AutoGoldText = autoGoldText;
        tradeSummaryView.GoldSummaryText = goldSummaryText;
        tradeSummaryView.ConfirmButton = confirmButton;
    }

    private void SyncFailurePopupViewReferences()
    {
        failurePopupView.Root = failurePopupRoot;
        failurePopupView.MessageText = failurePopupMessageText;
        failurePopupView.PopupConfirmButton = failurePopupConfirmButton;
        failurePopupView.TradeConfirmButton = confirmButton;
    }

    private void RefreshMerchantInventorySlots()
    {
        if (merchantInventorySlots == null || tradeService == null)
            return;

        slotPresenter.RefreshMerchantInventory(
            merchantInventorySlots,
            tradeService.MerchantInventory,
            tradeService.Session);
    }

    private void RefreshOfferSlots()
    {
        slotPresenter.RefreshOffers(merchantOfferSlots, tradeService != null ? tradeService.Session.MerchantOffers : null);
        slotPresenter.RefreshOffers(playerOfferSlots, tradeService != null ? tradeService.Session.PlayerOffers : null);
    }

    private void RefreshSummary()
    {
        SyncTradeSummaryViewReferences();
        tradeSummaryPresenter.Refresh(tradeSummaryView, tradeService, failurePopupOpen);
    }

    private void RefreshPlayerInventorySelection()
    {
        if (inventorySlotBridge == null || tradeService == null)
            return;

        inventorySlotBridge.RefreshSlotsWithOwnershipCheck();
        inventorySlotBridge.SetShopTradeSelectedSlots(tradeService.Session.GetSourceIndices(MerchantTradeOfferSide.Player));
    }

    private void ClearPlayerInventorySelection()
    {
        if (inventorySlotBridge != null)
            inventorySlotBridge.ClearShopTradeSelectedSlots();
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
    }

    private void ApplyFonts()
    {
        if (koreanFontAsset == null)
            return;

        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null)
                continue;

            texts[i].font = koreanFontAsset;
            texts[i].fontSharedMaterial = koreanFontAsset.material;
        }
    }

    private void SetPanelVisible(bool visible)
    {
        if (shopPanel != null)
            shopPanel.SetActive(visible);
    }

    private void SetGameObjectActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private void ShowFailurePopup(string message)
    {
        SyncFailurePopupViewReferences();
        string fallbackStatus;
        if (!failurePopupPresenter.Show(failurePopupView, message, out fallbackStatus))
        {
            LogMissingReference("TradeFailurePopup");
            SetStatus(fallbackStatus);
            return;
        }

        failurePopupOpen = true;
    }

    private void HideFailurePopup()
    {
        SyncFailurePopupViewReferences();
        failurePopupOpen = false;
        failurePopupPresenter.Hide(failurePopupView);
    }
}
