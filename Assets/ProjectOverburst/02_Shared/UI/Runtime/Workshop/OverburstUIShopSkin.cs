using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reuses the approved window typography and tabs while ShopUI owns trade state.</summary>
public sealed class OverburstUIShopSkin : MonoBehaviour
{
    public const float MerchantWidth = 608, TradeWidth = 448, WindowHeight = 776;
    public const float MerchantX = -544, TradeX = 0, InventoryX = 544, WindowY = 64;
    [Serializable] public struct Label
    {
        public TMP_Text source;
        public Text target;
    }
    [Serializable] public struct Tab
    {
        public Button button;
        public GameObject selected;
    }

    [SerializeField] private Label[] labels = Array.Empty<Label>();
    [SerializeField] private Tab[] tabs = Array.Empty<Tab>();
    [SerializeField] private Button[] headerCloseButtons = Array.Empty<Button>();
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject contextBlocker;
    [SerializeField] private GameObject failurePopup;
    private ShopUI owner;

    public void Configure(Label[] text, Tab[] tabArtwork, Button[] closeHeaders, Button close, GameObject context, GameObject failure)
    {
        labels = text; tabs = tabArtwork; headerCloseButtons = closeHeaders; closeButton = close;
        contextBlocker = context; failurePopup = failure;
    }

    private void OnEnable()
    {
        owner = GetComponentInParent<ShopUI>(true);
        if (owner) owner.transform.SetAsLastSibling();
        foreach (var button in headerCloseButtons)
            if (button) button.onClick.AddListener(Close);
        RefreshPresentation();
    }

    private void OnDisable()
    {
        foreach (var button in headerCloseButtons)
            if (button) button.onClick.RemoveListener(Close);
    }

    private void LateUpdate() => RefreshPresentation();

    public void RefreshPresentation()
    {
        // The existing inventory can receive focus after opening the shop. Keep its shop modal above it.
        if (owner && ((contextBlocker && contextBlocker.activeInHierarchy) || (failurePopup && failurePopup.activeInHierarchy))
            && owner.transform.GetSiblingIndex() != owner.transform.parent.childCount - 1)
            owner.transform.SetAsLastSibling();
        foreach (var label in labels)
            if (label.source && label.target && label.target.text != label.source.text)
                label.target.text = label.source.text;
        foreach (var tab in tabs)
        {
            if (!tab.selected) continue;
            bool selected = owner ? owner.IsSelectedTab(tab.button) : tab.button && tab.button.name == "TradeButton";
            if (tab.selected.activeSelf != selected) tab.selected.SetActive(selected);
        }
    }

    private void Close()
    {
        if (owner) owner.Close();
        else if (closeButton) closeButton.onClick.Invoke();
    }
}
