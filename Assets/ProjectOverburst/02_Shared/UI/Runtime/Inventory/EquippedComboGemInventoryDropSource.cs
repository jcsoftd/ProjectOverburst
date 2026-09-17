using System;

public delegate WeaponComboGemEquipResult WeaponComboGemInventorySlotUnequipHandler(
    PlayerInventory inventory,
    ItemData weapon,
    string attackId,
    int comboGemSlotIndex,
    int targetInventorySlotIndex,
    ItemData expectedGem); // 90-70 지정 슬롯 연결

public sealed class EquippedComboGemInventoryDropSource // 장착 보석 드래그 출처
{
    private readonly WeaponComboGemInventorySlotUnequipHandler unequipHandler;

    public ItemData Weapon { get; }
    public string WeaponRuntimeInstanceId { get; }
    public string AttackId { get; }
    public int ComboGemSlotIndex { get; }
    public ItemData Gem { get; }
    public string GemRuntimeInstanceId { get; }

    public bool IsValid => Weapon != null
        && Weapon.itemType == "Weapon"
        && !string.IsNullOrEmpty(WeaponRuntimeInstanceId)
        && !string.IsNullOrWhiteSpace(AttackId)
        && ComboGemSlotIndex >= 0
        && Gem != null
        && Gem.baseData is ComboGemItemData
        && !string.IsNullOrEmpty(GemRuntimeInstanceId)
        && unequipHandler != null;

    public EquippedComboGemInventoryDropSource(
        ItemData weapon,
        string attackId,
        int comboGemSlotIndex,
        ItemData gem)
        : this(weapon, attackId, comboGemSlotIndex, gem, TryUnequipToInventorySlot)
    {
    }

    public EquippedComboGemInventoryDropSource(
        ItemData weapon,
        string attackId,
        int comboGemSlotIndex,
        ItemData gem,
        WeaponComboGemInventorySlotUnequipHandler handler)
    {
        Weapon = weapon;
        Weapon?.EnsureRuntimeInstanceId();
        WeaponRuntimeInstanceId = weapon?.runtimeInstanceId ?? string.Empty;
        AttackId = attackId ?? string.Empty;
        ComboGemSlotIndex = comboGemSlotIndex;
        Gem = gem;
        Gem?.EnsureRuntimeInstanceId();
        GemRuntimeInstanceId = gem?.runtimeInstanceId ?? string.Empty;
        unequipHandler = handler;
    }

    public bool MatchesCurrentSource()
    {
        ItemData installedGem = Weapon?.GetWeaponComboGemAt(AttackId, ComboGemSlotIndex);
        return IsValid
            && string.Equals(Weapon.runtimeInstanceId, WeaponRuntimeInstanceId, StringComparison.Ordinal)
            && string.Equals(Gem.runtimeInstanceId, GemRuntimeInstanceId, StringComparison.Ordinal)
            && Weapon.IsWeaponComboGemSlotUnlocked(AttackId, ComboGemSlotIndex)
            && installedGem != null
            && installedGem.IsSameRuntimeItem(Gem);
    }

    public WeaponComboGemEquipResult TryUnequip(PlayerInventory inventory, int targetInventorySlotIndex)
    {
        if (!MatchesCurrentSource())
            return default;

        return unequipHandler(
            inventory,
            Weapon,
            AttackId,
            ComboGemSlotIndex,
            targetInventorySlotIndex,
            Gem); // 지정 슬롯 원자 해제
    }

    private static WeaponComboGemEquipResult TryUnequipToInventorySlot(
        PlayerInventory inventory,
        ItemData weapon,
        string attackId,
        int comboGemSlotIndex,
        int targetInventorySlotIndex,
        ItemData expectedGem)
    {
        expectedGem?.EnsureRuntimeInstanceId();
        return WeaponComboGemEquipService.TryUnequip(
            inventory,
            weapon,
            attackId,
            comboGemSlotIndex,
            targetInventorySlotIndex,
            expectedGem != null ? expectedGem.runtimeInstanceId : string.Empty);
    }
}
