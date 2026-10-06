using System.Collections.Generic;
using UnityEngine;

// EnemyAIController partial: AI 갱신 주기(LOD)와 계획 틱. 필드와 Unity 수명주기는 EnemyAIController.cs에 있다.
public sealed partial class EnemyAIController
{
    private bool ShouldRunAiTick()
    {
        float now = Time.time;
        if (aiTickScheduleInitialized && !forceAiTick && now < nextAiTickTime)
        {
            aiSkippedUpdateCount++;
            return false; // 예약 전 프레임은 거리·가시성 조회 생략
        }

        bool hasActiveTarget = target != null && target.gameObject.activeInHierarchy;
        float sqrDistance = ResolveAiLodSqrDistance(hasActiveTarget);
        bool requiresFullRate = forceAiTick || RequiresFullRateAi(sqrDistance, hasActiveTarget);
        bool isVisible = requiresFullRate
            || EnemyAiTickScheduler.IsLikelyVisible(aiLodRenderers, transform.position);
        float interval = EnemyAiTickScheduler.ResolveInterval(
            ActiveEnemies.Count,
            sqrDistance,
            isVisible,
            requiresFullRate);
        currentAiTickInterval = interval;

        if (interval <= 0f || forceAiTick)
        {
            forceAiTick = false;
            aiTickScheduleInitialized = false;
            aiTickCount++;
            return true;
        }

        if (!aiTickScheduleInitialized)
        {
            nextAiTickTime = now + EnemyAiTickScheduler.ResolveStaggerDelay(interval, GetInstanceID());
            aiTickScheduleInitialized = true;
        }

        if (now < nextAiTickTime)
        {
            aiSkippedUpdateCount++;
            return false;
        }

        nextAiTickTime = now + interval;
        aiTickCount++;
        return true;
    }

    private bool RequiresFullRateAi(float sqrDistance, bool hasActiveTarget)
    {
        IEnemyState current = stateMachine != null ? stateMachine.CurrentState : null;
        if (ReferenceEquals(current, attackState)
            || ReferenceEquals(current, repositionState)
            || ReferenceEquals(current, defendState))
        {
            return true;
        }

        return hasActiveTarget
            && sqrDistance <= EnemyAiTickScheduler.FullRateDistance * EnemyAiTickScheduler.FullRateDistance;
    }

    private float ResolveAiLodSqrDistance(bool hasActiveTarget)
    {
        if (hasActiveTarget)
            return HorizontalSqrDistance(transform.position, target.position);

        return EnemyAiTickScheduler.ResolveCameraSqrDistance(transform.position);
    }

    private void ResetAiTickSchedule()
    {
        ResetChaseObservation();
        delayedChaseEnabled = GetComponent<EnemyBossCombatDirector>() == null;
        ResetPlanningSchedule();
        planningEvaluationCount = planningReuseCount = 0;
        nextAiTickTime = 0f;
        currentAiTickInterval = 0f;
        aiTickScheduleInitialized = false;
        forceAiTick = false;
        aiTickCount = 0;
        aiSkippedUpdateCount = 0;
    }

    internal void NotifyFacingTurnCompleted() => RequestImmediateAiTick();

    private void RequestImmediateAiTick()
    {
        ResetPlanningSchedule();
        forceAiTick = true; // 피격·지원 요청은 LOD 대기 없이 반응
        nextAiTickTime = 0f;
        aiTickScheduleInitialized = false;
    }

    private bool ConsumePlanningTick(ref float nextTime)
    {
        float interval = PlanningInterval;
        if (interval > 0f && Time.time < nextTime) return false;
        nextTime = EnemyAiTickScheduler.NextPlanningTime(Time.time, interval, GetInstanceID());
        return true;
    }

    private void ResetPlanningSchedule()
    {
        hasCachedChasePlan = false;
        lastChasePlanningTime = -1f;
        nextChasePlanningTime = nextSeparationPlanningTime = nextTargetPlanningTime = 0f;
    }
}
