using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public static class SimpleItemTooltipBuilder // 기본 툴팁 생성
{
    private const string DisabledOptionColor = "#8A8A8A"; // 비활성 옵션색

    public static string Build(ItemData item)
    {
        if (item == null || item.baseData == null)
            return string.Empty;

        if (item.baseData is WeaponItemData weaponData)
            return BuildWeaponTooltip(item, weaponData);

        if (item.baseData is GearItemData gearData)
            return BuildGearTooltip(item, gearData);


        if (item.baseData is BagItemData bagData)
            return BuildBagTooltip(item, bagData);

        if (item.baseData is FlaskItemData flaskData)
            return item.itemName + "\n" + FlaskTooltip.Subtitle(flaskData) + "\n"
                + "아이템 레벨 " + OverburstGrowthRules.ClampLevel(item.level) + "\n"
                + FlaskTooltip.Status(item) + "\n\n" + FlaskTooltip.Details(item);

        if (item.baseData is ConsumableItemData consumableData)
            return BuildConsumableTooltip(item, consumableData);

        if (item.baseData is CurrencyItemData currencyData)
            return BuildCurrencyTooltip(item, currencyData);

        if (item.baseData is MapItemData)
            return BuildMapTooltip(item);

        return BuildDefaultTooltip(item);
    }

    private static string BuildWeaponTooltip(ItemData item, WeaponItemData weaponData)
    {
        StringBuilder builder = new StringBuilder(); // 툴팁 본문
        AppendTitle(builder, item, item.itemName, false);
        AppendSubtitle(builder, GetWeaponSubtitle(weaponData));
        AppendItemLevel(builder, item);
        AppendDescription(builder, item);

        WeaponFinalStats baseStats = WeaponStatCalculator.CalculateWeaponBase(item); // 기본 스탯
        WeaponFinalStats finalStats = WeaponStatCalculator.Calculate(item); // 최종 스탯

        AppendDivider(builder);
        if (weaponData.combatDefinition.usage.attackType == WeaponAttackType.MeleeSlash)
        {
            AppendMeleeWeaponGradeStatLinesFixed(builder, item, baseStats, finalStats);
            AppendMeleeDps(builder, weaponData, finalStats);
        }
        else if (IsMagicWeapon(weaponData))
            AppendMagicWeaponGradeStatLinesFixed(builder, item, baseStats, finalStats);
        else
            AppendWeaponGradeStatLinesFixed(builder, item, baseStats, finalStats);
        builder.Append("원소 방출 기본 위력 ")
            .Append(FormatOneDecimal(WeaponStatCalculator.GetElementalDischargePower(item)))
            .AppendLine();
        AppendPrice(builder, item);
        return builder.ToString();
    }

    private static string BuildGearTooltip(ItemData item, GearItemData data)
    {
        item.EnsureRuntimeState();
        var builder = new StringBuilder();
        AppendTitle(builder, item, item.itemName, false);
        AppendSubtitle(builder, data.kind == GearKind.Helmet ? "방어구 / 투구"
            : data.kind == GearKind.Chest ? "방어구 / 흉갑"
            : data.kind == GearKind.Gloves ? "방어구 / 장갑"
            : data.kind == GearKind.Boots ? "방어구 / 신발"
            : data.kind == GearKind.Earring ? "장신구 / 귀걸이" : "장신구 / 목걸이");
        AppendItemLevel(builder, item);
        AppendDescription(builder, item);
        AppendDivider(builder);
        for (int i = 0; i < item.gearRolls.Count; i++)
        {
            GearStatRoll roll = item.gearRolls[i];
            builder.Append(i == 0 ? "주능력치  " : "보조능력치  ");
            float value = GearQuality.Value(item, roll);
            builder.Append(GearStatLabel(roll.stat)).Append(value < 0f ? " " : " +")
                .Append(value.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(roll.stat == GearStat.MaxHealth || roll.stat == GearStat.Armor || roll.stat == GearStat.Attack ? ""
                    : roll.stat == GearStat.CriticalChance || roll.stat == GearStat.AttackSpeed || roll.stat == GearStat.CriticalDamage ? "%p" : "%");
            if (roll.stars != null && roll.stars.Count > 0)
            {
                builder.Append("  <size=60%>");
                foreach (WeaponGradeStarType star in roll.stars)
                    builder.Append("<color=")
                        .Append(star == WeaponGradeStarType.Red ? "#D76A63"
                            : star == WeaponGradeStarType.Yellow ? "#D2A85D" : star == WeaponGradeStarType.Green ? "#68AA84" : "#D5D8D8")
                        .Append(">◆</color>");
                builder.Append("</size>");
            }
            builder.AppendLine();
        }
        AppendPrice(builder, item);
        return builder.ToString();
    }

    private static string GearStatLabel(GearStat stat)
    {
        switch (stat)
        {
            case GearStat.MaxHealth: return "최대 체력";
            case GearStat.Armor: return "방어력";
            case GearStat.Attack: return "공격력";
            case GearStat.CriticalChance: return "치명타 확률";
            case GearStat.AttackSpeed: return "공격 속도";
            case GearStat.NormalDamage: return "일반 몬스터 피해";
            case GearStat.WeakDamage: return "약공 피해";
            case GearStat.HeavyDamage: return "강공 피해";
            case GearStat.EliteBossDamage: return "정예·보스 피해";
            case GearStat.ElementalDamage: return "원소 피해";
            default: return "치명타 피해";
        }
    }

    private static void AppendItemLevel(StringBuilder builder, ItemData item)
    {
        builder.Append("아이템 레벨 ").Append(OverburstGrowthRules.ClampLevel(item.level)).AppendLine();
    }

    private static string GetWeaponSubtitle(WeaponItemData weaponData)
    {
        string categoryName = weaponData.combatDefinition.usage.attackType == WeaponAttackType.MeleeSlash // 무기 분류명
            ? "근접무기"
            : IsMagicWeapon(weaponData)
                ? "마법무기"
            : ItemTooltipFormatter.GetWeaponFamilyName(weaponData.CombatFamily);

        return categoryName + " / " + ItemTooltipFormatter.GetWeaponClassName(weaponData.weaponClass);
    }

    private static bool IsMagicWeapon(WeaponItemData weaponData)
    {
        return weaponData != null && (weaponData.CombatFamily == WeaponCombatFamily.Magic || weaponData.combatDefinition.usage.attackType == WeaponAttackType.Chain);
    }

    private static string BuildBagTooltip(ItemData item, BagItemData bagData)
    {
        item.EnsureRuntimeState(); // 가방 옵션 보정
        StringBuilder builder = new StringBuilder(); // 툴팁 본문
        AppendTitle(builder, item, item.itemName, false);
        AppendSubtitle(builder, "가방 / 수납");
        AppendDivider(builder);
        builder.Append("인벤토리 슬롯 +").Append(Mathf.Max(0, bagData.additionalSlots)).AppendLine();
        AppendBagOptions(builder, item);
        AppendDivider(builder);
        builder.Append("가치 : ").Append(item.baseData.sellPrice).Append("G");
        return builder.ToString();
    }

    private static void AppendBagOptions(StringBuilder builder, ItemData item)
    {
        if (builder == null || item == null || item.bagOptions == null || item.bagOptions.Count == 0)
            return;

        builder.Append("추가 옵션").AppendLine();
        for (int i = 0; i < item.bagOptions.Count; i++)
        {
            BagRandomOptionRoll option = item.bagOptions[i];
            if (option == null)
                continue;

            builder.Append("- ");
            builder.Append(ItemTooltipFormatter.FormatBagOptionWithRollRange(option, item.grade, DisabledOptionColor));
            builder.AppendLine();
        }
    }

    private static string BuildDefaultTooltip(ItemData item)
    {
        StringBuilder builder = new StringBuilder(); // 툴팁 본문
        AppendTitle(builder, item, item.itemName, item.level > 0);
        AppendDescription(builder, item);
        AppendPrice(builder, item);
        return builder.ToString();
    }

    private static string BuildMapTooltip(ItemData item)
    {
        var builder = new StringBuilder();
        AppendTitle(builder, item, item.itemName, true);
        AppendSubtitle(builder, "지도 / " + MapThemeCatalog.DisplayName(item.mapState?.monsterThemeId));
        AppendDescription(builder, item);
        builder.Append("획득 경험치 +")
            .Append(Mathf.RoundToInt((MapOptionPolicy.ExperienceMultiplier(item.mapState) - 1f) * 100f))
            .AppendLine("%");
        builder.Append("장비·물약 고등급 보정 +")
            .Append((MapOptionPolicy.HighGradeRollBias(item.grade) * 100f).ToString("0.#"))
            .AppendLine("%");
        if (item.mapState?.options != null)
            foreach (var option in item.mapState.options)
            {
                string text = MapOptionPolicy.Describe(option);
                if (!string.IsNullOrEmpty(text)) builder.AppendLine(text);
            }
        return builder.ToString();
    }

    private static string BuildCurrencyTooltip(ItemData item, CurrencyItemData currencyData)
    {
        StringBuilder builder = new StringBuilder(); // 툴팁 본문
        AppendTitle(builder, item, item.itemName, false);
        AppendSubtitle(builder, "재화 / " + GetCurrencyDisplayName(currencyData.currencyType));
        AppendDescription(builder, item);
        builder.AppendLine();
        builder.Append("최대 스택 ").Append(Mathf.Max(1, currencyData.maxStack));
        return builder.ToString();
    }

    private static string BuildConsumableTooltip(ItemData item, ConsumableItemData consumableData)
    {
        StringBuilder builder = new StringBuilder(); // 툴팁 본문
        AppendTitle(builder, item, item.itemName, false);
        AppendSubtitle(builder, "소비아이템 / " + GetConsumableSubtypeName(consumableData));
        AppendDivider(builder);
        builder.AppendLine(BuildConsumableEffectText(consumableData));
        AppendDivider(builder);
        if (consumableData.duration > 0f)
            builder.Append("지속시간 ").Append(ItemTooltipFormatter.FormatSeconds(consumableData.duration)).AppendLine();
        else if (consumableData.consumableType == ConsumableType.HealHp)
            builder.AppendLine("즉시 회복");
        if (consumableData.cooldown > 0f)
            builder.Append("쿨타임 ").Append(ItemTooltipFormatter.FormatSeconds(consumableData.cooldown)).AppendLine();
        if (consumableData.IsPermanentSingleItem)
            builder.AppendLine("소모 없음");
        if (consumableData.IsPermanentSingleItem)
            builder.AppendLine("단일 아이템");
        else
            builder.Append("최대 스택 ").Append(Mathf.Max(1, consumableData.maxStack)).AppendLine();
        AppendDivider(builder);
        builder.Append("가치 ").Append(item.baseData.sellPrice).Append("G");
        return builder.ToString();
    }

    private static void AppendTitle(StringBuilder builder, ItemData item, string displayName, bool includeLevel)
    {
        string gradeColor = GradeConfig.GetGradeColorHex(item.grade); // 등급색
        builder.Append("<color=").Append(gradeColor).Append(">");
        builder.Append("[");
        builder.Append(ItemTooltipFormatter.GetGradeName(item.grade));
        builder.Append("]");

        if (!string.IsNullOrEmpty(displayName))
            builder.Append(" ").Append(displayName);

        if (includeLevel && item.level > 0)
            builder.Append(" Lv.").Append(item.level);

        if (item.ShouldDisplayStackCount)
            builder.Append(" x").Append(item.stackCount);

        builder.Append("</color>");
        builder.AppendLine();
    }

    private static void AppendSubtitle(StringBuilder builder, string subtitle)
    {
        if (string.IsNullOrEmpty(subtitle))
            return;

        builder.Append("<size=85%>");
        builder.Append(subtitle);
        builder.Append("</size>");
        builder.AppendLine();
    }

    private static void AppendDescription(StringBuilder builder, ItemData item)
    {
        if (string.IsNullOrEmpty(item.baseData.description))
            return;

        builder.AppendLine();
        builder.AppendLine(item.baseData.description);
    }

    private static void AppendDivider(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("<color=#7A6A4B>--------------------------</color>");
    }

    private static string BuildConsumableEffectText(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return string.Empty;

        switch (consumableData.consumableType)
        {
            case ConsumableType.SpeedBoost:
                return "이동속도 +" + ItemTooltipFormatter.FormatNumber(GetMoveSpeedBonusPercent(consumableData)) + "%";
            case ConsumableType.HealHp:
                return "체력 +" + FormatHealValue(consumableData.effectValue);
            default:
                return string.IsNullOrEmpty(consumableData.description) ? "사용 효과" : consumableData.description;
        }
    }

    private static string GetConsumableSubtypeName(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return "소비";

        switch (consumableData.consumableType)
        {
            case ConsumableType.SpeedBoost: return "물약";
            case ConsumableType.HealHp: return "물약";
            case ConsumableType.Invincible: return "방어";
            case ConsumableType.TimeStop: return "시간";
            default: return "소비";
        }
    }

    private static string GetCurrencyDisplayName(CurrencyType type)
    {
        switch (type)
        {
            case CurrencyType.Gold: return "골드";
            case CurrencyType.MapFragment: return "지도조각";
            default: return "재화";
        }
    }

    private static float GetMoveSpeedBonusPercent(ConsumableItemData consumableData)
    {
        float multiplier = consumableData.moveSpeedMultiplier > 0f ? consumableData.moveSpeedMultiplier : consumableData.effectValue;
        if (multiplier <= 0f)
            return 0f;

        return Mathf.Max(0f, (multiplier - 1f) * 100f);
    }

    private static string FormatHealValue(float value)
    {
        if (value > 0f && value <= 1f)
            return ItemTooltipFormatter.FormatNumber(value * 100f) + "%";

        return ItemTooltipFormatter.FormatNumber(value);
    }

    private const char GradeStarMarker = '★';

    private static void AppendWeaponGradeStatLinesFixed(StringBuilder builder, ItemData item, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Damage, "데미지", baseStats.damage, finalStats.damage, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Rpm, "RPM", ItemTooltipFormatter.ToRpm(baseStats.attackInterval), ItemTooltipFormatter.ToRpm(finalStats.attackInterval), FormatZeroDecimal);
        AppendWeaponIntFixed(builder, item, WeaponGradeStatType.MagazineSize, "장탄수", baseStats.magazineSize, finalStats.magazineSize);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.ReloadDuration, "재장전 시간", baseStats.reloadDuration, finalStats.reloadDuration, FormatSecondsTwoDecimals);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Range, "사거리", baseStats.range, finalStats.range, FormatMetersOneDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Recoil, "반동", baseStats.recoil, finalStats.recoil, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.RecoilRecovery, "반동 회복", baseStats.recoilRecoverySpeed, finalStats.recoilRecoverySpeed, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritChance, "치명타 확률", baseStats.critChance, finalStats.critChance, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritDamage, "치명타 피해", baseStats.critDamageMultiplier, finalStats.critDamageMultiplier, FormatMultiplierAsPercent);
    }

    private static void AppendMeleeWeaponGradeStatLinesFixed(StringBuilder builder, ItemData item, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Damage, "데미지", baseStats.damage, finalStats.damage, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.AttackSpeed, "공격속도", baseStats.meleeAttackSpeedMultiplier * 100f, finalStats.meleeAttackSpeedMultiplier * 100f, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, ResolveMeleeRangeGradeStatType(), "공격 범위", baseStats.range, finalStats.range, FormatMetersOneDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritChance, "치명타확률", baseStats.critChance, finalStats.critChance, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritDamage, "치명타피해", baseStats.critDamageMultiplier, finalStats.critDamageMultiplier, FormatMultiplierAsPercent);
        AppendWeaponFloatNoStarsFixed(builder, "넉백", baseStats.knockback, finalStats.knockback, FormatZeroDecimal);
    }

    private static void AppendMeleeDps(
        StringBuilder builder,
        WeaponItemData weaponData,
        WeaponFinalStats finalStats)
    {
        MeleeSingleTargetDpsEstimate estimate = MeleeSingleTargetDpsCalculator.Estimate(weaponData, finalStats);
        if (estimate.IsValid)
            builder.Append("단일 DPS ").Append(FormatOneDecimal(estimate.Dps)).AppendLine();
    }

    private static WeaponGradeStatType ResolveMeleeRangeGradeStatType()
    {
        return WeaponGradeStatType.AttackRange;
    }

    private static void AppendMagicWeaponGradeStatLinesFixed(StringBuilder builder, ItemData item, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Damage, "데미지", baseStats.damage, finalStats.damage, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Rpm, "쿨타임", baseStats.attackInterval, finalStats.attackInterval, FormatSecondsTwoDecimals);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Range, "사거리", baseStats.range, finalStats.range, FormatMetersOneDecimal);
        AppendWeaponFloatNoStarsFixed(builder, "투사체속도", baseStats.projectileSpeed, finalStats.projectileSpeed, FormatZeroDecimal);
        WeaponItemData weaponData = item != null ? item.baseData as WeaponItemData : null;
        if (weaponData == null || weaponData.combatDefinition.usage.attackType != WeaponAttackType.Chain)
        {
            AppendWeaponFloatNoStarsFixed(builder, "투사체크기", baseStats.projectileSize, finalStats.projectileSize, FormatOneDecimal);
            AppendWeaponFloatNoStarsFixed(builder, "폭발범위", baseStats.explosionRadius, finalStats.explosionRadius, FormatMetersOneDecimal);
        }
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritChance, "치명타확률", baseStats.critChance, finalStats.critChance, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritDamage, "치명타피해", baseStats.critDamageMultiplier, finalStats.critDamageMultiplier, FormatMultiplierAsPercent);
        AppendWeaponFloatNoStarsFixed(builder, "넉백", baseStats.knockback, finalStats.knockback, FormatZeroDecimal);
    }

    private static void AppendWeaponFloatNoStarsFixed(StringBuilder builder, string label, float baseValue, float finalValue, System.Func<float, string> formatter)
    {
        builder.Append(label).Append(" ");

        if (!Mathf.Approximately(baseValue, finalValue))
            builder.Append("(").Append(formatter(baseValue)).Append(" -> ").Append(formatter(finalValue)).Append(")");
        else
            builder.Append(formatter(baseValue));

        builder.AppendLine();
    }

    private static void AppendWeaponFloatFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType, string label, float baseValue, float finalValue, System.Func<float, string> formatter)
    {
        builder.Append(label).Append(" ");

        if (!Mathf.Approximately(baseValue, finalValue))
            builder.Append("(").Append(formatter(baseValue)).Append(" -> ").Append(formatter(finalValue)).Append(")");
        else
            builder.Append(formatter(baseValue));

        AppendStarTextFixed(builder, item, statType);
        builder.AppendLine();
    }

    private static void AppendWeaponIntFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType, string label, int baseValue, int finalValue)
    {
        builder.Append(label).Append(" ");

        if (baseValue != finalValue)
            builder.Append("(").Append(baseValue).Append(" -> ").Append(finalValue).Append(")");
        else
            builder.Append(baseValue);

        AppendStarTextFixed(builder, item, statType);
        builder.AppendLine();
    }

    private static void AppendStarTextFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType)
    {
        WeaponGradeStatRoll roll = WeaponGradeStatRoller.GetRoll(item != null ? item.weaponGradeStats : null, statType); // 별 롤
        if (roll == null || !roll.HasStars)
            return;

        List<WeaponGradeStarRoll> stars = roll.GetDisplayStars();
        if (stars.Count <= 0)
            return;

        builder.Append(" ");
        for (int i = 0; i < stars.Count; i++)
        {
            WeaponGradeStarType starType = stars[i] != null ? stars[i].starType : WeaponGradeStarType.White;
            builder.Append("<color=").Append(GetGradeStarTextColor(starType)).Append(">");
            builder.Append(GradeStarMarker);
            builder.Append("</color>");
        }
    }

    private static string GetGradeStarTextColor(WeaponGradeStarType starType)
    {
        switch (starType)
        {
            case WeaponGradeStarType.Green: return "#59FF59";
            case WeaponGradeStarType.Yellow: return "#FFD84A";
            case WeaponGradeStarType.Red: return "#FF4A4A";
            default: return "#F2F2F2";
        }
    }

    private static string FormatZeroDecimal(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatMeleeAttackRange(float value)
    {
        return Mathf.Approximately(value, Mathf.Round(value))
            ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string FormatOneDecimal(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string FormatSecondsTwoDecimals(float value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture) + "초";
    }

    private static string FormatMetersOneDecimal(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture) + "m";
    }

    private static string FormatPercentZeroDecimal(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatMultiplierAsPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private static void AppendPrice(StringBuilder builder, ItemData item)
    {
        AppendDivider(builder);
        builder.Append("가치 : ").Append(item.baseData.sellPrice).Append("G");
    }

}


