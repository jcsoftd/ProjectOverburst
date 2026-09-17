// GOAL A2: 상태 세 축 enum. 한 축 값으로 다른 축을 추론하지 않는다.
public enum PlayerConditionState
{
    Normal = 0,
    InputBlocked = 1,
    Stunned = 2,
    Dead = 3,
}

public enum PlayerLocomotionState
{
    Idle = 0,
    Moving = 1,
    Airborne = 2,
    Evading = 3,
    ControlledMove = 4,
}

public enum PlayerActionState
{
    None = 0,
    Attack = 1,
    GuardOrAim = 2,
    Interacting = 3,
}
