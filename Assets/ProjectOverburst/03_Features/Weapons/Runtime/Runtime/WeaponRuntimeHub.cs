using UnityEngine;

[DisallowMultipleComponent]
public class WeaponRuntimeHub : MonoBehaviour
{
    [Header("Runtime Controllers")]
    [SerializeField] private MeleeRuntime meleeController;

    public IWeaponRuntimeController MeleeRuntime => meleeController;
    public IWeaponActionPort MeleeActionPort => meleeController;

    public void ResolveControllers()
    {
        if (meleeController == null)
            meleeController = GetComponent<MeleeRuntime>();
    }

    public IWeaponRuntimeController GetRuntime(WeaponRuntimeKind runtimeKind)
    {
        ResolveControllers();

        switch (runtimeKind)
        {
            case WeaponRuntimeKind.Melee:
                return MeleeRuntime;

            default:
                return null; // WeaponRuntimeKind.Magic은 2026-09-30 마법 무기 제거로 대응 런타임이 없다.
        }
    }

    public IWeaponRuntimeController GetRuntime(ResolvedWeaponContext context)
    {
        if (!context.IsValid || !context.Usage.canPrimaryAttack)
            return null;

        if (context.Family == WeaponCombatFamily.Melee
            && context.Usage.attackType == WeaponAttackType.MeleeSlash)
            return GetRuntime(WeaponRuntimeKind.Melee);

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
    }
}
