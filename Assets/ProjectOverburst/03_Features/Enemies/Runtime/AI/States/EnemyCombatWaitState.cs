using UnityEngine;

public sealed class EnemyCombatWaitState : IEnemyState // 대치와 공격 순번 대기 통합
{
    private readonly EnemyAIController owner;
    private float pendingDelay;
    private float readyTime;

    public string Name => "CombatWait";

    public EnemyCombatWaitState(EnemyAIController owner)
    {
        this.owner = owner;
    }

    public void PrepareDelay(float delay)
    {
        pendingDelay = Mathf.Max(0f, delay);
    }

    public void Delay(float delay)
    {
        owner.Movement?.StopMovement();
        owner.CancelAttack();
        readyTime = Time.time + Mathf.Max(0f, delay);
    }

    public void Enter()
    {
        Delay(pendingDelay);
        pendingDelay = 0f;
    }

    public void Update()
    {
        if (owner.ShouldReturnFromCombat())
        {
            owner.ChangeToReturn();
            return;
        }

        owner.UpdateCombatWaitSeparation(); // 겹친 대기 몬스터만 천천히 분리
        owner.FaceTarget();
        if (Time.time >= readyTime)
            owner.ResolveCombatWaitDecision();
    }

    public void Exit()
    {
    }
}
