using UnityEngine;

// The existing neutral contact route must not award enemy HP/on-hit rewards.
public sealed class MainTownBreakableTarget : CombatHealth, IDamageable
{
    MainTownDestruction owner;
    int entry;
    public void Bind(MainTownDestruction controller, int index) { owner = controller; entry = index; ResetHealth(); }
    void IDamageable.TakeDamage(DamageInfo info)
    {
        if (owner != null && info.damage > 0 && !info.isDamageOverTime)
            owner.TryBreak(entry, info);
    }
}
