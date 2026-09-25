using UnityEngine;

public struct GearStatTotals
{
    public float MaxHealth, Armor, Attack, CriticalChance, AttackSpeed, NormalDamage;
    public float WeakDamage, HeavyDamage, EliteBossDamage, ElementalDamage, CriticalDamage;

    public static GearStatTotals From(PlayerEquipment equipment)
    {
        var totals = new GearStatTotals();
        if (equipment == null) return totals;
        for (int i = 0; i < 7; i++)
        {
            ItemData item = equipment.GetGearSlotItem(i);
            if (item == null || !(item.baseData is GearItemData) || item.gearRolls == null) continue;
            foreach (GearStatRoll row in item.gearRolls)
            {
                float value = GearQuality.Value(item, row);
                switch (row.stat)
                {
                    case GearStat.MaxHealth: totals.MaxHealth += value; break;
                    case GearStat.Armor: totals.Armor += value; break;
                    case GearStat.Attack: totals.Attack += value; break;
                    case GearStat.CriticalChance: totals.CriticalChance += value; break;
                    case GearStat.AttackSpeed: totals.AttackSpeed += value; break;
                    case GearStat.NormalDamage: totals.NormalDamage += value; break;
                    case GearStat.WeakDamage: totals.WeakDamage += value; break;
                    case GearStat.HeavyDamage: totals.HeavyDamage += value; break;
                    case GearStat.EliteBossDamage: totals.EliteBossDamage += value; break;
                    case GearStat.ElementalDamage: totals.ElementalDamage += value; break;
                    case GearStat.CriticalDamage: totals.CriticalDamage += value; break;
                }
            }
        }
        return totals;
    }

    public float TargetDamage(EnemyGradeType grade) => grade == EnemyGradeType.Normal
        ? NormalDamage : EliteBossDamage;
}
