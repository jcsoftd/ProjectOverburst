public sealed class EnemyDefendState : IEnemyState // 방패 보유 몬스터 전용 방어
{
    private readonly EnemyAIController owner;
    private float endTime;

    public string Name { get { return "Defend"; } }

    public EnemyDefendState(EnemyAIController owner)
    {
        this.owner = owner;
    }

    public void Enter()
    {
        if (!owner.CanDefend)
        {
            owner.ChangeToCombatWait();
            return;
        }

        owner.Movement?.StopMovement();
        owner.CancelAttack();
        owner.DefenseController.SetDefending(true);
        owner.AnimationBridge?.PlayBlockStart();
        endTime = UnityEngine.Time.time + owner.BehaviorProfile.DefendDuration;
    }

    public void Update()
    {
        if (owner.ShouldReturnFromCombat())
        {
            owner.ChangeToReturn();
            return;
        }

        owner.FaceTarget();
        if (UnityEngine.Time.time >= endTime)
            owner.ChangeToCombatWait(owner.BehaviorProfile.RecoveryDuration);
    }

    public void Exit()
    {
        owner.DefenseController?.SetDefending(false);
        owner.AnimationBridge?.PlayBlockStop();
    }
}
