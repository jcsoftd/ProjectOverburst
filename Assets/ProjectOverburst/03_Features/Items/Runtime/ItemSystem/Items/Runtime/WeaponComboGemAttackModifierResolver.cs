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

// Compatibility name for existing attack snapshot callers. Element gems no longer influence combat.
public static class WeaponComboGemAttackModifierResolver
{
    public static bool TryResolve(ItemData weapon, string attackId, out WeaponComboGemAttackModifierSnapshot snapshot,
        out WeaponComboGemAttackModifierResolveFailureReason failureReason)
    {
        snapshot = default;
        failureReason = WeaponComboGemAttackModifierResolveFailureReason.InvalidWeapon;
        if (weapon == null || !(weapon.baseData is WeaponItemData data)) return false;
        MeleeComboDefinition combo = data.GetMeleeComboDefinition();
        failureReason = WeaponComboGemAttackModifierResolveFailureReason.InvalidWeaponComboData;
        if (combo == null || !combo.TryGetStableAttackIds(out string[] ids, out _)) return false;
        failureReason = WeaponComboGemAttackModifierResolveFailureReason.InvalidAttackId;
        if (string.IsNullOrWhiteSpace(attackId) || System.Array.IndexOf(ids, attackId) < 0) return false;
        WeaponElement element = weapon.ResolvedElement;
        snapshot = element == WeaponElement.None ? WeaponComboGemAttackModifierSnapshot.Physical(attackId)
            : WeaponComboGemAttackModifierSnapshot.Elemental(attackId, element, 0f, string.Empty);
        failureReason = WeaponComboGemAttackModifierResolveFailureReason.None;
        return true;
    }
}
