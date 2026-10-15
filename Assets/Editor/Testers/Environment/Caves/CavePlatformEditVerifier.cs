using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Caves;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

public static class CavePlatformEditVerifier
{
    static bool Excluded(string name) => name == "CAVE_SMALL_02" || name == "CAVE_SMALL_03"
        || name == "CAVE_SMALL_05";

    public static object VerifyData(string output)
    {
        CavePlatformMapBuilder.Guard(); Directory.CreateDirectory(output);
        var library = CavePlatformSpawnBuilder.Library;
        int checks = 0, angles = 0;
        void Check(bool valid, string label) { checks++; if (!valid) throw new InvalidOperationException(label); }
        Check(library && library.platforms.Length == 9, "Nine source platforms retained");
        Check(library.spawnBakeSignature == CavePlatformSpawnBuilder.Signature(library, CavePlatformSpawnBuilder.Bodies()), "Current source, generated geometry and monster bodies match the bake");
        foreach (var p in library.platforms)
        {
            bool excluded = Excluded(p.source.name);
            var boundary = p.source.GetComponent<CavePlatformBoundary>();
            var generated = p.prefab.GetComponent<CavePlatformBoundary>();
            Check(boundary && generated && generated.Data == boundary, p.source.name + ": generated copy uses the authored boundary");
            Check(boundary.excludeMonsters == excluded && generated.Data.excludeMonsters == excluded, p.source.name + ": exact no-spawn policy");
            Check(boundary.loops.Length > 0 && boundary.ports.Length == p.regions.Length, p.source.name + ": boundary outline and connection metadata retained");
            Check(excluded ? p.spawnPoints.Length == 0 : p.spawnPoints.Length > 0, p.source.name + ": only combat platforms have baked spawn candidates");
            foreach (var t in p.prefab.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, p.source.name + ": no missing scripts");
            foreach (var r in p.regions)
            {
                int usable = 0;
                for (int i = 0; i <= 20; i++) if (p.TryAnchor(r, Mathf.Lerp(r.MinAngle, r.MaxAngle, i / 20f), out _)) { usable++; angles++; }
                Check(usable > 0, p.source.name + ": usable connection fan " + r.name);
            }
        }
        var three = library.platforms.Single(p => p.source.name == "CAVE_LARGE_01");
        var ten = library.platforms.Single(p => p.source.name == "CAVE_MEDIUM_02");
        Check(three.regions.Length > 4, "CAVE_LARGE_01 additional bridge became a connection region");
        Check(ten.regions.Count(r => !r.stone) > 1, "CAVE_MEDIUM_02 additional bridge became a connection region");
        Check(ten.regions.Any(r => r.stone && r.authoredStairCount >= 2 && r.maxStairCount >= 2 && r.angles.Length >= 2), "CAVE_MEDIUM_02 additional stair tier and angle retained");

        var sourceType = typeof(MapCombatGeometry).GetNestedType("Source", BindingFlags.NonPublic);
        var constructor = typeof(MapCombatGeometry.Area).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var grounds = new List<MapCombatGeometry.Area>();
        for (int i = 0; i < library.platforms.Length; i++)
        {
            var p = library.platforms[i]; var source = Activator.CreateInstance(sourceType, true);
            sourceType.GetField("boundary").SetValue(source, p.source.GetComponent<CavePlatformBoundary>());
            var points = Enumerable.Range(0, Excluded(p.source.name) ? 0 : 120).Select(n => new Vector3(n % 12, 0, n / 12)).ToList();
            grounds.Add((MapCombatGeometry.Area)constructor.Invoke(new object[] { i, p.source.name, Vector3.right * i * 100, i % 3, points, source }));
        }
        foreach (var theme in MapThemeCatalog.Tables)
        {
            var map = new MapInstanceState { monsterThemeId = theme.ThemeId };
            MapGenerationPolicy.Initialize(map, "cave-platform-edit-tests");
            map.terrain = MapTerrainKind.Cave; map.seed = 19091; map.areaCount = 9;
            map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreSmall, value = .9f });
            map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreMedium, value = .6f });
            map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreElite, value = .4f });
            map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreMediumElite, value = 1 });
            var roster = MapRunRoster.Build("excluded-fixture", map, grounds, entryArea: 0);
            foreach (var area in roster.Areas.Where(a => Excluded(a.Ground.Name)))
                Check(area.Count == 0 && area.InitialCount == 0 && area.ConcurrentLimit == 0 && area.BasePopulation == 0
                    && !roster.Members.Any(m => m.AreaIndex == area.Ground.Index), theme.ThemeId + ": excluded platform has no initial, fixed-option or reserve members");
            Check(roster.Areas.Count == 9 && roster.Total == roster.Areas.Sum(a => a.Count)
                && roster.Target == MapRunRoster.CaveTarget(roster.Total), theme.ThemeId + ": area indices and completion denominator remain consistent");
            foreach (var ground in grounds.Where(g => !g.AllowsMonsters))
            {
                var definition = theme.Entries.First().definition;
                Check(!MapCombatGeometry.CanPlace(ground, definition, ground.Center, true), "Excluded initial placement rejected");
                Check(!MapCombatGeometry.TryRuntimePoint(ground, definition, ground.Center, true, null, null, out _), "Excluded reinforcement/pool placement rejected before probing");
            }
        }
        var result = new { status = "PASS", checks, angles, platforms = library.platforms.Select(p => new {
            name = p.source.name, excluded = p.source.GetComponent<CavePlatformBoundary>().excludeMonsters,
            candidates = p.spawnPoints.Length, regions = p.regions.Select(r => new { r.name, r.stone, r.authoredStairCount, r.maxStairCount, r.angles }) }) };
        File.WriteAllText(Path.Combine(output, "platform-edit-native.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        return result;
    }

    public static void VerifyPlay(RunWorldGate gate, Action<bool, string> check, string output)
    {
        var world = gate.GetComponent<CaveWorld>();
        foreach (var area in gate.Roster.Areas)
        {
            bool excluded = world.courts[area.Ground.Index].tile.GetComponent<CavePlatformBoundary>().Data.excludeMonsters;
            check(area.Ground.AllowsMonsters == !excluded, "Cave platform policy bound to the actual generated source");
            if (!excluded) continue;
            check(area.Count == 0 && area.InitialCount == 0 && area.ConcurrentLimit == 0
                && !gate.Roster.Members.Any(m => m.AreaIndex == area.Ground.Index), "Excluded platform has no logical or reinforcement population");
            var field = gate.SpawnBudget.Fields[area.Ground.Index];
            check(field.FiniteActiveCount == 0 && field.InitialPlaced, "Excluded platform remains empty after spawn, death and pool-return checks");
            foreach (var other in gate.SpawnBudget.Fields)
            {
                var slots = (System.Collections.IEnumerable)typeof(MapMonsterField).GetField("activeSlots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(other);
                foreach (var slot in slots)
                {
                    var position = (Vector3)slot.GetType().GetField("Position").GetValue(slot);
                    check(!MapCombatGeometry.Contains(area.Ground, position), "Neighbor/pool spawn position stays outside excluded platforms");
                }
            }
        }
        check(gate.Roster.Target == MapRunRoster.CaveTarget(gate.Roster.Total), "Excluded platforms do not inflate the completion denominator");
        File.WriteAllText(Path.Combine(output, "platform-edit-play.json"), JsonConvert.SerializeObject(new { status = "PASS", gate.Roster.Total, gate.Roster.Target,
            areas = gate.Roster.Areas.Select(a => new { a.Ground.Index, a.Ground.Name, allowed = a.Ground.AllowsMonsters, a.Count, a.InitialCount, a.ConcurrentLimit }) }, Formatting.Indented));
    }
}
