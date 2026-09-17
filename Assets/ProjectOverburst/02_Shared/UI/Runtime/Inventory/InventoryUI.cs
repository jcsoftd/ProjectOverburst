using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour // 인벤토리 UI
{
    private const int ActiveBagSlotCount = 1; // 현재 사용 가방 슬롯 수

    [Header("References")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private InventorySlotBridge slotBridge;
    [SerializeField] private TooltipManager tooltipManager;
    [SerializeField] private SlotUI[] bagSlots;
    [SerializeField] private TMP_FontAsset koreanFontAsset;
    [SerializeField] private TMP_Dropdown sortDropdown;
    [SerializeField] private Button sortRefreshButton;
    [SerializeField] private TextMeshProUGUI sortRefreshButtonText;
    [SerializeField] private TextMeshProUGUI sortStatusText;
    [SerializeField] private TextMeshProUGUI goldSummaryText;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private StashCurrencyService stashCurrencyService;
    [SerializeField] private InventorySelectionController selectionController;
    [SerializeField] private InventoryQuickSlotBindingController quickSlotBindingController;
    [SerializeField] private InventoryItemActionService itemActionService;
    [SerializeField] private InventoryContextMenuController contextMenuController;

    private bool initialized; // 초기화 완료
    private ItemSortMode sortMode = ItemSortMode.Grade; // 정렬 기준
    private ItemSortDirection sortDirection = ItemSortDirection.Descending; // 정렬 방향
    private bool warnedMissingSortControls; // 정렬 경고
    private readonly HashSet<string> newItemRuntimeIds = new HashSet<string>(); // 닫힌 상태 획득 표시
    private PlayerInventory subscribedInventory;
    private PlayerStash subscribedStash;

    public bool IsVisible => inventoryPanel != null && inventoryPanel.activeSelf; // 패널 표시
    public bool InputToggleLocked { get; set; } // Tab 잠금

    public TMP_FontAsset KoreanFontAsset => koreanFontAsset;

    private void Awake()
    {
        EnsureRuntimeUI();
    }

    private void OnEnable()
    {
        EnsureRuntimeUI();
        SubscribeGoldSummarySources();
        RefreshGoldSummary();
    }

    private void Start()
    {
        SetVisible(false);
    }

    private void OnDisable()
    {
        UnsubscribeGoldSummarySources();
        CloseContextMenuIfNeeded();
        GameplayInputBlocker.Unblock(this); // 입력 복구
    }

    private void CloseContextMenuIfNeeded()
    {
        if (contextMenuController != null)
            contextMenuController.Close();
    }

    private void OnDestroy()
    {
        UnsubscribeGoldSummarySources();
        CloseContextMenuIfNeeded();
        GameplayInputBlocker.Unblock(this); // 입력 복구
    }

    private void Update()
    {
        // GOAL A2: Tab/Esc 직접 읽기 대신 Gameplay Inventory + UI Cancel을 사용한다.
        // 기존 감각 Tab 항상 토글, Esc는 열려 있을 때만 닫기를 보존한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;

        if (facade == null || InputToggleLocked)
            return;

        if (inventoryPanel == null && !EnsureRuntimeUI())
            return;

        if (facade.InventoryPressedThisFrame || facade.UiCancelPressedThisFrame && inventoryPanel.activeSelf)
            Toggle();
    }

    public void Toggle()
    {
        if (!EnsureRuntimeUI())
            return;

        SetVisible(!inventoryPanel.activeSelf);
    }

    public void SetVisible(bool visible)
    {
        if (!EnsureRuntimeUI())
            return;

        inventoryPanel.SetActive(visible); // 패널 표시
        GameplayInputBlocker.SetBlocked(this, visible); // 입력 차단

        if (!visible)
        {
            CloseContextMenuIfNeeded();
            WeaponComboGemPopupPresenter.CloseOpenPopup(); // 인벤토리 종료 시 상세 정리
            ClearNewItemMarkers(false); // 닫을 때 초기화
        }

        if (visible)
            ApplyCurrentSort(); // 열 때 현재 정렬 유지

        if (visible)
            UpdateSortControls(); // 정렬 표시

        if (visible)
            RefreshGoldSummary(); // 재화 표시

        if (!visible && tooltipManager != null)
            tooltipManager.HideTooltip(); // Tooltip 정리

        if (!visible && slotBridge != null)
            slotBridge.ClearDragPreview(); // 드래그 정리
    }

    public bool ContainsScreenPoint(Vector2 screenPosition)
    {
        if (!EnsureRuntimeUI() || inventoryPanel == null || !inventoryPanel.activeInHierarchy)
            return false;

        RectTransform panelRect = inventoryPanel.transform as RectTransform;
        if (panelRect == null)
            return false;

        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null; // UI 카메라
        return RectTransformUtility.RectangleContainsScreenPoint(panelRect, screenPosition, eventCamera);
    }

    public static void NotifyWorldPickupAdded(ItemData item)
    {
        if (item == null)
            return;

        InventoryUI[] inventoryUis = Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < inventoryUis.Length; i++)
            inventoryUis[i]?.MarkWorldPickupItem(item); // 닫힌 UI만 등록
    }

    public void MarkWorldPickupItem(ItemData item)
    {
        if (item == null || !item.HasValidBaseData || IsVisible)
            return;

        string runtimeKey = GetRuntimeKey(item);
        if (string.IsNullOrEmpty(runtimeKey))
            return;

        newItemRuntimeIds.Add(runtimeKey); // 신규 표시 등록
    }

    public bool IsNewlyAcquiredItem(ItemData item)
    {
        if (item == null || newItemRuntimeIds.Count == 0)
            return false;

        string runtimeKey = GetRuntimeKey(item);
        return !string.IsNullOrEmpty(runtimeKey) && newItemRuntimeIds.Contains(runtimeKey);
    }

    public void ClearNewItemMarkers(bool refreshVisible = true)
    {
        if (newItemRuntimeIds.Count == 0)
            return;

        newItemRuntimeIds.Clear(); // 신규 표시 초기화

        if (refreshVisible && IsVisible && slotBridge != null)
            slotBridge.RefreshSlotsWithOwnershipCheck();
    }

    private bool EnsureRuntimeUI()
    {
        if (initialized)
            return inventoryPanel != null;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(); // 캔버스 루트

        if (slotBridge == null)
            slotBridge = GetComponent<InventorySlotBridge>(); // 슬롯 Bridge

        if (tooltipManager == null)
            tooltipManager = GetComponentInChildren<TooltipManager>(true); // 자식 Tooltip

        if (tooltipManager == null && canvas != null)
            tooltipManager = canvas.GetComponentInChildren<TooltipManager>(true); // 캔버스 툴팁

        EnsureContextServices();

        initialized = inventoryPanel != null && slotBridge != null;
        if (initialized)
        {
            BindSceneBagSlots(); // 가방 슬롯
            BindSortControls(); // 정렬 UI
            ResolveGoldSummaryReferences(); // Gold 표시
            SubscribeGoldSummarySources(); // Gold 갱신
            RefreshGoldSummary();
        }

        if (initialized)
            InitContextServices();

        return initialized;
    }

    public void RefreshGoldSummary()
    {
        ResolveGoldSummaryReferences();
        SubscribeGoldSummarySources();

        if (goldSummaryText == null)
            return;

        int inventoryGold = GetInventoryGoldAmount();
        if (ShouldShowStashGold())
        {
            int stashGold = stashCurrencyService != null ? stashCurrencyService.GetAmount(CurrencyType.Gold) : 0;
            goldSummaryText.text = GoldSummaryTextFormatter.FormatPlayerGold(inventoryGold, stashGold, true);
        }
        else
        {
            goldSummaryText.text = GoldSummaryTextFormatter.FormatPlayerGold(inventoryGold, 0, false);
        }

        ApplyKoreanFont(goldSummaryText);
    }

    private void EnsureContextServices()
    {
        if (selectionController == null)
            selectionController = GetComponent<InventorySelectionController>() ?? gameObject.AddComponent<InventorySelectionController>();

        if (quickSlotBindingController == null)
            quickSlotBindingController = GetComponent<InventoryQuickSlotBindingController>() ?? gameObject.AddComponent<InventoryQuickSlotBindingController>();

        if (itemActionService == null)
            itemActionService = GetComponent<InventoryItemActionService>() ?? gameObject.AddComponent<InventoryItemActionService>();

        if (contextMenuController == null)
            contextMenuController = GetComponent<InventoryContextMenuController>() ?? gameObject.AddComponent<InventoryContextMenuController>();
    }

    private void InitContextServices()
    {
        itemActionService.Init(slotBridge, quickSlotBindingController);
        contextMenuController.Init(canvas, tooltipManager, selectionController, itemActionService, quickSlotBindingController, koreanFontAsset);
    }

    private void BindSortControls()
    {
        if (inventoryPanel == null)
            return;

        if (sortDropdown == null)
            sortDropdown = inventoryPanel.GetComponentInChildren<TMP_Dropdown>(true); // 씬 Dropdown

        if (sortRefreshButton == null)
        {
            Transform refreshTransform = inventoryPanel.transform.Find("InventorySortControls/InventorySortRefreshButton");
            sortRefreshButton = refreshTransform != null ? refreshTransform.GetComponent<Button>() : null; // 리프레시
        }

        if (sortRefreshButtonText == null && sortRefreshButton != null)
            sortRefreshButtonText = sortRefreshButton.GetComponentInChildren<TextMeshProUGUI>(true); // 버튼 Text

        if (sortStatusText == null)
        {
            Transform statusTransform = inventoryPanel.transform.Find("InventorySortControls/InventorySortStatusText");
            sortStatusText = statusTransform != null ? statusTransform.GetComponent<TextMeshProUGUI>() : null; // 상태 Text
        }

        if (sortDropdown == null || sortRefreshButton == null)
        {
            if (!warnedMissingSortControls)
            {
                Debug.LogWarning("InventoryUI sort controls are not connected. Place InventorySortDropdown and InventorySortRefreshButton under InventoryPanel and reconnect serialized references.", this);
                warnedMissingSortControls = true;
            }
            return;
        }

        sortDropdown.onValueChanged.RemoveListener(HandleSortDropdownChanged); // 중복 방지
        sortDropdown.onValueChanged.AddListener(HandleSortDropdownChanged); // 기준 선택
        sortRefreshButton.onClick.RemoveListener(HandleSortRefreshButtonClicked); // 중복 방지
        sortRefreshButton.onClick.AddListener(HandleSortRefreshButtonClicked); // 방향 토글
        ConfigureDropdown(sortDropdown);
        ApplyKoreanFont(sortRefreshButtonText);
        ApplyKoreanFont(sortStatusText);

        UpdateSortControls();
    }

    private void ResolveGoldSummaryReferences()
    {
        if (playerInventory == null)
            playerInventory = GetComponent<PlayerInventory>() ?? FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);

        if (stashCurrencyService == null)
            stashCurrencyService = FindFirstObjectByType<StashCurrencyService>(FindObjectsInactive.Include);
    }

    private void SubscribeGoldSummarySources()
    {
        ResolveGoldSummaryReferences();

        if (subscribedInventory != playerInventory)
        {
            if (subscribedInventory != null)
                subscribedInventory.Changed -= RefreshGoldSummary;

            subscribedInventory = playerInventory;
            if (subscribedInventory != null)
                subscribedInventory.Changed += RefreshGoldSummary;
        }

        PlayerStash stash = stashCurrencyService != null ? stashCurrencyService.Stash : null;
        if (subscribedStash == stash)
            return;

        if (subscribedStash != null)
        {
            subscribedStash.Changed -= RefreshGoldSummary;
            subscribedStash.CurrentTabChanged -= RefreshGoldSummary;
        }

        subscribedStash = stash;
        if (subscribedStash != null)
        {
            subscribedStash.Changed += RefreshGoldSummary;
            subscribedStash.CurrentTabChanged += RefreshGoldSummary;
        }
    }

    private void UnsubscribeGoldSummarySources()
    {
        if (subscribedInventory != null)
        {
            subscribedInventory.Changed -= RefreshGoldSummary;
            subscribedInventory = null;
        }

        if (subscribedStash != null)
        {
            subscribedStash.Changed -= RefreshGoldSummary;
            subscribedStash.CurrentTabChanged -= RefreshGoldSummary;
            subscribedStash = null;
        }
    }

    private int GetInventoryGoldAmount()
    {
        if (playerInventory == null)
            return 0;

        int total = 0;
        int limit = playerInventory.UnlockedSlotCount;
        for (int i = 0; i < limit; i++)
        {
            ItemData item = playerInventory.GetItemAt(i);
            if (item != null
                && item.stackCount > 0
                && item.baseData is CurrencyItemData currencyData
                && currencyData.currencyType == CurrencyType.Gold)
            {
                total += item.stackCount;
            }
        }

        return total;
    }

    private bool ShouldShowStashGold()
    {
        Scene dungeonScene = SceneManager.GetSceneByName(
            PersistentSceneFlow.DungeonRunSceneName);
        return !dungeonScene.IsValid() || !dungeonScene.isLoaded;
    }

    private void HandleSortDropdownChanged(int optionIndex)
    {
        sortMode = ItemSortComparer.GetInventoryModeByIndex(optionIndex); // 기준 변경
        ApplyCurrentSort(); // 선택 즉시 정렬
        UpdateSortControls();
    }

    private void HandleSortRefreshButtonClicked()
    {
        sortDirection = ItemSortComparer.ToggleDirection(sortDirection); // 다음 방향
        ApplyCurrentSort(); // 방향 변경 즉시 재정렬
        UpdateSortControls();
    }

    private void ApplyCurrentSort()
    {
        if (slotBridge == null)
            return;

        if (sortMode == ItemSortMode.None)
        {
            slotBridge.RefreshSlotsWithOwnershipCheck(); // 정렬 없이 표시 갱신
            return;
        }

        slotBridge.SortInventory(sortMode, sortDirection); // 현재 기준/방향 적용
    }

    private void UpdateSortControls()
    {
        if (sortDropdown != null)
        {
            int modeIndex = ItemSortComparer.GetInventoryModeIndex(sortMode);
            if (sortDropdown.value != modeIndex)
                sortDropdown.SetValueWithoutNotify(modeIndex); // 이벤트 방지

            sortDropdown.RefreshShownValue();
            ApplyDropdownTextStyle(sortDropdown); // 한글 보정
        }

        if (sortRefreshButtonText != null)
        {
            sortRefreshButtonText.text = sortDirection == ItemSortDirection.Ascending ? "▲" : "▼"; // 방향 표시
            ApplyKoreanFont(sortRefreshButtonText);
        }

        if (sortStatusText != null)
        {
            sortStatusText.text = ItemSortComparer.GetDescription(sortMode) + "\n방향: " + ItemSortComparer.GetDirectionDisplayName(sortDirection); // 상태 표시
        }
    }

    private void ConfigureDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
            return;

        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < ItemSortComparer.InventoryModes.Length; i++)
            options.Add(new TMP_Dropdown.OptionData(ItemSortComparer.GetDisplayName(ItemSortComparer.InventoryModes[i])));

        dropdown.options = options;
        dropdown.SetValueWithoutNotify(ItemSortComparer.GetInventoryModeIndex(sortMode)); // 이벤트 방지
        ExpandDropdownTemplate(dropdown, options.Count); // 스크롤 방지
        ApplyDropdownTextStyle(dropdown);
        dropdown.RefreshShownValue();
        SetDropdownCaptionText(dropdown);
    }

    private void ExpandDropdownTemplate(TMP_Dropdown dropdown, int optionCount)
    {
        if (dropdown == null || dropdown.template == null || optionCount <= 0)
            return;

        RectTransform template = dropdown.template;
        float itemHeight = 32f;
        Toggle itemToggle = template.GetComponentInChildren<Toggle>(true);
        if (itemToggle != null && itemToggle.transform is RectTransform itemRect && itemRect.rect.height > 0f)
            itemHeight = itemRect.rect.height;

        float targetHeight = itemHeight * optionCount + 8f; // 전체 항목 표시
        template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

        ScrollRect scrollRect = template.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect != null)
        {
            scrollRect.vertical = false; // 드롭다운 스크롤 비활성
            scrollRect.horizontal = false;
            scrollRect.verticalScrollbar = null;
        }

        RectTransform viewport = scrollRect != null ? scrollRect.viewport : template.Find("Viewport") as RectTransform;
        if (viewport != null)
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

        RectTransform content = scrollRect != null ? scrollRect.content : template.Find("Viewport/Content") as RectTransform;
        if (content != null)
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, itemHeight * optionCount);
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
            Debug.LogWarning("InventoryUI koreanFontAsset is not connected. Korean sort UI text may not render correctly.", this);
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

    private void BindSceneBagSlots()
    {
        if (slotBridge == null)
            return;

        if (bagSlots == null || bagSlots.Length < ActiveBagSlotCount)
        {
            Debug.LogWarning("InventoryUI requires scene-placed BagSlot_01 reference.", this);
            return;
        }

        for (int i = 0; i < ActiveBagSlotCount; i++)
        {
            if (bagSlots[i] != null)
                continue;

            Debug.LogWarning("InventoryUI bag slot reference is missing. Place BagPanel/BagSlot_01 in the scene and reconnect the InventoryUI bagSlots array.", this);
            return;
        }

        SlotUI[] activeBagSlots = new SlotUI[ActiveBagSlotCount];
        for (int i = 0; i < ActiveBagSlotCount; i++)
            activeBagSlots[i] = bagSlots[i];

        slotBridge.SetBagSlots(activeBagSlots); // 가방 슬롯 연결
    }

    private static string GetRuntimeKey(ItemData item)
    {
        if (item == null)
            return string.Empty;

        item.EnsureRuntimeState(); // 표시 추적용 id 보장
        return item.runtimeInstanceId;
    }
}
