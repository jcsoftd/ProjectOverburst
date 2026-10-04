using System.Collections.Generic;
using UnityEngine;

public struct GearStatTotals
{
    public float MaxHealth, Armor, Attack, CriticalChance, AttackSpeed, NormalDamage;
    public float WeakDamage, HeavyDamage, EliteBossDamage, ElementalDamage, CriticalDamage;

    public static GearStatTotals From(PlayerEquipment equipment)
    {
        var totals = new GearStatTotals();
        if (equipment == null) return totals;
        for (int i = 0; i < 6; i++)
            totals.Add(equipment.GetGearSlotItem(i));
        totals.Add(equipment.EquippedElementGem);
        return totals;
    }

    // 장착하지 않은 장비 목록의 합계. 밸런스 분석 도구가 장착 합계와 같은 행 계산으로 쓴다.
    public static GearStatTotals FromItems(IEnumerable<ItemData> items)
    {
        var totals = new GearStatTotals();
        if (items == null) return totals;
        foreach (ItemData item in items)
            totals.Add(item);
        return totals;
    }

    private void Add(ItemData item)
    {
        if (item?.baseData is ElementGemItemData)
        {
            var gem = ElementGemQuality.Calculate(item).Common;
            MaxHealth += gem.MaxHealth; Armor += gem.Armor; Attack += gem.Attack;
            CriticalChance += gem.CriticalChance; AttackSpeed += gem.AttackSpeed;
            NormalDamage += gem.NormalDamage; WeakDamage += gem.WeakDamage;
            HeavyDamage += gem.HeavyDamage; EliteBossDamage += gem.EliteBossDamage;
            ElementalDamage += gem.ElementalDamage; CriticalDamage += gem.CriticalDamage;
            return;
        }
        if (item == null || !(item.baseData is GearItemData) || item.gearRolls == null) return;
        foreach (GearStatRoll row in item.gearRolls)
        {
            float value = GearQuality.Value(item, row);
            AddValue(row.stat, value);
        }
    }

    public void AddValue(GearStat stat, float value)
    {
        switch (stat)
        {
            case GearStat.MaxHealth: MaxHealth += value; break;
            case GearStat.Armor: Armor += value; break;
            case GearStat.Attack: Attack += value; break;
            case GearStat.CriticalChance: CriticalChance += value; break;
            case GearStat.AttackSpeed: AttackSpeed += value; break;
            case GearStat.NormalDamage: NormalDamage += value; break;
            case GearStat.WeakDamage: WeakDamage += value; break;
            case GearStat.HeavyDamage: HeavyDamage += value; break;
            case GearStat.EliteBossDamage: EliteBossDamage += value; break;
            case GearStat.ElementalDamage: ElementalDamage += value; break;
            case GearStat.CriticalDamage: CriticalDamage += value; break;
        }
    }

    public float TargetDamage(EnemyGradeType grade) => grade == EnemyGradeType.Normal
        ? NormalDamage : EliteBossDamage;
}
