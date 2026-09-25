using System;
using UnityEngine;

public class InventorySlotBridge : MonoBehaviour, ISlotInteractionBridge, ISlotSingleClickInteractionBridge, ISlotRightClickInteractionBridge // 인벤토리 슬롯 정책
{
    public event Action<WeaponComboGemEquipResult> ComboGemInventoryDropCompleted;

    [Header("References")]
    [SerializeField] private PlayerInventory inventory; // 일반 슬롯 데이터
    [SerializeField] private PlayerEquipment playerEquipment; // 장착 슬롯 데이터
    [SerializeField] private InventoryUI inventoryUI; // UI 상태
    [SerializeField] private PickupGradeVfxSet pickupGradeVfxSet; // 월드 드롭 VFX
    [SerializeField] private PlayerMovement playerMovement; // 가방 이동속도 적용 대상
    [SerializeField] private PlayerStaminaController playerStaminaController; // 가방 스태미너 적용 대상
    [SerializeField] private CombatHealth playerHealth; // 가방 HP 적용 대상

    [Header("World Drop")]
    [SerializeField] private float worldDropForwardDistance = 1.25f; // 전방 거리
    [SerializeField] private float worldDropSpawnHeight = 0.85f; // 생성 높이
    [SerializeField] private float worldDropGroundProbeHeight = 2f; // 바닥 탐색 높이
    [SerializeField] private float worldDropGroundProbeDistance = 5f; // 바닥 탐색 거리

    [Header("Slots")]
    [SerializeField] private int baseInventorySlotCount = 16; // 기본 칸
    [SerializeField] private SlotUI[] inventorySlots; // 일반 슬롯
    [SerializeField] private SlotUI[] weaponSlots; // 무기 슬롯
    [SerializeField] private SlotUI[] bagSlots; // 가방 슬롯

    private const int EquippedBagSlotCount = 1; // 현재 장착 가방 슬롯 수

    private ItemData[] equippedBags = new ItemData[EquippedBagSlotCount]; // 장착 가방
    private SlotUI previewOriginSlot; // preview 원본
    private bool normalizingEquippedWeaponOwnership; // 중복 정리 중
    private bool allowEquippedWeaponInInventory; // 장착 이동 예외
    private bool synchronizingInventoryState; // 동기화 중
    private bool suppressSlotEventRefresh; // 이벤트 억제
    private PlayerMovement appliedBagMovementTarget; // 이동속도 적용 대상
    private PlayerStaminaController appliedBagStaminaTarget; // 스태미너 적용 대상
    private CombatHealth appliedBagHealthTarget; // HP 적용 대상
    private float appliedBagMaxStaminaBonus; // 기존 적용 스태미너
    private float appliedBagMaxHpBonus; // 기존 적용 HP
    private const int RequiredWeaponSlotCount = 1; // 무기 슬롯 수

    private readonly struct SlotMoveResult // 슬롯 이동 결과
    {
        public bool Succeeded { get; }
        public string Message { get; }

        private SlotMoveResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public static SlotMoveResult Success()
        {
            return new SlotMoveResult(true, string.Empty);
        }

        public static SlotMoveResult Fail(string message)
        {
            return new SlotMoveResult(false, message);
        }
    }

    private readonly struct InventorySourceSnapshot // 원본 백업
    {
        public int SlotIndex { get; }
        public ItemData Item { get; }

        public InventorySourceSnapshot(int slotIndex, ItemData item)
        {
            SlotIndex = slotIndex;
            Item = item;
        }

        public bool IsValid => SlotIndex >= 0 && Item != null;
    }

    private void Awake()
    {
        ResolveReferences();
        InitAllSlots();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InitAllSlots();

        if (inventory != null)
            inventory.Changed += HandleInventoryChanged;

        if (playerEquipment != null)
            playerEquipment.WeaponSlotsChanged += HandleWeaponSlotsChanged;


        RefreshSlotsWithOwnershipCheck();
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.Changed -= HandleInventoryChanged;

        if (playerEquipment != null)
            playerEquipment.WeaponSlotsChanged -= HandleWeaponSlotsChanged;

    }

    public void SetInventorySlots(SlotUI[] slots)
    {
        inventorySlots = slots;
        InitSlots(inventorySlots, false);
        RefreshSlotsWithOwnershipCheck();
    }

    public void SetWeaponSlots(SlotUI[] slots)
    {
        weaponSlots = slots;
        EnsureWeaponSlotCount();
        InitSlots(weaponSlots, true);
        RefreshSlotsWithOwnershipCheck();
    }

    public void SetBagSlots(SlotUI[] slots)
    {
        bagSlots = slots;
        InitBagSlots();
        RefreshSlotsWithOwnershipCheck();
    }

    public void RefreshSlots()
    {
        ClearAllDragOverlays(); // preview 제거
        int unlockedSlotCount = GetUnlockedInventorySlotCount(); // 해금 칸

        if (inventorySlots != null)
        {
            for (int i = 0; i < inventorySlots.Length; i++)
            {
                if (inventorySlots[i] == null)
                    continue;

                ItemData item = inventory != null && i < inventory.Items.Count ? inventory.Items[i] : null;
                inventorySlots[i].SetDisplayItem(item);
                inventorySlots[i].SetLocked(i >= unlockedSlotCount); // 잠금 표시
                inventorySlots[i].SetNewItemMarker(item != null && i < unlockedSlotCount && inventoryUI != null && inventoryUI.IsNewlyAcquiredItem(item)); // 신규 표시
            }
        }

        if (weaponSlots != null)
        {
            for (int i = 0; i < weaponSlots.Length; i++)
            {
                if (weaponSlots[i] != null)
                {
                    weaponSlots[i].SetDisplayItem(GetEquippedWeapon(i));
                    weaponSlots[i].SetNewItemMarker(false); // 장착 슬롯 제외
                    weaponSlots[i].SetActiveWeaponSlot(IsActiveWeaponSlot(i)); // 현재 리더
                }
            }
        }

        if (bagSlots != null)
        {
            int activeBagSlotCount = Mathf.Min(bagSlots.Length, EquippedBagSlotCount);
            for (int i = 0; i < activeBagSlotCount; i++)
            {
                if (bagSlots[i] != null)
                {
                    bagSlots[i].SetDisplayItem(i < equippedBags.Length ? equippedBags[i] : null);
                    bagSlots[i].SetNewItemMarker(false); // 가방 슬롯 제외
                }
            }
        }
    }

    public void RefreshSlotsWithOwnershipCheck()
    {
        SynchronizeInventoryStateBeforeRefresh(); // 소유권 정리
        ApplyEquippedBagStatBonuses(); // 가방 능력치
        RefreshSlots(); // UI 반영
    }

    public bool SortInventory(ItemSortMode sortMode, ItemSortDirection sortDirection)
    {
        if (inventory == null)
            return false;

        bool sorted = RunSlotDataMutation(() => inventory.SortUnlockedSlots(sortMode, sortDirection));
        RefreshSlotsAfterDataChange();
        return sorted;
    }

    private void HandleInventoryChanged()
    {
        if (synchronizingInventoryState || suppressSlotEventRefresh)
            return;

        RefreshSlotsWithOwnershipCheck();
    }

    private void HandleWeaponSlotsChanged()
    {
        if (synchronizingInventoryState || suppressSlotEventRefresh)
            return;

        RefreshSlotsWithOwnershipCheck();
    }

    private void RefreshSlotsAfterDataChange()
    {
        RefreshSlotsWithOwnershipCheck();
    }

    private void SynchronizeInventoryStateBeforeRefresh()
    {
        if (synchronizingInventoryState)
            return;

        synchronizingInventoryState = true;

        try
        {
            NormalizeInventoryOwnership();
            SyncInventorySlotCapacity();
        }
        finally
        {
            synchronizingInventoryState = false;
        }
    }

    private bool NormalizeInventoryOwnership()
    {
        return NormalizeEquippedWeaponOwnership();
    }

    private bool SyncInventorySlotCapacity()
    {
        return inventory != null && inventory.SetUnlockedSlotCount(GetUnlockedInventorySlotCount());
    }

    private T RunSlotDataMutation<T>(Func<T> operation)
    {
        bool previousSuppressState = suppressSlotEventRefresh;
        suppressSlotEventRefresh = true; // 중복 refresh 방지

        try
        {
            return operation();
        }
        finally
        {
            suppressSlotEventRefresh = previousSuppressState;
        }
    }

    public void BeginDragPreview(SlotUI originSlot)
    {
        previewOriginSlot = originSlot; // 드래그 원본
        ShowDragPreviewForTarget(null);
    }

    public void ShowDragPreviewForTarget(SlotUI targetSlot)
    {
        if (previewOriginSlot == null && DragSlot.EquippedComboGemSource == null)
        {
            previewOriginSlot = DragSlot.OriginSlot; // 다른 bridge에서 시작한 드래그 보정
            if (previewOriginSlot == null)
                return;
        }

        ClearAllDragOverlays(); // 이전 preview
        ApplyBagCapacityPreview(targetSlot); // 가방 칸 변화

        if (CanPreviewDropToTarget(targetSlot))
            targetSlot.SetDragOverlay(SlotDragOverlayState.WillUnlock); // 드롭 가능 표시
    }

    public void ClearDragPreview()
    {
        previewOriginSlot = null;
        ClearAllDragOverlays();
    }

    public bool HandleSlotClick(SlotClickContext context)
    {
        if (context == null || context.Slot == null || context.Item == null || context.Slot.IsLocked)
            return false;

        if (TryHandleOpenShopPlayerSlot(context))
            return true;

        bool handled;
        StashSlotBridge openStashBridge = StashSlotBridge.FindOpenBridge(); // 열린 창고
        if (openStashBridge != null && !context.Slot.IsWeaponSlot && !context.Slot.IsBagSlot)
        {
            handled = openStashBridge.TryMoveInventorySlotToFirstAvailableStashSlot(context.Slot.SlotIndex); // 창고 우선 이동
            return handled;
        }

        if (context.Slot.IsBagSlot)
            handled = UnequipBagToFirstAvailableSlot(context.Slot.SlotIndex);
        else if (context.Item.itemType == "Bag")
            handled = EquipBagFromInventorySlot(context.Slot);
        else if (context.Item.itemType == "Gear" && !context.Slot.IsWeaponSlot)
            handled = GearEquipmentService.EquipFromInventorySlot(context.Slot.SlotIndex);
        else if (context.Item.itemType != "Weapon")
            handled = false;
        else if (context.IsWeaponSlot)
            handled = UnequipWeaponToFirstAvailableSlot(context.Slot.SlotIndex);
        else
            handled = EquipWeaponFromInventorySlot(context.Slot);

        return handled;
    }

    public bool HandleSlotSingleClick(SlotClickContext context)
    {
        if (ShopUI.TryConsumeOpenPlayerInventorySingleClick())
            return true;

        return context != null
            && context.IsWeaponSlot
            && WeaponComboGemPopupPresenter.TryOpen(context.Item, this); // 장착 무기 상세 고정 팝업
    }

    public bool TryGetComboGemInventory(out PlayerInventory ownerInventory)
    {
        ownerInventory = inventory;
        return ownerInventory != null;
    }

    public bool TryGetComboGemEquipment(out PlayerEquipment equipment)
    {
        ResolveReferences();
        equipment = playerEquipment;
        return equipment != null;
    }

    public bool TryResolveComboGemCandidate(int slotIndex, out PlayerInventory ownerInventory, out ItemData candidate)
    {
        ownerInventory = inventory;
        candidate = null;
        if (inventory == null
            || slotIndex < 0
            || slotIndex >= inventory.UnlockedSlotCount
            || inventorySlots == null
            || slotIndex >= inventorySlots.Length
            || inventorySlots[slotIndex] == null
            || inventorySlots[slotIndex].IsWeaponSlot
            || inventorySlots[slotIndex].IsBagSlot
            || inventorySlots[slotIndex].IsLocked)
        {
            return false;
        }

        candidate = inventory.GetItemAt(slotIndex);
        return candidate != null && candidate.baseData is ComboGemItemData;
    }

    public bool HandleSlotRightClick(SlotClickContext context)
    {
        return ShopUI.TryOpenPlayerInventoryShopContextMenu(context);
    }

    public bool HandleSlotDrop(SlotDropContext context)
    {
        if (context == null)
            return false;

        if (context.EquippedComboGemSource != null)
            return HandleEquippedComboGemDrop(context);

        bool handled;
        if (context.TargetSlot != null && context.TargetSlot.IsBagSlot)
            handled = HandleDropToBagSlot(context.OriginSlot, context.TargetSlot); // 가방 장착
        else if (context.OriginSlot != null && context.OriginSlot.IsBagSlot)
            handled = HandleDropFromBagSlot(context.OriginSlot, context.TargetSlot); // 가방 해제
        else
            handled = HandleSlotDrop(context.OriginSlot, context.TargetSlot);

        return handled;
    }

    public bool HandleSlotDrop(SlotUI sourceSlot, SlotUI targetSlot)
    {
        if (sourceSlot == null || targetSlot == null || sourceSlot == targetSlot || sourceSlot.IsLocked || targetSlot.IsLocked)
            return false;

        bool handled;
        if (sourceSlot.IsWeaponSlot)
            handled = HandleWeaponSlotDrop(sourceSlot, targetSlot); // 무기 해제
        else if (targetSlot.IsWeaponSlot)
            handled = HandleDropToWeaponSlot(sourceSlot, targetSlot); // 무기 장착
        else
            handled = HandleInventorySlotDrop(sourceSlot, targetSlot);

        return handled;
    }

    public bool HandleExternalDrop(SlotUI sourceSlot)
    {
        return HandleExternalDropResult(sourceSlot, Vector2.zero).Succeeded;
    }

    public bool HandleExternalDrop(SlotUI sourceSlot, Vector2 screenPosition)
    {
        return HandleExternalDropResult(sourceSlot, screenPosition).Succeeded;
    }

    public InventoryActionResult HandleExternalDropResult(SlotUI sourceSlot, Vector2 screenPosition)
    {
        if (sourceSlot == null || sourceSlot.DisplayItem == null)
            return InventoryActionResult.Fail(InventoryActionFailureReason.InvalidSource, "드롭할 슬롯 아이템이 없습니다.");

        if (sourceSlot.IsBagSlot)
            return InventoryActionResult.Fail(InventoryActionFailureReason.BlockedSlotType, "장착 가방 슬롯 월드 드롭은 1차에서 지원하지 않습니다.");

        InventoryWorldDropRequest request = new InventoryWorldDropRequest
        {
            Inventory = inventory, // 드롭 주체
            InventoryUI = inventoryUI,
            SourceSlot = sourceSlot,
            PlayerEquipment = playerEquipment,
            FallbackTransform = transform,
            PickupGradeVfxSet = pickupGradeVfxSet,
            ScreenPosition = screenPosition,
            ForwardDistance = worldDropForwardDistance,
            SpawnHeight = worldDropSpawnHeight,
            GroundProbeHeight = worldDropGroundProbeHeight,
            GroundProbeDistance = worldDropGroundProbeDistance
        };

        InventoryActionResult result = RunSlotDataMutation(() => InventoryWorldDrop.TryDropInventorySlotToWorld(request)); // 월드 드롭

        if (result.Succeeded)
            RefreshSlotsAfterDataChange();

        return result;
    }

    public bool EquipWeaponFromContextMenu(SlotUI sourceSlot, int weaponSlotIndex)
    {
        return EquipWeaponFromInventorySlot(sourceSlot, weaponSlotIndex);
    }

    public bool UnequipWeaponFromContextMenu(int weaponSlotIndex, out string failureMessage)
    {
        failureMessage = string.Empty;

        if (inventory == null || !HasWeaponEquipment())
        {
            failureMessage = "Inventory or PlayerEquipment reference is missing.";
            return false;
        }

        ItemData currentWeapon = GetEquippedWeapon(weaponSlotIndex);
        if (currentWeapon == null)
        {
            failureMessage = "Weapon slot is empty.";
            return false;
        }

        if (!inventory.ContainsItem(currentWeapon) && inventory.FindFirstEmptySlot() < 0)
        {
            failureMessage = "인벤토리에 빈 슬롯이 없어 장착해제할 수 없습니다.";
            return false;
        }

        bool result = UnequipWeaponToFirstAvailableSlot(weaponSlotIndex);
        if (!result)
        {
            failureMessage = "Weapon unequip failed.";
            return false;
        }

        RefreshCurrentWeaponStats();
        RefreshSlotsAfterDataChange();
        return true;
    }

    public ItemData GetWeaponSlotItemFromContextMenu(int weaponSlotIndex)
    {
        return GetEquippedWeapon(weaponSlotIndex);
    }

    public void RefreshSlotsFromContextMenu()
    {
        RefreshSlotsAfterDataChange();
    }

    public void SetShopTradeSelectedSlots(System.Collections.Generic.IList<int> selectedIndices)
    {
        if (inventorySlots == null)
            return;

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            SlotUI slot = inventorySlots[i];
            if (slot == null)
                continue;

            slot.SetContextSelected(ContainsIndex(selectedIndices, i));
        }
    }

    public void ClearShopTradeSelectedSlots()
    {
        SetShopTradeSelectedSlots(null);
    }

    private bool TryHandleOpenShopPlayerSlot(SlotClickContext context)
    {
        if (!IsShopTradeEligibleInventorySlot(context))
            return false;

        string message;
        return ShopUI.TryHandleOpenPlayerInventorySlot(context.Slot.SlotIndex, out message);
    }

    private bool IsShopTradeEligibleInventorySlot(SlotClickContext context)
    {
        return context != null
            && context.Slot != null
            && context.Item != null
            && !context.Slot.IsLocked
            && !context.Slot.IsWeaponSlot
            && !context.Slot.IsBagSlot;
    }

    private bool ContainsIndex(System.Collections.Generic.IList<int> selectedIndices, int index)
    {
        if (selectedIndices == null)
            return false;

        for (int i = 0; i < selectedIndices.Count; i++)
        {
            if (selectedIndices[i] == index)
                return true;
        }

        return false;
    }

    private void ResolveReferences()
    {
        if (inventory == null)
            inventory = FindFirstObjectByType<PlayerInventory>(); // Persistent 데이터

        if (playerEquipment == null)
            playerEquipment = PlayerContext.GetOrCreate()?.CurrentActorEquipment;

        if (inventoryUI == null)
            inventoryUI = GetComponent<InventoryUI>() ?? GetComponentInParent<InventoryUI>() ?? FindFirstObjectByType<InventoryUI>(); // UI 참조

        ResolveBagStatTargets();
    }

    private void InitSlots(SlotUI[] slots, bool isWeaponSlot)
    {
        if (isWeaponSlot)
            EnsureWeaponSlotCount();

        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            SlotUI slot = slots[i];
            if (slot == null)
                continue;

            slot.Init(this, i, isWeaponSlot);
        }
    }

    private void InitBagSlots()
    {
        if (equippedBags == null || equippedBags.Length != EquippedBagSlotCount)
            equippedBags = new ItemData[EquippedBagSlotCount];

        if (bagSlots == null)
            return;

        int activeBagSlotCount = Mathf.Min(bagSlots.Length, EquippedBagSlotCount);
        for (int i = 0; i < activeBagSlotCount; i++)
        {
            if (bagSlots[i] != null)
                bagSlots[i].Init(this, i, false, true);
        }
    }

    private void InitAllSlots()
    {
        EnsureWeaponSlotCount();
        InitSlots(inventorySlots, false);
        InitSlots(weaponSlots, true);
        InitBagSlots();
    }

    private bool NormalizeEquippedWeaponOwnership()
    {
        if (inventory == null || !HasWeaponEquipment() || allowEquippedWeaponInInventory || normalizingEquippedWeaponOwnership)
            return false;

        normalizingEquippedWeaponOwnership = true; // 재진입 방지
        bool changed = false;

        for (int i = 0; i < RequiredWeaponSlotCount; i++)
        {
            ItemData equippedWeapon = GetEquippedWeapon(i);
            if (equippedWeapon == null)
                continue;

            if (inventory.ClearAllMatchingItems(equippedWeapon)) // 장착품 중복 제거
                changed = true;
        }

        normalizingEquippedWeaponOwnership = false;
        return changed;
    }

    private void EnsureWeaponSlotCount()
    {
        if (weaponSlots == null || weaponSlots.Length < RequiredWeaponSlotCount)
        {
            Debug.LogWarning("InventorySlotBridge requires scene-placed WeaponSlot_01, WeaponSlot_02, WeaponSlot_03 references.", this);
            return;
        }

        for (int i = 0; i < RequiredWeaponSlotCount; i++)
        {
            if (weaponSlots[i] == null)
                Debug.LogWarning("InventorySlotBridge weapon slot reference is missing. Connect WeaponSlot_01~03 in order.", this);
        }
    }

    private void ApplyBagCapacityPreview(SlotUI targetSlot)
    {
        if (targetSlot == null || inventorySlots == null)
            return;

        ItemData[] proposedBags; // 가상 가방
        int ignoredInventorySlotIndex = -1; // 이동 원본

        if (!TryBuildProposedBagsForPreview(targetSlot, out proposedBags, out ignoredInventorySlotIndex))
            return;

        if (!CanApplyBagLoadout(proposedBags, ignoredInventorySlotIndex))
            return;

        int currentUnlockedSlots = GetUnlockedInventorySlotCount();
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags);

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i] == null)
                continue;

            bool currentlyUnlocked = i < currentUnlockedSlots;
            bool willBeUnlocked = i < proposedUnlockedSlots;

            if (!currentlyUnlocked && willBeUnlocked)
                inventorySlots[i].SetDragOverlay(SlotDragOverlayState.WillUnlock);
            else if (currentlyUnlocked && !willBeUnlocked)
                inventorySlots[i].SetDragOverlay(SlotDragOverlayState.WillLock);
        }
    }

    private bool TryBuildProposedBagsForPreview(SlotUI targetSlot, out ItemData[] proposedBags, out int ignoredInventorySlotIndex)
    {
        proposedBags = null;
        ignoredInventorySlotIndex = -1;

        if (previewOriginSlot == null || targetSlot == null)
            return false;

        proposedBags = CopyEquippedBags();

        if (targetSlot.IsBagSlot && IsBagItem(previewOriginSlot.DisplayItem) && !previewOriginSlot.IsBagSlot)
        {
            if (!IsBagSlotIndexValid(targetSlot.SlotIndex))
                return false;

            proposedBags[targetSlot.SlotIndex] = previewOriginSlot.DisplayItem; // 장착 가정
            ignoredInventorySlotIndex = previewOriginSlot.SlotIndex; // 원본 제외
            return true;
        }

        if (!targetSlot.IsWeaponSlot && !targetSlot.IsBagSlot && previewOriginSlot.IsBagSlot)
        {
            if (!IsBagSlotIndexValid(previewOriginSlot.SlotIndex))
                return false;

            proposedBags[previewOriginSlot.SlotIndex] = null; // 해제 가정
            return true;
        }

        return false;
    }

    private void ClearAllDragOverlays()
    {
        ClearDragOverlays(inventorySlots);
        ClearDragOverlays(weaponSlots);
        ClearDragOverlays(bagSlots);
    }

    private void ClearDragOverlays(SlotUI[] slots)
    {
        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].ClearDragOverlay();
        }
    }

    private bool CanPreviewEquipBagIntoSlot(SlotUI sourceSlot, int bagSlotIndex)
    {
        if (inventory == null || sourceSlot == null || !IsBagSlotIndexValid(bagSlotIndex))
            return false;

        ItemData newBag = sourceSlot.DisplayItem;

        if (!IsBagItem(newBag))
            return false;

        ItemData oldBag = equippedBags[bagSlotIndex];
        ItemData[] proposedBags = CopyEquippedBags();
        proposedBags[bagSlotIndex] = newBag;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags);

        if (!CanApplyBagLoadout(proposedBags, sourceSlot.SlotIndex))
            return false;

        return oldBag == null || sourceSlot.SlotIndex < proposedUnlockedSlots;
    }

    private bool CanPreviewDropToTarget(SlotUI targetSlot)
    {
        if (DragSlot.EquippedComboGemSource != null)
            return CanAcceptEquippedComboGemDrop(DragSlot.EquippedComboGemSource, targetSlot);

        SlotUI sourceSlot = previewOriginSlot != null ? previewOriginSlot : DragSlot.OriginSlot;

        if (targetSlot == null || targetSlot.IsLocked)
            return false;

        if (sourceSlot == null || sourceSlot == targetSlot || sourceSlot.IsLocked || sourceSlot.DisplayItem == null)
            return false;

        if (targetSlot.IsBagSlot)
            return sourceSlot.IsBagSlot || CanPreviewEquipBagIntoSlot(sourceSlot, targetSlot.SlotIndex);

        if (sourceSlot.IsBagSlot)
            return CanPreviewUnequipBagToTarget(sourceSlot, targetSlot);

        if (targetSlot.IsWeaponSlot)
            return CanPreviewWeaponSlotTarget(sourceSlot, targetSlot);

        if (sourceSlot.IsWeaponSlot)
            return targetSlot.DisplayItem == null;

        return !targetSlot.IsBagSlot && !targetSlot.IsWeaponSlot;
    }

    private bool HandleEquippedComboGemDrop(SlotDropContext context)
    {
        EquippedComboGemInventoryDropSource source = context.EquippedComboGemSource;
        SlotUI targetSlot = context.TargetSlot;
        if (!CanAcceptEquippedComboGemDrop(source, targetSlot))
            return false;

        WeaponComboGemEquipResult result = RunSlotDataMutation(
            () => source.TryUnequip(inventory, targetSlot.SlotIndex)); // 70 원자 서비스 위임
        ComboGemInventoryDropCompleted?.Invoke(result);
        if (!result.Succeeded)
            return false;

        RefreshSlotsAfterDataChange();
        return true;
    }

    private bool CanAcceptEquippedComboGemDrop(EquippedComboGemInventoryDropSource source, SlotUI targetSlot)
    {
        if (inventory == null || source == null || !source.MatchesCurrentSource()
            || targetSlot == null || !ReferenceEquals(targetSlot.OwnerBridge, this)
            || targetSlot.IsWeaponSlot || targetSlot.IsBagSlot || targetSlot.IsLocked
            || targetSlot.DisplayItem != null)
        {
            return false;
        }

        int targetSlotIndex = targetSlot.SlotIndex;
        return targetSlotIndex >= 0
            && targetSlotIndex < inventory.UnlockedSlotCount
            && inventorySlots != null
            && targetSlotIndex < inventorySlots.Length
            && inventorySlots[targetSlotIndex] == targetSlot
            && inventory.GetItemAt(targetSlotIndex) == null;
    }

    private bool CanPreviewUnequipBagToTarget(SlotUI sourceBagSlot, SlotUI targetSlot)
    {
        if (sourceBagSlot == null || !sourceBagSlot.IsBagSlot || targetSlot == null || targetSlot.IsWeaponSlot || targetSlot.IsBagSlot || targetSlot.IsLocked)
            return false;

        if (inventory == null || targetSlot.DisplayItem != null)
            return false;

        ItemData[] proposedBags = CopyEquippedBags();
        if (!IsBagSlotIndexValid(sourceBagSlot.SlotIndex))
            return false;

        proposedBags[sourceBagSlot.SlotIndex] = null;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags);
        return targetSlot.SlotIndex < proposedUnlockedSlots && CanApplyBagLoadout(proposedBags);
    }

    private bool CanPreviewWeaponSlotTarget(SlotUI sourceSlot, SlotUI targetWeaponSlot)
    {
        if (sourceSlot == null || targetWeaponSlot == null || sourceSlot.DisplayItem == null)
            return false;

        if (sourceSlot.DisplayItem.itemType == "Weapon")
            return true;

        return false;
    }

    private bool HandleInventorySlotDrop(SlotUI sourceSlot, SlotUI targetSlot)
    {
        if (inventory == null || sourceSlot == null || targetSlot == null || sourceSlot.IsLocked || targetSlot.IsLocked)
            return false;

        bool moved = RunSlotDataMutation(() => inventory.MoveMergeOrSwapItems(sourceSlot.SlotIndex, targetSlot.SlotIndex)); // 이동/병합/교환

        if (moved)
            RefreshSlotsAfterDataChange();

        return moved;
    }

    private bool HandleDropToWeaponSlot(SlotUI sourceSlot, SlotUI targetWeaponSlot)
    {
        if (sourceSlot == null || targetWeaponSlot == null || sourceSlot.DisplayItem == null)
            return false;

        if (sourceSlot.DisplayItem.itemType != "Weapon")
            return false;

        return EquipWeaponFromInventorySlot(sourceSlot, targetWeaponSlot.SlotIndex);
    }

    private bool HandleWeaponSlotDrop(SlotUI sourceSlot, SlotUI targetSlot)
    {
        if (!HasWeaponEquipment() || inventory == null || sourceSlot.DisplayItem == null || targetSlot.IsWeaponSlot || targetSlot.IsBagSlot || targetSlot.IsLocked)
            return false;

        ItemData targetItem = inventory.GetItemAt(targetSlot.SlotIndex); // 목표 칸

        if (targetItem == null)
        {
            SlotMoveResult result = RunSlotDataMutation(() => MoveWeaponSlotToInventorySlot(sourceSlot.SlotIndex, targetSlot.SlotIndex)); // 무기 해제
            if (!result.Succeeded)
                return false;

            RefreshSlotsAfterDataChange();
            return true;
        }

        return false;
    }

    private SlotMoveResult MoveWeaponSlotToInventorySlot(int weaponSlotIndex, int inventorySlotIndex)
    {
        if (!HasWeaponEquipment() || inventory == null)
            return SlotMoveResult.Fail("Inventory or equipment is missing.");

        ItemData currentWeapon = GetEquippedWeapon(weaponSlotIndex);
        if (currentWeapon == null)
            return SlotMoveResult.Fail("Weapon slot is empty.");

        if (inventorySlotIndex < 0 || inventorySlotIndex >= inventory.UnlockedSlotCount)
            return SlotMoveResult.Fail("Target inventory slot is invalid.");

        if (inventory.GetItemAt(inventorySlotIndex) != null)
            return SlotMoveResult.Fail("Target inventory slot is not empty.");

        allowEquippedWeaponInInventory = true; // 이동 중 예외

        if (!inventory.SetItemAt(inventorySlotIndex, currentWeapon))
        {
            allowEquippedWeaponInInventory = false;
            return SlotMoveResult.Fail("Failed to place weapon in inventory.");
        }

        if (!ClearEquippedWeapon(weaponSlotIndex))
        {
            inventory.ClearSlot(inventorySlotIndex); // 롤백
            allowEquippedWeaponInInventory = false;
            return SlotMoveResult.Fail("Failed to clear weapon slot.");
        }

        allowEquippedWeaponInInventory = false; // 예외 해제
        return SlotMoveResult.Success();
    }

    private bool EquipWeaponFromInventorySlot(SlotUI sourceSlot)
    {
        if (!HasWeaponEquipment())
            return false;

        int targetWeaponSlotIndex = GetDefaultTargetWeaponSlotIndex(); // 현재 리더
        return EquipWeaponFromInventorySlot(sourceSlot, targetWeaponSlotIndex);
    }

    private bool EquipWeaponFromInventorySlot(SlotUI sourceSlot, int weaponSlotIndex)
    {
        return RunSlotDataMutation(() => EquipWeaponFromInventorySlotCore(sourceSlot, weaponSlotIndex));
    }

    private bool EquipWeaponFromInventorySlotCore(SlotUI sourceSlot, int weaponSlotIndex)
    {
        if (inventory == null || !HasWeaponEquipment() || sourceSlot == null || sourceSlot.IsWeaponSlot || sourceSlot.IsBagSlot || sourceSlot.IsLocked)
            return false;

        if (!TryCreateInventorySourceSnapshot(sourceSlot, "Weapon", out InventorySourceSnapshot source))
            return false;

        ItemData item = source.Item; // 장착할 무기
        ItemData previousWeapon = GetEquippedWeapon(weaponSlotIndex); // 기존 무기
        if (!ClearInventorySource(source))
            return false;

        if (!EquipWeapon(weaponSlotIndex, item))
        {
            RestoreInventorySource(source); // 원본 복구
            RefreshSlotsAfterDataChange();
            return false;
        }

        ClearInventoryCopiesOfEquippedWeapon(item); // 중복 제거

        if (previousWeapon != null && !IsSameRuntimeItem(previousWeapon, item) && !inventory.ContainsItem(previousWeapon))
        {
            int returnIndex = inventory.GetItemAt(source.SlotIndex) == null ? source.SlotIndex : inventory.FindFirstEmptySlot(); // 반환 위치
            if (returnIndex < 0 || !inventory.SetItemAt(returnIndex, previousWeapon))
                return RollbackWeaponEquip(weaponSlotIndex, previousWeapon, source);
        }

        RefreshSlotsAfterDataChange();
        return true;
    }

    private bool ClearInventoryCopiesOfEquippedWeapon(ItemData equippedItem)
    {
        return inventory != null && inventory.ClearAllMatchingItems(equippedItem);
    }

    private bool RollbackWeaponEquip(int weaponSlotIndex, ItemData previousWeapon, InventorySourceSnapshot source)
    {
        if (previousWeapon != null)
            EquipWeapon(weaponSlotIndex, previousWeapon);
        else
            ClearEquippedWeapon(weaponSlotIndex);

        RestoreInventorySource(source);

        RefreshSlotsAfterDataChange();
        return false;
    }

    private bool UnequipWeaponToFirstAvailableSlot(int weaponSlotIndex)
    {
        return RunSlotDataMutation(() => UnequipWeaponToFirstAvailableSlotCore(weaponSlotIndex));
    }

    private bool UnequipWeaponToFirstAvailableSlotCore(int weaponSlotIndex)
    {
        if (inventory == null || !HasWeaponEquipment())
            return false;

        ItemData currentWeapon = GetEquippedWeapon(weaponSlotIndex); // 해제 무기

        if (currentWeapon == null)
            return false;

        if (inventory.ContainsItem(currentWeapon))
        {
            ClearEquippedWeapon(weaponSlotIndex);
            RefreshSlotsAfterDataChange();
            return true;
        }

        int emptySlot = inventory.FindFirstEmptySlot(); // 반환 칸
        if (emptySlot < 0)
            return false;

        SlotMoveResult result = MoveWeaponSlotToInventorySlot(weaponSlotIndex, emptySlot);
        if (!result.Succeeded)
            return false;

        RefreshSlotsAfterDataChange();
        return true;
    }

    private bool EquipBagFromInventorySlot(SlotUI sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.IsWeaponSlot || sourceSlot.IsBagSlot || sourceSlot.IsLocked)
            return false;

        for (int i = 0; i < equippedBags.Length; i++)
        {
            if (equippedBags[i] == null)
                return EquipBagIntoSlot(sourceSlot, i); // 빈 가방칸
        }

        return equippedBags.Length > 0 && EquipBagIntoSlot(sourceSlot, 0); // 1칸 정책: 기존 가방 교체
    }

    private bool HandleDropToBagSlot(SlotUI sourceSlot, SlotUI targetBagSlot)
    {
        if (targetBagSlot == null || !targetBagSlot.IsBagSlot || sourceSlot == null || sourceSlot.IsLocked)
            return false;

        if (sourceSlot.IsBagSlot)
            return SwapBagSlots(sourceSlot.SlotIndex, targetBagSlot.SlotIndex); // 가방 교환

        return EquipBagIntoSlot(sourceSlot, targetBagSlot.SlotIndex);
    }

    private bool EquipBagIntoSlot(SlotUI sourceSlot, int bagSlotIndex)
    {
        SlotMoveResult result = RunSlotDataMutation(() => MoveInventoryBagToBagSlot(sourceSlot, bagSlotIndex));
        if (!result.Succeeded)
            return false;

        RefreshSlotsAfterDataChange();
        return true;
    }

    private SlotMoveResult MoveInventoryBagToBagSlot(SlotUI sourceSlot, int bagSlotIndex)
    {
        if (inventory == null || !IsBagSlotIndexValid(bagSlotIndex))
            return SlotMoveResult.Fail("Inventory or bag slot is invalid.");

        if (!TryCreateInventorySourceSnapshot(sourceSlot, "Bag", out InventorySourceSnapshot source))
            return SlotMoveResult.Fail("Source bag is invalid.");

        ItemData newBag = source.Item; // 새 가방
        if (!IsBagItem(newBag))
            return SlotMoveResult.Fail("Source item is not a bag.");

        ItemData oldBag = equippedBags[bagSlotIndex]; // 기존 가방
        ItemData[] proposedBags = CopyEquippedBags(); // 가상 장착
        proposedBags[bagSlotIndex] = newBag;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags); // 예상 칸

        if (!CanApplyBagLoadout(proposedBags, source.SlotIndex))
            return SlotMoveResult.Fail("Bag loadout would lock occupied slots.");

        if (oldBag != null && source.SlotIndex >= proposedUnlockedSlots)
            return SlotMoveResult.Fail("Old bag cannot return to source slot.");

        if (!ClearInventorySource(source))
            return SlotMoveResult.Fail("Failed to clear source bag.");

        equippedBags[bagSlotIndex] = newBag;

        if (!inventory.SetUnlockedSlotCount(proposedUnlockedSlots))
        {
            equippedBags[bagSlotIndex] = oldBag; // 가방 롤백
            RestoreInventorySource(source); // 원본 복구
            return SlotMoveResult.Fail("Failed to apply bag capacity.");
        }

        if (oldBag != null && !TryPlaceInventoryItemAtOrRestore(source.SlotIndex, oldBag))
        {
            equippedBags[bagSlotIndex] = oldBag;
            inventory.SetUnlockedSlotCount(GetUnlockedInventorySlotCount());
            RestoreInventorySource(source);
            return SlotMoveResult.Fail("Failed to return replaced bag.");
        }

        return SlotMoveResult.Success();
    }

    private bool HandleDropFromBagSlot(SlotUI sourceBagSlot, SlotUI targetSlot)
    {
        if (sourceBagSlot == null || !sourceBagSlot.IsBagSlot || targetSlot == null || targetSlot.IsWeaponSlot || targetSlot.IsBagSlot || targetSlot.IsLocked)
            return false;

        if (inventory == null || inventory.GetItemAt(targetSlot.SlotIndex) != null)
            return false;

        return UnequipBagToInventorySlot(sourceBagSlot.SlotIndex, targetSlot.SlotIndex);
    }

    private bool UnequipBagToFirstAvailableSlot(int bagSlotIndex)
    {
        if (!IsBagSlotIndexValid(bagSlotIndex) || equippedBags[bagSlotIndex] == null || inventory == null)
            return false;

        ItemData[] proposedBags = CopyEquippedBags(); // 해제 가정
        proposedBags[bagSlotIndex] = null;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags);
        int targetSlotIndex = inventory.FindFirstEmptySlotWithin(proposedUnlockedSlots); // 반환 칸

        if (targetSlotIndex < 0)
            return false;

        return UnequipBagToInventorySlot(bagSlotIndex, targetSlotIndex);
    }

    private bool UnequipBagToInventorySlot(int bagSlotIndex, int targetSlotIndex)
    {
        SlotMoveResult result = RunSlotDataMutation(() => MoveBagSlotToInventorySlot(bagSlotIndex, targetSlotIndex));
        if (!result.Succeeded)
            return false;

        RefreshSlotsAfterDataChange();
        return true;
    }

    private SlotMoveResult MoveBagSlotToInventorySlot(int bagSlotIndex, int targetSlotIndex)
    {
        if (!IsBagSlotIndexValid(bagSlotIndex) || equippedBags[bagSlotIndex] == null || inventory == null)
            return SlotMoveResult.Fail("Bag slot is invalid or empty.");

        ItemData bag = equippedBags[bagSlotIndex]; // 해제 가방
        ItemData[] proposedBags = CopyEquippedBags(); // 해제 가정
        proposedBags[bagSlotIndex] = null;
        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(proposedBags); // 예상 칸

        if (targetSlotIndex < 0 || targetSlotIndex >= proposedUnlockedSlots || inventory.GetItemAt(targetSlotIndex) != null)
            return SlotMoveResult.Fail("Target inventory slot is invalid.");

        if (!CanApplyBagLoadout(proposedBags))
            return SlotMoveResult.Fail("Bag removal would lock occupied slots.");

        equippedBags[bagSlotIndex] = null;

        if (!inventory.SetUnlockedSlotCount(proposedUnlockedSlots))
        {
            equippedBags[bagSlotIndex] = bag; // 가방 롤백
            return SlotMoveResult.Fail("Failed to apply bag capacity.");
        }

        if (!inventory.SetItemAt(targetSlotIndex, bag))
        {
            equippedBags[bagSlotIndex] = bag;
            inventory.SetUnlockedSlotCount(GetUnlockedInventorySlotCount());
            return SlotMoveResult.Fail("Failed to place bag in inventory.");
        }

        return SlotMoveResult.Success();
    }

    private bool SwapBagSlots(int fromIndex, int toIndex)
    {
        if (!IsBagSlotIndexValid(fromIndex) || !IsBagSlotIndexValid(toIndex) || fromIndex == toIndex)
            return false;

        ItemData temp = equippedBags[fromIndex]; // swap 임시값
        equippedBags[fromIndex] = equippedBags[toIndex];
        equippedBags[toIndex] = temp;
        RefreshSlotsAfterDataChange();
        return true;
    }

    private bool TryCreateInventorySourceSnapshot(SlotUI sourceSlot, string requiredItemType, out InventorySourceSnapshot source)
    {
        source = new InventorySourceSnapshot(-1, null); // 기본 실패값

        if (inventory == null || sourceSlot == null || sourceSlot.IsWeaponSlot || sourceSlot.IsBagSlot || sourceSlot.IsLocked)
            return false;

        ItemData displayItem = sourceSlot.DisplayItem; // UI 표시값
        if (displayItem == null || !displayItem.HasValidBaseData)
            return false;

        if (!string.IsNullOrEmpty(requiredItemType) && displayItem.itemType != requiredItemType)
            return false;

        int sourceInventoryIndex = inventory.FindFirstMatchingItemIndex(displayItem); // 실제 데이터 위치
        if (sourceInventoryIndex < 0)
            return false;

        ItemData inventoryItem = inventory.GetItemAt(sourceInventoryIndex); // 실제 데이터
        if (!IsSameRuntimeItem(inventoryItem, displayItem))
            return false;

        source = new InventorySourceSnapshot(sourceInventoryIndex, inventoryItem); // 원본 백업
        return source.IsValid;
    }

    private bool ClearInventorySource(InventorySourceSnapshot source)
    {
        return source.IsValid && inventory != null && inventory.ClearFirstMatchingItem(source.Item);
    }

    private bool RestoreInventorySource(InventorySourceSnapshot source)
    {
        if (!source.IsValid || inventory == null)
            return false;

        if (inventory.ContainsItem(source.Item))
            return true;

        if (inventory.GetItemAt(source.SlotIndex) == null && inventory.SetItemAt(source.SlotIndex, source.Item))
            return true;

        int emptySlot = inventory.FindFirstEmptySlot(); // fallback 칸
        return emptySlot >= 0 && inventory.SetItemAt(emptySlot, source.Item);
    }

    private bool TryPlaceInventoryItemAtOrRestore(int preferredSlotIndex, ItemData item)
    {
        if (inventory == null || item == null)
            return false;

        if (inventory.ContainsItem(item))
            return true;

        if (inventory.GetItemAt(preferredSlotIndex) == null && inventory.SetItemAt(preferredSlotIndex, item))
            return true;

        int emptySlot = inventory.FindFirstEmptySlot();
        return emptySlot >= 0 && inventory.SetItemAt(emptySlot, item);
    }

    private void ApplyEquippedBagStatBonuses()
    {
        CalculateEquippedBagStatBonuses(out float moveSpeedPercent, out float maxStaminaBonus, out float maxHpBonus);
        ResolveBagStatTargets();
        ApplyBagMoveSpeedBonus(moveSpeedPercent);
        ApplyBagMaxStaminaBonus(maxStaminaBonus);
        ApplyBagMaxHpBonus(maxHpBonus);
    }

    private void CalculateEquippedBagStatBonuses(out float moveSpeedPercent, out float maxStaminaBonus, out float maxHpBonus)
    {
        moveSpeedPercent = 0f;
        maxStaminaBonus = 0f;
        maxHpBonus = 0f;

        if (equippedBags == null)
            return;

        for (int i = 0; i < equippedBags.Length; i++)
        {
            ItemData bag = equippedBags[i];
            if (!IsBagItem(bag))
                continue;

            bag.EnsureRuntimeState();
            if (bag.bagOptions == null)
                continue;

            for (int optionIndex = 0; optionIndex < bag.bagOptions.Count; optionIndex++)
            {
                BagRandomOptionRoll option = bag.bagOptions[optionIndex];
                if (option == null)
                    continue;

                switch (option.optionType)
                {
                    case BagRandomOptionType.MoveSpeedPercent:
                        moveSpeedPercent += Mathf.Max(0f, option.value);
                        break;
                    case BagRandomOptionType.MaxStamina:
                        maxStaminaBonus += Mathf.Max(0f, option.value);
                        break;
                    case BagRandomOptionType.MaxHp:
                        maxHpBonus += Mathf.Max(0f, option.value);
                        break;
                }
            }
        }
    }

    private void ResolveBagStatTargets()
    {
        if (playerMovement == null)
            playerMovement = FindFirstObjectByType<PlayerMovement>();

        if (playerStaminaController == null)
            playerStaminaController = ResolvePlayerComponent<PlayerStaminaController>();

        if (playerHealth == null)
            playerHealth = ResolvePlayerComponent<CombatHealth>();
    }

    private T ResolvePlayerComponent<T>() where T : Component
    {
        if (playerMovement != null)
        {
            T component = playerMovement.GetComponent<T>();
            if (component != null)
                return component;

            component = playerMovement.GetComponentInParent<T>();
            if (component != null)
                return component;

            component = playerMovement.GetComponentInChildren<T>();
            if (component != null)
                return component;
        }

        return FindFirstObjectByType<T>();
    }

    private void ApplyBagMoveSpeedBonus(float moveSpeedPercent)
    {
        if (appliedBagMovementTarget != null && appliedBagMovementTarget != playerMovement)
            appliedBagMovementTarget.SetBagMoveSpeedBonusPercent(0f);

        appliedBagMovementTarget = playerMovement;
        if (appliedBagMovementTarget != null)
            appliedBagMovementTarget.SetBagMoveSpeedBonusPercent(moveSpeedPercent);
    }

    private void ApplyBagMaxStaminaBonus(float maxStaminaBonus)
    {
        maxStaminaBonus = Mathf.Max(0f, maxStaminaBonus);

        if (appliedBagStaminaTarget != null && appliedBagStaminaTarget != playerStaminaController)
        {
            float previousBase = Mathf.Max(1f, appliedBagStaminaTarget.MaxStamina - appliedBagMaxStaminaBonus);
            appliedBagStaminaTarget.SetMaxStamina(previousBase, false);
            appliedBagMaxStaminaBonus = 0f;
        }

        appliedBagStaminaTarget = playerStaminaController;
        if (appliedBagStaminaTarget == null)
            return;

        float baseMaxStamina = Mathf.Max(1f, appliedBagStaminaTarget.MaxStamina - appliedBagMaxStaminaBonus);
        appliedBagStaminaTarget.SetMaxStamina(baseMaxStamina + maxStaminaBonus, false);
        appliedBagMaxStaminaBonus = maxStaminaBonus;
    }

    private void ApplyBagMaxHpBonus(float maxHpBonus)
    {
        maxHpBonus = Mathf.Max(0f, maxHpBonus);

        if (appliedBagHealthTarget != null && appliedBagHealthTarget != playerHealth)
        {
            float previousBase = Mathf.Max(1f, appliedBagHealthTarget.MaxHp - appliedBagMaxHpBonus);
            appliedBagHealthTarget.SetMaxHp(previousBase, false);
            appliedBagMaxHpBonus = 0f;
        }

        appliedBagHealthTarget = playerHealth;
        if (appliedBagHealthTarget == null)
            return;

        float baseMaxHp = Mathf.Max(1f, appliedBagHealthTarget.MaxHp - appliedBagMaxHpBonus);
        appliedBagHealthTarget.SetMaxHp(baseMaxHp + maxHpBonus, false);
        appliedBagMaxHpBonus = maxHpBonus;
    }

    private int GetUnlockedInventorySlotCount()
    {
        return CalculateUnlockedInventorySlotCount(equippedBags);
    }

    private int CalculateUnlockedInventorySlotCount(ItemData[] bags)
    {
        int total = Mathf.Max(0, baseInventorySlotCount); // 기본 칸

        if (bags != null)
        {
            for (int i = 0; i < bags.Length; i++)
            {
                if (bags[i] != null && bags[i].baseData is BagItemData bagData)
                    total += Mathf.Max(0, bagData.additionalSlots); // 가방 보너스
            }
        }

        int maxSlots = inventorySlots != null && inventorySlots.Length > 0 ? inventorySlots.Length : inventory != null ? inventory.Capacity : total; // UI 한계
        return Mathf.Clamp(total, 0, maxSlots);
    }

    private bool CanApplyBagLoadout(ItemData[] bags)
    {
        return CanApplyBagLoadout(bags, -1);
    }

    private bool CanApplyBagLoadout(ItemData[] bags, int ignoredInventorySlotIndex)
    {
        if (inventory == null)
            return false;

        int proposedUnlockedSlots = CalculateUnlockedInventorySlotCount(bags); // 예상 칸

        for (int i = proposedUnlockedSlots; i < inventory.Items.Count; i++)
        {
            if (i == ignoredInventorySlotIndex)
                continue;

            if (inventory.Items[i] != null)
                return false;
        }

        return true;
    }

    private ItemData[] CopyEquippedBags()
    {
        ItemData[] copy = new ItemData[EquippedBagSlotCount]; // 얕은 복사

        if (equippedBags == null)
            return copy;

        int copyCount = Mathf.Min(copy.Length, equippedBags.Length);
        for (int i = 0; i < copyCount; i++)
            copy[i] = equippedBags[i];

        return copy;
    }

    private bool IsBagItem(ItemData item)
    {
        return item != null && item.itemType == "Bag" && item.baseData is BagItemData;
    }

    private bool IsBagSlotIndexValid(int index)
    {
        return equippedBags != null && index >= 0 && index < equippedBags.Length;
    }

    private bool HasWeaponEquipment()
    {
        return playerEquipment != null;
    }

    private ItemData GetEquippedWeapon(int weaponSlotIndex)
    {
        return playerEquipment != null ? playerEquipment.GetWeaponSlotItem(weaponSlotIndex) : null;
    }

    private bool EquipWeapon(int weaponSlotIndex, ItemData item)
    {
        return playerEquipment != null && playerEquipment.EquipWeaponItemToSlot(item, weaponSlotIndex);
    }

    private bool ClearEquippedWeapon(int weaponSlotIndex)
    {
        return playerEquipment != null && playerEquipment.ClearWeaponSlot(weaponSlotIndex);
    }

    private bool IsActiveWeaponSlot(int weaponSlotIndex)
    {
        return playerEquipment != null && playerEquipment.IsActiveWeaponSlot(weaponSlotIndex);
    }

    private int GetDefaultTargetWeaponSlotIndex()
    {
        return playerEquipment != null ? playerEquipment.ActiveWeaponSlotIndex : 0;
    }

    private void RefreshCurrentWeaponStats()
    {
        if (playerEquipment != null)
            playerEquipment.RefreshCurrentWeaponStats();
    }

    private bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }

}
