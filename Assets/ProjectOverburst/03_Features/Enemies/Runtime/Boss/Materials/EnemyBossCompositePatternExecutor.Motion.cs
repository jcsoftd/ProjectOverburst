using System.Collections;
using UnityEngine;

public sealed partial class EnemyBossCompositePatternExecutor
{
    EnemyMotionHandle playbackHandle, preparationHandle;
    EnemyMotionResult playbackResult, executionResult;
    Vector3 preparedAim;
    Quaternion preparedFacing;
    uint preparedLease;
    Transform preparedTarget;
    float preparationDeadline;
    Vector3 carryPreviousPosition;
    bool bodyRecovered;
    bool extractionAnchored;
    Vector3 extractionGround;
    float executionDeadline, executionUpperBound, executionLastRate, executionLastProgressAt;
    public EnemyMotionHandle PlaybackHandle => playbackHandle;
    public EnemyMotionResult ExecutionResult => executionResult;
    public EnemyMotionResult PlaybackResult => playbackResult;
    public float ExecutionDeadline => executionDeadline;
    public bool HasPreparation => UsesMotion && prepared && preparedLease == actor.LeaseVersion && actor.AnimationBridge.OwnsMotion(preparationHandle);
    bool UsesMotion => actor != null && actor.AnimationBridge != null && actor.AnimationBridge.UsesOwnedMotion;

    public bool TryBeginPreparation(EnemyBossThrowPayload payload, int group, int step, Transform aimTarget)
    {
        Resolve(); if (!UsesMotion || aimTarget == null || group == 0 || HasPreparation || IsExecuting) return false;
        // Payload and aim are installed only after the support request is accepted.
        if (!basic.TryPlayMotion("UnearthRock", true, group, step, this)) return false;
        overridePayload = payload; preparedPayload = ResolvePayload(); extractionAnchored = false;
        preparationHandle = basic.PlaybackHandle; preparedLease = actor.LeaseVersion;
        preparedTarget = aimTarget; preparationDeadline = Time.time + actor.AnimationBridge.PlaybackProfile.DurationBudget(actor.AnimationBridge.PlaybackProfile.Find("UnearthRock"), 1f) + 30f;
        preparedAim = actor.AbilityController.ResolveAimPosition(aimTarget); preparedAim.y = transform.position.y;
        preparedFacing = actor.Movement.PhysicalRotation; prepared = true;
        return true;
    }
    public bool TryGetPreparedStartContext(out EnemyAbilityStartContext context)
    {
        context = default;
        if (!HasPreparation) return false;
        context = new EnemyAbilityStartContext(preparationHandle, preparedAim, preparedFacing, preparedLease);
        return actor.AnimationBridge.CanCommitPreparedAttack(context);
    }
    public bool TryBeginCarry(Vector3 localDirection)
    {
        if (Mathf.Abs(localDirection.x) > .001f || Mathf.Abs(localDirection.z) < .001f || !TryGetPreparedStartContext(out var context)) return false;
        var request = new EnemyMotionRequest { Owner = this, Role = EnemyMotionRole.Carry, MotionId = "CarryLocomotion", Rate = 1f,
            GroupId = context.Preparation.GroupId, OnInvalidated = CarryInvalidated };
        if (!actor.AnimationBridge.TryHandoff(preparationHandle, request, out var accepted, out _)) return false;
        preparationHandle = accepted; carryPreviousPosition = transform.position;
        actor.Movement.AcquireMotionFacing(accepted, preparedFacing); actor.Movement.SetMoveFacingPolicy(true);
        actor.AnimationBridge.TryUpdateOwnedMotionIntent(accepted, new EnemyMotionIntent(new Vector2(0f, Mathf.Sign(localDirection.z)), 1f));
        return true;
    }
    public bool TryEndCarry()
    {
        if (!HasPreparation || actor.AnimationBridge.CurrentMotionRole != EnemyMotionRole.Carry) return false;
        var binding = actor.AnimationBridge.PlaybackProfile.Find("UnearthRock");
        var request = new EnemyMotionRequest { Owner = this, Role = EnemyMotionRole.Support, MotionId = "UnearthRock", Rate = binding.rate,
            GroupId = preparationHandle.GroupId, StartNormalized = binding.completeNormalized, HoldLastPose = true, OnInvalidated = CarryInvalidated };
        if (!actor.AnimationBridge.TryHandoff(preparationHandle, request, out var accepted, out _)) return false;
        preparationHandle = accepted; actor.Movement.AcquireMotionLock(accepted, 30f, true, preparedFacing); return true;
    }
    void CarryInvalidated(EnemyMotionResult result)
    {
        if (result.Handle != preparationHandle) return;
        actor.Movement.ReleaseMotionLock(result.Handle, result.Reason != EnemyMotionReason.HandoffAccepted);
        if (result.Reason != EnemyMotionReason.HandoffAccepted) { prepared = false; ReleaseHeld(); }
    }
    public override bool CanStart(EnemyAbilityDefinition ability, Transform aimTarget, in EnemyAbilityStartContext context)
    {
        Resolve(); if (!UsesMotion) return CanStart(ability, aimTarget);
        if (!Supports(ability) || !Usable || aimTarget == null || IsExecuting) return false;
        if (context.IsPrepared)
        {
            if (ability != patterns.throwMaterial.ability) return false;
            if (!HasPreparation || preparationHandle != context.Preparation || !actor.AnimationBridge.CanCommitPreparedAttack(context)) return false;
        }
        else if (basic.IsExecuting || actor.Movement.IsActionLocked || actor.AnimationBridge.BlocksAttackStart
            || !context.KeepCurrentFacing && !actor.Movement.IsFacingForAttack(actor.AbilityController.ResolveAimPosition(aimTarget))) return false;
        Vector3 delta = (context.IsPrepared || context.KeepCurrentFacing ? context.AimPosition : actor.AbilityController.ResolveAimPosition(aimTarget)) - transform.position; delta.y = 0f;
        return EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability, delta.magnitude, actor.Health.NormalizedHp);
    }
    public override bool TryStart(EnemyAbilityDefinition ability, int index, Transform aimTarget, in EnemyAbilityStartContext context)
    {
        Resolve(); if (!UsesMotion) return TryStart(ability, index, aimTarget);
        if (!CanStart(ability, aimTarget, context)) return false;
        var selectedSpit = patterns.Find(ability);
        var material = selectedSpit != null ? selectedSpit.material : patterns.throwMaterial;
        float baseRate = actor.Melee.AbilityAnimationSpeed * material.AnimationSpeedMultiplier;
        int nextSequence = EnemyAttackSequence.Next();
        var request = new EnemyMotionRequest { Owner = this, Role = EnemyMotionRole.Attack, MotionId = "Attack_" + ability.AnimatorTrigger,
            Rate = ability.ResolvePhaseAnimationSpeed(0f, baseRate), ExternalCompletion = true, AttackSequence = nextSequence,
            GroupId = context.IsPrepared ? context.Preparation.GroupId : 0, OnInvalidated = OwnedInvalidated, OnTerminated = OwnedTerminated };
        EnemyMotionHandle accepted;
        bool started = context.IsPrepared ? actor.AnimationBridge.TryHandoff(context.Preparation, request, out accepted, out _)
            : actor.AnimationBridge.TryBeginMotion(request, out accepted, out _);
        if (!started) return false;
        // Accepted handoff retains the held visual until it is transferred to this execution.
        var payload = context.IsPrepared ? preparedPayload ?? EnemyBossThrowPayload.Rock : ResolvePayload();
        reaction?.PrepareForAttack(); playbackHandle = accepted; sequence = nextSequence; lease = actor.LeaseVersion; generation++;
        spit = selectedSpit; current = material; target = aimTarget;
        aim = context.IsPrepared || context.KeepCurrentFacing ? context.AimPosition : actor.AbilityController.ResolveAimPosition(target);
        if (!context.IsPrepared) aim.y = transform.position.y;
        aimRotation = context.IsPrepared || context.KeepCurrentFacing ? context.Facing : actor.Movement.PhysicalRotation;
        aimDistance = Mathf.Max(4f, Vector3.Distance(aim, transform.position));
        speed = executionLastRate = baseRate; executionLastProgressAt = Time.time;
        // Bound includes every configured ballistic flight, rather than a fixed BT timeout.
        float flightBudget = ResolveOwnedFlightBudget();
        executionDeadline = Time.time + ability.ResolveExecutionDuration(baseRate) + flightBudget + .65f;
        executionUpperBound = Time.time + ability.ResolveExecutionDuration(.01f * material.AnimationSpeedMultiplier) + flightBudget + .65f;
        actor.Movement.AcquireMotionLock(playbackHandle, executionDeadline - Time.time, true, aimRotation);
        progress = 0f; entered = bodyRecovered = false; LastFailure = null;
        executionResult = playbackResult = new EnemyMotionResult(playbackHandle, EnemyMotionState.Entering);
        for (int i = 0; i < 3; i++) { released[i] = shown[i] = false; damaged[i].Clear(); }
        PlanEmissions();
        if (spit == null)
        {
            if (held == null) held = Acquire(payload == EnemyBossThrowPayload.Elite ? patterns.elite : null);
            basic.ClaimSupportPresentation(this); PlaceHeld(Hands(), aimRotation); throwCount++;
        }
        else ReleaseHeld();
        prepared = false; preparedPayload = overridePayload = null; preparationHandle = default;
        GetComponent<EnemyBossCombatDirector>()?.NotifyCommitted(ability);
        cast = StartCoroutine(ExecuteOwned(generation, flightBudget)); return true;
    }
    float ResolveOwnedFlightBudget()
    {
        float maximum = Mathf.Max(current.flightSeconds, patterns.elite.flightSeconds);
        if (patterns.elite.trajectory == EnemyBossPayloadTrajectory.Ballistic)
            maximum = Mathf.Max(maximum, BallisticDuration(patterns.elite, Hands(), Ground(aim) + PayloadCenter(patterns.elite)));
        if (spit != null) foreach (var emission in spit.emissions)
        {
            var payload = emission.payload;
            float duration = payload.trajectory == EnemyBossPayloadTrajectory.Ballistic
                ? BallisticDuration(payload, Mouth(), Ground(Endpoint(0f, emission.landingDistance + emission.scatter, 0f)) + PayloadCenter(payload)) : payload.flightSeconds;
            maximum = Mathf.Max(maximum, duration);
        }
        return maximum + .35f;
    }
    IEnumerator ExecuteOwned(int token, float flightBudget)
    {
        var material = current; var handle = playbackHandle; float last = 0f; bool completed = false;
        try
        {
            while (token == generation && Usable && actor.LeaseVersion == lease && actor.AnimationBridge.OwnsMotion(handle))
            {
                yield return AfterPhysics;
                if (token != generation || !Usable || actor.LeaseVersion != lease || !actor.AnimationBridge.OwnsMotion(handle)) yield break;
                if (!actor.AnimationBridge.TryReadMotion(handle, out var sample)) continue;
                if (sample.Clip != material.runtimeClip) { LastFailure = "Composite owned clip mismatch."; yield break; }
                entered = true; progress = Mathf.Clamp01(sample.Normalized);
                if (progress + .0001f < last) { LastFailure = "Composite owned timeline regressed."; yield break; }
                speed = actor.Melee.AbilityAnimationSpeed * material.AnimationSpeedMultiplier;
                if (!Mathf.Approximately(speed, executionLastRate))
                {
                    executionDeadline = Mathf.Min(executionUpperBound, Time.time + material.ability.ResolvePacedTime(1f, speed)
                        - material.ability.ResolvePacedTime(progress, speed) + flightBudget + .65f);
                    executionLastRate = speed;
                }
                float rate = material.ability.ResolvePhaseAnimationSpeed(progress, speed);
                actor.AnimationBridge.SetOwnedPlaybackRate(handle, rate);
                actor.Movement.RefreshMotionLock(handle, Mathf.Max(.25f, executionDeadline - Time.time));
                if (progress > last + .00001f) executionLastProgressAt = Time.time;
                if (Time.time > executionDeadline || Time.time - executionLastProgressAt > .65f + 2f / Mathf.Max(.01f, material.runtimeClip.frameRate * rate))
                { LastFailure = "Composite pacing deadline or stall."; yield break; }
                ActiveBeamPhase = -1;
                for (int phase = 0; phase < material.strikes.Length; phase++)
                {
                    var strike = material.strikes[phase];
                    if (!released[phase] && progress >= strike.impact)
                    { released[phase] = true; ReleaseCount++; actor.AbilityController.NotifyAbilityImpact(material.ability, phase); if (spit == null) Throw(phase); }
                    if (token != generation || !Usable || !actor.AnimationBridge.OwnsMotion(handle)) yield break;
                    bool crossed = last < strike.contactStart && progress >= strike.contactStart;
                    if (spit != null && released[phase] && (progress <= strike.contactEnd || crossed))
                    { ActiveBeamPhase = phase; Beam(phase); if (token != generation || !Usable) yield break; }
                    Warning(phase);
                    actor.AnimationBridge.UpdateOwnedAttackContext(handle, phase, released[phase] && progress >= strike.contactEnd ? strike.contactEnd : 0f);
                }
                if (spit != null)
                {
                    for (int i = 0; i < emitted.Length; i++) if (!emitted[i] && progress >= pendingEmissions[i].normalized)
                    { emitted[i] = true; var item = pendingEmissions[i]; if (last <= material.strikes[item.emission.phase].contactEnd) Eject(item.emission, item.seed); }
                    if (ActiveBeamPhase < 0) activeSpray?.Stop(false);
                }
                last = progress;
                if (progress >= .999f) { completed = bodyRecovered = true; break; }
            }
        }
        finally
        {
            if (token == generation)
            {
                cast = null; activeSpray?.Stop(false); ActiveBeamPhase = -1;
                if (completed) { actor.AnimationBridge.TryCompleteOwnedMotion(handle); if (spit != null) HideWarnings(); TryFinishOwnedExecution(); }
                else if (actor.AnimationBridge.OwnsMotion(handle)) actor.AnimationBridge.FailMotion(handle, EnemyMotionReason.UnexpectedExit);
            }
        }
    }
    void OwnedInvalidated(EnemyMotionResult result)
    {
        if (result.Handle != playbackHandle) return;
        playbackResult = result;
        if (result.State == EnemyMotionState.Completed) { actor.Movement.ReleaseMotionLock(result.Handle, false); return; }
        generation++; if (cast != null) { StopCoroutine(cast); cast = null; }
        actor.Movement.ReleaseMotionLock(result.Handle); ClearFlights(); ReleaseHeld(); HideWarnings();
        foreach (var spray in sprays.Values) spray.Stop(true);
        activeSpray = null; current = null; spit = null; prepared = false; preparedPayload = null;
        ActiveBeamPhase = -1; executionResult = result;
    }
    void OwnedTerminated(EnemyMotionResult result)
    {
        if (result.Handle != playbackHandle) return;
        playbackResult = result;
        if (result.State == EnemyMotionState.Failed) LastFailure = "Motion failed: " + result.Reason;
        if (result.State == EnemyMotionState.Completed) TryFinishOwnedExecution();
    }
    void TryFinishOwnedExecution()
    {
        if (!UsesMotion || !bodyRecovered || playbackResult.State != EnemyMotionState.Completed || cast != null || flights.Count != 0 || current == null) return;
        actor.Movement.ReleaseMotionLock(playbackHandle); basic.ReleaseSupportPresentation(this); current = null;
        executionResult = new EnemyMotionResult(playbackHandle, EnemyMotionState.Completed); CompletedCount++;
    }
    void ObserveOwnedPreparation()
    {
        if (!prepared) { if (held != null) PlaceHeld(Hands(), aimRotation); return; }
        if (!HasPreparation) { ReleaseHeld(); prepared = false; preparedPayload = null; return; }
        if (preparedTarget == null || !preparedTarget.gameObject.activeInHierarchy || Time.time > preparationDeadline)
        {
            actor.AnimationBridge.FailMotion(preparationHandle, preparedTarget == null || !preparedTarget.gameObject.activeInHierarchy ? EnemyMotionReason.TargetLost : EnemyMotionReason.DeadlineExceeded);
            prepared = false; preparedTarget = null; ReleaseHeld(); return;
        }
        if (actor.AnimationBridge.CurrentMotionRole == EnemyMotionRole.Carry)
        {
            Vector3 delta = transform.position - carryPreviousPosition; carryPreviousPosition = transform.position;
            if (Time.deltaTime > .00001f && delta.magnitude < .75f)
            {
                var local = Quaternion.Inverse(preparedFacing) * delta;
                string id = local.z < 0f ? "WalkBackwardsWithRock" : "WalkForwardWithRock";
                var binding = actor.AnimationBridge.PlaybackProfile.Find(id);
                float rate = delta.magnitude / Time.deltaTime / Mathf.Max(.01f, binding.strideSpeed);
                actor.AnimationBridge.TryUpdateOwnedMotionIntent(preparationHandle, new EnemyMotionIntent(new Vector2(0f, local.z < 0f ? -1f : 1f), Mathf.Clamp(rate, .01f, 8f)));
            }
            if (held != null) PlaceHeld(Hands(), preparedFacing);
            return;
        }
        if (!actor.AnimationBridge.TryReadMotion(preparationHandle, out var sample)) return;
        var entry = basic.Collection.FindMotion("UnearthRock");
        float frame = sample.Normalized * entry.runtime.length * entry.runtime.frameRate;
        PlacePreparedPayload(frame, preparedFacing);
    }
    // Dig at a fixed world ground point, then lift a full-size body into the two-hand socket.
    // SmoothStep makes the transfer continuous with both the buried start and the moving held pose.
    void PlaceExtractedElite(float frame, Quaternion facing)
    {
        if (held == null) held = Acquire(patterns.elite);
        if (!extractionAnchored) { extractionGround = Ground(Hands()); extractionAnchored = true; }
        float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(patterns.eliteRevealFrame, patterns.eliteFullSizeFrame, frame));
        Vector3 buried = extractionGround - Vector3.up * Mathf.Max(.25f, PayloadCenter(patterns.elite).y);
        PlaceHeld(Vector3.Lerp(buried, Hands(), lift),
            facing * Quaternion.Euler(12f * (1f - lift), 0f, -6f * Mathf.Sin(lift * Mathf.PI) * (1f - lift)));
    }
    void PlacePreparedPayload(float frame, Quaternion facing)
    {
        if (preparedPayload == EnemyBossThrowPayload.Elite)
        {
            if (frame >= patterns.eliteRevealFrame) PlaceExtractedElite(frame, facing);
        }
        else if (frame >= EnemyBossPayloadSocket.RockRevealFrame)
        {
            if (held == null) held = Acquire(null);
            PlaceHeld(Hands(), facing);
        }
    }
    void PlaceHeld(Vector3 position, Quaternion facing)
    {
        held.root.transform.SetPositionAndRotation(position, facing);
        held.root.SetActive(true);
    }
    EnemyBossPayloadGrip eliteGrip;
    float eliteGripReleaseUntil;
    void FitEliteHands()
    {
        if (!patterns.fitEliteHands || leftHand == null || rightHand == null || !Usable) { eliteGrip?.Restore(); return; }
        if (held?.payload == patterns.elite && held.root.activeSelf)
        {
            float weight = 1f;
            if (prepared && extractionAnchored && actor.AnimationBridge.TryReadMotion(preparationHandle, out var sample)
                && sample.MotionId == "UnearthRock")
            {
                float frame = sample.Normalized * sample.Clip.length * sample.Clip.frameRate;
                weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(patterns.eliteRevealFrame, patterns.eliteFullSizeFrame, frame));
            }
            if (eliteGrip == null) eliteGrip = new EnemyBossPayloadGrip(leftHand, rightHand);
            eliteGrip.Apply(held.root.transform, patterns.eliteLeftHandGrip, patterns.eliteRightHandGrip, weight, patterns.eliteGripPalmTilt);
        }
        else if (eliteGripReleaseUntil > Time.time && current == patterns.throwMaterial)
        {
            // Release from the animated hand socket, never chase the departing projectile.
            float weight = Mathf.SmoothStep(0f, 1f, (eliteGripReleaseUntil - Time.time) / Mathf.Max(.01f, patterns.eliteGripReleaseSeconds));
            if (eliteGrip == null) eliteGrip = new EnemyBossPayloadGrip(leftHand, rightHand);
            Vector3 center = Hands();
            eliteGrip.ApplyPoints(center + aimRotation * (patterns.eliteLeftHandGrip * patterns.elite.visualScale),
                center + aimRotation * (patterns.eliteRightHandGrip * patterns.elite.visualScale), weight, aimRotation * Vector3.forward, patterns.eliteGripPalmTilt);
        }
        else eliteGrip?.Restore();
    }
    void CancelOwnedExecution()
    {
        if (actor.AnimationBridge.OwnsMotion(playbackHandle)) actor.AnimationBridge.CancelMotion(playbackHandle, EnemyMotionReason.OwnerCancelled);
        if (HasPreparation) actor.AnimationBridge.CancelMotion(preparationHandle, EnemyMotionReason.OwnerCancelled);
        actor.Movement.ReleaseMotionLock(playbackHandle); generation++;
        if (cast != null) StopCoroutine(cast); cast = null;
        ClearFlights(); ReleaseHeld(); HideWarnings(); foreach (var spray in sprays.Values) spray.Stop(true);
        activeSpray = null; current = null; spit = null; prepared = false; preparedPayload = null; ActiveBeamPhase = -1;
        if (!executionResult.IsTerminal && playbackHandle.IsValid) executionResult = new EnemyMotionResult(playbackHandle, EnemyMotionState.Cancelled, EnemyMotionReason.OwnerCancelled);
    }
}
