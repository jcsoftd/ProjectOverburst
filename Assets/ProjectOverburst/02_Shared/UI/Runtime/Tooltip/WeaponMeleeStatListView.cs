using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WeaponMeleeStatListView : MonoBehaviour // 미확장 밀리 능력치 행
{
    public const int AuthoredRowCount = 7;

    private const string ChangedValueColor = "#FFD75A";
    private const float LabelWidth = 82f;
    private const float ValueWidth = 54f;
    private const float RowSpacing = 4f;
    private const float ImageStarPitch = 14f;
    private const float ImageStarEndCap = 1f;

    [SerializeField] private GameObject[] rowRoots; // 정식 능력치 행
    [SerializeField] private TextMeshProUGUI[] rowLabels; // 항목명
    [SerializeField] private TextMeshProUGUI[] rowValues; // 계산값
    [SerializeField] private WeaponGradeStarStrip[] rowStarStrips; // 별 표시

    private WeaponGradeStarSpriteSet spriteSet;
    private bool missingViewLogged;

    public float PreferredContentWidth { get; private set; } = LabelWidth + ValueWidth + RowSpacing * 2f;

    public bool HasAuthoredView
    {
        get
        {
            if (!HasRows(rowRoots)
                || !HasRows(rowLabels)
                || !HasRows(rowValues)
                || !HasRows(rowStarStrips))
            {
                return false;
            }

            for (int i = 0; i < AuthoredRowCount; i++)
            {
                if (!rowStarStrips[i].HasAuthoredView)
                    return false;
            }

            return true;
        }
    }

    public void Initialize(WeaponGradeStarSpriteSet stars)
    {
        spriteSet = stars;

        if (!HasAuthoredView)
            LogMissingAuthoredView();
    }

    public void SetContent(ItemData item, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        if (!HasAuthoredView)
        {
            LogMissingAuthoredView();
            return;
        }

        PreferredContentWidth = LabelWidth + ValueWidth + RowSpacing * 2f;
        rowRoots[6].SetActive(true);

        SetRow(0, item, WeaponGradeStatType.Damage, "데미지", baseStats.damage, finalStats.damage, FormatZeroDecimal, true);
        SetRow(1, item, WeaponGradeStatType.AttackSpeed, "공격 속도", baseStats.meleeAttackSpeedMultiplier * 100f, finalStats.meleeAttackSpeedMultiplier * 100f, FormatPercentZeroDecimal, true);
        SetRow(2, item, WeaponGradeStatType.AttackRange, "공격 범위", baseStats.range, finalStats.range, FormatMetersOneDecimal, true);
        SetRow(3, item, WeaponGradeStatType.CritChance, "치명타 확률", baseStats.critChance, finalStats.critChance, FormatPercentZeroDecimal, true);
        SetRow(4, item, WeaponGradeStatType.CritDamage, "치명타 피해", baseStats.critDamageMultiplier, finalStats.critDamageMultiplier, FormatMultiplierAsPercent, true);
        SetRow(5, item, WeaponGradeStatType.Damage, "넉백", baseStats.knockback, finalStats.knockback, FormatZeroDecimal, false);

        WeaponItemData weaponData = item != null ? item.baseData as WeaponItemData : null;
        MeleeSingleTargetDpsEstimate baseDps = MeleeSingleTargetDpsCalculator.Estimate(weaponData, baseStats);
        MeleeSingleTargetDpsEstimate finalDps = MeleeSingleTargetDpsCalculator.Estimate(weaponData, finalStats);
        SetRow(6, item, WeaponGradeStatType.Damage, "단일 DPS", baseDps.Dps, finalDps.Dps, FormatOneDecimal, false);
    }

    private void SetRow(
        int index,
        ItemData item,
        WeaponGradeStatType statType,
        string label,
        float baseValue,
        float finalValue,
        Func<float, string> formatter,
        bool showStars)
    {
        rowLabels[index].text = label;
        rowValues[index].text = FormatComparedValue(baseValue, finalValue, formatter);

        System.Collections.Generic.List<WeaponGradeStarRoll> stars = null;
        if (showStars)
        {
            WeaponGradeStatRoll roll = WeaponGradeStatRoller.GetRoll(item != null ? item.weaponGradeStats : null, statType);
            if (roll != null && roll.HasStars)
                stars = roll.GetDisplayStars();
        }

        rowStarStrips[index].SetStars(stars, spriteSet);
        int starCount = stars != null ? stars.Count : 0;
        float starStripWidth = starCount > 0 ? starCount * ImageStarPitch + ImageStarEndCap : 0f;
        PreferredContentWidth = Mathf.Max(
            PreferredContentWidth,
            LabelWidth + ValueWidth + RowSpacing * 2f + starStripWidth);
    }

    private static bool HasRows<T>(T[] values) where T : UnityEngine.Object
    {
        if (values == null || values.Length != AuthoredRowCount)
            return false;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
                return false;
        }

        return true;
    }

    private void LogMissingAuthoredView()
    {
        if (missingViewLogged)
            return;

        missingViewLogged = true;
        Debug.LogError(
            "[WeaponMeleeStatListView] 정식 능력치 행 참조가 없습니다. Tooltip View Objectizer를 실행하세요.",
            this);
    }

    private static string FormatComparedValue(float baseValue, float finalValue, Func<float, string> formatter)
    {
        if (Mathf.Approximately(baseValue, finalValue))
            return formatter(baseValue);

        return "(" + formatter(baseValue) + " → <color=" + ChangedValueColor + ">" + formatter(finalValue) + "</color>)";
    }

    private static string FormatZeroDecimal(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatOneDecimal(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string FormatPercentZeroDecimal(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatMetersOneDecimal(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture) + "m";
    }

    private static string FormatMultiplierAsPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
