using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

// These are objective hints, separate from the circular map's local enemy detection.
public sealed class MinimapGuideSource
{
    public enum Kind { Portal, Area, Enemy, Approach, Frontier, EmergencyExit }
    public struct Hint { public Kind Type; public Vector3 Position, Direction; public int Id; }
    public const int Capacity = 48;
    readonly Hint[] hints = new Hint[Capacity];
    readonly List<EnemyActor> actors = new List<EnemyActor>();
    readonly List<Hint> nearest = new List<Hint>(6);
    int[] parents = System.Array.Empty<int>(), queue = System.Array.Empty<int>();
    MinimapExplorationMap pathsMap;
    int pathOrigin = -1, frontierCount;
    Vector3 origin;
    public int Count { get; private set; }
    public int Remaining { get; private set; }
    public DungeonExitPortal Portal { get; private set; }
    public Hint Get(int index) => hints[index];
    public void Clear()
    {
        Count = Remaining = 0; Portal = null; actors.Clear(); nearest.Clear();
        pathsMap = null; pathOrigin = -1; parents = queue = System.Array.Empty<int>();
    }
    public void Refresh(RunWorldGate gate, MinimapExplorationMap map, Vector3 player, float detectionRadius)
    {
        Count = Remaining = frontierCount = 0; nearest.Clear(); actors.Clear(); origin = player;
        Portal = gate && gate.Completion ? gate.Completion.Portal : null;
        if (!gate || map == null || gate.Roster == null || !gate.SpawnBudget) return;
        var escape = gate.EmergencyExit;
        if (escape && escape.isActiveAndEnabled && escape.Discovered && map.IsRevealed(escape.transform.position))
            Add(Kind.EmergencyExit, escape.transform.position, -2);
        if (gate.Completion && gate.Completion.IsCompleted)
        {
            if (Portal && Portal.gameObject.activeInHierarchy) Add(Kind.Portal, Portal.transform.position, -1);
            return;
        }
        var roster = gate.Roster;
        Remaining = roster.Terrain == MapTerrainKind.Cave ? Mathf.Max(0, roster.Target - roster.Kills) : 0;
        if (Remaining == 0 || Remaining > 10) return;
        for (int a = 0; a < roster.Areas.Count; a++)
        {
            var area = roster.Areas[a];
            int alive = 0, pending = 0;
            for (int i = area.FirstMember; i < area.FirstMember + area.Count; i++)
            {
                if (roster.Members[i].State == MapRunRoster.Life.Dead) continue;
                alive++; if (roster.Members[i].State == MapRunRoster.Life.Pending) pending++;
            }
            if (alive == 0) continue;
            bool known = TryKnownPoint(area.Ground.Points, map, out var knownPoint);
            if (Remaining > 5)
            {
                if (known) Add(Kind.Area, knownPoint, area.Ground.Index);
                else AddFrontier(map, area.Ground.Points[0]);
                continue;
            }
            var field = gate.SpawnBudget.Fields[area.Ground.Index];
            bool distantKnown = false;
            actors.Clear(); field.CollectLivingActors(actors);
            foreach (var actor in actors)
            {
                var position = actor.transform.position;
                if (map.IsRevealed(position))
                {
                    // Remaining-target guidance must not turn into tracking beyond the normal detection radius.
                    if (FlatDistance(position, origin) > detectionRadius * detectionRadius) { distantKnown = true; continue; }
                    var hint = new Hint { Type = Kind.Enemy, Position = position, Id = actor.GetInstanceID() };
                    int at = 0; float distance = FlatDistance(position, origin);
                    while (at < nearest.Count && FlatDistance(nearest[at].Position, origin) <= distance) at++;
                    if (at >= Remaining) continue;
                    nearest.Insert(at, hint); if (nearest.Count > Remaining) nearest.RemoveAt(nearest.Count - 1);
                }
                else AddFrontier(map, position);
            }
            if (distantKnown && known && pending == 0) Add(Kind.Area, knownPoint, area.Ground.Index);
            if (pending > 0)
            {
                // An approach marks a real spawn candidate region, never a promised spawn point/time.
                if (TryKnownPoint(field.FiniteApproaches, map, out var approach)) Add(Kind.Approach, approach, area.Ground.Index);
                else if (known) Add(Kind.Approach, knownPoint, area.Ground.Index);
                else AddFrontier(map, area.Ground.Points[0]);
            }
        }
        foreach (var hint in nearest) if (Count < Capacity) hints[Count++] = hint;
    }
    bool TryKnownPoint(IReadOnlyList<Vector3> points, MinimapExplorationMap map, out Vector3 result)
    {
        result = default; float best = float.PositiveInfinity;
        if (points == null) return false;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            float d = FlatDistance(p, origin);
            if (d >= best || !map.IsRevealed(p)) continue;
            result = p; best = d;
        }
        return !float.IsPositiveInfinity(best);
    }
    static float FlatDistance(Vector3 a, Vector3 b) { var d = a - b; return d.x * d.x + d.z * d.z; }
    void Add(Kind type, Vector3 point, int id)
    { if (Count < Capacity) hints[Count++] = new Hint { Type = type, Position = point, Id = id }; }
    void AddFrontier(MinimapExplorationMap map, Vector3 destination)
    {
        if (Count >= Capacity || frontierCount >= 20 || !TryFrontier(map, origin, destination, out var point, out var direction)) return;
        for (int i = 0; i < Count; i++)
            if (hints[i].Type == Kind.Frontier && FlatDistance(hints[i].Position, point) < 4 * map.CellSize * map.CellSize) return;
        hints[Count++] = new Hint { Type = Kind.Frontier, Position = point, Direction = direction, Id = map.CellAt(point) };
        frontierCount++;
    }
    // The output stops at the FIRST unknown cell along an actual connected route.
    public bool TryFrontier(MinimapExplorationMap map, Vector3 from, Vector3 target, out Vector3 boundary, out Vector3 direction)
    {
        boundary = direction = default;
        int start = map.CellAt(from), end = map.CellAt(target);
        if (!map.IsGround(start) || !map.IsRevealedCell(start) || !map.IsGround(end)) return false;
        if (pathsMap != map || pathOrigin != start)
        {
            int length = map.Width * map.Height;
            if (parents.Length != length) { parents = new int[length]; queue = new int[length]; }
            System.Array.Fill(parents, -1); parents[start] = start;
            int head = 0, tail = 1; queue[0] = start;
            while (head < tail)
            {
                int p = queue[head++]; byte links = map.Connections(p);
                Visit(p + 1, (links & MinimapExplorationMap.East) != 0 && p % map.Width < map.Width - 1);
                Visit(p - 1, (links & MinimapExplorationMap.West) != 0 && p % map.Width > 0);
                Visit(p + map.Width, (links & MinimapExplorationMap.North) != 0);
                Visit(p - map.Width, (links & MinimapExplorationMap.South) != 0);
                void Visit(int next, bool linked)
                { if (linked && map.IsGround(next) && parents[next] < 0) { parents[next] = p; queue[tail++] = next; } }
            }
            pathsMap = map; pathOrigin = start;
        }
        if (parents[end] < 0) return false;
        int firstUnknown = -1;
        for (int p = end; p != start; p = parents[p])
            if (!map.IsRevealedCell(p)) firstUnknown = p;
        if (firstUnknown < 0) return false;
        boundary = map.Center(parents[firstUnknown]);
        direction = map.Center(firstUnknown) - boundary;
        return true;
    }
}
