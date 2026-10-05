using System;
using UnityEngine;

public partial class InventorySlotBridge : MonoBehaviour, ISlotInteractionBridge, ISlotSingleClickInteractionBridge, ISlotRightClickInteractionBridge // 인벤토리 슬롯 정책
{

    [Header("References")]
    [SerializeField] private PlayerInventory inventory; // 일반 슬롯 데이터
    [SerializeField] private PlayerEquipment playerEquipment; // 장착 슬롯 데이터
    [SerializeField] private InventoryUI inventoryUI; // UI 상태
    [SerializeField] private PickupGradeVfxSet pickupGradeVfxSet; // 월드 드롭 VFX
    [SerializeField] private PlayerMovement playerMovement; // 가방 이동속도 적용 대상
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

    private ItemData[] equippedBags
    {
        get => PlayerAccountInventoryService.Loadout.Bags;
    }
    private SlotUI previewOriginSlot; // preview 원본
    private bool suppressSlotEventRefresh; // 이벤트 억제
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
                inventorySlots[i].SetLocked(i >= unlockedSlotCount && item == null);
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
                    bagSlots[i].SetDisplayItem(equippedBags != null && i < equippedBags.Length ? equippedBags[i] : null);
                    bagSlots[i].SetNewItemMarker(false); // 가방 슬롯 제외
                }
            }
        }
    }

    public void RefreshSlotsWithOwnershipCheck()
    {
        // Compatibility entry point: ownership and capacity belong to committed data.
        RefreshSlots();
    }

    public bool CanSortInventory(out string reason)
    {
        if (inventory == null)
        {
            reason = "인벤토리가 준비되지 않았습니다.";
            return false;
        }

        if (ShopUI.HasPendingPlayerOffers(inventory))
        {
            reason = ShopUI.PendingPlayerOffersSortMessage;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool SortInventory(ItemSortMode sortMode, ItemSortDirection sortDirection)
    {
        if (!CanSortInventory(out _))
            return false;

        bool sorted = RunSlotDataMutation(() => inventory.SortUnlockedSlots(sortMode, sortDirection));
        RefreshSlotsAfterDataChange();
        return sorted;
    }

    private void HandleInventoryChanged()
    {
        if (suppressSlotEventRefresh)
            return;

        RefreshSlotsWithOwnershipCheck();
    }

    private void HandleWeaponSlotsChanged()
    {
        if (suppressSlotEventRefresh)
            return;

        RefreshSlotsWithOwnershipCheck();
    }

    private void RefreshSlotsAfterDataChange()
    {
        RefreshSlotsWithOwnershipCheck();
    }







    private T RunSlotDataMutation<T>(Func<T> operation)
    {
        bool previousSuppressState = suppressSlotEventRefresh;
        suppressSlotEventRefresh = true; // 중복 refresh 방지

        try
        {
            if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            {
                T result = default;
                bool committed = Overburst.Persistence.AccountGameplaySession.Run(() =>
                {
                    result = operation();
                    if (result is bool flag) return flag;
                    if (result is SlotMoveResult move) return move.Succeeded;
                    if (result is InventoryActionResult action) return action.Succeeded;
                    throw new InvalidOperationException("Unsupported inventory operation result.");
                });
                if (committed) return result;
                if (typeof(T) == typeof(bool)) return (T)(object)false;
                if (typeof(T) == typeof(SlotMoveResult)) return (T)(object)SlotMoveResult.Fail("아이템 변경을 완료하지 못했습니다.");
                if (typeof(T) == typeof(InventoryActionResult)) return (T)(object)InventoryActionResult.Fail(InventoryActionFailureReason.InventoryRemoveFailed, "아이템 변경을 완료하지 못했습니다.");
                return result;
            }
            return operation();
        }
        finally
        {
            suppressSlotEventRefresh = previousSuppressState;
        }
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
            Debug.LogError("Account bag slots are not ready. Bind a prepared account before inventory interaction.", this);

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
        if (sourceInventoryIndex != sourceSlot.SlotIndex || !ReferenceEquals(inventoryItem, displayItem))
            return false;

        source = new InventorySourceSnapshot(sourceInventoryIndex, inventoryItem); // 원본 백업
        return source.IsValid;
    }





    private bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }

}
