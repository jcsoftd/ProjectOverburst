using UnityEngine;

// Planar swept-circle guard. Returns a path prefix, so it cannot push the player,
// teleport an enemy outward, or tunnel through the protected circle in a long step.
public static class EnemyPlayerApproachClearance
{
    public const float SurfaceGap = .12f;

    // Body spacing follows the physical player capsule. The larger combat hurt
    // volume is for receiving attacks and must not stop short-reach enemies.
    public static CombatTargetVolume ResolveBodyVolume(CharacterController controller, CombatTarget fallback)
    {
        if (controller != null && controller.enabled && controller.gameObject.activeInHierarchy)
        {
            Bounds bounds = controller.bounds;
            return new CombatTargetVolume(bounds.center,
                Mathf.Max(bounds.extents.x, bounds.extents.z), bounds.extents.y);
        }
        return fallback != null ? fallback.CurrentVolume : default;
    }

    public static Vector3 Clip(Vector3 current, Vector3 candidate, Vector3 center, float radius)
    {
        Vector3 start = current - center; start.y = 0;
        Vector3 move = candidate - current; move.y = 0;
        float a = move.sqrMagnitude;
        if (a < .00000001f || radius <= 0) return candidate;
        float c = start.sqrMagnitude - radius * radius;
        float b = Vector3.Dot(start, move);
        if (c <= 0)
        {
            // Existing overlap (spawn or player movement): allow escape, never deepen it.
            return b >= 0 ? candidate : current;
        }
        if (b >= 0) return candidate;
        float discriminant = b * b - a * c;
        if (discriminant <= 0) return candidate;
        float entry = (-b - Mathf.Sqrt(discriminant)) / a;
        if (entry < 0 || entry >= 1) return candidate;
        return Vector3.Lerp(current, candidate, Mathf.Max(0, entry - .0001f));
    }
}
