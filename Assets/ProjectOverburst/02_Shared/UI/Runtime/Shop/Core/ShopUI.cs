using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ShopUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private MerchantTradeService tradeService;
    [SerializeField] private MerchantDefinition fallbackMerchantDefinition;
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private InventorySlotBridge inventorySlotBridge;
    [SerializeField] private ShopSlotBridge shopSlotBridge;
    [SerializeField] private ShopContextMenuController contextMenu;
    [SerializeField] private TMP_FontAsset koreanFontAsset;

    [Header("Tabs")]
    [SerializeField] private Button tradeTabButton;
    [SerializeField] private Button questTabButton;
    [SerializeField] private Image tradeTabImage;
    [SerializeField] private Image questTabImage;
    [SerializeField] private TextMeshProUGUI tradeTabText;
    [SerializeField] private TextMeshProUGUI questTabText;
    [SerializeField] private Button firstSpecialtyTabButton;
    [SerializeField] private Button secondSpecialtyTabButton;
    [SerializeField] private Image firstSpecialtyTabImage;
    [SerializeField] private Image secondSpecialtyTabImage;
    [SerializeField] private TextMeshProUGUI firstSpecialtyTabText;
    [SerializeField] private TextMeshProUGUI secondSpecialtyTabText;
    [SerializeField] private GameObject merchantInventoryWindowRoot;
    [SerializeField] private GameObject tradeWindowRoot;
    [SerializeField] private GameObject questListRoot;
    [SerializeField] private GameObject questDetailRoot;
    [SerializeField] private TextMeshProUGUI questStatusText;
    [SerializeField] private Button questAcceptButton;
    [SerializeField] private GameObject specialtyListRoot;
    [SerializeField] private GameObject specialtyDetailRoot;
    [SerializeField] private TextMeshProUGUI specialtyListTitleText;
    [SerializeField] private TextMeshProUGUI specialtyListBodyText;
    [SerializeField] private TextMeshProUGUI specialtyDetailTitleText;
    [SerializeField] private TextMeshProUGUI specialtyDetailDescriptionText;
    [SerializeField] private TextMeshProUGUI specialtyPrimaryTitleText;
    [SerializeField] private TextMeshProUGUI specialtyPrimaryBodyText;
    [SerializeField] private TextMeshProUGUI specialtySecondaryTitleText;
    [SerializeField] private TextMeshProUGUI specialtySecondaryBodyText;
    [SerializeField] private TextMeshProUGUI specialtyStatusText;
    [SerializeField] private Button specialtyActionButton;
    [SerializeField] private TextMeshProUGUI specialtyActionButtonText;

    [Header("Texts")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI merchantNameText;
    [SerializeField] private TextMeshProUGUI merchantGoldText;
    [SerializeField] private TextMeshProUGUI merchantDescriptionText;
    [SerializeField] private TextMeshProUGUI merchantValueText;
    [SerializeField] private TextMeshProUGUI playerValueText;
    [SerializeField] private TextMeshProUGUI autoGoldText;
    [SerializeField] private TextMeshProUGUI goldSummaryText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Merchant Top Panel")]
    [SerializeField] private GameObject merchantTopPanelRoot;
    [SerializeField] private TextMeshProUGUI merchantPortraitPlaceholderText;
    [SerializeField] private TextMeshProUGUI merchantPortraitCategoryText;
    [SerializeField] private TextMeshProUGUI reputationLevelText;
    [SerializeField] private Image reputationExpBarFill;
    [SerializeField] private TextMeshProUGUI reputationExpPercentText;
    [SerializeField] private TextMeshProUGUI reputationGradeText;
    [SerializeField] private TextMeshProUGUI reputationEffectsTitleText;
    [SerializeField] private TextMeshProUGUI reputationDiscountText;
    [SerializeField] private TextMeshProUGUI reputationStockGradeText;
    [SerializeField] private TextMeshProUGUI merchantGoldInfoText;

    [Header("Slots")]
    [SerializeField] private SlotUI[] merchantInventorySlots;
    [SerializeField] private SlotUI[] merchantOfferSlots;
    [SerializeField] private SlotUI[] playerOfferSlots;

    [Header("Controls")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button closeButton;

    [Header("Failure Popup")]
    [SerializeField] private GameObject failurePopupRoot;
    [SerializeField] private TextMeshProUGUI failurePopupMessageText;
    [SerializeField] private Button failurePopupConfirmButton;

    public const string PendingPlayerOffersSortMessage = "판매 제안 중에는 정렬할 수 없습니다.";

    public static bool HasPendingPlayerOffers(PlayerInventory target)
    {
        return target != null && openShop != null && openShop.IsOpen
            && openShop.tradeService != null
            && openShop.tradeService.PlayerInventory == target
            && openShop.tradeService.Session.PlayerOffers.Count > 0;
    }

    private string WithInventorySortNotice(string message)
    {
        return !failurePopupOpen && tradeService != null
            && HasPendingPlayerOffers(tradeService.PlayerInventory)
            ? message + "\n" + PendingPlayerOffersSortMessage
            : message;
    }

    private static ShopUI openShop;
    private MerchantDefinition currentMerchant;
    private bool isOpen;
    private bool initialized;
    private bool previousInventoryVisible;
    private bool inventoryToggleLockedByShop;
    private bool failurePopupOpen;
    private bool missingReferenceLogged;
    private ShopTab activeTab = ShopTab.Trade;
    private readonly System.Collections.Generic.List<ShopTab> availableTabs = new System.Collections.Generic.List<ShopTab>(4);
    private readonly ShopTab[] specialtyButtonTabs = new ShopTab[2];
    private readonly ShopMerchantTopPanelPresenter merchantTopPanelPresenter = new ShopMerchantTopPanelPresenter();
    private readonly ShopMerchantTopPanelView merchantTopPanelView = new ShopMerchantTopPanelView();
    private readonly ShopTradeSummaryPresenter tradeSummaryPresenter = new ShopTradeSummaryPresenter();
    private readonly ShopTradeSummaryView tradeSummaryView = new ShopTradeSummaryView();
    private readonly ShopFailurePopupPresenter failurePopupPresenter = new ShopFailurePopupPresenter();
    private readonly ShopFailurePopupView failurePopupView = new ShopFailurePopupView();
    private readonly ShopSlotPresenter slotPresenter = new ShopSlotPresenter();

    public bool IsOpen { get { return isOpen && shopPanel != null && shopPanel.activeSelf; } }
    public bool HasUsableCanvasRoot { get { return GetComponentInParent<Canvas>(true) != null; } }
    public bool IsTradeTabActive { get { return activeTab == ShopTab.Trade; } }

    private void Awake()
    {
        EnsureInitialized();
        SetPanelVisible(false);
    }

    private void OnEnable()
    {
        MerchantReputationService.ReputationChanged += HandleMerchantReputationChanged;
        MerchantStockRefreshService.StocksRefreshed += HandleMerchantStocksRefreshed;
    }

    private void OnDisable()
    {
        MerchantReputationService.ReputationChanged -= HandleMerchantReputationChanged;
        MerchantStockRefreshService.StocksRefreshed -= HandleMerchantStocksRefreshed;
        if (isOpen)
            Close();

        if (inventoryUI != null) inventoryUI.SetInputToggleLocked(this, false);
        inventoryToggleLockedByShop = false;
        GameplayInputBlocker.Unblock(this);
        ReleaseInteracting();
    }

    private void OnDestroy()
    {
        if (openShop == this)
            openShop = null;

        if (inventoryUI != null) inventoryUI.SetInputToggleLocked(this, false);
        inventoryToggleLockedByShop = false;
        GameplayInputBlocker.Unblock(this);
        ReleaseInteracting();
    }

    private void Update()
    {
        if (!isOpen)
            return;

        // GOAL A2: Esc/Enter/Space 직접 읽기 대신 UI Submit/Cancel을 사용한다. 팝업 우선 소비 감각 유지.
        // UI Submit에 Space가 없을 수 있어 기존 Space 감각은 Gameplay Jump 눌림으로 함께 받는다. UI 바인딩 변경 없음.
        // Tab 닫기는 Gameplay Inventory를 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null)
            return;

        if (failurePopupOpen)
        {
            if (facade.UiCancelPressedThisFrame || facade.UiSubmitPressedThisFrame || facade.JumpPressedThisFrame)
                HideFailurePopup();

            return;
        }

        if (facade.InventoryPressedThisFrame || facade.UiCancelPressedThisFrame)
            Close();
    }

    public void Open(MerchantDefinition merchantDefinition)
    {
        if (!EnsureInitialized())
            return;

        currentMerchant = merchantDefinition != null ? merchantDefinition : fallbackMerchantDefinition;
        if (currentMerchant == null)
        {
            SetStatus("상인 데이터가 없습니다.");
            return;
        }

        EnsureCanvasRootActive();
        RefreshAvailableTabs();
        OpenPlayerInventoryWindow();
        tradeService.Open(currentMerchant);
        isOpen = true;
        openShop = this;
        SetPanelVisible(true);
        GameplayInputBlocker.Block(this);
        // GOAL A2: 상점 세션 동안 Action.Interacting을 명시 요청한다. Close/OnDisable에서 해제.
        RequestInteracting();
        SetActiveTab(ShopTab.Trade, false);
        SetStatus("더블클릭, 드래그, 우클릭 거래 메뉴로 중앙 거래창에 올립니다.");
        Refresh();
    }

    public void Close()
    {
        if (!isOpen && (shopPanel == null || !shopPanel.activeSelf))
            return;

        isOpen = false;
        if (openShop == this)
            openShop = null;

        if (tradeService != null)
            tradeService.Close();

        contextMenu?.Close();
        HideFailurePopup();
        ClearPlayerInventorySelection();
        SetPanelVisible(false);
        RestorePlayerInventoryWindow();
        if (inventoryUI != null) inventoryUI.SetInputToggleLocked(this, false);
        inventoryToggleLockedByShop = false;
        GameplayInputBlocker.Unblock(this);
        ReleaseInteracting();
    }

    public bool IsOpenFor(MerchantDefinition merchantDefinition)
    {
        return IsOpen && currentMerchant == merchantDefinition;
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
            // Current가 이미 정리된 경우 씬 내 잔류 코디네이터를 직접 찾아 해제한다.
            PlayerStateCoordinator[] coordinators = FindObjectsByType<PlayerStateCoordinator>(FindObjectsSortMode.None);
            foreach (PlayerStateCoordinator coordinatorInScene in coordinators)
            {
                if (coordinatorInScene != null)
                    coordinatorInScene.ReleaseInteracting(this);
            }
        }
    }

    public void Refresh()
    {
        if (!EnsureInitialized())
            return;

        RefreshHeader();
        RefreshTabSelection();
        if (activeTab == ShopTab.Trade)
        {
            RefreshMerchantInventorySlots();
            RefreshOfferSlots();
            RefreshSummary();
            RefreshPlayerInventorySelection();
        }
        else
        {
            ClearPlayerInventorySelection();
        }
    }

    private bool EnsureInitialized()
    {
        if (initialized)
            return shopPanel != null && tradeService != null && shopSlotBridge != null;

        if (shopPanel == null)
            LogMissingReference(nameof(shopPanel));

        if (tradeService == null)
            tradeService = GetComponent<MerchantTradeService>() ?? GetComponentInParent<MerchantTradeService>(true);

        if (shopSlotBridge == null)
            shopSlotBridge = GetComponent<ShopSlotBridge>() ?? GetComponentInParent<ShopSlotBridge>(true);

        if (shopSlotBridge != null)
            shopSlotBridge.Init(this);

        EnsureContextMenu();

        if (inventoryUI == null)
            inventoryUI = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);

        if (inventorySlotBridge == null)
            inventorySlotBridge = inventoryUI != null ? inventoryUI.GetComponent<InventorySlotBridge>() : Object.FindFirstObjectByType<InventorySlotBridge>(FindObjectsInactive.Include);

        InitShopSlots(merchantInventorySlots);
        InitShopSlots(merchantOfferSlots);
        InitShopSlots(playerOfferSlots);
        BindControls();
        ApplyFonts();
        initialized = shopPanel != null && tradeService != null && shopSlotBridge != null;
        if (initialized)
            ValidateRequiredReferences();

        return initialized;
    }

    private void InitShopSlots(SlotUI[] slots)
    {
        if (slots == null || shopSlotBridge == null)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].Init(shopSlotBridge, i, false);
        }
    }

    private void BindControls()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            confirmButton.onClick.AddListener(HandleConfirmClicked);
        }

        if (clearButton != null)
        {
            clearButton.onClick.RemoveListener(HandleClearClicked);
            clearButton.onClick.AddListener(HandleClearClicked);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        if (failurePopupConfirmButton != null)
        {
            failurePopupConfirmButton.onClick.RemoveListener(HideFailurePopup);
            failurePopupConfirmButton.onClick.AddListener(HideFailurePopup);
        }

        if (tradeTabButton != null)
        {
            tradeTabButton.onClick.RemoveListener(ShowTradeTab);
            tradeTabButton.onClick.AddListener(ShowTradeTab);
        }

        if (questTabButton != null)
        {
            questTabButton.onClick.RemoveListener(ShowQuestTab);
            questTabButton.onClick.AddListener(ShowQuestTab);
        }

        if (firstSpecialtyTabButton != null)
        {
            firstSpecialtyTabButton.onClick.RemoveListener(ShowFirstSpecialtyTab);
            firstSpecialtyTabButton.onClick.AddListener(ShowFirstSpecialtyTab);
        }

        if (secondSpecialtyTabButton != null)
        {
            secondSpecialtyTabButton.onClick.RemoveListener(ShowSecondSpecialtyTab);
            secondSpecialtyTabButton.onClick.AddListener(ShowSecondSpecialtyTab);
        }

        if (questAcceptButton != null)
        {
            questAcceptButton.onClick.RemoveListener(HandleQuestAcceptClicked);
            questAcceptButton.onClick.AddListener(HandleQuestAcceptClicked);
        }

        if (specialtyActionButton != null)
        {
            specialtyActionButton.onClick.RemoveListener(HandleSpecialtyActionClicked);
            specialtyActionButton.onClick.AddListener(HandleSpecialtyActionClicked);
        }
    }

    private void EnsureCanvasRootActive()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>(true);
        if (parentCanvas != null && !parentCanvas.gameObject.activeSelf)
            parentCanvas.gameObject.SetActive(true);
    }

    private void HandleMerchantReputationChanged(string merchantId, int level)
    {
        if (!IsOpen || currentMerchant == null || currentMerchant.name != merchantId)
            return;

        RefreshHeader();
        RefreshSummary();
    }

    private void HandleMerchantStocksRefreshed()
    {
        if (!IsOpen || currentMerchant == null || tradeService == null)
            return;

        tradeService.ReloadCurrentMerchantInventory();
        SetStatus("상점 재고를 갱신했습니다.");
        Refresh();
    }

    private int IndexOf(SlotUI[] slots, SlotUI slot)
    {
        if (slots == null || slot == null)
            return -1;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == slot)
                return i;
        }

        return -1;
    }

    private void ValidateRequiredReferences()
    {
        if (AreSlotReferencesValid(merchantInventorySlots)
            && AreSlotReferencesValid(merchantOfferSlots)
            && AreSlotReferencesValid(playerOfferSlots)
            && failurePopupRoot != null
            && failurePopupMessageText != null
            && failurePopupConfirmButton != null
            && tradeTabButton != null
            && questTabButton != null
            && firstSpecialtyTabButton != null
            && secondSpecialtyTabButton != null
            && merchantInventoryWindowRoot != null
            && tradeWindowRoot != null
            && questListRoot != null
            && questDetailRoot != null
            && questAcceptButton != null
            && specialtyListRoot != null
            && specialtyDetailRoot != null
            && specialtyActionButton != null)
        {
            return;
        }

        if (missingReferenceLogged)
            return;

        missingReferenceLogged = true;
        Debug.LogWarning("[ShopUI] Shop UI scene references are incomplete. Reconnect serialized references on ShopUI instead of relying on hierarchy name search.", this);
    }

    private bool AreSlotReferencesValid(SlotUI[] slots)
    {
        if (slots == null || slots.Length == 0)
            return false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
                return false;
        }

        return true;
    }

    private void LogMissingReference(string referenceName)
    {
        Debug.LogWarning("[ShopUI] Missing scene reference: " + referenceName + ". Reconnect the serialized field on ShopUI.", this);
    }
}
