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
    private EnemyMovement movement;
    private EnemyMovementReaction reaction;
    private EnemyAnimationBridge animationBridge;
    private Transform preparedTarget;
    private Vector3 preparedPosition;

    public bool UsesCommittedAim => movement != null && movement.Profile != null && movement.Profile.HasTurnAnimation;
    public bool HasPreparedAim(Transform target) => UsesCommittedAim && target != null && preparedTarget == target;
    public Vector3 ResolveAimPosition(Transform target) => HasPreparedAim(target) ? preparedPosition : target != null ? target.position : transform.position;
    public Vector3 PrepareAttackAim(Transform target)
    {
        ResolveReferences();
        if (UsesCommittedAim && target != null && !HasPreparedAim(target)
            && !IsExecuting && !movement.IsActionLocked && !movement.IsStatusMovementLocked
            && (animationBridge == null || !animationBridge.IsBlockingActionActive)
            && (reaction == null || !reaction.IsStunned))
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
        if (health != null) { health.OnDamaged += ClearAimOnDamage; health.OnDead += ClearAimOnDamage; }
        if (reaction != null) reaction.ReactionStarted += ClearPreparedAim;
    }
    private void OnDisable()
    {
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
            fallbackRange = Mathf.Min(fallbackRange, ability.Range);
            // A long-range cooldown or a projectile's minimum-range dead zone
            // must not stop an actor outside its available close attack range.
            if (distance >= ability.MinimumRange && IsCooldownReady(ability))
                readyRange = Mathf.Max(readyRange, ability.Range);
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
        if (!selected.Executor.TryStart(selected.Ability, selected.Index, target))
            return false;

        readyTimeByAbility[selected.Ability] =
            Time.time + selected.Executor.ResolveCooldown(selected.Ability.Cooldown);
        lastCommittedAbilityIndex = selected.Index;
        lastCommittedAbility = selected.Ability;
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
            if (ability == null || !ability.IsValid || !ability.MatchesUseConditions(distance, hp) || !IsCooldownReady(ability))
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
                || !ability.MatchesUseConditions(distance, selfHealth)
                || !IsCooldownReady(ability))
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
                maximum = Mathf.Max(maximum, ability.Range);
        }
        return maximum;
    }

    private void ResolveReferences()
    {
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
