using System.Text;
using UnityEngine;

public static class ItemTooltipFormatter // 툴팁 포맷
{
    public static string GetGradeName(ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Common: return "일반";
            case ItemGrade.Uncommon: return "비범";
            case ItemGrade.Rare: return "희귀";
            case ItemGrade.Epic: return "영웅";
            case ItemGrade.Legendary: return "전설";
            case ItemGrade.Artifact: return "유물";
            case ItemGrade.Mythic: return "신화";
            case ItemGrade.Cursed: return "저주";
            default: return "미확인";
        }
    }

    public static string GetWeaponFamilyName(WeaponCombatFamily family)
    {
        switch (family)
        {
            case WeaponCombatFamily.Melee: return "근접";
            case WeaponCombatFamily.Magic: return "마법";
            case WeaponCombatFamily.Ranged: return "원거리";
            default: return "무기";
        }
    }

    public static string GetWeaponClassName(WeaponClass weaponClass)
    {
        switch (weaponClass)
        {
            case WeaponClass.Sword: return "검";
            case WeaponClass.Greatsword: return "대검";
            case WeaponClass.Orb: return "오브";
            default: return "무기";
        }
    }

    public static string GetWeaponElementName(WeaponElement element)
    {
        string label = OverburstElementRules.Label(OverburstElementRules.MigrateLegacy(element));
        return string.IsNullOrEmpty(label) ? "무속성" : label;
    }

    public static float ToRpm(float attackInterval)
    {
        if (attackInterval <= 0f)
            return 0f;

        return 60f / attackInterval;
    }

    public static string FormatNumber(float value)
    {
        return value.ToString("0.##");
    }

    public static string FormatInteger(int value)
    {
        return value.ToString();
    }

    public static string FormatPercentValue(float value)
    {
        return FormatNumber(value) + "%";
    }

    public static string FormatMultiplierPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }

    public static string FormatSeconds(float value)
    {
        return FormatNumber(value) + "초";
    }

    public static string FormatSecondsKorean(float value)
    {
        return FormatNumber(value) + "초";
    }

    public static string GetFireModeName(WeaponFireMode fireMode)
    {
        switch (fireMode)
        {
            case WeaponFireMode.Cooldown: return "쿨타임";
            case WeaponFireMode.None:
            default:
                return "불가";
        }
    }

    public static string FormatRpm(float value)
    {
        return Mathf.RoundToInt(value) + " RPM";
    }
}

