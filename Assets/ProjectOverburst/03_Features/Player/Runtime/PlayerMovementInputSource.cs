using UnityEngine;

[DefaultExecutionOrder(240)]
[DisallowMultipleComponent]
public sealed class PlayerMovementInputSource : MonoBehaviour
{
    private Vector2 rawMoveInput;
    private bool walkToggleRequested;
    private bool jumpRequested;
    private PlayerInputFacade inputFacade;

    public Vector2 RawMoveInput => rawMoveInput;

    private void Update()
    {
        // GOAL A2: WASD/C/Space 직접 읽기 대신 Gameplay Move/WalkToggle/Jump를 사용한다.
        PlayerInputFacade facade = ResolveFacade();
        if (facade == null)
        {
            rawMoveInput = Vector2.zero; // 입력 초기화
            return;
        }

        rawMoveInput = Vector2.ClampMagnitude(facade.MoveValue, 1f); // 원시 이동

        if (facade.WalkTogglePressedThisFrame)
            walkToggleRequested = true; // 걷기 요청

        if (facade.JumpPressedThisFrame)
            jumpRequested = true; // 점프 요청
    }

    private void OnDisable()
    {
        Clear();
    }

    public bool ConsumeWalkToggleRequest()
    {
        bool requested = walkToggleRequested;
        walkToggleRequested = false;
        return requested;
    }

    public bool ConsumeJumpRequest()
    {
        bool requested = jumpRequested;
        jumpRequested = false;
        return requested;
    }

    public void Clear()
    {
        rawMoveInput = Vector2.zero;
        walkToggleRequested = false;
        jumpRequested = false;
    }

    private PlayerInputFacade ResolveFacade()
    {
        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();
        if (inputFacade == null)
            inputFacade = PlayerInputFacade.Current;
        return inputFacade;
    }
}
