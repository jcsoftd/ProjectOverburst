using System.Globalization;
using System.Text;

public static class BagTooltip
{
    public static string Details(ItemData item)
    {
        if (item == null || !BagQuality.IsValid(item.bagState, item.grade)) return "가방 정보 확인 필요";
        var text = new StringBuilder();
        text.Append("아이템 레벨 ").Append(item.level);
        foreach (var row in item.bagState.rows)
        {
            float value = BagQuality.Value(item.level, row);
            float delta = value - BagQuality.Value(item.level, row, true);
            bool slots = row.stat == BagStat.InventorySlots;
            text.AppendLine().Append(Label(row.stat)).Append(" <color=#F2D48C>+")
                .Append(Number(value)).Append(slots ? "칸" : "%").Append("</color>");
            if (row.stars.Count > 0)
            {
                text.Append("  <size=85%>");
                foreach (var star in row.stars)
                {
                    string color = star == WeaponGradeStarType.Yellow ? "#D2A85D" : star == WeaponGradeStarType.Green ? "#68AA84"
                        : star == WeaponGradeStarType.Red ? "#DB6868" : "#D5D8D8";
                    text.Append("<color=").Append(color).Append(">◆</color>");
                }
                text.Append("</size> <color=#9EAAB5>(각인 ").Append(delta >= 0 ? "+" : "")
                    .Append(Number(delta)).Append(slots ? "칸" : "%").Append(")</color>");
            }
            if (row.stat == BagStat.KillExperience && PlayerProgression.CurrentLevel >= OverburstGrowthRules.MaximumLevel)
                text.Append(" <color=#9EAAB5>(최대 레벨)</color>");
        }
        return text.ToString();
    }
    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static string Label(BagStat stat)
    {
        switch (stat)
        {
            case BagStat.InventorySlots: return "인벤토리 수납";
            case BagStat.GoldMagnetRadius: return "골드 흡인 범위";
            case BagStat.ItemPickupDistance: return "아이템 획득 거리";
            case BagStat.KillExperience: return "처치 경험치";
            case BagStat.CombatGold: return "전투 골드 획득량";
            case BagStat.ExtraItemDrop: return "추가 아이템 드롭 확률";
            case BagStat.RareGradeWeight: return "희귀 이상 등급 가중치";
            default: return "";
        }
    }
}
