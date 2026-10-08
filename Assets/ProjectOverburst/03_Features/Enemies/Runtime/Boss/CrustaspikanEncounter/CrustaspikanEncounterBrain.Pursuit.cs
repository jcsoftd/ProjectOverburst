using UnityEngine;

public sealed partial class CrustaspikanEncounterBrain
{
    private bool pursuing, farPatternObserved, patternApproaches;
    private int farActions, farExecutionAtStart;
    private float pursuitAttemptAt, pursuitWalkAt, pursuitProgressAt, pursuitRefreshAt, pursuitRetryAt, pursuitStartDistance;
    private Vector3 pursuitOrigin, pursuitProgressPosition;
    private int FarReleaseCount => executor.LaunchCount + composite.RockThrowCount + composite.EliteThrowCount + composite.SummonedCount;
    private static bool ApproachesBeforeAttack(CrustaspikanEncounterSettings.Pattern pattern)
    {
        foreach (var step in pattern.steps)
        {
            if (step.kind == CrustaspikanStepKind.Attack || step.kind == CrustaspikanStepKind.ThrowElite) break;
            if (step.kind == CrustaspikanStepKind.Move && step.localDisplacement.z > .02f) return true;
        }
        return false;
    }
    private void ObserveFarExecution(bool acceptedMelee = false)
    {
        if (current == null || farPatternObserved || patternApproaches || Context.Distance <= settings.approachDistance - .5f) return;
        if (!acceptedMelee && FarReleaseCount <= farExecutionAtStart) return;
        farPatternObserved = true; farActions++;
    }
    private bool BeginPursuit()
    {
        if (Actor.Movement.IsStatusMovementLocked || (Actor.Movement.IsActionLocked && !Actor.Movement.IsOwnedTurning) || reaction.BlocksAttack)
        { pursuitRetryAt = Time.time + .4f; return false; }
        pursuing = true; pursuitAttemptAt = pursuitProgressAt = Time.time; pursuitWalkAt = 0f;
        pursuitOrigin = pursuitProgressPosition = Actor.transform.position;
        pursuitStartDistance = Context.Distance;
        Actor.Movement.SetMoveFacingPolicy(false);
        if (!RefreshPursuitDestination()) { EndPursuit(false, "추격 경로 거절"); return false; }
        State = "추격 방향 준비";
        return true;
    }
    private bool RefreshPursuitDestination()
    {
        pursuitRefreshAt = Time.time + .25f;
        Vector3 destination = encounter.ClampArena(player.transform.position, 5f);
        if (!Actor.Movement.IsWalkablePosition(destination)) return false;
        float speed = settings.approachSpeed / Mathf.Max(.1f, Actor.Movement.Profile != null
            ? Actor.Movement.Profile.MoveSpeed : EnemyMovementProfile.MinimumMoveSpeed);
        // The arena clamp must not add its offset to the requested player stopping distance.
        float stop = Mathf.Max(.3f, settings.approachDistance - .5f - HorizontalDistance(destination, player.transform.position));
        Actor.Movement.SetDestination(destination, stop, EnemyLocomotionMode.Walk, speed);
        return Actor.Movement.HasDestination;
    }
    private CrustaspikanNodeStatus Pursue()
    {
        if (Actor.Movement.IsStatusMovementLocked || (Actor.Movement.IsActionLocked && !Actor.Movement.IsOwnedTurning) || reaction.BlocksAttack)
            return EndPursuit(false, "추격 행동 잠금");
        if (Context.Distance <= settings.approachDistance - .5f)
            return EndPursuit(true, "근접 거리 도달");
        float preparation = Mathf.Max(settings.attackPreparationTimeout, Actor.Movement.FacingPreparationBudget + .65f);
        if (Time.time >= pursuitAttemptAt + preparation + settings.pursuitWalkSeconds)
            return EndPursuit(false, "추격 전체 제한");
        if (Time.time >= pursuitRefreshAt && !RefreshPursuitDestination())
            return EndPursuit(false, "추격 경로 거절");
        Vector3 position = Actor.transform.position;
        if (pursuitWalkAt <= 0f && HorizontalDistance(position, pursuitOrigin) >= .02f)
        { pursuitWalkAt = Time.time; pursuitProgressAt = Time.time; }
        if (HorizontalDistance(position, pursuitProgressPosition) >= .1f)
        { pursuitProgressPosition = position; pursuitProgressAt = Time.time; }
        if (Actor.Movement.IsOwnedTurning)
        {
            // A legitimate retargeting turn is bounded by the total attempt, not diagnosed as a blocked motor.
            pursuitProgressAt = Time.time;
            if (pursuitWalkAt <= 0f && Time.time >= pursuitAttemptAt + preparation)
                return EndPursuit(false, "추격 회전 제한");
        }
        else if (Time.time - pursuitProgressAt >= .6f)
            return EndPursuit(false, pursuitWalkAt > 0f ? "추격 진척 없음" : "추격 보행 시작 실패");
        if (pursuitWalkAt > 0f && Time.time >= pursuitWalkAt + settings.pursuitWalkSeconds)
            return EndPursuit(Context.Distance < pursuitStartDistance - .1f, "추격 보행 구간 종료");
        State = pursuitWalkAt > 0f ? "거리 좁히기 · 추격 보행" : "추격 방향 준비";
        return CrustaspikanNodeStatus.Running;
    }
    private CrustaspikanNodeStatus EndPursuit(bool success, string reason)
    {
        if (HorizontalDistance(pursuitOrigin, Actor.transform.position) >= .1f) farActions = 0;
        CancelPursuit(); readyAt = Time.time + .1f;
        pursuitRetryAt = success ? Time.time : Time.time + .4f;
        State = reason + (success ? " · 접근 성공" : " · 재판단");
        return success ? CrustaspikanNodeStatus.Success : CrustaspikanNodeStatus.Failure;
    }
    private void CancelPursuit()
    {
        if (!pursuing) return;
        pursuing = false; pursuitAttemptAt = pursuitWalkAt = pursuitProgressAt = pursuitRefreshAt = 0f;
        pursuitOrigin = pursuitProgressPosition = default;
        Actor.Movement.StopMovement(); Actor.Movement.SetMoveFacingPolicy(false);
    }
}
