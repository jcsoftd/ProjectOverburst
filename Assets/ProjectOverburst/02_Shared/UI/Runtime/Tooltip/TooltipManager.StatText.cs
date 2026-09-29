using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// TooltipManager partial: 등급 스탯 비교 문구와 숫자 형식. 필드와 Unity 수명주기는 TooltipManager.cs에 있다.
public partial class TooltipManager
{
    private string BuildWeaponGradeStatComparisonFixed(ItemData item, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        StringBuilder builder = new StringBuilder();

        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Damage, "데미지", baseStats.damage, finalStats.damage, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Rpm, "RPM", ItemTooltipFormatter.ToRpm(baseStats.attackInterval), ItemTooltipFormatter.ToRpm(finalStats.attackInterval), FormatZeroDecimal);
        AppendWeaponIntFixed(builder, item, WeaponGradeStatType.MagazineSize, "장탄수", baseStats.magazineSize, finalStats.magazineSize);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.ReloadDuration, "재장전 시간", baseStats.reloadDuration, finalStats.reloadDuration, FormatSecondsTwoDecimals);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Range, "사거리", baseStats.range, finalStats.range, FormatMetersOneDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Recoil, "반동", baseStats.recoil, finalStats.recoil, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.RecoilRecovery, "반동 회복", baseStats.recoilRecoverySpeed, finalStats.recoilRecoverySpeed, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritChance, "치명타 확률", baseStats.critChance, finalStats.critChance, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritDamage, "치명타 피해", baseStats.critDamageMultiplier, finalStats.critDamageMultiplier, FormatMultiplierAsPercent);

        return builder.ToString().TrimEnd();
    }

    private void AppendWeaponFloatFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType, string label, float baseValue, float finalValue, System.Func<float, string> formatter)
    {
        builder.Append(label).Append(" ");

        if (!Mathf.Approximately(baseValue, finalValue))
            builder.Append("(").Append(formatter(baseValue)).Append(" -> <color=").Append(ChangedValueColor).Append(">").Append(formatter(finalValue)).Append("</color>)");
        else
            builder.Append(formatter(baseValue));

        AppendStarTextFixed(builder, item, statType);
        builder.AppendLine();
    }

    private void AppendWeaponIntFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType, string label, int baseValue, int finalValue)
    {
        builder.Append(label).Append(" ");

        if (baseValue != finalValue)
            builder.Append("(").Append(baseValue).Append(" -> <color=").Append(ChangedValueColor).Append(">").Append(finalValue).Append("</color>)");
        else
            builder.Append(baseValue);

        AppendStarTextFixed(builder, item, statType);
        builder.AppendLine();
    }

    private void AppendStarTextFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType)
    {
        WeaponGradeStatRoll roll = WeaponGradeStatRoller.GetRoll(item != null ? item.weaponGradeStats : null, statType); // 별 롤
        if (roll == null || !roll.HasStars)
            return;

        List<WeaponGradeStarRoll> stars = roll.GetDisplayStars();
        if (stars.Count <= 0)
            return;

        builder.Append(" <size=68%>");
        for (int i = 0; i < stars.Count; i++)
        {
            WeaponGradeStarType starType = stars[i] != null ? stars[i].starType : WeaponGradeStarType.White;
            builder.Append("<color=").Append(GetGradeStarTextColor(starType)).Append(">");
            builder.Append(GradeStarMarker);
            builder.Append("</color>");
        }
        builder.Append("</size>");
    }

    private static string GetGradeStarTextColor(WeaponGradeStarType starType)
    {
        switch (starType)
        {
            case WeaponGradeStarType.Green: return "#68AA84";
            case WeaponGradeStarType.Yellow: return "#D2A85D";
            case WeaponGradeStarType.Red: return "#AE5962";
            default: return "#D5D8D8";
        }
    }

    private string FormatZeroDecimal(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
    }

    private string FormatSecondsTwoDecimals(float value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture) + "초";
    }

    private string FormatMetersOneDecimal(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture) + "m";
    }

    private string FormatPercentZeroDecimal(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private string FormatMultiplierAsPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

}
