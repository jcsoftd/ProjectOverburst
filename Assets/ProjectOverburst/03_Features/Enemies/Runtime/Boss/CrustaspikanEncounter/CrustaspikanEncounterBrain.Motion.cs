using UnityEngine;

public sealed partial class CrustaspikanEncounterBrain
{
    private static int nextMotionGroup;
    private int motionGroup;
    private EnemyMotionHandle stepMotionHandle;
    private bool stepComposite, facingAttempted;
    private float patternPlanDeadline;
    private EnemyMotionHandle transitionMotionHandle;
    private bool UsesMotion => Actor != null && Actor.AnimationBridge != null && Actor.AnimationBridge.UsesOwnedMotion;

    private CrustaspikanNodeStatus RunOwnedPhaseTransition()
    {
        State = "2페이즈 전환";
        if (!transitionStarted)
        {
            Actor.Movement.StopMovement(); Actor.AbilityController.Cancel();
            if (!executor.TryPlayMotion("Roar2", false, ++nextMotionGroup, 0, executor))
            { State = "페이즈 전환 모션 접수 대기"; return CrustaspikanNodeStatus.Running; }
            transitionMotionHandle = executor.PlaybackHandle; transitionDeadline = executor.ExecutionDeadline;
            transitionStarted = true; Phase = 2; encounter.Announce("2페이즈 · 군락의 분노", 5f);
        }
        var result = executor.ExecutionResult;
        if (result.Handle == transitionMotionHandle && result.IsTerminal)
        {
            transitionStarted = false; readyAt = Time.time + 1f;
            if (result.State != EnemyMotionState.Completed) State = "페이즈 전환 " + result.State + " · " + result.Reason;
        }
        else if (Time.time > transitionDeadline)
        { Actor.AnimationBridge.FailMotion(transitionMotionHandle, EnemyMotionReason.DeadlineExceeded); transitionStarted = false; readyAt = Time.time + 1f; State = "페이즈 전환 실행 제한"; }
        return CrustaspikanNodeStatus.Running;
    }

    private void BeginMotionPattern()
    {
        motionGroup = ++nextMotionGroup; facingAttempted = false; stepMotionHandle = default;
        Actor.Movement.SetMoveFacingPolicy(false);
        float plan = 0f;
        foreach (var step in current.steps)
        {
            if (step.kind == CrustaspikanStepKind.Wait || step.kind == CrustaspikanStepKind.Move) plan += Mathf.Max(0f, step.seconds) + .65f;
            else if (step.kind == CrustaspikanStepKind.Attack || step.kind == CrustaspikanStepKind.ThrowElite)
            {
                string key = step.kind == CrustaspikanStepKind.ThrowElite ? "ThrowRock" : step.materialOrMotion;
                var material = attacks[key];
                plan += material.ability.ResolveExecutionDuration(.01f * material.AnimationSpeedMultiplier) + material.flightSeconds + 5f;
            }
            else
            {
                string key = step.kind == CrustaspikanStepKind.LiftElite ? "UnearthRock" : step.materialOrMotion;
                var binding = Actor.AnimationBridge.PlaybackProfile.Find(key);
                plan += Actor.AnimationBridge.PlaybackProfile.DurationBudget(binding, binding != null ? binding.rate : 1f);
            }
        }
        patternPlanDeadline = Time.time + plan + 4f;
    }
    private bool PrepareOwnedStepFacing()
    {
        if (composite.HasPreparation) return true;
        if (Actor.Movement.IsOwnedTurning)
        { facingAttempted = true; State = "발 디딤 · " + current.label; return false; }
        if (current.rearOnly && !Actor.AnimationBridge.HasOwnedBlockingMotion) return true;
        if (Actor.Movement.IsFacingForAttack(player.transform.position, settings.attackFacingTolerance)) return true;
        if (facingAttempted || Actor.AnimationBridge.HasInvalidMotionProfile)
        { FailOwnedPattern("회전 종료 후 대상 방향 재계획"); return false; }
        State = "대상 조준 · " + current.label;
        Actor.Movement.StopMovement(); FaceTarget();
        if (Time.time > stepAttemptAt + Mathf.Max(settings.attackPreparationTimeout, Actor.Movement.FacingPreparationBudget + .65f))
            FailOwnedPattern("모션 준비 실패");
        return false;
    }
    private CrustaspikanNodeStatus RunOwnedPattern()
    {
        State = current.label;
        if (Time.time > patternPlanDeadline) return FailOwnedPattern("조립 계획 제한 초과");
        if (stepIndex >= current.steps.Length)
        {
            if (composite.HasPreparation) Actor.AbilityController.Cancel();
            Actor.Movement.SetMoveFacingPolicy(false); FinishPattern(); return CrustaspikanNodeStatus.Success;
        }
        var step = current.steps[stepIndex];
        if (!stepStarted)
        {
            bool prepare = step.kind == CrustaspikanStepKind.Attack || step.kind == CrustaspikanStepKind.ThrowElite
                || step.kind == CrustaspikanStepKind.LiftElite || step.kind == CrustaspikanStepKind.Motion && step.materialOrMotion == "UnearthRock";
            if (prepare && !PrepareOwnedStepFacing()) return CrustaspikanNodeStatus.Running;
            if (current == null) return CrustaspikanNodeStatus.Failure;
            stepStartedAt = Time.time; stepUntil = Time.time + step.seconds; stepComposite = false;
            switch (step.kind)
            {
                case CrustaspikanStepKind.Attack:
                case CrustaspikanStepKind.ThrowElite:
                    string key = step.kind == CrustaspikanStepKind.ThrowElite ? "ThrowRock" : step.materialOrMotion;
                    var material = attacks[key];
                    if (material.delivery == EnemyBossMaterialDelivery.Melee && !IntersectsTarget(material, Actor.Movement.PhysicalRotation)) return FailOwnedPattern("타격 범위 재계획");
                    EnemyAbilityStartContext context = default;
                    if (composite.HasPreparation)
                    { if (!composite.TryGetPreparedStartContext(out context)) return CrustaspikanNodeStatus.Running; }
                    else if (current.rearOnly) context = EnemyAbilityStartContext.RearCounter(player.transform.position, Actor.Movement.PhysicalRotation);
                    if (step.kind == CrustaspikanStepKind.ThrowElite && !context.IsPrepared) composite.SetNextThrowPayload(EnemyBossThrowPayload.Elite);
                    if (!Actor.AbilityController.TryStartAbility(material.ability, player.transform, context))
                    {
                        if (Time.time > stepAttemptAt + settings.attackPreparationTimeout + Actor.Movement.FacingPreparationBudget) return FailOwnedPattern("공격 접수 거절");
                        return CrustaspikanNodeStatus.Running;
                    }
                    stepComposite = composite.Supports(material.ability);
                    stepMotionHandle = stepComposite ? composite.PlaybackHandle : executor.PlaybackHandle;
                    Actor.AbilityController.ClearPreparedAim();
                    break;
                case CrustaspikanStepKind.LiftElite:
                    if (!composite.TryBeginPreparation(EnemyBossThrowPayload.Elite, motionGroup, stepIndex, player.transform)) return FailOwnedPattern("정예 뽑기 접수 실패");
                    stepMotionHandle = executor.PlaybackHandle; break;
                case CrustaspikanStepKind.Motion:
                    if (step.materialOrMotion == "UnearthRock")
                    { if (!composite.TryBeginPreparation(EnemyBossThrowPayload.Rock, motionGroup, stepIndex, player.transform)) return FailOwnedPattern("바위 뽑기 접수 실패"); }
                    else if (!executor.TryPlayMotion(step.materialOrMotion, false, motionGroup, stepIndex, executor)) return FailOwnedPattern("지원 모션 접수 실패");
                    stepMotionHandle = executor.PlaybackHandle; break;
                case CrustaspikanStepKind.Move:
                    if (!TryMoveDestination(step.localDisplacement, out moveDestination)) return FailOwnedPattern("이동 경로 거절");
                    Actor.Movement.SetMoveFacingPolicy(true);
                    if (composite.HasPreparation && !composite.TryBeginCarry(step.localDisplacement)) return FailOwnedPattern("없는 운반 모션");
                    break;
            }
            stepStarted = true;
            if (step.kind != CrustaspikanStepKind.Wait) CommitPatternStep();
        }
        bool done;
        if (step.kind == CrustaspikanStepKind.Wait) done = Time.time >= stepUntil;
        else if (step.kind == CrustaspikanStepKind.Move)
        {
            var mode = step.localDisplacement.z < 0f ? EnemyLocomotionMode.Backpedal : EnemyLocomotionMode.Walk;
            Actor.Movement.SetFacingDestination(moveDestination, .3f, Actor.transform.position + Actor.Movement.PhysicalRotation * Vector3.forward * 5f, mode, 1.5f);
            done = Time.time >= stepUntil || Vector3.Distance(Actor.transform.position, moveDestination) < .5f;
            if (done && composite.HasPreparation && Actor.AnimationBridge.CurrentMotionRole == EnemyMotionRole.Carry && !composite.TryEndCarry()) return FailOwnedPattern("운반 끝 자세 인계 실패");
        }
        else
        {
            var result = stepComposite ? composite.ExecutionResult : executor.ExecutionResult;
            done = result.Handle == stepMotionHandle && (result.State == EnemyMotionState.Completed
                || (step.kind == CrustaspikanStepKind.LiftElite || step.kind == CrustaspikanStepKind.Motion && step.materialOrMotion == "UnearthRock") && result.ReadyForHandoff);
            if (result.Handle == stepMotionHandle && result.IsTerminal && result.State != EnemyMotionState.Completed) return FailOwnedPattern("동작 " + result.State + " · " + result.Reason);
            float deadline = stepComposite ? composite.ExecutionDeadline : executor.ExecutionDeadline;
            if (!done && deadline > 0f && Time.time > deadline) return FailOwnedPattern("실행 계획 제한 초과");
        }
        if (done)
        {
            Actor.Movement.StopMovement(); stepIndex++; stepStarted = false; stepAttemptAt = Time.time; facingAttempted = false;
            if (!composite.HasPreparation) Actor.Movement.SetMoveFacingPolicy(false);
        }
        return CrustaspikanNodeStatus.Running;
    }
    private CrustaspikanNodeStatus FailOwnedPattern(string reason)
    { CancelPattern(); State = reason; readyAt = Time.time + .4f; return CrustaspikanNodeStatus.Failure; }
}
