using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMeleeAttackController))]
public sealed class EnemyAbilityController : MonoBehaviour // 선택·쿨다운·실행기 라우팅 공용 표면
{
    [SerializeField] private EnemyMeleeAttackController meleeExecutor;
    [SerializeField] private EnemyAbilitySet abilitySet;
    [SerializeField] private EnemyAbilityExecutor[] executors;
    [SerializeField] private bool avoidSameAbilityTwice = true;

    private readonly Dictionary<EnemyAbilityDefinition, float> readyTimeByAbility =
        new Dictionary<EnemyAbilityDefinition, float>();
    private readonly List<AbilityCandidate> candidates = new List<AbilityCandidate>(8);
    private CombatHealth health;
    private int lastCommittedAbilityIndex = -1;
    private EnemyAbilityDefinition lastCommittedAbility;
    private float lastCommittedAt;
    private float firstImpactAt, lastImpactAt;
    private Transform strongTarget;
    private Vector3 strongAim;
    private EnemyStrongAttackWarning strongWarning;
    private EnemyActor actor;
    private EnemyThemeSpecialExecutor themeExecutor;
    private bool finalImpactDelivered;
    private int nextImpactIndex;
    private bool warningAimLocked;
    public void NotifyAbilityImpact(EnemyAbilityDefinition ability, int index)
    {
        if (ability != lastCommittedAbility) return;
        nextImpactIndex = Mathf.Min(index + 1, ability.HitCount - 1);
        if (index >= ability.HitCount - 1) finalImpactDelivered = true;
    }
    public bool IsOrdinaryHitProtected
    {
        get
        {
            return IsExecuting && reaction != null
                && (reaction.CanActThroughOrdinaryHit || reaction.IsStrongAttackActive);
        }
    }
    // 2026-09-30: 정예는 어떤 타격에도 공격이 끊기지 않는다. 중형 강공은 플레이어 강공 본타(PlayerAttackKind.Heavy)에만 끊긴다.
    // 강공에 딸린 원소 폭발·번개 연쇄 같은 원소 피해(Elemental)로는 끊기지 않는다.
    public bool IsProtectedFrom(in DamageInfo info)
    {
        if (!IsOrdinaryHitProtected) return false;
        return reaction.CanActThroughOrdinaryHit || (info.playerAttackKind & PlayerAttackKind.Heavy) == 0;
    }
    // 중형이 평타 경직 중에 강공만 골라 시작할 때 AI가 감싸 쓰는 구간.
    private bool strongOnlyPass;
    public void BeginStrongOnlyPass()
    {
        ResolveReferences();
        strongOnlyPass = true;
        reaction?.SetStrongStartPass(true);
    }
    public void EndStrongOnlyPass()
    {
        strongOnlyPass = false;
        reaction?.SetStrongStartPass(false);
    }
    // 2026-09-30: 핵앤슬래시 기준으로 인정 구간을 첫 타격 0.70초 전부터 연다(신호와 동일).
    public const float ParryLeadSeconds = .70f;
    public bool IsParryThreatTo(CombatTarget player)
    {
        EnemyAbilityDefinition ability = lastCommittedAbility;
        if (player == null || ability == null || !ability.IsParryable
            || !IsExecuting || finalImpactDelivered
            || Time.time < firstImpactAt - ParryLeadSeconds || Time.time > lastImpactAt + .03f)
            return false;
        if (actor == null) actor = GetComponent<EnemyActor>();
        return EnemyAttackThreatGeometry.WouldHit(actor, ability, player);
    }
    private EnemyMovement movement;
    private EnemyMovementReaction reaction;
    private EnemyAnimationBridge animationBridge;
    private Transform preparedTarget;
    private Vector3 preparedPosition;

    public bool UsesCommittedAim => movement != null && movement.Profile != null && movement.Profile.HasTurnAnimation;
    public bool HasPreparedAim(Transform target) => UsesCommittedAim && target != null && preparedTarget == target;
    public Vector3 ResolveAimPosition(Transform target) => strongTarget != null && strongTarget == target
        ? strongAim : HasPreparedAim(target) ? preparedPosition : target != null ? target.position : transform.position;
    private void Update()
    {
        if (strongTarget == null) return;
        if (!IsExecuting) { EndStrongWarning(); return; }
        if (finalImpactDelivered) { strongWarning?.Hide(); return; }
        var currentAbility = lastCommittedAbility;
        if (currentAbility != null && currentAbility.UsesPacedTimeline && animationBridge != null
            && animationBridge.TryGetAttackNormalizedTime(currentAbility.AnimatorTrigger, out float progress))
        {
            float speed = meleeExecutor != null ? meleeExecutor.AbilityAnimationSpeed : 1f;
            float elapsed = currentAbility.ResolvePacedTime(progress, speed);
            firstImpactAt = Time.time + Mathf.Max(0f,
                currentAbility.ResolvePacedTime(currentAbility.GetHitNormalizedTime(nextImpactIndex), speed) - elapsed);
            lastImpactAt = Time.time + Mathf.Max(0f, currentAbility.ResolveLastImpactTime(speed) - elapsed);
        }
        if (Time.time >= firstImpactAt - .30f) warningAimLocked = true;
        if (!warningAimLocked)
        {
            strongAim = strongTarget.position;
            Vector3 direction = strongAim - transform.position; direction.y = 0f;
            if (direction.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360f * Time.deltaTime);
        }
        if (strongWarning != null)
        {
            EnemyAbilityDefinition ability = lastCommittedAbility;
            Vector3 center = ability != null && (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge
                    || ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
                ? transform.position : ability != null && ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam
                    ? transform.position : meleeExecutor != null && meleeExecutor.AttackPoint != null
                        ? meleeExecutor.AttackPoint.position : transform.position;
            strongWarning.SetCenter(center);
            if (ability != null && (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge
                || ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile))
            {
                strongWarning.SetFacing(ability.ExecutionMode == EnemyAbilityExecutionMode.Charge && themeExecutor != null
                    ? themeExecutor.ChargeDirection : strongAim - transform.position);
            }
            float remaining = firstImpactAt - Time.time;
            CombatTarget player = EnemyStrongAttackWarning.PlayerTarget;
            if (actor == null) actor = GetComponent<EnemyActor>();
            bool threatens = ability != null && ability.IsParryable && player != null && player.IsAlive
                && remaining <= ParryLeadSeconds + .05f
                && EnemyAttackThreatGeometry.IsLikelyThreatTo(actor, ability, center, player.transform.position);
            strongWarning.SetRemaining(remaining, threatens);
        }
    }
    private void EndStrongWarning()
    {
        reaction?.SetStrongAttackActive(false);
        strongTarget = null;
        strongWarning?.Hide();
        EnemyCombatCoordinator.ReleaseStrongAttack(this);
    }
    public Vector3 PrepareAttackAim(Transform target)
    {
        ResolveReferences();
        if (UsesCommittedAim && target != null && !HasPreparedAim(target)
            && !IsExecuting && !movement.IsActionLocked && !movement.IsStatusMovementLocked
            && (animationBridge == null || !animationBridge.BlocksAttackStart)
            && (reaction == null || !reaction.BlocksAttack))
        {
            preparedTarget = target;
            preparedPosition = target.position;
        }
        return ResolveAimPosition(target);
    }
    public void ClearPreparedAim() { preparedTarget = null; }
    private void OnEnable()
    {
        ResolveReferences(); ClearPreparedAim();
        if (health != null)
        {
            if (GetComponent<EnemyHitResponseCoordinator>() == null)
                health.OnDamaged += ClearAimOnDamage;
            health.OnDead += ClearAimOnDamage;
        }
        if (reaction != null) reaction.ReactionStarted += ClearPreparedAim;
    }
    private void OnDisable()
    {
        EndStrongWarning();
        if (health != null) { health.OnDamaged -= ClearAimOnDamage; health.OnDead -= ClearAimOnDamage; }
        if (reaction != null) reaction.ReactionStarted -= ClearPreparedAim;
        ClearPreparedAim();
    }
    private void ClearAimOnDamage(CombatHealth source, DamageInfo info)
    {
        if (source.IsDead || !info.isDamageOverTime && info.triggersOnHitEffects) ClearPreparedAim();
    }

    public EnemyAbilitySet AbilitySet => abilitySet;
    public float AttackRange => ResolveMaximumAttackRange();
    public bool IsExecuting => ResolveIsExecuting();
    public int LastCommittedAbilityIndex => lastCommittedAbilityIndex;
    public EnemyAbilityDefinition LastCommittedAbility => lastCommittedAbility;

    public float ResolveEngagementRange(Transform target)
    {
        if (target == null || abilitySet == null || !abilitySet.IsValid) return AttackRange;
        Vector3 delta = ResolveAimPosition(target) - transform.position;
        delta.y = 0f;
        float distance = delta.magnitude;
        float hp = health != null ? health.NormalizedHp : 1f;
        float readyRange = 0f, fallbackRange = float.PositiveInfinity;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            var ability = abilitySet.GetAbility(i);
            if (ability == null || !ability.IsValid
                || hp < ability.MinimumSelfHealthNormalized || hp > ability.MaximumSelfHealthNormalized)
                continue;
            float startRange = EnemyAttackThreatGeometry.ResolveStartRange(actor, ability);
            fallbackRange = Mathf.Min(fallbackRange, startRange);
            // A long-range cooldown or a projectile's minimum-range dead zone
            // must not stop an actor outside its available close attack range.
            if (distance >= ability.MinimumRange && IsCooldownReady(ability))
                readyRange = Mathf.Max(readyRange, startRange);
        }
        return readyRange > 0f ? readyRange : float.IsPositiveInfinity(fallbackRange) ? AttackRange : fallbackRange;
    }

    // Read-only geometry selection: cooldown, facing, prepared aim and reservations are untouched.
    public bool TryGetRangedPositioningAbility(out EnemyAbilityDefinition selected)
    {
        selected = null;
        if (abilitySet == null) return false;
        float hp = health != null ? health.NormalizedHp : 1f;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            var ability = abilitySet.GetAbility(i);
            if (ability == null || !ability.IsValid || ability.ExecutionMode != EnemyAbilityExecutionMode.Projectile
                || hp < ability.MinimumSelfHealthNormalized || hp > ability.MaximumSelfHealthNormalized
                || !(FindExecutor(ability) is EnemyThemeSpecialExecutor)) continue;
            if (selected == null || ability.Range > selected.Range) selected = ability;
        }
        return selected != null;
    }
    public bool HasRangedPositioningLine(EnemyAbilityDefinition ability, Transform target, Vector3 originPosition)
    {
        return FindExecutor(ability) is EnemyThemeSpecialExecutor executor
            && executor.HasPositioningLine(target, originPosition, target != null ? target.position : originPosition);
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public void Configure(
        EnemyAbilitySet configuredAbilitySet,
        float damageMultiplier,
        float attackSpeedMultiplier)
    {
        ResolveReferences();
        abilitySet = configuredAbilitySet;
        ClearPreparedAim();
        readyTimeByAbility.Clear();
        lastCommittedAbilityIndex = -1;
        lastCommittedAbility = null;
        if (meleeExecutor == null)
            return;

        meleeExecutor.Configure(configuredAbilitySet, damageMultiplier);
        meleeExecutor.SetRuntimeAttackSpeedMultiplier(attackSpeedMultiplier);
    }

    public bool TryStart(Transform target)
    {
        ResolveReferences();
        if (target == null || ResolveIsExecuting())
            return false;

        PrepareAttackAim(target);

        if (abilitySet == null || !abilitySet.IsValid || executors.Length == 0)
            return meleeExecutor != null && meleeExecutor.TryStartAttack(target);

        if (!TrySelectAbility(target, out AbilityCandidate selected))
            return false;
        float speed = meleeExecutor != null ? meleeExecutor.AbilityAnimationSpeed : 1f;
        float first = selected.Ability.ResolveFirstImpactTime(speed);
        float duration = selected.Ability.ResolveExecutionDuration(speed);
        if (selected.Ability.IsTelegraphedStrongAttack
            && !EnemyCombatCoordinator.TryReserveStrongAttack(this, target.position, Time.time + first, Time.time + duration)) return false;
        if (!selected.Executor.TryStart(selected.Ability, selected.Index, target))
        {
            EnemyCombatCoordinator.ReleaseStrongAttack(this);
            return false;
        }

        readyTimeByAbility[selected.Ability] =
            Time.time + Mathf.Max(selected.Ability.Cooldown, duration);
        lastCommittedAbilityIndex = selected.Index;
        lastCommittedAbility = selected.Ability;
        lastCommittedAt = Time.time;
        if (selected.Ability.IsTelegraphedStrongAttack) reaction?.SetStrongAttackActive(true);
        firstImpactAt = Time.time + first;
        lastImpactAt = Time.time + selected.Ability.ResolveLastImpactTime(speed);
        finalImpactDelivered = false;
        nextImpactIndex = 0;
        warningAimLocked = false;
        if (selected.Ability.IsTelegraphedAttack)
        {
            strongTarget = target; strongAim = target.position;
            if (strongWarning == null) strongWarning = gameObject.AddComponent<EnemyStrongAttackWarning>();
            if (actor == null) actor = GetComponent<EnemyActor>();
            bool projectile = selected.Ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile;
            strongWarning.Show(projectile ? selected.Ability.Range + 2f
                    : EnemyAttackThreatGeometry.ResolveRadius(actor, selected.Ability),
                selected.Ability.IsParryable,
                EnemyAttackThreatGeometry.ResolveHitAngle(actor, selected.Ability),
                selected.Ability.ExecutionMode == EnemyAbilityExecutionMode.Charge || projectile,
                selected.Ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc
                    || selected.Ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam
                    || selected.Ability.ExecutionMode == EnemyAbilityExecutionMode.Charge || projectile,
                first, projectile ? .14f : .4f);
        }
        return true;
    }

    // Readiness is checked before reserving an attack turn. A facing/animation
    // refusal must not incur recovery or the coordinator's re-entry cooldown.
    public bool HasAvailableAbility(Transform target)
    {
        ResolveReferences();
        if (target == null || ResolveIsExecuting()) return false;
        if (abilitySet == null || !abilitySet.IsValid || executors.Length == 0)
            return true; // Preserve the legacy melee-only start path.
        Vector3 delta = ResolveAimPosition(target) - transform.position; delta.y = 0f;
        float distance = delta.magnitude;
        float hp = health != null ? health.NormalizedHp : 1f;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            var ability = abilitySet.GetAbility(i);
            if (ability == null || !ability.IsValid || !EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability, distance, hp) || !IsCooldownReady(ability)
                || strongOnlyPass && !ability.IsTelegraphedStrongAttack)
                continue;
            var executor = FindExecutor(ability);
            if (executor != null && executor.CanStart(ability, target)) return true;
        }
        // If the completed aim cannot be used (wall, cooldown, range), release it
        // before the next decision. Do not invalidate a turn still in progress.
        if (HasPreparedAim(target) && movement.IsFacingForAttack(preparedPosition)) ClearPreparedAim();
        return false;
    }

    public bool IsCooldownReady(EnemyAbilityDefinition ability)
    {
        return ability != null
            && (!readyTimeByAbility.TryGetValue(ability, out float readyTime)
                || Time.time >= readyTime);
    }

    public float GetRemainingCooldown(EnemyAbilityDefinition ability)
    {
        if (ability == null
            || !readyTimeByAbility.TryGetValue(ability, out float readyTime))
        {
            return 0f;
        }

        return Mathf.Max(0f, readyTime - Time.time);
    }

    public void Cancel()
    {
        EndStrongWarning();
        ClearPreparedAim();
        ResolveReferences();
        for (int i = 0; i < executors.Length; i++)
            executors[i]?.Cancel();
        if (executors.Length == 0)
            meleeExecutor?.CancelAttack();
    }

    public void ClearTarget()
    {
        ClearPreparedAim();
        meleeExecutor?.ClearTarget();
    }

    public void ResetForReuse()
    {
        EndStrongWarning();
        ClearPreparedAim();
        ResolveReferences();
        for (int i = 0; i < executors.Length; i++)
            executors[i]?.ResetForReuse();
        if (executors.Length == 0 && meleeExecutor != null)
        {
            meleeExecutor.CancelAttack();
            meleeExecutor.ClearTarget();
            meleeExecutor.SetStatusActionSpeedMultiplier(1f);
            meleeExecutor.SetRuntimeAttackSpeedMultiplier(1f);
        }

        readyTimeByAbility.Clear();
        candidates.Clear();
        lastCommittedAbilityIndex = -1;
        lastCommittedAbility = null;
    }

    private bool TrySelectAbility(
        Transform target,
        out AbilityCandidate selected)
    {
        selected = default;
        candidates.Clear();
        Vector3 delta = ResolveAimPosition(target) - transform.position;
        delta.y = 0f;
        float distance = delta.magnitude;
        float selfHealth = health != null ? health.NormalizedHp : 1f;
        int highestPriority = int.MinValue;

        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition ability = abilitySet.GetAbility(i);
            if (ability == null
                || !ability.IsValid
                || !EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability, distance, selfHealth)
                || !IsCooldownReady(ability)
                || strongOnlyPass && !ability.IsTelegraphedStrongAttack)
            {
                continue;
            }

            EnemyAbilityExecutor executor = FindExecutor(ability);
            if (executor == null || !executor.CanStart(ability, target))
                continue;

            if (ability.Priority > highestPriority)
            {
                highestPriority = ability.Priority;
                candidates.Clear();
            }
            if (ability.Priority == highestPriority)
                candidates.Add(new AbilityCandidate(ability, executor, i));
        }

        if (candidates.Count == 0)
            return false;

        bool excludeLast = avoidSameAbilityTwice
            && candidates.Count > 1
            && lastCommittedAbility != null;
        float totalWeight = CalculateWeight(excludeLast);
        if (totalWeight <= 0f && excludeLast)
        {
            excludeLast = false;
            totalWeight = CalculateWeight(false);
        }
        if (totalWeight <= 0f)
            return false;

        float cursor = Random.value * totalWeight;
        for (int i = 0; i < candidates.Count; i++)
        {
            AbilityCandidate candidate = candidates[i];
            if (excludeLast && candidate.Ability == lastCommittedAbility)
                continue;

            cursor -= candidate.Ability.Weight;
            if (cursor > 0f)
                continue;
            selected = candidate;
            return true;
        }

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            AbilityCandidate candidate = candidates[i];
            if (!excludeLast || candidate.Ability != lastCommittedAbility)
            {
                selected = candidate;
                return true;
            }
        }

        return false;
    }

    private float CalculateWeight(bool excludeLast)
    {
        float total = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            EnemyAbilityDefinition ability = candidates[i].Ability;
            if (!excludeLast || ability != lastCommittedAbility)
                total += ability.Weight;
        }
        return total;
    }

    private EnemyAbilityExecutor FindExecutor(EnemyAbilityDefinition ability)
    {
        for (int i = 0; i < executors.Length; i++)
        {
            EnemyAbilityExecutor executor = executors[i];
            if (executor != null && executor.Supports(ability))
                return executor;
        }
        return null;
    }

    private bool ResolveIsExecuting()
    {
        if (executors != null)
        {
            for (int i = 0; i < executors.Length; i++)
            {
                if (executors[i] != null && executors[i].IsExecuting)
                    return true;
            }
        }

        return (executors == null || executors.Length == 0)
            && meleeExecutor != null
            && meleeExecutor.IsAttacking;
    }

    private float ResolveMaximumAttackRange()
    {
        if (abilitySet == null || !abilitySet.IsValid)
            return meleeExecutor != null ? meleeExecutor.AttackRange : 0f;

        float maximum = 0f;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition ability = abilitySet.GetAbility(i);
            if (ability != null && ability.IsValid)
                maximum = Mathf.Max(maximum, EnemyAttackThreatGeometry.ResolveStartRange(actor, ability));
        }
        return maximum;
    }

    private void ResolveReferences()
    {
        if (actor == null) actor = GetComponent<EnemyActor>();
        if (themeExecutor == null) themeExecutor = GetComponent<EnemyThemeSpecialExecutor>();
        if (movement == null) movement = GetComponent<EnemyMovement>();
        if (reaction == null) reaction = GetComponent<EnemyMovementReaction>();
        if (animationBridge == null) animationBridge = GetComponent<EnemyAnimationBridge>();
        if (meleeExecutor == null)
            meleeExecutor = GetComponent<EnemyMeleeAttackController>();
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (executors == null || executors.Length == 0)
            executors = GetComponents<EnemyAbilityExecutor>();
    }

    private readonly struct AbilityCandidate
    {
        public AbilityCandidate(
            EnemyAbilityDefinition ability,
            EnemyAbilityExecutor executor,
            int index)
        {
            Ability = ability;
            Executor = executor;
            Index = index;
        }

        public EnemyAbilityDefinition Ability { get; }
        public EnemyAbilityExecutor Executor { get; }
        public int Index { get; }
    }
}
