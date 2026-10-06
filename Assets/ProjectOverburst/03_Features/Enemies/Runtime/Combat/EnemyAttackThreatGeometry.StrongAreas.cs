using UnityEngine;

public static partial class EnemyAttackThreatGeometry
{
    // Standard ground areas use the authored combat body, rather than mixing
    // a locomotion collider at the outer edge with a center point at other edges.
    public static bool UsesStandardStrongBodyGeometry(EnemyActor actor, EnemyAbilityDefinition ability)
        => UsesStandardAttackAreas && actor != null && ability != null && ability.IsTelegraphedStrongAttack
            && ResolveTier(actor) != ThreatTier.None
            && (ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc
                || ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam
                || ability.ExecutionMode == EnemyAbilityExecutionMode.Charge);

    public static bool OverlapsStrongArea(EnemyActor actor, EnemyAbilityDefinition ability,
        Vector3 center, Vector3 facing, CombatTargetVolume body)
    {
        if (Mathf.Abs(center.y - body.Center.y) > body.HalfHeight + ability.VerticalTolerance) return false;
        Vector3 delta = body.Center - center; delta.y = 0f;
        facing.y = 0f;
        if (facing.sqrMagnitude < .0001f) facing = Vector3.forward;
        facing.Normalize();
        float outer = ResolveRadius(actor, ability);
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge)
        {
            float along = Mathf.Clamp(Vector3.Dot(delta, facing), 0f, Mathf.Max(.8f, outer));
            float combined = ChargeHalfWidth + body.Radius;
            return (delta - facing * along).sqrMagnitude <= combined * combined;
        }
        float inner = ResolveSectorInnerRadius(actor, ability);
        float halfAngle = Mathf.Clamp(ResolveHitAngle(actor, ability), 0f, 360f) * .5f;
        float distance = delta.magnitude;
        if (halfAngle >= 179.95f || distance < .0001f || Vector3.Angle(facing, delta) <= halfAngle)
        {
            float closestRadius = Mathf.Clamp(distance, inner, outer);
            float gap = distance - closestRadius;
            return gap * gap <= body.Radius * body.Radius;
        }
        float side = Vector3.Cross(facing, delta).y >= 0f ? 1f : -1f;
        Vector3 boundary = Quaternion.AngleAxis(halfAngle * side, Vector3.up) * facing;
        float projection = Mathf.Clamp(Vector3.Dot(delta, boundary), inner, outer);
        return (delta - boundary * projection).sqrMagnitude <= body.Radius * body.Radius;
    }

    public static float StrongChargeEntry(Vector3 center, Vector3 facing, CombatTargetVolume body)
    {
        facing.y = 0f; facing.Normalize();
        Vector3 delta = body.Center - center; delta.y = 0f;
        float along = Vector3.Dot(delta, facing);
        float sideSquared = Mathf.Max(0f, delta.sqrMagnitude - along * along);
        float combined = ChargeHalfWidth + body.Radius;
        return Mathf.Max(0f, along - Mathf.Sqrt(Mathf.Max(0f, combined * combined - sideSquared)));
    }

    public static Vector3 StrongContactPoint(Vector3 origin, CombatTargetVolume body)
    {
        Vector3 delta = origin - body.Center; delta.y = 0f;
        Vector3 planar = body.Center + Vector3.ClampMagnitude(delta, body.Radius);
        planar.y = Mathf.Clamp(origin.y, body.Center.y - body.HalfHeight, body.Center.y + body.HalfHeight);
        return planar;
    }
}
