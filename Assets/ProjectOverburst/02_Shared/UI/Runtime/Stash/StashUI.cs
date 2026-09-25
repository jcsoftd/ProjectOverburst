using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StashUI : MonoBehaviour // 창고 UI
{
    [Header("References")]
    [SerializeField] private GameObject stashPanel;
    [SerializeField] private RectTransform slotContainer;
    [SerializeField] private StashSlotBridge slotBridge;
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private TooltipManager tooltipManager;
    [SerializeField] private StashCurrencySummaryUI currencySummaryUI;
    [SerializeField] private TMP_FontAsset koreanFontAsset;

    [Header("Layout")]
    [SerializeField] private int stashSlotCount = 63;

    [Header("Scene UI")]
    [SerializeField] private SlotUI[] stashSlots;
    [SerializeField] private Button[] tabButtons;
    [SerializeField] private TextMeshProUGUI[] tabButtonTexts;
    [SerializeField] private TMP_Dropdown sortDropdown;
    [SerializeField] private Button sortRefreshButton;
    [SerializeField] private TextMeshProUGUI sortRefreshButtonText;
    [SerializeField] private Button storeAllButton;
    [SerializeField] private TextMeshProUGUI storeAllButtonText;
    [SerializeField] private TextMeshProUGUI actionStatusText;

    private bool initialized; // 초기화 완료
    private bool isOpen; // 열림 상태
    private bool inventoryWasOpenBeforeStash; // 기존 인벤토리
    private ItemSortMode sortMode = ItemSortMode.Default; // 정렬 기준
    private ItemSortDirection sortDirection = ItemSortDirection.Descending; // 정렬 방향
    private bool warnedMissingSlots; // 슬롯 경고
    private bool warnedMissingToolbar; // 툴바 경고
    private bool warnedMissingFont; // 폰트 경고

    public bool IsOpen => isOpen && stashPanel != null && stashPanel.activeSelf; // 실제 열림
    public bool HasUsableCanvasRoot
    {
        get
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>(true); // 캔버스 루트
            return parentCanvas != null && parentCanvas.gameObject.activeInHierarchy;
        }
    }

    private void Awake()
    {
        EnsureInitialized();

        if (!isOpen)
            SetPanelVisible(false);
    }

    private void OnDisable()
    {
        isOpen = false;
        if (inventoryUI != null)
            inventoryUI.InputToggleLocked = false; // Tab 복구

        GameplayInputBlocker.Unblock(this); // 입력 복구
        ReleaseInteracting();
    }

    private void OnDestroy()
    {
        GameplayInputBlocker.Unblock(this); // 입력 복구
        ReleaseInteracting();
    }

    private void Update()
    {
        if (!isOpen)
            return;

        // GOAL A2: Tab/Esc 직접 읽기 대신 Gameplay Inventory + UI Cancel을 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null)
            return;

        if (facade.InventoryPressedThisFrame || facade.UiCancelPressedThisFrame)
            Close();
    }

    public void Toggle()
    {
        if (isOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (!EnsureInitialized())
            return;

        if (IsOpen)
            return;

        inventoryWasOpenBeforeStash = inventoryUI != null && inventoryUI.IsVisible; // 이전 상태
        isOpen = true; // 열림
        EnsureCanvasRootActive();
        SetPanelVisible(true);
        GameplayInputBlocker.Block(this); // 입력 차단
        // GOAL A2: 창고 세션 동안 Action.Interacting을 명시 요청한다. Close/OnDisable에서 해제.
        RequestInteracting();

        if (inventoryUI != null)
        {
            inventoryUI.InputToggleLocked = true; // Tab 차단
            inventoryUI.SetVisible(true); // 인벤토리 표시
        }

        slotBridge?.RefreshSlots(); // 슬롯 갱신
        currencySummaryUI?.Refresh(); // 재화 합산 갱신
    }

    public void Close()
    {
        if (!isOpen && (stashPanel == null || !stashPanel.activeSelf))
            return;

        isOpen = false; // 닫힘
        SetPanelVisible(false);
        GameplayInputBlocker.Unblock(this); // 입력 복구
        ReleaseInteracting();
        slotBridge?.ClearDragPreview(); // 드래그 취소
        HideTooltip();

        if (inventoryUI != null)
        {
            inventoryUI.InputToggleLocked = false; // Tab 복구

            if (!inventoryWasOpenBeforeStash)
                inventoryUI.SetVisible(false); // 원래 숨김
        }
    }

    public void HideTooltip()
    {
        if (tooltipManager == null)
            tooltipManager = TooltipManager.Instance; // 단일 인스턴스 대체 경로

        tooltipManager?.HideTooltip();
    }

    private void RequestInteracting()
    {
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        if (coordinator != null)
            coordinator.RequestInteracting(this);
    }

    private void ReleaseInteracting()
    {
        PlayerStateCoordinator coordinator = PlayerStateCoordinator.Current;
        if (coordinator != null)
            coordinator.ReleaseInteracting(this);
        else
        {
            PlayerStateCoordinator[] coordinators = FindObjectsByType<PlayerStateCoordinator>(FindObjectsSortMode.None);
            foreach (PlayerStateCoordinator coordinatorInScene in coordinators)
            {
                if (coordinatorInScene != null)
                    coordinatorInScene.ReleaseInteracting(this);
            }
        }
    }

    private bool EnsureInitialized()
    {
        if (initialized)
            return stashPanel != null && slotBridge != null;

        if (stashPanel == null)
            stashPanel = gameObject; // 패널 root

        if (slotBridge == null)
            slotBridge = GetComponent<StashSlotBridge>(); // 같은 오브젝트

        if (inventoryUI == null)
            inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include); // 영구 UI

        if (tooltipManager == null)
            tooltipManager = GetComponentInParent<TooltipManager>(true) ?? TooltipManager.Instance; // 툴팁

        if (currencySummaryUI == null)
            currencySummaryUI = GetComponentInChildren<StashCurrencySummaryUI>(true); // 재화 합산 UI

        if (slotContainer == null)
            slotContainer = transform.Find("SlotGrid") as RectTransform; // 구 이름

        if (slotContainer == null)
            slotContainer = transform.Find("StashSlotGrid") as RectTransform; // 정식 grid

        bool slotsReady = BindSceneSlots(); // 씬 슬롯
        bool toolbarReady = BindToolbarControls(); // 씬 툴바
        initialized = stashPanel != null && slotBridge != null && slotsReady && toolbarReady;
        return initialized;
    }

    public void UpdateTabVisuals()
    {
        int currentTabIndex = slotBridge != null ? slotBridge.CurrentTabIndex : 0; // 현재 탭
        if (tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null)
                    continue;

                Image image = tabButtons[i].GetComponent<Image>();
                if (image != null)
                    image.color = i == currentTabIndex ? new Color(.36f, .24f, .12f, 1f) : new Color(.12f, .10f, .08f, 1f);
            }
        }

        UpdateSortControls();
        currencySummaryUI?.Refresh(); // 탭 전환 후 총량 갱신
    }

    private void EnsureCanvasRootActive()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>(true); // 캔버스 루트
        if (parentCanvas != null && !parentCanvas.gameObject.activeSelf)
            parentCanvas.gameObject.SetActive(true);
    }

    private bool BindSceneSlots()
    {
        stashSlotCount = Mathf.Max(1, stashSlotCount); // 최소 1

        if ((stashSlots == null || stashSlots.Length < stashSlotCount) && slotContainer != null)
            stashSlots = slotContainer.GetComponentsInChildren<SlotUI>(true); // 씬 슬롯 수집

        if (stashSlots != null && stashSlots.Length >= stashSlotCount)
        {
            slotBridge.SetStashSlots(stashSlots); // Bridge 연결
            return true;
        }

        if (!warnedMissingSlots)
        {
            Debug.LogWarning("StashUI requires scene-placed StashSlotGrid with StashSlot_00~62. Runtime slot creation is disabled.", this);
            warnedMissingSlots = true;
        }

        return false;
    }

    private bool BindToolbarControls()
    {
        if (stashPanel == null)
            return false;

        Transform toolbar = stashPanel.transform.Find("StashToolbar"); // 정식 툴바
        if ((tabButtons == null || tabButtons.Length < 3) && toolbar != null)
            tabButtons = new[]
            {
                FindButton(toolbar, "StashTabButton_01"),
                FindButton(toolbar, "StashTabButton_02"),
                FindButton(toolbar, "StashTabButton_03")
            };

        if ((tabButtonTexts == null || tabButtonTexts.Length < 3) && tabButtons != null && tabButtons.Length >= 3)
        {
            tabButtonTexts = new TextMeshProUGUI[3];
            for (int i = 0; i < tabButtonTexts.Length; i++)
                tabButtonTexts[i] = tabButtons[i] != null ? tabButtons[i].GetComponentInChildren<TextMeshProUGUI>(true) : null;
        }

        if (sortDropdown == null && toolbar != null)
            sortDropdown = FindComponent<TMP_Dropdown>(toolbar, "StashSortDropdown");

        if (sortRefreshButton == null && toolbar != null)
            sortRefreshButton = FindButton(toolbar, "StashSortRefreshButton");

        if (sortRefreshButtonText == null && sortRefreshButton != null)
            sortRefreshButtonText = sortRefreshButton.GetComponentInChildren<TextMeshProUGUI>(true);

        if (storeAllButton == null && toolbar != null)
            storeAllButton = FindButton(toolbar, "StashStoreAllButton");

        if (storeAllButtonText == null && storeAllButton != null)
            storeAllButtonText = storeAllButton.GetComponentInChildren<TextMeshProUGUI>(true);

        if (actionStatusText == null && toolbar != null)
            actionStatusText = FindComponent<TextMeshProUGUI>(toolbar, "StashStatusText");

        if (tabButtons == null || tabButtons.Length < 3 || sortDropdown == null || sortRefreshButton == null || storeAllButton == null)
        {
            if (!warnedMissingToolbar)
            {
                Debug.LogWarning("StashUI toolbar references are missing. Place StashToolbar, StashTabButton_01~03, StashSortDropdown, StashSortRefreshButton and StashStoreAllButton in the scene.", this);
                warnedMissingToolbar = true;
            }
            return false;
        }

        for (int i = 0; i < tabButtons.Length; i++)
        {
            int tabIndex = i; // closure 고정
            if (tabButtons[i] == null)
                continue;

            tabButtons[i].onClick.RemoveAllListeners(); // 중복 방지
            tabButtons[i].onClick.AddListener(() => HandleTabButtonClicked(tabIndex)); // 탭 클릭

            if (tabButtonTexts != null && i < tabButtonTexts.Length && tabButtonTexts[i] != null)
            {
                tabButtonTexts[i].text = (i + 1).ToString();
                ApplyKoreanFont(tabButtonTexts[i]); // 한글 폰트
            }
        }

        sortDropdown.onValueChanged.RemoveListener(HandleSortDropdownChanged); // 중복 방지
        sortDropdown.onValueChanged.AddListener(HandleSortDropdownChanged); // 정렬 선택
        sortRefreshButton.onClick.RemoveListener(HandleSortRefreshButtonClicked); // 중복 방지
        sortRefreshButton.onClick.AddListener(HandleSortRefreshButtonClicked); // 정렬 실행
        storeAllButton.onClick.RemoveListener(HandleStoreAllButtonClicked); // 중복 방지
        storeAllButton.onClick.AddListener(HandleStoreAllButtonClicked); // 전체보관

        ConfigureDropdown(sortDropdown);
        ApplyKoreanFont(sortRefreshButtonText);
        ApplyKoreanFont(storeAllButtonText);
        ApplyKoreanFont(actionStatusText);

        UpdateTabVisuals();
        return true;
    }

    private void HandleTabButtonClicked(int tabIndex)
    {
        if (DragSlot.IsDragging)
        {
            SetActionStatus("드래그 중 탭 전환 불가"); // 드래그 보호
            return;
        }

        if (slotBridge != null && slotBridge.SwitchTab(tabIndex))
            SetActionStatus("창고 " + (tabIndex + 1));
    }

    private void HandleSortDropdownChanged(int optionIndex)
    {
        sortMode = ItemSortComparer.GetModeByIndex(optionIndex); // 선택만 변경
        UpdateSortControls();
        SetActionStatus("정렬 선택: " + ItemSortComparer.GetDisplayName(sortMode));
    }

    private void HandleSortRefreshButtonClicked()
    {
        slotBridge?.SortCurrentTab(sortMode, sortDirection);
        SetActionStatus("정렬 적용: " + ItemSortComparer.GetDisplayName(sortMode) + " / " + ItemSortComparer.GetDirectionDisplayName(sortDirection));
        sortDirection = ItemSortComparer.ToggleDirection(sortDirection); // 다음 방향
        UpdateSortControls();
    }

    private void HandleStoreAllButtonClicked()
    {
        if (DragSlot.IsDragging)
        {
            SetActionStatus("드래그 중 전체보관 불가"); // 드래그 보호
            return;
        }

        int movedCount = slotBridge != null ? slotBridge.StoreAllInventoryItemsToCurrentTab() : 0; // 성공 수
        int failedCount = slotBridge != null ? slotBridge.LastStoreAllFailedCount : 0; // 실패 수
        if (movedCount <= 0)
        {
            SetActionStatus(failedCount > 0 ? "창고 공간 부족: 일부 아이템 보관 실패" : "보관할 아이템 없음");
            return;
        }

        SetActionStatus(failedCount > 0 ? movedCount + "개 보관 / 창고 공간 부족: 일부 아이템 보관 실패" : movedCount + "개 보관");
    }

    private void UpdateSortControls()
    {
        if (sortDropdown != null)
        {
            int modeIndex = ItemSortComparer.GetModeIndex(sortMode);
            if (sortDropdown.value != modeIndex)
            sortDropdown.SetValueWithoutNotify(modeIndex); // 이벤트 방지

            sortDropdown.RefreshShownValue();
            ApplyDropdownTextStyle(sortDropdown);
        }

        if (sortRefreshButtonText != null)
        {
            sortRefreshButtonText.text = "정렬 적용";
            ApplyKoreanFont(sortRefreshButtonText);
        }

        if (actionStatusText != null && string.IsNullOrEmpty(actionStatusText.text))
            actionStatusText.text = ItemSortComparer.GetDescription(sortMode) + " / 방향: " + ItemSortComparer.GetDirectionDisplayName(sortDirection); // 상태 기본값
    }

    private void SetActionStatus(string message)
    {
        if (actionStatusText != null)
            actionStatusText.text = message;

        currencySummaryUI?.Refresh();
    }

    private void SetPanelVisible(bool visible)
    {
        if (stashPanel != null)
            stashPanel.SetActive(visible);
    }

    private void ConfigureDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
            return;

        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < ItemSortComparer.Modes.Length; i++)
            options.Add(new TMP_Dropdown.OptionData(ItemSortComparer.GetDisplayName(ItemSortComparer.Modes[i])));

        dropdown.options = options;
        dropdown.SetValueWithoutNotify(ItemSortComparer.GetModeIndex(sortMode)); // 이벤트 방지
        ApplyDropdownTextStyle(dropdown);
        dropdown.RefreshShownValue();
        SetDropdownCaptionText(dropdown);
    }

    private void ApplyDropdownTextStyle(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
            return;

        ApplyKoreanFont(dropdown.captionText);
        ApplyKoreanFont(dropdown.itemText);
        SetDropdownCaptionText(dropdown);
    }

    private void ApplyKoreanFont(TMP_Text text)
    {
        if (text == null)
            return;

        if (koreanFontAsset == null)
        {
            if (!warnedMissingFont)
            {
                Debug.LogWarning("StashUI koreanFontAsset is not connected. Korean stash UI text may not render correctly.", this);
                warnedMissingFont = true;
            }
            return;
        }

            text.font = koreanFontAsset; // 한글 폰트
            text.fontSharedMaterial = koreanFontAsset.material; // 텍스트 재질
        text.color = Color.white;
        text.alpha = 1f;
        text.textWrappingMode = TextWrappingModes.NoWrap; // 줄바꿈 금지
        text.overflowMode = TextOverflowModes.Overflow; // 드롭다운 표시
        text.enabled = true;
        text.ForceMeshUpdate(true, true);
    }

    private void SetDropdownCaptionText(TMP_Dropdown dropdown)
    {
        if (dropdown == null || dropdown.captionText == null)
            return;

        dropdown.captionText.text = ItemSortComparer.GetDisplayName(sortMode); // 표시값
        dropdown.captionText.SetAllDirty();
        dropdown.captionText.ForceMeshUpdate(true, true);
    }

    private static Button FindButton(Transform root, string name)
    {
        return FindComponent<Button>(root, name);
    }

    private static T FindComponent<T>(Transform root, string name) where T : Component
    {
        if (root == null)
            return null;

        Transform child = root.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }
}
