using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OverburstElementEnergy : MonoBehaviour
{
    private PlayerEquipment equipment;
    private CombatHealth health;
    private readonly Dictionary<long, float> attacks = new Dictionary<long, float>();
    private readonly Queue<long> attackOrder = new Queue<long>();
    private int generation;
    public WeaponElement Element { get; private set; }
    public string WeaponInstanceId { get; private set; } = string.Empty;
    public float Amount { get; private set; }
    public float BaseMaximum => Mathf.Max(1f, OverburstElementTuning.Current.maximumEnergy);
    // Light alone stores past the shared 100 base. Every existing consumer keeps reading the 0..1 base ratio.
    public float Capacity => Element == WeaponElement.Light ? OverburstElementTuning.Current.SafeLightOverchargeMaximum : BaseMaximum;
    public float Normalized => Mathf.Clamp01(Amount / BaseMaximum);
    public bool IsOvercharged => Element == WeaponElement.Light && Amount > BaseMaximum + 0.0001f;
    public float OverchargeNormalized => Element != WeaponElement.Light ? 0f
        : Mathf.Clamp01((Amount - BaseMaximum) / Mathf.Max(0.0001f, Capacity - BaseMaximum));
    // Radiance exists only inside the 101..200 light band and is consumed by the heavy.
    public int RadianceStacks { get; private set; }
    public float RadianceNormalized => RadianceStacks / (float)Mathf.Max(1, OverburstElementTuning.Current.SafeLightRadianceMaxStacks);
    public float FullHoldRemaining => Mathf.Max(0f, holdUntil - Time.time);
    private float holdUntil;
    private float changedThrottle;
    public event Action Changed;

    private void OnEnable()
    {
        equipment = GetComponent<PlayerEquipment>();
        health = GetComponent<CombatHealth>();
        if (equipment != null) equipment.WeaponSlotsChanged += SyncWeapon;
        if (health != null) { health.OnDead += Died; health.OnReset += ResetHealth; }
        // 60D 빛: 광휘 중첩 동안 플레이어 몸 발광(HolyAura 변형).
        if (equipment != null && GetComponent<LightRadianceAuraPresenter>() == null) gameObject.AddComponent<LightRadianceAuraPresenter>();
        SyncWeapon();
    }
    private void OnDisable()
    {
        if (equipment != null) equipment.WeaponSlotsChanged -= SyncWeapon;
        if (health != null) { health.OnDead -= Died; health.OnReset -= ResetHealth; }
        Clear();
    }
    private void Died(CombatHealth _, DamageInfo info) => Clear();
    private void ResetHealth(CombatHealth _) => Clear();
    private void SyncWeapon()
    {
        ItemData item = equipment != null ? equipment.CurrentWeaponItem : null;
        bool changed = WeaponInstanceId != (item != null ? item.runtimeInstanceId : string.Empty)
            || Element != (item != null ? item.ResolvedElement : WeaponElement.None);
        BindWeapon(item != null ? item.runtimeInstanceId : string.Empty, item != null ? item.ResolvedElement : WeaponElement.None);
        if (changed) MeleeHeavyVfxPreparation.RequestForEquippedWeapon(equipment, Element);
    }
    public void BindWeapon(string weaponId, WeaponElement element)
    {
        weaponId = weaponId ?? string.Empty;
        if (!OverburstElementRules.IsActive(element)) element = WeaponElement.None;
        if (WeaponInstanceId == weaponId && Element == element) return;
        Clear();
        WeaponInstanceId = weaponId;
        Element = element;
        Changed?.Invoke();
    }
    public bool RecordConfirmedHit(string weaponId, WeaponElement element, int attackSequenceId, float actualDamage,
        bool isCritical = false, int attackPhaseIndex = 0)
    {
        if (!isActiveAndEnabled || (health != null && health.IsDead)
            || !OverburstElementTuning.IsFinitePositive(actualDamage) || attackSequenceId <= 0
            || string.IsNullOrWhiteSpace(weaponId) || !OverburstElementRules.IsActive(element)) return false;
        if (equipment != null) SyncWeapon();
        if (WeaponInstanceId != weaponId || Element != element) return false;
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        long key = ((long)attackSequenceId << 32) | (uint)Mathf.Max(0, attackPhaseIndex);
        float baseGain = isCritical ? Mathf.Max(1f, tuning.maximumEnergy) * Mathf.Clamp01(tuning.criticalEnergyFraction)
            : Mathf.Max(0f, tuning.energyPerAttack);
        float gain = baseGain * (1f + FlaskCombatModifiers.Bonus(gameObject, FlaskEffect.EnergyGain));
        bool alreadyHit = attacks.TryGetValue(key, out float credited);
        if (alreadyHit && gain <= credited) return false;
        // A later critical target upgrades this swing's total, independent of target iteration order.
        attacks[key] = gain;
        if (!alreadyHit) attackOrder.Enqueue(key);
        while (attackOrder.Count > 128) attacks.Remove(attackOrder.Dequeue());
        bool wasFull = Amount >= BaseMaximum - 0.0001f;
        Amount = Mathf.Min(Capacity, Amount + Mathf.Max(0f, gain - credited));
        if (!wasFull && Amount >= BaseMaximum - 0.0001f)
            CombatActionSfxService.PlayElementEnergyFull(transform.position); // A24 가득 참 알림 1회
        if (Element == WeaponElement.Light) RecordLightHit(tuning, !alreadyHit);
        MeleeHeavyVfxPreparation.RequestForEquippedWeapon(equipment, element);
        Changed?.Invoke();
        return true;
    }
    private void RecordLightHit(OverburstElementTuning tuning, bool newPhase)
    {
        if (Amount >= Capacity - 0.0001f) holdUntil = Time.time + tuning.SafeLightOverchargeFullHold;
        if (!newPhase || !IsOvercharged) return;
        int gain = Mathf.RoundToInt(tuning.SafeLightRadianceStacksPerPhase
            * (1f + FlaskCombatModifiers.Bonus(gameObject, FlaskEffect.LightRadianceGain)));
        RadianceStacks = Mathf.Min(tuning.SafeLightRadianceMaxStacks, RadianceStacks + Mathf.Max(0, gain));
    }
    private void Update()
    {
        if (Element != WeaponElement.Light) return;
        float baseMaximum = BaseMaximum;
        if (Amount <= baseMaximum + 0.0001f)
        {
            if (RadianceStacks <= 0) return;
            RadianceStacks = 0;
            Changed?.Invoke();
            return;
        }
        using var costScope = ElementCombatCostMarkers.Light_Overcharge_Tick.Auto();
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        if (Amount >= Capacity - 0.0001f && Time.time < holdUntil) return;
        Amount = Mathf.Max(baseMaximum, Amount - tuning.SafeLightOverchargeDecayPerSecond * Time.deltaTime);
        if (Amount <= baseMaximum + 0.0001f)
        {
            Amount = baseMaximum;
            RadianceStacks = 0;
            changedThrottle = 0f;
            Changed?.Invoke();
            return;
        }
        changedThrottle += Time.deltaTime;
        if (changedThrottle < 0.1f) return;
        changedThrottle = 0f;
        Changed?.Invoke();
    }
    // Heavy attacks commit once at their impact window.
    public bool TryCommitDischarge(float attackDamage, out OverburstElementDischarge discharge)
    {
        discharge = null;
        if (equipment != null) SyncWeapon();
        if (!isActiveAndEnabled || (health != null && health.IsDead)
            || !OverburstElementRules.IsActive(Element) || !OverburstElementTuning.IsFinitePositive(attackDamage)) return false;
        discharge = new OverburstElementDischarge(this, ++generation, Element, WeaponInstanceId, Amount, Normalized, attackDamage,
            RadianceStacks, OverchargeNormalized, IsOvercharged);
        Amount = 0f;
        RadianceStacks = 0;
        holdUntil = 0f;
        Changed?.Invoke();
        return true;
    }
    internal bool Owns(int token) => isActiveAndEnabled && token == generation && (health == null || !health.IsDead);
    // 패링한 강공: 위력은 모인 에너지 그대로 쓰고, 게이지는 기본 구간(과충전 제외)의 이 비율만 돌려준다.
    public const float ParriedHeavyRefundFraction = .5f;
    internal void RefundParried(float amount)
    {
        if (!OverburstElementTuning.IsFinitePositive(amount)) return;
        bool wasFull = Amount >= BaseMaximum - 0.0001f;
        Amount = Mathf.Min(Capacity, Amount + amount);
        if (!wasFull && Amount >= BaseMaximum - 0.0001f)
            CombatActionSfxService.PlayElementEnergyFull(transform.position);
        MeleeHeavyVfxPreparation.RequestForEquippedWeapon(equipment, Element);
        Changed?.Invoke();
    }
    public void Clear()
    {
        Amount = 0f;
        RadianceStacks = 0;
        holdUntil = 0f;
        generation++;
        attacks.Clear();
        attackOrder.Clear();
        Changed?.Invoke();
    }
}

public readonly struct OverburstDischargeResult
{
    public readonly WeaponElement Element;
    public readonly float BonusDamage;
    public readonly float Radius;
    public readonly int ChainTargets;
    public readonly int ConsumedStacks;
    public readonly bool Shattered;
    public OverburstDischargeResult(WeaponElement element, float damage, float radius, int chainTargets, int stacks, bool shattered)
    { Element = element; BonusDamage = damage; Radius = radius; ChainTargets = chainTargets; ConsumedStacks = stacks; Shattered = shattered; }
}

// A capability owned by one committed action. Multi-target hits share energy but consume each target only once.
public sealed class OverburstElementDischarge
{
    public readonly struct TargetSnapshot
    {
        internal readonly OverburstElementDischarge Discharge;
        internal readonly CombatHealth Health;
        internal readonly ElementalStatusController Status;
        internal readonly int Life, Frame, Stacks;
        internal readonly bool Frozen;
        internal TargetSnapshot(OverburstElementDischarge discharge, CombatHealth health, ElementalStatusController status,
            int stacks, bool frozen)
        { Discharge = discharge; Health = health; Status = status; Life = status != null ? status.LifecycleVersion : 0;
            Frame = Time.frameCount; Stacks = stacks; Frozen = frozen; }
    }
    private readonly OverburstElementEnergy owner;
    private readonly int token;
    private readonly HashSet<int> targets = new HashSet<int>();
    private readonly float attackDamage, baseDischargePower, energyCoefficient, stackCoefficient, shatterCoefficient, radius;
    private readonly int energyChainBonus;
    private bool ended;
    private bool refunded;
    public WeaponElement Element { get; }
    public string WeaponInstanceId { get; }
    public float Energy { get; }
    public float NormalizedEnergy { get; }
    public float Radius => radius;
    // Light snapshot at commit: radiance stacks and the 0..1 overcharge above the shared base.
    public int RadianceStacks { get; }
    public float Overcharge { get; }
    public bool LightTriple { get; }
    public float BaseDamage => attackDamage * energyCoefficient + baseDischargePower * NormalizedEnergy;
    public float FirstBlastDamage => attackDamage * Mathf.Lerp(OverburstCombatBalance.EmptyHeavyDamage, OverburstCombatBalance.FullHeavyDamage, NormalizedEnergy) * (1f + energyCoefficient)
        + baseDischargePower * NormalizedEnergy;
    internal OverburstElementDischarge(OverburstElementEnergy owner, int token, WeaponElement element, string weaponId,
        float energy, float normalized, float attackDamage, int radianceStacks = 0, float overcharge = 0f, bool lightTriple = false)
    {
        this.owner = owner; this.token = token; this.attackDamage = attackDamage;
        RadianceStacks = Mathf.Max(0, radianceStacks);
        Overcharge = Mathf.Clamp01(overcharge);
        LightTriple = element == WeaponElement.Light && lightTriple;
        PlayerEquipment equipped = owner != null ? owner.GetComponent<PlayerEquipment>() : null;
        baseDischargePower = equipped != null && equipped.CurrentWeaponItem != null
            && equipped.CurrentWeaponItem.runtimeInstanceId == weaponId
            ? WeaponStatCalculator.GetElementalDischargePower(equipped.CurrentWeaponItem, attackDamage) : attackDamage * .25f;
        Element = element; WeaponInstanceId = weaponId; Energy = energy; NormalizedEnergy = normalized;
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float elementBonus = element == WeaponElement.Fire ? FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.FireDischargeDamage)
            : element == WeaponElement.Electric ? FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.LightningDischargeDamage) : 0f;
        energyCoefficient = Mathf.Max(0f, tuning.dischargeDamageAtFullEnergy) * normalized
            * (1f + elementBonus + FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.EnergyDischargeDamage));
        stackCoefficient = Mathf.Max(0f, tuning.statusDamagePerStack) * (1f + elementBonus);
        shatterCoefficient = Mathf.Max(0f, tuning.shatterBlastFraction) * (1f + FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.ShatterDamage));
        radius = Mathf.Lerp(Mathf.Max(0f, tuning.minimumRadius), Mathf.Max(0f, tuning.maximumRadius), normalized);
        if (element == WeaponElement.Fire) radius *= 1f + FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.FireRadius);
        if (element == WeaponElement.Electric) radius *= 1f + FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.ChainRange);
        energyChainBonus = normalized >= .99999f ? 2 : normalized >= .5f ? 1 : 0;
    }
    public void End() { ended = true; }
    // 패링 환급: 방출당 1회, 이 방출이 아직 주인의 최신 방출일 때만. 빛 광휘 중첩·과충전은 돌려주지 않는다.
    public bool TryRefundParried()
    {
        if (ended || refunded || owner == null || !owner.Owns(token)) return false;
        refunded = true;
        owner.RefundParried(Mathf.Min(Energy, owner.BaseMaximum) * OverburstElementEnergy.ParriedHeavyRefundFraction);
        return true;
    }
    // Light heavy: hit index 0 = radiance, 1 = gauge, 2 = ordinary heavy. The double (<=100) skips index 0.
    public float LightHitRadius(int hit) => radius * OverburstElementTuning.Current.LightTripleRadiusScale(hit);
    public float LightHitDamage(int hit)
    {
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float h = FirstBlastDamage;
        float scale = hit == 0
            ? tuning.SafeLightTripleHit1Base + tuning.SafeLightTripleHit1PerStack * RadianceStacks / (float)tuning.SafeLightRadianceMaxStacks
            : hit == 1 ? tuning.SafeLightTripleHit2Base + tuning.SafeLightTripleHit2PerOvercharge * Overcharge
            : tuning.SafeLightTripleHit3Scale;
        float flask = owner != null ? FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.LightTripleImpactDamage) : 0f;
        return Mathf.Max(0f, h * scale * (1f + flask));
    }
    public int LightFirstHitIndex => LightTriple ? 0 : 1;
    // Capture immediately before the direct damage dispatch, so lethal hits retain their prepared bonus.
    public bool TryCaptureTarget(CombatHealth target, out TargetSnapshot snapshot)
    {
        snapshot = default;
        if (ended || owner == null || !owner.Owns(token) || target == null || !target.isActiveAndEnabled
            || target.IsDead || target.CurrentHp <= 0f
            || targets.Contains(target.GetInstanceID())) return false;
        CombatTarget sourceTarget = owner.GetComponent<CombatTarget>();
        CombatTarget targetActor = target.GetComponent<CombatTarget>();
        if (sourceTarget == null || targetActor == null || sourceTarget.Team == targetActor.Team) return false;
        ElementalStatusController statuses = target.GetComponent<ElementalStatusController>();
        int stacks = statuses != null ? statuses.GetStackCount(Element) : 0;
        bool frozen = Element == WeaponElement.Ice && statuses != null && statuses.IsFrozen;
        if (Element == WeaponElement.Ice && !frozen) stacks = 0;
        snapshot = new TargetSnapshot(this, target, statuses, stacks, frozen);
        return true;
    }
    public bool TryResolveConfirmedHit(TargetSnapshot snapshot, float actualDirectDamage, out OverburstDischargeResult result)
    {
        result = default;
        if (ended || owner == null || !owner.Owns(token) || snapshot.Discharge != this || snapshot.Health == null
            || snapshot.Frame != Time.frameCount || !OverburstElementTuning.IsFinitePositive(actualDirectDamage)
            || (snapshot.Status != null && snapshot.Status.LifecycleVersion != snapshot.Life)
            || !targets.Add(snapshot.Health.GetInstanceID())) return false;
        int consumed = snapshot.Stacks;
        bool shattered = snapshot.Frozen;
        // Dark corrosion survives the slam: the gather burst consumes it. Light no longer uses enemy status.
        bool consumesNow = Element != WeaponElement.Dark && Element != WeaponElement.Light;
        if (!consumesNow) { consumed = 0; shattered = false; }
        else if (snapshot.Status != null && !snapshot.Health.IsDead)
            consumed = snapshot.Status.ConsumeForDischarge(Element, out shattered);
        float bonus = Element == WeaponElement.Ice ? (shattered ? FirstBlastDamage * shatterCoefficient : 0f)
            : Element == WeaponElement.Fire || Element == WeaponElement.Electric
                || Element == WeaponElement.Dark || Element == WeaponElement.Light ? 0f
            : attackDamage * consumed * stackCoefficient;
        int resolvedChainTargets = Element == WeaponElement.Electric && consumed > 0
            ? Mathf.Min(7, consumed + energyChainBonus)
            : 0;
        result = new OverburstDischargeResult(Element, bonus, radius, resolvedChainTargets, consumed, shattered);
        return true;
    }
    public bool TryResolveConfirmedHit(CombatHealth target, float actualDirectDamage, out OverburstDischargeResult result)
    {
        result = default;
        return TryCaptureTarget(target, out TargetSnapshot snapshot) && TryResolveConfirmedHit(snapshot, actualDirectDamage, out result);
    }
}
