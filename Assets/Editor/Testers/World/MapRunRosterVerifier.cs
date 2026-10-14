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
            Check(MapRunRoster.BasePopulation(MapTerrainKind.Cave, 55, 1) == 30
                && MapRunRoster.BasePopulation(MapTerrainKind.Desert, 122, 1) == 40
                && MapRunRoster.BasePopulation(MapTerrainKind.Desert, 174, 1) <= 60, "Reduced population includes small-floor minima of cave 30 and desert 40");
            var areas = new List<MapCombatGeometry.Area>();
            int[] metres = { 55, 85, 300, 1950 };
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
                Check(a.Areas[0].Count >= 30 && a.Areas[3].Count > a.Areas[2].Count && a.Areas[2].Count > a.Areas[1].Count, theme.ThemeId + ": measured area changes reduced population");
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreSmall, value = .9f });
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreMedium, value = .6f });
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreElite, value = .4f });
                var boosted = MapRunRoster.Build("run-boost", map, areas);
                foreach (int entry in new[] { 0, 2 })
                {
                    var safeEntry = MapRunRoster.Build("safe-entry", map, areas, entryArea: entry);
                    Check(safeEntry.EntryAreaIndex == entry && safeEntry.Areas.Count == areas.Count
                        && safeEntry.Areas[entry].Count == 0 && safeEntry.Areas[entry].InitialCount == 0
                        && safeEntry.Areas[entry].BasePopulation == 0 && !safeEntry.Areas[entry].Cleared
                        && safeEntry.Members.All(m => m.AreaIndex != entry)
                        && safeEntry.Areas.Where(x => x.Ground.Index != entry).All(x => x.Count > 0)
                        && safeEntry.Target == MapRunRoster.CaveTarget(safeEntry.Total), theme.ThemeId + ": entry " + entry + " excludes option population while preserving area indices and the completion target");
                    Check(Signature(safeEntry) == Signature(MapRunRoster.Build("safe-entry-repeat", map, areas, entryArea: entry)),
                        theme.ThemeId + ": entry-excluded roster is deterministic");
                }
                int[] before = { a.Areas.Sum(x => x.Small), a.Areas.Sum(x => x.Medium), a.Areas.Sum(x => x.Elite) };
                int[] after = { boosted.Areas.Sum(x => x.Small), boosted.Areas.Sum(x => x.Medium), boosted.Areas.Sum(x => x.Elite) };
                float[] bonuses = { .9f, .6f, .4f };
                Check(Enumerable.Range(0, 3).All(t => after[t] == before[t] + (int)Math.Round(before[t] * (double)bonuses[t], MidpointRounding.AwayFromZero)), theme.ThemeId + ": independent tier additions preserve exact map totals");
                Check(boosted.Total > a.Total && boosted.Target > a.Target && boosted.Areas[3].Count > 500, theme.ThemeId + ": options extend the roster beyond a 500-enemy request without truncation");
                rows.Add(new { theme = theme.ThemeId, a.Total, a.Target, areas = a.Areas.Select(x => new { x.Ground.SquareMetres, x.BasePopulation, x.Count, x.InitialCount, x.Small, x.Medium, x.Elite }), boostedTotal = boosted.Total });
                last = a; lastMap = high;
            }
            Check(last.NaturalProgressMultiplier == 1f, "Cave progression starts at the original health and damage");
            Check(!last.TryActivate(0, out _) && !last.TryRecordDeath(0, 1), "No activation or kill before placement preparation");
            // Pure lifecycle exercise; physical placements are covered by the isolated entry Play verifier.
            typeof(MapRunRoster).GetProperty(nameof(MapRunRoster.PlacementReady)).SetValue(last, true);
            Check(last.TryActivate(0, out int first) && !last.TryActivate(0, out _), "An ID leases once while active");
            Check(last.TryReturn(0, first) && last.Kills == 0 && last.Pending == last.Total, "Despawn/failure returns the same ID to pending without a kill");
            Check(last.TryActivate(0, out int second) && second != first && !last.TryRecordDeath(0, first), "Old pooled lease cannot kill a new lease");
            Check(last.TryRecordDeath(0, second) && !last.TryRecordDeath(0, second) && !last.TryReturn(0, second)
                && !last.TryActivate(0, out _), "Actual death transition is once-only; dead ID never respawns");
            int target = last.Target, total = last.Total;
            Check(Mathf.Abs(last.NaturalProgressMultiplier - (1f + .15f / target)) < .000001f, "First unique death advances progression using the fixed 95 percent target");
            for (int i = 1; i < target; i++) { last.TryActivate(i, out int lease); if (!last.TryRecordDeath(i, lease)) throw new Exception("Unique death " + i); }
            Check(last.CaveTargetReached && last.Total == total && last.Target == target && last.Pending == total - target,
                "95 percent target is reached with the finite remainder still pending");
            Check(Mathf.Abs(last.NaturalProgressMultiplier - 1.15f) < .000001f, "95 percent completion reaches the 15 percent progression cap");
            last.TryActivate(target, out int afterTargetLease); last.TryRecordDeath(target, afterTargetLease);
            Check(Mathf.Abs(last.NaturalProgressMultiplier - 1.15f) < .000001f, "Optional remaining population keeps the progression cap");
            Check(!last.TryRecordDeath(-1, 1) && !last.TryRecordDeath(total, 1), "Unknown IDs never count");
            var clearing = MapRunRoster.Build("clear-area", lastMap, areas);
            typeof(MapRunRoster).GetProperty(nameof(MapRunRoster.PlacementReady)).SetValue(clearing, true);
            var court = clearing.Areas[0];
            foreach (var member in clearing.Members.Take(court.InitialCount))
            { clearing.TryActivate(member.Index, out int lease); clearing.TryRecordDeath(member.Index, lease); }
            Check(!court.Cleared && court.Kills == court.InitialCount, "Empty initial packs do not clear a platform with reserves");
            for (int i = court.InitialCount; i < court.ClearTarget - 1; i++)
            { clearing.TryActivate(i, out int lease); clearing.TryRecordDeath(i, lease); }
            int final = court.ClearTarget - 1;
            clearing.TryActivate(final, out int finalLease); clearing.TryReturn(final, finalLease);
            Check(!court.Cleared && court.Kills == court.ClearTarget - 1, "Streaming out the last required living ID does not clear its platform");
            clearing.TryActivate(final, out int newLease);
            Check(!clearing.TryRecordDeath(final, finalLease) && !court.Cleared, "Stale pooled lease cannot clear a platform");
            clearing.TryRecordDeath(final, newLease);
            Check(court.Cleared && court.Kills == court.ClearTarget && !clearing.TryRecordDeath(final, newLease)
                && clearing.Areas.Skip(1).All(a => !a.Cleared && a.Kills == 0), "Only 95 percent unique natural deaths clear their own platform once");
            Check(court.Count - court.Kills == clearing.Members.Take(court.Count).Count(m => m.State == MapRunRoster.Life.Pending)
                && clearing.Kills + clearing.Active + clearing.Pending == clearing.Total, "Clearing preserves the unspawned remainder and fixed denominator without fabricated deaths");
            var newRun = MapRunRoster.Build("new-run", lastMap, areas);
            Check(newRun.Areas.All(a => !a.Cleared && a.Kills == 0), "New map run resets platform clearing independently of seed");
            preview = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Owned roster gate fixture"); SceneManager.MoveGameObjectToScene(go, preview);
            var gate = go.AddComponent<RunWorldGate>();
            gate.BeginPreparation(new RunSnapshot { runId = "run-a", phase = RunPhase.EntryPending, map = lastMap });
            Check(!gate.CompletePreparation("run-a", go.transform), "Product gate cannot consume before a prepared roster exists");
            Check(!gate.SetRoster(last), "A population with earlier kills cannot initialize a new gate");
            var fresh = MapRunRoster.Build("run-a", lastMap, areas);
            typeof(MapRunRoster).GetProperty(nameof(MapRunRoster.PlacementReady)).SetValue(fresh, true);
            Check(gate.SetRoster(fresh) && !gate.SetRoster(fresh) && gate.CompletePreparation("run-a", go.transform), "Prepared roster attaches once before world readiness");
            var largeGround = Enumerable.Range(0, 17).Select(i => new MapCombatGeometry.Area(i, "Cap floor " + i, Vector3.right * i * 100,
                Math.Min(2, i), areas[0].Points.ToList())).ToList();
            foreach (int requested in new[] { 17, 18, 19, 20 })
            {
                var saved = ItemSnapshotCodec.CopyValues(lastMap); saved.areaCount = requested;
                string original = JsonConvert.SerializeObject(saved);
                gate.BeginPreparation(new RunSnapshot { runId = "cap-" + requested, phase = RunPhase.EntryPending, map = saved });
                var capped = MapRunRoster.Build("cap-" + requested, gate.Map, largeGround);
                typeof(MapRunRoster).GetProperty(nameof(MapRunRoster.PlacementReady)).SetValue(capped, true);
                Check(gate.Map.areaCount == 17 && capped.Areas.Count == 17 && gate.SetRoster(capped)
                    && gate.CompletePreparation("cap-" + requested, go.transform) && JsonConvert.SerializeObject(saved) == original,
                    "Stored " + requested + "-platform map prepares a 17-area roster without changing reserved map identity");
            }
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
