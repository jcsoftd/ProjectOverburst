using UnityEngine;

public sealed class WeaponComboGemPopupPresenter : MonoBehaviour
{
    [SerializeField] private WeaponComboGemPopupView view;

    private IWeaponComboGemPopupReadService readService;
    private ItemData currentWeapon;
    private string currentWeaponRuntimeInstanceId;
    private InventorySlotBridge ownerInventoryBridge;
    private PlayerInventory ownerInventory;
    private PlayerEquipment subscribedEquipment;

    private void Awake()
    {
        readService = new ItemDataWeaponComboGemPopupReadService(); // 공개 조회 API 어댑터
        if (view == null)
            view = GetComponent<WeaponComboGemPopupView>();

    }

    private void OnEnable()
    {
        if (view != null)
        {
            view.CloseRequested += Close;
            view.SlotDropped += HandleSlotDropped;
            view.InstalledDragRequested += HandleInstalledDragRequested;
        }

        WeaponComboGemEquipService.Changed += HandleComboGemChanged;
    }

    private void OnDisable()
    {
        if (view != null)
        {
            view.CloseRequested -= Close;
            view.SlotDropped -= HandleSlotDropped;
            view.InstalledDragRequested -= HandleInstalledDragRequested;
        }

        SetOwnerInventoryBridge(null);
        BindPlayerEquipment(null);
        WeaponComboGemEquipService.Changed -= HandleComboGemChanged;
        ClearTransientInteractionState();
        currentWeapon = null;
        currentWeaponRuntimeInstanceId = string.Empty;
        ownerInventory = null;
        view?.Hide();
    }

    public static bool TryOpen(ItemData weapon)
    {
        return TryOpen(weapon, null);
    }

    public static bool TryOpen(ItemData weapon, InventorySlotBridge inventoryBridge)
    {
        WeaponComboGemPopupPresenter presenter = Object.FindFirstObjectByType<WeaponComboGemPopupPresenter>(FindObjectsInactive.Include);
        if (presenter == null)
            return false;

        presenter.Open(weapon, inventoryBridge);
        return true;
    }

    public static void CloseOpenPopup()
    {
        WeaponComboGemPopupPresenter presenter = Object.FindFirstObjectByType<WeaponComboGemPopupPresenter>(FindObjectsInactive.Include);
        presenter?.Close();
    }

    public void Open(ItemData weapon)
    {
        Open(weapon, ownerInventoryBridge);
    }

    public void Open(ItemData weapon, InventorySlotBridge inventoryBridge)
    {
        weapon?.EnsureRuntimeInstanceId();
        string nextWeaponRuntimeInstanceId = weapon != null ? weapon.runtimeInstanceId : string.Empty;

        currentWeapon = weapon;
        currentWeaponRuntimeInstanceId = nextWeaponRuntimeInstanceId;
        if (inventoryBridge != null && inventoryBridge.TryGetComboGemInventory(out PlayerInventory inventory))
        {
            SetOwnerInventoryBridge(inventoryBridge);
            ownerInventory = inventory;
        }
        BindPlayerEquipment(ResolvePlayerEquipment(inventoryBridge));
        if (currentWeapon != null && subscribedEquipment != null && !TryFindEquippedTarget(out currentWeapon))
        {
            Close();
            return;
        }
        if (readService == null)
            readService = new ItemDataWeaponComboGemPopupReadService();

        if (view == null)
            view = GetComponent<WeaponComboGemPopupView>();

        if (view == null)
            return;

        if (readService.TryBuild(currentWeapon, out WeaponComboGemPopupViewData viewData, out string error))
            view.Show(viewData);
        else
            view.ShowError(error);
    }

    public void Refresh()
    {
        if (currentWeapon != null)
            Open(currentWeapon);
    }

    public void Close()
    {
        ClearTransientInteractionState();
        currentWeapon = null;
        currentWeaponRuntimeInstanceId = string.Empty;
        SetOwnerInventoryBridge(null);
        BindPlayerEquipment(null);
        ownerInventory = null;
        view?.Hide();
    }

    private void HandleComboGemChanged(WeaponComboGemChangedEvent change)
    {
        if (currentWeapon == null
            || string.IsNullOrEmpty(currentWeaponRuntimeInstanceId)
            || change.Snapshot.WeaponRuntimeInstanceId != currentWeaponRuntimeInstanceId)
        {
            return;
        }

        Refresh(); // 서비스 확정 결과만 다시 표시
    }

    private void HandleSlotDropped(WeaponComboGemSlotDropIntent intent)
    {
        SlotUI originSlot = intent.OriginSlot;
        if (currentWeapon == null || originSlot == null || !(originSlot.OwnerBridge is InventorySlotBridge bridge))
        {
            view?.ShowStatus("인벤토리의 콤보 보석만 장착할 수 있습니다.");
            return;
        }

        if (ownerInventoryBridge != null && ownerInventoryBridge != bridge)
        {
            view?.ShowStatus("현재 인벤토리 소유 보석이 아닙니다.");
            return;
        }

        if (!bridge.TryResolveComboGemCandidate(originSlot.SlotIndex, out PlayerInventory inventory, out ItemData gem))
        {
            view?.ShowStatus("인벤토리의 콤보 보석만 장착할 수 있습니다.");
            return;
        }

        gem.EnsureRuntimeInstanceId();

        WeaponComboGemEquipResult result = WeaponComboGemEquipService.TryEquip(
            inventory,
            currentWeaponRuntimeInstanceId,
            TryResolveCurrentWeapon,
            intent.Target.AttackId,
            intent.Target.SlotIndex,
            originSlot.SlotIndex,
            gem.runtimeInstanceId); // 드래그 원본 슬롯과 예상 보석 ID를 서비스에서 재검증

        if (!result.Succeeded)
        {
            view?.ShowStatus(string.IsNullOrWhiteSpace(result.Message) ? "보석 장착에 실패했습니다." : result.Message);
            return;
        }

        view?.ShowStatus(intent.Target.IsOccupied ? "보석 교체를 완료했습니다." : "보석 장착을 완료했습니다.");
    }

    private void HandleInstalledDragRequested(WeaponComboGemInstalledDragIntent intent)
    {
        if (currentWeapon == null || intent.SourceView == null || ownerInventory == null
            || string.IsNullOrWhiteSpace(intent.AttackId) || intent.SlotIndex < 0)
        {
            view?.ShowStatus("장착 보석 드래그를 시작할 수 없습니다.");
            return;
        }

        ItemData installed = currentWeapon.GetWeaponComboGemAt(intent.AttackId, intent.SlotIndex);
        installed?.EnsureRuntimeInstanceId();
        if (installed == null
            || !string.Equals(installed.runtimeInstanceId, intent.GemRuntimeInstanceId, System.StringComparison.Ordinal))
        {
            view?.ShowStatus("장착 보석 상태가 변경되었습니다. 다시 시도하세요.");
            Refresh();
            return;
        }

        EquippedComboGemInventoryDropSource source = new EquippedComboGemInventoryDropSource(
            currentWeapon,
            intent.AttackId,
            intent.SlotIndex,
            installed);

        if (!intent.SourceView.BeginEquippedGemDrag(source, intent.ScreenPosition))
            view?.ShowStatus("장착 보석 드래그를 시작할 수 없습니다.");
    }

    private void HandleComboGemInventoryDropCompleted(WeaponComboGemEquipResult result)
    {
        view?.ShowStatus(result.Succeeded
            ? "지정한 인벤토리 슬롯으로 보석을 해제했습니다."
            : string.IsNullOrWhiteSpace(result.Message) ? "보석 해제에 실패했습니다." : result.Message);
    }

    private void SetOwnerInventoryBridge(InventorySlotBridge bridge)
    {
        if (ownerInventoryBridge == bridge)
            return;

        if (ownerInventoryBridge != null)
            ownerInventoryBridge.ComboGemInventoryDropCompleted -= HandleComboGemInventoryDropCompleted;

        ownerInventoryBridge = bridge;
        if (ownerInventoryBridge != null)
            ownerInventoryBridge.ComboGemInventoryDropCompleted += HandleComboGemInventoryDropCompleted;
    }

    private bool TryResolveCurrentWeapon(string runtimeInstanceId, out ItemData weapon)
    {
        weapon = null;
        if (currentWeapon == null
            || !string.Equals(currentWeaponRuntimeInstanceId, runtimeInstanceId, System.StringComparison.Ordinal)
            || !TryFindEquippedTarget(out ItemData equippedWeapon))
        {
            return false;
        }

        currentWeapon = equippedWeapon;
        weapon = equippedWeapon;
        return true;
    }

    private void BindPlayerEquipment(PlayerEquipment equipment)
    {
        if (subscribedEquipment == equipment)
            return;

        if (subscribedEquipment != null)
            subscribedEquipment.WeaponSlotsChanged -= HandleWeaponSlotsChanged;

        subscribedEquipment = equipment;
        if (subscribedEquipment != null)
            subscribedEquipment.WeaponSlotsChanged += HandleWeaponSlotsChanged;
    }

    private void HandleWeaponSlotsChanged()
    {
        if (currentWeapon == null || string.IsNullOrEmpty(currentWeaponRuntimeInstanceId))
            return;

        if (!TryFindEquippedTarget(out ItemData equippedWeapon))
        {
            Close(); // 실제 장착 소유가 사라졌을 때만 종료
            return;
        }

        currentWeapon = equippedWeapon; // 활성 슬롯 전환은 유지
    }

    private bool TryFindEquippedTarget(out ItemData equippedWeapon)
    {
        equippedWeapon = null;
        if (subscribedEquipment == null)
            return false;

        for (int i = 0; i < subscribedEquipment.WeaponSlotCountValue; i++)
        {
            ItemData slotWeapon = subscribedEquipment.GetWeaponSlotItem(i);
            if (slotWeapon == null)
                continue;

            slotWeapon.EnsureRuntimeInstanceId();
            if (string.Equals(slotWeapon.runtimeInstanceId, currentWeaponRuntimeInstanceId, System.StringComparison.Ordinal))
            {
                equippedWeapon = slotWeapon;
                return true;
            }
        }

        return false;
    }

    private PlayerEquipment ResolvePlayerEquipment(InventorySlotBridge inventoryBridge)
    {
        if (inventoryBridge != null && inventoryBridge.TryGetComboGemEquipment(out PlayerEquipment equipment))
            return equipment;

        return Object.FindFirstObjectByType<PlayerEquipment>();
    }

    private static void ClearTransientInteractionState()
    {
        if (DragSlot.IsDragging)
            DragSlot.ClearDragState();
    }
}
