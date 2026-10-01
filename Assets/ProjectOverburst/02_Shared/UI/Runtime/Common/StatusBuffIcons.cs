using System.Collections.Generic;
using UnityEngine;

// Shared presentation lookup. Effect ownership stays in combat, flasks and the active map run.
public static class StatusBuffIcons
{
    public const string ResourceRoot = "UI/StatusBuffs/";
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite Load(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!cache.TryGetValue(key, out Sprite sprite))
        {
            sprite = Resources.Load<Sprite>(ResourceRoot + key);
            if (sprite != null) cache.Add(key, sprite);
        }
        return sprite;
    }

    public static Sprite Status(string name)
    {
        switch (name)
        {
            case "burning": return Load("01_Status_Effects/status_burning");
            case "chill": return Load("01_Status_Effects/status_chill");
            case "freeze": return Load("01_Status_Effects/status_freeze");
            case "shock": return Load("01_Status_Effects/status_shock");
            case "corrosion": return Load("01_Status_Effects/status_corrosion");
            case "radiance": return Load("01_Status_Effects/status_radiance");
            case "healing": return Load("01_Status_Effects/status_healing");
            case "haste": return Load("01_Status_Effects/status_haste");
            case "slow": return Load("01_Status_Effects/status_slow");
            case "stun": return Load("01_Status_Effects/status_stun");
            default: return null;
        }
    }
    public static Sprite Element(WeaponElement element, bool frozen = false)
    {
        switch (element)
        {
            case WeaponElement.Fire: return Status("burning");
            case WeaponElement.Ice: return Status(frozen ? "freeze" : "chill");
            case WeaponElement.Electric: return Status("shock");
            case WeaponElement.Dark: return Status("corrosion");
            case WeaponElement.Light: return Status("radiance");
            default: return null;
        }
    }

    public static Sprite Buff(BuffDefinition definition)
    {
        if (definition == null) return null;
        if (definition.moveSpeedMultiplier < 1f) return Status("slow");
        if (definition.moveSpeedMultiplier > 1f) return Status("haste");
        if (definition.initialHealPercent > 0f || definition.healPercentPerTick > 0f)
            return Status("healing");
        return definition.icon;
    }

    public static Sprite Flask(FlaskItemData data)
    {
        if (data == null) return null;
        switch (data.kind)
        {
            case FlaskKind.Regeneration: return Load("02_Potion_Buffs/potion_regeneration");
            case FlaskKind.Berserker: return Load("02_Potion_Buffs/potion_berserker");
            case FlaskKind.Giant: return Load("02_Potion_Buffs/potion_giant");
            case FlaskKind.Executioner: return Load("02_Potion_Buffs/potion_executioner");
            case FlaskKind.Ironclad: return Load("02_Potion_Buffs/potion_ironclad");
            case FlaskKind.Ghost: return Load("02_Potion_Buffs/potion_ghost");
            case FlaskKind.Life: return Status("healing");
            default: return data.icon;
        }
    }

    public static Sprite Map(MapBuffKind kind)
    {
        switch (kind)
        {
            case MapBuffKind.MaxHealth: return Load("03_Map_Buffs/map_vitality");
            case MapBuffKind.Armor: return Load("03_Map_Buffs/map_armor");
            case MapBuffKind.Attack: return Load("03_Map_Buffs/map_attack");
            case MapBuffKind.ElementalDamage: return Load("03_Map_Buffs/map_elemental_damage");
            case MapBuffKind.AttackSpeed: return Load("03_Map_Buffs/map_attack_speed");
            case MapBuffKind.ItemDrop: return Load("03_Map_Buffs/map_loot");
            case MapBuffKind.ExperienceGain: return Load("03_Map_Buffs/map_experience");
            case MapBuffKind.MoveSpeed: return Status("haste");
            default: return null;
        }
    }
}
