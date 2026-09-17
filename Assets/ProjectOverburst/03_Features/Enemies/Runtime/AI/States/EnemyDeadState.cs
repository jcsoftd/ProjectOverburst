public sealed class EnemyDeadState : IEnemyState // 사망 행동 정지
{
    private readonly EnemyAIController owner; // 상태 소유자

    public string Name { get { return "Dead"; } } // 상태 이름

    public EnemyDeadState(EnemyAIController owner) // 소유자 연결
    {
        this.owner = owner; // 상태 제어 참조 저장
    }

    public void Enter() // 사망 진입
    {
        owner.CancelAttack(); // 공격 정지
        owner.Movement?.StopForDeath(); // 이동 정지
    }

    public void Update() // 사망 유지
    {
    }

    public void Exit() // 사망 종료
    {
    }
}
