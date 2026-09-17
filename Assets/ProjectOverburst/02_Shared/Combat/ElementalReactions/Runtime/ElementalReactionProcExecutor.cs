using System.Collections.Generic;
using UnityEngine;

public static class ElementalReactionProcExecutor
{
    private static readonly List<CombatTarget> targetBuffer = new List<CombatTarget>(128);

    public static void ExecuteVaporize(
        ElementalApplicationContext context,
        ElementalReactionOwnerSnapshot owner)
    {
        CombatTarget triggerTarget = context.Target;
        if (triggerTarget == null)
            return;

        Vector3 center = triggerTarget.WorldCenter;
        float baseDamage = ElementalReactionRules.ResolveVaporizeDamage(context.ActualDirectDamage);
        if (baseDamage <= 0f)
            return;

        ElementalReactionTargetQuery.CollectHostileTargets(
            center,
            ElementalReactionRules.VaporizeRadius,
            owner.SourceTeam,
            owner.SourceTargetId,
            targetBuffer);
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            CombatTarget target = targetBuffer[i];
            CombatHealth health = target.DamageReceiver;
            if (health == null || health.IsDead || health.CurrentHp <= 0f || !health.isActiveAndEnabled)
                continue;

            Vector3 targetCenter = target.WorldCenter;
            float damage = ElementalReactionRules.ResolveCircularAreaDamage(
                baseDamage,
                center,
                targetCenter,
                ElementalReactionRules.VaporizeRadius);

            health.TakeDamage(new DamageInfo(
                damage,
                targetCenter,
                owner.SourceActor,
                Vector3.zero,
                0f,
                false,
                false,
                false,
                default,
                true,
                owner.IncomingElement,
                owner.SourceWeaponRuntimeInstanceId,
                ElementalReactionType.Vaporize));
        }
    }

    public static void ExecuteFracture(
        ElementalApplicationContext context,
        ElementalReactionOwnerSnapshot owner)
    {
        CombatHealth targetHealth = context.TargetHealth;
        if (targetHealth == null || targetHealth.IsDead || targetHealth.CurrentHp <= 0f)
            return;

        Vector3 center = context.Target != null
            ? context.Target.WorldCenter
            : targetHealth.transform.position;
        targetHealth.TakeDamage(new DamageInfo(
            ElementalReactionRules.ResolveFractureDamage(context.ActualDirectDamage),
            center,
            owner.SourceActor,
            Vector3.zero,
            0f,
            false,
            false,
            false,
            default,
            true,
            owner.IncomingElement,
            owner.SourceWeaponRuntimeInstanceId,
            ElementalReactionType.ThermalFracture));
    }

    public static void ExecuteShatter(
        CombatHealth targetHealth,
        CombatTarget target,
        ElementalReactionOwnerSnapshot owner,
        ElementalApplicationContext triggerContext)
    {
        if (targetHealth == null || targetHealth.IsDead || targetHealth.CurrentHp <= 0f)
            return;

        Vector3 center = target != null ? target.WorldCenter : targetHealth.transform.position;
        float hpBefore = targetHealth.CurrentHp;
        targetHealth.TakeDamage(new DamageInfo(
            ElementalReactionRules.ResolveShatterDamage(triggerContext.ActualDirectDamage),
            center,
            owner.SourceActor,
            Vector3.zero,
            0f,
            false,
            false,
            false,
            default,
            true,
            owner.IncomingElement,
            owner.SourceWeaponRuntimeInstanceId,
            ElementalReactionType.Shatter));

        RaiseProcEvent(
            ElementalReactionProcType.Shatter,
            target,
            center,
            owner,
            triggerContext,
            targetHealth.CurrentHp < hpBefore ? 1 : 0);
    }

    public static void ExecutePlasma(
        CombatTarget triggerTarget,
        ElementalReactionOwnerSnapshot owner,
        float storedDamage,
        ElementalApplicationContext triggerContext)
    {
        if (triggerTarget == null)
            return;

        Vector3 center = triggerTarget.WorldCenter;
        float baseDamage = ElementalReactionRules.ResolvePlasmaDamage(storedDamage);
        if (baseDamage <= 0f)
            return;

        int damagedTargetCount = 0;
        ElementalReactionTargetQuery.CollectHostileTargets(
            center,
            ElementalReactionRules.PlasmaRadius,
            owner.SourceTeam,
            owner.SourceTargetId,
            targetBuffer);
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            CombatTarget target = targetBuffer[i];
            CombatHealth health = target.DamageReceiver;
            if (health == null || health.IsDead || health.CurrentHp <= 0f || !health.isActiveAndEnabled)
                continue;

            Vector3 direction = ResolveExplosionDirection(center, target, triggerContext);
            Vector3 targetCenter = target.WorldCenter;
            float damage = ElementalReactionRules.ResolveCircularAreaDamage(
                baseDamage,
                center,
                targetCenter,
                ElementalReactionRules.PlasmaRadius);
            float hpBefore = health.CurrentHp;
            health.TakeDamage(new DamageInfo(
                damage,
                targetCenter,
                owner.SourceActor,
                direction,
                ElementalReactionRules.PlasmaKnockback,
                false,
                false,
                false,
                default,
                true,
                owner.IncomingElement,
                owner.SourceWeaponRuntimeInstanceId,
                ElementalReactionType.Plasma));
            if (health.CurrentHp < hpBefore)
                damagedTargetCount++;
        }

        RaiseProcEvent(
            ElementalReactionProcType.Plasma,
            triggerTarget,
            center,
            owner,
            triggerContext,
            damagedTargetCount);
    }

    public static void ExecuteColdCharge(
        CombatTarget triggerTarget,
        ElementalReactionOwnerSnapshot owner,
        ElementalApplicationContext triggerContext)
    {
        if (triggerTarget == null)
            return;

        Vector3 center = triggerTarget.WorldCenter;
        float baseDamage = ElementalReactionRules.ResolveColdChargeDamage(triggerContext.ActualDirectDamage);
        if (baseDamage <= 0f)
            return;

        int damagedTargetCount = 0;
        ElementalReactionTargetQuery.CollectHostileTargetsSharedForFrame(
            triggerTarget.TargetId,
            center,
            ElementalReactionRules.ColdChargeRadius,
            owner.SourceTeam,
            owner.SourceTargetId,
            targetBuffer);
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            CombatTarget target = targetBuffer[i];
            CombatHealth health = target.DamageReceiver;
            if (health == null || health.IsDead || health.CurrentHp <= 0f || !health.isActiveAndEnabled)
                continue;

            Vector3 targetCenter = target.WorldCenter;
            float damage = ElementalReactionRules.ResolveCircularAreaDamage(
                baseDamage,
                center,
                targetCenter,
                ElementalReactionRules.ColdChargeRadius);
            Vector3 direction = ResolveExplosionDirection(center, target, triggerContext);
            float hpBefore = health.CurrentHp;
            health.TakeDamage(new DamageInfo(
                damage,
                targetCenter,
                owner.SourceActor,
                direction,
                ElementalReactionRules.ColdChargeKnockback,
                false,
                false,
                false,
                default,
                true,
                WeaponElement.Ice,
                owner.SourceWeaponRuntimeInstanceId,
                ElementalReactionType.ColdCharge));
            if (health.CurrentHp < hpBefore)
                damagedTargetCount++;
        }

        RaiseProcEvent(
            ElementalReactionProcType.ColdCharge,
            triggerTarget,
            center,
            owner,
            triggerContext,
            damagedTargetCount);
    }

    private static Vector3 ResolveExplosionDirection(
        Vector3 center,
        CombatTarget target,
        ElementalApplicationContext triggerContext)
    {
        Vector3 direction = target.WorldCenter - center;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = triggerContext.Direction;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude <= 0.0001f && triggerContext.SourceActor != null)
        {
            direction = center - triggerContext.SourceActor.transform.position;
            direction.y = 0f;
        }

        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private static void RaiseProcEvent(
        ElementalReactionProcType procType,
        CombatTarget target,
        Vector3 center,
        ElementalReactionOwnerSnapshot owner,
        ElementalApplicationContext triggerContext,
        int damagedTargetCount)
    {
        ElementalReactionEvents.RaiseReactionProcExecuted(new ElementalReactionProcEvent(
            ElementalReactionEvents.AllocateSequenceId(),
            procType,
            target,
            center,
            owner,
            new ElementalReactionTriggerSnapshot(triggerContext),
            damagedTargetCount));
    }
}
