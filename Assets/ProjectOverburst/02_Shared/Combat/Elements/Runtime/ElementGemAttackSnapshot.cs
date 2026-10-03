using UnityEngine;

// Value snapshot for one attack and its status/committed follow-ups. Account revisions
// and currency changes are deliberately outside this lifetime.
public readonly struct ElementGemAttackSnapshot
{
    public readonly bool HasValue;
    public readonly GameObject Source;
    public readonly string WeaponId, GemId;
    public readonly int GemRevision, WeaponContextRevision, RadianceStacks;
    public readonly WeaponElement Element;
    public readonly ElementGemModifiers Modifiers;
    public readonly GearStatTotals Stats;
    public readonly float RunAttack, RunElemental;
    public ElementGemAttackSnapshot(PlayerEquipment equipment)
    {
        HasValue = equipment != null;
        Source = equipment != null ? equipment.gameObject : null;
        WeaponId = equipment?.CurrentWeaponItem?.runtimeInstanceId ?? string.Empty;
        GemId = equipment?.EquippedElementGem?.runtimeInstanceId ?? string.Empty;
        GemRevision = equipment != null ? equipment.GemRevision : 0;
        WeaponContextRevision = equipment != null ? equipment.WeaponContextRevision : 0;
        Element = equipment != null ? equipment.ActiveElement : WeaponElement.None;
        Modifiers = equipment != null ? equipment.GemModifiers : default;
        Stats = GearStatTotals.From(equipment);
        RunAttack = MapRunBuffs.Bonus(MapBuffKind.Attack);
        RunElemental = MapRunBuffs.Bonus(MapBuffKind.ElementalDamage);
        RadianceStacks = equipment != null ? equipment.GetComponent<OverburstElementEnergy>()?.RadianceStacks ?? 0 : 0;
    }
    public bool IsCurrent
    {
        get
        {
            if (!HasValue) return true;
            if (Source == null || !Source.activeInHierarchy) return false;
            var health = Source.GetComponent<CombatHealth>();
            if (health != null && health.IsDead) return false;
            var equipment = Source.GetComponent<PlayerEquipment>();
            return equipment != null && (equipment.CurrentWeaponItem?.runtimeInstanceId ?? string.Empty) == WeaponId
                && (equipment.EquippedElementGem?.runtimeInstanceId ?? string.Empty) == GemId && equipment.GemRevision == GemRevision
                && equipment.WeaponContextRevision == WeaponContextRevision;
        }
    }
    public float WeakBonus(CombatHealth target)
    {
        int stacks = Element == WeaponElement.Light ? RadianceStacks
            : Element == WeaponElement.Dark && target != null ? target.GetComponent<ElementalStatusController>()?.GetStackCount(WeaponElement.Dark) ?? 0 : 0;
        return Modifiers.WeakPerStack * stacks;
    }
}
