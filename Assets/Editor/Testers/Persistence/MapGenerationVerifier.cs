using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

public static class MapGenerationVerifier
{
    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Use an idle Editor.");
        output = Path.GetFullPath(output);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
            throw new InvalidOperationException("Use a new owned artifact directory.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        string Json(object value) => Newtonsoft.Json.JsonConvert.SerializeObject(value);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var randomState = UnityEngine.Random.state;
        try
        {
            var sizes = new HashSet<int>(); var candidates = new HashSet<string>();
            int caves = 0;
            for (int i = 0; i < 4096; i++)
            {
                var a = new MapInstanceState { monsterThemeId = "CavernMutants" };
                var b = new MapInstanceState { monsterThemeId = "CavernMutants", level = 100, grade = ItemGrade.Legendary };
                MapGenerationPolicy.Initialize(a, "map-test-" + i); MapGenerationPolicy.Initialize(b, "map-test-" + i);
                if (a.terrain != b.terrain || a.seed != b.seed || a.areaCount != b.areaCount || a.desertCandidateId != b.desertCandidateId)
                    throw new Exception("Level/grade changed map terrain/size.");
                if (a.terrain == MapTerrainKind.Cave) { caves++; sizes.Add(a.areaCount); }
                else candidates.Add(a.desertCandidateId);
                MapTerrainCatalog.SceneFor(a);
            }
            Check(sizes.Count == 17 && sizes.Min() == 4 && sizes.Max() == 20, "All 4..20 cave sizes generated");
            Check(candidates.Count == 30, "All 30 selected desert candidates generated");
            Check(caves > 1840 && caves < 2260, "4096 deterministic samples: terrain near initial 50/50 tuning");
            Check(true, "4096 paired samples: level/grade independent from terrain/size/seed");
            var regionalCounts = new Dictionary<string, int>();
            var desertThemes = new HashSet<string> { "PrimalHunt", "VenomBrood", "DeathHarvest" };
            var caveThemes = new HashSet<string> { "CavernMutants", "SpiderBrood", "RotsporeMarsh", "AlienContainment" };
            for (int i = 0; i < 4096; i++)
            {
                var a = new MapInstanceState();
                var b = new MapInstanceState { level = 100, grade = ItemGrade.Mythic };
                MapGenerationPolicy.Initialize(a, "regional-map-" + i);
                UnityEngine.Random.InitState(i + 73130);
                MapGenerationPolicy.Initialize(b, "regional-map-" + i);
                var allowedThemes = a.terrain == MapTerrainKind.Desert ? desertThemes : caveThemes;
                if (!allowedThemes.Contains(a.monsterThemeId) || a.monsterThemes.Count != 1
                    || a.monsterThemes[0].themeId != a.monsterThemeId || a.monsterThemes[0].weight != 100)
                    throw new Exception("Map did not retain exactly one allowed regional theme.");
                if (a.monsterThemeId != b.monsterThemeId || a.terrain != b.terrain
                    || a.seed != b.seed || a.areaCount != b.areaCount || a.desertCandidateId != b.desertCandidateId)
                    throw new Exception("Map generation depends on level, grade or global random state.");
                string key = a.terrain + "/" + a.monsterThemeId;
                regionalCounts.TryGetValue(key, out int count); regionalCounts[key] = count + 1;
            }
            Check(regionalCounts.Count == 7, "4096 new maps: every regional candidate appears; GraveHunt absent");
            Check(true, "4096 new maps: exactly one stored theme, independent of level/grade/global random");
            foreach (var table in MapThemeCatalog.Tables)
            {
                var old = new MapInstanceState { monsterThemeId = table.ThemeId };
                MapGenerationPolicy.Initialize(old, "legacy-theme-preserved-" + table.ThemeId);
                string saved = Json(old);
                Check(old.monsterThemeId == table.ThemeId && !MapGenerationPolicy.Initialize(old, "new-owner")
                    && saved == Json(old), "Legacy theme preserved without reroll: " + table.ThemeId);
            }
            bool badTerrain = false;
            try { MapThemeCatalog.RollThemeId(MapTerrainKind.Legacy, new System.Random(1)); }
            catch (InvalidOperationException) { badTerrain = true; }
            Check(badTerrain, "New theme selection rejects unsupported terrain instead of global fallback");
            foreach (var row in MapTerrainCatalog.DesertCandidates)
            {
                Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(row.scene) != null, "Native scene resolves: " + row.id);
            }
            var item = MapGenerationPolicy.CreateItem(definition, registry, 38, ItemGrade.Legendary);
            Check((item.mapState.terrain == MapTerrainKind.Desert ? desertThemes : caveThemes).Contains(item.mapState.monsterThemeId),
                "Production item factory selects its theme after terrain");
            var before = Json(item.mapState);
            var snapshot = ItemSnapshotCodec.Capture(item, registry);
            var restored = ItemSnapshotCodec.Restore(snapshot, registry);
            Check(before == Json(restored.mapState) && restored.runtimeInstanceId == item.runtimeInstanceId, "ES3 capture/restore retains every map field and identity");
            var split = item.CopyStack(1, true);
            Check(Json(split.mapState) == before && split.runtimeInstanceId != item.runtimeInstanceId, "Copy does not reroll generation");
            Check(!MapGenerationPolicy.Initialize(split.mapState, split.runtimeInstanceId), "Current map never rerolls with changed owner identity");

            var source = NewAccountFactory.Create(registry);
            var legacy = new ItemData(definition, 38, ItemGrade.Legendary);
            legacy.mapState = new MapInstanceState { mapContentId = registry.IdFor(definition), level = 38,
                grade = ItemGrade.Legendary, monsterThemeId = "CavernMutants",
                options = new List<MapOptionRoll> { new MapOptionRoll { optionId = "legacy-option-preserve", value = .23f } } };
            var oldMap = ItemSnapshotCodec.Capture(legacy, registry);
            oldMap.acquisitionOrder = source.nextAcquisitionOrder++;
            source.items.Add(oldMap); source.inventory[0] = oldMap.instanceId;
            var dormant = NewAccountFactory.Create(registry);
            var dormantMap = ItemSnapshotCodec.CopyValues(oldMap); dormantMap.instanceId = "dormant-map";
            dormant.items.Add(dormantMap); dormant.inventory[0] = dormantMap.instanceId;
            foreach (var row in dormant.items) row.acquisitionOrder = source.nextAcquisitionOrder++;
            dormant.nextAcquisitionOrder = source.nextAcquisitionOrder;
            source.activeCharacterId = AccountCharacterCommands.LegacyId;
            source.characters = new List<AccountCharacterSnapshot> {
                new AccountCharacterSnapshot { id = source.activeCharacterId, name = "기존 캐릭터" },
                new AccountCharacterSnapshot { id = Guid.NewGuid().ToString("N"), name = "보관검사", personal = dormant }
            };
            AccountInvariants.Validate(source, registry);
            var expected = ItemSnapshotCodec.CopyValues(source); MapAccountMigration.Upgrade(expected);
            string mapAfter = Json(expected.items.Single(x => x.instanceId == oldMap.instanceId).map);
            var store = new EasySaveAccountStore(Path.Combine(output, "Account"));
            store.Save(source, "seed-legacy"); long generation = store.Generation;
            store.FaultInjector = phase => { if (phase == "before-write") throw new IOException("Injected map migration save failure"); };
            bool failed = false;
            try { MapAccountMigration.Load(store, registry); } catch (IOException) { failed = true; }
            Check(failed && store.Generation == generation, "Migration write failure leaves generation unchanged");
            var onDisk = new EasySaveAccountStore(Path.Combine(output, "Account")).Load();
            Check(onDisk.items.Single(x => x.instanceId == oldMap.instanceId).map.generationVersion == 0, "Failed migration preserves original save");
            store.FaultInjector = null;
            var migrated = MapAccountMigration.Load(store, registry);
            var actual = migrated.items.Single(x => x.instanceId == oldMap.instanceId);
            Check(Json(actual.map) == mapAfter, "Migration retry generates same map");
            Check(actual.level == 38 && actual.grade == ItemGrade.Legendary && actual.instanceId == oldMap.instanceId
                && actual.map.options[0].optionId == "legacy-option-preserve" && actual.map.options[0].value == .23f,
                "Legacy identity/level/grade/options preserved");
            Check(migrated.characters[1].personal.items.Single(x => x.instanceId == "dormant-map").map.generationVersion == 1,
                "Dormant character maps migrate in same account checkpoint");
            Check(!MapAccountMigration.NeedsUpgrade(migrated), "Account migration complete");
            generation = store.Generation;
            MapAccountMigration.Load(store, registry);
            Check(store.Generation == generation, "Second load causes no migration save");
            var tx = new AccountTransactions(migrated, store, registry);
            tx.Execute("move-to-stash", tx.Revision, s => { s.inventory[0] = null; s.stashTabs[2].slots[8] = actual.instanceId; }, true);
            var reloaded = MapAccountMigration.Load(new EasySaveAccountStore(Path.Combine(output, "Account")), registry);
            Check(reloaded.stashTabs[2].slots[8] == actual.instanceId && Json(reloaded.items.Single(x => x.instanceId == actual.instanceId).map) == mapAfter,
                "Stash move and fresh store reload preserve map");
            tx.Execute("move-from-stash", tx.Revision, s => { s.stashTabs[2].slots[8] = null; s.inventory[0] = actual.instanceId; }, true);
            foreach (bool legacyRequest in new[] { false, true })
            {
                var stale = ItemSnapshotCodec.CopyValues(actual.map);
                if (legacyRequest) stale.generationVersion = 0; else stale.seed++;
                long unchangedRevision = tx.Revision;
                bool staleRejected = false;
                try { tx.Execute("stale-" + legacyRequest, tx.Revision, s => AccountRunCommands.PrepareEntry(s, "stale-run", stale, actual.instanceId), true); }
                catch (InvalidOperationException) { staleRejected = true; }
                Check(staleRejected && tx.Revision == unchangedRevision && tx.Read().items.Any(x => x.instanceId == actual.instanceId),
                    legacyRequest ? "Legacy request cannot bypass stored generation identity" : "Stale map seed rejected before preparation and consumption");
            }
            tx.Execute("prepare", tx.Revision, s => AccountRunCommands.PrepareEntry(s, "run-map-test", actual.map, actual.instanceId), true);
            Check(tx.Read().items.Any(x => x.instanceId == actual.instanceId), "Preparation does not consume map");
            tx.Execute("cancel", tx.Revision, s => AccountRunCommands.RecoverInterrupted(s), true);
            Check(tx.Read().items.Any(x => x.instanceId == actual.instanceId), "Cancelled preparation preserves map");
            tx.Execute("prepare2", tx.Revision, s => AccountRunCommands.PrepareEntry(s, "run-map-test2", actual.map, actual.instanceId), true);
            tx.Execute("activate", tx.Revision, s => AccountRunCommands.Activate(s, "run-map-test2"), true);
            Check(!tx.Read().items.Any(x => x.instanceId == actual.instanceId), "Activation consumes one reserved map");
            Check(!tx.Execute("activate", tx.Revision, s => AccountRunCommands.Activate(s, "run-map-test2"), true), "Duplicate committed activation does not consume again");
            var invalid = ItemSnapshotCodec.CopyValues(item.mapState); invalid.generationVersion = 99;
            bool rejected = false; try { MapGenerationPolicy.Initialize(invalid, "unchanged"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && invalid.generationVersion == 99, "Unknown generation version rejected without reroll");
            var wrongRun = tx.Read(); wrongRun.run.map.generationVersion = 99;
            rejected = false; try { AccountInvariants.Validate(wrongRun, registry); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Unsupported saved run generation rejected by account validation");
            invalid = ItemSnapshotCodec.CopyValues(item.mapState);
            invalid.terrain = MapTerrainKind.Desert; invalid.desertCandidateId = "missing-candidate";
            invalid.areaCount = 6; invalid.width = invalid.depth = 256;
            rejected = false; try { MapTerrainCatalog.SceneFor(invalid); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Missing desert candidate fails closed");
            invalid.terrain = MapTerrainKind.Cave; invalid.desertCandidateId = null;
            invalid.areaCount = 21; invalid.width = invalid.depth = 0;
            rejected = false; try { MapGenerationPolicy.Validate(invalid, false); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Out-of-range cave size rejected instead of silently clamped");
            invalid = ItemSnapshotCodec.CopyValues(item.mapState);
            invalid.monsterThemes.Add(new MapMonsterTheme { themeId = "GraveHunt", weight = 20 });
            rejected = false; try { MapGenerationPolicy.Validate(invalid, false); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Multiple saved themes are rejected; no implicit mixed roster");
            invalid = ItemSnapshotCodec.CopyValues(item.mapState);
            invalid.monsterThemes[0].weight = 70;
            rejected = false; try { MapGenerationPolicy.Validate(invalid, false); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Partial theme allocation rejected for a single-theme map");
            invalid = ItemSnapshotCodec.CopyValues(item.mapState);
            invalid.monsterThemes[0].themeId = invalid.monsterThemeId == "GraveHunt" ? "PrimalHunt" : "GraveHunt";
            rejected = false; try { MapGenerationPolicy.Validate(invalid, false); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Saved theme fields must agree");
            string result = Path.Combine(output, "result.json");
            File.WriteAllText(result, Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "PASS", scope = "Native EditMode data and isolated saves; no Play/Player", caves, regionalCounts, checks }, Newtonsoft.Json.Formatting.Indented));
            return result;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(output, "result.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "FAIL", checks, error = e.ToString() }, Newtonsoft.Json.Formatting.Indented));
            throw;
        }
        finally { UnityEngine.Random.state = randomState; }
    }
}
