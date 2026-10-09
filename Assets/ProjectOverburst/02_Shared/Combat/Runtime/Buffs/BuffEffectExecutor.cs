using System;
using UnityEngine;

// Uses the existing health/death pipeline. Actor-specific healing feedback is optional.
public static class BuffEffectExecutor
{
    public static void ApplyTick(CombatHealth target, BuffSnapshot effect, GameObject source, int ticks,
        Action<CombatHealth, float> healPercent = null, Func<bool> isTargetLifetimeCurrent = null)
    {
        if (ticks <= 0 || (effect.DamagePerTick <= 0f && effect.HealPercentPerTick <= 0f)) return;
        for (int i = 0; i < ticks; i++)
        {
            if (target == null || target.IsDead || (isTargetLifetimeCurrent != null && !isTargetLifetimeCurrent())) return;
            if (effect.DamagePerTick > 0f)
                target.TakeDamage(new DamageInfo(effect.DamagePerTick, target.transform.position, source,
                    triggersOnHitEffects: false, isDamageOverTime: true, suppressDefaultHitVfx: true));
            if (target == null || target.IsDead || (isTargetLifetimeCurrent != null && !isTargetLifetimeCurrent())) return;
            if (effect.HealPercentPerTick > 0f)
            {
                if (healPercent != null) healPercent(target, effect.HealPercentPerTick);
                else target.Heal(target.MaxHp * effect.HealPercentPerTick);
            }
        }
    }
}
