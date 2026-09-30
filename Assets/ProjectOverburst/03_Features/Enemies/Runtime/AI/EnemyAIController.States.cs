using System.Collections.Generic;
using UnityEngine;

// EnemyAIController partial: 상태 전환과 상태 머신 연결. 필드와 Unity 수명주기는 EnemyAIController.cs에 있다.
public sealed partial class EnemyAIController
{
    internal void ChangeToRoam()
    {
        EnemyCombatCoordinator.Release(this);
        ChangeState(roamState);
    }

    internal void ChangeToSuspicious() => ChangeState(suspiciousState);

    internal void ChangeToChase() => ChangeState(chaseState);

    internal void ChangeToCombatWait(float delay = 0f)
    {
        if (ReferenceEquals(stateMachine?.CurrentState, combatWaitState))
        {
            combatWaitState.Delay(delay);
            return;
        }

        combatWaitState.PrepareDelay(delay);
        ChangeState(combatWaitState);
    }

    // 2026-09-30: 중형은 평타 경직 중에도 강공(예고 강공)만 골라 시작할 수 있다. 정예는 경직이 공격을 막지 않아 이 경로를 쓰지 않는다.
    private void TryStartStrongThroughHit()
    {
        if (abilityController == null || movementReaction == null || !movementReaction.CanStartStrongThroughHit
            || !IsAggroActive || !IsTargetValid() || abilityController.IsExecuting)
            return;
        abilityController.BeginStrongOnlyPass();
        try
        {
            if (!abilityController.HasAvailableAbility(target))
                return;
            if (ReferenceEquals(stateMachine?.CurrentState, attackState))
                attackState.Restart(); // 맞아서 끊긴 공격 차례를 그대로 이어 쓴다.
            else
                ChangeToAttack();
        }
        finally
        {
            abilityController.EndStrongOnlyPass();
        }
    }

    internal void ChangeToAttack()
    {
        // Keep completing the current facing action when no attack can start.
        // Entering Attack on a refusal would charge recovery and a turn cooldown.
        if (abilityController != null && !abilityController.HasAvailableAbility(target))
        {
            // Chase calls this on entering range; CombatWait owns stationary facing.
            // Do not restart its timer while it is already completing that turn.
            if (!ReferenceEquals(stateMachine?.CurrentState, combatWaitState))
                ChangeToCombatWait();
            return;
        }
        if (EnemyCombatCoordinator.TryAcquireAttackTurn(this, target))
            ChangeState(attackState);
        else
            ChangeToCombatWait(BehaviorProfile.AttackWaitDuration * Random.Range(0.85f, 1.15f));
    }

    internal void ChangeToReposition(bool lowHealthBackstep = false)
    {
        repositionState.PrepareBackpedal(lowHealthBackstep);
        ChangeState(repositionState);
    }

    internal bool TryEnterDodgeLunge()
    {
        EnemyBehaviorProfile profile = BehaviorProfile;
        if (profile.RepositionStyle != EnemyRepositionStyle.Dodge
            || Time.time < nextDodgeLungeTime
            || !IsTargetValid())
            return false;

        float distance = TargetDistance;
        if (distance < profile.DodgeLungeMinDistance || distance > profile.DodgeLungeMaxDistance)
            return false;

        nextDodgeLungeTime = Time.time + profile.DodgeLungeCooldown;
        if (Random.value > profile.DodgeLungeChance)
            return false; // 같은 쿨타임 구간에는 다시 굴리지 않아 연속 발동 방지

        repositionState.PrepareDodgeLunge();
        ChangeState(repositionState);
        return true;
    }

    internal bool TryConsumeLowHealthReposition()
    {
        EnemyBehaviorProfile profile = BehaviorProfile;
        float threshold = profile.LowHealthRepositionThreshold;
        if (threshold <= 0f || NormalizedHealth > threshold)
        {
            lowHealthRepositionConsumed = false;
            return false;
        }

        if (lowHealthRepositionConsumed || TargetDistance > AttackExitRange + 1f)
            return false;

        lowHealthRepositionConsumed = true;
        return true;
    }

    internal void ChangeToDefend() => ChangeState(defendState);

    internal void ChangeToReturn()
    {
        tacticalPositioning?.Reset();
        EnemyCombatCoordinator.Release(this);
        ResetAggroReleaseCandidate();
        ChangeState(returnState);
    }

    private void ResetAggroReleaseCandidate()
    {
        aggroReleaseCandidateSince = -1f;
    }

    private void ChangeState(IEnemyState nextState)
    {
        if (!ReferenceEquals(nextState, combatWaitState) && !ReferenceEquals(nextState, attackState))
            abilityController?.ClearPreparedAim();
        stateMachine?.ChangeState(nextState);
    }

    internal void ReleaseAttackTurn()
    {
        EnemyCombatCoordinator.ReleaseAttackTurn(this, BehaviorProfile.AttackTurnCooldown);
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (!sessionDeathReported)
        {
            sessionDeathReported = true;
            sessionMonsterDeathCount++; // 디버그 HUD용 현재 플레이 세션 처치 수
        }

        EnemyCombatCoordinator.Release(this);
        ChangeState(deadState);
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (!CombatTeamUtility.IsPlayerActorDamage(info))
            return;

        GameObject playerActor = CombatTeamUtility.ResolvePlayerActorObject(info.source);
        RequestAggro(playerActor != null ? playerActor.transform : target, true);
    }

    private void InitializeStateMachine()
    {
        if (initialized)
            return;

        stateMachine = new EnemyStateMachine();
        roamState = new EnemyRoamState(this);
        suspiciousState = new EnemySuspiciousState(this);
        chaseState = new EnemyChaseState(this);
        combatWaitState = new EnemyCombatWaitState(this);
        attackState = new EnemyAttackState(this);
        repositionState = new EnemyRepositionState(this);
        defendState = new EnemyDefendState(this);
        returnState = new EnemyReturnState(this);
        deadState = new EnemyDeadState(this);
        combatBehavior = new EnemyCombatBehavior(this);
        stateMachine.StateChanged += HandleStateChanged;
        initialized = true;
        EnemyBehaviorProfile profile = BehaviorProfile;
        nextIdleBreakTime = Time.time + Random.Range(profile.IdleBreakMinInterval, profile.IdleBreakMaxInterval);
    }

    private void EnableStateDrivenComponents()
    {
        if (movement != null && (health == null || !health.IsDead))
            movement.enabled = true;
    }

    private void PrepareMovementForAliveState()
    {
        if (movement == null)
            return;
        movement.enabled = true;
        movement.StopMovement();
    }

    private void HandleStateChanged(IEnemyState previousState, IEnemyState nextState)
    {
        ResetPlanningSchedule();
        if (!logStateChanges)
            return;
        string previousName = previousState != null ? previousState.Name : "None";
        string nextName = nextState != null ? nextState.Name : "None";
        Debug.Log("[EnemyAI] " + name + " " + previousName + " -> " + nextName, this);
    }
}
