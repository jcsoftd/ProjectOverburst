using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DamageNumberFeelDebugUI : MonoBehaviour
{
    [SerializeField] private Button collapseButton;
    [SerializeField] private GameObject contentRoot;
    [SerializeField] private Button[] presetButtons;
    [SerializeField] private TextMeshProUGUI[] presetLabels;
    [SerializeField] private Button cycleButton;
    [SerializeField] private Button[] fontButtons;
    [SerializeField] private TextMeshProUGUI[] fontLabels;
    [SerializeField] private Button fontCycleButton;
    [SerializeField] private Button[] weightButtons;
    [SerializeField] private TextMeshProUGUI[] weightLabels;
    [SerializeField] private Button weightCycleButton;
    [SerializeField] private Button[] sizeButtons;
    [SerializeField] private TextMeshProUGUI[] sizeLabels;
    [SerializeField] private Button sizeCycleButton;
    [SerializeField] private Button previewButton;
    [SerializeField] private TextMeshProUGUI statusLabel;

    private UnityAction[] presetActions;
    private UnityAction[] fontActions;
    private UnityAction[] weightActions;
    private UnityAction[] sizeActions;
    private UnityAction cycleAction;
    private UnityAction fontCycleAction;
    private UnityAction weightCycleAction;
    private UnityAction sizeCycleAction;
    private UnityAction previewAction;
    private UnityAction collapseAction;
    private UnityAction styleAction;
    private Button styleButton;
    private TextMeshProUGUI styleLabel;
    private Image panelBackground;
    private bool detailsExpanded = true;

    private void Awake()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        gameObject.SetActive(false);
        return;
#endif
        if (collapseButton == null || contentRoot == null || statusLabel == null
            || !ValidGroup(presetButtons, presetLabels, 5)
            || !ValidGroup(fontButtons, fontLabels, 4)
            || !ValidGroup(weightButtons, weightLabels, 2)
            || !ValidGroup(sizeButtons, sizeLabels, 3)
            || cycleButton == null || fontCycleButton == null || weightCycleButton == null
            || sizeCycleButton == null || previewButton == null)
        {
            Debug.LogError("[DamageNumberFeelDebugUI] 프리셋 버튼 연결이 누락됐습니다.", this);
            gameObject.SetActive(false);
            return;
        }

        panelBackground = GetComponent<Image>();
        collapseAction = ToggleDetails;
        collapseButton.onClick.AddListener(collapseAction);
        presetActions = new UnityAction[presetButtons.Length];
        for (int i = 0; i < presetButtons.Length; i++)
        {
            DamageNumberFeelPreset preset = (DamageNumberFeelPreset)i;
            presetActions[i] = () => Select(preset);
            presetButtons[i].onClick.AddListener(presetActions[i]);
        }
        cycleAction = Cycle;
        cycleButton.onClick.AddListener(cycleAction);
        fontActions = new UnityAction[fontButtons.Length];
        for (int i = 0; i < fontButtons.Length; i++)
        {
            DamageNumberFontChoice font = (DamageNumberFontChoice)i;
            fontActions[i] = () => SelectFont(font);
            fontButtons[i].onClick.AddListener(fontActions[i]);
        }
        weightActions = new UnityAction[weightButtons.Length];
        for (int i = 0; i < weightButtons.Length; i++)
        {
            DamageNumberWeightChoice weight = (DamageNumberWeightChoice)i;
            weightActions[i] = () => SelectWeight(weight);
            weightButtons[i].onClick.AddListener(weightActions[i]);
        }
        sizeActions = new UnityAction[sizeButtons.Length];
        for (int i = 0; i < sizeButtons.Length; i++)
        {
            DamageNumberSizeChoice size = (DamageNumberSizeChoice)i;
            sizeActions[i] = () => SelectSize(size);
            sizeButtons[i].onClick.AddListener(sizeActions[i]);
        }
        fontCycleAction = CycleFont;
        fontCycleButton.onClick.AddListener(fontCycleAction);
        weightCycleAction = CycleWeight;
        weightCycleButton.onClick.AddListener(weightCycleAction);
        sizeCycleAction = CycleSize;
        sizeCycleButton.onClick.AddListener(sizeCycleAction);
        previewAction = Preview;
        previewButton.onClick.AddListener(previewAction);
        AddStyleToggle();
        Refresh();
    }

    // 2026-09-30: 종류별 연출 켜기/끄기(이전 연출과 바로 비교하는 스위치). 프리팹은 그대로 두고
    // 실행 중에 "다시 보기" 버튼을 복제해 머리글 아래에 붙인다. 머리글과 패널은 그만큼 위로 늘린다.
    private void AddStyleToggle()
    {
        const float Height = 30f, Gap = 6f;
        var root = transform as RectTransform;
        var header = collapseButton.transform as RectTransform;
        if (root == null || header == null)
            return;
        float slotY = header.anchoredPosition.y;
        root.sizeDelta += new Vector2(0f, Height + Gap);
        header.anchoredPosition += new Vector2(0f, Height + Gap);

        styleButton = Instantiate(previewButton, contentRoot.transform, false);
        styleButton.name = "Style Toggle";
        var rect = (RectTransform)styleButton.transform;
        rect.anchoredPosition = new Vector2(8f, slotY);
        rect.sizeDelta = new Vector2(220f, Height);
        styleButton.onClick.RemoveAllListeners();
        styleLabel = styleButton.GetComponentInChildren<TextMeshProUGUI>(true);
        styleAction = ToggleStyles;
        styleButton.onClick.AddListener(styleAction);
    }

    private void ToggleStyles()
    {
        DamageNumberStyleSettings.Toggle();
        Refresh();
        Preview();
    }

    private void OnEnable() => Refresh();

    private void OnDestroy()
    {
        if (presetActions != null && presetButtons != null)
            for (int i = 0; i < presetActions.Length; i++)
                if (presetButtons[i] != null)
                    presetButtons[i].onClick.RemoveListener(presetActions[i]);
        RemoveActions(fontButtons, fontActions);
        RemoveActions(weightButtons, weightActions);
        RemoveActions(sizeButtons, sizeActions);
        if (previewButton != null && previewAction != null)
            previewButton.onClick.RemoveListener(previewAction);
        if (cycleButton != null && cycleAction != null)
            cycleButton.onClick.RemoveListener(cycleAction);
        if (fontCycleButton != null && fontCycleAction != null)
            fontCycleButton.onClick.RemoveListener(fontCycleAction);
        if (weightCycleButton != null && weightCycleAction != null)
            weightCycleButton.onClick.RemoveListener(weightCycleAction);
        if (sizeCycleButton != null && sizeCycleAction != null)
            sizeCycleButton.onClick.RemoveListener(sizeCycleAction);
        if (collapseButton != null && collapseAction != null)
            collapseButton.onClick.RemoveListener(collapseAction);
        if (styleButton != null && styleAction != null)
            styleButton.onClick.RemoveListener(styleAction);
    }

    private void ToggleDetails()
    {
        detailsExpanded = !detailsExpanded;
        contentRoot.SetActive(detailsExpanded);
        if (panelBackground != null)
            panelBackground.enabled = detailsExpanded;
        Refresh();
    }

    private void Select(DamageNumberFeelPreset preset)
    {
        DamageNumberPopup.SelectPreset(preset);
        Refresh();
        Preview();
    }

    private void Cycle()
    {
        DamageNumberFeelPreset next = (DamageNumberFeelPreset)(((int)DamageNumberPopup.SelectedPreset + 1) % 5);
        Select(next);
    }

    private void SelectFont(DamageNumberFontChoice font)
    {
        DamageNumberPopup.SelectFont(font);
        Refresh();
        Preview();
    }

    private void CycleFont() =>
        SelectFont((DamageNumberFontChoice)(((int)DamageNumberPopup.SelectedFont + 1) % 4));

    private void SelectWeight(DamageNumberWeightChoice weight)
    {
        DamageNumberPopup.SelectWeight(weight);
        Refresh();
        Preview();
    }

    private void CycleWeight() =>
        SelectWeight((DamageNumberWeightChoice)(((int)DamageNumberPopup.SelectedWeight + 1) % 2));

    private void SelectSize(DamageNumberSizeChoice size)
    {
        DamageNumberPopup.SelectSize(size);
        Refresh();
        Preview();
    }

    private void CycleSize() =>
        SelectSize((DamageNumberSizeChoice)(((int)DamageNumberPopup.SelectedSize + 1) % 3));

    private void Preview()
    {
        int shown = DamageNumberSpawner.PreviewSelectedPreset();
        if (shown == 0 && statusLabel != null)
            statusLabel.text = "데미지 숫자 · 미리보기 불가";
    }

    private void Refresh()
    {
        if (presetButtons == null || presetLabels == null)
            return;
        DamageNumberFeelPreset selected = DamageNumberPopup.SelectedPreset;
        for (int i = 0; i < presetButtons.Length && i < presetLabels.Length; i++)
        {
            DamageNumberFeelPreset preset = (DamageNumberFeelPreset)i;
            bool active = preset == selected;
            if (presetLabels[i] != null)
                presetLabels[i].text = (active ? "● " : "")
                    + DamageNumberPopup.PresetLabel(preset)
                    + " " + DamageNumberPopup.PresetLifetime(preset).ToString("0.00") + "초";
            Highlight(presetButtons[i], active);
        }
        for (int i = 0; i < fontButtons.Length; i++)
        {
            var choice = (DamageNumberFontChoice)i;
            bool active = choice == DamageNumberPopup.SelectedFont;
            fontLabels[i].text = (active ? "● " : "") + DamageNumberPopup.FontLabel(choice);
            Highlight(fontButtons[i], active);
        }
        for (int i = 0; i < weightButtons.Length; i++)
        {
            var choice = (DamageNumberWeightChoice)i;
            bool active = choice == DamageNumberPopup.SelectedWeight;
            weightLabels[i].text = (active ? "● " : "") + DamageNumberPopup.WeightLabel(choice);
            Highlight(weightButtons[i], active);
        }
        for (int i = 0; i < sizeButtons.Length; i++)
        {
            var choice = (DamageNumberSizeChoice)i;
            bool active = choice == DamageNumberPopup.SelectedSize;
            sizeLabels[i].text = (active ? "● " : "") + DamageNumberPopup.SizeLabel(choice);
            Highlight(sizeButtons[i], active);
        }
        if (statusLabel != null)
            statusLabel.text = detailsExpanded
                ? DamageNumberPopup.PresetLabel(selected) + "·"
                    + DamageNumberPopup.FontLabel(DamageNumberPopup.SelectedFont) + "·"
                    + DamageNumberPopup.WeightLabel(DamageNumberPopup.SelectedWeight) + "·"
                    + DamageNumberPopup.SizeLabel(DamageNumberPopup.SelectedSize) + " ▼"
                : "데미지 숫자 ▶";
        if (styleLabel != null)
            styleLabel.text = DamageNumberStyleSettings.Enabled ? "종류별 연출: 켜짐 (신규)" : "종류별 연출: 꺼짐 (기존)";
        if (styleButton != null)
            Highlight(styleButton, DamageNumberStyleSettings.Enabled);
    }

    private static bool ValidGroup(Button[] buttons, TextMeshProUGUI[] labels, int count) =>
        buttons != null && labels != null && buttons.Length == count && labels.Length == count;

    private static void RemoveActions(Button[] buttons, UnityAction[] actions)
    {
        if (buttons == null || actions == null)
            return;
        for (int i = 0; i < buttons.Length && i < actions.Length; i++)
            if (buttons[i] != null && actions[i] != null)
                buttons[i].onClick.RemoveListener(actions[i]);
    }

    private static void Highlight(Button button, bool active)
    {
        if (button != null && button.targetGraphic is Image background)
            background.color = active
                ? new Color(.2f, .48f, .62f, .98f)
                : new Color(.16f, .19f, .24f, .92f);
    }
}
