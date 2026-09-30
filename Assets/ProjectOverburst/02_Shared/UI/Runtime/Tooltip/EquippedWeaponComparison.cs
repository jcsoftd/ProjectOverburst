using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

public enum WeaponCompareVerdict { None, Better, Similar, Worse }

// 툴팁을 누가 그리는지: Auto = 장착 무기와 비교, EquippedReference = Alt 나란히 보기의 "장착 중" 쪽.
public enum TooltipCompareMode { Auto, EquippedReference }

/// <summary>
/// 2026-10-01 장착 무기 비교. 툴팁 행별 "장착 대비"와 인벤토리 아이콘 ▲가 이 판정을 쓴다.
/// 무기 ▲의 기준은 툴팁에 이미 있는 단일 DPS 하나이고 ±1% 안은 "비슷함"이다.
/// 방어구·장신구는 두 번 클릭하면 바꿔 낄 칸의 장비와 능력치별로 비교해 툴팁에 모두 보이고,
/// ▲는 주능력치(첫 행) 하나만 본다(사용자 결정). 보조능력치·빠지는 능력치는 툴팁에서만 보인다.
/// Enabled=false면 전부, GearEnabled=false면 방어구·장신구만 비교 전 화면으로 돌아간다(롤백 스위치).
/// </summary>
public static class EquippedWeaponComparison
{
    public static bool Enabled = true;
    public static bool GearEnabled = true;

    public const float SimilarBand = .01f;
    public const string BetterColor = "#8FD68F";
    public const string WorseColor = "#E0857A";
    public const string SimilarColor = "#8C857C";
    // 툴팁 글꼴(Pretendard)에는 ▲▼가 있지만 슬롯 글꼴(Noto Serif 정적, Liberation)에는 없어 슬롯 ▲는 이 글꼴로 그린다.
    private const string CompareFontResource = "UI/Fonts/DamageFloating/Pretendard_Medium SDF";
    private const int CacheLimit = 256;
    private const int GearSlotCount = 7;

    public sealed class StatDelta
    {
        public string Label;
        public string Text;
        public bool Improved;
        public bool Equal;
    }

    public sealed class Result
    {
        public ItemData Candidate;
        public ItemData Equipped;
        public bool IsEquippedItem;
        public bool IsComparable;
        public WeaponCompareVerdict Verdict;
        public readonly Dictionary<string, StatDelta> Stats = new Dictionary<string, StatDelta>(StringComparer.Ordinal);
        // 방어구·장신구: 장착 장비에만 있어 바꿔 끼면 빠지는 능력치(행 순서대로).
        public readonly List<StatDelta> Lost = new List<StatDelta>();
    }

    private readonly struct Snapshot
    {
        public readonly bool Valid;
        public readonly float Damage, AttackSpeed, Range, CritChance, CritDamage, Knockback, Dps, Discharge;

        public Snapshot(ItemData item)
        {
            Valid = false; Damage = AttackSpeed = Range = CritChance = CritDamage = Knockback = Dps = Discharge = 0f;
            if (item == null || !(item.baseData is WeaponItemData weapon)) return;
            WeaponFinalStats stats = WeaponStatCalculator.Calculate(item);
            MeleeSingleTargetDpsEstimate dps = MeleeSingleTargetDpsCalculator.Estimate(weapon, stats);
            if (!dps.IsValid) return;
            Valid = true;
            Damage = stats.damage;
            AttackSpeed = stats.meleeAttackSpeedMultiplier * 100f;
            Range = stats.range;
            CritChance = stats.critChance;
            CritDamage = stats.critDamageMultiplier * 100f;
            Knockback = stats.knockback;
            Dps = dps.Dps;
            Discharge = WeaponStatCalculator.GetElementalDischargePower(item);
        }
    }

    private static readonly Dictionary<ItemData, Snapshot> snapshots = new Dictionary<ItemData, Snapshot>();
    private static PlayerEquipment subscribedEquipment;
    private static string cachedEquippedId;
    private static TMPro.TMP_FontAsset compareFont;

    public static event Action EquippedChanged;

    // 검증 촬영 도구(WeaponCompareCapture)만 켠다. Game 창에 포커스가 없어도 Alt 나란히 보기를 찍기 위한 것.
    public static bool SimulateAltForVerification;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        snapshots.Clear();
        subscribedEquipment = null;
        cachedEquippedId = null;
        compareFont = null;
        EquippedChanged = null;
        SimulateAltForVerification = false;
    }

    public static TMPro.TMP_FontAsset CompareFont
    {
        get
        {
            if (compareFont == null) compareFont = Resources.Load<TMPro.TMP_FontAsset>(CompareFontResource);
            return compareFont;
        }
    }

    public static ItemData ResolveEquippedWeapon()
    {
        PlayerEquipment equipment = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActorEquipment : null;
        TrackEquipment(equipment);
        return equipment != null ? equipment.CurrentWeaponItem : null;
    }

    // Alt 나란히 보기 대상. 같은 아이템이거나 비교할 장착품이 없으면 false.
    public static bool TryGetComparableEquipped(ItemData candidate, out ItemData equipped)
    {
        equipped = null;
        if (!Enabled || candidate == null) return false;
        if (candidate.baseData is GearItemData gear)
        {
            if (!GearEnabled || !HasRolls(candidate) || IsGearEquipped(candidate)) return false;
            equipped = ResolveEquippedGear(gear);
            return equipped != null && HasRolls(equipped);
        }
        if (!(candidate.baseData is WeaponItemData)) return false;
        equipped = ResolveEquippedWeapon();
        return equipped != null && !IsSame(candidate, equipped) && Get(candidate).Valid && Get(equipped).Valid;
    }

    public static WeaponCompareVerdict GetVerdict(ItemData candidate)
    {
        if (!Enabled || candidate == null) return WeaponCompareVerdict.None;
        if (candidate.baseData is GearItemData)
        {
            Result gear = Compare(candidate, TooltipCompareMode.Auto);
            return gear != null && gear.IsComparable ? gear.Verdict : WeaponCompareVerdict.None;
        }
        if (!(candidate.baseData is WeaponItemData)) return WeaponCompareVerdict.None;
        ItemData equipped = ResolveEquippedWeapon();
        if (equipped == null || IsSame(candidate, equipped)) return WeaponCompareVerdict.None;
        Snapshot a = Get(candidate), b = Get(equipped);
        if (!a.Valid || !b.Valid) return WeaponCompareVerdict.None;
        return VerdictOf(Ratio(a.Dps, b.Dps));
    }

    public static Result Compare(ItemData candidate, TooltipCompareMode mode)
    {
        if (!Enabled || candidate == null) return null;
        if (candidate.baseData is GearItemData gear) return CompareGear(candidate, gear, mode);
        if (!(candidate.baseData is WeaponItemData)) return null;
        // 툴팁은 한 번에 하나라 캐시 대신 매번 새로 계산한다(품질 재굴림 직후에도 정확하게).
        Snapshot a = new Snapshot(candidate);
        if (!a.Valid) return null;
        ItemData equipped = ResolveEquippedWeapon();
        var result = new Result { Candidate = candidate, Equipped = equipped };
        if (mode == TooltipCompareMode.EquippedReference || (equipped != null && IsSame(candidate, equipped)))
        {
            result.IsEquippedItem = true;
            return result;
        }
        if (equipped == null) return null;
        Snapshot b = new Snapshot(equipped);
        if (!b.Valid) return null;

        result.IsComparable = true;
        result.Verdict = VerdictOf(Ratio(a.Dps, b.Dps));
        // 라벨은 SimpleItemTooltipBuilder가 쓰는 행 이름 그대로다(툴팁 행을 라벨로 찾는다).
        Add(result, "데미지", a.Damage, b.Damage, 0, string.Empty);
        Add(result, "공격속도", a.AttackSpeed, b.AttackSpeed, 0, "%p");
        Add(result, "공격 범위", a.Range, b.Range, 1, "m");
        Add(result, "치명타확률", a.CritChance, b.CritChance, 0, "%p");
        Add(result, "치명타피해", a.CritDamage, b.CritDamage, 0, "%p");
        Add(result, "넉백", a.Knockback, b.Knockback, 0, string.Empty);
        Add(result, "단일 DPS", a.Dps, b.Dps, 1, string.Empty);
        Add(result, "강공 방출", a.Discharge, b.Discharge, 1, string.Empty);
        return result;
    }

    public static bool IsAltHeld()
    {
        if (SimulateAltForVerification) return true;
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
    }

    private static void Add(Result result, string label, float candidate, float equipped, int decimals, string unit)
        => result.Stats[label] = Delta(label, candidate, equipped, decimals, unit);

    private static StatDelta Delta(string label, float candidate, float equipped, int decimals, string unit)
    {
        float scale = decimals == 0 ? 1f : Mathf.Pow(10f, decimals);
        // 툴팁 값("0.##")과 같게 .5는 올린다. Mathf.Round는 짝수 쪽으로 반올림해 1.625가 1.62가 된다.
        double a = Math.Round(candidate, decimals, MidpointRounding.AwayFromZero);
        double b = Math.Round(equipped, decimals, MidpointRounding.AwayFromZero);
        float delta = (float)(a - b);
        var stat = new StatDelta { Label = label, Equal = Mathf.Abs(delta) < .5f / scale, Improved = delta > 0f };
        stat.Text = stat.Equal
            ? "<color=" + SimilarColor + ">–</color>"
            : "<color=" + (stat.Improved ? BetterColor : WorseColor) + ">" + (stat.Improved ? "▲" : "▼") + Format(Mathf.Abs(delta), decimals) + unit + "</color>";
        return stat;
    }

    // 방어구·장신구: 행마다 같은 능력치끼리 비교한다. 장착 장비에 없는 능력치는 0에서 오른 것으로,
    // 장착 장비에만 있는 능력치는 Lost(빠지는 능력치)로 둔다. 값은 툴팁에 보이는 것과 같은 GearQuality.Value다.
    private static Result CompareGear(ItemData candidate, GearItemData gear, TooltipCompareMode mode)
    {
        if (!GearEnabled || !HasRolls(candidate)) return null;
        var result = new Result { Candidate = candidate };
        if (mode == TooltipCompareMode.EquippedReference || IsGearEquipped(candidate))
        {
            result.IsEquippedItem = true;
            return result;
        }
        ItemData equipped = ResolveEquippedGear(gear);
        if (equipped == null || !HasRolls(equipped)) return null;
        result.Equipped = equipped;
        result.IsComparable = true;

        foreach (GearStatRoll row in candidate.gearRolls)
        {
            if (row == null) continue;
            GearStatRoll other = FindRoll(equipped, row.stat);
            StatDelta stat = GearDelta(row.stat, GearQuality.Value(candidate, row),
                other != null ? GearQuality.Value(equipped, other) : 0f);
            result.Stats[stat.Label] = stat;
        }
        foreach (GearStatRoll row in equipped.gearRolls)
        {
            if (row == null || FindRoll(candidate, row.stat) != null) continue;
            result.Lost.Add(GearDelta(row.stat, 0f, GearQuality.Value(equipped, row)));
        }
        // 인벤토리 ▲: 주능력치(첫 행, 같은 종류면 같은 능력치)만 본다.
        GearStatRoll main = candidate.gearRolls[0];
        StatDelta mainDelta = main != null ? GetOrNull(result, SimpleItemTooltipBuilder.GearStatLabel(main.stat)) : null;
        result.Verdict = mainDelta == null ? WeaponCompareVerdict.None
            : mainDelta.Equal ? WeaponCompareVerdict.Similar
            : mainDelta.Improved ? WeaponCompareVerdict.Better : WeaponCompareVerdict.Worse;
        return result;
    }

    private static StatDelta GetOrNull(Result result, string label)
        => result.Stats.TryGetValue(label, out StatDelta stat) ? stat : null;

    private static StatDelta GearDelta(GearStat stat, float candidate, float equipped)
    {
        bool flat = stat == GearStat.MaxHealth || stat == GearStat.Armor || stat == GearStat.Attack;
        return Delta(SimpleItemTooltipBuilder.GearStatLabel(stat), candidate, equipped, flat ? 0 : 2, flat ? string.Empty : "%p");
    }

    // 두 번 클릭 장착(GearEquipmentService)이 바꿔 낄 칸의 장비. 귀걸이는 빈 칸이 있으면 그 칸에 들어가므로 비교 대상이 없다.
    public static ItemData ResolveEquippedGear(GearItemData gear)
    {
        PlayerEquipment equipment = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActorEquipment : null;
        TrackEquipment(equipment);
        if (equipment == null || gear == null) return null;
        return equipment.GetGearSlotItem(GearEquipmentService.DefaultSlot(gear.kind, equipment));
    }

    private static bool IsGearEquipped(ItemData item)
    {
        PlayerEquipment equipment = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActorEquipment : null;
        if (equipment == null) return false;
        for (int i = 0; i < GearSlotCount; i++)
        {
            ItemData equipped = equipment.GetGearSlotItem(i);
            if (equipped != null && IsSame(item, equipped)) return true;
        }
        return false;
    }

    private static bool HasRolls(ItemData item) => item.gearRolls != null && item.gearRolls.Count > 0;

    private static GearStatRoll FindRoll(ItemData item, GearStat stat)
        => item.gearRolls.Find(row => row != null && row.stat == stat);

    private static Snapshot Get(ItemData item)
    {
        if (item == null) return default;
        if (snapshots.TryGetValue(item, out Snapshot snapshot)) return snapshot;
        if (snapshots.Count >= CacheLimit) snapshots.Clear();
        snapshot = new Snapshot(item);
        snapshots[item] = snapshot;
        return snapshot;
    }

    private static void TrackEquipment(PlayerEquipment equipment)
    {
        if (equipment != subscribedEquipment)
        {
            if (subscribedEquipment != null)
            {
                subscribedEquipment.WeaponSlotsChanged -= HandleSlotsChanged;
                subscribedEquipment.GearSlotsChanged -= HandleSlotsChanged;
            }
            subscribedEquipment = equipment;
            if (subscribedEquipment != null)
            {
                subscribedEquipment.WeaponSlotsChanged += HandleSlotsChanged;
                subscribedEquipment.GearSlotsChanged += HandleSlotsChanged;
            }
            RefreshEquippedId();
        }
    }

    private static void HandleSlotsChanged() => RefreshEquippedId();

    // 무기와 방어구·장신구 7칸을 한 줄로 이어 붙여, 어느 칸이든 바뀌면 인벤토리 ▲를 다시 그리게 한다.
    private static void RefreshEquippedId()
    {
        string id = null;
        if (subscribedEquipment != null)
        {
            var builder = new StringBuilder();
            AppendId(builder, subscribedEquipment.CurrentWeaponItem);
            for (int i = 0; i < GearSlotCount; i++) AppendId(builder.Append('|'), subscribedEquipment.GetGearSlotItem(i));
            id = builder.ToString();
        }
        if (id == cachedEquippedId) return;
        cachedEquippedId = id;
        // 품질 재굴림처럼 같은 아이템의 값이 바뀌는 경로도 장착 변경 때 한 번 비워 따라간다.
        snapshots.Clear();
        EquippedChanged?.Invoke();
    }

    private static void AppendId(StringBuilder builder, ItemData item)
    {
        if (item == null) return;
        builder.Append(string.IsNullOrEmpty(item.runtimeInstanceId)
            ? "#" + RuntimeHelpers.GetHashCode(item).ToString(CultureInfo.InvariantCulture)
            : item.runtimeInstanceId);
    }

    private static bool IsSame(ItemData a, ItemData b)
    {
        if (ReferenceEquals(a, b)) return true;
        return !string.IsNullOrEmpty(a.runtimeInstanceId) && a.runtimeInstanceId == b.runtimeInstanceId;
    }

    private static float Ratio(float candidate, float equipped) => equipped > .0001f ? candidate / equipped - 1f : 0f;

    private static WeaponCompareVerdict VerdictOf(float change)
        => change > SimilarBand ? WeaponCompareVerdict.Better : change < -SimilarBand ? WeaponCompareVerdict.Worse : WeaponCompareVerdict.Similar;

    private static string Format(float value, int decimals)
        => decimals == 0 ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(decimals == 1 ? "0.#" : "0.##", CultureInfo.InvariantCulture);
}
