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

    public GearStat MainStat => kind == GearKind.Helmet || kind == GearKind.Necklace
        ? GearStat.MaxHealth : kind == GearKind.Chest || kind == GearKind.Boots
            ? GearStat.Armor : kind == GearKind.Gloves
                ? GearStat.CriticalChance : GearStat.Attack;

    public float MainBaseValue => kind == GearKind.Helmet ? 15f : kind == GearKind.Chest ? 10f
        : kind == GearKind.Gloves ? .75f : kind == GearKind.Boots ? 5f
            : kind == GearKind.Earring ? 1f : 8f;

    public static bool Fits(GearKind kind, GearSlot slot)
    {
        if (kind == GearKind.Earring)
            return slot == GearSlot.EarringOne || slot == GearSlot.EarringTwo;
        if (kind == GearKind.Necklace)
            return slot == GearSlot.Necklace;
        return (int)kind == (int)slot;
    }
}
