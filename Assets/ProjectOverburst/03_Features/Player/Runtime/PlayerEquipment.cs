using UnityEngine;

public class PlayerEquipment : MonoBehaviour // 장비/무기 장착
{
    private const int WeaponSlotCount = 1; // 무기 슬롯 수

    [Header("Default Weapon")]
    [SerializeField] private Transform defaultWeaponRoot;
    [SerializeField] private bool useDefaultWeaponFallback;

    [Header("Weapon Mount")]
    [SerializeField] private Transform weaponHolder;
    [SerializeField] private Transform weaponSocket;

    [Header("Inventory")]
    [SerializeField] private PlayerInventory inventory;

    [Header("Weapon State Controllers")]
    [SerializeField] private WeaponRuntimeHub weaponRuntimeHub;
    [SerializeField] private PlayerMovement playerMovementController;
    [SerializeField] private PlayerAnimation playerAnimatorController;

    [Header("Current Weapon")]
    [SerializeField] private Transform currentWeaponRoot;
    [SerializeField] private WeaponAimSource currentWeaponAimSource;
    [SerializeField] private WeaponPose currentWeaponPose;
    [SerializeField] private WeaponTraceBinding currentWeaponTraceBinding;
    private int activeWeaponSlotIndex
    {
        get => PlayerAccountInventoryService.Loadout.ActiveWeaponSlot;
        set => PlayerAccountInventoryService.Loadout.ActiveWeaponSlot = value;
    }
    private ItemData[] weaponSlotItems
    {
        get => PlayerAccountInventoryService.Loadout.Weapons;
        set => PlayerAccountInventoryService.Loadout.Weapons = value;
    }
    private ItemData[] gearSlotItems
    {
        get => PlayerAccountInventoryService.Loadout.Gear;
        set => PlayerAccountInventoryService.Loadout.Gear = value;
    }

    [Header("Test")]
    [SerializeField] private WeaponItemData testWeaponItemData;
    [SerializeField] private ItemGrade testWeaponGrade = ItemGrade.Common;

    public Transform DefaultWeaponRoot => defaultWeaponRoot;
    public Transform CurrentWeaponRoot => currentWeaponRoot;
    public WeaponAimSource CurrentWeaponAimSource => currentWeaponAimSource;
    public WeaponPose CurrentWeaponPose => currentWeaponPose;
    public WeaponTraceBinding CurrentWeaponTraceBinding => currentWeaponTraceBinding;
    public ItemData CurrentWeaponItem { get; private set; }
    public WeaponItemData CurrentWeaponData => CurrentWeaponItem != null ? CurrentWeaponItem.baseData as WeaponItemData : null;
    public WeaponFinalStats CurrentWeaponStats { get; private set; }
    public ResolvedWeaponContext CurrentWeaponContext { get; private set; }
    public ItemData EquippedElementGem => PlayerAccountInventoryService.Loadout.ElementalGem;
    public ElementGemModifiers GemModifiers => ElementGemQuality.Calculate(EquippedElementGem);
    public WeaponElement ActiveElement => HasCurrentWeapon && EquippedElementGem?.baseData is ElementGemItemData gem ? gem.element : WeaponElement.None;
    public int GemRevision { get; private set; }
    public int WeaponContextRevision { get; private set; }
    private string seenGemId;
    public event System.Action GemSlotsChanged;
    internal void SetElementGem(ItemData item)
    {
        if (item != null) item.EnsureRuntimeState();
        PlayerAccountInventoryService.Loadout.ElementalGem = item;
        Overburst.Persistence.AccountGameplaySession.SynchronizeEquipmentAfterCommit(this);
    }
    private bool SyncGemContext()
    {
        string id = EquippedElementGem?.runtimeInstanceId ?? string.Empty;
        if ((seenGemId ?? string.Empty) == id) return false;
        seenGemId = id;
        GemRevision++;
        ResetWeaponRuntimeStateForSwitch();
        GemSlotsChanged?.Invoke();
        RaiseGearSlotsChanged();
        return true;
    }
    public int ActiveWeaponSlotIndex => activeWeaponSlotIndex;
    public int WeaponSlotCountValue => WeaponSlotCount;
    public bool HasCurrentWeapon => CurrentWeaponItem != null && CurrentWeaponData != null && currentWeaponRoot != null;
    public WeaponAimMode CurrentWeaponAimMode => GetCurrentWeaponAimMode();
    public bool CanCurrentWeaponAim => CanCurrentWeaponAimAny;
    public bool CanCurrentWeaponUseAimInput => CanUseCurrentWeaponAimMode(CurrentWeaponAimMode);
    public bool CanCurrentWeaponUseAimCombatMove => CanCurrentWeaponUseAimInput && CurrentWeaponData.combatDefinition.aim.usesCombatMove;
    public bool CanCurrentWeaponUseAimPose => CanCurrentWeaponUseAimInput && CurrentWeaponData.combatDefinition.aim.usesUpperBodyPose;
    public bool CanCurrentWeaponRotateToAim => CanCurrentWeaponUseAimInput && CurrentWeaponData.combatDefinition.aim.rotatesToMouse;
    public float CurrentAimIncomingDamageMultiplier => GetCurrentAimIncomingDamageMultiplier();
    public float CurrentMeleeAimUpperBodyYawOffset => HasCurrentWeapon ? CurrentWeaponData.combatDefinition.aim.meleeUpperBodyYawOffset : 0f;
    public float CurrentMeleeGuardParryWindow => GetCurrentMeleeGuardParryWindow();
    public float CurrentMeleeGuardParryDamage => GetCurrentMeleeGuardParryDamage();
    public bool CanCurrentWeaponPrimaryAttack => HasCurrentWeapon
        && CurrentWeaponData.combatDefinition.usage.canPrimaryAttack
        && CurrentWeaponData.combatDefinition.usage.attackType != WeaponAttackType.None;
    public bool CanCurrentWeaponUseMeleeSlash => CanCurrentWeaponPrimaryAttack
        && CurrentWeaponData.CombatFamily == WeaponCombatFamily.Melee
        && CurrentWeaponData.combatDefinition.usage.attackType == WeaponAttackType.MeleeSlash;
    public bool HasCurrentMeleeDefinition => HasCurrentWeapon
        && CurrentWeaponData.CombatFamily == WeaponCombatFamily.Melee
        && CurrentWeaponData.GetMeleeDefinition() != null;
    public bool CanCurrentWeaponUseMagicCaster => CanCurrentWeaponPrimaryAttack
        && CurrentWeaponData.CombatFamily == WeaponCombatFamily.Magic
        && CurrentWeaponData.combatDefinition.usage.attackType == WeaponAttackType.Chain;
    public bool CanCurrentWeaponUseMagicAim => CanUseCurrentWeaponAimMode(WeaponAimMode.Magic);
    public bool CanCurrentWeaponUseMeleeGuard => CanUseCurrentWeaponAimMode(WeaponAimMode.MeleeGuard);
    public bool CanCurrentWeaponMoveWhileGuarding => !HasCurrentWeapon
        || CurrentWeaponData.GetMeleeGuardSettings().AllowsMovementWhileGuarding;
    public bool CanCurrentWeaponUseMeleeCombatStance => HasCurrentMeleeDefinition;
    public bool CanCurrentWeaponAimAny => CanCurrentWeaponUseMagicAim;
    public IWeaponRuntimeController CurrentWeaponRuntimeController => GetCurrentWeaponRuntimeController();
    public WeaponRuntimeStatus CurrentWeaponRuntimeStatus => GetCurrentWeaponRuntimeStatus();

    private Transform spawnedWeaponRoot; // 생성 무기
    private CombatHealth contextHealth;
    public event System.Action WeaponSlotsChanged; // 슬롯 변경
    public event System.Action GearSlotsChanged;

    private void OnEnable()
    {
        WeaponContextRevision++;
        contextHealth = GetComponent<CombatHealth>();
        if (contextHealth != null) { contextHealth.OnDead += InvalidateDeadContext; contextHealth.OnReset += InvalidateResetContext; }
    }
    private void OnDisable()
    {
        WeaponContextRevision++;
        if (contextHealth != null) { contextHealth.OnDead -= InvalidateDeadContext; contextHealth.OnReset -= InvalidateResetContext; }
    }
    private void InvalidateDeadContext(CombatHealth _, DamageInfo info) => WeaponContextRevision++;
    private void InvalidateResetContext(CombatHealth _) => WeaponContextRevision++;

    private void Awake()
    {
        var loadout = PlayerAccountInventoryService.Loadout;
        if (!loadout.EquipmentInitialized)
        {
            loadout.Weapons = new ItemData[WeaponSlotCount];
            loadout.Gear = new ItemData[6];
            loadout.ActiveWeaponSlot = 0;
            loadout.EquipmentInitialized = true;
        }
        EnsureWeaponSlots(); // 슬롯 보장
        EnsureGearSlots();
        ResolveInventory(); // 인벤토리
        ResolveWeaponStateControllers(); // 상태 컨트롤러
        RefreshCurrentWeaponReferences(); // 무기 참조
    }

    private void Start()
    {
        SynchronizeAccountLoadoutVisual();
        if (useDefaultWeaponFallback && currentWeaponRoot == null && defaultWeaponRoot != null)
            RegisterCurrentWeapon(defaultWeaponRoot); // 기본 무기
    }

    public void RegisterCurrentWeapon(Transform weaponRoot)
    {
        currentWeaponRoot = weaponRoot; // 무기 root

        if (currentWeaponRoot != null)
            currentWeaponRoot.gameObject.SetActive(true); // 무기 표시

        RefreshCurrentWeaponReferences(); // 참조 갱신
        currentWeaponPose?.SnapToCurrentPose();
    }

    public bool TryEquipFromInventorySlot(PlayerInventory source, int sourceIndex, ItemData expected, int weaponSlot)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() =>
                TryEquipFromInventorySlot(source, sourceIndex, expected, weaponSlot));
        if (!Overburst.Persistence.AccountGameplaySession.UsesAccountStorage(source)
            || source == null || expected == null || !ReferenceEquals(source.GetItemAt(sourceIndex), expected)
            || expected.itemType != "Weapon" || weaponSlot < 0 || weaponSlot >= WeaponSlotCount) return false;
        var previous = GetWeaponSlotItem(weaponSlot);
        if (sourceIndex < 0 || sourceIndex >= source.UnlockedSlotCount
            || !OwnsWeaponOnlyAt(source, expected, sourceIndex, -1)
            || (previous != null && (source.IsOverCapacity || !OwnsWeaponOnlyAt(source, previous, -1, weaponSlot)))) return false;
        int previousActive = activeWeaponSlotIndex;
        if (!source.TryReplaceOwnedItemAt(sourceIndex, expected, previous, false)) return false;
        bool succeeded = false;
        try { succeeded = EquipWeaponSlotCore(expected, weaponSlot) == WeaponSlotEquipResult.Applied; }
        finally
        {
            if (!succeeded)
            {
                weaponSlotItems[weaponSlot] = previous;
                activeWeaponSlotIndex = previousActive;
                if (!source.TryRestoreOwnedItemAt(sourceIndex, previous, expected))
                    throw new System.InvalidOperationException("Inventory ownership changed during weapon compensation.");
                if (Overburst.Persistence.AccountGameplaySession.Current?.IsEditing != true)
                {
                    if (previous == null) ClearCurrentWeaponVisual();
                    else RestoreWeaponTradeVisual();
                }
            }
        }
        if (!succeeded) return false;
        NotifyInventoryWeaponTrade(source);
        return true;
    }

    public bool TryUnequipToInventorySlot(PlayerInventory target, int targetIndex, int weaponSlot, ItemData expected)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() =>
                TryUnequipToInventorySlot(target, targetIndex, weaponSlot, expected));
        if (!Overburst.Persistence.AccountGameplaySession.UsesAccountStorage(target)
            || target == null || target.IsOverCapacity || expected == null || weaponSlot < 0 || weaponSlot >= WeaponSlotCount
            || !ReferenceEquals(GetWeaponSlotItem(weaponSlot), expected) || targetIndex < 0
            || targetIndex >= target.UnlockedSlotCount || target.GetItemAt(targetIndex) != null
            || !OwnsWeaponOnlyAt(target, expected, -1, weaponSlot)) return false;
        if (!target.TryReplaceOwnedItemAt(targetIndex, null, expected, false)) return false;
        bool succeeded = false;
        try { succeeded = ClearWeaponSlotCore(weaponSlot); }
        finally
        {
            if (!succeeded)
            {
                weaponSlotItems[weaponSlot] = expected;
                if (!target.TryRestoreOwnedItemAt(targetIndex, expected, null))
                    throw new System.InvalidOperationException("Inventory ownership changed during weapon compensation.");
                if (Overburst.Persistence.AccountGameplaySession.Current?.IsEditing != true)
                    RestoreWeaponTradeVisual();
            }
        }
        if (!succeeded) return false;
        NotifyInventoryWeaponTrade(target);
        return true;
    }

    internal ItemData PeekWeaponSlotItem(int slotIndex)
    {
        var slots = PlayerAccountInventoryService.Loadout.Weapons;
        return slots != null && slotIndex >= 0 && slotIndex < slots.Length ? slots[slotIndex] : null;
    }

    private bool OwnsWeaponOnlyAt(PlayerInventory storage, ItemData item, int inventoryIndex, int weaponSlot)
    {
        for (int i = 0; i < storage.Capacity; i++)
            if (i != inventoryIndex && SameWeaponIdentity(storage.GetItemAt(i), item)) return false;
        var loadout = PlayerAccountInventoryService.Loadout;
        var weapons = loadout.Weapons;
        if (weapons != null)
            for (int i = 0; i < weapons.Length; i++)
                if (i != weaponSlot && SameWeaponIdentity(weapons[i], item)) return false;
        if (loadout.Gear != null)
            foreach (var equipped in loadout.Gear)
                if (SameWeaponIdentity(equipped, item)) return false;
        if (loadout.Bags != null)
            foreach (var equipped in loadout.Bags)
                if (SameWeaponIdentity(equipped, item)) return false;
        return !SameWeaponIdentity(loadout.ElementalGem, item);
    }

    private static bool SameWeaponIdentity(ItemData left, ItemData right)
    {
        return left != null && right != null && (ReferenceEquals(left, right)
            || (!string.IsNullOrEmpty(left.runtimeInstanceId) && !string.IsNullOrEmpty(right.runtimeInstanceId)
                && string.Equals(left.runtimeInstanceId, right.runtimeInstanceId, System.StringComparison.Ordinal)));
    }

    private void NotifyInventoryWeaponTrade(PlayerInventory inventorySource)
    {
        try { inventorySource.NotifyAccountApplied(); } catch (System.Exception error) { Debug.LogException(error); }
        try { NotifyWeaponSlotsChanged(); } catch (System.Exception error) { Debug.LogException(error); }
    }

    public void ClearCurrentWeapon()
    {
        ClearWeaponSlot(activeWeaponSlotIndex);
    }

    public bool ClearWeaponSlot(int slotIndex)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => ClearWeaponSlot(slotIndex));
        if (!ClearWeaponSlotCore(slotIndex)) return false;
        NotifyWeaponSlotsChanged();
        return true;
    }

    private bool ClearWeaponSlotCore(int slotIndex)
    {
        EnsureWeaponSlots();

        if (!IsWeaponSlotIndexValid(slotIndex) || weaponSlotItems[slotIndex] == null)
            return false;

        weaponSlotItems[slotIndex] = null; // 슬롯 비움

        if (slotIndex == activeWeaponSlotIndex)
        {
            if (Overburst.Persistence.AccountGameplaySession.Current?.IsEditing == true)
                Overburst.Persistence.AccountGameplaySession.SynchronizeEquipmentAfterCommit(this);
            else
            {
                ResetWeaponRuntimeStateForSwitch();
                ClearCurrentWeaponVisual();
            }
        }

        return true;
    }

    private void ClearCurrentWeaponVisual()
    {
        CurrentWeaponContext = ResolvedWeaponContext.Empty;
        currentWeaponTraceBinding = null;
        CleanupSpawnedWeapon(); // 생성 무기 정리
        currentWeaponRoot = null; // 무기 root
        currentWeaponAimSource = null; // 조준 기준
        currentWeaponPose = null; // 포즈 제어
        CurrentWeaponItem = null; // 현재 무기
        CurrentWeaponStats = WeaponFinalStats.Empty; // 최종 스탯
    }

    public void RefreshCurrentWeaponReferences()
    {
        currentWeaponTraceBinding = null;
        currentWeaponAimSource = null; // 조준 기준 초기화
        currentWeaponPose = null; // 포즈 제어 초기화

        if (currentWeaponRoot == null)
            return;

        currentWeaponAimSource = currentWeaponRoot.GetComponent<WeaponAimSource>();

        if (currentWeaponAimSource == null)
            currentWeaponAimSource = currentWeaponRoot.GetComponentInChildren<WeaponAimSource>(true);

        currentWeaponPose = currentWeaponRoot.GetComponent<WeaponPose>();

        if (currentWeaponPose == null)
            currentWeaponPose = currentWeaponRoot.GetComponentInChildren<WeaponPose>(true);

        currentWeaponTraceBinding = currentWeaponRoot.GetComponent<WeaponTraceBinding>();

        if (currentWeaponTraceBinding == null)
            currentWeaponTraceBinding = currentWeaponRoot.GetComponentInChildren<WeaponTraceBinding>(true);
    }

    public void RefreshCurrentWeaponStats()
    {
        WeaponFinalStats stats = CurrentWeaponItem != null
            ? WeaponStatCalculator.Calculate(CurrentWeaponItem)
            : WeaponFinalStats.Empty;
        if (CurrentWeaponItem != null)
        {
            GearStatTotals gear = GearStatTotals.From(this);
            stats = CombatBalanceFormulas.ComposePlayerWeaponStats(stats, gear, PlayerProgression.CurrentLevel,
                MapRunBuffs.Bonus(MapBuffKind.AttackSpeed), Overburst.Persistence.SkillTreeBonuses.AttackPercent);
        }
        CurrentWeaponStats = stats;
        CurrentWeaponContext = new ResolvedWeaponContext(CurrentWeaponData, CurrentWeaponStats, ActiveElement);
    }

    public void SynchronizeAccountLoadoutVisual()
    {
        CompleteLoadoutVisualSynchronization(SynchronizeLoadoutVisualCore());
    }

    internal void RequireAccountLoadoutVisual()
    {
        var result = SynchronizeLoadoutVisualCore();
        if (result == LoadoutVisualResult.Failed)
            throw new System.InvalidOperationException("Committed weapon visual projection could not be applied.");
        CompleteLoadoutVisualSynchronization(result);
    }

    private void RestoreWeaponTradeVisual()
    {
        if (SynchronizeLoadoutVisualCore() == LoadoutVisualResult.Failed)
            throw new System.InvalidOperationException("Previous weapon visual could not be restored after a failed trade.");
    }

    private enum LoadoutVisualResult { Unchanged, GemChanged, Applied, Failed }

    private LoadoutVisualResult SynchronizeLoadoutVisualCore()
    {
        EnsureWeaponSlots();
        EnsureGearSlots();
        var item = weaponSlotItems[activeWeaponSlotIndex];
        bool gemChanged = SyncGemContext();
        if (CurrentWeaponItem == item && (item == null || (currentWeaponRoot != null && currentWeaponAimSource != null)))
        {
            RefreshCurrentWeaponStats();
            return gemChanged ? LoadoutVisualResult.GemChanged : LoadoutVisualResult.Unchanged;
        }
        ResetWeaponRuntimeStateForSwitch();
        bool applied = EquipCurrentWeaponVisual(item);
        if (item != null && !applied) return LoadoutVisualResult.Failed;
        RefreshCurrentWeaponStats();
        return LoadoutVisualResult.Applied;
    }

    private void CompleteLoadoutVisualSynchronization(LoadoutVisualResult result)
    {
        if (result == LoadoutVisualResult.Unchanged) return;
        if (result == LoadoutVisualResult.Failed) RefreshCurrentWeaponStats();
        NotifyWeaponSlotsChanged();
        if (result != LoadoutVisualResult.GemChanged)
            Overburst.Persistence.AccountGameplaySession.Notify(RaiseGearSlotsChanged);
    }

    public ItemData GetGearSlotItem(int slotIndex)
    {
        EnsureGearSlots();
        if (slotIndex < 0 || slotIndex >= gearSlotItems.Length) return null;
        ItemData item = gearSlotItems[slotIndex];
        item?.EnsureRuntimeState();
        return item;
    }

    public bool EquipGearItemToSlot(ItemData item, int slotIndex, out ItemData previous)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
        {
            ItemData displaced = null;
            bool result = Overburst.Persistence.AccountGameplaySession.Run(() => EquipGearItemToSlot(item, slotIndex, out displaced));
            previous = result ? displaced : null;
            return result;
        }
        previous = null;
        EnsureGearSlots();
        if (item == null || !(item.baseData is GearItemData data)
            || slotIndex < 0 || slotIndex >= gearSlotItems.Length
            || !GearItemData.Fits(data.kind, (GearSlot)slotIndex)) return false;
        item.EnsureRuntimeState();
        previous = gearSlotItems[slotIndex];
        for (int i = 0; i < gearSlotItems.Length; i++)
            if (i != slotIndex && IsSameRuntimeItem(gearSlotItems[i], item)) gearSlotItems[i] = null;
        gearSlotItems[slotIndex] = item;
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseGearSlotsChanged);
        return true;
    }

    public bool ClearGearSlot(int slotIndex, out ItemData removed)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
        {
            ItemData displaced = null;
            bool result = Overburst.Persistence.AccountGameplaySession.Run(() => ClearGearSlot(slotIndex, out displaced));
            removed = result ? displaced : null;
            return result;
        }
        removed = GetGearSlotItem(slotIndex);
        if (removed == null) return false;
        gearSlotItems[slotIndex] = null;
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseGearSlotsChanged);
        return true;
    }

    private void EnsureGearSlots()
    {
        if (gearSlotItems == null || gearSlotItems.Length != 6)
        {
            ItemData[] original = gearSlotItems;
            gearSlotItems = new ItemData[6];
            if (original != null)
                for (int i = 0; i < Mathf.Min(original.Length, gearSlotItems.Length); i++)
                    gearSlotItems[i] = original[i];
        }
        for (int i = 0; i < gearSlotItems.Length; i++)
            if (gearSlotItems[i] != null && !gearSlotItems[i].HasValidBaseData) gearSlotItems[i] = null;
    }

    public bool EquipWeaponItem(ItemData item)
    {
        return EquipWeaponItemToSlot(item, activeWeaponSlotIndex);
    }

    public bool EquipWeaponItemToSlot(ItemData item, int slotIndex)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => EquipWeaponItemToSlot(item, slotIndex));
        var result = EquipWeaponSlotCore(item, slotIndex);
        if (result == WeaponSlotEquipResult.Rejected) return false;
        if (result == WeaponSlotEquipResult.Applied) inventory?.RemoveItem(item);
        NotifyWeaponSlotsChanged();
        return result == WeaponSlotEquipResult.Applied;
    }

    private enum WeaponSlotEquipResult { Rejected, Applied, VisualFailed }

    private WeaponSlotEquipResult EquipWeaponSlotCore(ItemData item, int slotIndex)
    {
        if (item == null || item.itemType != "Weapon")
            return WeaponSlotEquipResult.Rejected; // 무기 전용

        EnsureWeaponSlots();

        if (!IsWeaponSlotIndexValid(slotIndex))
            return WeaponSlotEquipResult.Rejected;

        WeaponItemData weaponData = item.baseData as WeaponItemData;

        if (weaponData == null || WeaponLevelCatalog.ResolveVisual(weaponData) == null || !WeaponContentPolicy.IsActiveWeapon(weaponData))
            return WeaponSlotEquipResult.Rejected; // prefab 없음

        item.EnsureRuntimeState(); // 런타임 보정

        int previousActiveIndex = activeWeaponSlotIndex; // 롤백 index
        ItemData previousSlotItem = weaponSlotItems[slotIndex]; // 롤백 item
        ClearMatchingWeaponSlot(item, slotIndex); // 중복 제거
        weaponSlotItems[slotIndex] = item; // 슬롯 배치
        if (Overburst.Persistence.AccountGameplaySession.Current?.IsEditing == true)
        {
            activeWeaponSlotIndex = slotIndex;
            Overburst.Persistence.AccountGameplaySession.SynchronizeEquipmentAfterCommit(this);
            return WeaponSlotEquipResult.Applied;
        }
        ResetWeaponRuntimeStateForSwitch(); // 런타임 정리
        activeWeaponSlotIndex = slotIndex; // 활성 슬롯

        if (!EquipCurrentWeaponVisual(item))
        {
            weaponSlotItems[slotIndex] = previousSlotItem; // 롤백 item
            activeWeaponSlotIndex = previousActiveIndex; // 롤백 index
            EquipCurrentWeaponVisual(GetWeaponSlotItem(activeWeaponSlotIndex));
            return WeaponSlotEquipResult.VisualFailed;
        }

        return WeaponSlotEquipResult.Applied;
    }

    private bool EquipCurrentWeaponVisual(ItemData item)
    {
        if (item == null || item.itemType != "Weapon")
        {
            ClearCurrentWeaponVisual();
            return false;
        }

        WeaponItemData weaponData = item.baseData as WeaponItemData;

        if (weaponData == null || WeaponLevelCatalog.ResolveVisual(weaponData) == null)
            return false;

        Transform holder = GetWeaponHolder(weaponData);

        if (holder == null)
            return false; // 부모 없음

        GameObject weaponPrefab = WeaponLevelCatalog.ResolveVisual(weaponData); // 무기 prefab
        if (currentWeaponRoot != spawnedWeaponRoot)
            HideCurrentWeaponRoot(); // 기존 무기 숨김

        CleanupSpawnedWeapon(); // 이전 무기 정리

        GameObject weaponInstance = Instantiate(weaponPrefab, holder, false);
        weaponInstance.name = weaponData.itemName + "_Equipped"; // 씬 식별
        spawnedWeaponRoot = weaponInstance.transform; // 생성 root
        CurrentWeaponItem = item; // 현재 무기
        RefreshCurrentWeaponStats(); // 스탯 갱신

        HideDefaultWeaponSourceIfNeeded(); // 기본 무기 숨김
        RegisterCurrentWeapon(spawnedWeaponRoot); // 새 무기 등록
        return currentWeaponAimSource != null; // 조준 기준 확인
    }

    public ItemData GetWeaponSlotItem(int slotIndex)
    {
        EnsureWeaponSlots();
        if (!IsWeaponSlotIndexValid(slotIndex))
            return null;

        ItemData item = weaponSlotItems[slotIndex];
        item?.EnsureRuntimeState(); // 런타임 보정
        return item;
    }

    public bool IsActiveWeaponSlot(int slotIndex)
    {
        return slotIndex == activeWeaponSlotIndex;
    }

    public bool EquipFirstWeaponFromInventory()
    {
        ResolveInventory();

        if (inventory == null)
            return false;

        ItemData item = inventory.GetFirstWeaponItem();
        return EquipWeaponItem(item);
    }

    [ContextMenu("Test/Add Weapon Item To Inventory")]
    private void TestAddWeaponItemToInventory()
    {
        ResolveInventory();

        if (inventory == null)
            return;

        WeaponItemData weaponData = testWeaponItemData != null ? testWeaponItemData : CreateRuntimeWeaponDataFromCurrentWeapon(); // 테스트 무기

        if (weaponData == null)
            return;

        inventory.AddItem(new ItemData(weaponData, 1, testWeaponGrade));
    }

    [ContextMenu("Test/Equip First Weapon From Inventory")]
    private void TestEquipFirstWeaponFromInventory()
    {
        EquipFirstWeaponFromInventory();
    }

    private Transform GetWeaponHolder(WeaponItemData weaponData = null)
    {
        Transform characterSocket = GetCharacterWeaponSocket(weaponData);
        if (characterSocket != null)
            return characterSocket;

        if (weaponSocket != null)
            return weaponSocket;

        if (weaponHolder != null)
            return weaponHolder;

        if (defaultWeaponRoot != null && defaultWeaponRoot.parent != null)
            return defaultWeaponRoot.parent;

        Debug.LogWarning("[PlayerEquipment] Weapon holder is missing. Configure WeaponSocket or WeaponHolder on the PlayerActor before equipping a weapon.", this);
        return null;
    }

    private Transform GetCharacterWeaponSocket(WeaponItemData weaponData)
    {
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            ICharacterWeaponSocketProvider provider = behaviour as ICharacterWeaponSocketProvider;
            if (provider == null)
                continue;

            Transform socket = provider.GetWeaponSocket(weaponData);
            if (socket != null)
                return socket;
        }

        return null;
    }

    private Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child.name == childName)
                return child;
        }

        return null;
    }

    private void HideCurrentWeaponRoot()
    {
        if (currentWeaponRoot == null)
            return;

        currentWeaponRoot.gameObject.SetActive(false);
    }

    private void HideDefaultWeaponSourceIfNeeded()
    {
        if (defaultWeaponRoot == null)
            return;

        defaultWeaponRoot.gameObject.SetActive(false);
    }

    private void CleanupSpawnedWeapon()
    {
        if (spawnedWeaponRoot == null)
            return; // 생성 무기 없음

        if (Application.isPlaying)
        {
            WeaponPose poseController = spawnedWeaponRoot.GetComponent<WeaponPose>();

            if (poseController == null)
                poseController = spawnedWeaponRoot.GetComponentInChildren<WeaponPose>(true); // 하위 검색

            if (poseController != null)
                poseController.FadeOutAndDestroy(spawnedWeaponRoot.gameObject); // fade 제거
            else
                Destroy(spawnedWeaponRoot.gameObject); // 즉시 제거
        }
        else
        {
            DestroyImmediate(spawnedWeaponRoot.gameObject);
        }

        spawnedWeaponRoot = null; // 생성 root 해제
    }

    private void ResolveInventory()
    {
        if (inventory != null)
            return;

        inventory = PlayerAccountInventoryService.FindSharedInventory();

        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();
    }

    private void ResolveWeaponStateControllers()
    {
        EnsureRuntimeControllerHub(); // 런타임 허브

        if (playerMovementController == null)
            playerMovementController = GetComponent<PlayerMovement>();

        if (playerAnimatorController == null)
            playerAnimatorController = GetComponent<PlayerAnimation>();
    }

    private void ResetWeaponRuntimeStateForSwitch()
    {
        WeaponContextRevision++;
        ResolveWeaponStateControllers(); // 참조 보장
        weaponRuntimeHub?.CancelAllActions(); // 무기 액션
        playerMovementController?.CancelWeaponActionLocks(); // 이동 잠금
        playerMovementController?.CancelWeaponAimStateForSwitch(); // 조준 상태
        playerAnimatorController?.CancelWeaponRuntimeState(); // 애니 상태
    }

    private IWeaponRuntimeController GetCurrentWeaponRuntimeController()
    {
        EnsureRuntimeControllerHub();
        return HasCurrentWeapon && weaponRuntimeHub != null
            ? weaponRuntimeHub.GetRuntime(CurrentWeaponContext)
            : null;
    }

    private WeaponRuntimeStatus GetCurrentWeaponRuntimeStatus()
    {
        EnsureRuntimeControllerHub();
        return weaponRuntimeHub != null && HasCurrentWeapon
            ? weaponRuntimeHub.GetRuntimeStatus(CurrentWeaponContext)
            : WeaponRuntimeStatus.Empty;
    }

    private void EnsureRuntimeControllerHub()
    {
        if (weaponRuntimeHub == null)
            weaponRuntimeHub = GetComponent<WeaponRuntimeHub>(); // 허브 검색

        if (weaponRuntimeHub == null)
            weaponRuntimeHub = gameObject.AddComponent<WeaponRuntimeHub>(); // 허브 보장

        weaponRuntimeHub.ResolveControllers(); // 컨트롤러 연결
    }

    private void EnsureWeaponSlots()
    {
        activeWeaponSlotIndex = 0;
        if (weaponSlotItems == null || weaponSlotItems.Length != WeaponSlotCount)
        {
            ItemData[] oldSlots = weaponSlotItems;
            weaponSlotItems = new ItemData[WeaponSlotCount];
            if (oldSlots != null)
            {
                int copyCount = Mathf.Min(oldSlots.Length, weaponSlotItems.Length);
                for (int i = 0; i < copyCount; i++)
                    weaponSlotItems[i] = oldSlots[i];
            }
        }

        // Unity may deserialize an empty inline ItemData slot as an object without base data.
        // Treat that placeholder as empty so the first equip never tries to return it to inventory.
        for (int i = 0; i < weaponSlotItems.Length; i++)
        {
            if (weaponSlotItems[i] != null && !weaponSlotItems[i].HasValidBaseData)
                weaponSlotItems[i] = null;
        }
    }

    private bool IsWeaponSlotIndexValid(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < WeaponSlotCount;
    }

    private void ClearMatchingWeaponSlot(ItemData item, int exceptSlotIndex)
    {
        if (item == null)
            return;

        for (int i = 0; i < weaponSlotItems.Length; i++)
        {
            if (i == exceptSlotIndex)
                continue;

            if (IsSameRuntimeItem(weaponSlotItems[i], item))
                weaponSlotItems[i] = null; // 중복 제거
        }
    }

    private void NotifyWeaponSlotsChanged()
    {
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseWeaponSlotsChanged); // UI 갱신
    }

    private WeaponAimMode GetCurrentWeaponAimMode()
    {
        WeaponItemData weaponData = CurrentWeaponData;
        return weaponData != null ? weaponData.GetResolvedAimMode() : WeaponAimMode.None;
    }

    private bool CanUseCurrentWeaponAimMode(WeaponAimMode mode)
    {
        if (!HasCurrentWeapon || mode == WeaponAimMode.None || mode == WeaponAimMode.Auto)
            return false;

        if (CurrentWeaponAimMode != mode)
            return false;

        switch (mode)
        {
            case WeaponAimMode.Magic:
                return CanCurrentWeaponUseMagicCaster
                    && CurrentWeaponData.combatDefinition.usage.canAim
                    && CurrentWeaponData.combatDefinition.usage.aimType == WeaponAimType.CasterLine;
            case WeaponAimMode.MeleeStance:
            case WeaponAimMode.MeleeGuard:
                return HasCurrentMeleeDefinition;
            default:
                return false;
        }
    }

    private float GetCurrentAimIncomingDamageMultiplier()
    {
        if (!CanCurrentWeaponUseAimInput || CurrentWeaponAimMode != WeaponAimMode.MeleeGuard)
            return 1f;

        MeleeGuardSettings guard = CurrentWeaponData.GetMeleeGuardSettings();
        float multiplier = guard.incomingDamageMultiplier;
        if (multiplier <= 0f)
            multiplier = 0.3f; // 이전 Sword 막기 기본값

        return Mathf.Clamp01(multiplier);
    }

    private float GetCurrentMeleeGuardParryWindow()
    {
        if (!CanCurrentWeaponUseMeleeGuard)
            return 0f;

        return CurrentWeaponData.GetMeleeGuardSettings().SafeParryWindow;
    }

    private float GetCurrentMeleeGuardParryDamage()
    {
        if (!CanCurrentWeaponUseMeleeGuard)
            return 1f;

        return CurrentWeaponData.GetMeleeGuardSettings().SafeParryDamage;
    }

    private bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }

    private WeaponItemData CreateRuntimeWeaponDataFromCurrentWeapon()
    {
        Transform sourceRoot = currentWeaponRoot != null ? currentWeaponRoot : defaultWeaponRoot; // 테스트 원본

        if (sourceRoot == null)
            return null;

        if (testWeaponItemData != null && WeaponContentPolicy.IsActiveWeapon(testWeaponItemData))
            return testWeaponItemData.CreateRuntimeCopy(sourceRoot.gameObject);

        return null;
    }

    private void RaiseWeaponSlotsChanged() => WeaponSlotsChanged?.Invoke();
    private void RaiseGearSlotsChanged() => GearSlotsChanged?.Invoke();
}
