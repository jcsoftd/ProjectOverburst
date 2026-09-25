using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ShopUI : MonoBehaviour
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

        GameplayInputBlocker.Unblock(this);
        ReleaseInteracting();
    }

    private void OnDestroy()
    {
        if (openShop == this)
            openShop = null;

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

    public static bool TryHandleOpenPlayerInventorySlot(int slotIndex, out string message)
    {
        message = string.Empty;
        if (openShop == null || !openShop.IsOpen)
            return false;

        return openShop.HandlePlayerInventorySlotClicked(slotIndex, out message);
    }

    public static bool TryConsumeOpenPlayerInventorySingleClick()
    {
        if (openShop == null || !openShop.IsOpen)
            return false;

        if (!openShop.IsTradeTabActive)
        {
            openShop.SetStatus(openShop.GetInputBlockedByTabMessage());
            return true;
        }

        openShop.SetStatus("거래 등록은 더블클릭, 드래그, 우클릭 메뉴로 처리합니다.");
        return true;
    }

    public static bool TryOpenPlayerInventoryShopContextMenu(SlotClickContext context)
    {
        if (openShop == null || !openShop.IsOpen)
            return false;

        openShop.OpenPlayerInventoryContextMenu(context);
        return true;
    }

    public static bool TryGetOpenTooltipPriceContext(SlotUI slot, out MerchantDefinition merchant, out bool merchantSelling)
    {
        merchant = null;
        merchantSelling = false;
        if (openShop == null || !openShop.IsOpen || slot == null || slot.DisplayItem == null)
            return false;

        return openShop.TryGetTooltipPriceContext(slot, out merchant, out merchantSelling);
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

    public bool HandleShopSlotClicked(SlotUI slot)
    {
        if (!IsOpen || slot == null || tradeService == null)
            return false;

        if (!IsTradeTabActive)
            return true;

        int index = IndexOf(merchantInventorySlots, slot);
        if (index >= 0)
        {
            string message;
            if (!CanAddMerchantOffer(slot, out message))
            {
                SetStatus(message);
                ShowFailurePopup(message);
                return true;
            }

            tradeService.ToggleMerchantOffer(index, out message);
            SetStatus(message);
            Refresh();
            return true;
        }

        index = IndexOf(merchantOfferSlots, slot);
        if (index >= 0)
        {
            if (tradeService.RemoveOffer(MerchantTradeOfferSide.Merchant, index))
                SetStatus("상인 제안에서 제거했습니다.");

            Refresh();
            return true;
        }

        index = IndexOf(playerOfferSlots, slot);
        if (index >= 0)
        {
            if (tradeService.RemoveOffer(MerchantTradeOfferSide.Player, index))
                SetStatus("플레이어 제안에서 제거했습니다.");

            Refresh();
            return true;
        }

        return false;
    }

    public bool HandleShopSlotSingleClicked(SlotUI slot)
    {
        if (!IsOpen || slot == null || !ContainsShopSlot(slot))
            return false;

        if (!IsTradeTabActive)
            return true;

        SetStatus("거래 등록은 더블클릭, 드래그, 우클릭 메뉴로 처리합니다.");
        return true;
    }

    public bool HandleShopSlotRightClicked(SlotUI slot, PointerEventData eventData)
    {
        if (!IsOpen || slot == null || !ContainsShopSlot(slot))
            return false;

        if (!IsTradeTabActive)
            return true;

        if (slot.DisplayItem == null)
            return true;

        ShopContextMenuTarget target;
        if (!TryGetShopContextTarget(slot, out target))
            return true;

        EnsureContextMenu();
        if (contextMenu == null)
        {
            SetStatus("상점 거래 메뉴가 연결되지 않았습니다.");
            return true;
        }

        contextMenu.Open(target, slot, eventData);
        return true;
    }

    public bool HandleShopSlotDrop(SlotUI originSlot, SlotUI targetSlot)
    {
        if (!ContainsShopSlot(originSlot) && !ContainsShopSlot(targetSlot))
            return false;

        if (!IsTradeTabActive)
            return true;

        if (TryHandleAllowedShopDrop(originSlot, targetSlot))
            return true;

        SetStatus("각 아이템은 자기 거래창으로만 드래그할 수 있습니다.");
        return true;
    }

    public bool CanConsumeShopSlotDrop(SlotUI originSlot, SlotUI targetSlot)
    {
        if (!IsTradeTabActive)
            return false;

        return IsAllowedShopDrop(originSlot, targetSlot);
    }

    public bool ContainsShopSlot(SlotUI slot)
    {
        return IndexOf(merchantInventorySlots, slot) >= 0
            || IndexOf(merchantOfferSlots, slot) >= 0
            || IndexOf(playerOfferSlots, slot) >= 0;
    }

    public void ClearShopSlotDragPreview()
    {
        ClearDragOverlays(merchantInventorySlots);
        ClearDragOverlays(merchantOfferSlots);
        ClearDragOverlays(playerOfferSlots);
    }

    private bool HandlePlayerInventorySlotClicked(int sourceSlotIndex, out string message)
    {
        message = string.Empty;
        if (!IsOpen || tradeService == null)
            return false;

        if (!IsTradeTabActive)
        {
            message = GetInputBlockedByTabMessage();
            SetStatus(message);
            return true;
        }

        tradeService.TogglePlayerOffer(sourceSlotIndex, out message);
        SetStatus(message);
        Refresh();
        return true;
    }

    private void OpenPlayerInventoryContextMenu(SlotClickContext context)
    {
        if (context == null || context.Slot == null || context.Item == null)
            return;

        if (!IsTradeTabActive)
        {
            SetStatus(GetInputBlockedByTabMessage());
            return;
        }

        EnsureContextMenu();
        if (contextMenu == null)
        {
            SetStatus("상점 거래 메뉴가 연결되지 않았습니다.");
            return;
        }

        contextMenu.Open(ShopContextMenuTarget.PlayerInventory, context.Slot, context.EventData);
    }

    public bool CanShopContextTrade(ShopContextMenuTarget target, SlotUI slot)
    {
        if (!IsOpen || slot == null || slot.DisplayItem == null || tradeService == null || slot.IsLocked)
            return false;

        if (!IsTradeTabActive)
            return false;

        if (IsDirectTradeBlockedCurrency(slot.DisplayItem))
            return false;

        switch (target)
        {
            case ShopContextMenuTarget.MerchantInventory:
                string message;
                return IndexOf(merchantInventorySlots, slot) >= 0
                    && CanAddMerchantOffer(slot, out message)
                    && HasOfferCapacity(MerchantTradeOfferSide.Merchant, IndexOf(merchantInventorySlots, slot));
            case ShopContextMenuTarget.PlayerInventory:
                return IsPlayerInventoryTradeSlot(slot) && HasOfferCapacity(MerchantTradeOfferSide.Player, slot.SlotIndex);
            default:
                return false;
        }
    }

    public bool CanShopContextSplitTrade(ShopContextMenuTarget target, SlotUI slot)
    {
        return CanShopContextTrade(target, slot)
            && slot.DisplayItem.stackCount > 1
            && IsStackableTradeItem(slot.DisplayItem);
    }

    public bool HandleShopContextTrade(ShopContextMenuTarget target, SlotUI slot)
    {
        return ToggleOfferFromContextTarget(target, slot, 0);
    }

    public bool HandleShopContextSplitTrade(ShopContextMenuTarget target, SlotUI slot, int amount)
    {
        if (!CanShopContextSplitTrade(target, slot))
        {
            SetStatus("나눠서 거래할 수 없는 아이템입니다.");
            return false;
        }

        int clamped = Mathf.Clamp(amount, 1, Mathf.Max(1, slot.DisplayItem.stackCount - 1));
        return ToggleOfferFromContextTarget(target, slot, clamped);
    }

    public bool HandleShopContextRemoveOffer(ShopContextMenuTarget target, SlotUI slot)
    {
        if (slot == null || tradeService == null)
            return false;

        if (target == ShopContextMenuTarget.MerchantOffer)
            return RemoveOfferFromSlot(MerchantTradeOfferSide.Merchant, slot);

        if (target == ShopContextMenuTarget.PlayerOffer)
            return RemoveOfferFromSlot(MerchantTradeOfferSide.Player, slot);

        return false;
    }

    public void ShowShopContextItemInfo(ItemData item)
    {
        if (item == null)
            return;

        TooltipManager.Instance?.ShowTooltip(item);
    }

    public void ShowShopContextStatus(string message)
    {
        SetStatus(message);
    }

    public string GetShopContextBlockedTradeMessage(ShopContextMenuTarget target, SlotUI slot)
    {
        ItemData item = slot != null ? slot.DisplayItem : null;
        if (item != null && item.baseData is CurrencyItemData currencyData)
        {
            return currencyData.currencyType == CurrencyType.Gold
                ? "Gold는 거래창에 올리지 않고 부족분 자동 결제로 사용합니다."
                : "재화 아이템은 1차 거래창에 올릴 수 없습니다.";
        }

        if (target == ShopContextMenuTarget.MerchantInventory || target == ShopContextMenuTarget.PlayerInventory)
        {
            string message;
            if (target == ShopContextMenuTarget.MerchantInventory && !CanAddMerchantOffer(slot, out message))
                return message;

            return "거래창이 가득 찼거나 거래할 수 없는 슬롯입니다.";
        }

        return string.Empty;
    }

    private bool ToggleOfferFromContextTarget(ShopContextMenuTarget target, SlotUI slot, int requestedStackCount)
    {
        if (slot == null || tradeService == null)
            return false;

        string message = string.Empty;
        bool changed = false;
        switch (target)
        {
            case ShopContextMenuTarget.MerchantInventory:
                int merchantIndex = IndexOf(merchantInventorySlots, slot);
                if (!CanAddMerchantOffer(slot, out message))
                {
                    SetStatus(message);
                    ShowFailurePopup(message);
                    Refresh();
                    return false;
                }

                if (merchantIndex >= 0)
                    changed = tradeService.ToggleMerchantOffer(merchantIndex, requestedStackCount, out message);
                break;
            case ShopContextMenuTarget.PlayerInventory:
                if (IsPlayerInventoryTradeSlot(slot))
                    changed = tradeService.TogglePlayerOffer(slot.SlotIndex, requestedStackCount, out message);
                break;
        }

        if (!string.IsNullOrWhiteSpace(message))
            SetStatus(message);

        Refresh();
        return changed;
    }

    private bool TryHandleAllowedShopDrop(SlotUI originSlot, SlotUI targetSlot)
    {
        if (!IsOpen || originSlot == null || targetSlot == null || tradeService == null)
            return false;

        ShopContextMenuTarget originTarget;
        ShopContextMenuTarget targetTarget;
        TryGetShopContextTarget(originSlot, out originTarget);
        TryGetShopContextTarget(targetSlot, out targetTarget);
        bool originIsPlayerInventory = IsPlayerInventoryTradeSlot(originSlot);
        bool targetIsPlayerInventory = IsPlayerInventoryTradeSlot(targetSlot);

        if (originTarget == ShopContextMenuTarget.MerchantInventory && targetTarget == ShopContextMenuTarget.MerchantOffer)
        {
            string message;
            if (!CanAddMerchantOffer(originSlot, out message))
            {
                SetStatus(message);
                ShowFailurePopup(message);
                return true;
            }

            return ToggleOfferFromContextTarget(ShopContextMenuTarget.MerchantInventory, originSlot, 0);
        }

        if (originIsPlayerInventory && targetTarget == ShopContextMenuTarget.PlayerOffer)
            return ToggleOfferFromContextTarget(ShopContextMenuTarget.PlayerInventory, originSlot, 0);

        if (originTarget == ShopContextMenuTarget.MerchantOffer && targetTarget == ShopContextMenuTarget.MerchantInventory)
            return RemoveOfferFromSlot(MerchantTradeOfferSide.Merchant, originSlot);

        if (originTarget == ShopContextMenuTarget.PlayerOffer && targetIsPlayerInventory)
            return RemoveOfferFromSlot(MerchantTradeOfferSide.Player, originSlot);

        return false;
    }

    private bool IsAllowedShopDrop(SlotUI originSlot, SlotUI targetSlot)
    {
        if (!IsOpen || originSlot == null || targetSlot == null || tradeService == null)
            return false;

        ShopContextMenuTarget originTarget;
        ShopContextMenuTarget targetTarget;
        TryGetShopContextTarget(originSlot, out originTarget);
        TryGetShopContextTarget(targetSlot, out targetTarget);

        if (originTarget == ShopContextMenuTarget.MerchantInventory && targetTarget == ShopContextMenuTarget.MerchantOffer)
            return CanShopContextTrade(ShopContextMenuTarget.MerchantInventory, originSlot);

        if (IsPlayerInventoryTradeSlot(originSlot) && targetTarget == ShopContextMenuTarget.PlayerOffer)
            return CanShopContextTrade(ShopContextMenuTarget.PlayerInventory, originSlot);

        if (originTarget == ShopContextMenuTarget.MerchantOffer && targetTarget == ShopContextMenuTarget.MerchantInventory)
            return true;

        if (originTarget == ShopContextMenuTarget.PlayerOffer && IsPlayerInventoryTradeSlot(targetSlot))
            return true;

        return false;
    }

    private bool RemoveOfferFromSlot(MerchantTradeOfferSide side, SlotUI slot)
    {
        int index = side == MerchantTradeOfferSide.Merchant
            ? IndexOf(merchantOfferSlots, slot)
            : IndexOf(playerOfferSlots, slot);

        if (index < 0 || tradeService == null)
            return false;

        bool removed = tradeService.RemoveOffer(side, index);
        if (removed)
            SetStatus(side == MerchantTradeOfferSide.Merchant ? "상인 제안에서 제거했습니다." : "플레이어 제안에서 제거했습니다.");

        Refresh();
        return removed;
    }

    private bool TryGetShopContextTarget(SlotUI slot, out ShopContextMenuTarget target)
    {
        if (IndexOf(merchantInventorySlots, slot) >= 0)
        {
            target = ShopContextMenuTarget.MerchantInventory;
            return true;
        }

        if (IndexOf(merchantOfferSlots, slot) >= 0)
        {
            target = ShopContextMenuTarget.MerchantOffer;
            return true;
        }

        if (IndexOf(playerOfferSlots, slot) >= 0)
        {
            target = ShopContextMenuTarget.PlayerOffer;
            return true;
        }

        target = default;
        return false;
    }

    private bool IsPlayerInventoryTradeSlot(SlotUI slot)
    {
        return slot != null
            && slot.OwnerBridge is InventorySlotBridge
            && !slot.IsWeaponSlot
            && !slot.IsBagSlot
            && !slot.IsLocked
            && slot.DisplayItem != null;
    }

    private bool CanAddMerchantOffer(SlotUI slot, out string message)
    {
        ItemData item = slot != null ? slot.DisplayItem : null;
        return MerchantTemporaryArtifactStockPolicy.CanAddMerchantOffer(currentMerchant, item, out message);
    }

    private bool TryGetTooltipPriceContext(SlotUI slot, out MerchantDefinition merchant, out bool merchantSelling)
    {
        merchant = currentMerchant;
        merchantSelling = false;
        if (merchant == null || slot == null || slot.DisplayItem == null)
            return false;

        if (IndexOf(merchantInventorySlots, slot) >= 0 || IndexOf(merchantOfferSlots, slot) >= 0)
        {
            merchantSelling = true;
            return true;
        }

        if (IndexOf(playerOfferSlots, slot) >= 0 || IsPlayerInventoryTradeSlot(slot))
            return true;

        return false;
    }

    private bool HasOfferCapacity(MerchantTradeOfferSide side, int sourceSlotIndex)
    {
        if (tradeService == null)
            return false;

        MerchantTradeSession session = tradeService.Session;
        if (session.ContainsSource(side, sourceSlotIndex))
            return true;

        int count = side == MerchantTradeOfferSide.Merchant ? session.MerchantOffers.Count : session.PlayerOffers.Count;
        return count < session.MaxOffersPerSide;
    }

    private bool IsDirectTradeBlockedCurrency(ItemData item)
    {
        return item != null && item.baseData is CurrencyItemData;
    }

    private bool IsStackableTradeItem(ItemData item)
    {
        if (item == null || item.baseData is CurrencyItemData)
            return false;

        if (item.baseData is ConsumableItemData consumableData && consumableData.IsPermanentSingleItem)
            return false;

        string itemType = item.itemType;
        return itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem";
    }

    private void RefreshHeader()
    {
        SyncMerchantTopPanelViewReferences();
        merchantTopPanelPresenter.Refresh(merchantTopPanelView, activeTab, currentMerchant, tradeService);
        reputationExpBarFill = merchantTopPanelView.ReputationExpBarFill;
    }

    private void SyncMerchantTopPanelViewReferences()
    {
        merchantTopPanelView.HeaderTitleText = titleText;
        merchantTopPanelView.MerchantNameText = merchantNameText;
        merchantTopPanelView.MerchantGoldText = merchantGoldText;
        merchantTopPanelView.MerchantDescriptionText = merchantDescriptionText;
        merchantTopPanelView.Root = merchantTopPanelRoot;
        merchantTopPanelView.FallbackSearchRoot = shopPanel;
        merchantTopPanelView.MerchantPortraitPlaceholderText = merchantPortraitPlaceholderText;
        merchantTopPanelView.MerchantPortraitCategoryText = merchantPortraitCategoryText;
        merchantTopPanelView.ReputationLevelText = reputationLevelText;
        merchantTopPanelView.ReputationExpBarFill = reputationExpBarFill;
        merchantTopPanelView.ReputationExpPercentText = reputationExpPercentText;
        merchantTopPanelView.ReputationGradeText = reputationGradeText;
        merchantTopPanelView.ReputationEffectsTitleText = reputationEffectsTitleText;
        merchantTopPanelView.ReputationDiscountText = reputationDiscountText;
        merchantTopPanelView.ReputationStockGradeText = reputationStockGradeText;
        merchantTopPanelView.MerchantGoldInfoText = merchantGoldInfoText;
    }

    private void SyncTradeSummaryViewReferences()
    {
        tradeSummaryView.MerchantValueText = merchantValueText;
        tradeSummaryView.PlayerValueText = playerValueText;
        tradeSummaryView.AutoGoldText = autoGoldText;
        tradeSummaryView.GoldSummaryText = goldSummaryText;
        tradeSummaryView.ConfirmButton = confirmButton;
    }

    private void SyncFailurePopupViewReferences()
    {
        failurePopupView.Root = failurePopupRoot;
        failurePopupView.MessageText = failurePopupMessageText;
        failurePopupView.PopupConfirmButton = failurePopupConfirmButton;
        failurePopupView.TradeConfirmButton = confirmButton;
    }

    private void RefreshMerchantInventorySlots()
    {
        if (merchantInventorySlots == null || tradeService == null)
            return;

        slotPresenter.RefreshMerchantInventory(
            merchantInventorySlots,
            tradeService.MerchantInventory,
            tradeService.Session);
    }

    private void RefreshOfferSlots()
    {
        slotPresenter.RefreshOffers(merchantOfferSlots, tradeService != null ? tradeService.Session.MerchantOffers : null);
        slotPresenter.RefreshOffers(playerOfferSlots, tradeService != null ? tradeService.Session.PlayerOffers : null);
    }

    private void RefreshSummary()
    {
        SyncTradeSummaryViewReferences();
        tradeSummaryPresenter.Refresh(tradeSummaryView, tradeService, failurePopupOpen);
    }

    private void RefreshPlayerInventorySelection()
    {
        if (inventorySlotBridge == null || tradeService == null)
            return;

        inventorySlotBridge.RefreshSlotsWithOwnershipCheck();
        inventorySlotBridge.SetShopTradeSelectedSlots(tradeService.Session.GetSourceIndices(MerchantTradeOfferSide.Player));
    }

    private void ClearPlayerInventorySelection()
    {
        if (inventorySlotBridge != null)
            inventorySlotBridge.ClearShopTradeSelectedSlots();
    }

    private void HandleConfirmClicked()
    {
        if (tradeService == null || failurePopupOpen || !IsTradeTabActive)
            return;

        MerchantTradeResult validation = tradeService.ValidateTrade();
        if (!validation.success)
        {
            SetStatus(validation.message);
            ShowFailurePopup(validation.message);
            Refresh();
            return;
        }

        MerchantTradeResult result = tradeService.ExecuteTrade();
        SetStatus(result.message);
        if (!result.success)
            ShowFailurePopup(result.message);

        Refresh();
    }

    private void HandleClearClicked()
    {
        if (!IsTradeTabActive)
            return;

        if (tradeService != null)
            tradeService.Session.Clear();

        SetStatus("거래창을 비웠습니다.");
        Refresh();
    }

    private void OpenPlayerInventoryWindow()
    {
        if (inventoryUI == null)
            return;

        previousInventoryVisible = inventoryUI.IsVisible;
        inventoryUI.SetVisible(true);
        inventoryUI.InputToggleLocked = true;
        inventoryToggleLockedByShop = true;
    }

    private void RestorePlayerInventoryWindow()
    {
        if (inventoryUI == null)
            return;

        if (inventoryToggleLockedByShop)
        {
            inventoryUI.InputToggleLocked = false;
            inventoryToggleLockedByShop = false;
        }

        inventoryUI.SetVisible(previousInventoryVisible);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
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

    private void EnsureContextMenu()
    {
        if (contextMenu == null)
            contextMenu = GetComponent<ShopContextMenuController>() ?? GetComponentInParent<ShopContextMenuController>(true);

        if (contextMenu == null)
        {
            Debug.LogError("[ShopUI] ShopContextMenuController reference is missing. Run the Shop Context UI objectizer.", this);
            return;
        }

        contextMenu.Init(this, koreanFontAsset);
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

    private void ApplyFonts()
    {
        if (koreanFontAsset == null)
            return;

        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null)
                continue;

            texts[i].font = koreanFontAsset;
            texts[i].fontSharedMaterial = koreanFontAsset.material;
        }
    }

    private void EnsureCanvasRootActive()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>(true);
        if (parentCanvas != null && !parentCanvas.gameObject.activeSelf)
            parentCanvas.gameObject.SetActive(true);
    }

    private void SetPanelVisible(bool visible)
    {
        if (shopPanel != null)
            shopPanel.SetActive(visible);
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

    private void SetGameObjectActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private void ShowFailurePopup(string message)
    {
        SyncFailurePopupViewReferences();
        string fallbackStatus;
        if (!failurePopupPresenter.Show(failurePopupView, message, out fallbackStatus))
        {
            LogMissingReference("TradeFailurePopup");
            SetStatus(fallbackStatus);
            return;
        }

        failurePopupOpen = true;
    }

    private void HideFailurePopup()
    {
        SyncFailurePopupViewReferences();
        failurePopupOpen = false;
        failurePopupPresenter.Hide(failurePopupView);
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

    private void ClearDragOverlays(SlotUI[] slots)
    {
        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
            slots[i]?.ClearDragOverlay();
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
