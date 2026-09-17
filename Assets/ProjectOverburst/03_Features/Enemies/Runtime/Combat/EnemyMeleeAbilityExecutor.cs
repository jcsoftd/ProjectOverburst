using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMeleeAttackController))]
public sealed class EnemyMeleeAbilityExecutor : EnemyAbilityExecutor
{
    [SerializeField] private EnemyMeleeAttackController melee;

    public override bool IsExecuting => melee != null && melee.IsAttacking;

    private void Awake()
    {
        ResolveReferences();
    }

    public override bool Supports(EnemyAbilityDefinition ability)
    {
        return ability != null
            && (ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc
                || ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget);
    }

    public override bool CanStart(
        EnemyAbilityDefinition ability,
        Transform target)
    {
        ResolveReferences();
        return melee != null && melee.CanStartAbility(ability, target);
    }

    public override bool TryStart(
        EnemyAbilityDefinition ability,
        int abilityIndex,
        Transform target)
    {
        ResolveReferences();
        return melee != null
            && melee.TryStartAbility(target, ability, abilityIndex);
    }

    public override float ResolveCooldown(float baseCooldown)
    {
        ResolveReferences();
        return melee != null
            ? melee.ResolveAbilityCooldown(baseCooldown)
            : Mathf.Max(0f, baseCooldown);
    }

    public override void Cancel()
    {
        melee?.CancelAttack();
    }

    public override void ResetForReuse()
    {
        ResolveReferences();
        if (melee == null)
            return;

        melee.CancelAttack();
        melee.ClearTarget();
        melee.SetStatusActionSpeedMultiplier(1f);
        melee.SetRuntimeAttackSpeedMultiplier(1f);
    }

    public void Configure(EnemyMeleeAttackController controller)
    {
        melee = controller;
    }

    private void ResolveReferences()
    {
        if (melee == null)
            melee = GetComponent<EnemyMeleeAttackController>();
    }
}
