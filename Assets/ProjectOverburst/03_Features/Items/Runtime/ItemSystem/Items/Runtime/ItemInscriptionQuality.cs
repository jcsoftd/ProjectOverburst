using System.Collections.Generic;

public enum ItemInscriptionQualityTier { Lowest, Low, Medium, High, Finest, Masterpiece }

public readonly struct ItemInscriptionQualityResult
{
    public readonly int Score;
    public readonly ItemGrade Grade;
    public readonly ItemInscriptionQualityTier Tier;
    public ItemInscriptionQualityResult(ItemGrade grade, int score)
    {
        Grade = grade;
        Score = System.Math.Max(0, score);
        Tier = ItemInscriptionQuality.Classify(grade, Score);
    }
    public string Label => ItemInscriptionQuality.Label(Tier);
    public float IconBrightness => Tier == ItemInscriptionQualityTier.Lowest ? .92f : Tier == ItemInscriptionQualityTier.Low ? .96f : 1f;
    public float ShineStrength => Tier == ItemInscriptionQualityTier.Masterpiece ? .28f : Tier == ItemInscriptionQualityTier.Finest ? .18f : Tier == ItemInscriptionQualityTier.High ? .10f : 0f;
    public string TextColor => Tier == ItemInscriptionQualityTier.Masterpiece ? "#EAD09A"
        : Tier == ItemInscriptionQualityTier.Finest ? "#E3CA95" : Tier == ItemInscriptionQualityTier.High ? "#D9CAA7"
        : Tier == ItemInscriptionQualityTier.Medium ? "#C7C1B4" : Tier == ItemInscriptionQualityTier.Low ? "#A4A49E" : "#858782";
    public string Heading => "각인 품질 <color=" + TextColor + ">" + Label + "</color>";
}

/// <summary>저장된 별 색의 개선 점수를 같은 등급 안에서 평가한다. 능력치·저장 데이터·난수는 변경하지 않는다.</summary>
public static class ItemInscriptionQuality
{
    public static bool TryEvaluate(ItemData item, out ItemInscriptionQualityResult result)
    {
        result = default;
        if (item == null || !item.baseData || WeaponGradeStatRoller.GetMeleePositiveStarCount(item.grade) == 0) return false;
        int score = 0;
        int positiveCount = 0;
        if (item.baseData is WeaponItemData)
        {
            if (item.weaponGradeStats != null) foreach (var row in item.weaponGradeStats)
            {
                if (row == null) continue;
                if (row.starRolls != null && row.starRolls.Count > 0)
                {
                    foreach (var star in row.starRolls) if (star != null) Add(star.starType, ref score, ref positiveCount);
                }
                // 색이 없는 구형 별은 흰별로만 평가하며 색을 임의로 복원하지 않는다.
                else positiveCount += System.Math.Max(0, row.positiveStarCount);
            }
        }
        else if (item.baseData is GearItemData)
        {
            if (item.gearRolls != null) foreach (var row in item.gearRolls) if (row != null) Add(row.stars, ref score, ref positiveCount);
        }
        else if (item.baseData is BagItemData)
        {
            if (item.bagState?.rows != null) foreach (var row in item.bagState.rows) if (row != null) Add(row.stars, ref score, ref positiveCount);
        }
        else if (item.baseData is FlaskItemData)
        {
            if (item.flaskState?.rolls != null) foreach (var row in item.flaskState.rolls) if (row != null) Add(row.stars, ref score, ref positiveCount);
        }
        else if (item.baseData is ElementGemItemData)
        {
            if (item.gemState?.rolls != null) foreach (var row in item.gemState.rolls) if (row != null) Add(row.stars, ref score, ref positiveCount);
        }
        else return false;
        if (positiveCount == 0) return false;
        result = new ItemInscriptionQualityResult(item.grade, score);
        return true;
    }

    /// <summary>표시 전용 점수. 실제 능력치의 별 배율과 별개이며 고정 빨간별은 점수에서 제외한다.</summary>
    public static int ColorScore(WeaponGradeStarType star)
    {
        switch (star)
        {
            case WeaponGradeStarType.Green: return 1;
            case WeaponGradeStarType.Yellow: return 2;
            default: return 0;
        }
    }

    private static void Add(WeaponGradeStarType star, ref int score, ref int positiveCount)
    {
        if (star != WeaponGradeStarType.White && star != WeaponGradeStarType.Green && star != WeaponGradeStarType.Yellow) return;
        positiveCount++;
        score += ColorScore(star);
    }

    private static void Add(IList<WeaponGradeStarType> stars, ref int score, ref int positiveCount)
    {
        if (stars != null) for (int i = 0; i < stars.Count; i++) Add(stars[i], ref score, ref positiveCount);
    }

    public static ItemInscriptionQualityTier Classify(ItemGrade grade, int score)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon: return Classify(score, 1, 2, 3, 4, 5);
            case ItemGrade.Rare: return Classify(score, 1, 2, 4, 5, 7);
            case ItemGrade.Epic: return Classify(score, 3, 4, 7, 8, 10);
            case ItemGrade.Legendary: return Classify(score, 5, 7, 10, 13, 15);
            case ItemGrade.Artifact: return Classify(score, 10, 13, 16, 19, 22);
            case ItemGrade.Mythic: return Classify(score, 13, 16, 20, 22, 25);
            case ItemGrade.Cursed: return Classify(score, 19, 21, 23, 25, 27);
            default: return ItemInscriptionQualityTier.Lowest;
        }
    }

    private static ItemInscriptionQualityTier Classify(int score, int low, int medium, int high, int finest, int masterpiece)
        => score >= masterpiece ? ItemInscriptionQualityTier.Masterpiece : score >= finest ? ItemInscriptionQualityTier.Finest
        : score >= high ? ItemInscriptionQualityTier.High : score >= medium ? ItemInscriptionQualityTier.Medium
        : score >= low ? ItemInscriptionQualityTier.Low : ItemInscriptionQualityTier.Lowest;

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
