using UnityEngine;

// PlayerMovement partial: 이동 속도 계산(전투·버프·조준 배율). 필드와 Unity 수명주기는 PlayerMovement.cs에 있다.
public partial class PlayerMovement
{
    private float GetTargetMoveSpeed()
    {
        if (IsEvading || IsMeleeAttackMoveLocked)
            return 0f;

        if (IsCombatWalkLocomotionMode)
            return ResolveAuthoredMoveSpeed(GetMeleeCombatMoveSpeed(), true)
                * (HasGreatswordEquipped ? GreatswordLocomotionSpeedMultiplier : 1f);

        float baseMoveSpeed = isWalkMode ? walkSpeed : runSpeed; // 기본 이동은 달리기
        float speed = IsAimCombatMoveActive ? walkSpeed * GetActiveAimMoveSpeedMultiplier() : baseMoveSpeed; // 상태별 속도

        if (landingSlowTimer > 0f)
            speed *= landingSpeedMultiplier; // 착지 감속

        return ResolveAuthoredMoveSpeed(speed, IsCombatMoveMode)
            * (HasGreatswordEquipped ? GreatswordLocomotionSpeedMultiplier : 1f);
    }

    private float GetMeleeCombatMoveSpeed()
    {
        if (IsMeleeGuarding
            && playerEquipment != null
            && !playerEquipment.CanCurrentWeaponMoveWhileGuarding)
        {
            return 0f;
        }

        if (IsMeleeGuarding)
            return Mathf.Max(0f, meleeCombatGuardMoveSpeed);

        MeleeWeaponDefinition melee = playerEquipment != null
            ? playerEquipment.CurrentWeaponContext.Melee
            : null;
        WeaponCombatAnimationProfile profile = melee != null ? melee.animationProfile : null;
        if (profile != null && profile.matchMovementToLocomotionSpeed && moveDirection.sqrMagnitude > 0.001f)
        {
            Vector3 localDirection = transform.InverseTransformDirection(moveDirection);
            float authoredSpeed = profile.locomotionReferenceSpeeds.GetSpeed(localDirection);
            if (authoredSpeed > 0.05f)
                return authoredSpeed;
        }

        return melee != null && melee.combatMoveSpeed > 0f
            ? melee.combatMoveSpeed
            : Mathf.Max(0f, meleeCombatMoveSpeed);
    }

    private float GetActiveBuffMoveSpeedMultiplier()
    {
        if (playerBuffController == null)
            playerBuffController = ResolveBuffController();

        return (playerBuffController != null ? playerBuffController.ActiveMoveSpeedMultiplier : 1f)
            * (1f + MapRunBuffs.Bonus(MapBuffKind.MoveSpeed));
    }

    private PlayerBuffController ResolveBuffController()
    {
        PlayerBuffController controller = GetComponent<PlayerBuffController>();
        if (controller != null)
            return controller;

        controller = GetComponentInParent<PlayerBuffController>();
        if (controller != null)
            return controller;

        return GetComponentInChildren<PlayerBuffController>();
    }

    private float GetActiveAimMoveSpeedMultiplier()
    {
        if (playerEquipment != null
            && playerEquipment.HasCurrentWeapon
            && playerEquipment.CurrentWeaponContext.Aim.moveSpeedMultiplier > 0f)
        {
            return playerEquipment.CurrentWeaponContext.Aim.moveSpeedMultiplier;
        }

        return aimMoveSpeedMultiplier;
    }

    private float ResolveCurrentBaseMoveSpeed()
    {
        return ResolveBaseMoveSpeed(
            activeMovementIntent.Gait,
            IsCombatWalkLocomotionMode);
    }

    private float ResolveBaseMoveSpeed(ActorMovementGait gait, bool useCombatSpeed)
    {
        if (useCombatSpeed)
            return ResolveAuthoredMoveSpeed(GetMeleeCombatMoveSpeed(), true); // 전투 전용 Walk 속도

        return gait == ActorMovementGait.Walk ? WalkMoveSpeed : RunMoveSpeed;
    }

    private ActorMovementGait ResolveStoppedMovementGait()
    {
        if (controlAuthority == ActorControlAuthority.Player)
        {
            return IsCombatMoveMode || isWalkMode
                ? ActorMovementGait.Walk
                : ActorMovementGait.Run;
        }

        return activeMovementIntent.Gait;
    }

    private float ResolveAuthoredMoveSpeed(float authoredSpeed, bool useCombatSpeed = false)
    {
        return Mathf.Max(0f, authoredSpeed)
            * GetActiveBuffMoveSpeedMultiplier()
            * GearStatTotals.From(playerEquipment).MovementSpeedMultiplier(useCombatSpeed);
    }
}
