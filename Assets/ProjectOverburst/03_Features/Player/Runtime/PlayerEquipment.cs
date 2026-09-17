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
    [SerializeField] private int activeWeaponSlotIndex;
    [SerializeField] private ItemData[] weaponSlotItems = new ItemData[WeaponSlotCount];

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
    public bool CanCurrentWeaponUseMeleeCombatStance => CanUseCurrentWeaponAimMode(WeaponAimMode.MeleeStance) || CanUseCurrentWeaponAimMode(WeaponAimMode.MeleeGuard);
    public bool CanCurrentWeaponAimAny => CanCurrentWeaponUseMagicAim;
    public IWeaponRuntimeController CurrentWeaponRuntimeController => GetCurrentWeaponRuntimeController();
    public WeaponRuntimeStatus CurrentWeaponRuntimeStatus => GetCurrentWeaponRuntimeStatus();

    private Transform spawnedWeaponRoot; // 생성 무기
    public event System.Action WeaponSlotsChanged; // 슬롯 변경

    private void Awake()
    {
        EnsureWeaponSlots(); // 슬롯 보장
        ResolveInventory(); // 인벤토리
        ResolveWeaponStateControllers(); // 상태 컨트롤러
        RefreshCurrentWeaponReferences(); // 무기 참조
    }

    private void Start()
    {
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

    public void ClearCurrentWeapon()
    {
        ClearWeaponSlot(activeWeaponSlotIndex);
    }

    public bool ClearWeaponSlot(int slotIndex)
    {
        EnsureWeaponSlots();

        if (!IsWeaponSlotIndexValid(slotIndex) || weaponSlotItems[slotIndex] == null)
            return false;

        weaponSlotItems[slotIndex] = null; // 슬롯 비움

        if (slotIndex == activeWeaponSlotIndex)
        {
            ResetWeaponRuntimeStateForSwitch(); // 런타임 정리
            ClearCurrentWeaponVisual(); // 표시 정리
        }

        NotifyWeaponSlotsChanged();
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
        CurrentWeaponStats = CurrentWeaponItem != null // 최종 스탯
            ? WeaponStatCalculator.Calculate(CurrentWeaponItem)
            : WeaponFinalStats.Empty;
        CurrentWeaponContext = new ResolvedWeaponContext(CurrentWeaponData, CurrentWeaponStats);
    }

    public bool EquipWeaponItem(ItemData item)
    {
        return EquipWeaponItemToSlot(item, activeWeaponSlotIndex);
    }

    public bool EquipWeaponItemToSlot(ItemData item, int slotIndex)
    {
        if (item == null || item.itemType != "Weapon")
            return false; // 무기 전용

        EnsureWeaponSlots();

        if (!IsWeaponSlotIndexValid(slotIndex))
            return false;

        WeaponItemData weaponData = item.baseData as WeaponItemData;

        if (weaponData == null || weaponData.weaponRootPrefab == null || !WeaponContentPolicy.IsActiveWeapon(weaponData))
            return false; // prefab 없음

        item.EnsureRuntimeState(); // 런타임 보정

        int previousActiveIndex = activeWeaponSlotIndex; // 롤백 index
        ItemData previousSlotItem = weaponSlotItems[slotIndex]; // 롤백 item
        ClearMatchingWeaponSlot(item, slotIndex); // 중복 제거
        weaponSlotItems[slotIndex] = item; // 슬롯 배치
        ResetWeaponRuntimeStateForSwitch(); // 런타임 정리
        activeWeaponSlotIndex = slotIndex; // 활성 슬롯

        if (!EquipCurrentWeaponVisual(item))
        {
            weaponSlotItems[slotIndex] = previousSlotItem; // 롤백 item
            activeWeaponSlotIndex = previousActiveIndex; // 롤백 index
            EquipCurrentWeaponVisual(GetWeaponSlotItem(activeWeaponSlotIndex));
            NotifyWeaponSlotsChanged();
            return false;
        }

        inventory?.RemoveItem(item); // 인벤 제거
        NotifyWeaponSlotsChanged();
        return true;
    }

    private bool EquipCurrentWeaponVisual(ItemData item)
    {
        if (item == null || item.itemType != "Weapon")
        {
            ClearCurrentWeaponVisual();
            return false;
        }

        WeaponItemData weaponData = item.baseData as WeaponItemData;

        if (weaponData == null || weaponData.weaponRootPrefab == null)
            return false;

        Transform holder = GetWeaponHolder(weaponData);

        if (holder == null)
            return false; // 부모 없음

        GameObject weaponPrefab = weaponData.weaponRootPrefab; // 무기 prefab
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
        WeaponSlotsChanged?.Invoke(); // UI 갱신
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
}
