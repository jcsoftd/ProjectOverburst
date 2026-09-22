public static class WeaponContentPolicy
{
    public static bool IsActiveWeapon(WeaponItemData weaponData)
    {
        if (weaponData == null)
            return false;

        return weaponData.CombatFamily == WeaponCombatFamily.Melee
            && weaponData.combatDefinition.usage.attackType == WeaponAttackType.MeleeSlash
            && weaponData.GetMeleeDefinition() != null;
    }

    public static bool IsAllowedItemData(BaseItemData itemData)
    {
        if (itemData == null)
            return false;

        if (itemData is ElementComboGemItemData) return false; // retired elemental gems

        if (itemData is WeaponItemData weaponData)
            return IsActiveWeapon(weaponData);

        return true;
    }

    public static bool IsAllowedRuntimeItem(ItemData item)
    {
        return item != null && (item.baseData is ElementComboGemItemData || IsAllowedItemData(item.baseData)); // preserve existing ownership until migration is decided
    }
}
