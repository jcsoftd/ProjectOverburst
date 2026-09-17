using UnityEngine;

[CreateAssetMenu(
    fileName = "MagicWeaponDefinition",
    menuName = "OVERBURST/Weapons/Magic Weapon Definition")]
public sealed class MagicWeaponDefinition : WeaponCombatDefinition
{
    public override WeaponCombatFamily Family => WeaponCombatFamily.Magic;

    [Header("마법 무기 설정")]
    public MagicWeaponSettings magic;
}
