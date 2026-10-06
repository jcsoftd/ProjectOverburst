using UnityEngine;

public sealed partial class EnemyAbilityController
{
    // Eligibility and both warning signals read the same committed motion and pending strike.
    public bool TryGetActiveParryMotionWindow(out int strike)
    {
        strike = nextImpactIndex;
        var ability = lastCommittedAbility;
        if (ability == null || !ability.HasParryMotionWindows || !IsExecuting || finalImpactDelivered
            || health != null && health.IsDead || movement != null && movement.IsStatusMovementLocked
            || animationBridge == null || !animationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float phase)
            || !ability.TryGetParryMotionWindow(strike, out var window)) return false;
        return phase >= window.x && phase <= window.y;
    }
}
