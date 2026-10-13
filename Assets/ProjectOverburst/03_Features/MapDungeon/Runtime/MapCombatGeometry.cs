using System;
using System.Collections;
using System.Collections.Generic;
using Overburst.Caves;
using Overburst.Mojave;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Grid integration of connected, unobstructed floor. Bounds only limit the search, never the population.
public static class MapCombatGeometry
{
    public const float CellSize = 1f;
    public const float EntrySafety = 4f;
    public const float BodyGap = .05f;
    public static int GroupSize(EnemyThemeTier tier) => tier == EnemyThemeTier.Small ? 5 : tier == EnemyThemeTier.Medium ? 2 : 1;
    // Planning and live placement must agree at the same packed-body boundary.
    // The tolerance absorbs float rounding; the 5cm body gap remains intact.
    public static bool BodiesSeparated(Vector3 a, float radiusA, Vector3 b, float radiusB)
    {
        var delta = a - b; delta.y = 0;
        float space = radiusA + radiusB + BodyGap;
        return delta.sqrMagnitude + .000001f >= space * space;
    }
    public static int BandForDistance(int distance, int farthest)
        => distance <= 0 ? 0 : Mathf.Min(2, (distance * 3 - 1) / Mathf.Max(1, farthest));
    public sealed class Area
    {
        public int Index { get; }
        public string Name { get; }
        public Vector3 Center { get; }
        public int Band { get; }
        public float SquareMetres => Points.Count * CellSize * CellSize;
        public IReadOnlyList<Vector3> Points { get; }
        internal Source Source { get; }
        public Area(int index, string name, Vector3 center, int band, IList<Vector3> points)
            : this(index, name, center, band, points, null) { }
        internal Area(int index, string name, Vector3 center, int band, IList<Vector3> points, Source source)
        {
            Index = index; Name = name; Center = center; Band = band;
            Points = new List<Vector3>(points).AsReadOnly(); Source = source;
        }
    }
    public readonly struct Body
    {
        public readonly float Radius, Height;
        public Body(float radius, float height) { Radius = radius; Height = Mathf.Max(height, 2 * radius); }
    }
    internal sealed class Source
    {
        public int index, band;
        public string name;
        public Vector3 center;
        public Bounds bounds;
        public Scene scene;
        public CaveWorld cave;
        public CavePlatformBoundary boundary;
        public MojaveWorld desert;
        public MojavePlace place;
        public CavePlatformLibrary spawnLibrary;
        public CavePlatformLibrary.Platform spawnPlatform;
        public int BakedBodyIndex(EnemyDefinition definition, Body body)
        {
            if (!spawnLibrary || spawnPlatform == null || spawnLibrary.spawnBodies == null
                || spawnLibrary.spawnBodies.Length > 63 || boundary.transform.lossyScale != Vector3.one) return -1;
            for (int i = 0; i < spawnLibrary.spawnBodies.Length; i++)
            {
                var shape = spawnLibrary.spawnBodies[i];
                if (shape == null || shape.definitions == null || Mathf.Abs(shape.radius - body.Radius) > .00001f
                    || Mathf.Abs(shape.height - body.Height) > .00001f) continue;
                foreach (var candidate in shape.definitions) if (candidate == definition) return i;
            }
            return -1;
        }
        public readonly List<Vector3> contacts = new List<Vector3>();
        public bool Floor(Vector3 near, float margin, out Vector3 floor)
        {
            floor = default;
            if (cave)
            {
                if (!boundary.Sample(near, 3, 3, out var surface)
                    || !cave.Ground(surface, out var hit, .4f) || Mathf.Abs(hit.point.y - surface.y) > .3f) return false;
                floor = hit.point;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4;
                    var edge = floor + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * margin;
                    if (!boundary.Sample(edge, .55f, .55f, out _)) return false;
                }
                return true;
            }
            var p = new Vector2(near.x, near.z);
            if (place.Distance(p) > -margin || !desert.IsWalkable(p, margin)) return false;
            // Overlapping authored courts contribute each square metre once.
            for (int i = 0; i < index; i++) if (desert.layout.places[i].Distance(p) <= 0) return false;
            floor = desert.Ground(p); return true;
        }
    }
    public static Body BodyFor(EnemyDefinition definition)
    {
        var actor = definition ? definition.ActorPrefab : null;
        var capsule = actor && actor.CollisionRoot ? actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>(true) : null;
        if (!capsule || capsule.direction != 1) throw new InvalidOperationException("몬스터 배치 충돌체가 유효하지 않습니다: " + (definition ? definition.EnemyId : "null"));
        var scale = Vector3.Scale(capsule.transform.lossyScale, definition.ResolveRuntimeStats().CollisionScale);
        return new Body(capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)), capsule.height * Mathf.Abs(scale.y));
    }
    static bool Clear(Source source, Vector3 point, Body body, Collider[] overlaps, bool ignoreActors = false)
    {
        float radius = body.Radius + .04f;
        int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * (radius + .12f),
            point + Vector3.up * Mathf.Max(radius + .12f, body.Height - radius), radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++)
            if (overlaps[i] && overlaps[i].gameObject.scene == source.scene
                && (!ignoreActors || !overlaps[i].GetComponentInParent<EnemyActor>())) return false;
        return true;
    }
    // Before bridge links, choose the largest connected clear floor component, not the first nearby rock-top polygon.
    public static bool TryNavigationCenter(CaveWorld world, CaveWorld.Court court, out Vector3 center)
    {
        center = default;
        var boundary = court.tile.GetComponent<CavePlatformBoundary>();
        if (!boundary || boundary.Data.loops.Length == 0) return false;
        var bounds = new Bounds(court.center, Vector3.zero);
        foreach (var loop in boundary.Data.loops) foreach (var p in loop.points) bounds.Encapsulate(boundary.transform.TransformPoint(p));
        var source = new Source { cave = world, boundary = boundary, scene = world.gameObject.scene };
        var representatives = new List<Vector3>(); var nearest = new List<Vector3>(); var counts = new List<int>();
        var path = new NavMeshPath(); var overlaps = new Collider[32];
        for (int z = Mathf.FloorToInt(bounds.min.z); z < Mathf.CeilToInt(bounds.max.z); z++)
            for (int x = Mathf.FloorToInt(bounds.min.x); x < Mathf.CeilToInt(bounds.max.x); x++)
            {
                var candidate = new Vector3(x + .5f, court.center.y, z + .5f);
                if (!source.Floor(candidate, .4f, out var floor)
                    || !NavMesh.SamplePosition(floor, out var nav, .18f, NavMesh.AllAreas)
                    || !Clear(source, floor, new Body(.35f, 1.8f), overlaps)) continue;
                int group = 0;
                for (; group < representatives.Count; group++)
                    if (NavMesh.CalculatePath(representatives[group], nav.position, NavMesh.AllAreas, path)
                        && path.status == NavMeshPathStatus.PathComplete) break;
                if (group == representatives.Count) { representatives.Add(nav.position); nearest.Add(nav.position); counts.Add(0); }
                counts[group]++;
                if ((nav.position - court.center).sqrMagnitude < (nearest[group] - court.center).sqrMagnitude) nearest[group] = nav.position;
            }
        int best = -1;
        for (int i = 0; i < counts.Count; i++) if (best < 0 || counts[i] > counts[best]) best = i;
        if (best < 0 || counts[best] < 6) return false;
        center = nearest[best]; return true;
    }
    static bool SafeDistance(Source source, Vector3 floor, Vector3 entry)
    {
        var away = floor - entry; away.y = 0;
        if (away.sqrMagnitude < EntrySafety * EntrySafety) return false;
        foreach (var p in source.contacts)
        { var delta = floor - p; delta.y = 0; if (delta.sqrMagnitude < 1.25f * 1.25f) return false; }
        return true;
    }
    public static bool CanPlace(Area area, EnemyDefinition definition, Vector3 point, bool ignoreActors = false)
    {
        if (area?.Source == null) return false;
        var body = BodyFor(definition);
        return area.Source.Floor(point, body.Radius + .1f, out var floor) && Mathf.Abs(floor.y - point.y) <= .3f
            && Clear(area.Source, floor, body, new Collider[32], ignoreActors);
    }
    public static bool Contains(Area area, Vector3 point)
        => area?.Source != null && area.Source.Floor(point, 0, out var floor) && Mathf.Abs(floor.y - point.y) < 3;

    public static bool TryCompletionPoint(Area area, Vector3 near, float radius,
        Collider[] overlaps, NavMeshPath path, out Vector3 point)
    {
        point = default;
        if (area?.Source == null || !area.Source.Floor(near, radius, out point)
            || !Clear(area.Source, point, new Body(radius, 2.5f), overlaps, true)
            || !NavMesh.SamplePosition(point, out var nav, .25f, NavMesh.AllAreas)
            || !NavMesh.CalculatePath(nav.position, area.Center, NavMesh.AllAreas, path)
            || path.status != NavMeshPathStatus.PathComplete) return false;
        point = nav.position; return true;
    }

    // Existing installed connections supply walk-in positions on fully visible small platforms.
    public static List<Vector3> Approaches(Area area)
    {
        var result = new List<Vector3>(); var source = area.Source;
        void Add(Vector3 a, Vector3 b)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 1.5f));
            for (int i = 0; i < steps; i++)
            {
                var p = Vector3.Lerp(a, b, (float)i / steps);
                if ((p - area.Center).sqrMagnitude <= 65 * 65) result.Add(p);
            }
        }
        if (source.cave)
            foreach (var passage in source.cave.passages)
            {
                if (passage.a != area.Index && passage.b != area.Index) continue;
                for (int i = 1; i < passage.points.Length; i++) Add(passage.points[i - 1], passage.points[i]);
            }
        if (source.desert)
            foreach (var trail in source.desert.layout.trails)
            {
                if (trail.from != area.Index && trail.to != area.Index) continue;
                for (int i = 1; i < trail.points.Length; i++) Add(source.desert.Ground(trail.points[i - 1]), source.desert.Ground(trail.points[i]));
            }
        return result;
    }
    public static bool TryRuntimePoint(Area area, EnemyDefinition definition, Vector3 desired, bool approach,
        Collider[] overlaps, NavMeshPath path, out Vector3 point)
    {
        point = default; var source = area.Source; var body = BodyFor(definition);
        if (source == null) return false;
        if (!approach)
        { if (!source.Floor(desired, body.Radius + .1f, out point)) return false; }
        else if (source.cave)
        {
            if (!source.cave.Ground(desired, out var hit) || !source.cave.CanStand(hit.point, body.Radius + .1f)) return false;
            point = hit.point;
        }
        else
        {
            var xz = new Vector2(desired.x, desired.z);
            if (!source.desert.IsWalkable(xz, body.Radius + .1f)) return false;
            point = source.desert.Ground(xz);
        }
        if (!Clear(source, point, body, overlaps, true)
            || !NavMesh.SamplePosition(point, out var nav, .25f, NavMesh.AllAreas)
            || !NavMesh.CalculatePath(nav.position, area.Center, NavMesh.AllAreas, path)
            || path.status != NavMeshPathStatus.PathComplete) return false;
        return true;
    }
    public static IEnumerator Prepare(RunWorldGate gate, CaveWorld cave, MojaveWorld desert, Vector3 entry)
    {
        if (gate == null || (cave == null) == (desert == null)) throw new ArgumentException("One prepared terrain is required.");
        var sources = new List<Source>();
        var edges = new List<Vector2Int>();
        if (cave)
        {
            var generator = cave.GetComponent<CaveRuntimeGenerator>();
            var library = generator && generator.assets ? generator.assets.library : null;
            for (int i = 0; i < cave.courts.Count; i++)
            {
                var court = cave.courts[i]; var boundary = court.tile.GetComponent<CavePlatformBoundary>();
                if (!boundary || boundary.Data.loops.Length == 0) throw new InvalidOperationException("전투 발판 경계가 없습니다.");
                var bounds = new Bounds(court.center, Vector3.zero);
                foreach (var loop in boundary.Data.loops) foreach (var p in loop.points) bounds.Encapsulate(boundary.transform.TransformPoint(p));
                var source = new Source { index = i, name = court.name, center = court.center, bounds = bounds,
                    scene = cave.gameObject.scene, cave = cave, boundary = boundary };
                if (library && !string.IsNullOrEmpty(library.spawnBakeSignature) && library.spawnBodies != null
                    && boundary.transform.lossyScale == Vector3.one)
                    foreach (var platform in library.platforms)
                        if (platform.source == boundary.Data.gameObject && platform.spawnPoints != null && platform.spawnPoints.Length > 0)
                        { source.spawnLibrary = library; source.spawnPlatform = platform; break; }
                sources.Add(source);
            }
            foreach (var passage in cave.passages)
            {
                edges.Add(new Vector2Int(passage.a, passage.b));
                sources[passage.a].contacts.Add(passage.points[0]);
                sources[passage.b].contacts.Add(passage.points[passage.points.Length - 1]);
            }
        }
        else
        {
            var prepared = desert.GetComponent<DesertRunWorld>();
            for (int i = 0; i < desert.layout.places.Count; i++)
            {
                var place = desert.layout.places[i];
                float radius = Mathf.Max(place.radius.x, place.radius.y) * 1.5f;
                var center = prepared.AreaCenters[i];
                sources.Add(new Source { index = i, name = place.name, center = center,
                    bounds = new Bounds(new Vector3(place.center.x, center.y, place.center.y), new Vector3(2 * radius, 6, 2 * radius)),
                    scene = desert.gameObject.scene, desert = desert, place = place });
            }
            foreach (var trail in desert.layout.trails) edges.Add(new Vector2Int(trail.from, trail.to));
        }
        int entryArea = 0;
        for (int i = 1; i < sources.Count; i++) if ((sources[i].center - entry).sqrMagnitude < (sources[entryArea].center - entry).sqrMagnitude) entryArea = i;
        var distance = new int[sources.Count]; for (int i = 0; i < distance.Length; i++) distance[i] = -1;
        var queue = new Queue<int>(); queue.Enqueue(entryArea); distance[entryArea] = 0; int farthest = 0;
        while (queue.Count > 0)
        {
            int from = queue.Dequeue();
            foreach (var edge in edges)
            {
                int to = edge.x == from ? edge.y : edge.y == from ? edge.x : -1;
                if (to < 0 || distance[to] >= 0) continue;
                distance[to] = distance[from] + 1; farthest = Mathf.Max(farthest, distance[to]); queue.Enqueue(to);
            }
        }
        var areas = new List<Area>(); var path = new NavMeshPath(); var overlaps = new Collider[32];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var source in sources)
        {
            if (distance[source.index] < 0) throw new InvalidOperationException("연결되지 않은 전투 구역입니다: " + source.name);
            gate.PreparationStage = "전투 바닥 확인 " + (source.index + 1) + "/" + sources.Count;
            source.band = BandForDistance(distance[source.index], farthest);
            var points = new List<Vector3>();
            int left = Mathf.FloorToInt(source.bounds.min.x / CellSize), right = Mathf.CeilToInt(source.bounds.max.x / CellSize);
            int bottom = Mathf.FloorToInt(source.bounds.min.z / CellSize), top = Mathf.CeilToInt(source.bounds.max.z / CellSize);
            for (int z = bottom; z < top; z++) for (int x = left; x < right; x++)
            {
                if (watch.Elapsed.TotalMilliseconds >= 3) { yield return null; watch.Restart(); }
                var candidate = new Vector3((x + .5f) * CellSize, source.center.y, (z + .5f) * CellSize);
                if (!source.Floor(candidate, .4f, out var floor)) continue;
                if (!SafeDistance(source, floor, entry)) continue;
                if (!NavMesh.SamplePosition(floor, out var nav, .18f, NavMesh.AllAreas)
                    || !Clear(source, floor, new Body(.35f, 1.8f), overlaps)
                    || !NavMesh.CalculatePath(source.center, nav.position, NavMesh.AllAreas, path)
                    || path.status != NavMeshPathStatus.PathComplete) continue;
                points.Add(floor);
            }
            if (points.Count < 6) throw new InvalidOperationException("실제 전투 가능한 바닥이 부족합니다: " + source.name + " / " + points.Count + "㎡");
            areas.Add(new Area(source.index, source.name, source.center, source.band, points, source));
        }
        if (desert) gate.BossArena = MapBossArena.Prepare(areas, entry);
        var roster = MapRunRoster.Build(gate.Context.RunId, gate.Map, areas, gate.BossArena?.Ground.Index ?? -1);
        var random = new System.Random(unchecked(gate.Map.seed ^ 0x76B902));
        foreach (var area in roster.Areas)
        {
            gate.PreparationStage = "기본 무리 배치 확인 " + (area.Ground.Index + 1) + "/" + roster.Areas.Count;
            var initial = new List<MapRunRoster.Member>();
            for (int i = 0; i < area.InitialCount; i++) initial.Add(roster.Members[area.FirstMember + i]);
            // Place broad bodies first so small monsters do not consume their only clear patch.
            initial.Sort((a, b) => { int order = BodyFor(b.Definition).Radius.CompareTo(BodyFor(a.Definition).Radius); return order != 0 ? order : a.Index.CompareTo(b.Index); });
            var reserved = new List<Vector3>(); var radii = new List<float>();
            var safeByDefinition = new Dictionary<EnemyDefinition, List<Vector3>>();
            var bakedDefinitions = new HashSet<EnemyDefinition>();
            var bakedPoints = new List<CavePlatformLibrary.SpawnPoint>();
            var groundSource = area.Ground.Source;
            if (groundSource.spawnPlatform != null)
                foreach (var candidate in groundSource.spawnPlatform.spawnPoints)
                {
                    if (watch.Elapsed.TotalMilliseconds >= 3) { yield return null; watch.Restart(); }
                    var point = groundSource.boundary.transform.TransformPoint(candidate.position);
                    if (candidate.bodyMask <= 0 || !SafeDistance(groundSource, point, entry)
                        || !NavMesh.SamplePosition(point, out var nav, .18f, NavMesh.AllAreas)
                        || !NavMesh.CalculatePath(area.Ground.Center, nav.position, NavMesh.AllAreas, path)
                        || path.status != NavMeshPathStatus.PathComplete) continue;
                    bakedPoints.Add(new CavePlatformLibrary.SpawnPoint { position = point, bodyMask = candidate.bodyMask });
                }
            for (int i = 0; i < area.Count; i++)
            {
                var member = roster.Members[area.FirstMember + i];
                if (safeByDefinition.ContainsKey(member.Definition)) continue;
                var body = BodyFor(member.Definition); var safe = new List<Vector3>();
                int bakedBody = groundSource.BakedBodyIndex(member.Definition, body);
                if (bakedBody >= 0)
                    foreach (var point in bakedPoints)
                        if ((point.bodyMask & (1L << bakedBody)) != 0) safe.Add(point.position);
                if (safe.Count > 0) bakedDefinitions.Add(member.Definition);
                else
                {
                    // Missing/changed bodies and unbaked or scaled platforms retain the measured-grid path.
                    // Population uses 1m area cells. Refine positions inside those cells for small bodies.
                    foreach (var cell in area.Ground.Points) for (int sample = 0; sample < 5; sample++)
                    {
                        if (watch.Elapsed.TotalMilliseconds >= 3) { yield return null; watch.Restart(); }
                        var point = cell + (sample == 0 ? Vector3.zero : new Vector3((sample & 1) == 0 ? -.25f : .25f, 0, sample <= 2 ? -.25f : .25f));
                        if (!area.Ground.Source.Floor(point, body.Radius + .1f, out var floor)
                            || !SafeDistance(area.Ground.Source, floor, entry) || !Clear(area.Ground.Source, floor, body, overlaps)
                            || !NavMesh.SamplePosition(floor, out var nav, .18f, NavMesh.AllAreas)
                            || !NavMesh.CalculatePath(area.Ground.Center, nav.position, NavMesh.AllAreas, path)
                            || path.status != NavMeshPathStatus.PathComplete) continue;
                        safe.Add(floor);
                    }
                }
                if (safe.Count == 0) throw new InvalidOperationException(area.Ground.Name + "에 배치 가능한 위치가 없는 몬스터: " + member.Definition.EnemyId);
                for (int j = safe.Count - 1; j > 0; j--) { int k = random.Next(j + 1); (safe[j], safe[k]) = (safe[k], safe[j]); }
                safeByDefinition.Add(member.Definition, safe);
            }
            bool compact = false;
            while (true)
            {
                initial.Sort((a, b) => { int order = BodyFor(b.Definition).Radius.CompareTo(BodyFor(a.Definition).Radius); return order != 0 ? order : a.Index.CompareTo(b.Index); });
                reserved.Clear(); radii.Clear(); MapRunRoster.Member failed = null;
                var groupCounts = new Dictionary<EnemyDefinition, int>();
                foreach (var member in initial)
                {
                    float radius = BodyFor(member.Definition).Radius; bool placed = false;
                    groupCounts.TryGetValue(member.Definition, out int grouped);
                    bool sameGroup = grouped % GroupSize(member.Tier) != 0;
                    var candidates = safeByDefinition[member.Definition];
                    int start = compact || sameGroup ? 0 : random.Next(candidates.Count);
                    for (int candidate = 0; candidate < candidates.Count; candidate++)
                    {
                        var point = candidates[(start + candidate) % candidates.Count];
                        bool blocked = false;
                        for (int i = 0; i < reserved.Count; i++)
                        { if (!BodiesSeparated(point, radius, reserved[i], radii[i])) { blocked = true; break; } }
                        if (blocked) continue;
                        // Only shortlisted baked positions need the live floor/static-collider check.
                        if (bakedDefinitions.Contains(member.Definition) && !CanPlace(area.Ground, member.Definition, point)) continue;
                        member.PlannedPosition = point; reserved.Add(point); radii.Add(radius); placed = true; break;
                    }
                    if (!placed) { failed = member; break; }
                    groupCounts[member.Definition] = grouped + 1;
                    // Prefer nearby safe points within each small group; compact fallback retains crowded-court capacity.
                    if (!compact && !sameGroup)
                    {
                        var anchor = member.PlannedPosition;
                        candidates.Sort((a, b) => { var da = a - anchor; var db = b - anchor; da.y = db.y = 0; return da.sqrMagnitude.CompareTo(db.sqrMagnitude); });
                    }
                    if (watch.Elapsed.TotalMilliseconds >= 3) { yield return null; watch.Restart(); }
                }
                if (failed == null) break;
                if (!compact)
                {
                    // Packing from an edge leaves a contiguous patch for subsequent bodies.
                    foreach (var candidates in safeByDefinition.Values)
                        candidates.Sort((a, b) => { int order = a.z.CompareTo(b.z); return order != 0 ? order : a.x.CompareTo(b.x); });
                    compact = true; continue;
                }
                // A small court may stage its larger members in the finite reinforcement roster instead.
                // Exchange roles only: preserve initial count, all tier counts, IDs and the 95% denominator.
                MapRunRoster.Member replacement = null;
                float largest = BodyFor(initial[0].Definition).Radius;
                for (int i = 0; i < area.Count; i++)
                {
                    var candidate = roster.Members[area.FirstMember + i];
                    if (initial.Contains(candidate) || BodyFor(candidate.Definition).Radius >= largest - .001f) continue;
                    if (replacement == null || BodyFor(candidate.Definition).Radius < BodyFor(replacement.Definition).Radius) replacement = candidate;
                }
                if (replacement == null) throw new InvalidOperationException(area.Ground.Name + "의 기본 배치 공간이 부족합니다: "
                    + area.InitialCount + "마리 / 배치 " + reserved.Count + " / 면적 " + area.Ground.SquareMetres + "㎡ / " + failed.Definition.EnemyId);
                initial[0] = replacement;
                if (watch.Elapsed.TotalMilliseconds >= 3) { yield return null; watch.Restart(); }
            }
            for (int i = 0; i < area.Count; i++)
            {
                var member = roster.Members[area.FirstMember + i]; member.Initial = initial.Contains(member);
                if (member.Initial) continue;
                var candidates = safeByDefinition[member.Definition];
                member.PlannedPosition = candidates[random.Next(candidates.Count)];
            }
        }
        roster.PlacementReady = true;
        if (!gate.SetRoster(roster)) throw new InvalidOperationException("런의 고정 몬스터 명단을 확정하지 못했습니다.");
        gate.PreparationStage = "전투 명단 준비 완료";
    }
}
