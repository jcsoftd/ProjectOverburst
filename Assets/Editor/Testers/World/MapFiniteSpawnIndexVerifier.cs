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
