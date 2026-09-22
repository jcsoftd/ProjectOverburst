using UnityEngine;

// Prototype values are centralized here; combo position is never an energy level.
[CreateAssetMenu(menuName = "OVERBURST/Combat/Element Tuning")]
public sealed class OverburstElementTuning : ScriptableObject
{
    [Min(1f)] public float maximumEnergy = 100f;
    [Min(0f)] public float energyPerAttack = 10f;
    [Min(1)] public int maximumStacks = 5;
    [Min(0.1f)] public float statusDuration = 8f;
    [Min(0.1f)] public float freezeDuration = 2f;
    [Min(0f)] public float dischargeDamageAtFullEnergy = 2f;
    [Min(0f)] public float statusDamagePerStack = 0.15f;
    [Min(0f)] public float shatterDamage = 1f;
    [Min(0f)] public float minimumRadius = 1.5f;
    [Min(0f)] public float maximumRadius = 4f;
    [Min(1)] public int maximumChainTargets = 6;
    [Min(0f)] public float maximumWaterPullDistance = 0.8f;
    [Header("Prototype new-weapon weights (not final economy)")]
    public Vector4 fireIceElectricWaterWeights = Vector4.one;
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
    public static bool IsActive(WeaponElement element) => Index(element) >= 0;
    public static int Index(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return 0;
            case WeaponElement.Ice: return 1;
            case WeaponElement.Electric: return 2;
            case WeaponElement.Water: return 3;
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
            case 3: return WeaponElement.Water;
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
            case WeaponElement.Water: return "물";
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
            case WeaponElement.Water: return "수압";
            default: return "에너지";
        }
    }
    public static WeaponElement RollNewWeapon(WeaponItemData data)
    {
        if (data == null) return WeaponElement.None;
        if (IsActive(data.defaultElement)) return data.defaultElement;
        Vector4 weights = OverburstElementTuning.Current.fireIceElectricWaterWeights;
        float sum = 0f;
        for (int i = 0; i < 4; i++) if (OverburstElementTuning.IsFinitePositive(weights[i])) sum += weights[i];
        if (!OverburstElementTuning.IsFinitePositive(sum)) return WeaponElement.None;
        float roll = Random.value * sum;
        WeaponElement last = WeaponElement.None;
        for (int i = 0; i < 4; i++)
        {
            if (!OverburstElementTuning.IsFinitePositive(weights[i])) continue;
            last = At(i);
            roll -= weights[i];
            if (roll <= 0f) return last;
        }
        return last;
    }
}
