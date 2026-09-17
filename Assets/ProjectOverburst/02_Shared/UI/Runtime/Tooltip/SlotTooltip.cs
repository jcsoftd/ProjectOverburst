using UnityEngine.EventSystems;

public class SlotTooltip : UnityEngine.MonoBehaviour, IPointerEnterHandler, IPointerExitHandler // 슬롯 툴팁
{
    private SlotUI slotUI;

    public void Init(SlotUI slot)
    {
        slotUI = slot; // 표시 슬롯
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (slotUI == null || slotUI.DisplayItem == null || !slotUI.DisplayItem.HasValidBaseData || TooltipManager.Instance == null)
            return; // 표시 대상 없음

        MerchantDefinition merchant;
        bool merchantSelling;
        if (ShopUI.TryGetOpenTooltipPriceContext(slotUI, out merchant, out merchantSelling))
            TooltipManager.Instance.ShowTooltip(slotUI.DisplayItem, merchant, merchantSelling);
        else
            TooltipManager.Instance.ShowTooltip(slotUI.DisplayItem);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipManager.Instance != null)
            TooltipManager.Instance.HideTooltip(); // 이탈 시 숨김
    }
}
