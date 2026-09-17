using UnityEngine;

[DisallowMultipleComponent]
public class WeaponRuntimeHub : MonoBehaviour
{
    [Header("Runtime Controllers")]
    [SerializeField] private MeleeRuntime meleeController;
    [SerializeField] private MagicRuntime magicController;

    public IWeaponRuntimeController MeleeRuntime => meleeController;
    public IWeaponRuntimeController MagicRuntime => magicController;
    public IWeaponActionPort MeleeActionPort => meleeController;

    public void ResolveControllers()
    {
        if (meleeController == null)
            meleeController = GetComponent<MeleeRuntime>();

        if (magicController == null)
            magicController = GetComponent<MagicRuntime>();
    }

    public IWeaponRuntimeController GetRuntime(WeaponRuntimeKind runtimeKind)
    {
        ResolveControllers();

        switch (runtimeKind)
        {
            case WeaponRuntimeKind.Melee:
                return MeleeRuntime;

            case WeaponRuntimeKind.Magic:
                return MagicRuntime;

            default:
                return null;
        }
    }

    public IWeaponRuntimeController GetRuntime(ResolvedWeaponContext context)
    {
        if (!context.IsValid || !context.Usage.canPrimaryAttack)
            return null;

        if (context.Family == WeaponCombatFamily.Melee
            && context.Usage.attackType == WeaponAttackType.MeleeSlash)
            return GetRuntime(WeaponRuntimeKind.Melee);

        if (context.Family == WeaponCombatFamily.Magic
            && context.Usage.attackType == WeaponAttackType.Chain)
            return GetRuntime(WeaponRuntimeKind.Magic);

        return null;
    }

    public WeaponRuntimeStatus GetRuntimeStatus(ResolvedWeaponContext context)
    {
        IWeaponRuntimeController runtime = GetRuntime(context);
        return runtime != null ? runtime.GetRuntimeStatus() : WeaponRuntimeStatus.Empty;
    }

    public IWeaponActionPort GetActionPort(ResolvedWeaponContext context)
    {
        return GetRuntime(context) as IWeaponActionPort; // 현재 무기 행동 포트
    }

    public void CancelAllActions()
    {
        ResolveControllers();
        MeleeRuntime?.CancelCurrentAction();
        MagicRuntime?.CancelCurrentAction();
    }
}
