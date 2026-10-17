using UnityEngine;
using UnityEngine.AI;

public sealed partial class EnemyAIController
{
    static readonly RaycastHit[] obstacleHits = new RaycastHit[24];
    Collider detourObstacle;
    Vector3 detourCorner;
    bool hasDetour;
    static int obstacleMask, obstacleFrame = -1, obstacleQueries;
    static double obstacleMilliseconds;
    static readonly Unity.Profiling.ProfilerMarker ObstacleMarker = new Unity.Profiling.ProfilerMarker("Overburst.AI.Navigation.Obstacle");

    Vector3 ResolvePhysicalObstacle(Vector3 destination)
    {
        using var sample = ObstacleMarker.Auto();
        var position = transform.position;
        float radius = crowdAgent != null ? crowdAgent.PhysicalBodyRadius : .4f;
        if (hasDetour && detourObstacle && detourObstacle.enabled
            && FlatSqr(position, detourCorner) > .25f)
        {
            if (ClearObstacleSegment(position, detourCorner, radius, out _)) return detourCorner;
        }
        hasDetour = false;
        Vector3 travel = destination - position; travel.y = 0;
        if (travel.sqrMagnitude < .01f && target != null) travel = target.position - position;
        travel.y = 0;
        if (travel.sqrMagnitude < .01f) return destination;
        float lookAhead = Mathf.Min(travel.magnitude, Mathf.Max(1.5f, radius + 1f));
        if (ClearObstacleSegment(position, position + travel.normalized * lookAhead, radius, out var obstacle))
            return destination;

        if (!obstacle || !NavMesh.SamplePosition(position, out var start, 1.5f, EnemyNavigationRoute.AreaMask)) return position;
        var bounds = obstacle.bounds;
        float padding = radius + .35f;
        bounds.Expand(new Vector3(padding * 2, 0, padding * 2));
        Vector3 goal = target != null ? target.position : destination;
        float best = float.PositiveInfinity;
        // Four conservative corners, only when blocked. Both physics clearance and a live floor are required.
        for (int i = 0; i < 4; i++)
        {
            var candidate = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x, position.y,
                (i & 2) == 0 ? bounds.min.z : bounds.max.z);
            if (!NavMesh.SamplePosition(candidate, out var floor, .75f, EnemyNavigationRoute.AreaMask)
                || Mathf.Abs(floor.position.y - position.y) > 1f
                || FlatSqr(position, floor.position) < .36f
                || NavMesh.Raycast(start.position, floor.position, out _, EnemyNavigationRoute.AreaMask)
                || !ClearObstacleSegment(position, floor.position, radius, out _)) continue;
            float score = Mathf.Sqrt(FlatSqr(position, floor.position)) + Mathf.Sqrt(FlatSqr(floor.position, goal));
            if (score >= best) continue;
            best = score; detourCorner = floor.position;
        }
        if (float.IsPositiveInfinity(best)) return position;
        detourObstacle = obstacle; hasDetour = true;
        detourCorner.y = position.y;
        return detourCorner;
    }

    bool ClearObstacleSegment(Vector3 from, Vector3 to, float radius, out Collider blocker)
    {
        blocker = null;
        Vector3 delta = to - from; delta.y = 0;
        float length = delta.magnitude;
        if (length < .001f) return true;
        // Cover the lower body as well as its centre: small mushrooms/crates can block
        // the Rigidbody even when a chest-height sphere passes over them.
        Vector3 lower = from + Vector3.up * (radius + .04f);
        Vector3 upper = from + Vector3.up * Mathf.Max(.7f, radius + .12f);
        if (obstacleFrame != Time.frameCount) { obstacleFrame = Time.frameCount; obstacleQueries = 0; obstacleMilliseconds = 0; }
        if (obstacleQueries >= 32 || obstacleMilliseconds >= .5) return false;
        if (obstacleMask == 0) obstacleMask = ~LayerMask.GetMask("Enemy", "Player", "Ignore Raycast");
        double began = Time.realtimeSinceStartupAsDouble;
        obstacleQueries++;
        int count = Physics.CapsuleCastNonAlloc(lower, upper, Mathf.Max(.1f, radius), delta / length,
            obstacleHits, length, obstacleMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = obstacleHits[i]; var collider = hit.collider;
            if (!collider || collider.transform.IsChildOf(transform) || hit.normal.y >= .6f
                || collider.GetComponentInParent<Overburst.Caves.CaveWalkSurface>()
                || collider.GetComponentInParent<EnemyActor>() || collider.GetComponentInParent<PlayerActorRuntime>()) continue;
            var bounds = collider.bounds;
            if (bounds.size.y <= .55f && Mathf.Min(bounds.size.x, bounds.size.z) >= bounds.size.y * 3
                && bounds.max.y <= from.y + .3f) continue; // Low, flat deck plates are traversable.
            // Padding contacts on a supporting plane must not be treated as a wall.
            if (hit.distance <= .001f && collider.bounds.max.y <= from.y + .06f) continue;
            if (hit.distance < nearest) { nearest = hit.distance; blocker = collider; }
        }
        obstacleMilliseconds += (Time.realtimeSinceStartupAsDouble - began) * 1000;
        return blocker == null && count < obstacleHits.Length;
    }
}
