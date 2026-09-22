using UnityEngine;

public sealed class EnemyCombatBehavior // 전투 거리 기반 단순 판단
{
    private readonly EnemyAIController owner;

    public EnemyCombatBehavior(EnemyAIController owner)
    {
        this.owner = owner;
    }

    public void Evaluate()
    {
        if (owner.ShouldReturnFromCombat())
        {
            owner.ChangeToReturn();
            return;
        }

        EnemyBehaviorProfile profile = owner.BehaviorProfile;
        if (owner.TryConsumeLowHealthReposition())
        {
            owner.ChangeToReposition(true);
            return;
        }

        if (owner.IsTargetBeyond(owner.AttackEnterRange))
        {
            owner.ChangeToChase();
            return;
        }

        if (profile.Tendency == EnemyBehaviorTendency.Defender
            && owner.CanDefend
            && Random.value < profile.DefendChance)
        {
            owner.ChangeToDefend();
            return;
        }

        if (owner.TargetDistance < Mathf.Min(profile.PreferredMinDistance, owner.AttackEnterRange * .75f))
        {
            owner.ChangeToReposition();
            return;
        }

        owner.ChangeToAttack();
    }
}
