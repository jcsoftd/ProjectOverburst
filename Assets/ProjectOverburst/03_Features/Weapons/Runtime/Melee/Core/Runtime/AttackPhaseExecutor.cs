using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct AttackPhaseHit
{
    public readonly AttackPhaseData Phase;
    public readonly MeleeAttackRuntimeData RuntimeData;
    public readonly IDamageable Damageable;
    public readonly CombatHealth TargetHealth;
    public readonly Vector3 HitPoint;
    public readonly Vector3 Direction;
    public readonly int PhaseIndex;

    public AttackPhaseHit(
        AttackPhaseData phase,
        MeleeAttackRuntimeData runtimeData,
        CombatTarget target,
        Vector3 hitPoint,
        Vector3 direction,
        int phaseIndex = 0)
    {
        Phase = phase;
        RuntimeData = runtimeData;
        Damageable = target != null ? target.Damageable : null;
        TargetHealth = target != null ? target.DamageReceiver : null;
        HitPoint = hitPoint;
        Direction = direction;
        PhaseIndex = phaseIndex;
    }
}

public sealed class AttackPhaseExecutor
{
    private readonly struct ReachedTarget
    {
        public readonly CombatTarget Target;
        public readonly float RequiredProgress;

        public ReachedTarget(CombatTarget target, float requiredProgress)
        {
            Target = target;
            RequiredProgress = requiredProgress;
        }
    }

    private sealed class PhaseState
    {
        public AttackPhaseData Phase;
        public int PhaseIndex;
        public AttackPatternRuntimeData Pattern;
        public MeleeAttackRuntimeData RuntimeData;
        public AttackPatternBasis Basis;
        public readonly AttackTargetSnapshot Targets = new AttackTargetSnapshot();
        public readonly AttackHitRegistry HitRegistry = new AttackHitRegistry();
        public readonly List<ReachedTarget> ReachedTargets = new List<ReachedTarget>(16);
        public readonly WeaponTipProgressResolver ProgressResolver = new WeaponTipProgressResolver();
        public readonly AttackVfxCuePlayer VfxPlayer = new AttackVfxCuePlayer();
        public MeleeAttackPhaseTrajectoryBakeData BakedTrajectory;
        public AttackProgressSample ProgressSample;
        public bool Started;
        public bool Completed;

        public void Reset()
        {
            Phase = default;
            Pattern = default;
            RuntimeData = default;
            Basis = default;
            Targets.Clear();
            HitRegistry.Clear();
            ReachedTargets.Clear();
            ProgressResolver.Reset();
            VfxPlayer.Reset();
            BakedTrajectory = null;
            ProgressSample = default;
            Started = false;
            Completed = false;
        }
    }

    private readonly List<PhaseState> phases = new List<PhaseState>(4);
    private readonly Stack<PhaseState> phasePool = new Stack<PhaseState>(4);
    private Transform owner;
    private CombatTarget sourceTarget;
    private Vector3 attackForward;
    private WeaponTraceBinding traceBinding;
    private WeaponElement weaponElement;
    private AttackPatternDebugRenderer debugRenderer;
    private Action<AttackPhaseHit> onHit;

    public bool IsRunning { get; private set; }
    public void OverrideUnstartedCircleRadius(float radius)
    {
        foreach (PhaseState state in phases)
        {
            if (state.Started || state.Pattern.Shape != AttackAreaShape.Circle) continue;
            AttackPatternRuntimeData p = state.Pattern;
            state.Pattern = new AttackPatternRuntimeData(p.Shape, p.FillMode, p.Direction, radius,
                p.Angle, p.Width, p.ForwardOffset, p.AngleOffset, p.VerticalTolerance, p.HitRevalidationTolerance, p.ProgressCurve);
            MeleeAttackRuntimeData d = state.RuntimeData;
            float scale = radius / Mathf.Max(.01f, p.Range);
            state.RuntimeData = new MeleeAttackRuntimeData(state.Pattern, d.Damage, d.Knockback,
                d.HitStunDuration, d.VfxScale, d.AttackRangeScale * scale);
        }
    }

    public bool Begin(
        AttackPhaseData[] phaseData,
        Transform attackOwner,
        CombatTarget attackSourceTarget,
        Vector3 fixedAttackForward,
        WeaponFinalStats weaponStats,
        MeleeWeaponBaseSettings meleeSettings,
        WeaponElement element,
        float attackDamageMultiplier,
        MeleeAttackStepTrajectoryBakeData bakedTrajectoryStep,
        WeaponTraceBinding weaponTraceBinding,
        AttackPatternDebugRenderer renderer,
        Action<AttackPhaseHit> hitCallback)
    {
        Cancel();

        if (phaseData == null
            || phaseData.Length == 0
            || attackOwner == null
            || attackSourceTarget == null
            || !attackSourceTarget.IsAlive
            || hitCallback == null)
        {
            return false;
        }

        if (!AttackPhaseValidator.TryValidate(phaseData, out string validationError))
        {
            Debug.LogError("[AttackPhaseExecutor] " + validationError);
            return false;
        }

        if (!TryValidateBakedTrajectories(phaseData, bakedTrajectoryStep, out string trajectoryError))
        {
            Debug.LogError("[AttackPhaseExecutor] " + trajectoryError);
            return false;
        }

        owner = attackOwner;
        sourceTarget = attackSourceTarget;
        attackForward = FlattenDirection(fixedAttackForward);
        traceBinding = weaponTraceBinding;
        weaponElement = element;
        debugRenderer = renderer;
        onHit = hitCallback;

        for (int i = 0; i < phaseData.Length; i++)
        {
            AttackPhaseData phase = phaseData[i];
            PhaseState state = AcquireState();
            state.Phase = phase;
            state.PhaseIndex = i;
            state.RuntimeData = MeleeAttackStatResolver.Resolve(
                weaponStats,
                meleeSettings,
                phase,
                attackDamageMultiplier);
            state.Pattern = state.RuntimeData.Pattern;
            if (phase.progressSource != AttackProgressSource.NormalizedTime)
            {
                bakedTrajectoryStep.TryGetPhase(
                    i,
                    phase.progressSource,
                    out state.BakedTrajectory);
            }
            phases.Add(state);
        }

        phases.Sort(ComparePhaseStart);
        IsRunning = true;
        return true;
    }

    public void Tick(float attackNormalizedTime)
    {
        if (!IsRunning)
        {
            debugRenderer?.Hide();
            return;
        }

        float normalizedTime = Mathf.Max(0f, attackNormalizedTime);
        PhaseState visiblePhase = null;
        float visibleProgress = 0f;

        for (int i = 0; i < phases.Count; i++)
        {
            PhaseState state = phases[i];
            if (state.Completed)
                continue;

            if (normalizedTime < state.Phase.SafeStart)
                continue;

            if (!state.Started)
                StartPhase(state);

            if (state.Phase.basisFollowMode == AttackBasisFollowMode.FollowOwnerPosition && owner != null)
                state.Basis = state.Basis.WithOrigin(owner.position);

            float linearProgress = Mathf.InverseLerp(
                state.Phase.SafeStart,
                state.Phase.SafeEnd,
                normalizedTime);
            bool canApplyHits;
            float resolvedProgress;
            if (state.Phase.progressSource == AttackProgressSource.NormalizedTime)
            {
                resolvedProgress = state.Pattern.EvaluateProgress(linearProgress);
                state.ProgressSample = AttackProgressSample.FromTime(resolvedProgress);
                canApplyHits = true;
            }
            else
            {
                canApplyHits = state.ProgressResolver.TryEvaluate(
                    state.Phase.progressSource,
                    normalizedTime,
                    state.BakedTrajectory,
                    traceBinding,
                    out AttackProgressSample sample);
                state.ProgressSample = sample;
                resolvedProgress = sample.ResolvedProgress;
            }

            if (canApplyHits)
                ApplyReachedTargets(state, resolvedProgress);

            if (canApplyHits)
            {
                AttackProgressSample vfxSample = ResolveVfxTraceSample(
                    state.ProgressSample,
                    resolvedProgress);
                state.VfxPlayer.Tick(
                    state.Pattern,
                    state.Basis,
                    resolvedProgress,
                    vfxSample,
                    weaponElement,
                    state.RuntimeData.VfxScale,
                    state.RuntimeData.AttackRangeScale);
            }

            if (linearProgress >= 1f)
            {
                state.Completed = true;
                continue;
            }

            visiblePhase = state;
            visibleProgress = resolvedProgress;
        }

        if (AllPhasesCompleted())
        {
            debugRenderer?.Hide();
            IsRunning = false;
            return;
        }

        if (visiblePhase != null)
            debugRenderer?.Render(
                visiblePhase.Pattern,
                visiblePhase.Basis,
                visibleProgress,
                visiblePhase.ProgressSample);
        else
            debugRenderer?.Hide();
    }

    public void Cancel()
    {
        for (int i = 0; i < phases.Count; i++)
            ReleaseState(phases[i]);

        phases.Clear();
        owner = null;
        sourceTarget = null;
        attackForward = Vector3.forward;
        traceBinding = null;
        weaponElement = WeaponElement.None;
        debugRenderer?.Hide();
        debugRenderer = null;
        onHit = null;
        IsRunning = false;
    }

    private void StartPhase(PhaseState state)
    {
        state.Started = true;
        state.HitRegistry.Clear();
        state.Basis = new AttackPatternBasis(owner.position, attackForward);
        state.Targets.Capture(state.Pattern, state.Basis, sourceTarget);
        state.VfxPlayer.Begin(state.Phase);
    }

    private AttackProgressSample ResolveVfxTraceSample(
        AttackProgressSample progressSample,
        float resolvedProgress)
    {
        if (progressSample.HasTrace)
            return progressSample;

        if (traceBinding == null || !traceBinding.IsValid)
            return progressSample;

        return new AttackProgressSample(
            true,
            traceBinding.WeaponTip.position,
            resolvedProgress,
            resolvedProgress);
    }

    private void ApplyReachedTargets(PhaseState state, float resolvedProgress)
    {
        state.ReachedTargets.Clear();
        IReadOnlyList<AttackTargetSnapshot.Entry> targets = state.Targets.Entries;

        for (int i = 0; i < targets.Count; i++)
        {
            AttackTargetSnapshot.Entry entry = targets[i];
            CombatTarget target = entry.Target;
            if (state.HitRegistry.Contains(entry.TargetId)
                || target == null
                || !CombatTargetFilter.CanDamage(sourceTarget, target))
            {
                continue;
            }

            if (!AttackPatternEvaluator.TryEvaluate(
                    state.Pattern,
                    state.Basis,
                    target.CurrentHurtVolume,
                    state.Pattern.HitRevalidationTolerance,
                    out float currentRequiredProgress))
            {
                continue;
            }

            if (currentRequiredProgress <= resolvedProgress + 0.0001f)
                state.ReachedTargets.Add(new ReachedTarget(target, currentRequiredProgress));
        }

        state.ReachedTargets.Sort(CompareReachedTargets);
        for (int i = 0; i < state.ReachedTargets.Count; i++)
        {
            ReachedTarget reached = state.ReachedTargets[i];
            CombatTarget target = reached.Target;
            if (target == null || !state.HitRegistry.TryRegister(target.TargetId))
                continue;

            CombatTargetVolume hurtVolume = target.CurrentHurtVolume;
            Vector3 hitPoint = hurtVolume.Center;
            Vector3 direction;
            if (state.Pattern.IsThrust)
            {
                direction = state.Basis.Forward;
            }
            else
            {
                direction = hitPoint - state.Basis.GetPatternOrigin(state.Pattern);
                direction.y = 0f;
                if (direction.sqrMagnitude <= 0.0001f)
                    direction = state.Basis.Forward;
                else
                    direction.Normalize();
            }

            // Place contact feedback on the same authored volume used for the hit.
            hitPoint -= direction * hurtVolume.Radius;
            onHit?.Invoke(new AttackPhaseHit(
                state.Phase,
                state.RuntimeData,
                target,
                hitPoint,
                direction,
                state.PhaseIndex));
        }
    }

    private PhaseState AcquireState()
    {
        PhaseState state = phasePool.Count > 0 ? phasePool.Pop() : new PhaseState();
        state.Reset();
        return state;
    }

    private void ReleaseState(PhaseState state)
    {
        if (state == null)
            return;

        state.Reset();
        phasePool.Push(state);
    }

    private bool AllPhasesCompleted()
    {
        for (int i = 0; i < phases.Count; i++)
        {
            if (!phases[i].Completed)
                return false;
        }

        return true;
    }

    private static int ComparePhaseStart(PhaseState left, PhaseState right)
    {
        return left.Phase.SafeStart.CompareTo(right.Phase.SafeStart);
    }

    private static int CompareReachedTargets(ReachedTarget left, ReachedTarget right)
    {
        int progressComparison = left.RequiredProgress.CompareTo(right.RequiredProgress);
        if (progressComparison != 0)
            return progressComparison;

        int leftId = left.Target != null ? left.Target.TargetId : int.MaxValue;
        int rightId = right.Target != null ? right.Target.TargetId : int.MaxValue;
        return leftId.CompareTo(rightId);
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.forward;
    }

    private static bool TryValidateBakedTrajectories(
        AttackPhaseData[] phaseData,
        MeleeAttackStepTrajectoryBakeData bakedTrajectoryStep,
        out string error)
    {
        for (int i = 0; i < phaseData.Length; i++)
        {
            AttackProgressSource source = phaseData[i].progressSource;
            if (source == AttackProgressSource.NormalizedTime)
                continue;

            if (bakedTrajectoryStep == null
                || !bakedTrajectoryStep.TryGetPhase(i, source, out _))
            {
                error = "WeaponTip 공격은 현재 Phase와 일치하는 정식 공격 궤적 베이크가 필요합니다. Phase=" + i;
                return false;
            }
        }

        error = null;
        return true;
    }
}
