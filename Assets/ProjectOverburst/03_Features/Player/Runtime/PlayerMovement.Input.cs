using UnityEngine;

// PlayerMovement partial: 이동·걷기 전환·점프 입력 읽기와 입력 연결. 필드와 Unity 수명주기는 PlayerMovement.cs에 있다.
public partial class PlayerMovement
{
    public void BindInputSource(PlayerMovementInputSource inputSource)
    {
        movementInputSource = inputSource != null
            ? inputSource
            : GetComponent<PlayerMovementInputSource>();
    }

    public bool ConsumeJumpAnimationRequest()
    {
        if (!jumpAnimationRequested)
            return false;

        jumpAnimationRequested = false;
        return true;
    }

    private void ReadMoveInput()
    {
        if (movementInputSource == null)
        {
            moveInput = Vector2.zero; // 입력 초기화
            moveDirection = Vector3.zero; // 방향 초기화
            isRunning = false; // 달리기 해제
            return;
        }

        ReadWalkToggleInput();

        if (GameplayInputBlocker.IsGameplayInputBlocked
            || IsEvading
            || IsMeleeAttackMoveLocked
            || (isMeleeCombatStance && !IsMeleeCombatLocomotionMode)
            || (IsMeleeGuarding
                && playerEquipment != null
                && !playerEquipment.CanCurrentWeaponMoveWhileGuarding))
        {
            moveInput = Vector2.zero; // 이동 차단
            moveDirection = Vector3.zero; // 방향 초기화
            isRunning = false; // 달리기 해제
            return;
        }

        moveInput = movementInputSource.RawMoveInput; // 원시 입력 소비
        moveDirection = GetMoveDirection(moveInput); // 카메라 기준
        isRunning = moveInput.sqrMagnitude > 0.001f && !isWalkMode && !IsCombatMoveMode; // 회피 입력은 별도 제어
    }

    private void ReadWalkToggleInput()
    {
        bool requested = movementInputSource != null
            && movementInputSource.ConsumeWalkToggleRequest();
        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return;

        if (requested)
            isWalkMode = !isWalkMode; // C 걷기 토글
    }

    private Vector3 GetMoveDirection(Vector2 input)
    {
        return locomotion != null
            ? locomotion.ResolveMoveDirection(input, useCameraRelativeMovement, movementCamera)
            : Vector3.zero;
    }

    public Vector3 ResolveMoveDirection(Vector2 input)
    {
        return GetMoveDirection(input);
    }

    private void ReadJumpInput()
    {
        bool requested = movementInputSource != null
            && movementInputSource.ConsumeJumpRequest();
        if (!requested || GameplayInputBlocker.IsGameplayInputBlocked)
            return;

        if (IsEvading || IsMeleeAttackMoveLocked)
            return;

        if (IsGrounded)
        {
            jumpRequested = true; // 점프 예약
            jumpAnimationRequested = true; // 점프 애니
            CancelAimUntilRelease(); // 조준 차단
        }
    }

    private PlayerInputFacade ResolveInputFacade()
    {
        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();
        if (inputFacade == null)
            inputFacade = PlayerInputFacade.Current;
        return inputFacade;
    }
}
