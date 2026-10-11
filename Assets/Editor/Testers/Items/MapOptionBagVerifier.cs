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

public static class MapOptionBagVerifier
{
    static string Json(object value) => JsonConvert.SerializeObject(value);
    static bool Gold(BagStat stat) => stat == BagStat.GoldMagnetRadius || stat == BagStat.CombatGold;
    static MapInstanceState Map(ItemGrade grade) => new MapInstanceState { level = 1, grade = grade,
        generationVersion = 1, terrain = MapTerrainKind.Cave, options = new List<MapOptionRoll>() };

    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        Directory.CreateDirectory(output);
        var checks = new List<string>(); string failure = null; var random = Random.state;
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        try
        {
            Random.InitState(10521);
            var all = new HashSet<string>();
            for (int grade = 0; grade <= 6; grade++)
            {
                bool lower = false, upper = false, noReward = false, reward = false;
                for (int sample = 0; sample < 1000; sample++)
                {
                    var rows = MapOptionPolicy.Roll((ItemGrade)grade);
                    if (rows.Count != (grade == 0 ? 0 : grade) && rows.Count != grade + 1) throw new Exception("option count");
                    if (rows.Select(r => r.optionId).Distinct().Count() != rows.Count) throw new Exception("duplicate option");
                    if (rows.Count(r => new[] { "enemy_damage", "enemy_speed", "enemy_attack_speed", "player_health" }.Contains(r.optionId)) > 3)
                        throw new Exception("danger cap");
                    foreach (var row in rows)
                    {
                        all.Add(row.optionId);
                        if (row.value <= 0 || row.value > 1.25f || string.IsNullOrEmpty(MapOptionPolicy.Describe(row))) throw new Exception("invalid option value/label");
                        if (Mathf.Abs(row.value * 100 - Mathf.Round(row.value * 100)) > .001f) throw new Exception("whole percent");
                        if (MapOptionPolicy.IsReward(row.optionId))
                        {
                            int[] min = { 10, 15, 20, 28, 35, 45 }, max = { 15, 22, 30, 40, 50, 60 };
                            if (row.value * 100 < min[grade-1] - .001f || row.value * 100 > max[grade-1] + .001f) throw new Exception("reward range");
                        }
                    }
                    lower |= rows.Count == grade; upper |= rows.Count == grade + 1;
                    reward |= rows.Any(r => MapOptionPolicy.IsReward(r.optionId)); noReward |= rows.All(r => !MapOptionPolicy.IsReward(r.optionId));
                }
                Check(lower && (grade == 0 || upper), "1000 rolls cover count range grade " + grade);
                Check(grade == 0 || reward && noReward, "rewards share pool without guaranteed slot grade " + grade);
            }
            Check(all.Count == 11 && !all.Contains(MapOptionPolicy.MoreMediumElite), "all eleven options reachable; retired combined count absent");
            var map = Map(ItemGrade.Mythic);
            Check(MapOptionPolicy.ExperienceMultiplier(map) == 1, "grade alone grants no new-map XP bonus");
            map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.ExtraExperience, value = .2f });
            Check(Mathf.Approximately(MapOptionPolicy.ExperienceMultiplier(map), 1.2f), "map XP uses stored option");
            foreach (var pair in new[] { (1,.4f), (10,.2f), (30,.14f), (50,.08f), (70,.06f), (90,.04f), (100,.04f) })
                Check(Mathf.Approximately(MapOptionPolicy.ExperienceDensity(pair.Item1), pair.Item2), "density Lv." + pair.Item1);
            Check(PlayerProgression.CalculateKillExperienceUnits(100, 1, 20, 10) == 1300000, "100 XP + map20 + bag10 = 130 XP");
            Check(PlayerProgression.CalculateKillExperienceUnits(1, .4f, 0, 0) == 4000, "sub-one kill retains fractional XP");
            var stats = new EnemyRuntimeStats(100, 1, 1, 1.3f, Vector3.one, Vector3.one, Vector3.one, Color.white, 2.7f);
            map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.EnemyAttackSpeed, value = .08f });
            var adjusted = MapOptionPolicy.ApplyEnemyStats(stats, map);
            Check(adjusted.AttackInterval == 2.7f, "map affixes preserve authored attack interval");
            Check(Mathf.Approximately(MapOptionPolicy.AttackSpeedMultiplier(stats, map, 1, false), 1.404f), "attack speed option applies once at level1");
            Check(MapOptionPolicy.AttackSpeedMultiplier(stats, map, 100, false) <= 1.3f * OverburstBalanceTable.Current.EnemyAttackCap + .0001f,
                "normal authored tempo respects growth cap");
            Check(MapOptionPolicy.AttackSpeedMultiplier(stats, map, 100, true) <= OverburstBalanceTable.Current.EnemyAttackCap, "boss respects absolute attack cap");

            int cases = 0;
            foreach (ItemGrade grade in Enum.GetValues(typeof(ItemGrade)))
            for (int a = 1; a < 5; a++) for (int b = a+1; b < 6; b++) for (int c = b+1; c < 7; c++)
            foreach (bool maxLevel in new[] { false, true })
            {
                var bag = BagQuality.Roll(grade, 1200 + cases); bag.version = 1;
                bag.rows[1].stat = (BagStat)a; bag.rows[2].stat = (BagStat)b; bag.rows[3].stat = (BagStat)c;
                var old = ItemSnapshotCodec.CopyValues(bag); var retry = ItemSnapshotCodec.CopyValues(bag);
                int slots = BagQuality.AdditionalSlots(73, bag);
                if (!BagQuality.IsValid(bag, grade)) throw new Exception("invalid legacy fixture");
                BagQuality.UpgradeGoldOptions(bag, grade, maxLevel); BagQuality.UpgradeGoldOptions(retry, grade, maxLevel);
                if (!BagQuality.IsValid(bag, grade) || bag.version != 2 || bag.rows.Any(r => Gold(r.stat)) || Json(bag) != Json(retry)) throw new Exception("migration output");
                if (bag.seed != old.seed || slots != BagQuality.AdditionalSlots(73, bag)) throw new Exception("seed/capacity changed");
                for (int i = 0; i < 4; i++)
                {
                    if (Json(bag.rows[i].stars) != Json(old.rows[i].stars)) throw new Exception("stars rerolled");
                    if (!Gold(old.rows[i].stat) && bag.rows[i].stat != old.rows[i].stat) throw new Exception("valid option replaced");
                    if (maxLevel && Gold(old.rows[i].stat) && bag.rows[i].stat == BagStat.KillExperience) throw new Exception("new max level XP option");
                }
                string once = Json(bag);
                if (BagQuality.UpgradeGoldOptions(bag, grade, maxLevel) || once != Json(bag)) throw new Exception("migration repeats");
                cases++;
            }
            Check(cases == 320, "320 legacy grade/option/max-level cases preserve seed, slots, good rows and stars; deterministic once");
            for (int seed = 0; seed < 1000; seed++)
            {
                if (BagQuality.Roll(ItemGrade.Mythic, seed).rows.Any(r => Gold(r.stat))) throw new Exception("new gold option");
                if (BagQuality.Roll(ItemGrade.Mythic, seed, true).rows.Any(r => r.stat == BagStat.KillExperience)) throw new Exception("new max level XP");
            }
            Check(true, "1000 new bags exclude gold and max-level new XP");
            var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var bagData = registry.Entries.Select(x => x.asset).OfType<BagItemData>().First();
            var state = BagQualityVerifier.EmptyAccount();
            var merchant = registry.Entries.Select(x => x.asset).OfType<MerchantDefinition>().First();
            state.merchants.Add(new MerchantSnapshot { contentId = registry.IdFor(merchant), capacity = 1, stockInitialized = true,
                reputationPolicyVersion = MerchantReputationPolicy.Version, stock = new List<string> { null } });
            for (int i = 0; i < 4; i++)
            {
                var item = ItemSnapshotCodec.Capture(new ItemData(bagData, 73, ItemGrade.Mythic), registry);
                item.bag.version = 1; item.bag.rows[1].stat = BagStat.GoldMagnetRadius; item.bag.rows[2].stat = BagStat.CombatGold; item.bag.rows[3].stat = BagStat.KillExperience;
                state.items.Add(item);
                if (i == 0) state.inventory[0] = item.instanceId;
                if (i == 1) state.bags[0] = item.instanceId;
                if (i == 2) state.stashTabs[0].slots[0] = item.instanceId;
                if (i == 3) state.merchants[0].stock[0] = item.instanceId;
            }
            AccountEquipmentPolicy.RecalculateCapacity(state, registry); AccountCharacterCommands.EnsureLegacy(state);
            AccountCharacterCommands.Create(state, Guid.NewGuid().ToString("N"), "가방검증", "knight", new CharacterAppearanceSnapshot(), registry);
            var personal = state.characters[1].personal;
            var inactive = ItemSnapshotCodec.CopyValues(state.items[0]); inactive.instanceId = Guid.NewGuid().ToString("N"); inactive.acquisitionOrder = 0;
            personal.items.Add(inactive); personal.inventory[personal.inventory.FindIndex(x => x == null)] = inactive.instanceId;
            AccountInvariants.Validate(state, registry);
            var expected = ItemSnapshotCodec.CopyValues(state); BagAccountMigration.UpgradeGoldOptions(expected, registry);
            var store = new EasySaveAccountStore(Path.Combine(output, "BagAccount")); store.Save(state, "legacy-fixture");
            string before = Json(store.Load()), sourceBefore = Json(state);
            store.FaultInjector = point => { if (point == "before-write") throw new IOException("G05B expected migration fault"); };
            bool failed = false; try { MapAccountMigration.Load(store, registry); } catch (IOException) { failed = true; }
            Check(failed && Json(store.Load()) == before && Json(state) == sourceBefore, "migration save failure preserves disk and source");
            store.FaultInjector = null;
            var loaded = MapAccountMigration.Load(store, registry);
            Check(Json(loaded.items) == Json(expected.items) && Json(loaded.characters) == Json(expected.characters), "all four containers and inactive character migrate exact payloads");
            Check(!BagAccountMigration.NeedsGoldUpgrade(loaded) && loaded.revision == state.revision + 1, "migration uses one transaction");
            Check(Json(MapAccountMigration.Load(store, registry)) == Json(loaded), "reloading upgraded save does not reroll or write another transaction");
            var corrupt = BagQuality.Roll(ItemGrade.Common, 1); corrupt.version = 1; corrupt.rows[2].stat = corrupt.rows[1].stat;
            string corruptBefore = Json(corrupt); bool rejected = false;
            try { BagQuality.UpgradeGoldOptions(corrupt, ItemGrade.Common, false); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && Json(corrupt) == corruptBefore, "corrupt bag rejected without repair");

            var xpState = BagQualityVerifier.EmptyAccount(); xpState.level = 10;
            var xpStore = new EasySaveAccountStore(Path.Combine(output, "XpAccount"));
            xpStore.Save(xpState, "xp-fixture"); var commands = new AccountTransactions(xpState, xpStore, registry);
            commands.GrantExperience("fraction1", commands.Revision, 0, out _, out _, 4000);
            Check(commands.Read().experience == 0 && commands.Read().bagExperienceCarry == 4000, "fraction-only transaction commits");
            xpStore.FaultInjector = point => { if (point == "before-write") throw new IOException("G05B expected XP fault"); };
            long revision = commands.Revision; failed = false;
            try { commands.GrantExperience("fraction2", revision, 0, out _, out _, 4000); } catch (IOException) { failed = true; }
            Check(failed && commands.Revision == revision && commands.Read().bagExperienceCarry == 4000, "XP save failure retains exact carry and revision");
            xpStore.FaultInjector = null;
            commands.GrantExperience("fraction2", commands.Revision, 0, out _, out _, 4000);
            commands.GrantExperience("fraction3", commands.Revision, 0, out _, out _, 4000);
            var disk = xpStore.Load(); Check(disk.experience == 1 && disk.bagExperienceCarry == 2000, "three 0.4 XP awards persist 1 XP and 0.2 carry");
            Check(!commands.GrantExperience("fraction3", commands.Revision, 0, out _, out _, 4000), "same XP transaction cannot duplicate award");
            commands.GrantExperience("additive", commands.Revision, 130, out _, out _);
            Check(commands.Read().experience == 131 && commands.Read().bagExperienceCarry == 2000, "whole reward preserves previous carry");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally { Random.state = random; }
        string path = Path.Combine(output, "native-result.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new { status = failure == null ? "PASS" : "FAIL", checks, failure }, Formatting.Indented));
        return path;
    }

    public static void VerifyPlay(Action<bool, string> check)
    {
        var account = AccountGameplaySession.Current;
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var player = PlayerContext.Instance.CurrentActor; var progression = PlayerProgression.Current;
        var inventory = PlayerAccountInventoryService.SharedInventory;
        check(account.ExecuteState("g05b-level", s => { s.level = 10; s.experience = 0; s.bagExperienceCarry = 0; }), "isolated XP fixture level10");
        var bagData = registry.Entries.Select(x => x.asset).OfType<BagItemData>().First();
        var bag = new ItemData(bagData, 1, ItemGrade.Common);
        bag.bagState.rows[1].stat = BagStat.KillExperience; bag.bagState.rows[2].stat = BagStat.ExtraItemDrop; bag.bagState.rows[3].stat = BagStat.RareGradeWeight;
        check(inventory.AddItem(bag), "actual inventory accepts v2 bag");
        var ui = Object.FindFirstObjectByType<OverburstGameUI>(FindObjectsInactive.Include);
        ui.inventory.SetVisible(true);
        var bridge = ui.inventory.GetComponent<InventorySlotBridge>();
        var slots = (SlotUI[])typeof(InventorySlotBridge).GetField("inventorySlots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bridge);
        var slot = slots[inventory.Items.ToList().FindIndex(i => i?.runtimeInstanceId == bag.runtimeInstanceId)];
        check((bool)typeof(InventorySlotBridge).GetMethod("EquipBagFromInventorySlot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(bridge, new object[] { slot }), "actual bag equip UI route");
        check(BagQuality.EquippedBonus(BagStat.KillExperience) == 2 && BagQuality.EquippedBonus(BagStat.CombatGold) == 0, "equipped v2 bag contributes XP without gold");
        int xp = progression.Experience;
        progression.AddKillExperience(100, 20);
        check(progression.FlushPendingExperience() && progression.Experience == xp + 122, "runtime map20 + equipped bag2 adds 122 from 100");
        progression.AddKillExperience(1, 0, .4f); progression.FlushPendingExperience();
        account.FlushPendingSave(); var saved = new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();
        check(saved.bagExperienceCarry == 4080, "runtime fraction-only award saved with equipped bag bonus");
        var enemy = new GameObject("MapOptionXpDeathFixture");
        string run = "g05b-xp-fixture";
        try
        {
            var health = enemy.AddComponent<CombatHealth>(); var rank = enemy.AddComponent<EnemyRank>();
            var map = MapGenerationPolicy.CreateItem(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"), registry, 1, ItemGrade.Common).mapState;
            map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.ExtraExperience, value = .2f });
            check(account.ExecuteState(run, s => { AccountRunCommands.PrepareEntry(s, run, map, null, true); AccountRunCommands.Activate(s, run); }), "isolated XP run active");
            typeof(WorldSessionState).GetMethod("SetPhase", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { WorldPhase.Run });
            rank.ConfigureTemporaryBoss(new EncounterContext(run, 1, ItemGrade.Common, map: map));
            xp = progression.Experience;
            int raw = OverburstGrowthRules.ExperienceForKill(1, EnemyGradeType.Boss, progression.Level);
            long expected = PlayerProgression.CalculateKillExperienceUnits(raw, .4f, 20, 2) + saved.bagExperienceCarry;
            health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, player.gameObject, triggersOnHitEffects:false));
            progression.FlushPendingExperience();
            check(progression.Experience == xp + expected / BagQuality.RewardScale && account.Read().bagExperienceCarry == expected % BagQuality.RewardScale, "real death routes density and additive XP once");
            int once = progression.Experience; health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, player.gameObject)); progression.FlushPendingExperience();
            check(progression.Experience == once, "repeated death cannot duplicate XP");
        }
        finally
        {
            Object.DestroyImmediate(enemy);
            account.ExecuteState(run + "-fail", s => AccountRunCommands.Fail(s, run));
            typeof(WorldSessionState).GetMethod("SetPhase", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { WorldPhase.Hideout });
        }
        var itemMap = MapGenerationPolicy.CreateItem(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"), registry, 1, ItemGrade.Mythic);
        itemMap.mapState.options = new List<MapOptionRoll> {
            new MapOptionRoll { optionId = MapOptionPolicy.ExtraDrop, value = .6f }, new MapOptionRoll { optionId = MapOptionPolicy.ExtraExperience, value = .6f },
            new MapOptionRoll { optionId = MapOptionPolicy.EnemyHealth, value = .6f }, new MapOptionRoll { optionId = MapOptionPolicy.EnemyAttackSpeed, value = .08f },
            new MapOptionRoll { optionId = MapOptionPolicy.MoreSmall, value = .9f }, new MapOptionRoll { optionId = MapOptionPolicy.MoreMedium, value = .7f }, new MapOptionRoll { optionId = MapOptionPolicy.MoreElite, value = 1.25f } };
        check(inventory.AddItem(itemMap), "seven-option map acquired");
        string tooltip = SimpleItemTooltipBuilder.Build(itemMap);
        check(tooltip.Contains("보상 등급 상한") && !tooltip.Contains("고등급 보정") && itemMap.mapState.options.All(r => tooltip.Contains(MapOptionPolicy.Describe(r))), "tooltip shows actual cap and all seven options");
        var portal = Object.FindFirstObjectByType<MapDungeonPortal>();
        check(portal.TryInteract(player) == InteractionExecutionResult.Succeeded, "production portal opens");
        // The production panel is being restyled in a separate shared-worktree change.
        var panel = typeof(MapDungeonPortalPanel).GetProperty("Active")?.GetValue(null) as MapDungeonPortalPanel;
        check(panel != null, "current production slot-based panel available");
        var panelSlots = (SlotUI[])typeof(MapDungeonPortalPanel).GetField("inventorySlots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
        check((bool)typeof(MapDungeonPortalPanel).GetMethod("TryInsert").Invoke(panel,
            new object[] { panelSlots.First(s => s.DisplayItem?.runtimeInstanceId == itemMap.runtimeInstanceId) }), "map selection uses real inventory slot");
        Canvas.ForceUpdateCanvases();
        var detail = (RectTransform)typeof(MapDungeonPortalPanel).GetField("details", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
        var texts = detail.GetComponentsInChildren<UnityEngine.UI.Text>().Where(t => t.gameObject.activeInHierarchy).ToArray();
        check(texts.Count(t => t.text.StartsWith("◆")) == 7 && texts.Any(t => t.text == "7개 적용"), "all seven option rows visible");
        check(texts.All(t => -t.rectTransform.anchoredPosition.y + t.rectTransform.rect.height <= detail.rect.height + .1f), "all detail rows fit within panel height");
        check(texts.All(t => t.preferredHeight <= t.rectTransform.rect.height + 1), "no text vertically clipped");
        account.FlushPendingSave();
    }
}
