using UnityEngine;

// PlayerMovement partial: 우클릭 조준·방어 입력, 무기별 가능 여부, 한손검 방어 시간과 패링 판정. 필드와 Unity 수명주기는 PlayerMovement.cs에 있다.
public partial class PlayerMovement
{
    private void ReadAimInput()
    {
        if (GameplayInputBlocker.IsGameplayInputBlocked)
        {
            isAiming = false; // 조준 해제
            isMeleeCombatStance = false; // 근접 자세 해제
            ResetSwordGuardTiming();
            meleeAttackMoveLockUntil = 0f; // 이동 잠금 해제
            meleeAttackRotationLocked = false; // 회전 잠금 해제
            meleeAttackLockedDirection = Vector3.zero; // 방향 초기화
            aimBlockedUntilRelease = true; // 재입력 대기
            return;
        }

        if (IsEvading)
        {
            isAiming = false; // 회피 중 조준 차단
            isMeleeCombatStance = false; // 회피 중 자세 차단
            ResetSwordGuardTiming();
            return;
        }

        if (debugForceAiming)
        {
            isAiming = CanAimWithCurrentWeapon(); // 디버그 조준
            isMeleeCombatStance = false; // 근접 제외
            ResetSwordGuardTiming();
            aimBlockedUntilRelease = false; // 차단 해제
            return;
        }

        // GOAL A2: 우클릭 홀드 직접 읽기 대신 Gameplay Aim 유지를 사용한다.
        // aimBlockedUntilRelease 래치와 무기별 CanAim/CanStance 분기 감각을 유지.
        PlayerInputFacade facade = ResolveInputFacade();
        if (facade == null)
        {
            isAiming = false;
            isMeleeCombatStance = false;
            ResetSwordGuardTiming();
            return;
        }

        bool rightPressed = facade.AimHeld;

        if (!rightPressed)
        {
            aimBlockedUntilRelease = false; // 재입력 해제
            isAiming = false; // 조준 해제
            isMeleeCombatStance = false; // 근접 자세 해제
            ResetSwordGuardTiming();
            return;
        }

        if (!IsGrounded || jumpRequested)
        {
            CancelAimUntilRelease();
            return;
        }

        if (CanUseMeleeCombatStanceWithCurrentWeapon())
        {
            if (!PlayerCombatModeController.IsSharedCombatModeActive())
            {
                isAiming = false;
                isMeleeCombatStance = false;
                ResetSwordGuardTiming();
                return;
            }

            if (aimBlockedUntilRelease)
            {
                isMeleeCombatStance = false; // 재입력 대기
                return;
            }

            isAiming = false; // 총기 조준 해제
            isMeleeCombatStance = true; // 근접 자세
            return;
        }

        isMeleeCombatStance = false; // 근접 자세 해제
        ResetSwordGuardTiming();

        if (!CanAimWithCurrentWeapon())
        {
            isAiming = false; // 조준 불가
            return;
        }

        if (aimBlockedUntilRelease)
        {
            isAiming = false; // 재입력 대기
            return;
        }

        isAiming = true; // 정조준
    }

    private bool CanAimWithCurrentWeapon()
    {
        return playerEquipment == null; // 우클릭 직접 조준(옛 마법 무기)은 제거됐다. 장비가 없을 때만 디버그 조준을 허용한다.
    }

    private bool CanUseMeleeCombatStanceWithCurrentWeapon()
    {
        return playerEquipment != null && playerEquipment.CanCurrentWeaponUseMeleeCombatStance; // 근접 자세 가능
    }

    private bool CanUseMeleeGuardWithCurrentWeapon()
    {
        return playerEquipment != null && playerEquipment.CanCurrentWeaponUseMeleeGuard;
    }

    private bool CanUseAimCombatMoveWithCurrentWeapon()
    {
        return playerEquipment == null || playerEquipment.CanCurrentWeaponUseAimCombatMove;
    }

    private bool CanUseAimPoseWithCurrentWeapon()
    {
        return playerEquipment == null || playerEquipment.CanCurrentWeaponUseAimPose;
    }

    private bool CanRotateAimWithCurrentWeapon()
    {
        return playerEquipment == null || playerEquipment.CanCurrentWeaponRotateToAim;
    }

    private void CancelAimUntilRelease()
    {
        isAiming = false; // 조준 해제
        isMeleeCombatStance = false; // 근접 해제
        ResetSwordGuardTiming();
        aimBlockedUntilRelease = true; // 재입력 대기
    }

    private void UpdateSwordGuardTiming(bool wasSwordGuarding)
    {
        bool isSwordGuarding = IsMeleeGuarding;
        if (!isSwordGuarding)
        {
            ResetSwordGuardTiming();
            return;
        }

        if (wasSwordGuarding)
            return;

        swordGuardStartedTime = Time.time; // false -> true 시점
        swordGuardParryConsumed = false;
    }

    private void ResetSwordGuardTiming()
    {
        swordGuardStartedTime = -999f;
        swordGuardParryConsumed = false;
    }

    public bool TryConsumeMeleeGuardParry()
    {
        if (!IsMeleeGuarding || swordGuardParryConsumed || playerEquipment == null)
            return false;

        float parryWindow = playerEquipment.CurrentMeleeGuardParryWindow;
        if (parryWindow <= 0f || Time.time - swordGuardStartedTime > parryWindow)
            return false;

        swordGuardParryConsumed = true;
        return true;
    }

    public void CancelWeaponAimStateForSwitch()
    {
        isAiming = false;
        isMeleeCombatStance = false;
        aimBlockedUntilRelease = true;
        ResetSwordGuardTiming();
    }
}
