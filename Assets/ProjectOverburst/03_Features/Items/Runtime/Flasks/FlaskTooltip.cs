using System.Text;
using UnityEngine;

public static class FlaskTooltip
{
    public static string Effects(ItemData item)
    {
        if (!(item?.baseData is FlaskItemData data)) return string.Empty;
        FlaskStats s = FlaskRuntime.Stats(item);
        return Effect(data.primaryEffect, s.primary) + "\n" + Effect(data.secondaryEffect, s.secondary)
            + (data.passesEnemyBodies ? "\n일반 적 몸 통과 · 지형/공격 충돌 유지" : string.Empty);
    }
    public static string Details(ItemData item)
    {
        var state = FlaskRuntime.State(item);
        if (state == null) return "물약 데이터 확인 필요";
        var s = FlaskRuntime.Stats(item);
        var b = new StringBuilder();
        var flaskData = (FlaskItemData)item.baseData;
        string[] names = { Effect(flaskData.primaryEffect, s.primary), Effect(flaskData.secondaryEffect, s.secondary), $"지속 {s.duration:0.##}초", $"사용 충전 {s.cost:0.##}", $"최대 충전 {s.capacity:0.##}" };
        for (int i = 0; i < 5; i++)
        {
            b.Append(names[i]).Append("  ");
            foreach (var star in state.rolls[i].stars)
                b.Append("<color=").Append(star == WeaponGradeStarType.Yellow ? "#FFD65A" : star == WeaponGradeStarType.Green ? "#74DE94" : "#E6EAF0").Append(">★</color>");
            if (state.rolls[i].stars.Count == 0) b.Append("—");
            b.AppendLine();
        }
        b.Append($"지속 {s.duration:0.##}초 · 1회 {s.cost:0.##} 소모\n충전 {state.charge:0.#}/{s.capacity:0.#} · {FlaskChargeRules.Uses(state, s)}회 사용 가능\n영구 보유 · 은신처에서 4~6번 장착\n같은 종류 중복 불가 · 전투 참여 중 충전");
        var data = (FlaskItemData)item.baseData;
        if (data.IsElemental) b.Append("\n일치하는 원소 무기 필요");
        var current = PlayerFlaskController.Current;
        if (current != null)
        {
            for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
            {
                var other = current.GetItem(i);
                if (other == null || other == item || !(other.baseData is FlaskItemData old) || old.kind != data.kind) continue;
                var o = FlaskRuntime.Stats(other);
                b.Append($"\n\n장착품 대비: 주효과 {Delta((s.primary-o.primary)*100)}%p\n보조효과 {Delta((s.secondary-o.secondary)*100)}%p · 지속 {Delta(s.duration-o.duration)}초\n소모 {Delta(s.cost-o.cost)} · 용량 {Delta(s.capacity-o.capacity)}");
            }
            var equipment = current.GetComponent<PlayerEquipment>();
            if (equipment != null && equipment.CurrentWeaponItem != null)
            {
                var w = equipment.CurrentWeaponStats;
                float applied = data.primaryEffect == FlaskEffect.AttackSpeed ? Mathf.Min(s.primary, Mathf.Max(0f, 1.5f-w.meleeAttackSpeedMultiplier))
                    : data.primaryEffect == FlaskEffect.CritChance ? Mathf.Min(s.primary, Mathf.Max(0f, .6f-w.critChance/100f))
                    : data.primaryEffect == FlaskEffect.AttackRadius ? Mathf.Min(s.primary, Mathf.Max(0f, 2f-w.meleeAttackRangeScale)) : s.primary;
                if (data.secondaryEffect == FlaskEffect.CritDamage)
                {
                    float secondaryApplied = Mathf.Min(s.secondary, Mathf.Max(0f, 2f-w.critDamageMultiplier));
                    if (secondaryApplied < s.secondary-.0001f) b.Append($"\n현재 무기 상한 적용: 치명타 피해 +{secondaryApplied*100:0.#}%p");
                }
                if (applied < s.primary-.0001f) b.Append($"\n현재 무기 상한 적용: 주효과 +{applied*100:0.#}%p");
            }
        }
        return b.ToString();
    }
    private static string Delta(float value) => value.ToString("+0.##;-0.##;0");
    private static string Effect(FlaskEffect effect, float v)
    {
        string[] names = { "최대 체력 즉시 회복", "초당 최대 체력 회복", "받는 직접 피해 감소", "받는 지속 피해 감소", "공격속도", "직접 피해", "근접 기본 반경", "넉백 충격량", "치명타 확률", "치명타 피해 배율", "원소 에너지 획득", "에너지 방출 피해", "받는 넉백 감소", "이동속도", "둔화 강도 감소", "불 방출 피해", "폭발 반경", "쇄빙 피해", "일반 적 빙결 시간", "번개 방출 피해", "연쇄 탐색 거리", "젖음 소비 압착 피해", "흡인 반경" };
        bool points = effect == FlaskEffect.AttackSpeed || effect == FlaskEffect.AttackRadius || effect == FlaskEffect.CritChance || effect == FlaskEffect.CritDamage;
        return names[(int)effect] + " +" + (v*100f).ToString("0.##") + (points ? "%p" : "%");
    }
}
