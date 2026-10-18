using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public static class MapFarmingLootVerifier
{
    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Use idle EditMode.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        var checks = new List<string>(); var rows = new List<object>(); string error = null;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Near(double value, double target, string label) => Check(Math.Abs(value - target) < .00001, label);
        var random = Random.state; var debug = CombatDebugSettings.RunLootOverride;
        try
        {
            var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var mapDefinition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
            Check(registry && mapDefinition, "real registry and map definition load");
            var map = MapGenerationPolicy.CreateItem(mapDefinition, registry, 1, ItemGrade.Common).mapState;
            map.options.Clear();
            Random.InitState(161021);
            for (int source = 1; source <= 100; source++)
            {
                int minimum = Math.Max(1, source - 1), maximum = Math.Min(100, source + 3);
                var counts = new int[maximum - minimum + 1];
                for (int sample = 0; sample < 2000; sample++)
                {
                    int level = OverburstGrowthRules.RollDropItemLevel(source);
                    if (level < minimum || level > maximum) throw new Exception("item level outside source range " + source);
                    counts[level - minimum]++;
                }
                double expected = 2000d / counts.Length;
                double tolerance = 6 * Math.Sqrt(expected * (1 - 1d / counts.Length)) + 3;
                Check(counts.All(n => n > 0 && Math.Abs(n - expected) <= tolerance), "all legal levels uniformly reachable at source " + source);
            }
            Check(MapFarmingLoot.Applies(map) && !MapFarmingLoot.Applies(null)
                && !MapFarmingLoot.Applies(new MapInstanceState()), "new map branch does not replace legacy or outdoor policies");
            double[][] baseline = { new[] { .009, .018, .0045, .003, .0015 },
                new[] { .15, .27, .075, .075, .03 }, new[] { 1.0, 1.0, .75, .30, .15 } };
            var ranks = new[] { EnemyGradeType.Normal, EnemyGradeType.Elite, EnemyGradeType.Boss };
            var terrains = new[] { MapTerrainKind.Desert, MapTerrainKind.Cave };
            double[][] weights = { new[] { 1.4, 1.15, .65, .8, .8 }, new[] { .75, .8, 1.65, 1.25, 1.25 } };
            CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Default);
            Random.InitState(10512);
            for (int t = 0; t < 2; t++) for (int r = 0; r < 3; r++)
            {
                map.terrain = terrains[t]; double total = 0;
                const int trials = 10000;
                int[] counts = new int[5];
                double sum = baseline[r].Sum(), weighted = Enumerable.Range(0, 5).Sum(i => baseline[r][i] * weights[t][i]);
                for (int kind = 0; kind < 5; kind++)
                {
                    double expected = r == 2 ? baseline[r][kind] : baseline[r][kind] * weights[t][kind] * sum / weighted;
                    float p = MapFarmingLoot.Chance((MapFarmingKind)kind, ranks[r], map); total += p;
                    Near(p, expected, "normalized chance " + terrains[t] + "/" + ranks[r] + "/" + kind);
                    for (int n = 0; n < trials; n++) if (MapFarmingLoot.Roll((MapFarmingKind)kind, ranks[r], map, 0) != null) counts[kind]++;
                    Check(Math.Abs(counts[kind] - trials * expected) <= 6 * Math.Sqrt(trials * expected * (1 - expected)) + 3,
                        "production occurrence sample within 6 sigma + 3: " + terrains[t] + "/" + ranks[r] + "/" + kind);
                    rows.Add(new { terrain = terrains[t].ToString(), rank = ranks[r].ToString(), kind, trials, count = counts[kind], expected });
                }
                Near(total, sum, "terrain preserves expected total " + terrains[t] + "/" + ranks[r]);
            }
            Near(1190 * .036 + 50 * .60, 72.84, "1240-member reference farming expectation");
            Near((1190 * .036 + 50 * .60) * .95, 69.198, "95 percent proportional reference farming expectation");
            Near(992 * .00075 + 198 * .00175 + 50 * .0075 + 1, 2.4655, "1240-member reference map expectation");
            Near((992 * .00075 + 198 * .00175 + 50 * .0075) * .95 + 1, 2.392225, "95 percent proportional reference map expectation");
            Near(MapFarmingLoot.Chance(MapFarmingKind.Gear, EnemyGradeType.GreaterElite, map),
                MapFarmingLoot.Chance(MapFarmingKind.Gear, EnemyGradeType.Elite, map), "greater elite uses elite farming rates");
            float before = MapFarmingLoot.Chance(MapFarmingKind.Weapon, EnemyGradeType.Normal, map);
            map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.ExtraDrop, value = .2f });
            Near(MapFarmingLoot.Chance(MapFarmingKind.Weapon, EnemyGradeType.Normal, map), before * 1.2, "extra drop multiplies occurrence by 1.2");
            Near(MapFarmingLoot.Chance(MapFarmingKind.Gear, EnemyGradeType.Boss, map), 1, "boss gear guarantee capped at one");
            Near(MapDropPolicy.ChanceForMap(EnemyThemeTier.Small, map), .0009, "extra drop also multiplies natural maps");
            map.options[0].value = 100000;
            Near(MapDropPolicy.ChanceForMap(EnemyThemeTier.Elite, map), 1, "natural map probability clamps at 100 percent");
            map.options.Clear();
            double[][] grades = {
                new[] { 25d, 35d, 28d, 10d, 2d, 0d, 0d },
                new[] { 18d, 32d, 32d, 15d, 3d, 0d, 0d },
                new[] { 12d, 28d, 36d, 18d, 5.5d, 0.5d, 0d },
                new[] { 7d, 21d, 38d, 25d, 8d, 1d, 0d },
                new[] { 3d, 12d, 38d, 32d, 13d, 1.8d, 0.2d },
                new[] { 1d, 7d, 32d, 38d, 18d, 3.5d, 0.5d },
                new[] { 0d, 2d, 20d, 42d, 28d, 7d, 1d } };
            double[][] mapGrades = {
                new[] { 47d, 30d, 17d, 5.3d, 0.7d, 0d, 0d },
                new[] { 37d, 32d, 21.5d, 8.2d, 1.3d, 0d, 0d },
                new[] { 27d, 32d, 26d, 11.2d, 3.65d, 0.15d, 0d },
                new[] { 17d, 28d, 33d, 16.3d, 5.3d, 0.4d, 0d },
                new[] { 8d, 19d, 38d, 25.2d, 8.6d, 1.13d, 0.07d },
                new[] { 3d, 11d, 34d, 33d, 15.5d, 3.2d, 0.3d },
                new[] { 0d, 3d, 25d, 41d, 24.5d, 5.9d, 0.6d } };
            for (int g = 0; g < 7; g++)
            {
                map.grade = (ItemGrade)g; double lower = 0;
                for (int itemGrade = 0; itemGrade < 7; itemGrade++)
                {
                    if (grades[g][itemGrade] == 0) continue;
                    float middle = (float)((lower + grades[g][itemGrade] * .5) / 100);
                    Check(MapFarmingLoot.SelectGrade(middle, map.grade) == (ItemGrade)itemGrade, "grade distribution interval " + g + "/" + itemGrade);
                    lower += grades[g][itemGrade];
                }
                double mapLower = 0;
                for (int itemGrade = 0; itemGrade < 7; itemGrade++)
                {
                    if (mapGrades[g][itemGrade] == 0) continue;
                    float middle = (float)((mapLower + mapGrades[g][itemGrade] * .5) / 100);
                    Check(MapFarmingLoot.SelectMapGrade(middle, map.grade) == (ItemGrade)itemGrade, "next-map distribution interval " + g + "/" + itemGrade);
                    var reward = MapRewardPolicy.CreateBossMap(mapDefinition, registry, 1, 0, middle, "native", sourceMap: map);
                    Check(reward.grade == (ItemGrade)itemGrade && reward.level == 2, "completion keeps next-map distribution, independently of farming-item grades " + g + "/" + itemGrade);
                    mapLower += mapGrades[g][itemGrade];
                }
                Check(MapFarmingLoot.SelectGrade(1, map.grade) == MapFarmingLoot.MaximumGrade(map.grade), "inclusive roll 1 never selects zero-probability grade " + g);
                foreach (int level in new[] { 1, 2, 20, 21, 97, 98, 99, 100 }) foreach (var rank in ranks)
                {
                    map.level = level;
                    CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Always);
                    for (int kind = 0; kind < 5; kind++)
                    {
                        var item = MapFarmingLoot.Roll((MapFarmingKind)kind, rank, map, 8);
                        Check(item != null && InRange(item.level, level) && item.grade <= MapFarmingLoot.MaximumGrade(map.grade)
                            && !string.IsNullOrEmpty(registry.IdFor(item.baseData)), "catalog and cap " + g + "/" + level + "/" + rank + "/" + kind);
                        Check(!(item.baseData is WeaponItemData weapon) || WeaponLootPolicy.DefinitionsForLevel(item.level).Contains(weapon), "weapon definition matches rolled level " + item.level);
                        Check(!(item.baseData is GearItemData gear) || gear.AppearsAtLevel(item.level), "gear definition matches rolled level " + item.level);
                        var saved = ItemSnapshotCodec.Capture(item, registry);
                        var restored = ItemSnapshotCodec.Capture(ItemSnapshotCodec.Restore(saved, registry), registry);
                        Check(JsonConvert.SerializeObject(saved) == JsonConvert.SerializeObject(restored), "quality persists without reroll " + g + "/" + level + "/" + rank + "/" + kind);
                        var extra = MapFarmingLoot.Extra(item, rank, map, 8);
                        Check(extra.runtimeInstanceId != item.runtimeInstanceId && extra.level == item.level && extra.grade <= MapFarmingLoot.MaximumGrade(map.grade),
                            "bag extra has fresh identity within source grade cap " + g + "/" + level + "/" + rank + "/" + kind);
                        Check(item.baseData is ElementGemItemData || item.baseData is BagItemData || extra.baseData == item.baseData,
                            "extra retains the original weapon, gear or flask definition");
                    }
                }
            }
            CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Never);
            Check(Enumerable.Range(0, 5).All(k => MapFarmingLoot.Roll((MapFarmingKind)k, EnemyGradeType.Boss, map, 0) == null), "debug Never still suppresses five item sources");
            Check(MapRewardPolicy.CreateBossMap(mapDefinition, registry, 100, 1, 1, "native", sourceMap: map).level == 100,
                "completion level clamps to 100");
            map.grade = ItemGrade.Common; map.level = 1; map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.ExtraDrop, value = 100000 });
            for (int n = 0; n < 200; n++)
            {
                var item = MapDropPolicy.Roll(mapDefinition, registry, map, EnemyThemeTier.Elite, "native");
                Check(item != null && item.grade <= ItemGrade.Legendary && item.level >= 1 && item.level <= 6 && item.originRunId == "native",
                    "natural map uses grade cap, independent level range and run stamp " + n);
            }
            foreach (var definition in MapThemeCatalog.Tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct())
            {
                var dropper = definition.ActorPrefab.GetComponent<EnemyLootDropper>();
                Check(dropper && new SerializedObject(dropper).FindProperty("dropTable").objectReferenceValue == null,
                    "map actor has no extra authored loot table: " + definition.EnemyId);
            }
        }
        catch (Exception failure) { error = failure.ToString(); }
        finally { Random.state = random; CombatDebugSettings.SetRunLootOverride(debug); }
        string path = Path.Combine(output, "native-result.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new { status = error == null ? "PASS" : "FAIL", checks, rows, error }, Formatting.Indented));
        return path;
    }

    public static void VerifyPlay(Action<bool, string> check)
    {
        var player = PlayerContext.Instance.CurrentActor;
        var account = AccountGameplaySession.Current;
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var old = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(old);
        var ids = new HashSet<int>(old.Select(x => x.GetInstanceID()));
        int goldBefore = Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None).Length;
        var fixture = new GameObject("MapFarmingLootFixture");
        fixture.transform.position = new Vector3(7000, 0, 7000);
        var debug = CombatDebugSettings.RunLootOverride; var random = Random.state;
        try
        {
            var health = fixture.AddComponent<CombatHealth>(); var rank = fixture.AddComponent<EnemyRank>();
            var dropper = fixture.AddComponent<EnemyLootDropper>();
            account.ExecuteState("map-loot-slots", s => s.unlockedSlots = s.baseUnlockedSlots = s.inventoryCapacity);
            VerifyBagLevelOptions(check, registry);
            for (int pass = 0; pass < 3; pass++)
            {
                var itemMap = MapGenerationPolicy.CreateItem(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"), registry, pass == 1 ? 100 : 21, ItemGrade.Common);
                var map = itemMap.mapState; map.options.Clear();
                string run = "map-loot-" + Guid.NewGuid().ToString("N");
                check(account.ExecuteState(run, s => { AccountRunCommands.PrepareEntry(s, run, map, null, true); AccountRunCommands.Activate(s, run); }), "map run activated " + pass);
                SetPhase(WorldPhase.Run);
                var context = new EncounterContext(run, map.level, map.grade, map: pass == 2 ? null : map);
                var before = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(before);
                var existing = new HashSet<int>(before.Select(x => x.GetInstanceID()));
                dropper.ResetForPool(); dropper.Configure(null, player.Inventory, player.transform, null); dropper.ConfigureEncounter(context);
                rank.ConfigureTemporaryBoss(context); health.ResetHealth();
                CombatDebugSettings.SetRunLootOverride(pass == 1 ? RunLootDebugOverride.Default : RunLootDebugOverride.Always);
                Random.InitState(10512 + pass);
                health.TakeDamage(new DamageInfo(1000000, fixture.transform.position, player.gameObject, triggersOnHitEffects: false));
                var after = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(after);
                var created = after.Where(x => !existing.Contains(x.GetInstanceID())).ToArray();
                check(created.Count(x => x.RuntimeItem.baseData is GearItemData) == 1, "exactly one boss gear guarantee " + pass);
                if (pass != 1) check(created.Length == 5 && created.Count(x => x.RuntimeItem.baseData is WeaponItemData) == 1
                    && created.Count(x => x.RuntimeItem.baseData is ElementGemItemData) == 1 && created.Count(x => x.RuntimeItem.baseData is FlaskItemData) == 1
                    && created.Count(x => x.RuntimeItem.baseData is BagItemData) == 1, "real death connects all five production source kinds");
                check(created.All(x => InRange(x.RuntimeItem.level, map.level) && x.RuntimeItem.originRunId == run && x.RuntimeItem.grade <= (pass == 2 ? ItemGrade.Artifact : ItemGrade.Legendary)), "world items preserve rolled level, grade cap and run " + pass + ": " + string.Join(",", created.Select(x => x.RuntimeItem.level)));
                check(created.All(x => !(x.RuntimeItem.baseData is GearItemData gear) || gear.AppearsAtLevel(x.RuntimeItem.level)), "world gear uses the rolled level catalog " + pass);
                if (pass != 1) check(created.Any(x => x.RuntimeItem.level != map.level), "death pipeline does not overwrite rolled levels " + pass);
                check(Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None).Length == goldBefore, "boss death creates no gold pickup " + pass);
                int count = after.Count;
                health.TakeDamage(new DamageInfo(1000000, fixture.transform.position, player.gameObject));
                after.Clear(); WorldItemPickup.CopyActivePickups(after);
                check(after.Count == count, "repeat death cannot duplicate farming loot " + pass);
                var pickup = created.First(x => x.RuntimeItem.baseData is GearItemData); var item = pickup.RuntimeItem;
                var saved = ItemSnapshotCodec.Capture(item, registry);
                check(pickup.TryPickup(player.Inventory), "actual map item pickup commits " + pass);
                account.FlushPendingSave();
                var disk = new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load(); AccountInvariants.Validate(disk, registry);
                var persisted = disk.items.Single(i => i.instanceId == item.runtimeInstanceId);
                check(saved.acquisitionOrder == 0 && persisted.acquisitionOrder > 0, "pickup assigns inventory acquisition order " + pass);
                saved.acquisitionOrder = persisted.acquisitionOrder;
                check(JsonConvert.SerializeObject(persisted) == JsonConvert.SerializeObject(saved), "disk preserves generated item and quality " + pass);
                if (pass == 0) VerifyExtraPickup(check, dropper, fixture, context, registry);
                if (pass == 1) check(account.ExecuteState(run + "-transfer", s => AccountRunCommands.Transfer(s, run, "map-loot-transfer", item.runtimeInstanceId, registry)), "picked item can transfer before failure");
                check(account.ExecuteState(run + "-fail", s => AccountRunCommands.Fail(s, run)), "run settlement " + pass);
                check(account.Read().items.Any(i => i.instanceId == item.runtimeInstanceId) == (pass == 1), "unprotected loss and transferred retention " + pass);
                foreach (var p in created) if (p) Object.DestroyImmediate(p.gameObject);
                SetPhase(WorldPhase.Hideout);
            }
        }
        finally
        {
            var remaining = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(remaining);
            foreach (var p in remaining) if (p && !ids.Contains(p.GetInstanceID())) Object.DestroyImmediate(p.gameObject);
            Object.DestroyImmediate(fixture); SetPhase(WorldPhase.Hideout);
            CombatDebugSettings.SetRunLootOverride(debug); Random.state = random;
        }
    }

    private static void VerifyBagLevelOptions(Action<bool, string> check, AccountContentRegistry registry)
    {
        var progression = PlayerProgression.Current;
        int level = progression.Level, experience = progression.Experience;
        var apply = typeof(PlayerProgression).GetMethod("ApplyAccountProgression", BindingFlags.NonPublic | BindingFlags.Instance);
        var data = BagFarmingLoot.Definitions.First(x => x.defaultGrade == ItemGrade.Mythic);
        try
        {
            apply.Invoke(progression, new object[] { 99, 0, false });
            bool experienceOption = false;
            for (int n = 0; n < 32; n++)
            {
                var bag = new ItemData(data, 100, ItemGrade.Mythic);
                check(BagQuality.IsValid(bag.bagState, bag.grade), "level100 bag quality remains valid for player99");
                experienceOption |= bag.bagState.rows.Any(row => row.stat == BagStat.KillExperience);
            }
            check(experienceOption, "item level100 does not remove player99 experience options");
            apply.Invoke(progression, new object[] { 100, 0, false });
            for (int n = 0; n < 8; n++)
                check(!new ItemData(data, 99, ItemGrade.Mythic).bagState.rows.Any(row => row.stat == BagStat.KillExperience), "player100 excludes experience even on item99");
        }
        finally { apply.Invoke(progression, new object[] { level, experience, false }); }
        check(progression.Level == level && progression.Experience == experience, "bag option fixture restores player progression");
    }

    private static void VerifyExtraPickup(Action<bool, string> check, EnemyLootDropper dropper, GameObject fixture,
        EncounterContext context, AccountContentRegistry registry)
    {
        var inventory = PlayerContext.Instance.CurrentActor.Inventory;
        var account = AccountGameplaySession.Current;
        var previous = PlayerAccountInventoryService.EquippedBag;
        var data = BagFarmingLoot.Definitions.First(x => x.defaultGrade == ItemGrade.Mythic);
        var bag = new ItemData(data, 21, ItemGrade.Mythic);
        bag.bagState.rows.Clear();
        var kinds = new[] { BagStat.InventorySlots, BagStat.ExtraItemDrop, BagStat.ItemPickupDistance, BagStat.RareGradeWeight };
        for (int i = 0; i < kinds.Length; i++)
        {
            var row = new BagStatRoll { stat = kinds[i] };
            for (int n = 0; n < (i < 2 ? 6 : 4); n++) row.stars.Add(WeaponGradeStarType.Yellow);
            bag.bagState.rows.Add(row);
        }
        check(BagQuality.IsValid(bag.bagState, bag.grade) && inventory.AddItem(bag), "valid isolated extra-drop bag acquired");
        var owned = inventory.Items.First(x => x != null && x.runtimeInstanceId == bag.runtimeInstanceId);
        check(PlayerAccountInventoryService.TryEquipBagFromInventorySlot(inventory, inventory.Items.ToList().IndexOf(owned), owned, 0), "isolated extra-drop bag equipped");
        bool found = false;
        try
        {
            check(BagQuality.EquippedBonus(BagStat.ExtraItemDrop) > 0, "real equipped bag enables extra probability");
            var drop = typeof(EnemyLootDropper).GetMethod("DropFarmingItem", BindingFlags.NonPublic | BindingFlags.Instance);
            Random.InitState(161024);
            for (int attempt = 0; attempt < 512 && !found; attempt++)
            {
                var before = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(before);
                var known = new HashSet<int>(before.Select(x => x.GetInstanceID()));
                var source = new ItemData(WeaponLootPolicy.DefinitionsForLevel(24)[0], 24, ItemGrade.Common);
                drop.Invoke(dropper, new object[] { source, fixture.transform.position });
                var after = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(after);
                var created = after.Where(x => !known.Contains(x.GetInstanceID())).ToArray();
                try
                {
                    check(created.Length >= 1 && created.Length <= 2, "one parent and at most one nonrecursive extra " + attempt);
                    check(created.All(x => x.RuntimeItem.level == 24 && x.RuntimeItem.originRunId == context.RunId), "actual parent and extra retain level24 from source21 " + attempt);
                    var extra = created.FirstOrDefault(x => x.RuntimeItem.runtimeInstanceId != source.runtimeInstanceId);
                    if (extra == null) continue;
                    check(extra.RuntimeItem.baseData == source.baseData, "actual extra retains the original weapon definition");
                    string identity = extra.RuntimeItem.runtimeInstanceId;
                    check(extra.TryPickup(inventory), "actual extra pickup commits");
                    account.FlushPendingSave();
                    var disk = new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();
                    var saved = disk.items.Single(x => x.instanceId == identity);
                    check(saved.level == 24 && saved.originRunId == context.RunId, "actual extra level and run survive ES3 reload");
                    found = true;
                }
                finally { foreach (var pickup in created) if (pickup) Object.DestroyImmediate(pickup.gameObject); }
            }
            check(found, "actual production extra-drop branch observed");
        }
        finally
        {
            if (previous != null)
            {
                var original = inventory.Items.First(x => x != null && x.runtimeInstanceId == previous.runtimeInstanceId);
                check(PlayerAccountInventoryService.TryEquipBagFromInventorySlot(inventory, inventory.Items.ToList().IndexOf(original), original, 0), "previous isolated bag restored");
            }
            else
            {
                int empty = Enumerable.Range(0, inventory.UnlockedSlotCount).First(i => inventory.GetItemAt(i) == null);
                check(PlayerAccountInventoryService.TryUnequipBagToInventorySlot(inventory, empty, PlayerAccountInventoryService.EquippedBag, 0), "isolated extra-drop bag unequipped");
            }
        }
    }

    private static bool InRange(int itemLevel, int sourceLevel) => itemLevel >= Math.Max(1, sourceLevel - 1)
        && itemLevel <= Math.Min(100, sourceLevel + 3);

    private static void SetPhase(WorldPhase phase) => typeof(WorldSessionState)
        .GetMethod("SetPhase", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { phase });
}
