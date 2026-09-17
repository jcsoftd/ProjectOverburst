using UnityEngine;

[CreateAssetMenu(fileName = "NewElementComboGem", menuName = "Items/Combo Gems/Element Gem")]
public sealed class ElementComboGemItemData : ComboGemItemData // 속성 콤보 보석
{
    [InspectorName("부여 원소")]
    public WeaponElement element = WeaponElement.None; // 고정 원소 정체성

    public override ComboGemType GemType { get { return ComboGemType.Element; } }
    public override bool HasValidDefinition { get { return element != WeaponElement.None; } }

    public override Sprite GetIcon(ItemGrade grade)
    {
        int index = (int)grade;
        if (gradeIcons != null && index >= 0 && index < gradeIcons.Length)
            return gradeIcons[index]; // Common·Cursed null 보존

        return icon;
    }

    public bool TryGetElementDefinition(out WeaponElement weaponElement)
    {
        weaponElement = element;
        return HasValidDefinition;
    }

    protected override bool IsOptionAllowed(ComboGemRandomOptionType optionType)
    {
        return optionType == ComboGemRandomOptionType.ElementDamageIncrease;
    }
}
