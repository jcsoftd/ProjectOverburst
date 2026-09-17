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

        if (itemData is WeaponItemData weaponData)
            return IsActiveWeapon(weaponData);

        return true;
    }

    public static bool IsAllowedRuntimeItem(ItemData item)
    {
        return item != null && IsAllowedItemData(item.baseData);
    }
}
