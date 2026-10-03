using UnityEngine;

[CreateAssetMenu(fileName = "ElementGem", menuName = "Items/Overburst Element Gem")]
public sealed class ElementGemItemData : BaseItemData
{
    public WeaponElement element;
    public ItemGrade fixedGrade;
    public static bool IsAllowed(WeaponElement element, ItemGrade grade)
        => grade >= ItemGrade.Common && grade <= ItemGrade.Cursed
        && (element == WeaponElement.Fire || element == WeaponElement.Ice || element == WeaponElement.Electric
            || ((element == WeaponElement.Dark || element == WeaponElement.Light) && grade >= ItemGrade.Legendary));
}
