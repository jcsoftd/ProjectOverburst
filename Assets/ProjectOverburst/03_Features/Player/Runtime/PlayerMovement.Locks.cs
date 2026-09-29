using UnityEngine;

// PlayerMovement partial: 근접 공격 이동 잠금과 무기 동작 잠금 해제. 필드와 Unity 수명주기는 PlayerMovement.cs에 있다.
public partial class PlayerMovement
{
    private void UpdateMeleeAttackMoveLock()
    {
        if (!meleeAttackRotationLocked)
            return;

        if (IsMeleeAttackMoveLocked)
            return;

        meleeAttackRotationLocked = false; // 회전 잠금 해제
        meleeAttackLockedDirection = Vector3.zero; // 방향 초기화
    }

    public void BeginQuickFireCombatMove(float holdTime, float moveSpeedMultiplier)
    {
        quickFireUntil = Mathf.Max(quickFireUntil, Time.time + Mathf.Max(0f, holdTime)); // QuickFire 시간
        activeQuickFireMoveSpeedMultiplier = moveSpeedMultiplier > 0f ? moveSpeedMultiplier : quickFireMoveSpeedMultiplier; // QuickFire 속도
        isRunning = false; // 달리기 차단
    }

    public void BeginMeleeAttackMoveLock(float duration)
    {
        BeginMeleeAttackMoveLock(duration, transform.forward);
    }

    public void BeginMeleeAttackMoveLock(float duration, Vector3 lockedDirection)
    {
        meleeAttackMoveLockUntil = Time.time + Mathf.Max(0f, duration); // 이동 잠금
        lockedDirection.y = 0f;

        if (lockedDirection.sqrMagnitude <= 0.001f)
            lockedDirection = transform.forward;

        meleeAttackLockedDirection = lockedDirection.sqrMagnitude > 0.001f ? lockedDirection.normalized : Vector3.zero;
        meleeAttackRotationLocked = meleeAttackLockedDirection.sqrMagnitude > 0.001f;
        moveInput = Vector2.zero; // 이동 차단
        moveDirection = Vector3.zero; // 회전 차단
        isRunning = false; // 달리기 차단
        jumpRequested = false; // 점프 제거
        jumpAnimationRequested = false; // 점프 애니 제거
    }

    public void CancelWeaponActionLocks()
    {
        meleeAttackMoveLockUntil = 0f;
        meleeAttackRotationLocked = false;
        meleeAttackLockedDirection = Vector3.zero;
        moveInput = Vector2.zero;
        moveDirection = Vector3.zero;
        isRunning = false;
        jumpRequested = false;
        jumpAnimationRequested = false;
        quickFireUntil = 0f;
        externalAimUntil = 0f;
    }
}
