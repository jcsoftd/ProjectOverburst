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
        int count = physics.OverlapCapsule(top, bottom, radius + Skin, overlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count >= overlaps.Length) { saturated = true; return Vector3.zero; }
        for (int i = 0; i < count; i++)
        {
            Collider other = overlaps[i];
            if (!IsBlocker(owner, other) || IsFloorBelow(other, center, halfHeight)
                || !MovesToward(other, center, direction)) continue;
            return Vector3.zero;
        }
        count = physics.CapsuleCast(top, bottom, radius + Skin, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) { saturated = true; return Vector3.zero; }
        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (!IsBlocker(owner, hit.collider) || IsFloorBelow(hit.collider, center, halfHeight) || hit.normal.y >= .6f
                || hit.distance <= .001f && !MovesToward(hit.collider, center, direction)) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - .001f));
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

    private static bool MovesToward(Collider collider, Vector3 center, Vector3 direction)
    {
        Vector3 to = collider.bounds.center - center; to.y = 0f;
        return to.sqrMagnitude < .000001f || Vector3.Dot(direction, to.normalized) > .001f;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
