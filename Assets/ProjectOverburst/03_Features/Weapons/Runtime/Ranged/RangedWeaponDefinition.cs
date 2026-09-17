using UnityEngine;

[CreateAssetMenu(
    fileName = "RangedWeaponDefinition",
    menuName = "OVERBURST/Weapons/Ranged Weapon Definition")]
public sealed class RangedWeaponDefinition : WeaponCombatDefinition
{
    public override WeaponCombatFamily Family => WeaponCombatFamily.Ranged;
}
