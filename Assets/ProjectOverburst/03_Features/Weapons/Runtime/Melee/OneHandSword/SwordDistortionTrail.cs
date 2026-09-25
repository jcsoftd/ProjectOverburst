using UnityEngine;

// 기존 공격의 Trail 구간을 사용하므로 취소·무기 교체 시에도 잔상이 정리된다.
[DisallowMultipleComponent]
public sealed class SwordDistortionTrail : MonoBehaviour, IWeaponTrailController
{
    [SerializeField] private OneHandSwordDistortionStyle style;
    [SerializeField] private TrailRenderer bladeTrail;
    private bool swinging;

    public void BeginTrail()
    {
        ClearTrail();
        swinging = true;
        if (bladeTrail != null)
            bladeTrail.emitting = style != null && style.version == SwordDistortionVersion.BladeTrail;
        var equipment = GetComponentInParent<PlayerEquipment>();
        if (equipment != null && equipment.CurrentWeaponItem != null)
            MeleeElementSfxService.TryPlaySlash(equipment.CurrentWeaponItem.ResolvedElement, transform.position);
    }

    public void EndTrail()
    {
        swinging = false;
        if (bladeTrail != null) bladeTrail.emitting = false;
    }

    public void ClearTrail()
    {
        EndTrail();
        if (bladeTrail != null) bladeTrail.Clear();
    }

    private void LateUpdate()
    {
        if (swinging && (style == null || style.version != SwordDistortionVersion.BladeTrail))
            ClearTrail();
    }

    private void OnDisable() => ClearTrail();
}
