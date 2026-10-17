using UnityEngine;
using UnityEngine.AI;
using Unity.Profiling;

// Shared by a squad. Corners are reused; a blocked/queued route never instructs a wall-facing walk.
internal sealed class EnemyNavigationRoute
{
    // The built-in Jump area marks cave stairs; bridge surfaces/links retain Walkable.
    static readonly int StairArea = NavMesh.GetAreaFromName("Jump");
    internal static int AreaMask => Overburst.Caves.CaveFallProtection.HasActiveWorld
        ? NavMesh.AllAreas & ~(1 << StairArea) : NavMesh.AllAreas;
    internal readonly NavMeshPath Path = new NavMeshPath();
    internal readonly Vector3[] Corners = new Vector3[64];
    internal int Count, Revision;
    internal Vector3 Goal;
    internal float NextRefresh;
    static int budgetFrame = -1;
    static double spentMilliseconds;
    internal static int SearchesThisFrame { get; private set; }
    internal static int TotalSearches { get; private set; }
    internal static int DeferredSearches { get; private set; }
    internal static double MaximumSearchMilliseconds { get; private set; }
    internal static readonly ProfilerMarker Marker = new ProfilerMarker("Overburst.AI.Navigation");
    static readonly ProfilerMarker SearchMarker = new ProfilerMarker("Overburst.AI.Navigation.Path");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBudget()
    {
        budgetFrame = -1; spentMilliseconds = 0;
        SearchesThisFrame = TotalSearches = DeferredSearches = 0;
        MaximumSearchMilliseconds = 0;
    }

    internal bool Refresh(Vector3 from, Vector3 goal, bool force = false)
    {
        float now = Time.time;
        if (now < NextRefresh && (!force || Count == 0)) return Count > 0;
        if (!force && Count > 0 && (Goal - goal).sqrMagnitude < 2.25f && now < NextRefresh + 1f)
            return true;
        if (budgetFrame != Time.frameCount)
        { budgetFrame = Time.frameCount; SearchesThisFrame = 0; spentMilliseconds = 0; }
        // Additional navigation work has its own small cooperative budget; spawn's 2ms is unchanged.
        if (SearchesThisFrame >= 2 || spentMilliseconds >= .5)
        { DeferredSearches++; return Count > 0; }
        double began = Time.realtimeSinceStartupAsDouble;
        SearchesThisFrame++; TotalSearches++;
        using (SearchMarker.Auto())
        {
            bool valid = NavMesh.CalculatePath(from, goal, AreaMask, Path)
                && Path.status == NavMeshPathStatus.PathComplete;
            Count = valid ? Path.GetCornersNonAlloc(Corners) : 0;
            // A truncated path cannot be interpreted as arrival at the requested destination.
            if (Count == Corners.Length && (Corners[Count - 1] - goal).sqrMagnitude > .25f) Count = 0;
            Goal = goal; Revision++; NextRefresh = now + .5f;
        }
        double elapsed = (Time.realtimeSinceStartupAsDouble - began) * 1000;
        spentMilliseconds += elapsed;
        MaximumSearchMilliseconds = System.Math.Max(MaximumSearchMilliseconds, elapsed);
        return Count > 0;
    }
}
