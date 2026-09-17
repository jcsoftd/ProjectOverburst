using UnityEngine;

public abstract class WeaponCombatDefinition : ScriptableObject
{
    [Header("사용 정책")]
    public WeaponUsageSettings usage;

    [Header("조준 정책")]
    public WeaponAimSettings aim;

    [Header("애니메이션")]
    public WeaponAnimationSettings animation;

    public abstract WeaponCombatFamily Family { get; }
}
