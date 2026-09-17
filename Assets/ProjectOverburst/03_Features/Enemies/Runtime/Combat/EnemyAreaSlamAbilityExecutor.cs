using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMeleeAttackController))]
public sealed class EnemyAreaSlamAbilityExecutor : EnemyAbilityExecutor
{
    [SerializeField] private EnemyMeleeAttackController melee;

    public override bool IsExecuting => melee != null && melee.IsAttacking;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Configure(EnemyMeleeAttackController controller)
    {
        melee = controller;
    }

    public override bool Supports(EnemyAbilityDefinition ability)
    {
        return ability != null
            && ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam;
    }

    public override bool CanStart(
        EnemyAbilityDefinition ability,
        Transform target)
    {
        ResolveReferences();
        return Supports(ability)
            && melee != null
            && melee.CanStartAbility(ability, target);
    }

    public override bool TryStart(
        EnemyAbilityDefinition ability,
        int abilityIndex,
        Transform target)
    {
        ResolveReferences();
        return Supports(ability)
            && melee != null
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
        if (melee == null)
            return;

        melee.CancelAttack();
        melee.ClearTarget();
    }

    private void ResolveReferences()
    {
        if (melee == null)
            melee = GetComponent<EnemyMeleeAttackController>();
    }
}
