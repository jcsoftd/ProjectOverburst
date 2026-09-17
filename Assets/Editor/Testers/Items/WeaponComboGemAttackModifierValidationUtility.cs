using System;
using UnityEditor;
using UnityEngine;

public static class WeaponComboGemAttackModifierValidationUtility // 전투 조회 회귀 검증
{
    private const string FireGemPath = "Assets/ProjectOverburst/03_Features/Items/Data/Items/ComboGems/ComboGem_Element_Fire.asset";

    [MenuItem("OVERBURST/Codex/Validate/Combo Gem Attack Modifier")]
    public static void Validate()
    {
        WeaponItemData weaponData = FindMeleeWeapon(out string attackId);
        ElementComboGemItemData fireGemData = AssetDatabase.LoadAssetAtPath<ElementComboGemItemData>(FireGemPath);
        if (fireGemData == null)
            throw new InvalidOperationException("Fire combo gem asset is missing.");

        ItemData weapon = new ItemData(weaponData, 1, ItemGrade.Common);
        ValidatePhysicalSnapshot(weapon, attackId);

        ItemData fireGem = new ItemData(fireGemData, 1, ItemGrade.Common);
        float rolledPercent = GetElementDamageIncrease(fireGem);
        if (!weapon.TryAssignWeaponComboGemReference(
                attackId,
                WeaponComboGemSlotRules.ElementSlotIndex,
                fireGem))
        {
            throw new InvalidOperationException("Fire combo gem assignment failed.");
        }

        ValidateFireSnapshot(weapon, attackId, fireGem, rolledPercent);
        ValidateStableReresolve(weapon, attackId, fireGem, rolledPercent);
        ValidateInvalidAttackId(weapon);
        ValidateUnequipService(weaponData, attackId, fireGemData);
        Debug.Log("[ProjectVTP] Combo gem attack modifier validation passed.");
    }

    public static void RunOnceFromCommandLine()
    {
        Validate();
    }

    private static WeaponItemData FindMeleeWeapon(out string attackId)
    {
        string[] guids = AssetDatabase.FindAssets("t:WeaponItemData");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            WeaponItemData weaponData = AssetDatabase.LoadAssetAtPath<WeaponItemData>(path);
            if (weaponData == null)
                continue;

            ItemData weapon = new ItemData(weaponData, 1, ItemGrade.Common);
            if (weapon.TryGetWeaponComboAttackId(0, out attackId))
                return weaponData;
        }

        attackId = null;
        throw new InvalidOperationException("Melee weapon with a stable attackId was not found.");
    }

    private static void ValidatePhysicalSnapshot(ItemData weapon, string attackId)
    {
        if (!WeaponComboGemAttackModifierResolver.TryResolve(
                weapon,
                attackId,
                out WeaponComboGemAttackModifierSnapshot snapshot,
                out WeaponComboGemAttackModifierResolveFailureReason failureReason)
            || failureReason != WeaponComboGemAttackModifierResolveFailureReason.None
            || snapshot.AttackId != attackId
            || snapshot.HasElement
            || snapshot.Element != WeaponElement.None
            || !Mathf.Approximately(snapshot.ElementDamageIncreasePercent, 0f)
            || !Mathf.Approximately(snapshot.DamageMultiplier, 1f)
            || !string.IsNullOrEmpty(snapshot.SourceGemRuntimeInstanceId))
        {
            throw new InvalidOperationException("Physical combo snapshot is invalid: " + failureReason);
        }
    }

    private static void ValidateFireSnapshot(
        ItemData weapon,
        string attackId,
        ItemData fireGem,
        float rolledPercent)
    {
        if (!WeaponComboGemAttackModifierResolver.TryResolve(
                weapon,
                attackId,
                out WeaponComboGemAttackModifierSnapshot snapshot,
                out WeaponComboGemAttackModifierResolveFailureReason failureReason)
            || failureReason != WeaponComboGemAttackModifierResolveFailureReason.None
            || !snapshot.HasElement
            || snapshot.Element != WeaponElement.Fire
            || !Mathf.Approximately(snapshot.ElementDamageIncreasePercent, rolledPercent)
            || !Mathf.Approximately(snapshot.DamageMultiplier, 1f + rolledPercent / 100f)
            || snapshot.SourceGemRuntimeInstanceId != fireGem.runtimeInstanceId)
        {
            throw new InvalidOperationException("Fire combo snapshot is invalid: " + failureReason);
        }
    }

    private static void ValidateStableReresolve(
        ItemData weapon,
        string attackId,
        ItemData fireGem,
        float rolledPercent)
    {
        fireGem.EnsureRuntimeState(); // 기존 롤 보존 확인
        ValidateFireSnapshot(weapon, attackId, fireGem, rolledPercent);
        ValidateFireSnapshot(weapon, attackId, fireGem, rolledPercent);
    }

    private static void ValidateInvalidAttackId(ItemData weapon)
    {
        if (WeaponComboGemAttackModifierResolver.TryResolve(
                weapon,
                "invalid_attack_id",
                out _,
                out WeaponComboGemAttackModifierResolveFailureReason failureReason)
            || failureReason != WeaponComboGemAttackModifierResolveFailureReason.InvalidAttackId)
        {
            throw new InvalidOperationException("Invalid attackId was not rejected: " + failureReason);
        }
    }

    private static float GetElementDamageIncrease(ItemData fireGem)
    {
        if (fireGem.comboGemOptions == null || fireGem.comboGemOptions.Count != 1)
            throw new InvalidOperationException("Fire combo gem option count is invalid.");

        ComboGemRolledOption option = fireGem.comboGemOptions[0];
        if (option == null || option.optionType != ComboGemRandomOptionType.ElementDamageIncrease)
            throw new InvalidOperationException("Fire combo gem option type is invalid.");

        return option.value;
    }

    private static void ValidateUnequipService(
        WeaponItemData weaponData,
        string attackId,
        ElementComboGemItemData fireGemData)
    {
        GameObject inventoryObject = new GameObject("ComboGemEquipServiceValidation");
        try
        {
            PlayerInventory inventory = inventoryObject.AddComponent<PlayerInventory>();
            ValidateDragEquip(inventory, weaponData, attackId, fireGemData);
            ValidateExactInventorySlotUnequip(inventory, weaponData, attackId, fireGemData);
            ValidateUnequipFailures(inventory, weaponData, attackId, fireGemData);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(inventoryObject);
        }
    }

    private static void ValidateDragEquip(
        PlayerInventory inventory,
        WeaponItemData weaponData,
        string attackId,
        ElementComboGemItemData fireGemData)
    {
        const int sourceInventorySlotIndex = 3;
        ClearInventory(inventory);
        ItemData weapon = new ItemData(weaponData, 1, ItemGrade.Common);
        ItemData gem = new ItemData(fireGemData, 1, ItemGrade.Common);
        Require(inventory.SetItemAt(sourceInventorySlotIndex, gem), "드래그 장착 출발 슬롯 준비 실패");

        long revisionBefore = WeaponComboGemEquipService.Revision;
        int inventoryEventCount = 0;
        int serviceEventCount = 0;
        Action inventoryChanged = () => inventoryEventCount++;
        Action<WeaponComboGemChangedEvent> serviceChanged = _ => serviceEventCount++;
        inventory.Changed += inventoryChanged;
        WeaponComboGemEquipService.Changed += serviceChanged;
        try
        {
            WeaponComboGemEquipResult result = WeaponComboGemEquipService.TryEquip(
                inventory,
                weapon,
                attackId,
                WeaponComboGemSlotRules.ElementSlotIndex,
                sourceInventorySlotIndex,
                gem.runtimeInstanceId);
            Require(result.Succeeded, "드래그 장착 실패: " + result.FailureReason);
            Require(inventory.GetItemAt(sourceInventorySlotIndex) == null, "장착 성공 후 출발 인벤토리 슬롯이 비워지지 않았습니다.");
            ItemData installed = weapon.GetWeaponComboGemAt(attackId, WeaponComboGemSlotRules.ElementSlotIndex);
            Require(installed != null && installed.IsSameRuntimeItem(gem), "장착 성공 후 원본 보석 참조가 유지되지 않았습니다.");
            Require(WeaponComboGemEquipService.Revision == revisionBefore + 1, "장착 성공 revision 증가 횟수가 다릅니다.");
            Require(inventoryEventCount == 1 && serviceEventCount == 1, "장착 성공 event 발생 횟수가 다릅니다.");
        }
        finally
        {
            inventory.Changed -= inventoryChanged;
            WeaponComboGemEquipService.Changed -= serviceChanged;
        }
    }

    private static void ValidateExactInventorySlotUnequip(
        PlayerInventory inventory,
        WeaponItemData weaponData,
        string attackId,
        ElementComboGemItemData fireGemData)
    {
        const int targetInventorySlotIndex = 5;
        ClearInventory(inventory);
        ItemData weapon = CreateWeaponWithInstalledGem(weaponData, attackId, fireGemData, out ItemData installed);
        long revisionBefore = WeaponComboGemEquipService.Revision;
        int inventoryEventCount = 0;
        int serviceEventCount = 0;
        Action inventoryChanged = () => inventoryEventCount++;
        Action<WeaponComboGemChangedEvent> serviceChanged = _ => serviceEventCount++;
        inventory.Changed += inventoryChanged;
        WeaponComboGemEquipService.Changed += serviceChanged;
        try
        {
            WeaponComboGemEquipResult result = WeaponComboGemEquipService.TryUnequip(
                inventory,
                weapon,
                attackId,
                WeaponComboGemSlotRules.ElementSlotIndex,
                targetInventorySlotIndex,
                installed.runtimeInstanceId);

            Require(result.Succeeded, "지정 인벤토리 슬롯 해제 실패: " + result.FailureReason);
            Require(weapon.GetWeaponComboGemAt(attackId, WeaponComboGemSlotRules.ElementSlotIndex) == null, "성공 후 콤보 슬롯이 비워지지 않았습니다.");
            Require(inventory.GetItemAt(targetInventorySlotIndex) != null
                && inventory.GetItemAt(targetInventorySlotIndex).IsSameRuntimeItem(installed), "지정한 정확한 인벤토리 슬롯으로 복귀하지 않았습니다.");
            Require(WeaponComboGemEquipService.Revision == revisionBefore + 1, "성공 revision 증가 횟수가 다릅니다.");
            Require(inventoryEventCount == 1 && serviceEventCount == 1, "성공 event 발생 횟수가 다릅니다.");
        }
        finally
        {
            inventory.Changed -= inventoryChanged;
            WeaponComboGemEquipService.Changed -= serviceChanged;
        }
    }

    private static void ValidateUnequipFailures(
        PlayerInventory inventory,
        WeaponItemData weaponData,
        string attackId,
        ElementComboGemItemData fireGemData)
    {
        ValidateUnequipFailure(inventory, weaponData, attackId, fireGemData, -1, null, WeaponComboGemEquipFailureReason.InvalidInventorySlot);
        ValidateUnequipFailure(inventory, weaponData, attackId, fireGemData, inventory.UnlockedSlotCount, null, WeaponComboGemEquipFailureReason.InventorySlotLocked);
        ValidateUnequipFailure(inventory, weaponData, attackId, fireGemData, 0, new ItemData(fireGemData, 1, ItemGrade.Common), WeaponComboGemEquipFailureReason.InventorySlotOccupied);
        ValidateUnequipFailure(inventory, weaponData, attackId, fireGemData, 0, null, WeaponComboGemEquipFailureReason.SourceGemMismatch, "stale_gem_id");
        ValidateInvalidComboTargetFailures(inventory, weaponData, attackId, fireGemData);

        ClearInventory(inventory);
        for (int i = 0; i < inventory.UnlockedSlotCount; i++)
            Require(inventory.SetItemAt(i, new ItemData(fireGemData, 1, ItemGrade.Common)), "전체 인벤토리 검증 준비 실패: " + i);

        ItemData fullInventoryWeapon = CreateWeaponWithInstalledGem(weaponData, attackId, fireGemData, out ItemData fullInventoryGem);
        ValidateFailurePreserved(
            inventory,
            fullInventoryWeapon,
            attackId,
            fullInventoryGem,
            () => WeaponComboGemEquipService.TryUnequip(
                inventory,
                fullInventoryWeapon,
                attackId,
                WeaponComboGemSlotRules.ElementSlotIndex,
                0,
                fullInventoryGem.runtimeInstanceId),
            WeaponComboGemEquipFailureReason.InventorySlotOccupied);
    }

    private static void ValidateInvalidComboTargetFailures(
        PlayerInventory inventory,
        WeaponItemData weaponData,
        string attackId,
        ElementComboGemItemData fireGemData)
    {
        ClearInventory(inventory);
        ItemData invalidAttackWeapon = CreateWeaponWithInstalledGem(weaponData, attackId, fireGemData, out ItemData invalidAttackGem);
        ValidateFailurePreserved(
            inventory,
            invalidAttackWeapon,
            attackId,
            invalidAttackGem,
            () => WeaponComboGemEquipService.TryUnequip(
                inventory,
                invalidAttackWeapon,
                "invalid_attack_id",
                WeaponComboGemSlotRules.ElementSlotIndex,
                0,
                invalidAttackGem.runtimeInstanceId),
            WeaponComboGemEquipFailureReason.InvalidAttack);

        ItemData invalidSlotWeapon = CreateWeaponWithInstalledGem(weaponData, attackId, fireGemData, out ItemData invalidSlotGem);
        ValidateFailurePreserved(
            inventory,
            invalidSlotWeapon,
            attackId,
            invalidSlotGem,
            () => WeaponComboGemEquipService.TryUnequip(
                inventory,
                invalidSlotWeapon,
                attackId,
                WeaponComboGemSlotRules.SlotCapacity,
                0,
                invalidSlotGem.runtimeInstanceId),
            WeaponComboGemEquipFailureReason.InvalidSlot);

        ItemData lockedSlotWeapon = CreateWeaponWithInstalledGem(weaponData, attackId, fireGemData, out ItemData lockedSlotGem);
        ValidateFailurePreserved(
            inventory,
            lockedSlotWeapon,
            attackId,
            lockedSlotGem,
            () => WeaponComboGemEquipService.TryUnequip(
                inventory,
                lockedSlotWeapon,
                attackId,
                WeaponComboGemSlotRules.SlotCapacity - 1,
                0,
                lockedSlotGem.runtimeInstanceId),
            WeaponComboGemEquipFailureReason.SlotLocked);
    }

    private static void ValidateUnequipFailure(
        PlayerInventory inventory,
        WeaponItemData weaponData,
        string attackId,
        ElementComboGemItemData fireGemData,
        int targetInventorySlotIndex,
        ItemData targetItem,
        WeaponComboGemEquipFailureReason expectedFailureReason,
        string expectedGemRuntimeInstanceId = null)
    {
        ClearInventory(inventory);
        if (targetItem != null)
            Require(inventory.SetItemAt(targetInventorySlotIndex, targetItem), "대상 슬롯 점유 검증 준비 실패");

        ItemData weapon = CreateWeaponWithInstalledGem(weaponData, attackId, fireGemData, out ItemData installed);
        string expectedId = expectedGemRuntimeInstanceId ?? installed.runtimeInstanceId;
        ValidateFailurePreserved(
            inventory,
            weapon,
            attackId,
            installed,
            () => WeaponComboGemEquipService.TryUnequip(
                inventory,
                weapon,
                attackId,
                WeaponComboGemSlotRules.ElementSlotIndex,
                targetInventorySlotIndex,
                expectedId),
            expectedFailureReason);
    }

    private static void ValidateFailurePreserved(
        PlayerInventory inventory,
        ItemData weapon,
        string attackId,
        ItemData installed,
        Func<WeaponComboGemEquipResult> operation,
        WeaponComboGemEquipFailureReason expectedFailureReason)
    {
        long revisionBefore = WeaponComboGemEquipService.Revision;
        int inventoryEventCount = 0;
        int serviceEventCount = 0;
        Action inventoryChanged = () => inventoryEventCount++;
        Action<WeaponComboGemChangedEvent> serviceChanged = _ => serviceEventCount++;
        inventory.Changed += inventoryChanged;
        WeaponComboGemEquipService.Changed += serviceChanged;
        try
        {
            WeaponComboGemEquipResult result = operation();
            Require(!result.Succeeded && result.FailureReason == expectedFailureReason,
                "예상한 해제 실패 사유가 아닙니다: " + result.FailureReason + " / expected=" + expectedFailureReason);
            ItemData current = weapon.GetWeaponComboGemAt(attackId, WeaponComboGemSlotRules.ElementSlotIndex);
            Require(current != null && current.IsSameRuntimeItem(installed), "실패 후 원본 콤보 장착 상태가 바뀌었습니다.");
            Require(!inventory.ContainsItem(installed), "실패 후 보석이 인벤토리에 중복 생성되었습니다.");
            Require(WeaponComboGemEquipService.Revision == revisionBefore, "실패 후 revision이 바뀌었습니다.");
            Require(inventoryEventCount == 0 && serviceEventCount == 0, "실패 후 event가 발생했습니다.");
        }
        finally
        {
            inventory.Changed -= inventoryChanged;
            WeaponComboGemEquipService.Changed -= serviceChanged;
        }
    }

    private static ItemData CreateWeaponWithInstalledGem(
        WeaponItemData weaponData,
        string attackId,
        ElementComboGemItemData fireGemData,
        out ItemData installed)
    {
        ItemData weapon = new ItemData(weaponData, 1, ItemGrade.Common);
        installed = new ItemData(fireGemData, 1, ItemGrade.Common);
        Require(weapon.TryAssignWeaponComboGemReference(
            attackId,
            WeaponComboGemSlotRules.ElementSlotIndex,
            installed), "검증용 보석 장착 실패");
        return weapon;
    }

    private static void ClearInventory(PlayerInventory inventory)
    {
        for (int i = 0; i < inventory.UnlockedSlotCount; i++)
        {
            if (inventory.GetItemAt(i) != null)
                Require(inventory.SetItemAt(i, null), "검증용 인벤토리 초기화 실패: " + i);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
