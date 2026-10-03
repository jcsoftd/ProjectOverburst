using System.Collections.Generic;

public enum ItemInscriptionQualityTier { Lowest, Low, Medium, High, Finest, Masterpiece }

public readonly struct ItemInscriptionQualityResult
{
    public readonly float Score;
    public readonly ItemInscriptionQualityTier Tier;
    public ItemInscriptionQualityResult(float score) { Score = score; Tier = ItemInscriptionQuality.Classify(score); }
    public string Label => ItemInscriptionQuality.Label(Tier);
    public float IconBrightness => Tier == ItemInscriptionQualityTier.Lowest ? .92f : Tier == ItemInscriptionQualityTier.Low ? .96f : 1f;
    public float ShineStrength => Tier == ItemInscriptionQualityTier.Masterpiece ? .28f : Tier == ItemInscriptionQualityTier.Finest ? .18f : Tier == ItemInscriptionQualityTier.High ? .10f : 0f;
    public string TextColor => Tier == ItemInscriptionQualityTier.Masterpiece ? "#EAD09A"
        : Tier == ItemInscriptionQualityTier.Finest ? "#E3CA95" : Tier == ItemInscriptionQualityTier.High ? "#D9CAA7"
        : Tier == ItemInscriptionQualityTier.Medium ? "#C7C1B4" : Tier == ItemInscriptionQualityTier.Low ? "#A4A49E" : "#858782";
    public string Heading => "각인 품질 <color=" + TextColor + ">" + Label + "</color>";
}

/// <summary>표시용 파생 품질. 저장된 별만 읽으며 레벨·능력치·등급·저장 데이터와 난수 상태를 바꾸지 않는다.</summary>
public static class ItemInscriptionQuality
{
    public static bool TryEvaluate(ItemData item, out ItemInscriptionQualityResult result)
    {
        result = default;
        if (item == null || !item.baseData) return false;
        float score = 0f;
        if (item.baseData is WeaponItemData)
        {
            if (item.weaponGradeStats != null) foreach (var row in item.weaponGradeStats)
            {
                if (row == null) continue;
                if (row.starRolls != null && row.starRolls.Count > 0)
                {
                    foreach (var star in row.starRolls) if (star != null) score += Weight(star.starType);
                }
                else score += System.Math.Max(0, row.positiveStarCount) - System.Math.Max(0, row.negativeStarCount);
            }
        }
        else if (item.baseData is GearItemData)
        {
            if (item.gearRolls != null) foreach (var row in item.gearRolls) if (row != null) score += Sum(row.stars);
        }
        else if (item.baseData is BagItemData)
        {
            if (item.bagState?.rows != null) foreach (var row in item.bagState.rows) if (row != null) score += Sum(row.stars);
        }
        else if (item.baseData is FlaskItemData)
        {
            if (item.flaskState?.rolls != null) foreach (var row in item.flaskState.rolls) if (row != null) score += Sum(row.stars);
        }
        else if (item.baseData is ElementGemItemData)
        {
            if (item.gemState?.rolls != null) foreach (var row in item.gemState.rolls) if (row != null) score += Sum(row.stars);
        }
        else return false;
        result = new ItemInscriptionQualityResult(score);
        return true;
    }

    public static float Weight(WeaponGradeStarType star)
    {
        switch (star)
        {
            case WeaponGradeStarType.White: return 1f;
            case WeaponGradeStarType.Green: return 1.5f;
            case WeaponGradeStarType.Yellow: return 2f;
            case WeaponGradeStarType.Red: return -1f;
            default: return 0f;
        }
    }

    private static float Sum(IList<WeaponGradeStarType> stars)
    {
        float score = 0f;
        if (stars != null) for (int i = 0; i < stars.Count; i++) score += Weight(stars[i]);
        return score;
    }

    public static ItemInscriptionQualityTier Classify(float score)
        => score >= 32f ? ItemInscriptionQualityTier.Masterpiece : score >= 24f ? ItemInscriptionQualityTier.Finest
        : score >= 16f ? ItemInscriptionQualityTier.High : score >= 10f ? ItemInscriptionQualityTier.Medium
        : score >= 5f ? ItemInscriptionQualityTier.Low : ItemInscriptionQualityTier.Lowest;

    public static string Label(ItemInscriptionQualityTier tier)
    {
        switch (tier)
        {
            case ItemInscriptionQualityTier.Low: return "하급";
            case ItemInscriptionQualityTier.Medium: return "중급";
            case ItemInscriptionQualityTier.High: return "상급";
            case ItemInscriptionQualityTier.Finest: return "최상급";
            case ItemInscriptionQualityTier.Masterpiece: return "명품";
            default: return "최하급";
        }
    }
}
