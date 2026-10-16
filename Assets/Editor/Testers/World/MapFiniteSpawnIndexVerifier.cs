using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.DebugTools.Performance;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MapFiniteSpawnIndexVerifier
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, Fields).SetValue(value, data);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Fields).Invoke(value, args);
    static List<object> List(object value, string name) => ((IEnumerable)Get(value, name)).Cast<object>().ToList();
    static MapRunRoster.Member Member(object slot) => (MapRunRoster.Member)Get(slot, "Member");

    public static void VerifyRuntime(MapSpawnBudget budget, Action<bool, string> check)
    {
        foreach (var field in budget.Fields) VerifyField(field, check);
        check(budget.Fields.Sum(f => f.FiniteActiveCount) == budget.GetComponentInParent<RunWorldGate>().Roster.Active,
            "Finite indices: scene active count equals logical active leases");
    }
    static void VerifyField(MapMonsterField field, Action<bool, string> check)
    {
        var slots = List(field, "slots"); var active = List(field, "activeSlots"); var pending = List(field, "spawnSlots");
        var reserves = List(field, "reserves"); string prefix = "Finite indices " + field.FiniteArea.Ground.Index + ": ";
        check(active.Count == field.FiniteActiveCount && active.Distinct().Count() == active.Count,
            prefix + "one active entry per health binding");
        check(active.Select((s, i) => (int)Get(s, "ActiveIndex") == i && Member(s).State == MapRunRoster.Life.Active).All(x => x),
            prefix + "active indices track swap removals and logical state");
        check(slots.Count(s => Member(s).State == MapRunRoster.Life.Active) == active.Count,
            prefix + "logical active members are neither omitted nor duplicated");
        check(pending.Distinct().Count() == pending.Count && pending.All(s => (bool)Get(s, "SpawnQueued")
            && (bool)Get(s, "Released") && Member(s).State == MapRunRoster.Life.Pending), prefix + "spawn queue contains only released pending IDs");
        check(slots.Count(s => (bool)Get(s, "Released") && Member(s).State == MapRunRoster.Life.Pending) == pending.Count,
            prefix + "spawn queue contains every released pending ID");
        check(pending.Select(s => (int)Get(s, "Order")).SequenceEqual(pending.Select(s => (int)Get(s, "Order")).OrderBy(i => i)),
            prefix + "round-robin queue retains authored slot order");
        check((int)Get(field, "releasedReserves") == pending.Count(s => !Member(s).Initial), prefix + "reserve pending counter matches queue");
        check((int)Get(field, "initialRemaining") == slots.Count(s => Member(s).Initial && !(bool)Get(s, "EverSpawned")),
            prefix + "initial completion counter matches first-spawn state");
        check(reserves.Count == slots.Count(s => !(bool)Get(s, "Released") && Member(s).State == MapRunRoster.Life.Pending)
            && reserves.Distinct().Count() == reserves.Count, prefix + "unreleased reserve membership preserved");
    }

    static Func<Vector3, float> OuterDistance(MapCombatGeometry.Area area)
    {
        var source = Get(area, "<Source>k__BackingField");
        var boundary = (Overburst.Caves.CavePlatformBoundary)Get(source, "boundary");
        if (!boundary)
        {
            var place = (Overburst.Mojave.MojavePlace)Get(source, "place");
            return p => Mathf.Abs(place.Distance(new Vector2(p.x, p.z)));
        }
        bool Inside(Vector3 p, Vector3[] loop)
        {
            bool inside = false;
            for (int i = 0, j = loop.Length - 1; i < loop.Length; j = i++)
                if ((loop[i].z > p.z) != (loop[j].z > p.z)
                    && p.x < (loop[j].x - loop[i].x) * (p.z - loop[i].z) / (loop[j].z - loop[i].z) + loop[i].x) inside = !inside;
            return inside;
        }
        var loops = boundary.Data.loops; var segments = new List<(Vector2 a, Vector2 b)>();
        foreach (var loop in loops)
        {
            int depth = loops.Count(other => other != loop && Inside(loop.points[0], other.points));
            if (depth % 2 != 0) continue;
            for (int i = 0; i < loop.points.Length; i++)
            {
                var a = boundary.transform.TransformPoint(loop.points[i]); var b = boundary.transform.TransformPoint(loop.points[(i + 1) % loop.points.Length]);
                segments.Add((new Vector2(a.x,a.z),new Vector2(b.x,b.z)));
            }
        }
        return point =>
        {
            float best = float.PositiveInfinity; var p = new Vector2(point.x,point.z);
            foreach (var edge in segments)
            {
                var d = edge.b - edge.a; float t = Mathf.Clamp01(Vector2.Dot(p - edge.a,d) / Mathf.Max(.00001f,d.sqrMagnitude));
                best = Mathf.Min(best,Vector2.Distance(p,edge.a + d * t));
            }
            return best;
        };
    }
    public static void VerifyOuterCache(MapSpawnBudget budget, Action<bool, string> check, string output)
    {
        var evidence = new List<object>();
        foreach (var field in budget.Fields)
        {
            var area = field.FiniteArea; if (area.Count == 0) continue;
            var distance = OuterDistance(area.Ground); var cached = (List<Vector3>)Get(field,"edgePoints");
            float maximum = cached.Count > 0 ? cached.Max(distance) : float.PositiveInfinity;
            var cells = new HashSet<Vector2Int>(area.Ground.Points.Select(p => new Vector2Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.z))));
            int rejectedInterior = area.Ground.Points.Count(p =>
            {
                var c = new Vector2Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.z));
                return (!cells.Contains(c + Vector2Int.right) || !cells.Contains(c + Vector2Int.left)
                    || !cells.Contains(c + Vector2Int.up) || !cells.Contains(c + Vector2Int.down)) && distance(p) > MapCombatGeometry.CellSize * 1.5f;
            });
            evidence.Add(new { area.Ground.Index, area.Ground.Name, cached = cached.Count, maximumOuterDistance = maximum, legacyInteriorCandidates = rejectedInterior });
            File.WriteAllText(Path.Combine(output,"outer-cache.json"),JsonConvert.SerializeObject(evidence,Formatting.Indented));
            check(cached.Count > 0 && maximum <= MapCombatGeometry.CellSize * 1.5f,
                "Outer cache follows the authored platform rim, excluding internal passability holes: " + area.Ground.Index);
        }
    }
    public static void VerifyVisiblePositions(MapMonsterField field, Action<bool, string> check, string output)
    {
        var distance = OuterDistance(field.FiniteArea.Ground); var evidence = new List<object>(); bool valid = true;
        foreach (var slot in List(field,"slots"))
        {
            var member = Member(slot); if (member.Initial || member.State != MapRunRoster.Life.Active || !field.TryGetActor(member.Index,out var actor)) continue;
            float radius = (float)Get(slot,"Radius"), rim = distance(actor.transform.position);
            valid &= rim <= MapCombatGeometry.CellSize * 1.5f + radius + .6f;
            var position = actor.transform.position;
            evidence.Add(new { member.Id, rimDistance = rim, radius, position = new { position.x, position.y, position.z } });
        }
        File.WriteAllText(Path.Combine(output,"visible-outer-positions.json"),JsonConvert.SerializeObject(evidence,Formatting.Indented));
        check(valid && evidence.Count > 0,"Camera-visible actual reserve actors stay at the true outer rim with body inset");
    }

    public static void VerifyDeferredBoundary(MapMonsterField field, int memberIndex, Vector3 player, Action<bool, string> check, string output)
    {
        var slot = List(field, "slots").Single(s => Member(s).Index == memberIndex);
        // Reproduce the state immediately after the last offscreen candidate consumed the frame budget.
        Set(slot, "SearchRemaining", 0);
        if ((int)Get(slot, "Cursor") == 0) Set(slot, "Cursor", 1);
        int cursor = (int)Get(slot, "Cursor");
        var args = new object[] { slot, player, false, System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency, Vector3.zero };
        bool found = (bool)Call(field, "TryFinitePositionWithinBudget", args);
        var result = new { found, deferred = (bool)Get(slot, "PositionDeferred"), remaining = (int)Get(slot, "SearchRemaining"),
            cursorBefore = cursor, cursorAfter = (int)Get(slot, "Cursor") };
        File.WriteAllText(Path.Combine(output, "budget-boundary-" + memberIndex + ".json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        check(!result.found && result.deferred && result.remaining == 0 && result.cursorBefore == result.cursorAfter,
            "Completed offscreen pass survives an exhausted budget without restarting the floor scan");
    }
    public static object RunEdgeGeometry(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))
            throw new InvalidOperationException("Use unguarded idle EditMode.");
        string output = CombatPerformancePaths.RequireOutput(Path.GetFullPath(directory)); Directory.CreateDirectory(output);
        var checks = new List<string>(); var evidence = new List<object>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            for (int shape = 0; shape < 3; shape++)
            {
                var root = new GameObject("Owned outer-rim fixture"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var boundary = root.AddComponent<Overburst.Caves.CavePlatformBoundary>();
                var outer = shape == 2 ? new[] { new Vector3(0,0,0),new Vector3(25,0,0),new Vector3(25,0,13),new Vector3(13,0,13),new Vector3(13,0,25),new Vector3(0,0,25) }
                    : new[] { Vector3.zero,new Vector3(25,0,0),new Vector3(25,0,25),new Vector3(0,0,25) };
                boundary.loops = shape == 1 ? new[] { new Overburst.Caves.CavePlatformBoundary.Loop { points = outer },
                    new Overburst.Caves.CavePlatformBoundary.Loop { points = new[] { new Vector3(11,0,11),new Vector3(14,0,11),new Vector3(14,0,14),new Vector3(11,0,14) } } }
                    : new[] { new Overburst.Caves.CavePlatformBoundary.Loop { points = outer } };
                var points = new List<Vector3>();
                for (int z = 0; z < 25; z++) for (int x = 0; x < 25; x++)
                {
                    if (shape < 2 && (x >= 11 && x <= 13 && z >= 11 && z <= 13 || x == 12 && z <= 10)) continue;
                    if (shape == 2 && (x >= 13 && z >= 13 || x >= 5 && x <= 7 && z >= 5 && z <= 7)) continue;
                    points.Add(new Vector3(x + .5f,0,z + .5f));
                }
                var area = new MapCombatGeometry.Area(0,"Outer-rim fixture",new Vector3(12.5f,0,12.5f),0,points);
                var sourceType = typeof(MapCombatGeometry).GetNestedType("Source",BindingFlags.NonPublic);
                var source = Activator.CreateInstance(sourceType,true); Set(source,"boundary",boundary);
                Set(source,"bounds",new Bounds(area.Center,new Vector3(25,0,25))); Set(area,"<Source>k__BackingField",source);
                var watch = System.Diagnostics.Stopwatch.StartNew(); var edge = MapCombatGeometry.EdgePoints(area); watch.Stop();
                int internalCandidates = shape < 2 ? edge.Count(p => p.x > .5f && p.x < 24.5f && p.z > .5f && p.z < 24.5f)
                    : edge.Count(p => p.x > 3 && p.x < 10 && p.z > 3 && p.z < 10);
                evidence.Add(new { shape, candidates = edge.Count, internalCandidates, buildMilliseconds = watch.Elapsed.TotalMilliseconds });
                File.WriteAllText(Path.Combine(output,"geometry-probe.json"),JsonConvert.SerializeObject(evidence,Formatting.Indented));
                Check(edge.Count > 0 && internalCandidates == 0,"Outer rim excludes internal obstacles, open obstacle corridors and enclosed floor holes: " + shape);
                if (shape == 2) Check(edge.Contains(new Vector3(12.5f,0,18.5f)),"Concave outer boundary remains a valid rim");
            }
            var result = new { status="PASS", checks, evidence }; File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(result,Formatting.Indented)); return result;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static object RunContinuationNative(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))
            throw new InvalidOperationException("Use unguarded idle EditMode.");
        string output = CombatPerformancePaths.RequireOutput(Path.GetFullPath(directory)); Directory.CreateDirectory(output);
        var checks = new List<object>();
        void Check(bool passed, string label) { checks.Add(new { passed, label }); }
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var theme = MapThemeCatalog.Resolve("PrimalHunt"); var map = new MapInstanceState { monsterThemeId = theme.ThemeId };
            MapGenerationPolicy.Initialize(map,"continuation-test"); map.terrain = MapTerrainKind.Cave; map.areaCount = 4; map.seed = 48310;
            map.width = map.depth = 0; map.desertCandidateId = null;
            var areas = Enumerable.Range(0,4).Select(i => new MapCombatGeometry.Area(i,"Continuation fixture",Vector3.zero,0,
                Enumerable.Repeat(Vector3.zero,1000).ToList())).ToList();
            var sourceType = typeof(MapCombatGeometry).GetNestedType("Source",BindingFlags.NonPublic);
            foreach (var area in areas) Set(area,"<Source>k__BackingField",Activator.CreateInstance(sourceType,true));
            var roster = MapRunRoster.Build("continuation",map,areas);
            var root = new GameObject("Owned continuation fixture"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var budget = root.AddComponent<MapSpawnBudget>(); var field = root.AddComponent<MapMonsterField>();
            Call(field,"ConfigureFinite",roster,roster.Areas[0],budget);
            Set(field,"edgePoints",Enumerable.Repeat(Vector3.zero,8).ToList());
            var slot = List(field,"slots").First(s => !Member(s).Initial);
            Set(slot,"Radius",.01f); Set(slot,"Height",.1f);
            void Optional(string name, object value) { slot.GetType().GetField(name,Fields)?.SetValue(slot,value); }
            var groupType = typeof(MapMonsterField).GetNestedType("SpawnGroup",BindingFlags.NonPublic);
            var group = Activator.CreateInstance(groupType,true); Set(group,"Ground",areas[0]); Set(group,"Point",Vector3.zero);
            Set(slot,"Group",group); Set(slot,"GroupCursor",23); Optional("GroupRemaining",1);
            Set(slot,"Cursor",1); Set(slot,"SearchRemaining",-1); Set(slot,"EverSpawned",false);
            var expired = new object[] { slot,Vector3.zero,false,System.Diagnostics.Stopwatch.GetTimestamp()-System.Diagnostics.Stopwatch.Frequency,Vector3.zero };
            Call(field,"TryFinitePositionWithinBudget",expired);
            Check((int)Get(slot,"GroupCursor")==23 && (bool)Get(slot,"PositionDeferred"),"Expired group continuation consumes no candidate");
            Call(field,"TryFinitePositionWithinBudget",slot,Vector3.zero,false,0L,Vector3.zero);
            int groupAfter=(int)Get(slot,"GroupCursor");
            Check(groupAfter==24,"Only the last outstanding group candidate is retried before floor/edge fallback");
            Set(slot,"Group",null); Set(slot,"EverSpawned",true); Set(slot,"SearchRemaining",0); Set(slot,"EdgeCursor",7); Optional("EdgeRemaining",1);
            Call(field,"TryFinitePositionWithinBudget",expired);
            Check((int)Get(slot,"EdgeCursor")==7 && (int)Get(slot,"SearchRemaining")==0,"Expired edge continuation consumes no candidate or completed floor pass");
            Call(field,"TryFinitePositionWithinBudget",slot,Vector3.zero,false,0L,Vector3.zero);
            int edgeAfter=(int)Get(slot,"EdgeCursor");
            Check(edgeAfter==8 && (int)Get(slot,"SearchRemaining")==-1,"Last invalid edge candidate completes the deferred pass and reopens offscreen search");
            Set(field,"approaches",Enumerable.Repeat(Vector3.zero,8).ToList());
            int Scan(object candidate, bool returned)
            {
                Set(candidate,"EverSpawned",returned); Set(candidate,"Cursor",1); Set(candidate,"SearchRemaining",-1); Set(candidate,"Group",null);
                Call(field,"TryFinitePositionWithinBudget",candidate,Vector3.zero,true,0L,Vector3.zero);
                return (int)Get(candidate,"Cursor")-1;
            }
            int freshCount=Scan(slot,false), returnedCount=Scan(slot,true);
            var initialSlot=List(field,"slots").First(s=>Member(s).Initial); Set(initialSlot,"Radius",.01f); Set(initialSlot,"Height",.1f);
            int initialCount=Scan(initialSlot,false);
            Check(freshCount==1000,"New reserves never scan connection approaches");
            Check(returnedCount==1008,"Living returns retain connection approaches");
            Check(initialCount==1008,"Initial road packs retain connection approaches");
            bool passed=checks.All(c=>(bool)c.GetType().GetProperty("passed").GetValue(c));
            var result=new {status=passed?"PASS":"FAIL",checks,groupAfter,edgeAfter,freshCount,returnedCount,initialCount};
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(result,Formatting.Indented));
            if (!passed) throw new InvalidOperationException("Continuation regression failed; inspect result.json.");
            return result;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static void VerifyReserveFloor(MapSpawnBudget budget, Action<bool,string> check, string output)
    {
        foreach (var field in budget.Fields.Where(f=>f.FiniteArea.Count>f.FiniteArea.InitialCount))
        {
            var area=field.FiniteArea.Ground;
            var member=budget.GetComponentInParent<RunWorldGate>().Roster.Members.Where(m=>m.AreaIndex==area.Index && !m.Initial)
                .OrderBy(m=>MapCombatGeometry.BodyFor(m.Definition).Radius).First();
            var body=MapCombatGeometry.BodyFor(member.Definition); var player=PlayerContext.Instance.CurrentActor.transform.position;
            var path=new UnityEngine.AI.NavMeshPath(); var overlaps=new Collider[32];
            foreach (var point in field.FiniteApproaches)
            {
                if (Vector3.Distance(point,player)<MapCombatGeometry.EntrySafety+body.Radius+1
                    || budget.Fields.Any(f=>MapCombatGeometry.Contains(f.FiniteArea.Ground,point))
                    || !MapCombatGeometry.TryRuntimePoint(area,member.Definition,point,true,overlaps,path,out var road)
                    || !(bool)Call(budget,"IsSeparated",road,body.Radius)) continue;
                var original=Get(field,"approaches");
                try
                {
                    Set(field,"approaches",new List<Vector3>{point});
                    var slot=Activator.CreateInstance(typeof(MapMonsterField).GetNestedType("Slot",BindingFlags.NonPublic),true);
                    Set(slot,"Member",member); Set(slot,"Radius",body.Radius); Set(slot,"Height",body.Height);
                    int total=area.Points.Count+1, cursor=(area.Points.Count-member.Index*17%total+total)%total;
                    Set(slot,"Cursor",cursor); Set(slot,"EverSpawned",true);
                    var args=new object[]{slot,player,true,Vector3.zero}; bool restored=(bool)Call(field,"TryFinitePosition",args);
                    bool restoredOnRoad=restored && !MapCombatGeometry.Contains(area,(Vector3)args[3]);
                    Set(slot,"Cursor",cursor); Set(slot,"SearchRemaining",-1); Set(slot,"EverSpawned",false);
                    args[3]=Vector3.zero; bool fresh=(bool)Call(field,"TryFinitePosition",args);
                    bool freshOnFloor=!fresh || MapCombatGeometry.Contains(area,(Vector3)args[3]);
                    File.WriteAllText(Path.Combine(output,"reserve-floor.json"),JsonConvert.SerializeObject(new { area.Index,restoredOnRoad,fresh,freshOnFloor },Formatting.Indented));
                    check(restoredOnRoad,"Real connected road remains available to a living return");
                    check(freshOnFloor,"A new reserve rejects that same real connection and stays on its own platform");
                    return;
                }
                finally { Set(field,"approaches",original); }
            }
        }
        check(false,"A physically valid connection midpoint is required for the reserve-floor regression");
    }

    public static object RunNative(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))
            throw new InvalidOperationException("Use unguarded idle EditMode.");
        string output = CombatPerformancePaths.RequireOutput(Path.GetFullPath(directory));
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var theme = MapThemeCatalog.Resolve("PrimalHunt");
            var map = new MapInstanceState { monsterThemeId = theme.ThemeId };
            MapGenerationPolicy.Initialize(map, "finite-index-test"); map.terrain = MapTerrainKind.Cave; map.areaCount = 4; map.seed = 48310;
            map.width = map.depth = 0; map.desertCandidateId = null;
            foreach (string id in new[] { MapOptionPolicy.MoreSmall, MapOptionPolicy.MoreMedium, MapOptionPolicy.MoreElite })
                map.options.Add(new MapOptionRoll { optionId = id, value = 4 });
            var areas = new List<MapCombatGeometry.Area>();
            for (int i = 0; i < 4; i++) areas.Add(new MapCombatGeometry.Area(i, "Index fixture", Vector3.right * (i * 100), Math.Min(i, 2),
                Enumerable.Range(0, 1000).Select(n => new Vector3(i * 100 + n % 40, 0, n / 40)).ToList()));
            var sourceType = typeof(MapCombatGeometry).GetNestedType("Source", BindingFlags.NonPublic);
            foreach (var area in areas) Set(area, "<Source>k__BackingField", Activator.CreateInstance(sourceType, true));
            var roster = MapRunRoster.Build("finite-index-test", map, areas);
            Set(roster, "<PlacementReady>k__BackingField", true);
            var root = new GameObject("Finite index fixture"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var budget = root.AddComponent<MapSpawnBudget>();
            var field = new GameObject("Finite index court").AddComponent<MapMonsterField>(); field.transform.SetParent(root.transform);
            Call(field, "ConfigureFinite", roster, roster.Areas[0], budget);
            VerifyField(field, Check);
            var slots = List(field, "slots"); var first = slots.First(s => Member(s).Initial);
            int pending = List(field, "spawnSlots").Count, initial = (int)Get(field, "initialRemaining");
            Call(field, "QueueSpawn", first); Call(field, "QueueSpawn", first);
            Check(List(field, "spawnSlots").Count == pending, "Duplicate release does not duplicate pending IDs");
            Call(field, "DequeueSpawn", first); Call(field, "DequeueSpawn", first);
            Check(List(field, "spawnSlots").Count == pending - 1, "Duplicate dequeue is idempotent");
            Check(roster.TryActivate(Member(first).Index, out int lease), "Fixture logical lease activated");
            Set(first, "RosterLease", lease); Set(first, "ActiveIndex", 0);
            ((IList)Get(field, "activeSlots")).Add(first);
            Call(field, "ReconcileFinite");
            Check(Member(first).State == MapRunRoster.Life.Pending && List(field, "activeSlots").Count == 0,
                "Missing actor reconciles one living ID without a synthetic death");
            Check(List(field, "spawnSlots").Count == pending && roster.Kills == 0 && (int)Get(field, "initialRemaining") == initial,
                "Unexpected return restores queue and initial demand without kills");
            Call(field, "ReturnSlot", first, false);
            Check(List(field, "spawnSlots").Count == pending, "Repeated return cannot duplicate the same ID");
            VerifyField(field, Check);
            var args = new object[] { first, Vector3.zero, true,
                System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency, Vector3.zero };
            int cursor = (int)Get(first, "Cursor");
            Check(!(bool)Call(field, "TryFinitePositionWithinBudget", args) && (bool)Get(first, "PositionDeferred")
                && (int)Get(first, "Cursor") == cursor, "Expired budget defers without consuming a placement candidate");
            Check(roster.Kills == 0 && List(field, "spawnSlots").Count == pending, "Budget deferral preserves logical state and retry queue");
            Set(first, "EverSpawned", true);
            Set(field, "edgePoints", Enumerable.Range(0, 8).Select(i => Vector3.right * (i * .1f)).ToList());
            VerifyDeferredBoundary(field, Member(first).Index, Vector3.zero, Check, output);
            int completedCursor = (int)Get(first, "Cursor");
            Call(field, "TryFinitePositionWithinBudget", first, Vector3.zero, false, 0L, Vector3.zero);
            Check((int)Get(first, "Cursor") == completedCursor && (int)Get(first, "EdgeCursor") > 0,
                "Next budget resumes edge candidates without repeating completed floor candidates");
            // Compile invocations once; reflection/boxing stays outside the measured hot path.
            var expressionField = System.Linq.Expressions.Expression.Constant(field);
            var expressionSlot = System.Linq.Expressions.Expression.Constant(first, first.GetType());
            var position = System.Linq.Expressions.Expression.Variable(typeof(Vector3));
            Action Probe(bool preparing, long started, bool forceEdgeSort)
            {
                var call = System.Linq.Expressions.Expression.Call(expressionField,
                    typeof(MapMonsterField).GetMethod("TryFinitePositionWithinBudget", Fields), expressionSlot,
                    System.Linq.Expressions.Expression.Constant(Vector3.zero), System.Linq.Expressions.Expression.Constant(preparing),
                    System.Linq.Expressions.Expression.Constant(started), position);
                var work = new List<System.Linq.Expressions.Expression>();
                if (forceEdgeSort)
                {
                    work.Add(System.Linq.Expressions.Expression.Assign(System.Linq.Expressions.Expression.Field(expressionField, "edgeSortPlayer"),
                        System.Linq.Expressions.Expression.Constant(new Vector3(float.PositiveInfinity, 0, 0))));
                    work.Add(System.Linq.Expressions.Expression.Assign(System.Linq.Expressions.Expression.Field(expressionSlot, "SearchRemaining"), System.Linq.Expressions.Expression.Constant(1)));
                }
                work.Add(call); work.Add(System.Linq.Expressions.Expression.Empty());
                return System.Linq.Expressions.Expression.Lambda<Action>(System.Linq.Expressions.Expression.Block(new[] { position }, work)).Compile();
            }
            long Allocations(Action action, int iterations)
            {
                using (var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc", 8192,
                    Unity.Profiling.ProfilerRecorderOptions.StartImmediately | Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    if (!recorder.Valid) throw new InvalidOperationException("GC.Alloc recorder is unavailable.");
                    for (int i = 0; i < iterations; i++) action();
                    recorder.Stop(); return recorder.ToArray().Sum(sample => sample.Count);
                }
            }
            var expiredProbe = Probe(true, (long)args[3], false);
            for (int i = 0; i < 32; i++) expiredProbe();
            long positiveControlEvents = Allocations(() => { var allocation = new byte[1024]; GC.KeepAlive(allocation); }, 1);
            long expiredProbeAllocationEvents = Allocations(expiredProbe, 1000);
            // Synthetic edges stay inside player safety, avoiding physics against the unbound fixture source.
            Set(field, "edgePoints", Enumerable.Range(0, 8).Select(i => Vector3.right * (i * .1f)).ToList());
            Set(first, "EverSpawned", true);
            var edgeProbe = Probe(false, 0, true);
            for (int i = 0; i < 32; i++) edgeProbe();
            long edgeProbeAllocationEvents = Allocations(edgeProbe, 1000);
            File.WriteAllText(Path.Combine(output, "hot-path-allocation.json"), JsonConvert.SerializeObject(new
                { iterations = 1000, positiveControlEvents, expiredProbeAllocationEvents, edgeProbeAllocationEvents,
                    scope = "GC.Alloc events on the current thread after warmup: budget deferral and forced 8-point edge sort/safety rejection. Excludes physical search and whole-frame allocations." }, Formatting.Indented));
            Check(positiveControlEvents > 0, "GC.Alloc recorder detects a deliberate positive-control allocation");
            Check(expiredProbeAllocationEvents == 0, "Expired-budget placement adds no managed allocation after warmup");
            Check(edgeProbeAllocationEvents == 0, "Repeated edge sorting and safety rejection add no managed allocation after warmup");
            Call(field, "RefreshFinite", Vector3.zero, false, System.Diagnostics.Stopwatch.GetTimestamp());
            Check(!field.HasTriggered, "Entering an untouched platform does not start reinforcement");
            Call(field, "PrepareReinforcement", Vector3.zero, 0, false);
            Check(List(field, "spawnSlots").All(s => Member(s).Initial), "Untouched initial population cannot release reserves");
            foreach (var s in slots.Where(s => Member(s).Initial)) Set(s, "EverSpawned", true);
            Set(field, "initialRemaining", 0); Set(field, "triggered", true);
            var unreleased = List(field, "reserves");
            int total = roster.Total;
            Call(field, "PrepareReinforcement", Vector3.zero, 0, false);
            var released = List(field, "spawnSlots").Where(s => !Member(s).Initial).ToArray();
            Check(released.Length > 0 && released.Length <= MapCombatGeometry.GroupSize(Member(unreleased[0]).Tier)
                && released.All(s => Member(s).Definition == Member(unreleased[0]).Definition), "Reserve queue releases only the first same-species group");
            int releasedCount = released.Length;
            Call(field, "PrepareReinforcement", Vector3.zero, 0, false);
            Check((int)Get(field, "releasedReserves") == releasedCount && roster.Total == total && roster.Kills == 0,
                "Outstanding reserve group prevents duplicate reinforcement");
            VerifyField(field, Check);
            var result = new { status = "PASS_NATIVE", checks, total, fieldPopulation = slots.Count };
            File.WriteAllText(Path.Combine(output, "native-result.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            return result;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
