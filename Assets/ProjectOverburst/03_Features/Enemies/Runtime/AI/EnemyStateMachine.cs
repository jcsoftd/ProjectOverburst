using System;

public sealed class EnemyStateMachine // 적 상태 전환 관리
{
    public event Action<IEnemyState, IEnemyState> StateChanged; // 상태 변경 알림

    public IEnemyState CurrentState { get; private set; } // 현재 상태

    public void ChangeState(IEnemyState nextState) // 상태 변경
    {
        if (nextState == null || ReferenceEquals(CurrentState, nextState))
            return; // 빈 값 및 동일 상태 차단

        IEnemyState previousState = CurrentState; // 이전 상태 보관
        previousState?.Exit(); // 이전 상태 종료

        CurrentState = nextState; // 현재 상태 교체
        CurrentState.Enter(); // 새 상태 진입
        StateChanged?.Invoke(previousState, CurrentState); // 변경 알림
    }

    public void Update() // 현재 상태 갱신
    {
        CurrentState?.Update(); // 활성 상태만 실행
    }

    public void Clear() // 상태 머신 정리
    {
        if (CurrentState == null)
            return; // 활성 상태 없음

        IEnemyState previousState = CurrentState; // 종료 상태 보관
        previousState.Exit(); // 종료 처리
        CurrentState = null; // 현재 상태 해제
        StateChanged?.Invoke(previousState, null); // 해제 알림
    }
}
