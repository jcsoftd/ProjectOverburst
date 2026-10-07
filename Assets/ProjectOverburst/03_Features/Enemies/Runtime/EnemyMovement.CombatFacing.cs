using UnityEngine;

public partial class EnemyMovement
{
    private EnemyActor combatFacingActor;
    private EnemyBossCombatDirector combatFacingBoss;
    private bool combatFacingResolved;
    public bool UsesSmoothCombatFacing
    {
        get
        {
            if (!combatFacingResolved)
            {
                combatFacingActor = GetComponent<EnemyActor>();
                combatFacingBoss = GetComponent<EnemyBossCombatDirector>();
                combatFacingResolved = true;
            }
            return combatFacingActor != null && combatFacingBoss == null && profile != null
                && !profile.HasTurnAnimation && !UsesMotionFacing;
        }
    }
    private bool IsSmoothCombatFacing(Vector3 position)
    {
        Vector3 direction = position - transform.position; direction.y = 0f;
        Vector3 forward = motor != null ? motor.Rotation * Vector3.forward : transform.forward;
        return direction.sqrMagnitude < .0001f || Vector3.Angle(forward, direction) <= 5f;
    }
    private void ConfigureCombatFacing(bool enabled)
    { if (motor != null) motor.FacingSmoothTime = enabled ? .12f : 0f; }
    private void ResetCombatFacing()
    { ConfigureCombatFacing(false); motor?.ResetFacingSmoothing(); facingRequestUntil = 0f; }
    private bool TickCombatFacing()
    {
        if (!UsesSmoothCombatFacing || hasDestination || Time.time >= facingRequestUntil) return false;
        ConfigureCombatFacing(true);
        motor?.HoldPosition();
        motor?.Face(requestedFacingPosition - transform.position, profile.TurnSpeed);
        StopLocomotionOutput();
        return true;
    }
}
