using UnityEngine;

public enum GearKind { Helmet, Chest, Gloves, Boots, Earring, Necklace }
public enum GearSlot { Helmet, Chest, Gloves, Boots, Earring, Necklace }
public enum GearStat
{
    MaxHealth, Armor, Attack, CriticalChance, AttackSpeed, NormalDamage,
    WeakDamage, HeavyDamage, EliteBossDamage, ElementalDamage, CriticalDamage
}

[CreateAssetMenu(fileName = "NewGear", menuName = "Items/Overburst Gear")]
public sealed class GearItemData : BaseItemData
{
    public GearKind kind;
    [Min(1)] public int catalogMinLevel = 1;
    [Min(1)] public int catalogMaxLevel = 100;

    public bool AppearsAtLevel(int level) => level >= catalogMinLevel && level <= catalogMaxLevel;

    public GearStat MainStat => OverburstCombatBalance.MainStat(kind);

    public float MainBaseValue => OverburstCombatBalance.GearBase(kind, 1);

    public static bool Fits(GearKind kind, GearSlot slot)
    {
        if (kind == GearKind.Earring)
            return slot == GearSlot.Earring;
        if (kind == GearKind.Necklace)
            return slot == GearSlot.Necklace;
        return (int)kind == (int)slot;
    }
}
