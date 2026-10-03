using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public static class ElementGemTooltip
{
    public sealed class Row
    {
        public string Label, Marks;
        public float Value;
        public ElementGemUnit Unit;
        public bool Fixed;
        public string Formatted => Format(Value, Unit);
    }
    public static string Archetype(ElementGemArchetype type) => type == ElementGemArchetype.Weak ? "약공 강화형" : type == ElementGemArchetype.Heavy ? "강공 강화형" : "밸런스형";
    public static string Subtitle(ItemData item) => "원소보석 / " + OverburstElementRules.Label(((ElementGemItemData)item.baseData).element) + " · " + Archetype(item.gemState.archetype);
    public static List<Row> Rows(ItemData item)
    {
        var fixedValues = ElementGemQuality.CalculateFixed(item);
        var rows = new List<Row>();
        void Percent(string label, float value) { if (value != 0) rows.Add(new Row { Label = label, Value = value * 100, Unit = ElementGemUnit.RatioBps, Fixed = true }); }
        Percent("기본 약공 피해", fixedValues.Common.WeakDamage);
        Percent("기본 강공 피해", fixedValues.Common.HeavyDamage);
        Percent("기본 연소 피해", fixedValues.BurnDamage); Percent("기본 연쇄폭발 피해", fixedValues.ExplosionDamage);
        Percent("기본 냉기 감속", fixedValues.ChillSlow); Percent("기본 빙결 지속", fixedValues.FreezeDuration); Percent("기본 쇄빙 피해", fixedValues.ShatterDamage);
        Percent("기본 감전 피해", fixedValues.ShockDamage); Percent("기본 연쇄번개 피해", fixedValues.ChainDamage);
        Percent("중첩당 약공 피해", fixedValues.WeakPerStack); Percent("기본 어둠 투사체", fixedValues.ProjectileDamage); Percent("기본 빛 강공 피해", fixedValues.LightHitDamage);
        foreach (var roll in item.gemState.rolls)
        {
            var option = ElementGemQuality.Option(roll.optionId);
            var marks = new StringBuilder();
            foreach (var star in roll.stars) marks.Append("<color=").Append(star == WeaponGradeStarType.Red ? "#E29A8E" : star == WeaponGradeStarType.Yellow ? "#D2A85D" : star == WeaponGradeStarType.Green ? "#68AA84" : "#D5D8D8").Append(">◆</color>");
            rows.Add(new Row { Label = option.Label, Value = ElementGemQuality.Value(item,roll), Unit = option.Unit, Marks = marks.Length > 0 ? marks.ToString() : "각인 없음" });
        }
        return rows;
    }
    public static string Format(float value, ElementGemUnit unit)
    {
        string number = ItemTooltipFormatter.FormatNumber(unit == ElementGemUnit.RatioBps ? value / 100f : value);
        return (unit == ElementGemUnit.ThresholdReduction && value > 0 ? "−" : "+") + number
            + (unit == ElementGemUnit.RatioBps ? "%" : unit == ElementGemUnit.PercentagePoints ? "%p" : unit == ElementGemUnit.ThresholdReduction ? "중첩" : unit == ElementGemUnit.Count ? "" : "");
    }
    public static string Notes(ItemData item)
    {
        var modifiers = ElementGemQuality.Calculate(item); var lines = new List<string>();
        if (modifiers.FreezeReduction > 0) lines.Add("빙결 필요 중첩 5 → 4 · 면역 대상 냉기 상한 5 유지");
        if (modifiers.FreezeDuration > 0) lines.Add("빙결 지속시간 강화는 최종 2배 상한");
        if (modifiers.ChillSlow > 0) lines.Add("냉기 감속은 대상 등급별 기준 적용 · 최종 80% 상한");
        if (modifiers.ChainHops > 0) lines.Add("연쇄 도약 추가 +" + modifiers.ChainHops + "회 · 최종 상한 7회");
        if (modifiers.CorrosionExtra > 0) lines.Add("최대 잠식 " + (OverburstElementTuning.Current.maximumStacks + modifiers.CorrosionExtra) + "중첩");
        if (modifiers.RadianceExtra > 0) lines.Add("최대 광휘 " + (OverburstElementTuning.Current.SafeLightRadianceMaxStacks + modifiers.RadianceExtra) + "중첩");
        if (item.gemState.rolls.Any(x=>ElementGemQuality.Option(x.optionId).Special && ElementGemQuality.Value(item,x)==0)) lines.Add("0 효과 옵션은 현재 별 품질이 활성 구간에 미달합니다.");
        var equipped = PlayerContext.Instance?.CurrentActorEquipment;
        if (equipped != null && !equipped.HasCurrentWeapon) lines.Add("무기 장착 시 원소 사용 · 생존 능력치는 적용 중");
        if (equipped?.EquippedElementGem?.baseData is ElementGemItemData other && other.element != ((ElementGemItemData)item.baseData).element) lines.Add("장착 보석과 원소가 달라 수치 비교를 표시하지 않습니다.");
        return string.Join("\n", lines);
    }
    public static string Details(ItemData item)
    {
        var text = new StringBuilder(); bool random = false;
        text.AppendLine("고정 효과");
        foreach (var row in Rows(item))
        {
            if (!row.Fixed && !random) { text.AppendLine().AppendLine("랜덤 능력치 · 품질 각인"); random = true; }
            text.Append(row.Label).Append("  ").Append(row.Formatted);
            if (!row.Fixed) text.Append("  ").Append(row.Marks);
            text.AppendLine();
        }
        return text.Append(Notes(item)).ToString().TrimEnd();
    }
    public static string Build(ItemData item) => item.itemName + "\n" + Subtitle(item) + "\n아이템 레벨 " + item.level + "\n" + Details(item) + "\n가치 " + ElementGemLootPolicy.Value(item) + "G";
}
