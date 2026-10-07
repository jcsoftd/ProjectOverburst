using UnityEngine;

public sealed partial class ElementDischargeBatch
{
    // A lightning hop reads as a real hit on the enemy it reaches: the electric weapon hit spark,
    // that enemy's blood along the hop and a flinch. Damage, stacks and energy stay derived.
    private void PlayLightningHopFeedback(int index, Vector3 from)
    {
        CombatHealth health = nodes[index].Health;
        if (health == null) return;
        Vector3 point = nodes[index].Point;
        Vector3 direction = Vector3.ProjectOnPlane(point - from, Vector3.up);
        direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
        Vector3 visualPoint = point;
        Vector3 bloodPoint = point - direction * Mathf.Min(nodes[index].Radius, .35f);
        float hitSize = 1f;
        if (health.TryGetComponent(out CombatTarget target)
            && target.TryGetComponent(out CombatTargetVfxPlacement placement) && placement.HasBodyContacts)
            bloodPoint = visualPoint = CombatTargetVfxPlacement.ResolveContact(target, point, direction, out hitSize);
        MeleeElementHitVfxService.TryPlay(WeaponElement.Electric, visualPoint, hitSize);
        BloodHitVfxService.RequestTargetHit(health, bloodPoint,
            direction, CombatImpactShape.Thrust, .85f, health.IsDead ? 2 : 0);
        if (health.IsDead) return;
        if (health.TryGetComponent<EnemyHitResponseCoordinator>(out var response))
            response.TryApplyDerivedFlinch(new DamageInfo(0f, point, null, direction,
                element: WeaponElement.Electric, playerAttackKind: PlayerAttackKind.Elemental));
    }
}
