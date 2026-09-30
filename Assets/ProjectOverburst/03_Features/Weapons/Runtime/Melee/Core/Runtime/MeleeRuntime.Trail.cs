using UnityEngine;

// MeleeRuntime partial: 무기 궤적 시작·정지. 필드와 Unity 수명주기는 MeleeRuntime.cs에 있다.
public partial class MeleeRuntime
{
    private void ResolveAttackTrail(MeleeComboStepData step)
    {
        activeAttackTrail = step.trailPhases != null && step.trailPhases.Length > 0
            ? ResolveCurrentWeaponTrail()
            : null;
    }

    private void StartAttackTrail()
    {
        if (activeAttackTrail == null)
            activeAttackTrail = ResolveCurrentWeaponTrail();

        activeAttackTrail?.BeginTrail();
    }

    private void StopAttackTrail()
    {
        activeAttackTrail?.EndTrail();
    }

    private void ClearAttackTrail()
    {
        if (activeAttackTrail == null)
            return;

        activeAttackTrail.ClearTrail();
    }

    private void ResetActiveTrailState()
    {
        activeAttackTrail = null;
    }

    private IWeaponTrailController ResolveCurrentWeaponTrail()
    {
        Transform weaponRoot = playerEquipment != null ? playerEquipment.CurrentWeaponRoot : null;
        if (weaponRoot == null)
            return null;

        MonoBehaviour[] components = weaponRoot.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IWeaponTrailController trailController)
                return trailController;
        }

        return null;
    }
}
