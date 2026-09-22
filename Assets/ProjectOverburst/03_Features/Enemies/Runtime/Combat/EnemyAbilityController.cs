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

    public EnemyAbilitySet AbilitySet => abilitySet;
    public float AttackRange => ResolveMaximumAttackRange();
    public bool IsExecuting => ResolveIsExecuting();
    public int LastCommittedAbilityIndex => lastCommittedAbilityIndex;
    public EnemyAbilityDefinition LastCommittedAbility => lastCommittedAbility;

    public float ResolveEngagementRange(Transform target)
    {
        if (target == null || abilitySet == null || !abilitySet.IsValid) return AttackRange;
        Vector3 delta = target.position - transform.position;
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
        Vector3 delta = target.position - transform.position; delta.y = 0f;
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
        ResolveReferences();
        for (int i = 0; i < executors.Length; i++)
            executors[i]?.Cancel();
        if (executors.Length == 0)
            meleeExecutor?.CancelAttack();
    }

    public void ClearTarget()
    {
        meleeExecutor?.ClearTarget();
    }

    public void ResetForReuse()
    {
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
        Vector3 delta = target.position - transform.position;
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
