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
        switch (element)
        {
            case WeaponElement.Fire: return "불";
            case WeaponElement.Ice: return "얼음";
            case WeaponElement.Electric: return "번개";
            case WeaponElement.Water: return "물";
            default: return "무속성";
        }
    }

    public static string FormatBagOption(BagRandomOptionRoll option)
    {
        if (option == null)
            return string.Empty;

        switch (option.optionType)
        {
            case BagRandomOptionType.MoveSpeedPercent:
                return "이동속도 +" + FormatNumber(option.value) + "%";
            case BagRandomOptionType.MaxStamina:
                return "스태미너 최대치 +" + Mathf.RoundToInt(option.value);
            case BagRandomOptionType.MaxHp:
                return "HP 최대치 +" + Mathf.RoundToInt(option.value);
            default:
                return "알 수 없는 옵션 +" + FormatNumber(option.value);
        }
    }

    public static string FormatBagOptionWithRollRange(BagRandomOptionRoll option, ItemGrade grade, string rangeColorHex)
    {
        string optionText = FormatBagOption(option); // 기본 문구
        if (option == null)
            return optionText;

        if (!BagRandomOptionRoller.TryGetValueRange(option.optionType, grade, out float minValue, out float maxValue))
            return optionText;

        string color = string.IsNullOrEmpty(rangeColorHex) ? "#8A8A8A" : rangeColorHex; // 범위 색
        return optionText + " <color=" + color + ">(" + FormatBagRollRange(option.optionType, minValue, maxValue) + ")</color>";
    }

    private static string FormatBagRollRange(BagRandomOptionType optionType, float minValue, float maxValue)
    {
        switch (optionType)
        {
            case BagRandomOptionType.MoveSpeedPercent:
                return FormatNumber(minValue) + "~" + FormatNumber(maxValue) + "%";
            case BagRandomOptionType.MaxStamina:
            case BagRandomOptionType.MaxHp:
                return Mathf.RoundToInt(minValue) + "~" + Mathf.RoundToInt(maxValue);
            default:
                return FormatNumber(minValue) + "~" + FormatNumber(maxValue);
        }
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

