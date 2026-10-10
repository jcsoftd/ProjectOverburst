using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Overburst.Caves
{
// Shared by the editor preview and live generation; yields without changing the seeded search order.
public class CavePlatformLayout
{
    public sealed class Room
    {
        public int platform;
        public Vector3 position;
        public float yaw;
        public bool[] used;
    }
    public sealed class Link
    {
        public int a, b, portA, portB;
        public float angleA, angleB;
        public Vector3 start, end;
        public bool stone;
        public float pitch;
        public int stairCount, stairDirection;
        public float reviewShortfall;
    }
    public sealed class Layout
    {
        public List<Room> rooms = new List<Room>();
        public List<Link> links = new List<Link>();
    }
    protected static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
    protected static Vector3 Direction(float angle) => new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0, Mathf.Sin(angle * Mathf.Deg2Rad));
    protected static Vector3 Rotate(Vector3 p, float yaw) => Quaternion.Euler(0, yaw, 0) * p;
    protected static float Angle(Vector3 v) => Mathf.Atan2(v.z, v.x) * Mathf.Rad2Deg;
    protected static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    public static Vector2[] Hull(IEnumerable<Vector2> input)
    {
        var points = input.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToArray();
        var hull = new List<Vector2>();
        foreach (var p in points)
        { while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
        int lower = hull.Count;
        for (int i = points.Length - 2; i >= 0; i--)
        { var p = points[i]; while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
        hull.RemoveAt(hull.Count - 1); return hull.ToArray();
    }
    protected static bool Inside(Vector2[] hull, Vector2 p) => Enumerable.Range(0, hull.Length).All(i => Cross(hull[(i + 1) % hull.Length] - hull[i], p - hull[i]) >= -.001f);
    protected static float EdgeDistance(Vector2[] hull, Vector2 p) => Enumerable.Range(0, hull.Length).Min(i =>
    { var a = hull[i]; var d = hull[(i + 1) % hull.Length] - a; return Vector2.Distance(p, a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude)); });
    protected static Vector2[] WorldHull(CavePlatformLibrary library, Room room) => library.platforms[room.platform].hull.Select(p => XZ(room.position + Rotate(new Vector3(p.x, 0, p.y), room.yaw))).ToArray();
    protected static bool Overlap(Vector2[] a, Vector2[] b, float margin)
    {
        foreach (var polygon in new[] { a, b })
            for (int i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[(i + 1) % polygon.Length] - polygon[i]; var axis = new Vector2(-edge.y, edge.x).normalized;
                float amin = a.Min(p => Vector2.Dot(p, axis)), amax = a.Max(p => Vector2.Dot(p, axis));
                float bmin = b.Min(p => Vector2.Dot(p, axis)), bmax = b.Max(p => Vector2.Dot(p, axis));
                if (amax + margin < bmin || bmax + margin < amin) return false;
            }
        return true;
    }
    protected static bool SegmentCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        => Cross(b - a, c - a) * Cross(b - a, d - a) < 0 && Cross(d - c, a - c) * Cross(d - c, b - c) < 0;
    public static System.Collections.IEnumerator Solve(CavePlatformLibrary library, int count, int seed, float spacing, Func<CavePlatformLibrary.Platform, Vector3, bool> blocked, Action<Layout> complete)
    {
        if (!library || library.platforms == null || library.platforms.Length < 2 || count < 2 || count > 24) throw new ArgumentException("Use 2–24 rooms.");
        if (!Mathf.Approximately(spacing, 1)) throw new ArgumentException("Connection distances follow the original bridge/stair dimensions; spacing must stay 1.");
        if (library.timber == null || library.singleStair == null || library.doubleStair == null) throw new InvalidOperationException("Refresh the connector library first.");
        var slice = System.Diagnostics.Stopwatch.StartNew();
        bool ApproachClear(CavePlatformLibrary.Platform p, Vector3 a, Vector3 d)
        { for(float n=-1.2f;n<2;n+=.4f) if(blocked(p,a+d*n)) return false; return true; }
        bool PassageHitsRoom(CavePlatformLibrary l, Room r, Vector3 a, Vector3 b)
        { for(float t=0;t<=1;t+=.05f) if(blocked(l.platforms[r.platform],Rotate(Vector3.Lerp(a,b,t)-r.position,-r.yaw))) return true; return false; }
        var rejected = new Dictionary<string, int>(); int furthest = 0;
        Layout bestLayout = null; int bestJunctionDeficit = int.MaxValue, bestReviews = int.MaxValue, completeCandidates = 0;
        void Reject(string reason) { rejected.TryGetValue(reason, out int n); rejected[reason] = n + 1; }
        for (int restart = 0; restart < 30; restart++)
        {
            var random = new System.Random(unchecked(seed + restart * 7919)); var layout = new Layout();
            var order = Enumerable.Range(0, count).Select(i => i % library.platforms.Length).OrderBy(i => random.Next()).ToList();
            if (count == 3) order = new[] { "D1_19", "D1_06", "D1_01" }.Select(prefix => Array.FindIndex(library.platforms, p => p.source.name.StartsWith(prefix))).ToList();
            int hub = count == 3 ? Array.FindIndex(library.platforms, p => p.source.name.StartsWith("D1_19")) : Array.FindIndex(library.platforms, p => p.regions.Length > 2); if (hub < 0) hub = order[0];
            order.Remove(hub); order.Insert(0, hub);
            layout.rooms.Add(new Room { platform = hub, position = new Vector3(0, 22, 0), yaw = random.Next(360), used = new bool[library.platforms[hub].regions.Length] });
            for (int next = 1; next < count; next++)
            {
                bool added = false;
                // Fill authored junctions before extending their branches. Give every open socket
                // its own angle/candidate budget, so a blocked socket cannot monopolize the search.
                var frontier = Enumerable.Range(0, layout.rooms.Count)
                    .SelectMany(i => Enumerable.Range(0, layout.rooms[i].used.Length).Where(p => !layout.rooms[i].used[p])
                        .Select(p => (room: i, port: p)))
                    .OrderByDescending(socket => layout.rooms[socket.room].used.Length > 2)
                    .ThenBy(socket => socket.room).ThenBy(socket => socket.port).ToArray();
                const int socketAttempts = 240;
                int priorityAttempts = frontier.Length * socketAttempts;
                // Junctions placed at the final room would have no budget left for their exits.
                // Try them early while there is room to grow, then fall back to other shapes.
                var junctionCandidates = Enumerable.Range(next, order.Count - next)
                    .Where(i => library.platforms[order[i]].regions.Length > 2 &&
                        count - next >= library.platforms[order[i]].regions.Length).ToArray();
                for (int attempt = 0; attempt < priorityAttempts + 700 && !added; attempt++)
                {
                    if (slice.ElapsedMilliseconds >= 6) { yield return null; slice.Restart(); }
                    int candidateIndex = junctionCandidates.Length > 0 && attempt < priorityAttempts && attempt % socketAttempts < 90
                        ? junctionCandidates[random.Next(junctionCandidates.Length)]
                        : attempt == 0 ? next : next + random.Next(order.Count - next);
                    int candidatePlatform = order[candidateIndex]; var definition = library.platforms[candidatePlatform];
                    if (frontier.Length == 0) break;
                    var socket = frontier[attempt < priorityAttempts ? attempt / socketAttempts : random.Next(frontier.Length)];
                    int parentIndex = socket.room; var parent = layout.rooms[parentIndex];
                    int portA = socket.port, portB = random.Next(definition.regions.Length);
                    var ra = library.platforms[parent.platform].regions[portA]; var rb = definition.regions[portB];
                    if (ra.stone != rb.stone) { Reject("connector type"); continue; }
                    if (ra.stone && ra.stairDirection == rb.stairDirection) { Reject("stair rise direction"); continue; }
                    float aa = attempt == 0 ? ra.heading : Mathf.Lerp(ra.MinAngle, ra.MaxAngle, (float)random.NextDouble());
                    float ab = attempt == 0 ? rb.heading : Mathf.Lerp(rb.MinAngle, rb.MaxAngle, (float)random.NextDouble());
                    if (!library.platforms[parent.platform].TryAnchor(ra, aa, out var anchorA) || !definition.TryAnchor(rb, ab, out var anchorB)) { Reject("contact"); continue; }
                    // Angle() uses mathematical XZ angles; Unity yaw rotates in the opposite direction.
                    float yaw = parent.yaw + ab - aa - 180;
                    var direction = Rotate(Direction(aa), parent.yaw);
                    int stairCount = ra.stone ? 1 + random.Next(Mathf.Min(ra.maxStairCount, rb.maxStairCount)) : 0;
                    float pitch = 0, length, rise;
                    if (ra.stone)
                    {
                        var pieces = ConnectorPieces(library, stairCount);
                        length = pieces.Sum(p => new Vector2(p.Span.x, p.Span.z).magnitude);
                        rise = pieces.Sum(p => Mathf.Abs(p.Span.y)) * ra.stairDirection;
                        pitch = Mathf.Atan2(rise, length) * Mathf.Rad2Deg;
                    }
                    else
                    {
                        float pitchMin = Mathf.Max(ra.pitch - ra.pitchTolerance, -rb.pitch - rb.pitchTolerance);
                        float pitchMax = Mathf.Min(ra.pitch + ra.pitchTolerance, -rb.pitch + rb.pitchTolerance);
                        if (pitchMax < pitchMin) { Reject("pitch"); continue; }
                        pitch = Mathf.Lerp(pitchMin, pitchMax, (float)random.NextDouble());
                        length = library.timber.Span.magnitude * Mathf.Cos(pitch * Mathf.Deg2Rad);
                        rise = library.timber.Span.magnitude * Mathf.Sin(pitch * Mathf.Deg2Rad);
                    }
                    var start = parent.position + Rotate(anchorA, parent.yaw);
                    var end = start + direction * length + Vector3.up * rise;
                    var room = new Room { platform = candidatePlatform, position = end - Rotate(anchorB, yaw), yaw = yaw, used = new bool[definition.regions.Length] };
                    if (Mathf.Abs(room.position.y - 22) > 12) continue;
                    var hull = WorldHull(library, room);
                    float shortfall = 0;
                    if (layout.rooms.Any(r => Overlap(WorldHull(library, r), hull, .4f)))
                    {
                        // Keep a small near-miss as an explicitly marked review candidate; never stretch a connector.
                        if (attempt < priorityAttempts && attempt % socketAttempts < 200) { Reject("floor bounds " + definition.source.name); continue; }
                        room.position += direction * .65f; hull = WorldHull(library, room); shortfall = .65f;
                        if (layout.rooms.Any(r => Overlap(WorldHull(library, r), hull, .4f))) { Reject("floor bounds " + definition.source.name); continue; }
                    }
                    if (layout.links.Any(l => SegmentCross(XZ(start), XZ(end), XZ(l.start), XZ(l.end)))) continue;
                    bool crossing = false;
                    for (int k = 1; k < 20; k++)
                    {
                        var p = Vector3.Lerp(start, end, k / 20f);
                        for (int i = 0; i < layout.rooms.Count; i++) if (i != parentIndex && Inside(WorldHull(library, layout.rooms[i]), XZ(p))) crossing = true;
                    }
                    if (crossing) continue;
                    if (!ApproachClear(library.platforms[parent.platform], anchorA, Direction(aa)) || !ApproachClear(definition, anchorB, Direction(ab))) { Reject("approach " + definition.source.name); continue; }
                    if (layout.links.Any(l => PassageHitsRoom(library, room, l.start, l.end))) continue;
                    if (layout.rooms.Any(r => PassageHitsRoom(library, r, start, end))) continue;
                    parent.used[portA] = true; room.used[portB] = true; layout.rooms.Add(room);
                    order[candidateIndex] = order[next]; order[next] = candidatePlatform;
                    layout.links.Add(new Link { a = parentIndex, b = next, portA = portA, portB = portB, angleA = aa, angleB = ab, start = start, end = end, stone = ra.stone, pitch = pitch, stairCount = stairCount, stairDirection = ra.stairDirection, reviewShortfall = shortfall }); added = true;
                }
                if (!added) break;
            }
            furthest = Mathf.Max(furthest, layout.rooms.Count);
            if (layout.rooms.Count == count)
            {
                var junctions = layout.rooms.Where(r => r.used.Length > 2).ToArray();
                int deficit = junctions.Sum(r => r.used.Count(u => !u));
                int reviews = layout.links.Count(l => l.reviewShortfall > 0);
                if (deficit < bestJunctionDeficit || (deficit == bestJunctionDeficit && reviews < bestReviews))
                { bestLayout = layout; bestJunctionDeficit = deficit; bestReviews = reviews; }
                // A small map has too few neighbours to fill every exit. Respect that limit;
                // otherwise compare a few complete arrangements before leaving junctions unused.
                int unavoidable = Mathf.Max(0, junctions.Sum(r => r.used.Length) - (count - 1 + Mathf.Max(0, junctions.Length - 1)));
                if (bestJunctionDeficit <= unavoidable || ++completeCandidates >= 4) { complete(bestLayout); yield break; }
            }
        }
        if (bestLayout != null) { complete(bestLayout); yield break; }
        var failure = new InvalidOperationException("No layout satisfies the fixed connector dimensions and allowed joint angles for this seed."); failure.Data["LayoutConnectivity"] = true; throw failure;
    }

    public static CavePlatformLibrary.Connector[] ConnectorPieces(CavePlatformLibrary library, int stairs)
    {
        switch (stairs)
        {
            case 0: return new[] { library.timber };
            case 1: return new[] { library.singleStair };
            case 2: return new[] { library.doubleStair };
            case 3: return new[] { library.doubleStair, library.singleStair };
            default: throw new ArgumentOutOfRangeException(nameof(stairs));
        }
    }

}
}
