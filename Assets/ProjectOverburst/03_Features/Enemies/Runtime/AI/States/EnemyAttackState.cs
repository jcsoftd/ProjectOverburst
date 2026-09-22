public sealed class EnemyAttackState : IEnemyState // 플레이어 근접 공격
{
    private readonly EnemyAIController owner; // 상태 소유자
    private bool attackStarted; // 단일 공격 시작 여부

    public string Name { get { return "Attack"; } } // 상태 이름

    public EnemyAttackState(EnemyAIController owner) // 소유자 연결
    {
        this.owner = owner; // 상태 제어 참조 저장
    }

    public void Enter() // 공격 진입
    {
        owner.Movement?.StopMovement(); // 공격 중 이동 정지
        attackStarted = owner.TryStartAttack(); // 능력 실행 표면으로 공격 1회 요청
    }

    public void Update() // 공격 조건 갱신
    {
        if (owner.ShouldReturnFromCombat())
        {
            owner.ChangeToReturn(); // 장거리 이탈 유지 시 복귀
            return; // 전환 후 공격 중단
        }

        // A target leaving the aimed point is a miss, not a request to cancel
        // the committed animation and chase it before the swing/release ends.
        if (attackStarted && owner.IsCommittedAttackPlaying)
            return;

        if (owner.IsTargetBeyond(owner.AttackExitRange))
        {
            owner.ChangeToChase(); // 공격 거리 이탈 시 다시 접근
            return; // 전환 후 공격 중단
        }

        if (!attackStarted || !owner.IsAttackInProgress())
            owner.ChangeToCombatWait(owner.BehaviorProfile.RecoveryDuration); // 공격 후 판단 빈틈
    }

    public void Exit() // 공격 종료
    {
        owner.CancelAttack(); // 진행 공격 취소
        owner.ReleaseAttackTurn(); // 다음 몬스터에게 공격 순번 반환
    }
}
