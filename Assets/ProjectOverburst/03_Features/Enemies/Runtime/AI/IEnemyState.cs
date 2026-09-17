public interface IEnemyState // 몬스터 상태 공통 계약
{
    string Name { get; } // 상태 이름

    void Enter(); // 상태 진입
    void Update(); // 상태 유지
    void Exit(); // 상태 종료
}
