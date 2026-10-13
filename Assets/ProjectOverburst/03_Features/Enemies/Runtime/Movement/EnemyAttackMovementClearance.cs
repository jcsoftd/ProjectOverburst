using UnityEngine;
using UnityEngine.SceneManagement;

// Queries run synchronously on Unity's main thread. Reused buffers contain no
// attack state and are only read up to the count returned by the current query.
public static class EnemyAttackMovementClearance
{
    private const float Skin = .025f;
    private static readonly Collider[] overlaps = new Collider[32];
    private static readonly RaycastHit[] hits = new RaycastHit[32];

    public static Vector3 Clip(GameObject owner, Vector3 center, float radius, float halfHeight,
        Vector3 displacement, out bool saturated)
    {
        saturated = false; displacement.y = 0f;
        float distance = displacement.magnitude;
        if (owner == null || distance < .000001f) return Vector3.zero;
        if (!Finite(distance) || !Finite(radius) || !Finite(halfHeight) || radius <= 0f || halfHeight <= 0f
            || !Finite(center.x) || !Finite(center.y) || !Finite(center.z)) return Vector3.zero;
        PhysicsScene physics = owner.scene.GetPhysicsScene();
        if (!physics.IsValid()) return Vector3.zero;
        radius = Mathf.Max(.01f, radius);
        float segment = Mathf.Max(0f, halfHeight - radius);
        Vector3 top = center + Vector3.up * segment, bottom = center - Vector3.up * segment;
        Vector3 direction = displacement / distance;
        CapsuleCollider body = null;
        bool concave = false;
        int count = physics.OverlapCapsule(top, bottom, radius + Skin, overlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count >= overlaps.Length) { saturated = true; return Vector3.zero; }
        for (int i = 0; i < count; i++)
        {
            Collider other = overlaps[i];
            if (!IsBlocker(owner, other)) continue;
            if (!SupportsClosestPoint(other))
            {
                concave = true;
                if (!body) body = ResolveBody(owner);
                if (TrySeparation(body, other, center, out var separation)
                    && separation.y < .6f && Vector3.Dot(separation, direction) < -.001f) return Vector3.zero;
                continue; // The surface-normal sweep below resolves concave floor and wall contacts.
            }
            if (IsFloorBelow(other, center, halfHeight)
                || !MovesToward(other, center, direction)) continue;
            return Vector3.zero;
        }
        count = physics.CapsuleCast(top, bottom, radius + Skin, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) { saturated = true; return Vector3.zero; }
        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (!IsBlocker(owner, hit.collider)) continue;
            if (!SupportsClosestPoint(hit.collider)) { concave = true; continue; }
            if (IsFloorBelow(hit.collider, center, halfHeight) || hit.normal.y >= .6f
                || hit.distance <= .001f && !MovesToward(hit.collider, center, direction)) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - .001f));
        }
        if (!concave || allowed <= 0) return direction * allowed;
        // Starting inside the padding yields a zero-distance cast with an artificial normal.
        // Lift only this concave sweep off its supporting plane; keep primitive queries unchanged.
        Vector3 sweep = direction;
        float lift = Skin * 2;
        if (physics.Raycast(center, Vector3.down, out var floor, halfHeight + Skin, ~0, QueryTriggerInteraction.Ignore)
            && IsBlocker(owner, floor.collider) && floor.normal.y >= .6f)
        {
            lift = Mathf.Max(lift, (radius + Skin - Vector3.Dot(bottom - floor.point, floor.normal)) / floor.normal.y + .001f);
            sweep.y = -(direction.x * floor.normal.x + direction.z * floor.normal.z) / floor.normal.y;
        }
        float slopeScale = sweep.magnitude;
        count = physics.CapsuleCast(top + Vector3.up * lift, bottom + Vector3.up * lift,
            radius + Skin, sweep / slopeScale, hits, allowed * slopeScale, ~0, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) { saturated = true; return Vector3.zero; }
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (!IsBlocker(owner, hit.collider) || SupportsClosestPoint(hit.collider) || hit.normal.y >= .6f) continue;
            if (hit.distance <= .001f)
            {
                if (!body) body = ResolveBody(owner);
                if (TrySeparation(body, hit.collider, center, out var separation)
                    && (separation.y >= .6f || Vector3.Dot(separation, direction) >= -.001f)) continue;
            }
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance / slopeScale - .001f));
        }
        return direction * allowed;
    }

    private static bool IsBlocker(GameObject owner, Collider collider)
        => collider != null && collider.enabled && !collider.isTrigger
            && collider.transform != owner.transform && !collider.transform.IsChildOf(owner.transform);

    private static bool IsFloorBelow(Collider collider, Vector3 center, float halfHeight)
    {
        Vector3 point = collider.ClosestPoint(center);
        return point.y <= center.y - halfHeight + Skin;
    }

    private static bool SupportsClosestPoint(Collider collider)
        => collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider
            || collider is MeshCollider mesh && mesh.convex;

    private static CapsuleCollider ResolveBody(GameObject owner)
    {
        var actor = owner.GetComponent<EnemyActor>();
        return actor && actor.CollisionRoot ? actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>(true)
            : owner.GetComponent<CapsuleCollider>();
    }

    private static bool TrySeparation(CapsuleCollider body, Collider other, Vector3 center, out Vector3 separation)
    {
        separation = default;
        if (!body) return false;
        Vector3 position = body.transform.position + center - body.transform.TransformPoint(body.center);
        return Physics.ComputePenetration(body, position, body.transform.rotation,
            other, other.transform.position, other.transform.rotation, out separation, out _);
    }

    private static bool MovesToward(Collider collider, Vector3 center, Vector3 direction)
    {
        Vector3 to = collider.bounds.center - center; to.y = 0f;
        return to.sqrMagnitude < .000001f || Vector3.Dot(direction, to.normalized) > .001f;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
