using UnityEngine;

public abstract class EnemyAbilityExecutor : MonoBehaviour // 능력 실행 방식별 공용 확장 경계
{
    public abstract bool IsExecuting { get; }

    public abstract bool Supports(EnemyAbilityDefinition ability);

    public abstract bool CanStart(
        EnemyAbilityDefinition ability,
        Transform target);

    public abstract bool TryStart(
        EnemyAbilityDefinition ability,
        int abilityIndex,
        Transform target);

    public virtual bool CanStart(EnemyAbilityDefinition ability, Transform target, in EnemyAbilityStartContext context) => !context.IsPrepared && CanStart(ability, target);

    public virtual bool TryStart(EnemyAbilityDefinition ability, int abilityIndex, Transform target, in EnemyAbilityStartContext context)
        => !context.IsPrepared && TryStart(ability, abilityIndex, target);

    public abstract float ResolveCooldown(float baseCooldown);

    public abstract void Cancel();

    public abstract void ResetForReuse();
}
