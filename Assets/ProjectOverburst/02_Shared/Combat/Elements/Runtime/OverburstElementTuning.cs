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
    // 60D upper elements. Existing assets lack these fields until saved; zero means "use the design start value".
    [Header("Light radiance overcharge (60D)")]
    [Min(0f)] public float lightOverchargeMaximum = 200f;
    [Min(0f)] public float lightOverchargeDecayPerSecond = 3f;
    [Min(0f)] public float lightOverchargeFullHold = 2f;
    [Min(0)] public int lightRadianceStacksPerPhase = 5;
    [Min(0)] public int lightRadianceMaxStacks = 100;
    [Min(0f)] public float lightRadianceAttackSpeedPerStack = 0.002f;
    [Min(0f)] public float lightFinalAttackSpeedCap = 1.8f;
    [Header("Light triple impact (60D)")]
    public Vector3 lightTripleRadiusScale = new Vector3(0.43f, 0.57f, 1f);
    [Min(0f)] public float lightTripleHit1Base = 0.2f;
    [Min(0f)] public float lightTripleHit1PerStack = 1.6f;
    [Min(0f)] public float lightTripleHit2Base = 0.65f;
    [Min(0f)] public float lightTripleHit2PerOvercharge = 0.05f;
    [Min(0f)] public float lightTripleHit3Scale = 0.8f;
    [Min(0f)] public float lightTripleVfxPlaybackSpeed = 1.25f;
    [Tooltip("Rune_Multi_Impact original times of the 1st/2nd/3rd explosion bursts (seconds at 1x).")]
    public Vector3 lightTripleSourceHitTimes = new Vector3(1f, 2f, 3f);
    [Header("Dark corrosion magnet (60D)")]
    [Min(0f)] public float darkMagnetRadius = 3f;
    [Min(0f)] public float darkMagnetTickInterval = 0.1f;
    [Min(0f)] public float darkMagnetSpeedPerStack = 0.08f;
    public Vector4 darkMagnetMass = new Vector4(1f, 2f, 4f, 6f);
    [Header("Dark gather burst (60D, Demon_Runic_Explotion times at 1x)")]
    [Min(0f)] public float darkGatherStart = 0.6f;
    [Min(0f)] public float darkGatherEnd = 1.4f;
    [Min(0f)] public float darkBurstTime = 1.95f;
    [Min(0f)] public float darkGatherHoldSpeedFraction = 0.25f;
    [Min(0f)] public float darkVfxPlaybackSpeed = 1f;
    [Min(0f)] public float darkGatherRadiusMultiplier = 2.5f;
    [Min(0f)] public float darkGatherInnerRadius = 0.8f;
    [Min(0)] public int darkGatherMaxTargets = 48;
    [Min(0f)] public float darkBurstBaseFraction = 0.2f;
    [Min(0f)] public float darkBurstPerStack = 0.02f;
    [Min(0)] public int darkBurstStackCap = 50;
    // 2026-10-01: the magnet and gather-burst fields above are kept for serialization only. The corrosion
    // barrage reuses darkGatherRadiusMultiplier as its search radius (heavy radius x 2.5).
    [Header("Dark corrosion barrage (60D 4, 2026-10-01)")]
    [Min(0)] public int darkBarrageMinTargets = 8;
    [Min(0)] public int darkBarrageMaxTargets = 40;
    [Tooltip("일반탄 피해(H 배수). H는 강공 확정 때의 FirstBlastDamage")]
    [Min(0f)] public float darkBarrageShotDamage = 0.08f;
    [Tooltip("완충 대형탄 피해(H 배수)")]
    [Min(0f)] public float darkBarrageFinisherDamage = 0.30f;
    [Tooltip("공중에 모인 탄을 묶음으로 쏘는 간격(초). 비행 속도와는 별도다")]
    [Min(0.005f)] public float darkBarrageFireInterval = 0.10f;
    [Tooltip("한 묶음에서 함께 출발하는 탄 수. 총 탄 수는 강공이 소비한 잠식 중첩 합계다")]
    [Range(2, 4)] public int darkBarrageShotsPerVolley = 3;
    [Min(0)] public int darkBarrageMaxLaunchPerFrame = 24;
    [Min(0)] public int darkBarrageMaxConcurrent = 3;
    [Tooltip("착지점에서 공중 대기 위치까지 솟는 시간")]
    [Min(0f)] public float darkBarrageRiseTime = 0.25f;
    [Tooltip("탄마다 솟기 시작을 늦추는 최대 무작위 시간")]
    [Min(0f)] public float darkBarrageRiseStagger = 0.08f;
    [Tooltip("공중 대기 높이 범위(착지점 기준 m)")]
    public Vector2 darkBarrageRiseHeight = new Vector2(2.8f, 3.6f);
    [Tooltip("공중 대기 원반 반경(m)")]
    [Min(0f)] public float darkBarrageHoverRadius = 1.6f;
    [Min(0f)] public float darkBarrageSpeed = 14f;
    public Vector2 darkBarrageFlightTime = new Vector2(0.3f, 0.9f);
    [Tooltip("무작위 곡선 휘는 정도(거리 배수, 0.8~3m로 제한)")]
    [Min(0f)] public float darkBarrageCurveAmount = 0.4f;
    [Min(0f)] public float darkBarrageMaxLifetime = 1.2f;
    [Min(0)] public int darkBarrageHitVfxCap = 48;
    [Min(0)] public int darkBarrageProjectileVfxCap = 160;
    [Min(0f)] public float darkBarrageSfxMinInterval = 0.04f;
    [Tooltip("대형탄 배율: x 투사체, y 명중 폭발")]
    public Vector2 darkBarrageFinisherScale = new Vector2(1.8f, 1.5f);
    private static float Positive(float value, float fallback) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
    public float SafeLightOverchargeMaximum => Mathf.Max(Mathf.Max(1f, maximumEnergy), Positive(lightOverchargeMaximum, 200f));
    public float SafeLightOverchargeDecayPerSecond => Positive(lightOverchargeDecayPerSecond, 3f);
    public float SafeLightOverchargeFullHold => Positive(lightOverchargeFullHold, 2f);
    public int SafeLightRadianceStacksPerPhase => lightRadianceStacksPerPhase > 0 ? lightRadianceStacksPerPhase : 5;
    public int SafeLightRadianceMaxStacks => lightRadianceMaxStacks > 0 ? lightRadianceMaxStacks : 100;
    public float SafeLightRadianceAttackSpeedPerStack => Positive(lightRadianceAttackSpeedPerStack, 0.002f);
    public float SafeLightFinalAttackSpeedCap => Positive(lightFinalAttackSpeedCap, 1.8f);
    public float LightTripleRadiusScale(int hit)
    {
        float value = hit == 0 ? lightTripleRadiusScale.x : hit == 1 ? lightTripleRadiusScale.y : lightTripleRadiusScale.z;
        return Positive(value, hit == 0 ? 0.43f : hit == 1 ? 0.57f : 1f);
    }
    public float SafeLightTripleHit1Base => lightTripleHit1Base > 0f ? lightTripleHit1Base : 0.2f;
    public float SafeLightTripleHit1PerStack => Positive(lightTripleHit1PerStack, 1.6f);
    public float SafeLightTripleHit2Base => Positive(lightTripleHit2Base, 0.65f);
    public float SafeLightTripleHit2PerOvercharge => Positive(lightTripleHit2PerOvercharge, 0.05f);
    public float SafeLightTripleHit3Scale => Positive(lightTripleHit3Scale, 0.8f);
    public float SafeLightTripleVfxPlaybackSpeed => Positive(lightTripleVfxPlaybackSpeed, 1.25f);
    public float LightTripleSourceHitTime(int hit)
    {
        float value = hit == 0 ? lightTripleSourceHitTimes.x : hit == 1 ? lightTripleSourceHitTimes.y : lightTripleSourceHitTimes.z;
        return Positive(value, hit + 1f);
    }
    public float SafeDarkMagnetRadius => Positive(darkMagnetRadius, 3f);
    public float SafeDarkMagnetTickInterval => Positive(darkMagnetTickInterval, 0.1f);
    public float SafeDarkMagnetSpeedPerStack => Positive(darkMagnetSpeedPerStack, 0.08f);
    public float DarkMagnetMass(EnemyGradeType grade)
    {
        float value = grade == EnemyGradeType.Normal ? darkMagnetMass.x : grade == EnemyGradeType.Elite ? darkMagnetMass.y
            : grade == EnemyGradeType.GreaterElite ? darkMagnetMass.z : darkMagnetMass.w;
        return Positive(value, grade == EnemyGradeType.Normal ? 1f : grade == EnemyGradeType.Elite ? 2f
            : grade == EnemyGradeType.GreaterElite ? 4f : 6f);
    }
    public float SafeDarkGatherStart => Positive(darkGatherStart, 0.6f);
    public float SafeDarkGatherEnd => Mathf.Max(SafeDarkGatherStart + 0.05f, Positive(darkGatherEnd, 1.4f));
    public float SafeDarkBurstTime => Mathf.Max(SafeDarkGatherEnd, Positive(darkBurstTime, 1.95f));
    public float SafeDarkGatherHoldSpeedFraction => Positive(darkGatherHoldSpeedFraction, 0.25f);
    public float SafeDarkVfxPlaybackSpeed => Positive(darkVfxPlaybackSpeed, 1f);
    public float SafeDarkGatherRadiusMultiplier => Mathf.Max(1f, Positive(darkGatherRadiusMultiplier, 2.5f));
    public float SafeDarkGatherInnerRadius => Positive(darkGatherInnerRadius, 0.8f);
    public int SafeDarkGatherMaxTargets => darkGatherMaxTargets > 0 ? darkGatherMaxTargets : 48;
    public float SafeDarkBurstBaseFraction => Positive(darkBurstBaseFraction, 0.2f);
    public float SafeDarkBurstPerStack => Positive(darkBurstPerStack, 0.02f);
    public int SafeDarkBurstStackCap => darkBurstStackCap > 0 ? darkBurstStackCap : 50;
    public int SafeDarkBarrageMinTargets => darkBarrageMinTargets > 0 ? darkBarrageMinTargets : 8;
    public int SafeDarkBarrageMaxTargets => Mathf.Max(SafeDarkBarrageMinTargets, darkBarrageMaxTargets > 0 ? darkBarrageMaxTargets : 40);
    public int DarkBarrageTargetLimit(float normalizedEnergy)
        => Mathf.RoundToInt(Mathf.Lerp(SafeDarkBarrageMinTargets, SafeDarkBarrageMaxTargets, Mathf.Clamp01(normalizedEnergy)));
    public float SafeDarkBarrageShotDamage => Positive(darkBarrageShotDamage, 0.08f);
    public float SafeDarkBarrageFinisherDamage => Positive(darkBarrageFinisherDamage, 0.30f);
    public float SafeDarkBarrageFireInterval => Mathf.Max(0.005f, Positive(darkBarrageFireInterval, 0.10f));
    public int SafeDarkBarrageShotsPerVolley => Mathf.Clamp(darkBarrageShotsPerVolley > 0 ? darkBarrageShotsPerVolley : 3, 2, 4);
    public int SafeDarkBarrageMaxLaunchPerFrame => darkBarrageMaxLaunchPerFrame > 0 ? darkBarrageMaxLaunchPerFrame : 24;
    public int SafeDarkBarrageMaxConcurrent => darkBarrageMaxConcurrent > 0 ? darkBarrageMaxConcurrent : 3;
    public float SafeDarkBarrageRiseTime => Positive(darkBarrageRiseTime, 0.25f);
    public float SafeDarkBarrageRiseStagger => Mathf.Max(0f, darkBarrageRiseStagger);
    public float SafeDarkBarrageRiseHeightMin => Positive(darkBarrageRiseHeight.x, 2.8f);
    public float SafeDarkBarrageRiseHeightMax => Mathf.Max(SafeDarkBarrageRiseHeightMin, Positive(darkBarrageRiseHeight.y, 3.6f));
    public float SafeDarkBarrageHoverRadius => Mathf.Max(0f, darkBarrageHoverRadius);
    public float SafeDarkBarrageSpeed => Positive(darkBarrageSpeed, 14f);
    public float SafeDarkBarrageFlightMin => Positive(darkBarrageFlightTime.x, 0.3f);
    public float SafeDarkBarrageFlightMax => Mathf.Max(SafeDarkBarrageFlightMin, Positive(darkBarrageFlightTime.y, 0.9f));
    public float SafeDarkBarrageCurveAmount => Mathf.Max(0f, darkBarrageCurveAmount);
    // Counted from release (leaving the hover), not from spawn: shots may wait in the air for a long barrage.
    public float SafeDarkBarrageMaxLifetime => Mathf.Max(SafeDarkBarrageFlightMax, Positive(darkBarrageMaxLifetime, 1.2f));
    public int SafeDarkBarrageHitVfxCap => darkBarrageHitVfxCap > 0 ? darkBarrageHitVfxCap : 48;
    public int SafeDarkBarrageProjectileVfxCap => darkBarrageProjectileVfxCap > 0 ? darkBarrageProjectileVfxCap : 160;
    public float SafeDarkBarrageSfxMinInterval => Positive(darkBarrageSfxMinInterval, 0.04f);
    public float SafeDarkBarrageFinisherProjectileScale => Positive(darkBarrageFinisherScale.x, 1.8f);
    public float SafeDarkBarrageFinisherHitScale => Positive(darkBarrageFinisherScale.y, 1.5f);
    // Pull resistance by grade: normal moves fully, bosses never move.
    public static float GradeMoveResistance(EnemyGradeType grade)
    {
        switch (grade)
        {
            case EnemyGradeType.Normal: return 1f;
            case EnemyGradeType.Elite: return 0.5f;
            case EnemyGradeType.GreaterElite: return 0.25f;
            default: return 0f;
        }
    }
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
