using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStash : MonoBehaviour // 플레이어 창고
{
    private const int TotalTabCount = 3; // 탭 수

    [SerializeField] private int capacity = 63;
    [SerializeField] private List<ItemData> items = new List<ItemData>();
    [SerializeField] private List<ItemData> tab2Items = new List<ItemData>();
    [SerializeField] private List<ItemData> tab3Items = new List<ItemData>();
    [SerializeField] private int currentTabIndex;

    public event Action Changed; // 데이터 변경
    public event Action CurrentTabChanged; // 탭 변경

    public IReadOnlyList<ItemData> Items => CurrentItems; // 현재 탭
    public int Capacity => Mathf.Max(0, capacity); // 탭 용량
    public int TabCount => TotalTabCount; // 전체 탭 수
    public int CurrentTabIndex => currentTabIndex; // 내부 index
    public int CurrentTabNumber => currentTabIndex + 1; // 표시 번호

    private void Awake()
    {
        currentTabIndex = Mathf.Clamp(currentTabIndex, 0, TotalTabCount - 1); // 탭 보정
        EnsureAllTabCapacity(); // 용량 보장
        SanitizeInvalidItems(); // 깨진 아이템 제거
    }

    public IReadOnlyList<ItemData> GetItemsInTab(int tabIndex)
    {
        EnsureAllTabCapacity(); // 용량 보장
        return GetItemsForTab(tabIndex);
    }

    public ItemData GetItemAt(int tabIndex, int index)
    {
        EnsureAllTabCapacity(); // 용량 보장
        List<ItemData> targetItems = GetItemsForTab(tabIndex);
        if (targetItems == null || index < 0 || index >= targetItems.Count)
            return null;

        return targetItems[index];
    }

    public ItemData GetItemAt(int index)
    {
        List<ItemData> currentItems = CurrentItems; // 현재 탭
        if (index < 0 || index >= currentItems.Count)
            return null;

        return currentItems[index];
    }

    public bool SetItemAt(int index, ItemData item)
    {
        return SetItemAt(currentTabIndex, index, item, true);
    }

    public bool SetItemAt(int tabIndex, int index, ItemData item)
    {
        return SetItemAt(tabIndex, index, item, true);
    }

    public bool SetItemAt(int tabIndex, int index, ItemData item, bool notify)
    {
        if (index < 0 || index >= Capacity)
            return false;

        if (item != null && !item.HasValidBaseData)
            return false;

        EnsureAllTabCapacity(); // 용량 보장
        List<ItemData> targetItems = GetItemsForTab(tabIndex); // 대상 탭

        if (item != null)
        {
            item.EnsureRuntimeState(); // 런타임 보정
            ClearFirstMatchingItem(item, tabIndex, index, false); // 중복 제거
        }

        targetItems[index] = item; // 참조 배치
        if (notify)
            Changed?.Invoke();
        return true;
    }

    public bool ClearSlot(int index)
    {
        return ClearSlot(currentTabIndex, index, true);
    }

    public bool ClearSlot(int tabIndex, int index)
    {
        return ClearSlot(tabIndex, index, true);
    }

    public bool ClearSlot(int tabIndex, int index, bool notify)
    {
        List<ItemData> targetItems = GetItemsForTab(tabIndex); // 대상 탭
        if (targetItems == null || index < 0 || index >= targetItems.Count || targetItems[index] == null)
            return false;

        targetItems[index] = null; // 슬롯 비움
        if (notify)
            Changed?.Invoke();
        return true;
    }

    public int FindFirstEmptySlot()
    {
        return FindFirstEmptySlot(currentTabIndex);
    }

    public int FindFirstEmptySlot(int tabIndex)
    {
        EnsureAllTabCapacity(); // 용량 보장
        List<ItemData> currentItems = GetItemsForTab(tabIndex); // 대상 탭

        for (int i = 0; i < currentItems.Count; i++)
        {
            if (currentItems[i] == null)
                return i;
        }

        return -1;
    }

    public bool MoveMergeOrSwapItems(int fromIndex, int toIndex)
    {
        EnsureAllTabCapacity(); // 용량 보장
        List<ItemData> currentItems = CurrentItems; // 현재 탭

        if (fromIndex < 0 || fromIndex >= currentItems.Count || toIndex < 0 || toIndex >= currentItems.Count || fromIndex == toIndex)
            return false;

        ItemData fromItem = currentItems[fromIndex]; // 출발 아이템
        if (fromItem == null)
            return false;

        ItemData targetItem = currentItems[toIndex]; // 대상 아이템
        if (CanStackItems(fromItem, targetItem))
        {
            targetItem.stackCount += fromItem.stackCount; // 스택 병합
            currentItems[fromIndex] = null; // 출발 비움
            Changed?.Invoke();
            return true;
        }

        currentItems[fromIndex] = targetItem; // 교환
        currentItems[toIndex] = fromItem; // 교환
        Changed?.Invoke();
        return true;
    }

    public bool CanSplitStackAt(int index)
    {
        EnsureAllTabCapacity(); // 용량 보장
        ItemData item = GetItemAt(index);
        return IsStackableItem(item) && item.stackCount > 1 && FindFirstEmptySlot() >= 0;
    }

    public bool SplitStackAt(int index, int amount)
    {
        EnsureAllTabCapacity(); // 용량 보장
        List<ItemData> currentItems = CurrentItems;

        if (index < 0 || index >= currentItems.Count)
            return false;

        ItemData source = currentItems[index];
        if (!IsStackableItem(source) || source.stackCount <= 1)
            return false;

        int emptySlot = FindFirstEmptySlot();
        if (emptySlot < 0)
            return false;

        int splitCount = Mathf.Clamp(amount, 1, source.stackCount - 1);
        ItemData splitItem = new ItemData(source.baseData, source.level, source.grade, splitCount);
        splitItem.EnsureRuntimeState();
        splitItem.acquisitionOrder = source.acquisitionOrder;

        source.stackCount -= splitCount;
        currentItems[emptySlot] = splitItem;
        Changed?.Invoke();
        return true;
    }

    public bool ContainsItem(ItemData item)
    {
        return FindFirstMatchingItemIndex(item) >= 0;
    }

    public int FindFirstMatchingItemIndex(ItemData item)
    {
        if (item == null)
            return -1;

        List<ItemData> currentItems = CurrentItems; // 현재 탭
        for (int i = 0; i < currentItems.Count; i++)
        {
            if (IsSameRuntimeItem(currentItems[i], item))
                return i;
        }

        return -1;
    }

    public bool ClearFirstMatchingItem(ItemData item, int exceptIndex = -1)
    {
        return ClearFirstMatchingItem(item, currentTabIndex, exceptIndex, true);
    }

    public bool ClearFirstMatchingItem(ItemData item, int exceptTabIndex, int exceptIndex, bool notify)
    {
        if (item == null)
            return false;

        bool changed = false; // 변경 여부
        EnsureAllTabCapacity(); // 용량 보장

        for (int tabIndex = 0; tabIndex < TotalTabCount; tabIndex++)
        {
            List<ItemData> targetItems = GetItemsForTab(tabIndex); // 탭 목록
            for (int i = 0; i < targetItems.Count; i++)
            {
                if ((tabIndex == exceptTabIndex && i == exceptIndex) || !IsSameRuntimeItem(targetItems[i], item))
                    continue;

                targetItems[i] = null; // 중복 제거
                changed = true;
            }
        }

        if (changed && notify)
            Changed?.Invoke();

        return changed;
    }

    public bool CanStack(ItemData source, ItemData target)
    {
        return CanStackItems(source, target);
    }

    public bool SetCurrentTab(int tabIndex)
    {
        int clamped = Mathf.Clamp(tabIndex, 0, TotalTabCount - 1); // 탭 보정
        if (currentTabIndex == clamped)
            return true;

        currentTabIndex = clamped; // 탭 변경
        EnsureAllTabCapacity(); // 용량 보장
        CurrentTabChanged?.Invoke();
        Changed?.Invoke();
        return true;
    }

    public bool SortCurrentTab(ItemSortMode sortMode, ItemSortDirection sortDirection)
    {
        EnsureAllTabCapacity(); // 용량 보장
        ItemSortComparer.Sort(CurrentItems, 0, Capacity, sortMode, sortDirection); // 현재 탭 정렬
        Changed?.Invoke();
        return true;
    }

    public bool TryStoreInCurrentTab(ItemData item)
    {
        if (item == null || !item.HasValidBaseData)
            return false;

        item.EnsureAcquisitionOrder(); // 획득 순번

        int targetIndex = FindPreferredTarget(item); // 병합 우선
        if (targetIndex < 0)
            return false;

        ItemData targetItem = GetItemAt(targetIndex); // 대상 슬롯
        if (CanStackItems(item, targetItem))
        {
            targetItem.stackCount += item.stackCount; // 스택 병합
            Changed?.Invoke();
            return true;
        }

        return SetItemAt(targetIndex, item);
    }

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }

    private int FindPreferredTarget(ItemData item)
    {
        EnsureAllTabCapacity(); // 용량 보장
        List<ItemData> currentItems = CurrentItems; // 현재 탭

        for (int i = 0; i < currentItems.Count; i++)
        {
            if (CanStackItems(item, currentItems[i]))
                return i; // 스택 대상
        }

        return FindFirstEmptySlot();
    }

    private List<ItemData> CurrentItems
    {
        get
        {
            return GetItemsForTab(currentTabIndex);
        }
    }

    private List<ItemData> GetItemsForTab(int tabIndex)
    {
        switch (Mathf.Clamp(tabIndex, 0, TotalTabCount - 1))
        {
            case 1:
                return tab2Items;
            case 2:
                return tab3Items;
            default:
                return items;
        }
    }

    private void EnsureAllTabCapacity()
    {
        EnsureCapacity(items); // 1번 탭
        EnsureCapacity(tab2Items); // 2번 탭
        EnsureCapacity(tab3Items); // 3번 탭
    }

    private void EnsureCapacity(List<ItemData> targetItems)
    {
        capacity = Mathf.Max(0, capacity);
        if (targetItems == null)
            return;

        while (targetItems.Count < capacity)
            targetItems.Add(null); // 빈 슬롯

        if (targetItems.Count > capacity)
            targetItems.RemoveRange(capacity, targetItems.Count - capacity); // 초과 제거
    }

    private void SanitizeInvalidItems()
    {
        bool changed = SanitizeInvalidItems(items); // 1번 탭
        changed |= SanitizeInvalidItems(tab2Items); // 2번 탭
        changed |= SanitizeInvalidItems(tab3Items); // 3번 탭

        if (changed)
            Changed?.Invoke();
    }

    private bool SanitizeInvalidItems(List<ItemData> targetItems)
    {
        bool changed = false; // 변경 여부
        if (targetItems == null)
            return false;

        for (int i = 0; i < targetItems.Count; i++)
        {
            if (targetItems[i] == null || targetItems[i].HasValidBaseData)
                continue;

            targetItems[i] = null; // 깨진 참조 제거
            changed = true; // 변경 표시
        }

        return changed;
    }

    private bool CanStackItems(ItemData source, ItemData target)
    {
        if (source == null || target == null)
            return false;

        if (source.baseData == null || target.baseData == null || source.baseData != target.baseData)
            return false;

        if (source.grade != target.grade || source.level != target.level)
            return false;

        if (source.stackCount <= 0 || target.stackCount <= 0)
            return false;

        if (source.baseData is ConsumableItemData sourceConsumable && sourceConsumable.IsPermanentSingleItem)
            return false;

        string itemType = source.itemType; // 스택 정책
        return (itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem" || itemType == "Currency")
            && target.stackCount + source.stackCount <= GetMaxStack(target);
    }

    private bool IsStackableItem(ItemData item)
    {
        if (item == null)
            return false;

        if (item.baseData is ConsumableItemData consumableData && consumableData.IsPermanentSingleItem)
            return false;

        string itemType = item.itemType;
        return itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem" || itemType == "Currency";
    }

    public int GetMaxStack(ItemData item)
    {
        if (item != null && item.baseData is ConsumableItemData consumableData)
        {
            if (consumableData.IsPermanentSingleItem)
                return 1;

            return Mathf.Max(1, consumableData.maxStack);
        }

        if (item != null && item.baseData is CurrencyItemData currencyData)
            return Mathf.Max(1, currencyData.maxStack);

        string itemType = item != null ? item.itemType : string.Empty;
        return itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem" || itemType == "Currency" ? 99 : 1;
    }

    private bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }
}
