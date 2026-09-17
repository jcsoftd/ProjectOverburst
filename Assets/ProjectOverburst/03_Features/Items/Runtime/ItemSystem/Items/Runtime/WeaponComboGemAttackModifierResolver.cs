public enum WeaponComboGemAttackModifierResolveFailureReason // 전투 조회 실패 이유
{
    None = 0,
    InvalidWeapon = 1,
    InvalidWeaponComboData = 2,
    InvalidAttackId = 3,
    InvalidLoadoutData = 4,
    InvalidElementSlotData = 5,
    InvalidElementGem = 6,
    InvalidElementGemRuntimeState = 7,
    InvalidElementDamageOption = 8
}

public readonly struct WeaponComboGemAttackModifierSnapshot // 공격 시작 시 고정값
{
    public string AttackId { get; }
    public WeaponElement Element { get; }
    public float ElementDamageIncreasePercent { get; }
    public float DamageMultiplier { get; }
    public bool HasElement { get; }
    public string SourceGemRuntimeInstanceId { get; }

    private WeaponComboGemAttackModifierSnapshot(
        string attackId,
        WeaponElement element,
        float elementDamageIncreasePercent,
        string sourceGemRuntimeInstanceId)
    {
        AttackId = attackId;
        Element = element;
        ElementDamageIncreasePercent = elementDamageIncreasePercent;
        DamageMultiplier = 1f + elementDamageIncreasePercent / 100f;
        HasElement = element != WeaponElement.None;
        SourceGemRuntimeInstanceId = sourceGemRuntimeInstanceId ?? string.Empty;
    }

    internal static WeaponComboGemAttackModifierSnapshot Physical(string attackId)
    {
        return new WeaponComboGemAttackModifierSnapshot(
            attackId,
            WeaponElement.None,
            0f,
            string.Empty);
    }

    internal static WeaponComboGemAttackModifierSnapshot Elemental(
        string attackId,
        WeaponElement element,
        float elementDamageIncreasePercent,
        string sourceGemRuntimeInstanceId)
    {
        return new WeaponComboGemAttackModifierSnapshot(
            attackId,
            element,
            elementDamageIncreasePercent,
            sourceGemRuntimeInstanceId);
    }
}

public static class WeaponComboGemAttackModifierResolver // 70번대 전투 조회 계약
{
    public static bool TryResolve(
        ItemData weapon,
        string attackId,
        out WeaponComboGemAttackModifierSnapshot snapshot,
        out WeaponComboGemAttackModifierResolveFailureReason failureReason)
    {
        snapshot = default;
        failureReason = WeaponComboGemAttackModifierResolveFailureReason.None;

        if (weapon == null || !(weapon.baseData is WeaponItemData))
            return Fail(WeaponComboGemAttackModifierResolveFailureReason.InvalidWeapon, out failureReason);

        if (string.IsNullOrWhiteSpace(attackId))
            return Fail(WeaponComboGemAttackModifierResolveFailureReason.InvalidAttackId, out failureReason);

        int attackCount = weapon.GetWeaponComboGemLoadoutCount();
        if (attackCount <= 0)
            return Fail(WeaponComboGemAttackModifierResolveFailureReason.InvalidWeaponComboData, out failureReason);

        bool hasAttackId = false;
        for (int stepIndex = 0; stepIndex < attackCount; stepIndex++)
        {
            if (!weapon.TryGetWeaponComboAttackId(stepIndex, out string stableAttackId))
                return Fail(WeaponComboGemAttackModifierResolveFailureReason.InvalidWeaponComboData, out failureReason);

            if (string.Equals(stableAttackId, attackId, System.StringComparison.Ordinal))
                hasAttackId = true;
        }

        if (!hasAttackId)
            return Fail(WeaponComboGemAttackModifierResolveFailureReason.InvalidAttackId, out failureReason);

        if (!TryFindLoadout(weapon, attackId, out WeaponComboGemLoadout loadout))
            return Fail(WeaponComboGemAttackModifierResolveFailureReason.InvalidLoadoutData, out failureReason);

        if (loadout.SlotCount != WeaponComboGemSlotRules.SlotCapacity
            || !WeaponComboGemSlotRules.IsValidUnlockedSlotCount(loadout.UnlockedSlotCount)
            || !loadout.IsSlotUnlocked(WeaponComboGemSlotRules.ElementSlotIndex))
        {
            return Fail(WeaponComboGemAttackModifierResolveFailureReason.InvalidElementSlotData, out failureReason);
        }

        ItemData elementGem = loadout.GetGemAt(WeaponComboGemSlotRules.ElementSlotIndex);
        if (elementGem == null)
        {
            snapshot = WeaponComboGemAttackModifierSnapshot.Physical(attackId); // 정상 무속성 공격
            return true;
        }

        if (!(elementGem.baseData is ElementComboGemItemData elementGemData)
            || !elementGemData.TryGetElementDefinition(out WeaponElement element)
            || element == WeaponElement.None)
        {
            return Fail(WeaponComboGemAttackModifierResolveFailureReason.InvalidElementGem, out failureReason);
        }

        if (string.IsNullOrWhiteSpace(elementGem.runtimeInstanceId))
        {
            return Fail(
                WeaponComboGemAttackModifierResolveFailureReason.InvalidElementGemRuntimeState,
                out failureReason);
        }

        if (!TryReadElementDamageIncrease(elementGem, out float damageIncreasePercent))
        {
            return Fail(
                WeaponComboGemAttackModifierResolveFailureReason.InvalidElementDamageOption,
                out failureReason);
        }

        snapshot = WeaponComboGemAttackModifierSnapshot.Elemental(
            attackId,
            element,
            damageIncreasePercent,
            elementGem.runtimeInstanceId); // 인스턴스 롤 고정
        return true;
    }

    private static bool TryFindLoadout(
        ItemData weapon,
        string attackId,
        out WeaponComboGemLoadout result)
    {
        result = null;
        if (weapon.weaponComboGemLoadouts == null)
            return false;

        for (int i = 0; i < weapon.weaponComboGemLoadouts.Count; i++)
        {
            WeaponComboGemLoadout loadout = weapon.weaponComboGemLoadouts[i];
            if (loadout == null)
                return false;

            if (!string.Equals(loadout.AttackId, attackId, System.StringComparison.Ordinal))
                continue;

            if (result != null)
                return false; // 중복 attackId 차단

            result = loadout;
        }

        return result != null;
    }

    private static bool TryReadElementDamageIncrease(ItemData elementGem, out float value)
    {
        value = 0f;
        if (elementGem.comboGemOptions == null)
            return false;

        bool found = false;
        for (int i = 0; i < elementGem.comboGemOptions.Count; i++)
        {
            ComboGemRolledOption option = elementGem.comboGemOptions[i];
            if (option == null)
                return false;

            if (option.optionType != ComboGemRandomOptionType.ElementDamageIncrease)
                continue;

            if (found || float.IsNaN(option.value) || float.IsInfinity(option.value) || option.value < 0f)
                return false;

            value = option.value;
            found = true;
        }

        return found;
    }

    private static bool Fail(
        WeaponComboGemAttackModifierResolveFailureReason reason,
        out WeaponComboGemAttackModifierResolveFailureReason failureReason)
    {
        failureReason = reason;
        return false;
    }
}
