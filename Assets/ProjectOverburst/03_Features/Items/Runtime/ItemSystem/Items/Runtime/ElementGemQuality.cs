using System;
using System.Collections.Generic;
using UnityEngine;

public enum ElementGemArchetype { Weak, Heavy, Balanced }
public enum ElementGemUnit { RatioBps, FlatStat, PercentagePoints, Count, ThresholdReduction }

[Serializable] public sealed class ElementGemRoll
{
    public string optionId;
    public List<WeaponGradeStarType> stars = new List<WeaponGradeStarType>();
    public float Weight
    {
        get
        {
            float value = 0;
            if (stars != null) foreach (var star in stars)
                value += star == WeaponGradeStarType.Red ? -1 : star == WeaponGradeStarType.Yellow ? 2
                    : star == WeaponGradeStarType.Green ? 1.5f : 1;
            return value;
        }
    }
}
[Serializable] public sealed class ElementGemState
{
    public ElementGemArchetype archetype;
    public List<ElementGemRoll> rolls = new List<ElementGemRoll>();
    public ElementGemState Copy()
    {
        var copy = new ElementGemState { archetype = archetype, rolls = rolls == null ? null : new List<ElementGemRoll>() };
        if (rolls != null) foreach (var row in rolls)
            copy.rolls.Add(row == null ? null : new ElementGemRoll { optionId = row.optionId,
                stars = row.stars == null ? null : new List<WeaponGradeStarType>(row.stars) });
        return copy;
    }
}
public sealed class ElementGemOption
{
    public readonly string Id, Label;
    public readonly WeaponElement Element;
    public readonly ElementGemUnit Unit;
    public readonly float BaseValue;
    public readonly GearStat? CommonStat;
    public bool Special => Unit == ElementGemUnit.Count || Unit == ElementGemUnit.ThresholdReduction;
    public ElementGemOption(string id, string label, WeaponElement element, ElementGemUnit unit, float value, GearStat? common = null)
    { Id = "gem." + id; Label = label; Element = element; Unit = unit; BaseValue = value; CommonStat = common; }
}

// Percent fields follow GearStatTotals; integral fields are extra counts only.
public struct ElementGemModifiers
{
    public GearStatTotals Common;
    public float BurnDamage, FreezeDuration, ChillSlow, ShatterDamage, ShockDamage, ChainDamage;
    public float ExplosionDamage, ProjectileDamage, LightHitDamage, WeakPerStack;
    public int FreezeReduction, ChainHops, CorrosionExtra, RadianceExtra;
}

public static class ElementGemQuality
{
    public const int RowCount = 4, MaxStarsPerRow = 6;
    static readonly float[] GradeScales = { .20f, .35f, .50f, .70f, 1f, 1.20f, 1.40f, 1.30f };
    static readonly ElementGemOption[] Options =
    {
        new ElementGemOption("fire.burn_damage", "연소 피해", WeaponElement.Fire, ElementGemUnit.RatioBps, 800),
        new ElementGemOption("fire.chain_explosion_damage", "연쇄폭발 피해", WeaponElement.Fire, ElementGemUnit.RatioBps, 800),
        new ElementGemOption("ice.freeze_threshold_reduction", "빙결 필요 중첩 감소", WeaponElement.Ice, ElementGemUnit.ThresholdReduction, 1),
        new ElementGemOption("ice.freeze_duration", "빙결 지속시간", WeaponElement.Ice, ElementGemUnit.RatioBps, 500),
        new ElementGemOption("ice.chill_slow", "냉기 감속 강도", WeaponElement.Ice, ElementGemUnit.RatioBps, 1000),
        new ElementGemOption("ice.shatter_damage", "쇄빙 피해", WeaponElement.Ice, ElementGemUnit.RatioBps, 1000),
        new ElementGemOption("electric.chain_hops", "연쇄번개 추가 도약", WeaponElement.Electric, ElementGemUnit.Count, 2),
        new ElementGemOption("electric.chain_damage", "연쇄번개 피해", WeaponElement.Electric, ElementGemUnit.RatioBps, 800),
        new ElementGemOption("electric.shock_damage", "감전 피해", WeaponElement.Electric, ElementGemUnit.RatioBps, 1000),
        new ElementGemOption("dark.projectile_damage", "어둠 투사체 피해", WeaponElement.Dark, ElementGemUnit.RatioBps, 800),
        new ElementGemOption("dark.corrosion_max_stacks", "최대 잠식 중첩", WeaponElement.Dark, ElementGemUnit.Count, 2),
        new ElementGemOption("light.radiance_max_stacks", "최대 광휘 중첩", WeaponElement.Light, ElementGemUnit.Count, 20),
        new ElementGemOption("common.weak_damage", "약공 피해", WeaponElement.None, ElementGemUnit.RatioBps, 500, GearStat.WeakDamage),
        new ElementGemOption("common.heavy_damage", "강공 피해", WeaponElement.None, ElementGemUnit.RatioBps, 500, GearStat.HeavyDamage),
        new ElementGemOption("common.elemental_damage", "원소 피해", WeaponElement.None, ElementGemUnit.RatioBps, 500, GearStat.ElementalDamage),
        new ElementGemOption("common.normal_enemy_damage", "일반 몬스터 피해", WeaponElement.None, ElementGemUnit.RatioBps, 500, GearStat.NormalDamage),
        new ElementGemOption("common.elite_boss_damage", "정예 및 보스 피해", WeaponElement.None, ElementGemUnit.RatioBps, 500, GearStat.EliteBossDamage),
        new ElementGemOption("common.attack", "공격력", WeaponElement.None, ElementGemUnit.FlatStat, .10f, GearStat.Attack),
        new ElementGemOption("common.critical_damage", "치명타 피해", WeaponElement.None, ElementGemUnit.PercentagePoints, 3, GearStat.CriticalDamage),
        new ElementGemOption("common.max_health", "최대 체력", WeaponElement.None, ElementGemUnit.FlatStat, .05f, GearStat.MaxHealth),
        new ElementGemOption("common.armor", "방어력", WeaponElement.None, ElementGemUnit.FlatStat, .05f, GearStat.Armor)
    };
    public static IReadOnlyList<ElementGemOption> Catalog => Options;
    public static ElementGemOption Option(string id)
    { foreach (var option in Options) if (option.Id == id) return option; return null; }
    static bool Allowed(ElementGemOption option, ElementGemItemData data)
        => option != null && (option.Element == WeaponElement.None || option.Element == data.element)
            && (!option.Special || data.fixedGrade >= ItemGrade.Rare);
    public static ElementGemState Roll(ElementGemItemData data, int seed, ElementGemArchetype? archetype = null)
    {
        if (data == null || !ElementGemItemData.IsAllowed(data.element, data.fixedGrade)) throw new ArgumentException("Invalid gem definition.");
        var rng = new System.Random(seed);
        var state = new ElementGemState { archetype = archetype ?? (ElementGemArchetype)rng.Next(3) };
        if (!Enum.IsDefined(typeof(ElementGemArchetype), state.archetype)) throw new ArgumentException("Invalid archetype.");
        var pool = new List<ElementGemOption>();
        foreach (var option in Options) if (Allowed(option, data)) pool.Add(option);
        for (int row = 0; row < RowCount; row++)
        {
            if (pool.Count == 0) throw new InvalidOperationException("Gem option pool is exhausted.");
            var selected = pool[rng.Next(pool.Count)];
            state.rolls.Add(new ElementGemRoll { optionId = selected.Id });
            pool.Remove(selected);
            if (selected.Special) pool.RemoveAll(o => o.Special);
        }
        int positive = WeaponGradeStatRoller.GetMeleePositiveStarCount(data.fixedGrade);
        int negative = WeaponGradeStatRoller.GetMeleeNegativeStarCount(data.fixedGrade);
        if (positive + negative > RowCount * MaxStarsPerRow) throw new InvalidOperationException("Insufficient gem star capacity.");
        for (int i = 0; i < positive + negative; i++)
        {
            var rows = new List<ElementGemRoll>();
            foreach (var row in state.rolls) if (row.stars.Count < MaxStarsPerRow) rows.Add(row);
            if (rows.Count == 0) throw new InvalidOperationException("Gem star capacity exhausted.");
            var color = WeaponGradeStarType.Red;
            if (i < positive)
            {
                WeaponGradeStatRoller.GetMeleePositiveStarChances(data.fixedGrade, out float white, out float green, out _);
                double roll = rng.NextDouble();
                color = roll < white ? WeaponGradeStarType.White : roll < white + green ? WeaponGradeStarType.Green : WeaponGradeStarType.Yellow;
            }
            rows[rng.Next(rows.Count)].stars.Add(color);
        }
        return state;
    }
    public static bool IsValid(ElementGemItemData data, int level, ItemGrade grade, int count, ElementGemState state)
    {
        if (data == null || !ElementGemItemData.IsAllowed(data.element, grade) || grade != data.fixedGrade
            || level < 1 || level > 100 || count != 1 || state == null || !Enum.IsDefined(typeof(ElementGemArchetype), state.archetype)
            || state.rolls == null || state.rolls.Count != RowCount) return false;
        var ids = new HashSet<string>();
        int positive = 0, negative = 0, special = 0;
        foreach (var row in state.rolls)
        {
            if (row == null || !ids.Add(row.optionId) || row.stars == null || row.stars.Count > MaxStarsPerRow) return false;
            var option = Option(row.optionId);
            if (!Allowed(option, data) || (option.Special && ++special > 1)) return false;
            foreach (var color in row.stars)
                if (color == WeaponGradeStarType.Red) negative++;
                else if (color == WeaponGradeStarType.White || color == WeaponGradeStarType.Green || color == WeaponGradeStarType.Yellow) positive++;
                else return false;
        }
        return positive == WeaponGradeStatRoller.GetMeleePositiveStarCount(grade)
            && negative == WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade);
    }
    public static int FixedGroupCount(ElementGemArchetype archetype) => archetype == ElementGemArchetype.Balanced ? 4 : 2;
    public static float Value(ItemData item, ElementGemRoll row, bool withoutStars = false)
    {
        if (item == null || row == null || !(item.baseData is ElementGemItemData data)) return 0;
        var option = Option(row.optionId);
        if (!Allowed(option, data)) throw new InvalidOperationException("Unsupported gem option.");
        float weight = withoutStars ? 0 : row.Weight;
        if (option.Special)
        {
            int stage = weight >= 5 ? 2 : weight >= 1 ? 1 : 0;
            return option.Unit == ElementGemUnit.ThresholdReduction ? Math.Min(stage, 1)
                : option.Id == "gem.light.radiance_max_stacks" ? stage * 10 : stage;
        }
        float value = option.BaseValue * GradeScales[(int)item.grade] * Mathf.Max(0, 1 + .15f * weight);
        if (option.Unit == ElementGemUnit.FlatStat)
        {
            var kind = option.CommonStat == GearStat.Attack ? GearKind.Earring : option.CommonStat == GearStat.MaxHealth ? GearKind.Helmet : GearKind.Chest;
            return Mathf.Max(0, OverburstCombatBalance.RoundStat(value * OverburstCombatBalance.GearBase(kind, item.level)));
        }
        return option.Unit == ElementGemUnit.RatioBps ? Mathf.Round(value) : value;
    }
    public static ElementGemModifiers CalculateFixed(ItemData item)
    {
        var result = new ElementGemModifiers();
        if (item == null) return result;
        if (!(item.baseData is ElementGemItemData data) || item.balanceVersion != OverburstCombatBalance.ItemBalanceVersion
            || !IsValid(data, item.level, item.grade, item.stackCount, item.gemState)) throw new InvalidOperationException("Invalid gem instance.");
        var archetype = item.gemState.archetype;
        float scale = GradeScales[(int)item.grade];
        float weak = archetype == ElementGemArchetype.Heavy ? 0 : archetype == ElementGemArchetype.Balanced ? .6f : 1;
        float heavy = archetype == ElementGemArchetype.Weak ? 0 : archetype == ElementGemArchetype.Balanced ? .6f : 1;
        result.Common.WeakDamage = 15 * scale * weak;
        result.Common.HeavyDamage = 15 * scale * heavy;
        switch (data.element)
        {
            case WeaponElement.Fire: result.BurnDamage = 25 * scale * weak; result.ExplosionDamage = 25 * scale * heavy; break;
            case WeaponElement.Ice: result.ChillSlow = 20 * scale * weak; result.FreezeDuration = 10 * scale * weak; result.ShatterDamage = 25 * scale * heavy; break;
            case WeaponElement.Electric: result.ShockDamage = 30 * scale * weak; result.ChainDamage = 25 * scale * heavy; break;
            case WeaponElement.Dark: result.WeakPerStack = 2 * scale * weak; result.ProjectileDamage = 25 * scale * heavy; break;
            case WeaponElement.Light: result.WeakPerStack = .1f * scale * weak; result.LightHitDamage = 25 * scale * heavy; break;
        }
        return result;
    }
    public static ElementGemModifiers Calculate(ItemData item)
    {
        var result = CalculateFixed(item);
        if (item == null) return result;
        foreach (var row in item.gemState.rolls)
        {
            var option = Option(row.optionId);
            float value = Value(item, row);
            if (option.CommonStat.HasValue)
            {
                result.Common.AddValue(option.CommonStat.Value, option.Unit == ElementGemUnit.RatioBps ? value / 100 : value);
                continue;
            }
            float percent = value / 100;
            switch (option.Id)
            {
                case "gem.fire.burn_damage": result.BurnDamage += percent; break;
                case "gem.fire.chain_explosion_damage": result.ExplosionDamage += percent; break;
                case "gem.ice.freeze_threshold_reduction": result.FreezeReduction += (int)value; break;
                case "gem.ice.freeze_duration": result.FreezeDuration += percent; break;
                case "gem.ice.chill_slow": result.ChillSlow += percent; break;
                case "gem.ice.shatter_damage": result.ShatterDamage += percent; break;
                case "gem.electric.chain_hops": result.ChainHops += (int)value; break;
                case "gem.electric.chain_damage": result.ChainDamage += percent; break;
                case "gem.electric.shock_damage": result.ShockDamage += percent; break;
                case "gem.dark.projectile_damage": result.ProjectileDamage += percent; break;
                case "gem.dark.corrosion_max_stacks": result.CorrosionExtra += (int)value; break;
                case "gem.light.radiance_max_stacks": result.RadianceExtra += (int)value; break;
            }
        }
        return result;
    }
}
