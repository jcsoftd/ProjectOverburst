using TMPro;
using UnityEngine.UI;

public sealed class ShopTradeSummaryView
{
    public TextMeshProUGUI MerchantValueText;
    public TextMeshProUGUI PlayerValueText;
    public TextMeshProUGUI AutoGoldText;
    public TextMeshProUGUI GoldSummaryText;
    public Button ConfirmButton;
}

public sealed class ShopTradeSummaryPresenter
{
    public void Refresh(ShopTradeSummaryView view, MerchantTradeService tradeService, bool failurePopupOpen)
    {
        if (view == null || tradeService == null)
            return;

        int merchantValue = tradeService.GetMerchantOfferValue();
        int playerValue = tradeService.GetPlayerOfferValue();
        int autoGold = tradeService.GetAutoGoldCost();
        int merchantPayout = tradeService.GetMerchantGoldPayout();
        int inventoryGold = tradeService.GetInventoryGoldAmount();
        int stashGold = tradeService.GetStashGoldAmount();

        if (view.MerchantValueText != null)
            view.MerchantValueText.text = "상인 제안 가치 : " + merchantValue + "G";

        if (view.PlayerValueText != null)
            view.PlayerValueText.text = "플레이어 제안 가치 : " + playerValue + "G";

        if (view.AutoGoldText != null)
            view.AutoGoldText.text = GoldSummaryTextFormatter.FormatTradeGoldDelta(autoGold, merchantPayout);

        if (view.GoldSummaryText != null)
        {
            bool hasStashGoldSource = tradeService.HasStashCurrencySource;
            view.GoldSummaryText.text = GoldSummaryTextFormatter.FormatPlayerGold(inventoryGold, stashGold, hasStashGoldSource);
        }

        if (view.ConfirmButton != null)
            view.ConfirmButton.interactable = !failurePopupOpen;
    }
}
