using UnityEngine;

public class StashSlotBridge : MonoBehaviour, ISlotInteractionBridge // 창고 슬롯 연결
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerStash stash;
    [SerializeField] private InventorySlotBridge inventorySlotBridge;
    [SerializeField] private StashUI stashUI;
    [SerializeField] private SlotUI[] stashSlots;

    private SlotUI previewOriginSlot; // 드래그 출발
    public int CurrentTabIndex => stash != null ? stash.CurrentTabIndex : 0; // 현재 탭
    public int OccupiedSlotCount { get; private set; } // 현재 표시 슬롯 갱신 때 함께 계산
    public int LastStoreAllFailedCount { get; private set; } // 전체보관 실패

    private void Awake()
    {
        ResolveReferences(); // 참조 수집
        InitSlots(); // 슬롯 연결
    }

    private void OnEnable()
    {
        ResolveReferences(); // 참조 재수집
        InitSlots(); // 슬롯 재연결

        if (stash != null)
            stash.Changed += HandleStashChanged; // 창고 변경

        if (inventory != null)
            inventory.Changed += HandleInventoryChanged; // 인벤토리 변경

        RefreshSlots(); // 표시 갱신
    }

    private void OnDisable()
    {
        if (stash != null)
            stash.Changed -= HandleStashChanged; // 이벤트 해제

        if (inventory != null)
            inventory.Changed -= HandleInventoryChanged; // 이벤트 해제
    }

    public void SetStashSlots(SlotUI[] slots)
    {
        stashSlots = slots; // 씬 슬롯
        InitSlots(); // Bridge 연결
        RefreshSlots(); // 표시 갱신
    }

    public void RefreshSlots()
    {
        if (stash == null || stashSlots == null)
            return;

        int occupied = 0;
        for (int i = 0; i < stashSlots.Length; i++)
        {
            if (stashSlots[i] == null)
                continue;

            ItemData item = stash.GetItemAt(i);
            stashSlots[i].SetDisplayItem(item); // 현재 탭 데이터
            if (stashSlots[i].DisplayItem != null) occupied++;
            stashSlots[i].SetLocked(false); // 창고 슬롯
        }
        OccupiedSlotCount = occupied;
    }

    public bool SwitchTab(int tabIndex)
    {
        ResolveReferences(); // 탭 전 참조

        if (stash == null || DragSlot.IsDragging)
            return false;

        bool switched = stash.SetCurrentTab(tabIndex); // 탭 변경
        RefreshSlots(); // 표시 갱신
        stashUI?.UpdateTabVisuals(); // 탭 강조
        return switched;
    }

    public bool SortCurrentTab(ItemSortMode sortMode, ItemSortDirection sortDirection)
    {
        ResolveReferences(); // 정렬 전 참조

        if (stash == null)
            return false;

        bool sorted = stash.SortCurrentTab(sortMode, sortDirection); // 현재 탭 정렬
        RefreshAfterMutation(); // UI 갱신
        return sorted;
    }

    public int StoreAllInventoryItemsToCurrentTab()
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
        {
            int moved = 0;
            bool committed = Overburst.Persistence.AccountGameplaySession.Run(() => { moved = StoreAllInventoryItemsToCurrentTab(); return moved > 0; });
            return committed ? moved : 0;
        }
        ResolveReferences(); // 이동 전 참조
        LastStoreAllFailedCount = 0; // 실패 초기화

        if (inventory == null || stash == null)
            return 0;

        int movedCount = 0; // 이동 수
        int unlockedCount = inventory.Items.Count; // 초과 보관품도 창고 이동 허용

        for (int i = 0; i < unlockedCount; i++)
        {
            ItemData item = inventory.GetItemAt(i);
            if (item == null)
                continue;

            int targetIndex = FindPreferredStashTarget(item); // 병합 우선
            if (targetIndex < 0 || !TryMoveInventoryToStash(i, targetIndex))
            {
                LastStoreAllFailedCount++; // 공간 부족
                continue;
            }

            movedCount++; // 성공 수
        }

        RefreshAfterMutation();
        return movedCount;
    }

    public bool HasSpaceForAnyInventoryItem()
    {
        ResolveReferences(); // 공간 체크 전

        if (inventory == null || stash == null)
            return false;

        int unlockedCount = inventory.UnlockedSlotCount;
        for (int i = 0; i < unlockedCount; i++)
        {
            ItemData item = inventory.GetItemAt(i);
            if (item == null)
                continue;

            if (FindPreferredStashTarget(item) >= 0)
                return true;
        }

        return false;
    }

    public static StashSlotBridge FindOpenBridge()
    {
        StashSlotBridge[] candidates = FindObjectsByType<StashSlotBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None); // 열린 창고 후보

        for (int i = 0; i < candidates.Length; i++)
        {
            StashSlotBridge candidate = candidates[i];
            if (candidate == null)
                continue;

            candidate.ResolveReferences(); // 상태 최신화
            if (candidate.stashUI == null || !candidate.stashUI.IsOpen)
                continue;

            if (candidate.stashUI.HasUsableCanvasRoot)
                return candidate;
        }

        return null;
    }

    public bool TryMoveInventorySlotToFirstAvailableStashSlot(int inventoryIndex)
    {
        ResolveReferences(); // 이동 전 참조

        if (inventory == null || stash == null)
            return false;

        ItemData item = inventory.GetItemAt(inventoryIndex);
        if (item == null)
            return false;

        int targetIndex = FindPreferredStashTarget(item); // 병합 우선
        if (targetIndex < 0)
        {
            Debug.LogWarning("[Stash] No available stash slot for inventory double-click move.", this);
            return false;
        }

        return TryMoveInventoryToStash(inventoryIndex, targetIndex);
    }

    public bool CanSplitStackFromContextMenu(SlotUI sourceSlot)
    {
        ResolveReferences(); // 메뉴 기준 최신화
        return stash != null && IsStashSlot(sourceSlot) && IsCurrentSlot(sourceSlot) && stash.CanSplitStackAt(sourceSlot.SlotIndex);
    }

    public bool SplitStackFromContextMenu(SlotUI sourceSlot, int amount)
    {
        ResolveReferences(); // 메뉴 기준 최신화
        if (stash == null || !IsStashSlot(sourceSlot) || !IsCurrentSlot(sourceSlot))
            return false;

        bool split = stash.SplitStackAt(sourceSlot.SlotIndex, amount);
        if (split)
            RefreshAfterMutation();
        else
            Debug.LogWarning("[Stash] Stack split failed. Check split amount and empty stash slot.", this);

        return split;
    }

    public void BeginDragPreview(SlotUI originSlot)
    {
        previewOriginSlot = originSlot; // 출발 슬롯
        ShowDragPreviewForTarget(null); // 미리보기 초기화
    }

    public void ShowDragPreviewForTarget(SlotUI targetSlot)
    {
        ClearSlotOverlays(); // 이전 표시 제거

        if (previewOriginSlot == null || targetSlot == null)
            return;

        if (CanAcceptTarget(previewOriginSlot, targetSlot))
            targetSlot.SetDragOverlay(SlotDragOverlayState.WillUnlock); // 허용 표시
    }

    public void ClearDragPreview()
    {
        previewOriginSlot = null; // 출발 해제
        ClearSlotOverlays(); // 표시 제거
    }

    public bool HandleSlotClick(SlotClickContext context)
    {
        if (context == null || context.Slot == null || context.Item == null || !IsStashSlot(context.Slot)
            || !IsCurrentSlot(context.Slot) || !ReferenceEquals(context.Item, context.Slot.DisplayItem))
            return false;

        int targetIndex = inventory != null ? inventory.FindFirstEmptySlot() : -1; // 빈 인벤토리
        if (targetIndex < 0)
            return false;

        return TryMoveStashToInventory(context.Slot.SlotIndex, targetIndex);
    }

    public bool HandleSlotDrop(SlotDropContext context)
    {
        if (context == null)
            return false;

        SlotUI sourceSlot = context.OriginSlot; // 출발
        SlotUI targetSlot = context.TargetSlot; // 도착

        if (sourceSlot == null || targetSlot == null || sourceSlot == targetSlot || sourceSlot.IsLocked || targetSlot.IsLocked)
            return false;

        if (sourceSlot.IsWeaponSlot || targetSlot.IsWeaponSlot || sourceSlot.IsBagSlot || targetSlot.IsBagSlot)
            return false;

        bool sourceIsStash = IsStashSlot(sourceSlot); // 출발 창고
        bool targetIsStash = IsStashSlot(targetSlot); // 도착 창고

        if ((!sourceIsStash && !targetIsStash) || !IsCurrentSlot(sourceSlot) || !IsCurrentSlot(targetSlot))
            return false;

        if (sourceIsStash && targetIsStash)
            return MoveWithinStash(sourceSlot.SlotIndex, targetSlot.SlotIndex);

        if (!sourceIsStash && targetIsStash)
            return TryMoveInventoryToStash(sourceSlot.SlotIndex, targetSlot.SlotIndex);

        return TryMoveStashToInventory(sourceSlot.SlotIndex, targetSlot.SlotIndex);
    }

    public bool HandleExternalDrop(SlotUI sourceSlot, Vector2 screenPosition)
    {
        return false;
    }

    private bool MoveWithinStash(int sourceIndex, int targetIndex)
    {
        if (stash == null)
            return false;

        bool moved = stash.MoveMergeOrSwapItems(sourceIndex, targetIndex); // 탭 내부 이동
        RefreshAfterMutation(); // UI 갱신
        return moved;
    }

    private bool TryMoveInventoryToStash(int inventoryIndex, int stashIndex)
    {
        if (inventory == null || stash == null) return false;
        bool moved = stash.TryMoveFromInventory(inventory, inventoryIndex, stashIndex,
            inventory.GetItemAt(inventoryIndex), stash.GetItemAt(stashIndex));
        RefreshAfterMutation();
        return moved;
    }

    private int FindPreferredStashTarget(ItemData item)
    {
        if (stash == null || item == null)
            return -1;

        for (int i = 0; i < stash.Capacity; i++)
        {
            ItemData targetItem = stash.GetItemAt(i);
            if (stash.CanStack(item, targetItem))
                return i; // 스택 대상
        }

        return stash.FindFirstEmptySlot(); // 빈 슬롯
    }

    private bool TryMoveStashToInventory(int stashIndex, int inventoryIndex)
    {
        if (inventory == null || stash == null) return false;
        bool moved = stash.TryMoveToInventory(inventory, stashIndex, inventoryIndex,
            stash.GetItemAt(stashIndex), inventory.GetItemAt(inventoryIndex));
        RefreshAfterMutation();
        return moved;
    }

    private bool CanAcceptTarget(SlotUI sourceSlot, SlotUI targetSlot)
    {
        if (sourceSlot == null || targetSlot == null || targetSlot.IsLocked)
            return false;

        if (sourceSlot.IsWeaponSlot || targetSlot.IsWeaponSlot || sourceSlot.IsBagSlot || targetSlot.IsBagSlot)
            return false;

        return IsStashSlot(sourceSlot) || IsStashSlot(targetSlot);
    }

    private bool IsCurrentSlot(SlotUI slot)
    {
        if (slot == null || slot.IsWeaponSlot || slot.IsBagSlot || slot.SlotIndex < 0)
            return false;

        ItemData current = IsStashSlot(slot)
            ? stash?.GetItemAt(slot.SlotIndex)
            : inventory?.GetItemAt(slot.SlotIndex);
        return ReferenceEquals(current, slot.DisplayItem);
    }

    private bool IsStashSlot(SlotUI slot)
    {
        return slot != null && ReferenceEquals(slot.OwnerBridge, this);
    }

    private void InitSlots()
    {
        if (stashSlots == null)
            return;

        for (int i = 0; i < stashSlots.Length; i++)
        {
            if (stashSlots[i] != null)
                stashSlots[i].Init(this, i, false, false); // 창고 슬롯
        }
    }

    private void ResolveReferences()
    {
        if (stashUI == null)
            stashUI = GetComponent<StashUI>() ?? GetComponentInParent<StashUI>(true); // 창고 UI

        if (stash == null)
            stash = FindFirstObjectByType<PlayerStash>(); // 영구 창고

        if (inventory == null)
            inventory = FindFirstObjectByType<PlayerInventory>(); // 영구 인벤토리

        if (inventorySlotBridge == null)
            inventorySlotBridge = FindFirstObjectByType<InventorySlotBridge>(FindObjectsInactive.Include); // 인벤 Bridge
    }

    private void RefreshAfterMutation()
    {
        RefreshSlots(); // 창고 갱신
        inventorySlotBridge?.RefreshSlotsWithOwnershipCheck(); // 인벤 갱신
        stashUI?.HideTooltip(); // Tooltip 정리
    }

    private void HandleStashChanged()
    {
        RefreshSlots(); // 창고 갱신
    }

    private void HandleInventoryChanged()
    {
        inventorySlotBridge?.RefreshSlotsWithOwnershipCheck(); // 인벤 갱신
    }

    private void ClearSlotOverlays()
    {
        if (stashSlots == null)
            return;

        for (int i = 0; i < stashSlots.Length; i++)
            stashSlots[i]?.ClearDragOverlay(); // 표시 제거
    }

}
