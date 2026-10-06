using System.Collections;
using UnityEngine;

public sealed partial class EnemyBossMaterialExecutor
{
    private EnemyMotionHandle playbackHandle;
    private EnemyMotionResult playbackResult, executionResult;
    private EnemyBossAttackMaterial invalidatedMaterial;
    private float executionDeadline, executionUpperBound, executionLastRate, executionLastProgressAt;
    private bool bodyRecovered;
    public EnemyMotionHandle PlaybackHandle => playbackHandle;
    public EnemyMotionResult PlaybackResult => playbackResult;
    public EnemyMotionResult ExecutionResult => executionResult;
    public EnemyMotionCancelSnapshot LastCancelledSnapshot { get; private set; }
    public float ExecutionDeadline => executionDeadline;
    public bool IsHoldingPreparation => actor != null && actor.AnimationBridge.UsesOwnedMotion
        && actor.AnimationBridge.OwnsMotion(playbackHandle) && actor.AnimationBridge.CurrentMotionRole == EnemyMotionRole.Support
        && actor.AnimationBridge.GetMotionResult(playbackHandle).ReadyForHandoff;
    private bool UsesMotion => actor != null && actor.AnimationBridge != null && actor.AnimationBridge.UsesOwnedMotion;

    public override bool CanStart(EnemyAbilityDefinition ability, Transform target, in EnemyAbilityStartContext context)
    {
        Resolve(); if (!UsesMotion) return CanStart(ability, target);
        if (!Supports(ability) || !Usable || target == null || IsExecuting) return false;
        if (context.IsPrepared)
        { if (collection.Find(ability).delivery != EnemyBossMaterialDelivery.Boulder || !actor.AnimationBridge.CanCommitPreparedAttack(context)) return false; }
        else if (actor.Movement.IsActionLocked || actor.AnimationBridge.BlocksAttackStart
            || !context.KeepCurrentFacing && !actor.Movement.IsFacingForAttack(actor.AbilityController.ResolveAimPosition(target))) return false;
        Vector3 aim = context.IsPrepared || context.KeepCurrentFacing ? context.AimPosition : actor.AbilityController.ResolveAimPosition(target);
        Vector3 delta = aim - transform.position; delta.y = 0f;
        return EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability, delta.magnitude, actor.Health.NormalizedHp);
    }

    public override bool TryStart(EnemyAbilityDefinition ability, int abilityIndex, Transform target, in EnemyAbilityStartContext context)
    {
        Resolve(); if (!UsesMotion) return TryStart(ability, abilityIndex, target);
        if (!CanStart(ability, target, context)) return false;
        var material = collection.Find(ability);
        float baseRate = actor.Melee.AbilityAnimationSpeed * material.AnimationSpeedMultiplier;
        float flightBudget = material.delivery == EnemyBossMaterialDelivery.Boulder ? material.flightSeconds
            : material.delivery == EnemyBossMaterialDelivery.Spit ? ability.Range / material.projectileSpeed : 0f;
        int nextSequence = EnemyAttackSequence.Next();
        var request = new EnemyMotionRequest { Owner = this, Role = EnemyMotionRole.Attack, MotionId = "Attack_" + ability.AnimatorTrigger,
            Rate = ability.ResolvePhaseAnimationSpeed(0f, baseRate), ExternalCompletion = true, AttackSequence = nextSequence,
            GroupId = context.IsPrepared ? context.Preparation.GroupId : 0, OnInvalidated = OwnedInvalidated, OnTerminated = OwnedTerminated };
        EnemyMotionHandle accepted;
        bool started = context.IsPrepared ? actor.AnimationBridge.TryHandoff(context.Preparation, request, out accepted, out _)
            : actor.AnimationBridge.TryBeginMotion(request, out accepted, out _);
        if (!started) return false;
        reaction?.PrepareForAttack();
        playbackHandle = accepted; sequence = nextSequence; lease = actor.LeaseVersion; generation++;
        CurrentMaterial = material; LastFailure = null; progress = 0f; entered = bodyRecovered = false; upcomingStrike = 0;
        for (int i = 0; i < 3; i++) { hitTargets[i].Clear(); released[i] = warningShown[i] = false; }
        committedAim = context.IsPrepared || context.KeepCurrentFacing ? context.AimPosition : actor.AbilityController.ResolveAimPosition(target);
        if (!context.IsPrepared) committedAim.y = transform.position.y;
        Quaternion facing = context.IsPrepared || context.KeepCurrentFacing ? context.Facing : actor.Movement.PhysicalRotation;
        actor.Movement.AcquireMotionLock(playbackHandle, ability.ResolveExecutionDuration(baseRate) + .65f, true, facing);
        castSpeed = executionLastRate = baseRate; executionLastProgressAt = Time.time;
        executionDeadline = Time.time + ability.ResolveExecutionDuration(baseRate) + flightBudget + .65f;
        // Melee clamps every positive status rate to .01; the material multiplier is applied afterwards.
        executionUpperBound = Time.time + ability.ResolveExecutionDuration(.01f * material.AnimationSpeedMultiplier) + flightBudget + .65f;
        executionResult = playbackResult = new EnemyMotionResult(playbackHandle, EnemyMotionState.Entering);
        if (material.delivery == EnemyBossMaterialDelivery.Boulder) SetRockHeld(true);
        cast = StartCoroutine(ExecuteOwned(material, generation, flightBudget)); return true;
    }

    private IEnumerator ExecuteOwned(EnemyBossAttackMaterial material, int token, float flightBudget)
    {
        var handle = playbackHandle; var ability = material.ability; float lastProgress = 0f; bool completed = false;
        try
        {
            while (token == generation && Usable && actor.LeaseVersion == lease && actor.AnimationBridge.OwnsMotion(handle))
            {
                yield return AfterPhysics;
                if (token != generation || !Usable || actor.LeaseVersion != lease || !actor.AnimationBridge.OwnsMotion(handle)) yield break;
                if (!actor.AnimationBridge.TryReadMotion(handle, out var sample)) continue;
                if (sample.Clip != material.runtimeClip) { LastFailure = "Owned attack clip mismatch."; yield break; }
                entered = true; progress = Mathf.Clamp01(sample.Normalized);
                if (progress + .0001f < lastProgress) { LastFailure = "Owned attack timeline regressed."; yield break; }
                castSpeed = actor.Melee.AbilityAnimationSpeed * material.AnimationSpeedMultiplier;
                if (!Mathf.Approximately(castSpeed, executionLastRate))
                {
                    float remaining = ability.ResolvePacedTime(1f, castSpeed) - ability.ResolvePacedTime(progress, castSpeed);
                    executionDeadline = Mathf.Min(executionUpperBound, Time.time + remaining + flightBudget + .65f);
                    executionLastRate = castSpeed;
                }
                float phaseRate = ability.ResolvePhaseAnimationSpeed(progress, castSpeed);
                actor.AnimationBridge.SetOwnedPlaybackRate(handle, phaseRate);
                actor.Movement.RefreshMotionLock(handle, Mathf.Max(.25f, executionDeadline - Time.time));
                if (progress > lastProgress + .00001f) executionLastProgressAt = Time.time;
                float stallBudget = .65f + 2f / Mathf.Max(.01f, material.runtimeClip.frameRate * phaseRate);
                if (Time.time > executionDeadline || Time.time - executionLastProgressAt > stallBudget) { LastFailure = "Owned attack exceeded its pacing or stalled."; yield break; }
                if (material.advanceDistance > 0f)
                {
                    float prior = Mathf.Clamp01(Mathf.InverseLerp(material.advanceWindow.x, material.advanceWindow.y, lastProgress));
                    float now = Mathf.Clamp01(Mathf.InverseLerp(material.advanceWindow.x, material.advanceWindow.y, progress));
                    if (now > prior) actor.Movement.RequestOwnedAttackDisplacement(handle, transform.forward * (now - prior) * material.advanceDistance);
                }
                float consumedEnd = 0f;
                for (int phase = 0; phase < material.strikes.Length; phase++)
                {
                    var strike = material.strikes[phase];
                    if (!released[phase] && progress >= strike.impact)
                    {
                        released[phase] = true; ImpactCount++; upcomingStrike = Mathf.Min(phase + 1, material.strikes.Length - 1);
                        if (material.delivery != EnemyBossMaterialDelivery.Melee) Launch(material, phase); else EnemyStrongAttackImpactVfx.Play(strike.Origin(transform));
                        actor.AbilityController.NotifyAbilityImpact(ability, phase); StrikeReleased?.Invoke(material, phase);
                        if (token != generation || !Usable || !actor.AnimationBridge.OwnsMotion(handle)) yield break;
                    }
                    bool crossed = lastProgress < strike.impact && progress >= strike.impact;
                    if (material.delivery == EnemyBossMaterialDelivery.Melee && released[phase] && (progress <= strike.contactEnd + .0001f || crossed))
                        DealShape(material, phase, strike.Origin(transform), strike.Rotation(transform), sequence, lease);
                    if (token != generation || !Usable || !actor.AnimationBridge.OwnsMotion(handle)) yield break;
                    if (released[phase] && progress >= strike.contactEnd) consumedEnd = Mathf.Max(consumedEnd, strike.contactEnd);
                    actor.AnimationBridge.UpdateOwnedAttackContext(handle, phase, consumedEnd);
                    UpdateWarning(material, phase, progress);
                }
                lastProgress = progress;
                if (progress >= .999f && AllReleased(material)) { completed = bodyRecovered = true; break; }
            }
        }
        finally
        {
            if (token == generation)
            {
                cast = null;
                if (completed)
                {
                    actor.AnimationBridge.TryCompleteOwnedMotion(handle);
                    if (material.delivery == EnemyBossMaterialDelivery.Melee) HideWarnings();
                    TryFinishOwnedExecution();
                }
                else if (actor.AnimationBridge.OwnsMotion(handle)) actor.AnimationBridge.FailMotion(handle, EnemyMotionReason.UnexpectedExit);
            }
        }
    }

    public bool TryPlayMotion(string id, bool hold, int group, int step, Object owner, EnemyMotionRole role = EnemyMotionRole.Support)
    {
        Resolve(); if (!UsesMotion) return TryPlayMotion(id, hold);
        var entry = collection != null ? collection.FindMotion(id) : null;
        if (entry == null || !entry.IsPlayable || !Usable || IsExecuting || actor.Movement.IsActionLocked || actor.AnimationBridge.BlocksAttackStart) return false;
        var binding = actor.AnimationBridge.PlaybackProfile.Find(id); if (binding == null) return false;
        var request = new EnemyMotionRequest { Owner = owner != null ? owner : this, Role = role, MotionId = id,
            Rate = binding.rate, HoldLastPose = hold, GroupId = group, StepId = step,
            OnInvalidated = OwnedInvalidated, OnTerminated = OwnedTerminated };
        if (!actor.AnimationBridge.TryBeginMotion(request, out var handle, out _)) return false;
        playbackHandle = handle; playbackResult = executionResult = new EnemyMotionResult(handle, EnemyMotionState.Entering);
        LastFailure = null; generation++; lease = actor.LeaseVersion; bodyRecovered = false;
        executionDeadline = Time.time + actor.AnimationBridge.PlaybackProfile.DurationBudget(binding, binding.rate);
        actor.Movement.AcquireMotionLock(handle, executionDeadline - Time.time, hold, actor.Movement.PhysicalRotation);
        if (id == "UnearthRock") SetRockHeld(false);
        motion = StartCoroutine(ObserveOwnedSupport(entry, hold, generation, handle)); return true;
    }
    private IEnumerator ObserveOwnedSupport(EnemyBossMaterialCollection.Motion entry, bool hold, int token, EnemyMotionHandle handle)
    {
        while (token == generation && Usable && actor.LeaseVersion == lease)
        {
            yield return null;
            if (token != generation) yield break;
            var result = playbackResult.Handle == handle && playbackResult.IsTerminal ? playbackResult : actor.AnimationBridge.GetMotionResult(handle);
            if (result.IsTerminal) { playbackResult = executionResult = result; motion = null; yield break; }
            if (actor.AnimationBridge.TryReadMotion(handle, out var sample) && entry.id == "UnearthRock"
                && sample.Normalized >= 100f / (entry.runtime.length * entry.runtime.frameRate)) SetRockHeld(true);
            if (result.ReadyForHandoff)
            { playbackResult = executionResult = result; motion = null; actor.Movement.RefreshMotionLock(handle, 30f); yield break; }
            if (Time.time > executionDeadline && !hold) { actor.AnimationBridge.FailMotion(handle, EnemyMotionReason.DeadlineExceeded); yield break; }
        }
        if (actor.AnimationBridge.OwnsMotion(handle)) actor.AnimationBridge.CancelMotion(handle, EnemyMotionReason.OwnerCancelled);
    }
    public bool TryHandoffIntroduction(string id, Object owner, float blendSeconds)
    {
        if (!UsesMotion || owner == null) return false;
        var binding = actor.AnimationBridge.PlaybackProfile.Find(id); if (binding == null) return false;
        var request = new EnemyMotionRequest { Owner = owner, Role = EnemyMotionRole.Introduction, MotionId = id, GroupId = playbackHandle.GroupId,
            Rate = binding.rate, BlendInOverride = blendSeconds, HoldLastPose = true, OnInvalidated = OwnedInvalidated, OnTerminated = OwnedTerminated };
        if (!actor.AnimationBridge.TryHandoff(playbackHandle, request, out var accepted, out _)) return false;
        playbackHandle = accepted; playbackResult = executionResult = new EnemyMotionResult(accepted, EnemyMotionState.Entering);
        executionDeadline = Time.time + actor.AnimationBridge.PlaybackProfile.DurationBudget(binding, binding.rate);
        actor.Movement.AcquireMotionLock(accepted, executionDeadline - Time.time); return true;
    }
    private void OwnedInvalidated(EnemyMotionResult result)
    {
        if (result.Handle != playbackHandle) return;
        LastCancelledSnapshot = result.Snapshot; playbackResult = result;
        if (result.State == EnemyMotionState.Completed)
        { actor.Movement.ReleaseMotionLock(result.Handle, false); return; }
        generation++;
        if (cast != null) { StopCoroutine(cast); cast = null; }
        if (motion != null) { StopCoroutine(motion); motion = null; }
        actor.Movement.ReleaseMotionLock(result.Handle, result.Reason != EnemyMotionReason.HandoffAccepted);
        if (result.Reason == EnemyMotionReason.HandoffAccepted) return;
        invalidatedMaterial = CurrentMaterial;
        for (int i = flights.Count - 1; i >= 0; i--) EndFlight(i);
        HideWarnings(); SetRockHeld(false); CurrentMaterial = null; executionResult = result;
    }
    private void OwnedTerminated(EnemyMotionResult result)
    {
        if (result.Handle != playbackHandle) return;
        playbackResult = result;
        if (result.State == EnemyMotionState.Failed) LastFailure = "Motion failed: " + result.Reason;
        if (result.Reason != EnemyMotionReason.HandoffAccepted && result.State != EnemyMotionState.Completed && invalidatedMaterial != null)
        { var cancelled = invalidatedMaterial; invalidatedMaterial = null; AttackCancelled?.Invoke(cancelled); }
        if (result.State == EnemyMotionState.Completed)
        {
            if (CurrentMaterial == null) executionResult = result;
            else TryFinishOwnedExecution();
        }
    }
    private void TryFinishOwnedExecution()
    {
        if (!UsesMotion || !bodyRecovered || playbackResult.State != EnemyMotionState.Completed || cast != null || flights.Count != 0 || CurrentMaterial == null) return;
        actor.Movement.ReleaseMotionLock(playbackHandle); CurrentMaterial = null;
        executionResult = new EnemyMotionResult(playbackHandle, EnemyMotionState.Completed); CompletedCount++;
    }
    private void CancelOwnedExecution()
    {
        if (actor.AnimationBridge.OwnsMotion(playbackHandle)) actor.AnimationBridge.CancelMotion(playbackHandle, EnemyMotionReason.OwnerCancelled);
        else if (cast != null || motion != null || flights.Count != 0 || CurrentMaterial != null)
        {
            generation++; if (cast != null) StopCoroutine(cast); if (motion != null) StopCoroutine(motion); cast = motion = null;
            for (int i = flights.Count - 1; i >= 0; i--) EndFlight(i);
            actor.Movement.ReleaseMotionLock(playbackHandle); HideWarnings(); SetRockHeld(false); CurrentMaterial = null;
            executionResult = new EnemyMotionResult(playbackHandle, EnemyMotionState.Cancelled, EnemyMotionReason.OwnerCancelled);
        }
    }
}
