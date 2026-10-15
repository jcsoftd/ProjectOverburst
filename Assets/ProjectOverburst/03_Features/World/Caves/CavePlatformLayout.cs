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
    protected static bool Inside(Vector2[] hull, Vector2 p)
    {
        for (int i = 0; i < hull.Length; i++)
            if (!(Cross(hull[(i + 1) % hull.Length] - hull[i], p - hull[i]) >= -.001f)) return false;
        return true;
    }
    protected static float EdgeDistance(Vector2[] hull, Vector2 p) => Enumerable.Range(0, hull.Length).Min(i =>
    { var a = hull[i]; var d = hull[(i + 1) % hull.Length] - a; return Vector2.Distance(p, a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude)); });
    protected static Vector2[] WorldHull(CavePlatformLibrary library, Room room)
    {
        var source = library.platforms[room.platform].hull; var result = new Vector2[source.Length];
        var rotation = Quaternion.Euler(0, room.yaw, 0);
        for (int i = 0; i < source.Length; i++) result[i] = XZ(room.position + rotation * new Vector3(source[i].x, 0, source[i].y));
        return result;
    }
    protected static bool Overlap(Vector2[] a, Vector2[] b, float margin)
    {
        for (int side = 0; side < 2; side++)
        {
            var polygon = side == 0 ? a : b;
            for (int i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[(i + 1) % polygon.Length] - polygon[i]; var axis = new Vector2(-edge.y, edge.x).normalized;
                float amin = float.PositiveInfinity, amax = float.NegativeInfinity;
                float bmin = float.PositiveInfinity, bmax = float.NegativeInfinity;
                for (int j = 0; j < a.Length; j++) { float p = Vector2.Dot(a[j], axis); amin = Mathf.Min(amin, p); amax = Mathf.Max(amax, p); }
                for (int j = 0; j < b.Length; j++) { float p = Vector2.Dot(b[j], axis); bmin = Mathf.Min(bmin, p); bmax = Mathf.Max(bmax, p); }
                if (amax + margin < bmin || bmax + margin < amin) return false;
            }
        }
        return true;
    }
    protected static bool SegmentCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        => Cross(b - a, c - a) * Cross(b - a, d - a) < 0 && Cross(d - c, a - c) * Cross(d - c, b - c) < 0;
    public const int MaxLayoutAttempts = 3;
    public static System.Collections.IEnumerator Solve(CavePlatformLibrary library, int count, int seed, float spacing, Func<CavePlatformLibrary.Platform, Vector3, bool> blocked, Action<Layout> complete, Action<int> onAttempt = null)
    {
        for (int attempt = 0; attempt < MaxLayoutAttempts; attempt++)
        {
            onAttempt?.Invoke(attempt + 1);
            // Disjoint deterministic seed ranges: each search already tries 30 seeds spaced by 7919.
            var search = SolveOnce(library, count, unchecked(seed + attempt * 30 * 7919), spacing, blocked, complete);
            try
            {
                while (true)
                {
                    bool next;
                    try { next = search.MoveNext(); }
                    catch (InvalidOperationException error) when (error.Data["LayoutConnectivity"] is bool connectivity && connectivity)
                    {
                        if (attempt == MaxLayoutAttempts - 1) throw;
                        break;
                    }
                    if (!next) yield break;
                    yield return search.Current;
                }
            }
            finally { (search as IDisposable)?.Dispose(); }
        }
    }
    static System.Collections.IEnumerator SolveOnce(CavePlatformLibrary library, int count, int seed, float spacing, Func<CavePlatformLibrary.Platform, Vector3, bool> blocked, Action<Layout> complete)
    {
        if (!library || library.platforms == null || library.platforms.Length < 2 || count < 2 || count > 24) throw new ArgumentException("Use 2–24 rooms.");
        if (!Mathf.Approximately(spacing, 1)) throw new ArgumentException("Connection distances follow the original bridge/stair dimensions; spacing must stay 1.");
        if (library.timber == null || library.singleStair == null || library.doubleStair == null) throw new InvalidOperationException("Refresh the connector library first.");
        var slice = System.Diagnostics.Stopwatch.StartNew();
        bool ApproachClear(CavePlatformLibrary.Platform p, Vector3 a, Vector3 d)
        { for(float n=-1.2f;n<2;n+=.4f) if(blocked(p,a+d*n)) return false; return true; }
        bool PassageHitsRoom(CavePlatformLibrary l, Room r, Vector3 a, Vector3 b)
        { for(float t=0;t<=1;t+=.05f) if(blocked(l.platforms[r.platform],Rotate(Vector3.Lerp(a,b,t)-r.position,-r.yaw))) return true; return false; }
        // Peripheral decorations may reduce usable area, but may not split an otherwise connected combat floor.
        var clearFloors = new Dictionary<CavePlatformLibrary.Platform, FloorGraph>();
        bool InvadesFloor(Room floorRoom, Room obstacleRoom)
        {
            var definition = library.platforms[floorRoom.platform];
            if (!clearFloors.TryGetValue(definition, out var graph))
                clearFloors.Add(definition, graph = new FloorGraph(definition, blocked));
            var rotation = Quaternion.Euler(0, -obstacleRoom.yaw, 0) * Quaternion.Euler(0, floorRoom.yaw, 0);
            var offset = Quaternion.Euler(0, -obstacleRoom.yaw, 0) * (floorRoom.position - obstacleRoom.position);
            return graph.Splits(p => blocked(library.platforms[obstacleRoom.platform], offset + rotation * p));
        }
        var rejected = new Dictionary<string, int>(); int furthest = 0;
        Layout bestLayout = null; int bestJunctionDeficit = int.MaxValue, bestReviews = int.MaxValue, completeCandidates = 0;
        void Reject(string reason) { rejected.TryGetValue(reason, out int n); rejected[reason] = n + 1; }
        for (int restart = 0; restart < 30; restart++)
        {
            var random = new System.Random(unchecked(seed + restart * 7919)); var layout = new Layout();
            var order = Enumerable.Range(0, Mathf.Max(count, library.platforms.Length)).Select(i => i % library.platforms.Length).OrderBy(i => random.Next()).ToList();
            if (count == 3) order = new[] { "D1_19", "D1_06", "D1_01" }.Select(prefix => Array.FindIndex(library.platforms, p => p.source.name.StartsWith(prefix))).ToList();
            int hub = count == 3 ? Array.FindIndex(library.platforms, p => p.source.name.StartsWith("D1_19")) : Array.FindIndex(library.platforms, p => p.regions.Length > 2); if (hub < 0) hub = order[0];
            order.Remove(hub); order.Insert(0, hub);
            layout.rooms.Add(new Room { platform = hub, position = new Vector3(0, 22, 0), yaw = random.Next(360), used = new bool[library.platforms[hub].regions.Length] });
            // Accepted rooms never move during this search; reuse their transformed footprints.
            var placedHulls = new Dictionary<Room, Vector2[]> { [layout.rooms[0]] = WorldHull(library, layout.rooms[0]) };
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
                    int portA = socket.port;
                    var ra = library.platforms[parent.platform].regions[portA];
                    bool Compatible(CavePlatformLibrary.Region candidate) => ra.stone == candidate.stone && (!ra.stone || ra.stairDirection != candidate.stairDirection);
                    int compatibleCount = 0;
                    foreach (var region in definition.regions) if (Compatible(region)) compatibleCount++;
                    if (compatibleCount == 0) { Reject("connector type"); continue; }
                    int choice = random.Next(compatibleCount), portB = 0;
                    for (; portB < definition.regions.Length; portB++)
                        if (Compatible(definition.regions[portB]) && choice-- == 0) break;
                    var rb = definition.regions[portB];
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
                    if (layout.rooms.Any(r => Overlap(placedHulls[r], hull, .4f)))
                    {
                        // Keep a small near-miss as an explicitly marked review candidate; never stretch a connector.
                        if (attempt < priorityAttempts && attempt % socketAttempts < 200) { Reject("floor bounds " + definition.source.name); continue; }
                        room.position += direction * .65f; hull = WorldHull(library, room); shortfall = .65f;
                        if (layout.rooms.Any(r => Overlap(placedHulls[r], hull, .4f))) { Reject("floor bounds " + definition.source.name); continue; }
                    }
                    if (layout.links.Any(l => SegmentCross(XZ(start), XZ(end), XZ(l.start), XZ(l.end)))) continue;
                    bool crossing = false;
                    for (int k = 1; k < 20; k++)
                    {
                        var p = Vector3.Lerp(start, end, k / 20f);
                        for (int i = 0; i < layout.rooms.Count; i++) if (i != parentIndex && Inside(placedHulls[layout.rooms[i]], XZ(p))) crossing = true;
                    }
                    if (crossing) continue;
                    if (!ApproachClear(library.platforms[parent.platform], anchorA, Direction(aa)) || !ApproachClear(definition, anchorB, Direction(ab))) { Reject("approach " + definition.source.name); continue; }
                    if (PassageHitsRoom(library, room, start, end) || layout.links.Any(l => PassageHitsRoom(library, room, l.start, l.end))) continue;
                    if (layout.rooms.Any(r => PassageHitsRoom(library, r, start, end))) continue;
                    if (layout.rooms.Any(r => InvadesFloor(r, room) || InvadesFloor(room, r))) { Reject("neighbour combat floor"); continue; }
                    if (ra.stone)
                    {
                        float? Floor(bool fromA, Vector3 near)
                        {
                            var placed = fromA ? parent : room;
                            if (!library.platforms[placed.platform].Surface(Rotate(near - placed.position, -placed.yaw), out var point)) return null;
                            return placed.position.y + point.y;
                        }
                        float width = ConnectorPieces(library, stairCount).Min(p => p.width);
                        if (StairWalkPoints(start, end, width, StairDeckPoints(library, stairCount, start, end), Floor) == null) { Reject("stair walk slope"); continue; }
                    }
                    parent.used[portA] = true; room.used[portB] = true; layout.rooms.Add(room);
                    placedHulls.Add(room, hull);
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

    sealed class FloorGraph
    {
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<int[]> neighbours = new List<int[]>();
        readonly List<int[]> components = new List<int[]>();
        public FloorGraph(CavePlatformLibrary.Platform platform, Func<CavePlatformLibrary.Platform, Vector3, bool> blocked)
        {
            var cells = new Dictionary<Vector2Int, int>();
            var boundary = platform.prefab.GetComponent<CavePlatformBoundary>();
            int left = Mathf.FloorToInt(platform.hull.Min(p => p.x)), right = Mathf.CeilToInt(platform.hull.Max(p => p.x));
            int bottom = Mathf.FloorToInt(platform.hull.Min(p => p.y)), top = Mathf.CeilToInt(platform.hull.Max(p => p.y));
            for (int z = bottom; z < top; z++) for (int x = left; x < right; x++)
            {
                var p = new Vector3(x + .5f, platform.spawn.y, z + .5f);
                if (boundary && !boundary.ContainsLocal(p)) continue;
                if (!platform.Surface(p, out var floor) || blocked(platform, floor)) continue;
                cells.Add(new Vector2Int(x, z), points.Count); points.Add(floor);
            }
            for (int i = 0; i < points.Count; i++) neighbours.Add(null);
            foreach (var cell in cells)
            {
                var edges = new List<int>();
                foreach (var step in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                    if (cells.TryGetValue(cell.Key + step, out int i) && Mathf.Abs(points[i].y - points[cell.Value].y) <= .5f) edges.Add(i);
                neighbours[cell.Value] = edges.ToArray();
            }
            var visited = new bool[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                if (visited[i]) continue;
                var members = new List<int>(); var pending = new Queue<int>(); pending.Enqueue(i); visited[i] = true;
                while (pending.Count > 0) { int n = pending.Dequeue(); members.Add(n); foreach (int next in neighbours[n]) if (!visited[next]) { visited[next] = true; pending.Enqueue(next); } }
                if (members.Count >= 6) components.Add(members.ToArray());
            }
        }
        public bool Splits(Func<Vector3, bool> obstructed)
        {
            var occupied = new bool[points.Count]; bool any = false;
            for (int i = 0; i < points.Count; i++) { occupied[i] = obstructed(points[i]); any |= occupied[i]; }
            if (!any) return false;
            var visited = new bool[points.Count]; var pending = new Queue<int>();
            foreach (var component in components)
            {
                int first = -1, available = 0;
                foreach (int i in component) if (!occupied[i]) { first = i; available++; }
                if (available < 6) return true;
                int reached = 0; pending.Enqueue(first); visited[first] = true;
                while (pending.Count > 0) { int n = pending.Dequeue(); reached++; foreach (int next in neighbours[n]) if (!occupied[next] && !visited[next]) { visited[next] = true; pending.Enqueue(next); } }
                if (reached != available) return true;
            }
            return false;
        }
    }

    public static List<Vector3> StairDeckPoints(CavePlatformLibrary library, int count, Vector3 start, Vector3 end)
    {
        var result = new List<Vector3>(); var cursor = start;
        var direction = end - start; direction.y = 0; direction.Normalize();
        foreach (var piece in ConnectorPieces(library, count))
        {
            if (piece.walkProfile == null || piece.walkProfile.Length < 2) throw new InvalidOperationException("Refresh stair walk profiles before generation.");
            bool reverse = Mathf.Sign(piece.Span.y) != Mathf.Sign(end.y - start.y);
            var entry = reverse ? piece.exit : piece.entry; var exit = reverse ? piece.entry : piece.exit;
            var scale = piece.prefab.transform.localScale; var span = Vector3.Scale(exit - entry, scale);
            var rotation = Quaternion.LookRotation(direction) * Quaternion.Inverse(Quaternion.LookRotation(new Vector3(span.x, 0, span.z)));
            var position = cursor - rotation * Vector3.Scale(entry, scale);
            foreach (var point in reverse ? piece.walkProfile.Reverse() : piece.walkProfile)
                result.Add(position + rotation * Vector3.Scale(point, scale));
            cursor = position + rotation * Vector3.Scale(exit, scale);
        }
        return result.OrderBy(p => Vector3.Dot(p - start, direction)).ToList();
    }

    public static Vector3[] StairWalkPoints(Vector3 start, Vector3 end, float width, IList<Vector3> deck, Func<bool, Vector3, float?> floor)
    {
        var direction = end - start; direction.y = 0;
        float length = direction.magnitude;
        if (length < .01f || deck == null || deck.Count < 2) return null;
        direction /= length;
        var side = new Vector3(-direction.z, 0, direction.x);
        float startHeight = floor(true, start) ?? start.y, endHeight = floor(false, end) ?? end.y;
        const float approach = 2f, footprint = .15f, clearance = .02f, maxRisePerMetre = .97f;
        int count = Mathf.CeilToInt((length + approach * 2) / .1f), index = 0;
        float step = (length + approach * 2) / count;
        var points = new Vector3[count + 1];
        for (int i = 0; i <= count; i++)
        {
            float along = -approach + step * i;
            var near = start + direction * along;
            float height = along < 0 ? startHeight : along > length ? endHeight : Mathf.Lerp(start.y, end.y, along / length);
            if (along >= 0 && along <= length)
            {
                while (index < deck.Count - 2 && Vector3.Dot(deck[index + 1] - start, direction) < along) index++;
                float a = Vector3.Dot(deck[index] - start, direction), b = Vector3.Dot(deck[index + 1] - start, direction);
                height = Mathf.Lerp(deck[index].y, deck[index + 1].y, Mathf.InverseLerp(a, b, along));
            }
            bool fromA = along < length * .5f;
            float anchor = fromA ? startHeight : endHeight;
            for (int lane = -2; lane <= 2; lane++)
            {
                var supported = floor(fromA, near + side * (lane * width * .25f) + direction * (fromA ? -footprint : footprint));
                if (supported.HasValue && Mathf.Abs(supported.Value - anchor) < .5f) height = Mathf.Max(height, supported.Value);
            }
            points[i] = near; points[i].y = height + clearance;
        }
        // The smallest slope-limited upper surface follows real treads without intersecting them or creating vertical risers.
        for (int i = 1; i <= count; i++) points[i].y = Mathf.Max(points[i].y, points[i - 1].y - step * maxRisePerMetre);
        for (int i = count - 1; i >= 0; i--) points[i].y = Mathf.Max(points[i].y, points[i + 1].y - step * maxRisePerMetre);
        if (points[0].y > startHeight + .15f || points[count].y > endHeight + .15f) return null;
        return points;
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
