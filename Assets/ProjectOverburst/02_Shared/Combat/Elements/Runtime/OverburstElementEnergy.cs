using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OverburstElementEnergy : MonoBehaviour
{
    private PlayerEquipment equipment;
    private CombatHealth health;
    private readonly HashSet<int> attacks = new HashSet<int>();
    private readonly Queue<int> attackOrder = new Queue<int>();
    private int generation;
    public WeaponElement Element { get; private set; }
    public string WeaponInstanceId { get; private set; } = string.Empty;
    public float Amount { get; private set; }
    public float Normalized => Mathf.Clamp01(Amount / Mathf.Max(1f, OverburstElementTuning.Current.maximumEnergy));
    public event Action Changed;

    private void OnEnable()
    {
        equipment = GetComponent<PlayerEquipment>();
        health = GetComponent<CombatHealth>();
        if (equipment != null) equipment.WeaponSlotsChanged += SyncWeapon;
        if (health != null) { health.OnDead += Died; health.OnReset += ResetHealth; }
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
        BindWeapon(item != null ? item.runtimeInstanceId : string.Empty, item != null ? item.ResolvedElement : WeaponElement.None);
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
    public bool RecordConfirmedHit(string weaponId, WeaponElement element, int attackSequenceId, float actualDamage)
    {
        if (!isActiveAndEnabled || (health != null && health.IsDead)
            || !OverburstElementTuning.IsFinitePositive(actualDamage) || attackSequenceId <= 0
            || string.IsNullOrWhiteSpace(weaponId) || !OverburstElementRules.IsActive(element)) return false;
        if (equipment != null) SyncWeapon();
        if (WeaponInstanceId != weaponId || Element != element || !attacks.Add(attackSequenceId)) return false;
        attackOrder.Enqueue(attackSequenceId);
        while (attackOrder.Count > 128) attacks.Remove(attackOrder.Dequeue());
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        Amount = Mathf.Min(Mathf.Max(1f, tuning.maximumEnergy), Amount + Mathf.Max(0f, tuning.energyPerAttack) * (1f + FlaskCombatModifiers.Bonus(gameObject, FlaskEffect.EnergyGain)));
        Changed?.Invoke();
        return true;
    }
    // Heavy attacks commit once at their impact window.
    public bool TryCommitDischarge(float attackDamage, out OverburstElementDischarge discharge)
    {
        discharge = null;
        if (equipment != null) SyncWeapon();
        if (!isActiveAndEnabled || (health != null && health.IsDead) || Amount <= 0f
            || !OverburstElementRules.IsActive(Element) || !OverburstElementTuning.IsFinitePositive(attackDamage)) return false;
        discharge = new OverburstElementDischarge(this, ++generation, Element, WeaponInstanceId, Amount, Normalized, attackDamage);
        Amount = 0f;
        Changed?.Invoke();
        return true;
    }
    internal bool Owns(int token) => isActiveAndEnabled && token == generation && (health == null || !health.IsDead);
    public void Clear()
    {
        Amount = 0f;
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
    public readonly float RequestedPullDistance;
    public OverburstDischargeResult(WeaponElement element, float damage, float radius, int chainTargets, int stacks, bool shattered, float pullDistance)
    { Element = element; BonusDamage = damage; Radius = radius; ChainTargets = chainTargets; ConsumedStacks = stacks; Shattered = shattered; RequestedPullDistance = pullDistance; }
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
    private readonly int chainTargets;
    private readonly float waterPullDistance;
    private bool ended;
    public WeaponElement Element { get; }
    public string WeaponInstanceId { get; }
    public float Energy { get; }
    public float NormalizedEnergy { get; }
    internal OverburstElementDischarge(OverburstElementEnergy owner, int token, WeaponElement element, string weaponId,
        float energy, float normalized, float attackDamage)
    {
        this.owner = owner; this.token = token; this.attackDamage = attackDamage;
        PlayerEquipment equipped = owner != null ? owner.GetComponent<PlayerEquipment>() : null;
        baseDischargePower = equipped != null && equipped.CurrentWeaponItem != null
            && equipped.CurrentWeaponItem.runtimeInstanceId == weaponId
            ? WeaponStatCalculator.GetElementalDischargePower(equipped.CurrentWeaponItem) : 0f;
        Element = element; WeaponInstanceId = weaponId; Energy = energy; NormalizedEnergy = normalized;
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float elementBonus = element == WeaponElement.Fire ? FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.FireDischargeDamage)
            : element == WeaponElement.Electric ? FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.LightningDischargeDamage) : 0f;
        energyCoefficient = Mathf.Max(0f, tuning.dischargeDamageAtFullEnergy) * normalized
            * (1f + elementBonus + FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.EnergyDischargeDamage));
        stackCoefficient = Mathf.Max(0f, tuning.statusDamagePerStack) * (1f + elementBonus + (element == WeaponElement.Water ? FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.CompressionDamage) : 0f));
        shatterCoefficient = Mathf.Max(0f, tuning.shatterDamage) * (1f + FlaskCombatModifiers.Bonus(owner.gameObject, FlaskEffect.ShatterDamage));
        radius = Mathf.Lerp(Mathf.Max(0f, tuning.minimumRadius), Mathf.Max(0f, tuning.maximumRadius), normalized);
        FlaskEffect areaEffect = element == WeaponElement.Fire ? FlaskEffect.FireRadius : element == WeaponElement.Electric ? FlaskEffect.ChainRange : FlaskEffect.SuctionRadius;
        if (element != WeaponElement.Ice) radius *= 1f + FlaskCombatModifiers.Bonus(owner.gameObject, areaEffect);
        chainTargets = Mathf.Clamp(1 + Mathf.FloorToInt(normalized * (tuning.maximumChainTargets - 1)), 1, Mathf.Max(1, tuning.maximumChainTargets));
        waterPullDistance = element == WeaponElement.Water ? Mathf.Max(0f, tuning.maximumWaterPullDistance) * normalized : 0f;
    }
    public void End() { ended = true; }
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
        if (snapshot.Status != null && !snapshot.Health.IsDead)
            consumed = snapshot.Status.ConsumeForDischarge(Element, out shattered);
        float bonus = attackDamage * (energyCoefficient + consumed * stackCoefficient + (shattered ? shatterCoefficient : 0f))
            + baseDischargePower * NormalizedEnergy;
        result = new OverburstDischargeResult(Element, bonus, radius, Element == WeaponElement.Electric ? chainTargets : 0, consumed, shattered, waterPullDistance);
        return true;
    }
    public bool TryResolveConfirmedHit(CombatHealth target, float actualDirectDamage, out OverburstDischargeResult result)
    {
        result = default;
        return TryCaptureTarget(target, out TargetSnapshot snapshot) && TryResolveConfirmedHit(snapshot, actualDirectDamage, out result);
    }
}
