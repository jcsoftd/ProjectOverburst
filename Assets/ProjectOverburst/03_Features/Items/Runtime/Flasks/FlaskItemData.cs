using UnityEngine;

public enum FlaskKind
{
    Life, Regeneration, Berserker, Giant, Executioner, Overcharge,
    Ironclad, Ghost, Fire, Ice, Lightning, Water
}

public enum FlaskEffect
{
    InstantHeal, HealPerSecond, DirectDamageReduction, DotDamageReduction,
    AttackSpeed, DirectDamage, AttackRadius, OutgoingImpact, CritChance, CritDamage,
    EnergyGain, EnergyDischargeDamage, IncomingImpactReduction, MoveSpeed, SlowResistance,
    FireDischargeDamage, FireRadius, ShatterDamage, FreezeDuration,
    LightningDischargeDamage, ChainRange, CompressionDamage, SuctionRadius
}

[CreateAssetMenu(fileName = "Flask", menuName = "Items/Equipment Flask")]
public sealed class FlaskItemData : ConsumableItemData
{
    public FlaskKind kind;
    public FlaskEffect primaryEffect;
    public FlaskEffect secondaryEffect;
    [Min(0f)] public float primaryValue;
    [Min(0f)] public float secondaryValue;
    [Min(1f)] public float chargeCost = 50f;
    [Min(1f)] public float chargeCapacity = 100f;
    public bool passesEnemyBodies;

    public bool IsElemental => kind >= FlaskKind.Fire;
    // Enable alongside the real heavy-discharge action, not while only its calculation API exists.
    public bool AvailableForDropsAndShop => !IsElemental && kind != FlaskKind.Overcharge;

    public void Configure(FlaskKind value)
    {
        kind = value;
        consumeOnUse = false;
        maxStack = 1;
        defaultGrade = ItemGrade.Common;
        duration = 6f;
        cooldown = 0f;
        chargeCost = 50f;
        chargeCapacity = 100f;
        weight = 1f;
        sellPrice = 100;
        targetBuffId = "overburst_flask_" + kind.ToString().ToLowerInvariant();
        passesEnemyBodies = kind == FlaskKind.Ghost;
        switch (kind)
        {
            case FlaskKind.Life:
                Set("생명 물약", FlaskEffect.InstantHeal, .20f, FlaskEffect.HealPerSecond, .015f,
                    "체력을 즉시 회복하고 잠시 동안 추가로 회복합니다.");
                chargeCost = 60f; chargeCapacity = 120f; break;
            case FlaskKind.Regeneration:
                Set("재생 물약", FlaskEffect.HealPerSecond, .05f, FlaskEffect.DotDamageReduction, .25f,
                    "지속적으로 체력을 회복하며 독 등 지속 피해를 줄입니다.");
                chargeCost = 60f; chargeCapacity = 120f; break;
            case FlaskKind.Berserker:
                Set("광전사 물약", FlaskEffect.AttackSpeed, .15f, FlaskEffect.DirectDamage, .15f,
                    "공격속도와 직접 공격의 피해를 높여 빠르게 몰아붙입니다."); break;
            case FlaskKind.Giant:
                Set("거인 물약", FlaskEffect.AttackRadius, .15f, FlaskEffect.OutgoingImpact, .35f,
                    "근접 공격 범위와 피격 충격량을 높입니다. 적의 체급 저항은 유지됩니다."); break;
            case FlaskKind.Executioner:
                Set("처형자 물약", FlaskEffect.CritChance, .20f, FlaskEffect.CritDamage, .25f,
                    "치명타 확률과 치명타 피해를 높입니다."); break;
            case FlaskKind.Overcharge:
                Set("과충전 물약", FlaskEffect.EnergyGain, .30f, FlaskEffect.EnergyDischargeDamage, .25f,
                    "직접 적중의 원소 에너지 획득량과 에너지 방출 피해를 높입니다.");
                chargeCost = 60f; chargeCapacity = 120f; break;
            case FlaskKind.Ironclad:
                Set("철갑 물약", FlaskEffect.DirectDamageReduction, .20f, FlaskEffect.IncomingImpactReduction, .30f,
                    "받는 직접 피해와 피격 충격량을 줄입니다. 무적이나 기절 면역을 주지는 않습니다."); break;
            case FlaskKind.Ghost:
                Set("유령 물약", FlaskEffect.MoveSpeed, .25f, FlaskEffect.SlowResistance, .50f,
                    "빨리 움직이며 둔화를 완화하고 일반 적의 몸을 통과합니다. 지형과 공격은 통과하지 않습니다."); break;
            case FlaskKind.Fire:
                Set("홍염 물약", FlaskEffect.FireDischargeDamage, .25f, FlaskEffect.FireRadius, .20f,
                    "불 무기의 방출 피해와 폭발 반경을 높입니다."); break;
            case FlaskKind.Ice:
                Set("빙심 물약", FlaskEffect.ShatterDamage, .30f, FlaskEffect.FreezeDuration, .25f,
                    "얼음 무기의 쇄빙 피해와 일반 적 빙결 시간을 높입니다. 보스의 제어 면역은 유지됩니다."); break;
            case FlaskKind.Lightning:
                Set("뇌광 물약", FlaskEffect.LightningDischargeDamage, .25f, FlaskEffect.ChainRange, .25f,
                    "번개 방출 피해와 연쇄 탐색 거리를 높입니다. 연쇄 대상 수는 늘지 않습니다."); break;
            default:
                Set("심해 물약", FlaskEffect.CompressionDamage, .30f, FlaskEffect.SuctionRadius, .20f,
                    "물 무기의 젖음 소비 압착 피해와 흡인 반경을 높입니다."); break;
        }
    }

    private void Set(string displayName, FlaskEffect primary, float p, FlaskEffect secondary, float s, string detail)
    {
        itemName = displayName; primaryEffect = primary; primaryValue = p;
        secondaryEffect = secondary; secondaryValue = s; description = detail;
    }
}
