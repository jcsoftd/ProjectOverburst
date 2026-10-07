using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Presentation only. The existing settings panel remains the owner of options, rebinding and saving.
public sealed class OverburstSettingsGothicView : MonoBehaviour
{
    public OverburstSettingsPanel panel;
    public TMP_Text pageTitle, pageDescription, helpTitle, helpDescription, helpValue, helpTip;
    public Image helpIcon;
    public Image[] tabBackgrounds, tabIcons, tabBorders;
    public GameObject saveHint;
    public TMP_Text[] tabLabels;
    public ScrollRect[] scrolls;
    public OverburstSettingsGothicFoldout[] foldouts;
    public OverburstSettingsGothicRow[] rows;

    static readonly string[] Titles = { "소리", "화면", "전투 표시", "조작" };
    static readonly string[] Descriptions = {
        "음량과 소리 재생 방식을 조절합니다.", "플레이할 화면과 프레임을 설정합니다.",
        "타격의 무게감과 전투 가독성을 조절합니다.", "항목을 눌러 사용할 키를 변경합니다."
    };
    OverburstSettingsGothicRow selected;
    bool refreshPending, layoutPending, wasRebinding;
    int hoveredTab = -1;
    public OverburstSettingsGothicRow SelectedRow => selected;

    void Awake()
    {
        for (int i = 0; i < panel.tabs.Length; i++)
        {
            int tab = i;
            panel.tabs[i].onValueChanged.AddListener(on => { if (on) RefreshTab(tab); });
            var trigger = panel.tabs[i].GetComponent<EventTrigger>() ?? panel.tabs[i].gameObject.AddComponent<EventTrigger>();
            AddTabEvent(trigger, EventTriggerType.PointerEnter, () => { hoveredTab = tab; RefreshTabStyle(); });
            AddTabEvent(trigger, EventTriggerType.PointerExit, () => { if (hoveredTab == tab) hoveredTab = -1; RefreshTabStyle(); });
        }
        panel.resetButton.onClick.AddListener(RequestRefresh);
        panel.closeButton.onClick.AddListener(RequestRefresh);
        panel.footerCloseButton.onClick.AddListener(RequestRefresh);
        if (panel.bloodResetButton) panel.bloodResetButton.onClick.AddListener(RequestRefresh);
    }

    void OnEnable() { RefreshTab(CurrentTab()); RequestRefresh(); }
    void LateUpdate()
    {
        bool rebinding = panel.keyPrompt.activeSelf;
        if (wasRebinding && !rebinding) RequestRefresh();
        wasRebinding = rebinding;
        if (layoutPending) RebuildLayout();
        if (!refreshPending) return;
        refreshPending = false;
        if (!selected || !selected.gameObject.activeInHierarchy) SelectFirstVisible();
        RefreshHelp();
        if (saveHint) saveHint.SetActive(string.IsNullOrEmpty(panel.statusText.text));
    }

    public void RequestRefresh() => refreshPending = true;
    public void RequestLayout() { layoutPending = true; refreshPending = true; }
    public int CurrentTab()
    {
        for (int i = 0; i < panel.tabs.Length; i++) if (panel.tabs[i].isOn) return i;
        return 0;
    }

    public void RefreshTab(int tab)
    {
        foreach (var scroll in scrolls)
            if (scroll && scroll.viewport) scroll.viewport.GetComponent<OverburstSettingsSmoothScroll>()?.Cancel();
        pageTitle.text = Titles[tab]; pageDescription.text = Descriptions[tab];
        RefreshTabStyle();
        if (selected) selected.SetHighlighted(false);
        selected = null;
        RequestRefresh();
    }

    static void AddTabEvent(EventTrigger trigger, EventTriggerType type, Action callback)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => callback());
        trigger.triggers.Add(entry);
    }

    void RefreshTabStyle()
    {
        int tab = CurrentTab();
        for (int i = 0; i < panel.tabs.Length; i++)
        {
            bool active = i == tab;
            bool hovered = i == hoveredTab;
            tabBackgrounds[i].color = active ? new Color(.23f,.14f,.10f,.94f)
                : hovered ? new Color(.16f,.13f,.10f,.85f) : Color.clear;
            tabLabels[i].color = active ? new Color(.91f,.76f,.49f,1f)
                : hovered ? new Color(.84f,.74f,.58f,1f) : new Color(.66f,.60f,.50f,1f);
            tabIcons[i].color = tabLabels[i].color;
            foreach (var edge in tabBorders[i].GetComponentsInChildren<Image>(true))
                edge.color = active ? new Color(.51f,.38f,.26f,1f) : Color.clear;
        }
    }

    public void SelectRow(OverburstSettingsGothicRow row, bool navigate = false)
    {
        if (!row || !row.gameObject.activeInHierarchy) return;
        if (selected != row)
        {
            if (selected) selected.SetHighlighted(false);
            selected = row; row.SetHighlighted(true);
        }
        RequestRefresh();
        if (navigate) KeepVisible(row);
    }

    void SelectFirstVisible()
    {
        int tab = CurrentTab();
        foreach (var row in rows)
            if (row && row.tabIndex == tab && row.gameObject.activeInHierarchy) { SelectRow(row); return; }
    }

    public void RefreshHelp()
    {
        if (!selected) return;
        helpTitle.text = selected.title;
        helpDescription.text = selected.CurrentDescription;
        helpValue.text = selected.DisplayValue();
        helpTip.text = selected.tip;
        helpIcon.sprite = selected.icon;
    }

    public void RebuildLayout()
    {
        layoutPending = false;
        foreach (var fold in foldouts) if (fold && fold.gameObject.activeInHierarchy) fold.Recalculate();
        foreach (var scroll in scrolls)
            if (scroll && scroll.content && scroll.gameObject.activeInHierarchy) LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        Canvas.ForceUpdateCanvases();
    }

    void KeepVisible(OverburstSettingsGothicRow row)
    {
        var scroll = row.GetComponentInParent<ScrollRect>();
        if (!scroll || !scroll.viewport || !scroll.content) return;
        scroll.viewport.GetComponent<OverburstSettingsSmoothScroll>()?.Cancel();
        RebuildLayout();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, row.transform);
        var view = scroll.viewport.rect;
        float offset = bounds.max.y > view.yMax ? bounds.max.y - view.yMax
            : bounds.min.y < view.yMin ? bounds.min.y - view.yMin : 0f;
        if (Mathf.Abs(offset) < .01f) return;
        var pos = scroll.content.anchoredPosition; pos.y -= offset;
        pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f,scroll.content.rect.height-view.height));
        scroll.StopMovement(); scroll.content.anchoredPosition = pos;
    }

    public void PlayClick() { if (OverburstGameMenu.Instance) OverburstGameMenu.Instance.PlayClick(); }
}
