using System;
using System.Collections.Generic;

public enum WeaponComboGemEquipFailureReason // 장착 실패 이유
{
    None,
    MissingInventory,
    InvalidWeapon,
    InvalidAttack,
    InvalidTarget,
    InvalidSlot,
    SlotLocked,
    InvalidGem,
    UnclassifiedGem,
    GemTypeMismatch,
    GemNotOwnedByInventory,
    DuplicateOwnership,
    InventorySpaceUnavailable,
    EmptySourceSlot,
    CommitFailed,
    InvalidInventorySlot,
    InventorySlotLocked,
    InventorySlotOccupied,
    SourceGemMismatch
}

public enum WeaponComboGemEquipOperation
{
    Equip,
    Unequip,
    MoveOrSwap
}

public delegate bool WeaponComboGemWeaponResolver(string runtimeInstanceId, out ItemData weapon); // 런타임 무기 조회

public readonly struct WeaponComboGemSlotSnapshot
{
    public readonly int SlotIndex;
    public readonly bool IsUnlocked;
    public readonly IReadOnlyList<ComboGemType> AllowedTypes;
    public readonly ItemData Gem;

    public WeaponComboGemSlotSnapshot(int slotIndex, bool isUnlocked, ComboGemType[] allowedTypes, ItemData gem)
    {
        SlotIndex = slotIndex;
        IsUnlocked = isUnlocked;
        AllowedTypes = Array.AsReadOnly(allowedTypes ?? Array.Empty<ComboGemType>());
        Gem = gem;
    }
}

public readonly struct WeaponComboGemLoadoutSnapshot
{
    public readonly string AttackId;
    public readonly IReadOnlyList<WeaponComboGemSlotSnapshot> Slots;

    public WeaponComboGemLoadoutSnapshot(string attackId, WeaponComboGemSlotSnapshot[] slots)
    {
        AttackId = attackId;
        Slots = Array.AsReadOnly(slots ?? Array.Empty<WeaponComboGemSlotSnapshot>());
    }
}

public readonly struct WeaponComboGemWeaponSnapshot
{
    public readonly string WeaponRuntimeInstanceId;
    public readonly long Revision;
    public readonly IReadOnlyList<WeaponComboGemLoadoutSnapshot> Loadouts;

    public WeaponComboGemWeaponSnapshot(
        string weaponRuntimeInstanceId,
        long revision,
        WeaponComboGemLoadoutSnapshot[] loadouts)
    {
        WeaponRuntimeInstanceId = weaponRuntimeInstanceId;
        Revision = revision;
        Loadouts = Array.AsReadOnly(loadouts ?? Array.Empty<WeaponComboGemLoadoutSnapshot>());
    }
}

public readonly struct WeaponComboGemEquipResult
{
    public readonly bool Succeeded;
    public readonly WeaponComboGemEquipFailureReason FailureReason;
    public readonly string Message;
    public readonly WeaponComboGemWeaponSnapshot Snapshot;

    private WeaponComboGemEquipResult(
        bool succeeded,
        WeaponComboGemEquipFailureReason failureReason,
        string message,
        WeaponComboGemWeaponSnapshot snapshot)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        Message = message ?? string.Empty;
        Snapshot = snapshot;
    }

    internal static WeaponComboGemEquipResult Success(WeaponComboGemWeaponSnapshot snapshot)
    {
        return new WeaponComboGemEquipResult(true, WeaponComboGemEquipFailureReason.None, string.Empty, snapshot);
    }

    internal static WeaponComboGemEquipResult Fail(
        WeaponComboGemEquipFailureReason failureReason,
        string message,
        WeaponComboGemWeaponSnapshot snapshot)
    {
        return new WeaponComboGemEquipResult(false, failureReason, message, snapshot);
    }
}

public readonly struct WeaponComboGemChangedEvent
{
    public readonly WeaponComboGemEquipOperation Operation;
    public readonly ItemData Weapon;
    public readonly string AttackId;
    public readonly int SlotIndex;
    public readonly long Revision;
    public readonly WeaponComboGemWeaponSnapshot Snapshot;

    public WeaponComboGemChangedEvent(
        WeaponComboGemEquipOperation operation,
        ItemData weapon,
        string attackId,
        int slotIndex,
        long revision,
        WeaponComboGemWeaponSnapshot snapshot)
    {
        Operation = operation;
        Weapon = weapon;
        AttackId = attackId;
        SlotIndex = slotIndex;
        Revision = revision;
        Snapshot = snapshot;
    }
}

public static class WeaponComboGemEquipService // 콤보 보석 소유권 전담
{
    private static long revision;

    public static event Action<WeaponComboGemChangedEvent> Changed;

    public static long Revision => revision;

    public static WeaponComboGemEquipResult TryEquip(
        PlayerInventory inventory,
        string weaponRuntimeInstanceId,
        WeaponComboGemWeaponResolver weaponResolver,
        string attackId,
        int slotIndex,
        int sourceInventorySlotIndex,
        string expectedGemRuntimeInstanceId)
    {
        if (!TryResolveWeapon(weaponRuntimeInstanceId, weaponResolver, out ItemData weapon, out WeaponComboGemEquipResult failure))
            return failure;

        return TryEquip(
            inventory,
            weapon,
            attackId,
            slotIndex,
            sourceInventorySlotIndex,
            expectedGemRuntimeInstanceId);
    }

    public static WeaponComboGemEquipResult TryEquip(
        PlayerInventory inventory,
        ItemData weapon,
        string attackId,
        int slotIndex,
        int sourceInventorySlotIndex,
        string expectedGemRuntimeInstanceId)
    {
        if (!TryValidateInventory(inventory, weapon, out WeaponComboGemEquipResult failure))
            return failure;

        if (!TryValidateTarget(weapon, attackId, slotIndex, out WeaponComboGemLoadout loadout, out failure))
            return failure;

        if (!TryValidateInventorySourceSlot(
                inventory,
                sourceInventorySlotIndex,
                expectedGemRuntimeInstanceId,
                weapon,
                out ItemData gem,
                out failure))
        {
            return failure;
        }

        if (!TryValidateGemForSlot(weapon, loadout, gem, slotIndex, out failure))
            return failure;

        ItemData installed = loadout.GetGemAt(slotIndex);
        if (installed != null && installed.IsSameRuntimeItem(gem))
        {
            return Fail(
                WeaponComboGemEquipFailureReason.DuplicateOwnership,
                "같은 보석이 인벤토리와 장착 슬롯에 동시에 존재합니다.",
                weapon);
        }

        if (ContainsInstalledGem(weapon, gem))
        {
            return Fail(
                WeaponComboGemEquipFailureReason.DuplicateOwnership,
                "이미 이 무기의 다른 슬롯에 장착된 보석입니다.",
                weapon);
        }

        if (installed != null && inventory.ContainsItem(installed))
        {
            return Fail(
                WeaponComboGemEquipFailureReason.DuplicateOwnership,
                "반환할 보석이 이미 인벤토리에 존재합니다.",
                weapon);
        }

        if (installed != null && CountInstalledGemReferences(weapon, installed) != 1)
        {
            return Fail(
                WeaponComboGemEquipFailureReason.DuplicateOwnership,
                "교체할 보석의 장착 참조가 중복되어 있습니다.",
                weapon);
        }

        if (!inventory.CanReplaceOwnedItemAt(sourceInventorySlotIndex, gem, installed))
        {
            return Fail(
                WeaponComboGemEquipFailureReason.InventorySpaceUnavailable,
                "교체 보석을 반환할 인벤토리 공간을 확보할 수 없습니다.",
                weapon);
        }

        if (installed != null)
            weapon.RemoveWeaponComboGemReference(attackId, slotIndex);

        if (!weapon.TryAssignWeaponComboGemReference(attackId, slotIndex, gem))
        {
            if (installed != null)
                weapon.TryAssignWeaponComboGemReference(attackId, slotIndex, installed); // 원본 복구

            return Fail(WeaponComboGemEquipFailureReason.CommitFailed, "보석 슬롯 반영에 실패했습니다.", weapon);
        }

        if (!inventory.TryReplaceOwnedItemAt(sourceInventorySlotIndex, gem, installed))
        {
            weapon.RemoveWeaponComboGemReference(attackId, slotIndex);
            if (installed != null)
                weapon.TryAssignWeaponComboGemReference(attackId, slotIndex, installed); // 양쪽 복구

            return Fail(WeaponComboGemEquipFailureReason.CommitFailed, "인벤토리 소유권 반영에 실패했습니다.", weapon);
        }

        return Complete(WeaponComboGemEquipOperation.Equip, weapon, attackId, slotIndex);
    }

    public static WeaponComboGemEquipResult TryUnequip(
        PlayerInventory inventory,
        ItemData weapon,
        string attackId,
        int slotIndex,
        int targetInventorySlotIndex,
        string expectedGemRuntimeInstanceId)
    {
        if (!TryValidateInventory(inventory, weapon, out WeaponComboGemEquipResult failure))
            return failure;

        if (!TryValidateTarget(weapon, attackId, slotIndex, out WeaponComboGemLoadout loadout, out failure))
            return failure;

        if (!TryValidateInventoryTargetSlot(inventory, targetInventorySlotIndex, weapon, out failure))
            return failure;

        ItemData installed = loadout.GetGemAt(slotIndex);
        if (installed == null)
            return Fail(WeaponComboGemEquipFailureReason.EmptySourceSlot, "해제할 보석이 없습니다.", weapon);

        installed.EnsureRuntimeInstanceId();
        if (string.IsNullOrWhiteSpace(expectedGemRuntimeInstanceId)
            || !string.Equals(installed.runtimeInstanceId, expectedGemRuntimeInstanceId, StringComparison.Ordinal))
        {
            return Fail(
                WeaponComboGemEquipFailureReason.SourceGemMismatch,
                "장착 보석이 드래그 시작 시점과 달라졌습니다.",
                weapon);
        }

        if (inventory.ContainsItem(installed))
        {
            return Fail(
                WeaponComboGemEquipFailureReason.DuplicateOwnership,
                "장착 보석이 이미 인벤토리에 존재합니다.",
                weapon);
        }

        if (CountInstalledGemReferences(weapon, installed) != 1)
        {
            return Fail(
                WeaponComboGemEquipFailureReason.DuplicateOwnership,
                "해제할 보석의 장착 참조가 중복되어 있습니다.",
                weapon);
        }

        if (!inventory.CanReplaceOwnedItemAt(targetInventorySlotIndex, null, installed))
        {
            return Fail(
                WeaponComboGemEquipFailureReason.CommitFailed,
                "인벤토리 대상 슬롯 상태가 변경되었습니다.",
                weapon);
        }

        ItemData removed = weapon.RemoveWeaponComboGemReference(attackId, slotIndex);
        if (removed == null || !removed.IsSameRuntimeItem(installed))
        {
            if (removed != null)
                weapon.TryAssignWeaponComboGemReference(attackId, slotIndex, removed);

            return Fail(WeaponComboGemEquipFailureReason.CommitFailed, "보석 해제에 실패했습니다.", weapon);
        }

        if (!inventory.TryReplaceOwnedItemAt(targetInventorySlotIndex, null, installed))
        {
            weapon.TryAssignWeaponComboGemReference(attackId, slotIndex, installed); // 양쪽 원본 복구
            return Fail(WeaponComboGemEquipFailureReason.CommitFailed, "인벤토리 반환에 실패했습니다.", weapon);
        }

        return Complete(WeaponComboGemEquipOperation.Unequip, weapon, attackId, slotIndex);
    }

    public static WeaponComboGemEquipResult TryUnequip(
        PlayerInventory inventory,
        string weaponRuntimeInstanceId,
        WeaponComboGemWeaponResolver weaponResolver,
        string attackId,
        int slotIndex,
        int targetInventorySlotIndex,
        string expectedGemRuntimeInstanceId)
    {
        if (!TryResolveWeapon(weaponRuntimeInstanceId, weaponResolver, out ItemData weapon, out WeaponComboGemEquipResult failure))
            return failure;

        return TryUnequip(
            inventory,
            weapon,
            attackId,
            slotIndex,
            targetInventorySlotIndex,
            expectedGemRuntimeInstanceId);
    }

    public static WeaponComboGemEquipResult TryMoveOrSwap(
        ItemData weapon,
        string sourceAttackId,
        int sourceSlotIndex,
        string targetAttackId,
        int targetSlotIndex)
    {
        if (!TryValidateWeapon(weapon, out WeaponComboGemEquipResult failure))
            return failure;

        if (string.Equals(sourceAttackId, targetAttackId, StringComparison.Ordinal)
            && sourceSlotIndex == targetSlotIndex)
        {
            return Fail(WeaponComboGemEquipFailureReason.InvalidTarget, "같은 슬롯으로 이동할 수 없습니다.", weapon);
        }

        if (!TryValidateTarget(weapon, sourceAttackId, sourceSlotIndex, out WeaponComboGemLoadout sourceLoadout, out failure)
            || !TryValidateTarget(weapon, targetAttackId, targetSlotIndex, out WeaponComboGemLoadout targetLoadout, out failure))
        {
            return failure;
        }

        ItemData sourceGem = sourceLoadout.GetGemAt(sourceSlotIndex);
        if (sourceGem == null)
            return Fail(WeaponComboGemEquipFailureReason.EmptySourceSlot, "이동할 보석이 없습니다.", weapon);

        ItemData targetGem = targetLoadout.GetGemAt(targetSlotIndex);
        if (CountInstalledGemReferences(weapon, sourceGem) != 1
            || (targetGem != null && CountInstalledGemReferences(weapon, targetGem) != 1))
        {
            return Fail(
                WeaponComboGemEquipFailureReason.DuplicateOwnership,
                "이동할 보석의 장착 참조가 중복되어 있습니다.",
                weapon);
        }

        if (!TryValidateGemForSlot(weapon, targetLoadout, sourceGem, targetSlotIndex, out failure))
            return failure;

        if (targetGem != null && !TryValidateGemForSlot(weapon, sourceLoadout, targetGem, sourceSlotIndex, out failure))
            return failure;

        weapon.RemoveWeaponComboGemReference(sourceAttackId, sourceSlotIndex);
        if (targetGem != null)
            weapon.RemoveWeaponComboGemReference(targetAttackId, targetSlotIndex);

        bool targetAssigned = weapon.TryAssignWeaponComboGemReference(targetAttackId, targetSlotIndex, sourceGem);
        bool sourceAssigned = targetGem == null
            || weapon.TryAssignWeaponComboGemReference(sourceAttackId, sourceSlotIndex, targetGem);
        if (!targetAssigned || !sourceAssigned)
        {
            weapon.RemoveWeaponComboGemReference(targetAttackId, targetSlotIndex);
            weapon.RemoveWeaponComboGemReference(sourceAttackId, sourceSlotIndex);
            weapon.TryAssignWeaponComboGemReference(sourceAttackId, sourceSlotIndex, sourceGem);
            if (targetGem != null)
                weapon.TryAssignWeaponComboGemReference(targetAttackId, targetSlotIndex, targetGem); // 양쪽 복구

            return Fail(WeaponComboGemEquipFailureReason.CommitFailed, "보석 슬롯 이동에 실패했습니다.", weapon);
        }

        return Complete(WeaponComboGemEquipOperation.MoveOrSwap, weapon, targetAttackId, targetSlotIndex);
    }

    public static WeaponComboGemEquipResult TryMoveOrSwap(
        string weaponRuntimeInstanceId,
        WeaponComboGemWeaponResolver weaponResolver,
        string sourceAttackId,
        int sourceSlotIndex,
        string targetAttackId,
        int targetSlotIndex)
    {
        if (!TryResolveWeapon(weaponRuntimeInstanceId, weaponResolver, out ItemData weapon, out WeaponComboGemEquipResult failure))
            return failure;

        return TryMoveOrSwap(weapon, sourceAttackId, sourceSlotIndex, targetAttackId, targetSlotIndex);
    }

    public static bool TryCreateSnapshot(
        ItemData weapon,
        out WeaponComboGemWeaponSnapshot snapshot,
        out string error)
    {
        snapshot = default;
        if (weapon == null || !(weapon.baseData is WeaponItemData))
        {
            error = "유효한 무기 아이템이 아닙니다.";
            return false;
        }

        if (!weapon.TryEnsureWeaponComboGemLoadouts(out error))
            return false;

        int loadoutCount = weapon.GetWeaponComboGemLoadoutCount();
        WeaponComboGemLoadoutSnapshot[] loadouts = new WeaponComboGemLoadoutSnapshot[loadoutCount];
        for (int stepIndex = 0; stepIndex < loadoutCount; stepIndex++)
        {
            if (!weapon.TryGetWeaponComboAttackId(stepIndex, out string attackId))
            {
                error = "콤보 공격 ID를 읽을 수 없습니다.";
                return false;
            }

            WeaponComboGemLoadout loadout = weapon.GetWeaponComboGemLoadout(attackId);
            WeaponComboGemSlotSnapshot[] slots = new WeaponComboGemSlotSnapshot[WeaponComboGemSlotRules.SlotCapacity];
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                slots[slotIndex] = new WeaponComboGemSlotSnapshot(
                    slotIndex,
                    loadout != null && loadout.IsSlotUnlocked(slotIndex),
                    WeaponComboGemSlotRules.GetAllowedTypes(slotIndex),
                    loadout != null ? loadout.GetGemAt(slotIndex) : null);
            }

            loadouts[stepIndex] = new WeaponComboGemLoadoutSnapshot(attackId, slots);
        }

        weapon.EnsureRuntimeInstanceId();
        snapshot = new WeaponComboGemWeaponSnapshot(weapon.runtimeInstanceId, revision, loadouts);
        error = null;
        return true;
    }

    public static bool TryCreateSnapshot(
        string weaponRuntimeInstanceId,
        WeaponComboGemWeaponResolver weaponResolver,
        out WeaponComboGemWeaponSnapshot snapshot,
        out string error)
    {
        if (!TryResolveWeapon(
                weaponRuntimeInstanceId,
                weaponResolver,
                out ItemData weapon,
                out WeaponComboGemEquipResult failure))
        {
            snapshot = default;
            error = failure.Message;
            return false;
        }

        return TryCreateSnapshot(weapon, out snapshot, out error);
    }

    private static bool TryValidateInventory(
        PlayerInventory inventory,
        ItemData weapon,
        out WeaponComboGemEquipResult failure)
    {
        if (inventory == null)
        {
            failure = Fail(WeaponComboGemEquipFailureReason.MissingInventory, "인벤토리를 찾을 수 없습니다.", weapon);
            return false;
        }

        return TryValidateWeapon(weapon, out failure);
    }

    private static bool TryResolveWeapon(
        string weaponRuntimeInstanceId,
        WeaponComboGemWeaponResolver weaponResolver,
        out ItemData weapon,
        out WeaponComboGemEquipResult failure)
    {
        weapon = null;
        if (string.IsNullOrWhiteSpace(weaponRuntimeInstanceId)
            || weaponResolver == null
            || !weaponResolver(weaponRuntimeInstanceId, out weapon)
            || weapon == null)
        {
            failure = Fail(WeaponComboGemEquipFailureReason.InvalidWeapon, "런타임 ID에 해당하는 무기를 찾을 수 없습니다.", null);
            return false;
        }

        weapon.EnsureRuntimeInstanceId();
        if (!string.Equals(weapon.runtimeInstanceId, weaponRuntimeInstanceId, StringComparison.Ordinal))
        {
            failure = Fail(WeaponComboGemEquipFailureReason.InvalidWeapon, "조회된 무기 런타임 ID가 요청과 다릅니다.", weapon);
            return false;
        }

        return TryValidateWeapon(weapon, out failure);
    }

    private static bool TryValidateWeapon(ItemData weapon, out WeaponComboGemEquipResult failure)
    {
        if (weapon == null || !(weapon.baseData is WeaponItemData))
        {
            failure = Fail(WeaponComboGemEquipFailureReason.InvalidWeapon, "유효한 무기 아이템이 아닙니다.", weapon);
            return false;
        }

        if (!weapon.TryEnsureWeaponComboGemLoadouts(out string error))
        {
            failure = Fail(WeaponComboGemEquipFailureReason.InvalidWeapon, error, weapon);
            return false;
        }

        failure = default;
        return true;
    }

    private static bool TryValidateTarget(
        ItemData weapon,
        string attackId,
        int slotIndex,
        out WeaponComboGemLoadout loadout,
        out WeaponComboGemEquipResult failure)
    {
        loadout = null;
        if (!TryValidateWeapon(weapon, out failure))
            return false;

        if (string.IsNullOrWhiteSpace(attackId)
            || (loadout = weapon.GetWeaponComboGemLoadout(attackId)) == null)
        {
            failure = Fail(WeaponComboGemEquipFailureReason.InvalidAttack, "유효한 콤보 공격 ID가 아닙니다.", weapon);
            return false;
        }

        if (!WeaponComboGemSlotRules.IsValidSlotIndex(slotIndex))
        {
            failure = Fail(WeaponComboGemEquipFailureReason.InvalidSlot, "보석 슬롯 범위를 벗어났습니다.", weapon);
            return false;
        }

        if (!WeaponComboGemSlotRules.IsSlotUnlocked(slotIndex, loadout.UnlockedSlotCount))
        {
            failure = Fail(WeaponComboGemEquipFailureReason.SlotLocked, "잠긴 보석 슬롯입니다.", weapon);
            return false;
        }

        failure = default;
        return true;
    }

    private static bool TryValidateInventoryTargetSlot(
        PlayerInventory inventory,
        int targetInventorySlotIndex,
        ItemData weapon,
        out WeaponComboGemEquipResult failure)
    {
        if (targetInventorySlotIndex < 0 || targetInventorySlotIndex >= inventory.Capacity)
        {
            failure = Fail(
                WeaponComboGemEquipFailureReason.InvalidInventorySlot,
                "인벤토리 슬롯 범위를 벗어났습니다.",
                weapon);
            return false;
        }

        if (targetInventorySlotIndex >= inventory.UnlockedSlotCount)
        {
            failure = Fail(
                WeaponComboGemEquipFailureReason.InventorySlotLocked,
                "잠긴 인벤토리 슬롯에는 보석을 해제할 수 없습니다.",
                weapon);
            return false;
        }

        if (inventory.GetItemAt(targetInventorySlotIndex) != null)
        {
            failure = Fail(
                WeaponComboGemEquipFailureReason.InventorySlotOccupied,
                "비어 있는 인벤토리 슬롯에만 보석을 해제할 수 있습니다.",
                weapon);
            return false;
        }

        failure = default;
        return true;
    }

    private static bool TryValidateInventorySourceSlot(
        PlayerInventory inventory,
        int sourceInventorySlotIndex,
        string expectedGemRuntimeInstanceId,
        ItemData weapon,
        out ItemData gem,
        out WeaponComboGemEquipResult failure)
    {
        gem = null;
        if (sourceInventorySlotIndex < 0 || sourceInventorySlotIndex >= inventory.Capacity)
        {
            failure = Fail(
                WeaponComboGemEquipFailureReason.InvalidInventorySlot,
                "인벤토리 출발 슬롯 범위를 벗어났습니다.",
                weapon);
            return false;
        }

        if (sourceInventorySlotIndex >= inventory.UnlockedSlotCount)
        {
            failure = Fail(
                WeaponComboGemEquipFailureReason.InventorySlotLocked,
                "잠긴 인벤토리 슬롯의 보석은 장착할 수 없습니다.",
                weapon);
            return false;
        }

        gem = inventory.GetItemAt(sourceInventorySlotIndex);
        if (gem == null)
        {
            failure = Fail(
                WeaponComboGemEquipFailureReason.EmptySourceSlot,
                "인벤토리 출발 슬롯에 보석이 없습니다.",
                weapon);
            return false;
        }

        gem.EnsureRuntimeInstanceId();
        if (string.IsNullOrWhiteSpace(expectedGemRuntimeInstanceId)
            || !string.Equals(gem.runtimeInstanceId, expectedGemRuntimeInstanceId, StringComparison.Ordinal))
        {
            failure = Fail(
                WeaponComboGemEquipFailureReason.SourceGemMismatch,
                "인벤토리 보석이 드래그 시작 시점과 달라졌습니다.",
                weapon);
            return false;
        }

        failure = default;
        return true;
    }

    private static bool TryValidateGemForSlot(
        ItemData weapon,
        WeaponComboGemLoadout loadout,
        ItemData gem,
        int slotIndex,
        out WeaponComboGemEquipResult failure)
    {
        if (gem == null || gem.stackCount <= 0)
        {
            failure = Fail(WeaponComboGemEquipFailureReason.InvalidGem, "유효한 보석 아이템이 아닙니다.", weapon);
            return false;
        }

        if (!(gem.baseData is ComboGemItemData comboGemData))
        {
            failure = Fail(WeaponComboGemEquipFailureReason.InvalidGem, "유효한 보석 아이템이 아닙니다.", weapon);
            return false;
        }

        WeaponComboGemSlotValidationResult slotResult = WeaponComboGemSlotRules.ValidateGemData(
            slotIndex,
            loadout != null ? loadout.UnlockedSlotCount : 0,
            comboGemData);

        if (!slotResult.Succeeded)
        {
            failure = CreateSlotRuleFailure(slotResult, weapon);
            return false;
        }

        failure = default;
        return true;
    }

    private static WeaponComboGemEquipResult CreateSlotRuleFailure(
        WeaponComboGemSlotValidationResult slotResult,
        ItemData weapon)
    {
        switch (slotResult.FailureReason)
        {
            case WeaponComboGemSlotValidationFailureReason.InvalidSlot:
                return Fail(WeaponComboGemEquipFailureReason.InvalidSlot, "보석 슬롯 범위를 벗어났습니다.", weapon);
            case WeaponComboGemSlotValidationFailureReason.InvalidUnlockedSlotCount:
                return Fail(WeaponComboGemEquipFailureReason.InvalidTarget, "무기 보석 슬롯 해금 상태가 유효하지 않습니다.", weapon);
            case WeaponComboGemSlotValidationFailureReason.SlotLocked:
                return Fail(WeaponComboGemEquipFailureReason.SlotLocked, "잠긴 보석 슬롯입니다.", weapon);
            case WeaponComboGemSlotValidationFailureReason.UnclassifiedGem:
                return Fail(WeaponComboGemEquipFailureReason.UnclassifiedGem, "콤보 보석 역할이 지정되지 않았습니다.", weapon);
            default:
                return Fail(
                    WeaponComboGemEquipFailureReason.GemTypeMismatch,
                    slotResult.SlotIndex == WeaponComboGemSlotRules.ElementSlotIndex
                        ? "1번 슬롯에는 속성 보석만 장착할 수 있습니다."
                        : "2~4번 슬롯에는 연계·강화 보석만 장착할 수 있습니다.",
                    weapon);
        }
    }

    private static bool ContainsInstalledGem(ItemData weapon, ItemData gem)
    {
        return CountInstalledGemReferences(weapon, gem) > 0;
    }

    private static int CountInstalledGemReferences(ItemData weapon, ItemData gem)
    {
        if (weapon == null || gem == null)
            return 0;

        int referenceCount = 0;

        int count = weapon.GetWeaponComboGemLoadoutCount();
        for (int stepIndex = 0; stepIndex < count; stepIndex++)
        {
            if (!weapon.TryGetWeaponComboAttackId(stepIndex, out string attackId))
                continue;

            for (int slotIndex = 0; slotIndex < WeaponComboGemSlotRules.SlotCapacity; slotIndex++)
            {
                ItemData installed = weapon.GetWeaponComboGemAt(attackId, slotIndex);
                if (installed != null && installed.IsSameRuntimeItem(gem))
                    referenceCount++;
            }
        }

        return referenceCount;
    }

    private static WeaponComboGemEquipResult Complete(
        WeaponComboGemEquipOperation operation,
        ItemData weapon,
        string attackId,
        int slotIndex)
    {
        revision++;
        TryCreateSnapshot(weapon, out WeaponComboGemWeaponSnapshot snapshot, out _);
        Changed?.Invoke(new WeaponComboGemChangedEvent(operation, weapon, attackId, slotIndex, revision, snapshot));
        return WeaponComboGemEquipResult.Success(snapshot);
    }

    private static WeaponComboGemEquipResult Fail(
        WeaponComboGemEquipFailureReason reason,
        string message,
        ItemData weapon)
    {
        TryCreateSnapshot(weapon, out WeaponComboGemWeaponSnapshot snapshot, out _);
        return WeaponComboGemEquipResult.Fail(reason, message, snapshot);
    }
}
