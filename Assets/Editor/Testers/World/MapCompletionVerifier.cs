using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

public static class MapCompletionVerifier
{
    public static string Run(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Use idle EditMode.");
        string output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Use new owned evidence.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        try
        {
            Check((int)RunPhase.EntryPending == 0 && (int)RunPhase.BossCleared == 2 && (int)RunPhase.Failed == 4 && (int)RunPhase.Completed == 5, "Legacy serialized enum values preserved");
            Check(MapRunRoster.CaveTarget(100) == 95 && MapRunRoster.CaveTarget(125) == 119, "95 percent uses ceiling for both divisible and fractional totals");
            foreach (var terrain in new[] { MapTerrainKind.Cave, MapTerrainKind.Desert })
            {
                var initial = NewAccountFactory.Create(registry); AccountCharacterCommands.EnsureLegacy(initial);
                var map = MapGenerationPolicy.CreateItem(definition, registry, 1, ItemGrade.Common).mapState;
                map.terrain = terrain; map.monsterThemeId = terrain == MapTerrainKind.Cave ? "CavernMutants" : "PrimalHunt";
                map.monsterThemes[0].themeId = map.monsterThemeId;
                if (terrain == MapTerrainKind.Cave) { map.areaCount = 4; map.desertCandidateId = null; map.width = map.depth = 0; }
                else { var candidate = MapTerrainCatalog.DesertCandidates[0]; map.desertCandidateId = candidate.id; map.areaCount = candidate.areaCount; map.width = candidate.width; map.depth = candidate.depth; }
                string id = "completion-native-" + terrain;
                AccountRunCommands.PrepareEntry(initial, id, map, null, true); AccountRunCommands.Activate(initial, id);
                var rewardItem = MapRewardPolicy.CreateBossMap(definition, registry, 1, .1f, .1f, id);
                rewardItem.runtimeInstanceId = AccountRunCommands.CompletionRewardId(id);
                var reward = ItemSnapshotCodec.Capture(rewardItem, registry);
                var store = new EasySaveAccountStore(Path.Combine(output, terrain.ToString())); store.Save(initial, "fixture");
                var transactions = new AccountTransactions(initial, store, registry, true);
                long before = transactions.Revision;
                Action<AccountSnapshot> complete = state => AccountRunCommands.CompleteMap(state, id, DateTime.UtcNow.Ticks, 1, new Vector3(3, 4, 5), reward);
                store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture completion write failure"); };
                bool failed = false;
                try { transactions.Execute("complete-" + id, before, complete, true); } catch (IOException) { failed = true; }
                Check(failed && transactions.Revision == before && transactions.ReadRun().phase == RunPhase.Active, terrain + ": failed completion save preserves active state and revision");
                store.FaultInjector = null;
                Check(transactions.Execute("complete-" + id, before, complete, true), terrain + ": completion retry commits");
                var saved = new EasySaveAccountStore(Path.Combine(output, terrain.ToString())).Load();
                Check(saved.run.phase == RunPhase.Completed && saved.run.completionReward.instanceId == reward.instanceId
                    && !saved.run.completionRewardClaimed && saved.items.All(x => x.instanceId != reward.instanceId), terrain + ": floor reward is durable but not auto-acquired");
                Check(saved.bossClearCount == (terrain == MapTerrainKind.Desert ? 1 : 0), terrain + ": boss count respects terrain");
                Check(AccountInvariants.IsRunning(saved.run.phase) && new EncounterContext(id, 1, ItemGrade.Common).MatchesRun(saved.run), terrain + ": post-clear rewards remain eligible");
                var duplicate = ItemSnapshotCodec.CopyValues(saved); complete(duplicate);
                Check(duplicate.bossClearCount == saved.bossClearCount && duplicate.run.completionReward.instanceId == reward.instanceId, terrain + ": duplicate completion preserves identity and count");
                var beforePickupDeath = ItemSnapshotCodec.CopyValues(saved); AccountRunCommands.Fail(beforePickupDeath, id);
                AccountInvariants.Validate(beforePickupDeath, registry);
                Check(beforePickupDeath.items.All(x => x.instanceId != reward.instanceId), terrain + ": unclaimed reward never becomes owned on failure");
                transactions.Execute("pickup", transactions.Revision, state => AccountRunCommands.Acquire(state, id, reward), true);
                var acquired = transactions.Read();
                Check(acquired.run.completionRewardClaimed && acquired.items.Single(x => x.instanceId == reward.instanceId).originRunId == id, terrain + ": pickup and claim flag commit together");
                var dropped = ItemSnapshotCodec.CopyValues(acquired);
                dropped.inventory[dropped.inventory.IndexOf(reward.instanceId)] = null;
                dropped.items.RemoveAll(x => x.instanceId == reward.instanceId);
                AccountRunCommands.Acquire(dropped, id, reward); AccountInvariants.Validate(dropped, registry);
                Check(dropped.run.completionRewardClaimed && dropped.items.Count(x => x.instanceId == reward.instanceId) == 1,
                    terrain + ": owned completion map may be dropped and reacquired without a new reward");
                var afterPickupDeath = ItemSnapshotCodec.CopyValues(acquired); int xp = afterPickupDeath.experience;
                AccountRunCommands.Fail(afterPickupDeath, id); AccountInvariants.Validate(afterPickupDeath, registry);
                Check(afterPickupDeath.items.All(x => x.instanceId != reward.instanceId) && afterPickupDeath.experience == xp, terrain + ": failure removes unextracted reward and preserves XP");
                var interrupted = ItemSnapshotCodec.CopyValues(acquired);
                Check(AccountRunCommands.RecoverInterrupted(interrupted) && interrupted.run.phase == RunPhase.Failed
                    && interrupted.items.All(x => x.instanceId != reward.instanceId), terrain + ": interrupted completed run still fails");
                store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture extraction write failure"); };
                failed = false;
                try { transactions.Execute("extract", transactions.Revision, state => AccountRunCommands.Extract(state, id), true); } catch (IOException) { failed = true; }
                Check(failed && transactions.ReadRun().phase == RunPhase.Completed && transactions.Read().items.Single(x => x.instanceId == reward.instanceId).originRunId == id, terrain + ": extraction failure cannot publish success");
                store.FaultInjector = null;
                transactions.Execute("extract", transactions.Revision, state => AccountRunCommands.Extract(state, id), true);
                saved = new EasySaveAccountStore(Path.Combine(output, terrain.ToString())).Load();
                Check(saved.run.phase == RunPhase.Extracted && string.IsNullOrEmpty(saved.items.Single(x => x.instanceId == reward.instanceId).originRunId), terrain + ": extraction persists reward ownership");
            }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "PASS", checks }, Formatting.Indented));
            return "PASS " + checks.Count;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = error.ToString() }, Formatting.Indented));
            throw;
        }
    }
}
