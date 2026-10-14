using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CaveBakedSpawnIntegrationVerifier
{
    static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Source(MapCombatGeometry.Area area) => typeof(MapCombatGeometry.Area).GetProperty("Source", Instance).GetValue(area);
    static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, Instance).GetValue(source);
    static int BodyIndex(object source, EnemyDefinition definition, MapCombatGeometry.Body body)
        => (int)source.GetType().GetMethod("BakedBodyIndex", Instance).Invoke(source, new object[] { definition, body });

    public static object VerifyData(string output)
    {
        CavePlatformMapBuilder.Guard(); Directory.CreateDirectory(output);
        var library = CavePlatformSpawnBuilder.Library; int checks = 0;
        void Check(bool valid, string label) { checks++; if (!valid) throw new InvalidOperationException(label); }
        var source = Activator.CreateInstance(typeof(MapCombatGeometry).GetNestedType("Source", BindingFlags.NonPublic), true);
        void Set(string name, object value) => source.GetType().GetField(name, Instance).SetValue(source, value);
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            Check(library.platforms.Length == 9 && library.platforms.Sum(p => p.spawnPoints.Length) == 4323, "Approved platform bank retained.");
            Check(library.spawnBodies.Length == 48 && library.spawnBodies.Sum(b => b.definitions.Length) == 62, "Approved body bank retained.");
            var p = library.platforms[0]; Set("spawnLibrary", library); Set("spawnPlatform", p); Set("boundary", p.source.GetComponent<CavePlatformBoundary>());
            for (int i = 0; i < library.spawnBodies.Length; i++)
                foreach (var definition in library.spawnBodies[i].definitions)
                {
                    var body = MapCombatGeometry.BodyFor(definition);
                    Check(BodyIndex(source, definition, body) == i, "Current definition uses its authored mask: " + definition.EnemyId);
                    Check(BodyIndex(source, definition, new MapCombatGeometry.Body(body.Radius + .01f, body.Height)) == -1, "Changed body falls back: " + definition.EnemyId);
                }
            var first = library.spawnBodies[0].definitions[0]; var normal = MapCombatGeometry.BodyFor(first);
            Set("spawnPlatform", null); Check(BodyIndex(source, first, normal) == -1, "Unbaked platform retains legacy path."); Set("spawnPlatform", p);
            var root = new GameObject("Owned scaled spawn binding fixture"); SceneManager.MoveGameObjectToScene(root, preview);
            var boundary = root.AddComponent<CavePlatformBoundary>(); root.transform.localScale = Vector3.one * 2; Set("boundary", boundary);
            Check(BodyIndex(source, first, normal) == -1, "Scaled platform retains live measured-grid path.");
            var result = new { status = "PASS", checks, points = 4323, profiles = 48, definitions = 62 };
            File.WriteAllText(Path.Combine(output, "native-binding.json"), JsonConvert.SerializeObject(result, Formatting.Indented)); return result;
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    public static void VerifyPlay(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var roster = gate.Roster; var world = gate.GetComponent<CaveWorld>(); var library = gate.GetComponent<CaveRuntimeGenerator>().assets.library;
        var path = new NavMeshPath(); int members = 0, physical = 0;
        foreach (var area in roster.Areas)
        {
            var source = Source(area.Ground); var platform = Field<CavePlatformLibrary.Platform>(source, "spawnPlatform");
            var boundary = Field<CavePlatformBoundary>(source, "boundary");
            check(platform != null && Field<CavePlatformLibrary>(source, "spawnLibrary") == library, "Baked cave " + area.Ground.Index + ": runtime bound to reviewed platform library.");
            foreach (var member in roster.Members.Skip(area.FirstMember).Take(area.Count))
            {
                var body = MapCombatGeometry.BodyFor(member.Definition); int index = BodyIndex(source, member.Definition, body);
                check(index >= 0, "Baked member " + member.Index + ": current body mask found.");
                var local = boundary.transform.InverseTransformPoint(member.PlannedPosition);
                check(platform.spawnPoints.Any(p => (p.bodyMask & (1L << index)) != 0 && (p.position - local).sqrMagnitude < .000025f),
                    "Baked member " + member.Index + ": planned position came from approved bank.");
                var away = member.PlannedPosition - gate.EntryPoint.position; away.y = 0;
                check(away.sqrMagnitude >= MapCombatGeometry.EntrySafety * MapCombatGeometry.EntrySafety - .001f, "Baked member " + member.Index + ": entrance exclusion retained.");
                foreach (var contact in Field<System.Collections.Generic.List<Vector3>>(source, "contacts"))
                { var delta = member.PlannedPosition - contact; delta.y = 0; check(delta.sqrMagnitude >= 1.25f * 1.25f - .001f, "Baked member " + member.Index + ": actual bridge contact excluded."); }
                check(NavMesh.SamplePosition(member.PlannedPosition, out var nav, .18f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(area.Ground.Center, nav.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete,
                    "Baked member " + member.Index + ": assembled-world navigation connected.");
                if (member.Initial)
                {
                    check(MapCombatGeometry.CanPlace(area.Ground, member.Definition, member.PlannedPosition, true), "Baked initial " + member.Index + ": live floor and body check retained."); physical++;
                }
                members++;
            }
        }
        var rebuilt = MapRunRoster.Build(gate.Context.RunId, gate.Map, roster.Areas.Select(a => a.Ground).ToList(), entryArea: roster.EntryAreaIndex);
        check(rebuilt.Total == roster.Total && rebuilt.Target == roster.Target && rebuilt.Areas.Count == roster.Areas.Count, "Baked population: original measured-area totals and target retained.");
        check(rebuilt.Members.Select(m => m.Definition.EnemyId).SequenceEqual(roster.Members.Select(m => m.Definition.EnemyId)), "Baked population: original seeded definition roster retained.");
        File.WriteAllText(Path.Combine(output, "baked-consumer.json"), JsonConvert.SerializeObject(new { status = "PASS", members, physical, roster.Total, roster.Target,
            areas = roster.Areas.Select(a => new { a.Ground.Index, a.Ground.SquareMetres, a.Count, a.InitialCount }), seed = world.seed, theme = roster.ThemeId }, Formatting.Indented));
    }
}
