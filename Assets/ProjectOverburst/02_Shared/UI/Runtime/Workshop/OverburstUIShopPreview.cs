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
