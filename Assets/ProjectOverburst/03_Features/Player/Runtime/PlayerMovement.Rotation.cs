using UnityEngine;

// PlayerMovement partial: 마우스·이동·공격 방향 회전. 필드와 Unity 수명주기는 PlayerMovement.cs에 있다.
public partial class PlayerMovement
{
    private void RotatePlayer()
    {
        if (IsMeleeAttackMoveLocked)
        {
            RotateToLockedMeleeAttackDirection();
            return;
        }

        if (controlAuthority == ActorControlAuthority.AI)
        {
            Vector3 facingDirection = activeMovementIntent.FacingDirection.sqrMagnitude > 0.001f
                ? activeMovementIntent.FacingDirection
                : moveDirection;
            RotateToDirection(facingDirection, rotationSpeed);
            return;
        }

        if (lootAutoMoveActive)
        {
            RotateToMoveDirection(); // 아이템 접근 방향 우선
            return;
        }

        if (IsEvading)
            return;

        if (IsMeleeCombatLocomotionMode)
        {
            RotateToMeleeAimDirection();
            return;
        }

        if (isMeleeCombatStance)
        {
            if (CanRotateAimWithCurrentWeapon())
                RotateToMeleeAimDirection();
            return;
        }

        if (IsCombatMoveMode)
            return;

        RotateToExplorationMoveDirection();
    }

    private void RotateToMeleeAimDirection()
    {
        if (!MeleeAimCalculator.TryGetMouseDirectionFromPlayer(transform, meleeAimCamera, out Vector3 direction))
            return;

        if (combatFacingController != null && combatFacingController.AcceptAim(direction)) return;
        RotateToDirection(direction, meleeFacingRotationSpeed);
    }

    private void RotateToLockedMeleeAttackDirection()
    {
        Vector3 direction = meleeAttackLockedDirection;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            direction = transform.forward;

        if (direction.sqrMagnitude <= 0.001f)
            return;

        locomotion?.FaceImmediately(direction);
    }

    private void RotateToMoveDirection()
    {
        if (moveDirection.sqrMagnitude <= 0.001f)
            return;

        RotateToDirection(moveDirection, rotationSpeed);
    }

    private void RotateToExplorationMoveDirection()
    {
        if (moveDirection.sqrMagnitude <= 0.001f)
            return;

        locomotion?.RotateSmooth(moveDirection, explorationFacingSharpness, Time.deltaTime);
    }

    private void RotateToDirection(Vector3 direction, float rotateSpeed)
    {
        locomotion?.RotateTowards(
            direction,
            rotateSpeed,
            EvadeRotationSpeedMultiplier,
            Time.deltaTime);
    }
}
