using System.Collections.Generic;
using System.Linq;
using DuloGames.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 2026-10-01 ESC 메뉴 > 설정 창. 소리·화면·전투 표시·조작 네 탭을 OverburstGameSettings에 잇는다.
/// 값은 움직이는 즉시 적용하고, 화면 모드·해상도는 빌드에서 10초 안에 "유지"를 누르지 않으면 되돌린다.
/// </summary>
public sealed class OverburstSettingsPanel : MonoBehaviour
{
    private const float KeepDisplaySeconds = 10f;

    [Header("Tabs")]
    public UITab[] tabs;
    public GameObject[] pages;

    [Header("Sound")]
    public Slider masterVolume;
    public Slider uiVolume;
    public Toggle muteInBackground;

    [Header("Screen")]
    public UISwitchSelect screenMode;
    public UISwitchSelect resolution;
    public Toggle vSync;
    public UISwitchSelect frameLimit;
    public CanvasGroup frameLimitRow;
    public Text frameLimitNote;

    [Header("Combat display")]
    public Slider cameraShake;
    public Slider hitEffect;
    public Toggle combatFacingIndicator;
    public UISwitchSelect combatFacingStyle;
    public Slider combatFacingBrightness;

    public Toggle motionBlur;
    public Slider motionBlurIntensity;
    public Toggle edgeBlur;
    public Slider explorationEdgeBlurIntensity,combatEdgeBlurIntensity;
    public ScrollRect combatScroll;

    [Header("Blood")]
    public UISwitchSelect bloodStyle, bloodPalette;
    public OverburstSettingsNumberRow[] bloodRows;
    public Button bloodResetButton;

    [Header("Controls")]
    public OverburstKeyBindingRow[] keyRows;
    public ScrollRect keyScroll;
    public GameObject keyPrompt;
    public Text keyPromptLabel;

    [Header("Footer")]
    public Button resetButton;
    public Button closeButton;
    public Button footerCloseButton;
    public Text statusText;

    private OverburstGameMenu menu;
    private bool refreshing;
    private int currentTab;
    private readonly float[] scrollPositions = { 1f, 1f, 1f, 1f };
    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    private InputActionRebindingExtensions.RebindingOperation rebind;
    private int rebindEndedFrame = -1;
    private float revertAt = -1f;
    private FullScreenMode previousMode;
    private Vector2Int previousResolution;

    private static readonly FullScreenMode[] Modes = { FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
    private static readonly string[] ModeNames = { "전체 화면", "창 모드" };

    // 키를 기다리는 동안(과 그 입력이 끝난 프레임)의 ESC는 메뉴가 아니라 키 입력 취소다.
    public bool ConsumesEscape => rebind != null || rebindEndedFrame == Time.frameCount;

    public void Bind(OverburstGameMenu owner)
    {
        menu = owner;
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            tabs[i].onValueChanged.AddListener(on => { if (on) SelectTab(index); });
        }
        masterVolume.onValueChanged.AddListener(v => { if (!refreshing) OverburstGameSettings.MasterVolume = v; });
        uiVolume.onValueChanged.AddListener(v => { if (!refreshing) OverburstGameSettings.UiVolume = v; });
        muteInBackground.onValueChanged.AddListener(v => { if (!refreshing) { OverburstGameSettings.MuteInBackground = v; menu.PlayClick(); } });
        cameraShake.onValueChanged.AddListener(v => { if (!refreshing) OverburstGameSettings.CameraShakeScale = v; });
        hitEffect.onValueChanged.AddListener(v => { if (!refreshing) OverburstGameSettings.HitEffectScale = v; });
        if (combatFacingIndicator != null) combatFacingIndicator.onValueChanged.AddListener(v => { if (!refreshing) { OverburstGameSettings.CombatFacingIndicator = v; RefreshDependencies(); menu.PlayClick(); } });
        if (combatFacingStyle != null) combatFacingStyle.onChange.AddListener((i, _) => { if (!refreshing) OverburstGameSettings.CombatFacingStyle = (CombatFacingIndicatorStyle)i; });
        if (combatFacingBrightness != null) combatFacingBrightness.onValueChanged.AddListener(v => { if (!refreshing) OverburstGameSettings.CombatFacingBrightness = v; });
        if(edgeBlur!=null)edgeBlur.onValueChanged.AddListener(v=>{if(!refreshing){OverburstGameSettings.EdgeBlurEnabled=v;RefreshDependencies();menu.PlayClick();}});
        if(explorationEdgeBlurIntensity!=null)explorationEdgeBlurIntensity.onValueChanged.AddListener(v=>{if(!refreshing)OverburstGameSettings.ExplorationEdgeBlurIntensity=v;});
        if(combatEdgeBlurIntensity!=null)combatEdgeBlurIntensity.onValueChanged.AddListener(v=>{if(!refreshing)OverburstGameSettings.CombatEdgeBlurIntensity=v;});
        if(motionBlur!=null)motionBlur.onValueChanged.AddListener(v=>{if(!refreshing){OverburstGameSettings.MotionBlurEnabled=v;RefreshDependencies();menu.PlayClick();}});
        if(motionBlurIntensity!=null)motionBlurIntensity.onValueChanged.AddListener(v=>{if(!refreshing)OverburstGameSettings.MotionBlurIntensity=v;});
        vSync.onValueChanged.AddListener(v => { if (!refreshing) { OverburstGameSettings.SetFrameOptions(v, OverburstGameSettings.FrameLimit); RefreshFrameRow(); menu.PlayClick(); } });
        frameLimit.onChange.AddListener((i, _) => { if (!refreshing) OverburstGameSettings.SetFrameOptions(OverburstGameSettings.VSync, OverburstGameSettings.FrameLimits[i]); });
        screenMode.onChange.AddListener((i, _) => { if (!refreshing) ChangeDisplay(Modes[i], OverburstGameSettings.Resolution); });
        resolution.onChange.AddListener((i, _) => { if (!refreshing && i >= 0 && i < resolutions.Count) ChangeDisplay(OverburstGameSettings.ScreenMode, resolutions[i]); });
        foreach (var row in keyRows)
        {
            var target = row;
            row.keyButton.onClick.AddListener(() => StartRebind(target));
        }
        resetButton.onClick.AddListener(ResetCurrentTab);
        closeButton.onClick.AddListener(() => menu.CloseSettings());
        if (footerCloseButton != null) footerCloseButton.onClick.AddListener(() => menu.CloseSettings());
        if (bloodStyle != null) bloodStyle.onChange.AddListener((i, _) => { if (!refreshing) { OverburstGameSettings.BloodPack = i == 1; RefreshBlood(); menu.PlayClick(); } });
        if (bloodPalette != null) bloodPalette.onChange.AddListener((i, _) => { if (!refreshing) { OverburstGameSettings.BloodUniformRed = i == 1; menu.PlayClick(); } });
        if (bloodRows != null) foreach (var row in bloodRows) row.Bind();
        if (bloodResetButton != null) bloodResetButton.onClick.AddListener(() => { BloodComparisonTuning.ResetCurrent(); RefreshBlood(); menu.PlayClick(); });
        keyPrompt.SetActive(false);
    }

    public void Show()
    {
        Refresh();
        statusText.text = string.Empty;
        refreshing = true;
        try
        {
            // UITab의 켜짐 모양(주황 밑줄·화살표)은 값 변경 이벤트로 바뀌므로 알림을 끄지 않고 켠다.
            tabs[currentTab].isOn = true;
            for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == currentTab);
        }
        finally { refreshing = false; }
        RestoreScroll(currentTab);
        SelectDefault();
    }

    public void SelectDefault()
    {
        var page = pages[Mathf.Clamp(currentTab, 0, pages.Length - 1)];
        var first = page.GetComponentsInChildren<Selectable>(false).FirstOrDefault(s => s.IsInteractable() && Visible(s));
        OverburstGameMenu.Select(first != null ? first : (Selectable)tabs[currentTab]);
    }
    static bool Visible(Selectable item)
    {
        var scroll = item.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.viewport == null) return true;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, item.transform);
        return bounds.min.y >= scroll.viewport.rect.yMin && bounds.max.y <= scroll.viewport.rect.yMax;
    }
    void RememberScroll(int tab)
    {
        var scroll = tab == 2 ? combatScroll : tab == 3 ? keyScroll : null;
        if (scroll != null) scrollPositions[tab] = scroll.verticalNormalizedPosition;
    }
    void RestoreScroll(int tab)
    {
        Canvas.ForceUpdateCanvases();
        var scroll = tab == 2 ? combatScroll : tab == 3 ? keyScroll : null;
        if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = scrollPositions[tab]; }
    }

    private void SelectTab(int index)
    {
        if (currentTab == index && pages[index].activeSelf) return;
        RememberScroll(currentTab);
        currentTab = index;
        for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == index);
        statusText.text = string.Empty;
        RestoreScroll(index);
        if (!refreshing) menu.PlayClick();
    }

    private void Update()
    {
        if (revertAt < 0f) return;
        float remaining = revertAt - Time.unscaledTime;
        if (remaining <= 0f)
        {
            revertAt = -1f;
            menu.CancelModal(); // 되돌리기 쪽을 실행한다.
            return;
        }
        menu.SetModalDescription(Mathf.CeilToInt(remaining) + "초 뒤 이전 화면 설정으로 돌아갑니다.");
    }

    private void Refresh()
    {
        refreshing = true;
        try
        {
            masterVolume.SetValueWithoutNotify(OverburstGameSettings.MasterVolume);
            uiVolume.SetValueWithoutNotify(OverburstGameSettings.UiVolume);
            muteInBackground.SetIsOnWithoutNotify(OverburstGameSettings.MuteInBackground);
            cameraShake.SetValueWithoutNotify(OverburstGameSettings.CameraShakeScale);
            hitEffect.SetValueWithoutNotify(OverburstGameSettings.HitEffectScale);
            if (combatFacingIndicator != null) combatFacingIndicator.SetIsOnWithoutNotify(OverburstGameSettings.CombatFacingIndicator);
            if (combatFacingStyle != null) FillOptions(combatFacingStyle, new[] { "기존 절제형", "끝 연장형" }, (int)OverburstGameSettings.CombatFacingStyle);
            if (combatFacingBrightness != null) combatFacingBrightness.SetValueWithoutNotify(OverburstGameSettings.CombatFacingBrightness);
            if(edgeBlur!=null)edgeBlur.SetIsOnWithoutNotify(OverburstGameSettings.EdgeBlurEnabled);
            if(explorationEdgeBlurIntensity!=null){explorationEdgeBlurIntensity.SetValueWithoutNotify(OverburstGameSettings.ExplorationEdgeBlurIntensity);explorationEdgeBlurIntensity.GetComponent<UISliderDisplayValue>()?.SetValue(OverburstGameSettings.ExplorationEdgeBlurIntensity);}
            if(combatEdgeBlurIntensity!=null){combatEdgeBlurIntensity.SetValueWithoutNotify(OverburstGameSettings.CombatEdgeBlurIntensity);combatEdgeBlurIntensity.GetComponent<UISliderDisplayValue>()?.SetValue(OverburstGameSettings.CombatEdgeBlurIntensity);}
            if(motionBlur!=null)motionBlur.SetIsOnWithoutNotify(OverburstGameSettings.MotionBlurEnabled);
            if(motionBlurIntensity!=null){motionBlurIntensity.SetValueWithoutNotify(OverburstGameSettings.MotionBlurIntensity);motionBlurIntensity.GetComponent<UISliderDisplayValue>()?.SetValue(OverburstGameSettings.MotionBlurIntensity);}
            vSync.SetIsOnWithoutNotify(OverburstGameSettings.VSync);
            // 슬라이더·스위치 모양(값 글자·손잡이 위치)도 값에 맞춘다.
            foreach (var display in GetComponentsInChildren<UISliderDisplayValue>(true)) display.SetValue(display.GetComponent<Slider>().value);
            foreach (var onOff in GetComponentsInChildren<UIToggle_OnOff>(true)) onOff.OnValueChanged(onOff.toggle.isOn);

            FillOptions(screenMode, ModeNames, System.Array.IndexOf(Modes, OverburstGameSettings.ScreenMode == FullScreenMode.Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow));
            BuildResolutions();
            var current = OverburstGameSettings.Resolution;
            FillOptions(resolution, resolutions.Select(r => r.x + " × " + r.y).ToArray(), Mathf.Max(0, resolutions.IndexOf(current)));
            int limitIndex = System.Array.IndexOf(OverburstGameSettings.FrameLimits, OverburstGameSettings.FrameLimit);
            FillOptions(frameLimit, OverburstGameSettings.FrameLimits.Select(v => v > 0 ? v + " FPS" : "제한 없음").ToArray(),
                limitIndex >= 0 ? limitIndex : OverburstGameSettings.FrameLimits.Length - 1);
            RefreshFrameRow();
            RefreshKeys();
            RefreshBlood();
            RefreshDependencies();
        }
        finally { refreshing = false; }
    }

    void RefreshBlood()
    {
        bool wasRefreshing = refreshing; refreshing = true;
        if (bloodStyle != null) FillOptions(bloodStyle, new[] { "기존 혈흔", "새 혈흔 팩" }, OverburstGameSettings.BloodPack ? 1 : 0);
        if (bloodPalette != null) FillOptions(bloodPalette, new[] { "몬스터별 색상", "전체 붉은색" }, OverburstGameSettings.BloodUniformRed ? 1 : 0);
        if (bloodRows != null) foreach (var row in bloodRows) row.RefreshValue();
        refreshing = wasRefreshing;
    }
    void RefreshDependencies()
    {
        if (combatFacingBrightness != null) combatFacingBrightness.interactable = OverburstGameSettings.CombatFacingIndicator;
        if (motionBlurIntensity != null) motionBlurIntensity.interactable = OverburstGameSettings.MotionBlurEnabled;
        if (explorationEdgeBlurIntensity != null) explorationEdgeBlurIntensity.interactable = OverburstGameSettings.EdgeBlurEnabled;
        if (combatEdgeBlurIntensity != null) combatEdgeBlurIntensity.interactable = OverburstGameSettings.EdgeBlurEnabled;
    }

    private void RefreshFrameRow()
    {
        bool manual = !OverburstGameSettings.VSync;
        frameLimitRow.alpha = manual ? 1f : .45f;
        frameLimitRow.interactable = manual;
        frameLimitNote.text = manual ? "초당 최대 화면 갱신 수" : "수직 동기화가 켜져 있으면 모니터 주사율을 따릅니다";
    }

    private void BuildResolutions()
    {
        resolutions.Clear();
        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (size.x >= 1280 && size.y >= 720 && !resolutions.Contains(size)) resolutions.Add(size);
        }
        var current = OverburstGameSettings.Resolution;
        if (current.x > 0 && !resolutions.Contains(current)) resolutions.Add(current);
        if (resolutions.Count == 0) resolutions.Add(new Vector2Int(Screen.width, Screen.height));
        resolutions.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
    }

    private static void FillOptions(UISwitchSelect select, IList<string> options, int selected)
    {
        select.options.Clear();
        foreach (var option in options) select.options.Add(option);
        select.SelectOptionByIndex(Mathf.Clamp(selected, 0, options.Count - 1));
        // 같은 값이면 이벤트가 없어 글자가 갱신되지 않으므로 직접 맞춘다.
        var text = select.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "Text");
        if (text != null && options.Count > 0) text.text = options[Mathf.Clamp(selected, 0, options.Count - 1)];
    }

    private void ChangeDisplay(FullScreenMode mode, Vector2Int size)
    {
        previousMode = OverburstGameSettings.ScreenMode;
        previousResolution = OverburstGameSettings.Resolution;
        OverburstGameSettings.SetDisplay(mode, size);
        menu.PlayClick();
        if (Application.isEditor)
        {
            statusText.text = "에디터 Game 창에서는 화면 모드·해상도가 바뀌지 않습니다. 빌드에서 적용됩니다.";
            return;
        }
        revertAt = Time.unscaledTime + KeepDisplaySeconds;
        menu.Confirm("이 화면 설정을 유지할까요?", Mathf.CeilToInt(KeepDisplaySeconds) + "초 뒤 이전 화면 설정으로 돌아갑니다.",
            "유지", "되돌리기",
            () => { revertAt = -1f; statusText.text = "화면 설정을 저장했습니다."; },
            () => { revertAt = -1f; OverburstGameSettings.SetDisplay(previousMode, previousResolution); Refresh(); statusText.text = "이전 화면 설정으로 되돌렸습니다."; });
    }

    private void ResetCurrentTab()
    {
        menu.PlayClick();
        switch (currentTab)
        {
            case 0: OverburstGameSettings.ResetSection("sound"); break;
            case 1: OverburstGameSettings.SetFrameOptions(true, -1); break;
            case 2: OverburstGameSettings.ResetSection("combat"); break;
            case 3:
                var asset = PlayerInputFacade.Current != null ? PlayerInputFacade.Current.RuntimeAsset : null;
                if (asset != null) { asset.RemoveAllBindingOverrides(); OverburstGameSettings.StoreBindingOverrides(asset); }
                break;
        }
        Refresh();
        statusText.text = "이 탭을 기본값으로 되돌렸습니다.";
    }

    private void RefreshKeys()
    {
        var asset = PlayerInputFacade.Current != null ? PlayerInputFacade.Current.RuntimeAsset : null;
        foreach (var row in keyRows) row.Refresh(asset);
    }

    private void StartRebind(OverburstKeyBindingRow row)
    {
        if (rebind != null) return;
        var asset = PlayerInputFacade.Current != null ? PlayerInputFacade.Current.RuntimeAsset : null;
        var action = row.FindAction(asset);
        int index = row.ResolveBindingIndex(action);
        if (action == null || index < 0) { statusText.text = "이 동작은 지금 바꿀 수 없습니다."; return; }
        string before = action.bindings[index].effectivePath;
        bool wasEnabled = action.enabled;
        if (wasEnabled) action.Disable();
        menu.PlayClick();
        keyPrompt.SetActive(true);
        keyPrompt.transform.SetAsLastSibling();
        keyPromptLabel.text = "'" + row.label.text + "'에 쓸 키를 누르세요";
        statusText.text = string.Empty;
        rebind = action.PerformInteractiveRebinding(index)
            .WithExpectedControlType("Button")
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsHavingToMatchPath("<Mouse>")
            .WithControlsExcluding("<Keyboard>/anyKey")
            .WithControlsExcluding("<Mouse>/press")
            .WithControlsExcluding("<Pointer>/press")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(.05f)
            .OnComplete(_ => FinishRebind(row, action, index, before, true, wasEnabled))
            .OnCancel(_ => FinishRebind(row, action, index, before, false, wasEnabled))
            .Start();
    }

    private void FinishRebind(OverburstKeyBindingRow row, InputAction action, int index, string before, bool completed, bool reenable)
    {
        rebind?.Dispose();
        rebind = null;
        rebindEndedFrame = Time.frameCount;
        keyPrompt.SetActive(false);
        if (reenable && !OverburstGameMenu.IsOpen) action.Enable();
        var asset = action.actionMap.asset;
        if (completed)
        {
            string after = action.bindings[index].effectivePath;
            if (after != before)
            {
                string swapped = null;
                foreach (var other in keyRows)
                {
                    if (other == row || other.CurrentPath(asset) != after) continue;
                    var otherAction = other.FindAction(asset);
                    otherAction.ApplyBindingOverride(other.ResolveBindingIndex(otherAction), before);
                    MirrorUi(asset, other, before);
                    swapped = other.label.text;
                }
                MirrorUi(asset, row, after);
                statusText.text = swapped != null
                    ? "'" + swapped + "'에 쓰던 키라서 두 키를 맞바꿨습니다."
                    : "'" + row.label.text + "' → " + OverburstKeyBindingRow.DisplayName(after);
                OverburstGameSettings.StoreBindingOverrides(asset);
                menu.PlayClick();
            }
        }
        RefreshKeys();
        OverburstGameMenu.Select(row.keyButton);
    }

    private static void MirrorUi(InputActionAsset asset, OverburstKeyBindingRow row, string path)
    {
        if (string.IsNullOrEmpty(row.mirrorUiAction)) return;
        var ui = asset.FindAction(PlayerInputFacade.UiMapName + "/" + row.mirrorUiAction);
        if (ui != null && ui.bindings.Count > 0) ui.ApplyBindingOverride(0, path);
    }

    public void CancelRebind()
    {
        if (rebind != null) rebind.Cancel();
        revertAt = -1f;
    }

    private void OnDisable()
    {
        RememberScroll(currentTab);
        if (rebind != null) rebind.Cancel();
    }
}
