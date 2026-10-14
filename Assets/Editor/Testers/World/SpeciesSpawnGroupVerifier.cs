using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Uses the existing isolated map-entry session for boot, failure handling and account/Editor return.
public static class SpeciesSpawnGroupVerifier
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object owner, string name) => owner.GetType().GetField(name, Fields).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Fields).SetValue(owner, value);
    static object[] Rows(object owner, string field) => ((IEnumerable)Read(owner, field)).Cast<object>().ToArray();
    static float Distance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a, b); }
    static float[] Point(Vector3 value) => new[] { value.x, value.y, value.z };
    static void Write(string output, string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
    static void Freeze(EnemyActor actor)
    {
        actor.AI.enabled = false; actor.Movement.StopMovement(); actor.Movement.enabled = false;
    }
    public static string Native(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
        Check(MapCombatGeometry.GroupSize(EnemyThemeTier.Small) == 5 && MapCombatGeometry.GroupSize(EnemyThemeTier.Medium) == 2
            && MapCombatGeometry.GroupSize(EnemyThemeTier.Elite) == 1, "group size 5/2/1");
        var theme = MapThemeCatalog.Resolve("GraveHunt");
        Check(theme && theme.Validate(out _), "actual outdoor theme is valid");
        var node = new GameObject("Species group native queue check"); node.SetActive(false);
        try
        {
            var spawner = node.AddComponent<OutdoorWorldSpawner>(); Set(spawner, "theme", theme);
            foreach (int stage in new[] { 1, 4, 10 }) foreach (var cell in new[] { new Vector2Int(2, 1), new Vector2Int(3, 2) })
            {
                Set(spawner, "stage", stage);
                typeof(OutdoorWorldSpawner).GetMethod("QueueArea", Fields).Invoke(spawner, new object[] { cell });
                var queued = Rows(spawner, "pending");
                int seed = unchecked(73130 + cell.x * 73856093 + cell.y * 19349663 + stage * 83492791);
                var baseline = theme.BuildRoster(stage == 1 ? 41 : 40, 9, stage == 1 ? 0 : 1, seed, stage); var random = new System.Random(seed);
                for (int i = baseline.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (baseline[i], baseline[j]) = (baseline[j], baseline[i]); }
                var expected = baseline.Select(def => new { def, level = AccountWorldLevelCommands.MinimumMonsterLevel(stage) + random.Next(10), seed = random.Next() })
                    .Select(row => row.def.EnemyId + "/" + row.level + "/" + row.seed).OrderBy(s => s).ToArray();
                var actual = queued.Select(row => ((EnemyDefinition)Read(row, "definition")).EnemyId + "/" + Read(row, "level") + "/" + Read(row, "seed")).OrderBy(s => s).ToArray();
                Check(actual.SequenceEqual(expected) && queued.Length == 50, "outdoor stage-filtered species/level/seed multiset: " + stage + "/" + cell);
                Check(queued.GroupBy(row => Read(row, "group")).All(group => group.Select(row => Read(row, "definition")).Distinct().Count() == 1
                    && group.Count() <= MapCombatGeometry.GroupSize(MapSpawnPolicy.Tier(theme, (EnemyDefinition)Read(group.First(), "definition")))), "homogeneous bounded outdoor groups: " + stage + "/" + cell);
                Read(spawner, "pending").GetType().GetMethod("Clear").Invoke(Read(spawner, "pending"), null);
            }
        }
        finally { Object.DestroyImmediate(node); }
        Write(output, "native.json", new { status = "PASS", count = checks.Count, checks });
        return "PASS " + checks.Count;
    }
    public static IEnumerator VerifyOutdoor(string output, Action<bool, string> check)
    {
        var spawner = OutdoorWorldSpawner.Current; var player = PlayerContext.Instance.CurrentActor;
        check(spawner && WorldSessionState.IsHideout, "outdoor real spawner in MainScene");
        var original = player.transform.position;
        var terrain = (Terrain)Read(spawner, "terrain"); var theme = (EnemyThemeTable)Read(spawner, "theme");
        var actors = new List<EnemyActor>(); var positions = new Dictionary<int, Vector3>();
        try
        {
            spawner.ResetForWorldLevel();
            var point = new Vector3(154, 0, 76); point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
            check(!spawner.IsInTown(point), "outdoor sample is outside town");
            ActorTeleportUtility.TeleportSafely(player.transform, point, Quaternion.identity);
            double until = EditorApplication.timeSinceStartup + 40;
            while ((spawner.PendingCount != 0 || spawner.ActiveCount == 0) && EditorApplication.timeSinceStartup < until)
            {
                foreach (var row in Rows(spawner, "leases"))
                {
                    var actor = (EnemyActor)Read(row, "actor"); if (!actor || positions.ContainsKey(actor.GetInstanceID())) continue;
                    Freeze(actor); actors.Add(actor); positions.Add(actor.GetInstanceID(), actor.transform.position);
                }
                check(spawner.ActiveCount <= (int)Read(spawner, "aliveLimit"), "outdoor active limit");
                yield return null;
            }
            foreach (var row in Rows(spawner, "leases"))
            {
                var actor = (EnemyActor)Read(row, "actor"); if (!actor || positions.ContainsKey(actor.GetInstanceID())) continue;
                Freeze(actor); actors.Add(actor); positions.Add(actor.GetInstanceID(), actor.transform.position);
            }
            int expectedCount = (int)Read(spawner, "smallCount") + (int)Read(spawner, "mediumCount") + (int)Read(spawner, "eliteCount");
            check(spawner.VisitedCount == 1 && actors.Count == expectedCount && spawner.PendingCount == 0, "outdoor actual cell fully spawns original " + expectedCount + " members");
            var local = point - terrain.transform.position; float size = (float)Read(spawner, "areaSize");
            var cell = new Vector2Int(Mathf.FloorToInt(local.x / size), Mathf.FloorToInt(local.z / size));
            int seed = unchecked(73130 + cell.x * 73856093 + cell.y * 19349663 + spawner.AppliedStage * 83492791);
            int elite = spawner.AppliedStage < 2 || spawner.AppliedStage == 2 && (seed & 1) != 0 ? 0 : (int)Read(spawner, "eliteCount");
            var baseline = theme.BuildRoster((int)Read(spawner, "smallCount") + (int)Read(spawner, "eliteCount") - elite,
                (int)Read(spawner, "mediumCount"), elite, seed, spawner.AppliedStage);
            check(actors.Select(a => a.Definition.EnemyId).OrderBy(s => s).SequenceEqual(baseline.Select(d => d.EnemyId).OrderBy(s => s)), "outdoor actual species counts match stage-filtered roster");
            var samples = actors.Where(a => MapSpawnPolicy.Tier(theme, a.Definition) == EnemyThemeTier.Small)
                .Select(a => actors.Where(b => b != a && b.Definition == a.Definition).Min(b => Distance(positions[a.GetInstanceID()], positions[b.GetInstanceID()]))).ToArray();
            float ratio = samples.Count(d => d <= 5f) / (float)samples.Length;
            Write(output, "outdoor-groups.json", new { cell = cell.ToString(), count = actors.Count, nearbySameSmallRatio = ratio,
                nearestSameMean = samples.Average(), members = actors.Select(a => new { enemy = a.Definition.EnemyId, position = Point(positions[a.GetInstanceID()]) }) });
            check(ratio >= .8f, "outdoor actual small monsters clustered: same species within 5m for " + ratio.ToString("P0"));
            Capture(output, "outdoor-groups.png", positions.Values.ToArray());
        }
        finally
        {
            ActorTeleportUtility.TeleportSafely(player.transform, original, Quaternion.identity);
            spawner.ResetForWorldLevel();
        }
        yield return Frames(5);
        check(spawner.ActiveCount == 0 && spawner.PendingCount == 0, "outdoor own sample returned at town");
    }
    public static IEnumerator VerifyMap(RunWorldGate gate, string output, Action<bool, string> check)
    {
        string label = gate.Roster.Terrain.ToString(); var roster = gate.Roster; var budget = gate.SpawnBudget;
        var player = PlayerContext.Instance.CurrentActor; var theme = MapThemeCatalog.Resolve(roster.ThemeId);
        var baseline = MapRunRoster.Build(roster.RunId, gate.Map, roster.Areas.Select(a => a.Ground).ToArray(), gate.BossArena?.Ground.Index ?? -1, roster.EntryAreaIndex);
        check(roster.Total == baseline.Total && roster.Target == baseline.Target && roster.Members.Zip(baseline.Members, (a, b) => a.Id == b.Id && a.Definition == b.Definition && a.Tier == b.Tier).All(ok => ok), label + " unchanged exact natural IDs/species/tier/total/target");
        var initialDistances = new List<float>(); var points = new List<Vector3>(); int actualInitial = 0;
        foreach (var field in budget.Fields)
        {
            var initial = roster.Members.Skip(field.FiniteArea.FirstMember).Take(field.FiniteArea.Count).Where(m => m.Initial).ToArray();
            foreach (var species in initial.GroupBy(m => m.Definition))
            {
                var ordered = species.OrderBy(m => m.Index).ToArray(); int size = MapCombatGeometry.GroupSize(ordered[0].Tier);
                for (int i = 0; i < ordered.Length; i++)
                {
                    if (i % size != 0) initialDistances.Add(Distance(ordered[i].PlannedPosition, ordered[i - i % size].PlannedPosition));
                    if (!field.TryGetActor(ordered[i].Index, out var actor)) continue;
                    Freeze(actor); actualInitial++; points.Add(actor.transform.position);
                    check(Distance(actor.transform.position, ordered[i].PlannedPosition) < 1.5f, label + " actual initial actor follows grouped position " + ordered[i].Index);
                }
            }
        }
        float initialRatio = initialDistances.Count(d => d <= 6f) / (float)initialDistances.Count;
        Write(output, label + "-initial-groups.json", new { roster.Total, roster.Target, actualInitial, groupedWithin6mRatio = initialRatio,
            meanDistance = initialDistances.Average(), members = roster.Members.Where(m => m.Initial).Select(m => new { m.Index, m.AreaIndex, enemy = m.Definition.EnemyId, position = Point(m.PlannedPosition) }) });
        check(actualInitial > 0 && initialRatio >= .8f, label + " initial same-species groups within 6m: " + initialRatio.ToString("P0"));
        Capture(output, label + "-initial-groups.png", points.ToArray());
        int before = roster.Kills;
        var active = roster.Members.Where(m => m.State == MapRunRoster.Life.Active).ToArray();
        foreach (var member in active)
            if (budget.Fields[member.AreaIndex].TryGetActor(member.Index, out var enemy))
            {
                var hit = new DamageInfo(1000000, enemy.transform.position, player.gameObject, suppressDefaultHitVfx: true);
                enemy.Health.TakeDamage(hit); enemy.Health.TakeDamage(hit);
            }
        check(roster.Kills == before + active.Length, label + " each natural death counts once");
        var spawned = new Dictionary<int, object>(); var groups = new Dictionary<object, List<object>>();
        double until = EditorApplication.timeSinceStartup + 45; bool limits = true;
        while (EditorApplication.timeSinceStartup < until)
        {
            foreach (var field in budget.Fields) foreach (var slot in Rows(field, "slots"))
            {
                var member = (MapRunRoster.Member)Read(slot, "Member");
                if (member.Initial || member.State != MapRunRoster.Life.Active || spawned.ContainsKey(member.Index)) continue;
                var actor = (EnemyActor)Read(slot, "Actor"); Freeze(actor); spawned.Add(member.Index, slot);
                var group = Read(slot, "Group"); check(group != null, label + " reinforcement belongs to a group");
                if (!groups.TryGetValue(group, out var list)) groups.Add(group, list = new List<object>());
                list.Add(slot);
            }
            limits &= budget.SpawnedThisFrame <= MapSpawnBudget.MaxPerFrame && roster.Active <= budget.AliveLimit;
            if (spawned.Count >= 12 && groups.Values.Count(g => g.Count > 1) >= 2) break;
            yield return null;
        }
        check(spawned.Count >= 8 && groups.Values.Any(g => g.Count > 1), label + " actual homogeneous reinforcement groups spawned");
        check(groups.Values.All(g => g.Select(s => ((MapRunRoster.Member)Read(s, "Member")).Definition).Distinct().Count() == 1
            && g.Count <= MapCombatGeometry.GroupSize(((MapRunRoster.Member)Read(g[0], "Member")).Tier)), label + " reinforcement groups respect same-kind 5/2/1 limits");
        var distances = groups.Values.Where(g => g.Count > 1).SelectMany(g => g.Select(s => Distance((Vector3)Read(s, "Safe"), (Vector3)Read(Read(s, "Group"), "Point")))).ToArray();
        float ratio = distances.Count(d => d <= 6f) / (float)distances.Length;
        Write(output, label + "-reinforcement-groups.json", new { count = spawned.Count, groups = groups.Values.Select(g => g.Select(s => {
            var m = (MapRunRoster.Member)Read(s, "Member"); return new { m.Index, enemy = m.Definition.EnemyId, position = Point((Vector3)Read(s, "Safe")), anchor = Point((Vector3)Read(Read(s, "Group"), "Point")) }; })),
            groupedWithin6mRatio = ratio, roster.Total, roster.Active, roster.Pending, roster.Kills, budget.SpawnRetries });
        check(ratio >= .8f, label + " actual reinforcement clustered within 6m: " + ratio.ToString("P0"));
        check(limits && roster.Total == baseline.Total && roster.Target == baseline.Target && roster.Active + roster.Pending + roster.Kills == baseline.Total,
            label + " frame limit and natural totals maintained through deaths/reinforcement");
        Capture(output, label + "-reinforcement-groups.png", spawned.Values.Select(s => (Vector3)Read(s, "Safe")).ToArray());
        yield return Frames(4);
    }
    static void Capture(string output, string name, Vector3[] positions)
    {
        if (positions.Length == 0) return;
        var bounds = new Bounds(positions[0], Vector3.zero); foreach (var point in positions) bounds.Encapsulate(point);
        var node = new GameObject("Owned spawn group capture"); var texture = new RenderTexture(1024, 1024, 24); Texture2D image = null;
        var previous = RenderTexture.active;
        try
        {
            var camera = node.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) + 6;
            camera.transform.SetPositionAndRotation(bounds.center + Vector3.up * 100, Quaternion.Euler(90, 0, 0));
            camera.nearClipPlane = .1f; camera.farClipPlane = 200; camera.targetTexture = texture;
            camera.Render(); RenderTexture.active = texture; image = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); image.Apply(); File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; texture.Release(); Object.DestroyImmediate(texture); if (image) Object.DestroyImmediate(image); Object.DestroyImmediate(node); }
    }
}
