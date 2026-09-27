using UnityEngine;
using UnityEngine.Serialization;

// Prototype values are centralized here; combo position is never an energy level.
[CreateAssetMenu(menuName = "OVERBURST/Combat/Element Tuning")]
public sealed class OverburstElementTuning : ScriptableObject
{
    [Min(1f)] public float maximumEnergy = 100f;
    [Min(0f)] public float energyPerAttack = 10f;
    [Range(0f, 1f)] public float criticalEnergyFraction = .2f;
    [Min(1)] public int maximumStacks = 5;
    [Min(0.1f)] public float statusDuration = 8f;
    [Min(0.1f)] public float freezeDuration = 5f;
    [Header("Status ticks and shatter")]
    [Min(0.1f)] public float burnDuration = 5f;
    [Min(0.05f)] public float burnTickInterval = 0.5f;
    [Min(0f)] public float burnTickDamagePerStack = 0.01f;
    [Min(0.05f)] public float shockTickInterval = 1.5f;
    [Min(0f)] public float shockTickDamagePerStack = 0.02f;
    [Min(0f)] public float shockStaggerBase = 0.20f;
    [Min(0f)] public float shockStaggerPerStack = 0.08f;
    [Min(0f)] public float shatterBlastFraction = 1.5f;
    [Min(0f)] public float lightningChainBaseFraction = .25f;
    [Min(0f)] public float lightningChainFractionPerStack = .05f;
    public float LightningChainFraction(int stacks) => Mathf.Max(0f, lightningChainBaseFraction)
        + Mathf.Max(0f, lightningChainFractionPerStack) * (Mathf.Clamp(stacks, 1, 5) - 1);
    public float StatusDuration(WeaponElement element) => element == WeaponElement.Fire ? Mathf.Max(.1f, burnDuration) : Mathf.Max(.1f, statusDuration);
    public float TickInterval(WeaponElement element) => element == WeaponElement.Fire ? Mathf.Max(.05f, burnTickInterval) : element == WeaponElement.Electric ? Mathf.Max(.05f, shockTickInterval) : 0f;
    public float TickCoefficient(WeaponElement element) => element == WeaponElement.Fire ? burnTickDamagePerStack : element == WeaponElement.Electric ? shockTickDamagePerStack : 0f;
    [Min(0f)] public float dischargeDamageAtFullEnergy = 2f;
    [Min(0f)] public float statusDamagePerStack = 0.15f;
    [Min(0f)] public float shatterDamage = 1f;
    [Min(0f)] public float minimumRadius = 1.5f;
    [Min(0f)] public float maximumRadius = 4f;
    [Min(1)] public int maximumChainTargets = 6;
    [Header("Dark heavy pull (prototype)")]
    [Min(0.01f)] public float darkPullDuration = 0.30f;
    [Min(0.01f)] public float darkPullDistance = 1.25f;
    [Min(1)] public int darkPullMaxTargets = 24;
    [Header("Light heavy afterglow (prototype)")]
    [Min(0.01f)] public float lightAfterglowDelay = 0.12f;
    [Min(0.01f)] public float lightAfterglowDuration = 0.25f;
    [Min(1f)] public float lightAfterglowRadiusMultiplier = 1.2f;
    [Range(0f, 1f)] public float lightAfterglowDamageFraction = 0.35f;
    // Existing assets lack these fields until saved through the Editor. Keep their runtime defaults valid.
    public float SafeDarkPullDuration => darkPullDuration > 0f ? darkPullDuration : 0.30f;
    public float SafeDarkPullDistance => darkPullDistance > 0f ? darkPullDistance : 1.25f;
    public int SafeDarkPullMaxTargets => darkPullMaxTargets > 0 ? darkPullMaxTargets : 24;
    public float SafeLightAfterglowDelay => lightAfterglowDelay > 0f ? lightAfterglowDelay : 0.12f;
    public float SafeLightAfterglowDuration => lightAfterglowDuration > 0f ? lightAfterglowDuration : 0.25f;
    public float SafeLightAfterglowRadiusMultiplier => lightAfterglowRadiusMultiplier >= 1f ? lightAfterglowRadiusMultiplier : 1.2f;
    public float SafeLightAfterglowDamageFraction => lightAfterglowDamageFraction > 0f ? Mathf.Clamp01(lightAfterglowDamageFraction) : 0.35f;
    [Header("Prototype new-weapon weights (not final economy)")]
    [FormerlySerializedAs("fireIceElectricWaterWeights")]
    public Vector4 fireIceElectricDarkWeights = Vector4.one;
    [Min(0f)] public float lightWeight = 1f;
    private static OverburstElementTuning cached;
    public static OverburstElementTuning Current
    {
        get
        {
            if (cached != null) return cached;
            cached = Resources.Load<OverburstElementTuning>("Combat/OverburstElementTuning");
            if (cached == null)
            {
                cached = CreateInstance<OverburstElementTuning>();
                cached.hideFlags = HideFlags.HideAndDontSave;
            }
            return cached;
        }
    }
    public static bool IsFinitePositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

public static class OverburstElementRules
{
    public const int Count = 5;
    public static WeaponElement MigrateLegacy(WeaponElement element) =>
        element == WeaponElement.Water ? WeaponElement.Dark : element;
    public static bool IsActive(WeaponElement element) => Index(element) >= 0;
    public static int Index(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return 0;
            case WeaponElement.Ice: return 1;
            case WeaponElement.Electric: return 2;
            case WeaponElement.Dark: return 3;
            case WeaponElement.Light: return 4;
            default: return -1;
        }
    }
    public static WeaponElement At(int index)
    {
        switch (index)
        {
            case 0: return WeaponElement.Fire;
            case 1: return WeaponElement.Ice;
            case 2: return WeaponElement.Electric;
            case 3: return WeaponElement.Dark;
            case 4: return WeaponElement.Light;
            default: return WeaponElement.None;
        }
    }
    public static string Label(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return "불";
            case WeaponElement.Ice: return "얼음";
            case WeaponElement.Electric: return "번개";
            case WeaponElement.Dark: return "어둠";
            case WeaponElement.Light: return "빛";
            default: return string.Empty;
        }
    }
    public static string EnergyLabel(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return "열기";
            case WeaponElement.Ice: return "냉기";
            case WeaponElement.Electric: return "전하";
            case WeaponElement.Dark: return "암흑";
            case WeaponElement.Light: return "광휘";
            default: return "에너지";
        }
    }
    public static WeaponElement RollNewWeapon(WeaponItemData data)
    {
        if (data == null) return WeaponElement.None;
        WeaponElement authored = MigrateLegacy(data.defaultElement);
        if (IsActive(authored)) return authored;
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        Vector4 weights = tuning.fireIceElectricDarkWeights;
        float sum = 0f;
        for (int i = 0; i < Count; i++)
        {
            float weight = i == 4 ? tuning.lightWeight : weights[i];
            if (OverburstElementTuning.IsFinitePositive(weight)) sum += weight;
        }
        if (!OverburstElementTuning.IsFinitePositive(sum)) return WeaponElement.None;
        float roll = Random.value * sum;
        WeaponElement last = WeaponElement.None;
        for (int i = 0; i < Count; i++)
        {
            float weight = i == 4 ? tuning.lightWeight : weights[i];
            if (!OverburstElementTuning.IsFinitePositive(weight)) continue;
            last = At(i);
            roll -= weight;
            if (roll <= 0f) return last;
        }
        return last;
    }
}
