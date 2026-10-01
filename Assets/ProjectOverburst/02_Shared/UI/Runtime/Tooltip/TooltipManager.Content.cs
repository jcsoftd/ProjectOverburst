using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// TooltipManager partial: 무기·가방·소모품·재화별 내용과 가격. 필드와 Unity 수명주기는 TooltipManager.cs에 있다.
public partial class TooltipManager
{
    private void SetTooltipContent(ItemData item)
    {
        SetActive(nameText, false);
        SetActive(weaponHeaderRoot, true);
        SetActive(meleeStatListView, false);

        if (item.baseData is WeaponItemData weaponData)
        {
            SetWeaponTooltipContent(item, weaponData); // 무기 툴팁
            return;
        }

        if (item.baseData is BagItemData bagData)
        {
            SetBagTooltipContent(item, bagData); // 가방 툴팁
            return;
        }

        if (item.baseData is ConsumableItemData consumableData)
        {
            SetConsumableTooltipContent(item, consumableData); // 소비 툴팁
            return;
        }

        if (item.baseData is CurrencyItemData currencyData)
        {
            SetCurrencyTooltipContent(item, currencyData); // 재화 툴팁
            return;
        }

        SetDefaultTooltipContent(item); // 일반 툴팁
    }

    private void SetWeaponTooltipContent(ItemData item, WeaponItemData weaponData)
    {
        SetActive(dividerBasic, true);
        bool isMeleeSlash = weaponData.combatDefinition.usage.attackType == WeaponAttackType.MeleeSlash; // 근접 분기
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);

        SetActive(dividerPrice, true);
        SetActive(priceText, true);

        string categoryName = isMeleeSlash
            ? "근접무기"
            : ItemTooltipFormatter.GetWeaponFamilyName(weaponData.CombatFamily);
        SetWeaponHeader(item, weaponData, categoryName);

        WeaponFinalStats baseStats = WeaponStatCalculator.CalculateWeaponBase(item); // 기본 스탯
        WeaponFinalStats finalStats = WeaponStatCalculator.Calculate(item); // 최종 스탯

        SetActive(basicStatsText, !isMeleeSlash);
        SetActive(meleeStatListView, isMeleeSlash);
        if (isMeleeSlash)
        {
            basicStatsText.text = string.Empty;
            meleeStatListView.Initialize(weaponGradeStarSprites);
            meleeStatListView.SetContent(item, baseStats, finalStats);
        }
        else
        {
            if (flaskStatsFont == null)
                flaskStatsFont = Resources.Load<TMP_FontAsset>("UI/Tooltip/FlaskTooltipFont");
            if (flaskStatsFont != null)
                basicStatsText.font = flaskStatsFont;
            basicStatsText.text = BuildWeaponGradeStatComparisonFixed(item, baseStats, finalStats);
        }
        weaponStatsText.text = string.Empty;
        priceText.text = BuildPriceText(item);
    }

    private void SetWeaponHeader(ItemData item, WeaponItemData weaponData, string categoryName)
    {
        string displayName = item != null ? item.itemName : string.Empty;
        if (string.IsNullOrWhiteSpace(displayName) && weaponData != null)
            displayName = !string.IsNullOrWhiteSpace(weaponData.itemName) ? weaponData.itemName : weaponData.name;

        string weaponClassName = weaponData != null
            ? ItemTooltipFormatter.GetWeaponClassName(weaponData.weaponClass)
            : string.Empty;
        string elementName = item != null ? GetWeaponElementName(item.ResolvedElement) : "무속성";
        SetItemHeader(item, displayName, categoryName + " / " + weaponClassName + " · " + elementName);
    }

    private void SetItemHeader(ItemData item, string displayName, string subtitle)
    {
        if (string.IsNullOrWhiteSpace(displayName) && item?.baseData != null)
            displayName = !string.IsNullOrWhiteSpace(item.baseData.itemName) ? item.baseData.itemName : item.baseData.name;

        Color gradeColor = item != null ? GradeConfig.GetGradeColor(item.grade) : Color.gray;
        weaponNameText.text = displayName ?? string.Empty;
        weaponNameText.color = gradeColor; // 이름 등급색
        weaponSubtitleText.text = subtitle ?? string.Empty;
        weaponGradeTagText.text = item != null ? ItemTooltipFormatter.GetGradeName(item.grade) : string.Empty;
        weaponGradeTagImage.color = gradeColor;
        weaponGradeTagText.color = GetContrastingTextColor(gradeColor);
    }

    private static Color GetContrastingTextColor(Color background)
    {
        float luminance = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
        return luminance >= 0.62f ? new Color32(24, 20, 16, 255) : Color.white;
    }

    private static string GetWeaponElementName(WeaponElement element)
    {
        string label = OverburstElementRules.Label(OverburstElementRules.MigrateLegacy(element));
        return string.IsNullOrEmpty(label) ? "무속성" : label;
    }

    private void SetBagTooltipContent(ItemData item, BagItemData bagData)
    {
        item.EnsureRuntimeState(); // 가방 옵션 보정
        SetActive(dividerBasic, true);
        SetActive(basicStatsText, true);
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);
        SetActive(dividerPrice, true);
        SetActive(priceText, true);
        SetActive(dividerPrice, true);
        SetActive(priceText, true);

        SetItemHeader(item, item.itemName, "가방 / 수납 · 파밍");
        if (flaskStatsFont == null) flaskStatsFont = Resources.Load<TMP_FontAsset>("UI/Tooltip/FlaskTooltipFont");
        if (flaskStatsFont != null) basicStatsText.font = flaskStatsFont;
        basicStatsText.text = BuildBagStatsText(item, bagData);
        priceText.text = BuildPriceText(item);
    }

    private string BuildBagStatsText(ItemData item, BagItemData bagData) => BagTooltip.Details(item);

    private void SetDefaultTooltipContent(ItemData item)
    {
        SetActive(dividerBasic, true);
        SetActive(basicStatsText, true);
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);

        SetItemHeader(item, item.itemName, "일반 아이템");

        basicStatsText.text = string.IsNullOrEmpty(item.baseData.description) ? string.Empty : item.baseData.description;
        priceText.text = BuildPriceText(item);
    }

    private void SetCurrencyTooltipContent(ItemData item, CurrencyItemData currencyData)
    {
        SetActive(dividerBasic, true);
        SetActive(basicStatsText, true);
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);
        SetActive(dividerPrice, false);
        SetActive(priceText, false);

        SetItemHeader(
            item,
            item.itemName + " x" + Mathf.Max(1, item.stackCount),
            "재화 / " + GetCurrencyDisplayName(currencyData.currencyType));

        basicStatsText.text = string.IsNullOrEmpty(currencyData.description) ? "창고 기준으로 자동 정산되는 아이템형 재화" : currencyData.description;
    }

    private void SetConsumableTooltipContent(ItemData item, ConsumableItemData consumableData)
    {
        SetActive(dividerBasic, true);
        SetActive(basicStatsText, true);
        SetActive(dividerWeapon, true);
        SetActive(weaponStatsText, true);
        SetActive(dividerPrice, true);
        SetActive(priceText, true);

        SetItemHeader(item, item.itemName, "소비아이템 / " + GetConsumableSubtypeName(consumableData));

        if (regularConsumableStatsFont == null)
            regularConsumableStatsFont = weaponStatsText.font;

        if (consumableData is FlaskItemData flaskData)
        {
            if (flaskStatsFont == null)
                flaskStatsFont = Resources.Load<TMP_FontAsset>("UI/Tooltip/FlaskTooltipFont");
            if (flaskStatsFont != null)
                weaponStatsText.font = flaskStatsFont;
            SetItemHeader(item, item.itemName, FlaskTooltip.Subtitle(flaskData));
            basicStatsText.text = FlaskTooltip.Status(item);
            weaponStatsText.text = FlaskTooltip.Details(item);
            SetActive(dividerPrice, currentShopPriceContextActive);
            SetActive(priceText, currentShopPriceContextActive);
        }
        else
        {
            if (regularConsumableStatsFont != null)
                weaponStatsText.font = regularConsumableStatsFont;
            basicStatsText.text = BuildConsumableEffectText(consumableData);
            weaponStatsText.text = BuildConsumableTimingText(consumableData);
        }
        priceText.text = BuildPriceText(item);
    }

    private string BuildConsumableEffectText(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return string.Empty;

        switch (consumableData.consumableType)
        {
            case ConsumableType.SpeedBoost:
                return "이동속도 +" + FormatConsumablePercent(GetMoveSpeedBonusPercent(consumableData)) + "%";
            case ConsumableType.HealHp:
                return "체력 +" + FormatHealValue(consumableData.effectValue);
            default:
                return string.IsNullOrEmpty(consumableData.description) ? "사용 효과" : consumableData.description;
        }
    }

    private string BuildConsumableTimingText(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return string.Empty;

        StringBuilder builder = new StringBuilder();
        if (consumableData.duration > 0f)
            builder.Append("지속시간 ").Append(ItemTooltipFormatter.FormatSeconds(consumableData.duration)).AppendLine();
        else if (consumableData.consumableType == ConsumableType.HealHp)
            builder.AppendLine("즉시 회복");

        if (consumableData.cooldown > 0f)
            builder.Append("쿨타임 ").Append(ItemTooltipFormatter.FormatSeconds(consumableData.cooldown));

        if (consumableData.IsPermanentSingleItem)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.AppendLine("소모 없음");
            builder.Append("단일 아이템");
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildPriceText(ItemData item)
    {
        int baseValue = GetBaseTooltipValue(item);
        if (currentShopPriceContextActive && currentShopMerchant != null)
        {
            int saleValue = GetCurrentShopPrice(item);
            return "가치 : " + baseValue + "G\n판매가 : " + saleValue + "G";
        }

        return "가치 : " + baseValue + "G";
    }

    private int GetBaseTooltipValue(ItemData item)
    {
        ResolvePriceValueCalculator();
        if (priceValueCalculator != null)
            return priceValueCalculator.GetBaseValue(item);

        return GetFallbackStackValue(item);
    }

    private int GetCurrentShopPrice(ItemData item)
    {
        ResolvePriceValueCalculator();
        if (priceValueCalculator != null)
        {
            return currentShopMerchantSelling
                ? priceValueCalculator.GetBuyValue(item, currentShopMerchant)
                : priceValueCalculator.GetSellValue(item, currentShopMerchant);
        }

        return GetFallbackStackValue(item);
    }

    private void ResolvePriceValueCalculator()
    {
        if (priceValueCalculator == null)
            priceValueCalculator = FindFirstObjectByType<MerchantTradeValueCalculator>(FindObjectsInactive.Include);
    }

    private int GetFallbackStackValue(ItemData item)
    {
        if (item == null || item.baseData == null)
            return 0;

        int unitValue = item.baseData is CurrencyItemData currencyData && currencyData.currencyType == CurrencyType.Gold
            ? 1
            : Mathf.Max(0, item.baseData.sellPrice);
        return unitValue * Mathf.Max(1, item.stackCount);
    }

    private string GetConsumableSubtypeName(ConsumableItemData consumableData)
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

    private string GetCurrencyDisplayName(CurrencyType type)
    {
        switch (type)
        {
            case CurrencyType.Gold: return "골드";
            case CurrencyType.MapFragment: return "지도조각";
            default: return "재화";
        }
    }

    private float GetMoveSpeedBonusPercent(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return 0f;

        float multiplier = consumableData.moveSpeedMultiplier > 0f ? consumableData.moveSpeedMultiplier : consumableData.effectValue;
        if (multiplier <= 0f)
            return 0f;

        return Mathf.Max(0f, (multiplier - 1f) * 100f);
    }

    private string FormatHealValue(float value)
    {
        if (value > 0f && value <= 1f)
            return FormatConsumablePercent(value * 100f) + "%";

        return ItemTooltipFormatter.FormatNumber(value);
    }

    private string FormatConsumablePercent(float value)
    {
        return value.ToString("0.##");
    }
}
