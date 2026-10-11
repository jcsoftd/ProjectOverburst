using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows sample stock in the management scene without opening a gameplay trade session.</summary>
public sealed class OverburstUIShopPreview : MonoBehaviour
{
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private OverburstUIWorkshop workshop;
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI merchantNameText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private SlotUI[] merchantSlots;
    [SerializeField] private ConsumableItemData[] sampleItems;

    public bool IsVisible => shopPanel != null && shopPanel.activeInHierarchy;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
        Hide();
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
    }

    public void Close()
    {
        if (workshop != null)
            workshop.ShowHud();
        else
            Hide();
    }

    public void Show()
    {
        if (shopPanel == null)
            return;

        shopPanel.SetActive(true);
        if (titleText != null) titleText.text = "상점 거래";
        if (merchantNameText != null) merchantNameText.text = "잡화상인 · 전시";
        if (statusText != null) statusText.text = "관리씬 표본 · 거래 데이터 없음";
        foreach(var label in shopPanel.GetComponentsInChildren<TMP_Text>(true))
        {
            if(label.name=="DescriptionText")label.text="메인 마을 · 잡화점";
            else if(label.name=="PortraitCategoryText")label.text="잡화";
            else if(label.name=="ReputationLevelText")label.text="우호도 Lv.0";
            else if(label.name=="ReputationGradeText")label.text="기본";
            else if(label.name=="ExpPercentText")label.text="0 / 30  (0%)";
            else if(label.name=="ReputationDiscountText")label.text="0 %";
            else if(label.name=="MerchantGoldInfoText")label.text="996 <size=13>G</size>";
        }
        var portrait=shopPanel.GetComponentsInChildren<RawImage>(true).FirstOrDefault(p=>p.name=="Merchant Portrait");
        if(portrait)portrait.texture=Resources.Load<Texture2D>("UI/Quests/GeneralMerchantPortrait");
        shopPanel.GetComponent<OverburstUIShopSkin>()?.RefreshPresentation();

        if (merchantSlots == null || sampleItems == null)
            return;
        int count = Mathf.Min(merchantSlots.Length, sampleItems.Length);
        for (int i = 0; i < count; i++)
        {
            if (merchantSlots[i] == null || sampleItems[i] == null)
                continue;
            merchantSlots[i].SetDisplayItem(
                new ItemData(sampleItems[i], 1, sampleItems[i].defaultGrade, 2 + i));
        }
    }

    public void Hide()
    {
        if (shopPanel != null)
            shopPanel.SetActive(false);
    }
}
