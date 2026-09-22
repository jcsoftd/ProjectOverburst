using UnityEngine;

public static class OverburstElementCombat
{
    // Called with measured HP loss, including lethal hits. Status application below is survival-gated.
    public static void ReportConfirmedHit(CombatHealth target, DamageInfo info, float actualDamage)
    {
        if (target == null || info.source == null || !info.triggersOnHitEffects || info.isDamageOverTime
            || info.elementalReactionType != ElementalReactionType.None || info.sourceAttackSequenceId <= 0
            || !OverburstElementTuning.IsFinitePositive(actualDamage) || !OverburstElementRules.IsActive(info.element)) return;
        PlayerEquipment equipment = info.source.GetComponentInParent<PlayerEquipment>();
        if (equipment == null || equipment.CurrentWeaponItem == null
            || equipment.CurrentWeaponItem.runtimeInstanceId != info.sourceWeaponRuntimeInstanceId
            || equipment.CurrentWeaponItem.ResolvedElement != info.element) return;
        CombatTarget sourceTarget = equipment.GetComponent<CombatTarget>();
        CombatTarget targetActor = target.GetComponent<CombatTarget>();
        if (sourceTarget == null || targetActor == null || sourceTarget.Team == targetActor.Team) return;
        OverburstElementEnergy energy = equipment.GetComponent<OverburstElementEnergy>();
        if (energy == null) energy = equipment.gameObject.AddComponent<OverburstElementEnergy>();
        energy.RecordConfirmedHit(info.sourceWeaponRuntimeInstanceId, info.element, info.sourceAttackSequenceId, actualDamage);
        if (target.IsDead || target.CurrentHp <= 0f) return;
        ElementalStatusController status = target.GetComponent<ElementalStatusController>();
        if (status != null) status.ApplyConfirmedHit(info, actualDamage);
    }
}
