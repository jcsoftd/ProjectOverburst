public sealed class EnemyReturnState : IEnemyState // 생성 위치 복귀
{
    private readonly EnemyAIController owner; // 상태 소유자

    public string Name { get { return "Return"; } } // 상태 이름

    public EnemyReturnState(EnemyAIController owner) // 소유자 연결
    {
        this.owner = owner; // 상태 제어 참조 저장
    }

    public void Enter() // 복귀 진입
    {
        owner.CancelAttack(); // 공격 취소
        owner.Movement?.SetDestination(owner.HomePosition, owner.ReturnArriveDistance); // 생성 위치 지정
    }

    public void Update() // 복귀 조건 갱신
    {
        if (owner.TryEvaluateReturnAwareness())
            return; // 복귀 중 직접 발견하면 경계 연출부터 다시 진입

        if (owner.TryRejoinEngagedGroup())
            return; // 같은 부대의 실제 교전이 남아 있으면 즉시 재합류

        if (owner.IsAtHome())
        {
            owner.ChangeToRoam(); // 복귀 후 평시 행동 재개
            return; // 전환 후 이동 중단
        }

        owner.Movement?.SetDestination(owner.HomePosition, owner.ReturnArriveDistance); // 복귀 목적지 유지
    }

    public void Exit() // 복귀 종료
    {
        owner.Movement?.StopMovement(); // 복귀 이동 정지
    }
}
