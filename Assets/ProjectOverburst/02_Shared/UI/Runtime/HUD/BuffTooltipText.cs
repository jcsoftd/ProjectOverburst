using System.Globalization;
using UnityEngine;

// Presentation only: values come from the active effect, never from a tooltip balance table.
public static class BuffTooltipText
{
    public const string BuffColor = "#9CC8A3";
    public const string DebuffColor = "#DE9B96";
    private static readonly string[] MapNames = { "생명의 각인", "강철의 각인", "파괴의 각인", "원소의 각인", "질풍의 각인", "순풍의 각인", "수확의 각인", "지혜의 각인" };
    private static readonly string[] MapLabels = { "최대 체력", "방어력", "공격력", "원소 피해", "공격 속도", "이동 속도", "아이템 드롭률", "획득 경험치" };

    public static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    public static string Highlight(string value, bool debuff = false)
        => "<color=" + (debuff ? DebuffColor : BuffColor) + ">" + value + "</color>";

    public static string BuffName(BuffDefinition definition)
    {
        if (definition.displayName == "Recovery" || definition.buffId == "health_pickup_regen") return "회복";
        if (definition.displayName == "Slow" || definition.buffId == PlayerBuffController.SlowBuffId) return "둔화";
        return string.IsNullOrWhiteSpace(definition.displayName) ? (definition.isDebuff ? "약화 효과" : "강화 효과") : definition.displayName;
    }

    public static string BuffEffect(BuffDefinition definition)
        => FormatBuff(definition.tickInterval, definition.healPercentPerTick, definition.damagePerTick,
            definition.moveSpeedMultiplier, definition.isDebuff, 1);

    public static string BuffEffect(BuffInstance instance)
    {
        var state = instance.Snapshot;
        return FormatBuff(state.TickInterval, state.HealPercentPerTick, state.DamagePerTick,
            state.MoveSpeedMultiplier, state.IsDebuff, state.StackCount);
    }

    private static string FormatBuff(float interval, float healPercent, float damage, float moveSpeed, bool debuff, int stacks)
    {
        string text = string.Empty;
        if (healPercent > 0f)
            text = Number(interval) + "초마다 최대 체력의 " + Highlight(Number(healPercent * 100f) + "% 회복", debuff);
        if (damage > 0f)
            text += (text.Length > 0 ? " · " : "") + Number(interval) + "초마다 " + Highlight(Number(damage) + " 지속 피해", debuff);
        float move = (moveSpeed - 1f) * 100f;
        if (!Mathf.Approximately(move, 0f))
            text += (text.Length > 0 ? " · " : "") + "이동 속도 "
                + Highlight((move > 0 ? "+" : "−") + Number(Mathf.Abs(move)) + "%", debuff);
        if (stacks > 1) text += (text.Length > 0 ? " · " : "") + stacks + "중첩";
        return text.Length > 0 ? text : "효과 적용 중";
    }

    public static string FlaskEffectText(FlaskEffectSnapshot snapshot)
    {
        FlaskItemData data = snapshot.Data;
        string text = string.Empty;
        void Append(FlaskEffect effect, float value)
        {
            // Instant healing has already happened; it is not an ongoing status.
            if (effect == FlaskEffect.InstantHeal || value <= 0f) return;
            if (text.Length > 0) text += " · ";
            text += FlaskTooltip.Label(effect) + " " + Highlight(FlaskTooltip.EffectValue(effect, value));
        }
        Append(data.primaryEffect, snapshot.Stats.primary);
        Append(data.secondaryEffect, snapshot.Stats.secondary);
        if (data.passesEnemyBodies) text += (text.Length > 0 ? " · " : "") + "일반 적 통과";
        return text.Length > 0 ? text : "물약 효과 적용 중";
    }

    public static string MapName(MapBuffKind kind) => MapNames[(int)kind];
    public static string MapEffect(MapBuffKind kind, int stacks, float bonus)
        => MapLabels[(int)kind] + " " + Highlight("+" + Number(kind == MapBuffKind.Armor ? bonus : bonus * 100f)
            + (kind == MapBuffKind.Armor ? "" : "%")) + (stacks > 1 ? " · " + stacks + "중첩" : "");
}
