using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ShopUI partial: 거래·퀘스트·특산 탭. 필드와 Unity 수명주기는 ShopUI.cs에 있다.
public partial class ShopUI
{
    public void ShowTradeTab()
    {
        SetActiveTab(ShopTab.Trade, true);
    }

    public void ShowQuestTab()
    {
        SetActiveTab(ShopTab.Quest, true);
    }

    public void ShowFirstSpecialtyTab()
    {
        SetActiveTab(specialtyButtonTabs[0], true);
    }

    public void ShowSecondSpecialtyTab()
    {
        SetActiveTab(specialtyButtonTabs[1], true);
    }

    public void SetActiveTab(ShopTab tab)
    {
        SetActiveTab(tab, true);
    }

    private void SetActiveTab(ShopTab tab, bool closeTransientViews)
    {
        if (!IsTabAvailable(tab))
            tab = IsTabAvailable(ShopTab.Trade) ? ShopTab.Trade : GetFirstAvailableTab();

        activeTab = tab;
        if (closeTransientViews)
        {
            contextMenu?.Close();
            HideFailurePopup();
            ClearShopSlotDragPreview();
        }

        bool tradeVisible = tab == ShopTab.Trade;
        bool questVisible = tab == ShopTab.Quest;
        bool specialtyVisible = IsSpecialtyTab(tab);
        SetGameObjectActive(merchantInventoryWindowRoot, tradeVisible);
        SetGameObjectActive(tradeWindowRoot, tradeVisible);
        SetGameObjectActive(questListRoot, questVisible);
        SetGameObjectActive(questDetailRoot, questVisible);
        SetGameObjectActive(specialtyListRoot, specialtyVisible);
        SetGameObjectActive(specialtyDetailRoot, specialtyVisible);

        if (inventoryUI != null)
        {
            inventoryUI.SetVisible(tradeVisible);
            inventoryUI.InputToggleLocked = true;
            inventoryToggleLockedByShop = true;
        }

        if (questVisible)
        {
            ClearPlayerInventorySelection();
            if (questStatusText != null)
                questStatusText.text = "퀘스트 시스템은 준비 중입니다.";
            SetStatus("퀘스트 탭은 목록 / 상세 / 수락 버튼만 있는 1차 껍데기입니다.");
        }
        else if (specialtyVisible)
        {
            ClearPlayerInventorySelection();
            RefreshSpecialtyTabContent(tab);
            SetStatus(ShopTabDisplayPolicy.GetDisplayName(tab) + " 기능은 준비 중입니다.");
        }
        else if (isOpen)
        {
            SetStatus("더블클릭, 드래그, 우클릭 거래 메뉴로 중앙 거래창에 올립니다.");
        }

        RefreshHeader();
        RefreshTabSelection();
        if (tradeVisible && isOpen)
            Refresh();
    }

    private void RefreshTabSelection()
    {
        RefreshAvailableTabs();
        SetTabButton(tradeTabButton, tradeTabImage, tradeTabText, ShopTab.Trade, IsTabAvailable(ShopTab.Trade));
        SetTabButton(questTabButton, questTabImage, questTabText, ShopTab.Quest, IsTabAvailable(ShopTab.Quest));

        ShopTab firstSpecialty;
        ShopTab secondSpecialty;
        GetSpecialtyTabs(out firstSpecialty, out secondSpecialty);
        specialtyButtonTabs[0] = firstSpecialty;
        specialtyButtonTabs[1] = secondSpecialty;
        SetTabButton(firstSpecialtyTabButton, firstSpecialtyTabImage, firstSpecialtyTabText, firstSpecialty, IsSpecialtyTab(firstSpecialty));
        SetTabButton(secondSpecialtyTabButton, secondSpecialtyTabImage, secondSpecialtyTabText, secondSpecialty, IsSpecialtyTab(secondSpecialty));
    }

    private void SetTabButton(Button button, Image image, TextMeshProUGUI text, ShopTab tab, bool visible)
    {
        if (button != null && button.gameObject.activeSelf != visible)
            button.gameObject.SetActive(visible);

        if (!visible)
            return;

        if (text != null)
            text.text = ShopTabDisplayPolicy.GetDisplayName(tab);

        if (button != null)
            button.interactable = true;

        ApplyTabVisual(image, text, activeTab == tab);
    }

    private void ApplyTabVisual(Image image, TextMeshProUGUI text, bool selected)
    {
        if (image != null)
            image.color = selected
                ? new Color(0.22f, 0.35f, 0.48f, 1f)
                : new Color(0.28f, 0.29f, 0.31f, 1f);

        if (text != null)
        {
            text.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            text.color = selected
                ? new Color(0.95f, 0.98f, 1f, 1f)
                : new Color(0.78f, 0.81f, 0.85f, 1f);
        }
    }

    private void RefreshAvailableTabs()
    {
        availableTabs.Clear();
        if (currentMerchant != null)
            currentMerchant.GetSupportedTabs(availableTabs);

        if (availableTabs.Count == 0)
        {
            availableTabs.Add(ShopTab.Trade);
            availableTabs.Add(ShopTab.Quest);
        }
    }

    private bool IsTabAvailable(ShopTab tab)
    {
        return availableTabs.Contains(tab);
    }

    private ShopTab GetFirstAvailableTab()
    {
        return availableTabs.Count > 0 ? availableTabs[0] : ShopTab.Trade;
    }

    private void GetSpecialtyTabs(out ShopTab first, out ShopTab second)
    {
        first = ShopTab.Trade;
        second = ShopTab.Trade;
        int found = 0;
        for (int i = 0; i < availableTabs.Count; i++)
        {
            ShopTab tab = availableTabs[i];
            if (!IsSpecialtyTab(tab))
                continue;

            if (found == 0)
                first = tab;
            else if (found == 1)
                second = tab;

            found++;
            if (found >= 2)
                return;
        }
    }

    private bool IsSpecialtyTab(ShopTab tab)
    {
        return tab != ShopTab.Trade && tab != ShopTab.Quest;
    }

    private string GetInputBlockedByTabMessage()
    {
        return ShopTabDisplayPolicy.GetInputBlockedMessage(activeTab);
    }

    private void RefreshSpecialtyTabContent(ShopTab tab)
    {
        string label = ShopTabDisplayPolicy.GetDisplayName(tab);
        if (specialtyListTitleText != null)
            specialtyListTitleText.text = label + " 목록";
        if (specialtyListBodyText != null)
            specialtyListBodyText.text = "현재 준비된 항목이 없습니다.";
        if (specialtyDetailTitleText != null)
            specialtyDetailTitleText.text = label;
        if (specialtyDetailDescriptionText != null)
            specialtyDetailDescriptionText.text = ShopTabDisplayPolicy.GetSpecialtyDescription(tab);
        if (specialtyPrimaryTitleText != null)
            specialtyPrimaryTitleText.text = "현재 상태";
        if (specialtyPrimaryBodyText != null)
            specialtyPrimaryBodyText.text = "기능 연결 준비 중";
        if (specialtySecondaryTitleText != null)
            specialtySecondaryTitleText.text = "향후 연결";
        if (specialtySecondaryBodyText != null)
            specialtySecondaryBodyText.text = ShopTabDisplayPolicy.GetSpecialtyFutureText(tab);
        if (specialtyStatusText != null)
            specialtyStatusText.text = label + " 시스템은 준비 중입니다.";
        if (specialtyActionButtonText != null)
            specialtyActionButtonText.text = "준비중";
    }

    private void HandleQuestAcceptClicked()
    {
        if (questStatusText != null)
            questStatusText.text = "퀘스트 시스템은 준비 중입니다.";

        SetStatus("퀘스트 시스템은 준비 중입니다.");
    }

    private void HandleSpecialtyActionClicked()
    {
        string label = ShopTabDisplayPolicy.GetDisplayName(activeTab);
        if (specialtyStatusText != null)
            specialtyStatusText.text = label + " 시스템은 준비 중입니다.";

        SetStatus(label + " 기능은 준비 중입니다.");
    }
}
