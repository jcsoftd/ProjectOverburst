using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MapRunRosterVerifier
{
    public static string Run(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Use idle EditMode.");
        string output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Use a new owned output folder.");
        Directory.CreateDirectory(output);
        var checks = new List<string>(); var rows = new List<object>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        string Signature(MapRunRoster roster) => JsonConvert.SerializeObject(roster.Members.Select(m => new { m.AreaIndex, enemy = m.Definition.EnemyId, m.Tier, m.Initial }));
        var preview = default(Scene);
        try
        {
            Check(MapRunRoster.CaveTarget(100) == 95 && MapRunRoster.CaveTarget(125) == 119 && MapRunRoster.CaveTarget(1240) == 1178, "95 percent target rounds up using integer arithmetic");
            Check(MapCombatGeometry.BandForDistance(0, 1) == 0 && MapCombatGeometry.BandForDistance(1, 1) == 2
                && MapCombatGeometry.BandForDistance(1, 2) == 1 && MapCombatGeometry.BandForDistance(2, 2) == 2, "Short branches still have an entry and farthest combat band");
            Check(MapRunRoster.BasePopulation(MapTerrainKind.Desert, 122, 1) >= 60
                && MapRunRoster.BasePopulation(MapTerrainKind.Desert, 174, 1) <= 90, "Measured small desert floors start within adopted 60..90 tuning");
            var areas = new List<MapCombatGeometry.Area>();
            int[] metres = { 55, 85, 300, 950 };
            for (int i = 0; i < 4; i++) areas.Add(new MapCombatGeometry.Area(i, "Synthetic " + i, Vector3.right * i * 100, Math.Min(2, i),
                Enumerable.Range(0, metres[i]).Select(n => new Vector3(n % 40, 0, n / 40)).ToList()));
            MapRunRoster last = null; MapInstanceState lastMap = null;
            foreach (var theme in MapThemeCatalog.Tables)
            {
                var map = new MapInstanceState { monsterThemeId = theme.ThemeId };
                MapGenerationPolicy.Initialize(map, "roster-tests");
                map.terrain = MapTerrainKind.Cave; map.areaCount = 4; map.seed = 73130;
                map.desertCandidateId = null; map.width = map.depth = 0;
                var a = MapRunRoster.Build("run-a", map, areas);
                var high = ItemSnapshotCodec.CopyValues(map); high.level = 100; high.grade = ItemGrade.Mythic;
                var b = MapRunRoster.Build("run-b", high, areas);
                Check(Signature(a) == Signature(b), theme.ThemeId + ": level/grade/run identity do not reroll population or composition");
                Check(a.Members.All(m => theme.Entries.Any(e => e.definition == m.Definition)) && a.ThemeId == theme.ThemeId, theme.ThemeId + ": every natural member belongs to the stored theme");
                Check(a.Areas.Sum(x => x.Small + x.Medium + x.Elite) == a.Total && a.Target == MapRunRoster.CaveTarget(a.Total), theme.ThemeId + ": tier sums and fixed denominator agree");
                Check(a.Members.Select(m => m.Id).Distinct().Count() == a.Total && a.Members.All(m => m.Id.StartsWith("run-a/")), theme.ThemeId + ": stable unique logical IDs independent of pooled actors");
                Check(a.Areas.All(x => x.InitialCount >= Math.Ceiling(x.Count * .3) && x.InitialCount <= Math.Floor(x.Count * .5))
                    && a.Members.Count(m => m.Initial) == a.Areas.Sum(x => x.InitialCount), theme.ThemeId + ": 30..50 percent initial allocation plus finite reserve");
                Check(a.Areas[0].Count >= 50 && a.Areas[3].Count > a.Areas[2].Count && a.Areas[2].Count > a.Areas[1].Count, theme.ThemeId + ": measured area changes population");
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreSmall, value = .9f });
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreMedium, value = .6f });
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreElite, value = .4f });
                var boosted = MapRunRoster.Build("run-boost", map, areas);
                int[] before = { a.Areas.Sum(x => x.Small), a.Areas.Sum(x => x.Medium), a.Areas.Sum(x => x.Elite) };
                int[] after = { boosted.Areas.Sum(x => x.Small), boosted.Areas.Sum(x => x.Medium), boosted.Areas.Sum(x => x.Elite) };
                float[] bonuses = { .9f, .6f, .4f };
                Check(Enumerable.Range(0, 3).All(t => after[t] == before[t] + (int)Math.Round(before[t] * (double)bonuses[t], MidpointRounding.AwayFromZero)), theme.ThemeId + ": independent tier additions preserve exact map totals");
                Check(boosted.Total > a.Total && boosted.Target > a.Target && boosted.Areas[3].Count > 500, theme.ThemeId + ": options extend the roster beyond a 500-enemy request without truncation");
                rows.Add(new { theme = theme.ThemeId, a.Total, a.Target, areas = a.Areas.Select(x => new { x.Ground.SquareMetres, x.BasePopulation, x.Count, x.InitialCount, x.Small, x.Medium, x.Elite }), boostedTotal = boosted.Total });
                last = a; lastMap = high;
            }
            Check(!last.TryActivate(0, out _) && !last.TryRecordDeath(0, 1), "No activation or kill before placement preparation");
            // Pure lifecycle exercise; physical placements are covered by the isolated entry Play verifier.
            typeof(MapRunRoster).GetProperty(nameof(MapRunRoster.PlacementReady)).SetValue(last, true);
            Check(last.TryActivate(0, out int first) && !last.TryActivate(0, out _), "An ID leases once while active");
            Check(last.TryReturn(0, first) && last.Kills == 0 && last.Pending == last.Total, "Despawn/failure returns the same ID to pending without a kill");
            Check(last.TryActivate(0, out int second) && second != first && !last.TryRecordDeath(0, first), "Old pooled lease cannot kill a new lease");
            Check(last.TryRecordDeath(0, second) && !last.TryRecordDeath(0, second) && !last.TryReturn(0, second)
                && !last.TryActivate(0, out _), "Actual death transition is once-only; dead ID never respawns");
            int target = last.Target, total = last.Total;
            for (int i = 1; i < target; i++) { last.TryActivate(i, out int lease); if (!last.TryRecordDeath(i, lease)) throw new Exception("Unique death " + i); }
            Check(last.CaveTargetReached && last.Total == total && last.Target == target && last.Pending == total - target,
                "95 percent target is reached with the finite remainder still pending");
            Check(!last.TryRecordDeath(-1, 1) && !last.TryRecordDeath(total, 1), "Unknown IDs never count");
            preview = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Owned roster gate fixture"); SceneManager.MoveGameObjectToScene(go, preview);
            var gate = go.AddComponent<RunWorldGate>();
            gate.BeginPreparation(new RunSnapshot { runId = "run-a", phase = RunPhase.EntryPending, map = lastMap });
            Check(!gate.CompletePreparation("run-a", go.transform), "Product gate cannot consume before a prepared roster exists");
            Check(!gate.SetRoster(last), "A population with earlier kills cannot initialize a new gate");
            var fresh = MapRunRoster.Build("run-a", lastMap, areas);
            typeof(MapRunRoster).GetProperty(nameof(MapRunRoster.PlacementReady)).SetValue(fresh, true);
            Check(gate.SetRoster(fresh) && !gate.SetRoster(fresh) && gate.CompletePreparation("run-a", go.transform), "Prepared roster attaches once before world readiness");
            var invalid = ItemSnapshotCodec.CopyValues(lastMap); invalid.monsterThemeId = "missing-theme"; invalid.monsterThemes[0].themeId = invalid.monsterThemeId;
            bool rejected = false; try { MapRunRoster.Build("bad", invalid, areas); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Unavailable stored theme fails instead of substituting a different theme");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "PASS", scope = "Native roster/lifecycle with synthetic floor areas; physical geometry in Play", checks, rows }, Formatting.Indented));
        }
        catch (Exception e)
        { File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = e.ToString(), rows }, Formatting.Indented)); throw; }
        finally { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); }
        return Path.Combine(output, "result.json");
    }
}
