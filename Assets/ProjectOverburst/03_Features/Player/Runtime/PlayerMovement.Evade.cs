using UnityEngine;

public partial class PlayerMovement
{
    public void CompleteExplorationEvade(Vector2 input)
    {
        if (locomotion == null || GameplayInputBlocker.IsGameplayInputBlocked || IsKnockedDown
            || PlayerCombatModeController.IsSharedCombatModeActive()) return;
        Vector3 direction = ResolveMoveDirection(input);
        float speed = GetTargetMoveSpeed();
        locomotion.SetHorizontalVelocity(direction.sqrMagnitude > .001f
            ? direction.normalized * speed * Mathf.Clamp01(input.magnitude) : Vector3.zero);
    }
}
