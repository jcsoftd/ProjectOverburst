using UnityEngine;

public enum GearKind { Helmet, Chest, Gloves, Boots, Earring, Necklace }
public enum GearSlot { Helmet, Chest, Gloves, Boots, EarringOne, EarringTwo, Necklace }
public enum GearStat
{
    MaxHealth, Armor, Attack, CriticalChance, AttackSpeed, NormalDamage,
    WeakDamage, HeavyDamage, EliteBossDamage, ElementalDamage, CriticalDamage
}

[CreateAssetMenu(fileName = "NewGear", menuName = "Items/Overburst Gear")]
public sealed class GearItemData : BaseItemData
{
    public GearKind kind;

    public GearStat MainStat => OverburstCombatBalance.MainStat(kind);

    public float MainBaseValue => OverburstCombatBalance.GearBase(kind, 1);

    public static bool Fits(GearKind kind, GearSlot slot)
    {
        if (kind == GearKind.Earring)
            return slot == GearSlot.EarringOne || slot == GearSlot.EarringTwo;
        if (kind == GearKind.Necklace)
            return slot == GearSlot.Necklace;
        return (int)kind == (int)slot;
    }
}
