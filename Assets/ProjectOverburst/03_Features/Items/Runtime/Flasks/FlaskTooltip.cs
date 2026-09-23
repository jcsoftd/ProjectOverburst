using System.Globalization;
using System.Text;
using UnityEngine;

public static class FlaskTooltip
{
    private const string ValueColor = "#F2D48C";
    private const string MutedColor = "#9EAAB5";
    private const string NoteColor = "#D5B778";

    public static string Subtitle(FlaskItemData data)
    {
        if (data == null) return "영구 장착 물약";
        string role;
        switch (data.kind)
        {
            case FlaskKind.Life:
            case FlaskKind.Regeneration: role = "회복"; break;
            case FlaskKind.Berserker:
            case FlaskKind.Giant:
            case FlaskKind.Executioner: role = "공격"; break;
            case FlaskKind.Ironclad: role = "방어"; break;
            case FlaskKind.Ghost: role = "기동"; break;
            case FlaskKind.Overcharge: role = "원소 에너지"; break;
            case FlaskKind.Fire: role = "불"; break;
            case FlaskKind.Ice: role = "얼음"; break;
            case FlaskKind.Lightning: role = "번개"; break;
            default: role = "물"; break;
        }
        return "영구 장착 물약  ·  " + role;
    }

    public static string Status(ItemData item)
    {
        FlaskInstanceState state = FlaskRuntime.State(item);
        if (state == null) return "물약 정보 확인 필요";
        PlayerFlaskController current = PlayerFlaskController.Current;
        if (current == null || state.equippedSlot < 0 || current.GetItem(state.equippedSlot) != item)
            return "<color=" + MutedColor + ">장비칸에 장착하면 사용할 수 있습니다.</color>";
        float active = current.Remaining(state.equippedSlot);
        var text = new StringBuilder(80);
        if (active > 0f)
            text.Append("<color=#89D6A0>효과 적용 중 ").Append(Number(active)).Append("초</color>    ");
        text.Append("<color=").Append(MutedColor).Append(">재사용</color>  ");
        if (state.cooldownRemaining > 0f)
            text.Append("<color=#D5B778>").Append(Number(state.cooldownRemaining)).Append("초 후</color>");
        else
            text.Append("<color=#89D6A0>준비 완료</color>");
        return text.ToString();
    }
    public static string Details(ItemData item)
    {
        if (!(item?.baseData is FlaskItemData data)) return string.Empty;
        FlaskInstanceState state = FlaskRuntime.State(item);
        if (state == null) return "물약 정보 확인 필요";
        FlaskStats stats = FlaskRuntime.Stats(item);
        var text = new StringBuilder(420);

        text.Append("<color=").Append(MutedColor).AppendLine(">최종 효과</color>");
        AppendRow(text, Label(data.primaryEffect), EffectValue(data.primaryEffect, stats.primary), state.rolls[0]);
        AppendRow(text, Label(data.secondaryEffect), EffectValue(data.secondaryEffect, stats.secondary), state.rolls[1]);
        AppendRow(text, "지속 시간", Number(stats.duration) + "초", state.rolls[2]);
        AppendRow(text, "재사용 대기", Number(stats.cooldown) + "초", state.rolls[3]);

        if (data.IsElemental) AppendNote(text, RequiredElementName(data.kind) + " 무기에서 사용 가능");
        if (data.passesEnemyBodies) AppendNote(text, "일반 적의 몸을 통과");

        PlayerFlaskController current = PlayerFlaskController.Current;
        if (current != null)
        {
            AppendWeaponLimit(text, current, data, stats);
            AppendEquippedComparison(text, current, item, data, stats);
        }
        return text.ToString().TrimEnd();
    }

    private static void AppendRow(StringBuilder text, string label, string value, FlaskStatRoll roll)
    {
        text.Append(label).Append(" <color=").Append(ValueColor).Append("><b>")
            .Append(value).Append("</b></color>");
        if (roll?.stars != null && roll.stars.Count > 0)
        {
            text.Append(" <size=55%>");
            foreach (WeaponGradeStarType star in roll.stars)
            {
                string color = star == WeaponGradeStarType.Yellow ? "#FFD65A"
                    : star == WeaponGradeStarType.Green ? "#74DE94" : "#DCE2E8";
                text.Append("<color=").Append(color).Append(">★</color>");
            }
            text.Append("</size>");
        }
        text.AppendLine();
    }

    private static void AppendNote(StringBuilder text, string note)
    {
        text.Append("<color=").Append(NoteColor).Append(">• ")
            .Append(note).AppendLine("</color>");
    }

    private static void AppendWeaponLimit(StringBuilder text, PlayerFlaskController current,
        FlaskItemData data, FlaskStats stats)
    {
        PlayerEquipment equipment = current.GetComponent<PlayerEquipment>();
        if (equipment == null || equipment.CurrentWeaponItem == null) return;
        WeaponFinalStats weapon = equipment.CurrentWeaponStats;
        float primaryApplied = data.primaryEffect == FlaskEffect.AttackSpeed
            ? Mathf.Min(stats.primary, Mathf.Max(0f, 1.5f - weapon.meleeAttackSpeedMultiplier))
            : data.primaryEffect == FlaskEffect.CritChance
                ? Mathf.Min(stats.primary, Mathf.Max(0f, .6f - weapon.critChance / 100f))
                : data.primaryEffect == FlaskEffect.AttackRadius
                    ? Mathf.Min(stats.primary, Mathf.Max(0f, 2f - weapon.meleeAttackRangeScale))
                    : stats.primary;
        if (primaryApplied < stats.primary - .0001f)
            AppendNote(text, "현재 무기 적용: " + Label(data.primaryEffect) + " "
                + EffectValue(data.primaryEffect, primaryApplied));

        if (data.secondaryEffect != FlaskEffect.CritDamage) return;
        float secondaryApplied = Mathf.Min(stats.secondary, Mathf.Max(0f, 2f - weapon.critDamageMultiplier));
        if (secondaryApplied < stats.secondary - .0001f)
            AppendNote(text, "현재 무기 적용: " + Label(data.secondaryEffect) + " "
                + EffectValue(data.secondaryEffect, secondaryApplied));
    }

    private static void AppendEquippedComparison(StringBuilder text, PlayerFlaskController current,
        ItemData item, FlaskItemData data, FlaskStats stats)
    {
        for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
        {
            ItemData equipped = current.GetItem(i);
            if (equipped == null || equipped == item || !(equipped.baseData is FlaskItemData old)
                || old.kind != data.kind) continue;
            FlaskStats previous = FlaskRuntime.Stats(equipped);
            text.Append("\n<color=").Append(MutedColor).AppendLine(">장착품 대비</color>");
            text.Append("주효과 ").Append(EffectDelta(data.primaryEffect, stats.primary - previous.primary))
                .Append("  ·  보조 ").Append(EffectDelta(data.secondaryEffect, stats.secondary - previous.secondary))
                .AppendLine();
            text.Append("지속 ").Append(Delta(stats.duration - previous.duration)).Append("초")
                .Append("  ·  대기 ").Append(Delta(stats.cooldown - previous.cooldown)).Append("초");
            return;
        }
    }

    private static string RequiredElementName(FlaskKind kind)
    {
        switch (kind)
        {
            case FlaskKind.Fire: return "불";
            case FlaskKind.Ice: return "얼음";
            case FlaskKind.Lightning: return "번개";
            default: return "물";
        }
    }

    private static string Label(FlaskEffect effect)
    {
        switch (effect)
        {
            case FlaskEffect.InstantHeal: return "즉시 회복";
            case FlaskEffect.HealPerSecond: return "초당 회복";
            case FlaskEffect.DirectDamageReduction: return "받는 피해";
            case FlaskEffect.DotDamageReduction: return "받는 지속 피해";
            case FlaskEffect.AttackSpeed: return "공격 속도";
            case FlaskEffect.DirectDamage: return "직접 피해";
            case FlaskEffect.AttackRadius: return "근접 범위";
            case FlaskEffect.OutgoingImpact: return "공격 충격";
            case FlaskEffect.CritChance: return "치명타 확률";
            case FlaskEffect.CritDamage: return "치명타 피해";
            case FlaskEffect.EnergyGain: return "에너지 획득";
            case FlaskEffect.EnergyDischargeDamage: return "방출 피해";
            case FlaskEffect.IncomingImpactReduction: return "받는 넉백";
            case FlaskEffect.MoveSpeed: return "이동 속도";
            case FlaskEffect.SlowResistance: return "둔화 효과";
            case FlaskEffect.FireDischargeDamage: return "불 방출 피해";
            case FlaskEffect.FireRadius: return "폭발 반경";
            case FlaskEffect.ShatterDamage: return "쇄빙 피해";
            case FlaskEffect.FreezeDuration: return "빙결 시간";
            case FlaskEffect.LightningDischargeDamage: return "번개 방출 피해";
            case FlaskEffect.ChainRange: return "연쇄 거리";
            case FlaskEffect.CompressionDamage: return "압착 피해";
            case FlaskEffect.SuctionRadius: return "흡인 반경";
            default: return effect.ToString();
        }
    }

    private static string EffectValue(FlaskEffect effect, float value)
    {
        bool reduction = effect == FlaskEffect.DirectDamageReduction
            || effect == FlaskEffect.DotDamageReduction
            || effect == FlaskEffect.IncomingImpactReduction
            || effect == FlaskEffect.SlowResistance;
        bool points = effect == FlaskEffect.AttackSpeed || effect == FlaskEffect.AttackRadius
            || effect == FlaskEffect.CritChance || effect == FlaskEffect.CritDamage;
        string suffix = points ? "%p" : effect == FlaskEffect.HealPerSecond ? "%/초" : "%";
        return (reduction ? "-" : "+") + Number(value * 100f) + suffix;
    }

    private static string EffectDelta(FlaskEffect effect, float value)
    {
        bool points = effect == FlaskEffect.AttackSpeed || effect == FlaskEffect.AttackRadius
            || effect == FlaskEffect.CritChance || effect == FlaskEffect.CritDamage;
        return Delta(value * 100f) + (points ? "%p" : "%");
    }

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Delta(float value) => value.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
}
