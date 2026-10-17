using UnityEngine;
using UnityEngine.AI;

public sealed partial class EnemyAIController
{
    EnemyNavigationRoute privateNavigation;
    EnemyNavigationRoute observedNavigation;
    int observedNavigationRevision, navigationCorner;
    float nextNavigationTime;
    Vector3 navigationDestination;
    bool navigationActive;
    Vector3 rejectedNavigationGoal;
    float rejectedNavigationUntil;
    float nextApproachLineTime;
    bool approachLineClear = true;

    internal bool HasNavigationApproachToTarget()
    {
        if (UsesRangedTactics || target == null || RunWalkableContext.Current != null) return true;
        if (Time.time < nextApproachLineTime) return approachLineClear;
        nextApproachLineTime = EnemyAiTickScheduler.NextPlanningTime(Time.time, .25f, GetInstanceID());
        using var sample = EnemyNavigationRoute.Marker.Auto();
        approachLineClear = !NavMesh.SamplePosition(transform.position, out var from, 1.5f, EnemyNavigationRoute.AreaMask)
            || !NavMesh.SamplePosition(target.position, out var to, 2f, EnemyNavigationRoute.AreaMask)
            || !NavMesh.Raycast(from.position, to.position, out _, EnemyNavigationRoute.AreaMask);
        return approachLineClear;
    }

    void ResetNavigation()
    {
        if (privateNavigation != null) { privateNavigation.Count = 0; privateNavigation.NextRefresh = 0f; }
        observedNavigation = null;
        navigationActive = false; nextNavigationTime = 0f;
        nextApproachLineTime = 0f; approachLineClear = true;
        hasDetour = false; detourObstacle = null;
        rejectedNavigationUntil = 0f;
    }

    Vector3 ResolveNavigatedChase(Vector3 desired, Vector3 goal)
    {
        // Grid-based arenas retain the existing shared flow field. Authored cave/desert use their baked NavMesh.
        if (RunWalkableContext.Current != null || target == null) return desired;
        Vector3 position = transform.position;
        if (Time.time < nextNavigationTime)
            return navigationActive ? navigationDestination : desired;
        nextNavigationTime = EnemyAiTickScheduler.NextPlanningTime(Time.time, .25f, GetInstanceID());
        using var sample = EnemyNavigationRoute.Marker.Auto();
        navigationActive = false;
        var route = CalculateNavigatedChase(desired, goal, position);
        var safe = ResolvePhysicalObstacle(route);
        return hasDetour || FlatSqr(safe, route) > .0001f ? FollowNavigation(safe, position) : route;
    }

    Vector3 CalculateNavigatedChase(Vector3 desired, Vector3 goal, Vector3 position)
    {
        float radius = crowdAgent != null ? crowdAgent.PhysicalBodyRadius : .4f;
        if (Overburst.Caves.CaveFallProtection.TryConnectorWaypoint(position, target.position, radius, out var crossing))
            return FollowNavigation(crossing, position);
        if (!NavMesh.SamplePosition(position, out var start, 1.5f, EnemyNavigationRoute.AreaMask)
            || Mathf.Abs(start.position.y - position.y) > 1.2f)
            return desired; // No navigation surface in a town/debug scene.
        if (Time.time < rejectedNavigationUntil && FlatSqr(goal, rejectedNavigationGoal) < 4f) goal = target.position;
        if (!NavMesh.SamplePosition(goal, out var end, 2f, EnemyNavigationRoute.AreaMask)
            && !NavMesh.SamplePosition(target.position, out end, 2f, EnemyNavigationRoute.AreaMask))
            return HoldNavigation(position);
        if (!NavMesh.Raycast(start.position, end.position, out _, EnemyNavigationRoute.AreaMask))
        {
            if (NavMesh.SamplePosition(desired, out var steer, .75f, EnemyNavigationRoute.AreaMask)
                && !NavMesh.Raycast(start.position, steer.position, out _, EnemyNavigationRoute.AreaMask))
                return desired;
            return FollowNavigation(end.position, position);
        }

        var shared = EnemySquadPursuitRuntimeService.NavigationFor(this);
        var route = shared ?? (privateNavigation ??= new EnemyNavigationRoute());
        int previousRevision = route.Revision;
        route.Refresh(start.position, end.position);
        if (route.Revision != previousRevision && route.Count == 0 && FlatSqr(goal, target.position) > 1f
            && NavMesh.SamplePosition(target.position, out var fallback, 2f, EnemyNavigationRoute.AreaMask))
        {
            rejectedNavigationGoal = goal; rejectedNavigationUntil = Time.time + 2f;
            end = fallback;
            route = privateNavigation ??= new EnemyNavigationRoute();
            route.NextRefresh = 0f;
            route.Refresh(start.position, fallback.position, true);
        }
        if (TryFollowRoute(route, start.position, position, out var waypoint))
            return FollowNavigation(waypoint, position);
        // A member stranded on the other side of an obstacle needs a separate connector, not a squad-wide rebuild.
        route = privateNavigation ??= new EnemyNavigationRoute();
        route.Refresh(start.position, end.position, true);
        if (TryFollowRoute(route, start.position, position, out waypoint))
            return FollowNavigation(waypoint, position);
        return HoldNavigation(position);
    }

    bool TryFollowRoute(EnemyNavigationRoute route, Vector3 start, Vector3 position, out Vector3 waypoint)
    {
        waypoint = position;
        if (route.Count < 2) return false;
        if (observedNavigation != route || observedNavigationRevision != route.Revision)
        {
            observedNavigation = route; observedNavigationRevision = route.Revision;
            navigationCorner = 1;
            // Choose the closest segment, not a corner behind a dispersed follower.
            float closest = float.PositiveInfinity;
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 a = route.Corners[i - 1], delta = route.Corners[i] - a;
                float u = Mathf.Clamp01(Vector3.Dot(start - a, delta) / Mathf.Max(.0001f, delta.sqrMagnitude));
                float d = (start - a - delta * u).sqrMagnitude;
                if (d < closest) { closest = d; navigationCorner = i; }
            }
        }
        // At most four visibility checks per planning slice. Do not cut a blocked corner.
        int first = navigationCorner;
        int last = Mathf.Min(route.Count - 1, first + 3);
        for (int i = last; i >= first; i--)
        {
            var corner = route.Corners[i];
            if (i == first && first < route.Count - 1 && FlatSqr(position, corner) < .16f) break;
            if (NavMesh.Raycast(start, corner, out _, EnemyNavigationRoute.AreaMask)) continue;
            navigationCorner = i; waypoint = corner; return true;
        }
        // A motor stops just short of a waypoint. At a tight turn that tiny gap can make
        // the next NavMesh ray touch the boundary; confirm body clearance before rounding it.
        if (first < route.Count - 1 && FlatSqr(position, route.Corners[first]) < .16f)
        {
            var next = route.Corners[first + 1]; var delta = next - position; delta.y = 0;
            float radius = crowdAgent != null ? crowdAgent.PhysicalBodyRadius : .4f;
            if (ClearObstacleSegment(position, position + delta.normalized * Mathf.Min(delta.magnitude, 1.5f), radius, out _))
            { navigationCorner = first + 1; waypoint = next; return true; }
        }
        return false;
    }

    Vector3 FollowNavigation(Vector3 waypoint, Vector3 position)
    {
        navigationActive = true;
        navigationDestination = waypoint;
        navigationDestination.y = position.y;
        return navigationDestination;
    }
    Vector3 HoldNavigation(Vector3 position) => FollowNavigation(position, position);
    static float FlatSqr(Vector3 a, Vector3 b) { a.y = b.y; return (a - b).sqrMagnitude; }
}
