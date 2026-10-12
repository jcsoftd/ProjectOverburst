using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

// Uses isolated account files; the Play half exercises the actual session and merchant projection.
public static class MapEscapeSettlementVerifier
{
    static AccountSnapshot Fixture(AccountContentRegistry registry, MapTerrainKind terrain, string id)
    {
        var state = NewAccountFactory.Create(registry); AccountCharacterCommands.EnsureLegacy(state);
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var entry = MapGenerationPolicy.CreateItem(definition, registry, 1, ItemGrade.Common);
        var map = entry.mapState;
        map.terrain = terrain; map.monsterThemeId = terrain == MapTerrainKind.Cave ? "CavernMutants" : "PrimalHunt";
        map.monsterThemes[0].themeId = map.monsterThemeId;
        if (terrain == MapTerrainKind.Cave) { map.areaCount = 4; map.desertCandidateId = null; map.width = map.depth = 0; }
        else { var c = MapTerrainCatalog.DesertCandidates[0]; map.desertCandidateId = c.id; map.areaCount = c.areaCount; map.width = c.width; map.depth = c.depth; }
        var item = ItemSnapshotCodec.Capture(entry, registry);
        item.acquisitionOrder = state.nextAcquisitionOrder++;
        state.items.Add(item); state.inventory[0] = item.instanceId;
        AccountRunCommands.PrepareEntry(state, id, map, item.instanceId);
        AccountRunCommands.Activate(state, id);
        var loot = MapGenerationPolicy.CreateItem(definition, registry, 1, ItemGrade.Common, id);
        var acquired = ItemSnapshotCodec.Capture(loot, registry); acquired.acquisitionOrder = state.nextAcquisitionOrder++;
        AccountRunCommands.Acquire(state, id, acquired);
        AccountInvariants.Validate(state, registry);
        return state;
    }
    static void Complete(AccountSnapshot state, AccountContentRegistry registry)
    {
        var item = MapRewardPolicy.CreateBossMap(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"), registry, 1, .1f, .1f, state.run.runId);
        item.runtimeInstanceId = AccountRunCommands.CompletionRewardId(state.run.runId);
        AccountRunCommands.CompleteMap(state, state.run.runId, DateTime.UtcNow.Ticks, 1, Vector3.one,
            ItemSnapshotCodec.Capture(item, registry), state.run.map.terrain == MapTerrainKind.Desert);
    }
    static void Throws<T>(Action action, Action<bool, string> check, string label) where T : Exception
    {
        try { action(); } catch (T) { check(true, label); return; }
        check(false, label);
    }
    static string Stock(AccountSnapshot state)
    {
        var ids = new HashSet<string>(state.merchants.SelectMany(m => m.stock.Concat(m.currency)));
        return JsonConvert.SerializeObject(new { state.merchants, items = state.items.Where(i => ids.Contains(i.instanceId)).OrderBy(i => i.instanceId) });
    }
    public static string RunNative(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Use idle EditMode.");
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        output = Path.GetFullPath(output);
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Use new owned evidence.");
        Directory.CreateDirectory(output);
        var checks = new List<string>(); var random = UnityEngine.Random.state;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        try
        {
            foreach (var terrain in new[] { MapTerrainKind.Cave, MapTerrainKind.Desert })
            {
                string id = "escape-native-" + terrain;
                var initial = Fixture(registry, terrain, id);
                string lootId = initial.items.Single(i => i.originRunId == id).instanceId;
                Check(initial.items.All(i => i.instanceId != initial.run.mapInstanceId), terrain + ": entry map consumed before escape");
                Throws<InvalidOperationException>(() => AccountRunCommands.Extract(ItemSnapshotCodec.CopyValues(initial), id), Check, terrain + ": ordinary extraction rejects incomplete run");
                var store = new EasySaveAccountStore(Path.Combine(output, terrain.ToString())); store.Save(initial, "initial");
                var tx = new AccountTransactions(initial, store, registry, true);
                var before = ES3.Serialize(tx.Read()); long revision = tx.Revision;
                store.FaultInjector = p => { if (p == "before-write") throw new IOException("escape fixture before-write"); };
                Throws<IOException>(() => tx.Execute("extract", revision, s => AccountRunCommands.Extract(s, id, true), true), Check, terrain + ": write fault injected");
                Check(before.SequenceEqual(ES3.Serialize(tx.Read())) && tx.Revision == revision && store.Load().run.phase == RunPhase.Active,
                    terrain + ": failed escape keeps complete memory and disk state");
                store.FaultInjector = p => { if (p == "after-write") throw new IOException("escape fixture after-write"); };
                Check(tx.Execute("extract", revision, s => AccountRunCommands.Extract(s, id, true), true), terrain + ": ambiguous successful write resolves through readback");
                var saved = new EasySaveAccountStore(Path.Combine(output, terrain.ToString())).Load();
                Check(saved.run.phase == RunPhase.Extracted && saved.run.exitReason == RunExitReason.EmergencyEscape, terrain + ": escape reason persists across ES3 reload");
                Check(saved.items.Single(i => i.instanceId == lootId).originRunId == null && saved.items.All(i => i.instanceId != saved.run.mapInstanceId), terrain + ": carried loot retained without entry map refund");
                Check(saved.run.completionReward == null && saved.bossClearCount == initial.bossClearCount && Stock(saved) == Stock(initial), terrain + ": no completion map, boss count or merchant mutation");
                Check(!AccountRunCommands.RecoverInterrupted(saved), terrain + ": completed escape survives restart recovery");
                Throws<InvalidOperationException>(() => AccountRunCommands.PrepareEntry(saved, id, saved.run.map, null, true), Check, terrain + ": consumed run identity cannot re-enter");
                var completed = ItemSnapshotCodec.CopyValues(initial); Complete(completed, registry);
                AccountRunCommands.Extract(completed, id, true); AccountInvariants.Validate(completed, registry);
                Check(completed.run.exitReason == RunExitReason.Completed && !completed.run.completionRewardClaimed
                    && completed.items.All(i => i.instanceId != completed.run.completionReward.instanceId), terrain + ": completed state wins; unpicked floor reward is not auto-acquired");
                var legacy = JToken.Parse(Encoding.UTF8.GetString(ES3.Serialize(completed)));
                foreach (var field in legacy.SelectTokens("$..exitReason").ToArray()) field.Parent.Remove();
                var loaded = ES3.Deserialize<AccountSnapshot>(Encoding.UTF8.GetBytes(legacy.ToString()));
                AccountInvariants.Validate(loaded, registry);
                Check(loaded.run.exitReason == RunExitReason.None && loaded.run.phase == RunPhase.Extracted, terrain + ": older saves without exit reason stay readable");
                var corrupt = ItemSnapshotCodec.CopyValues(initial); corrupt.run.exitReason = RunExitReason.EmergencyEscape;
                Throws<InvalidDataException>(() => AccountInvariants.Validate(corrupt, registry), Check, terrain + ": active run cannot claim an exit reason");
                corrupt = ItemSnapshotCodec.CopyValues(completed); corrupt.run.exitReason = RunExitReason.EmergencyEscape;
                Throws<InvalidDataException>(() => AccountInvariants.Validate(corrupt, registry), Check, terrain + ": emergency reason cannot contain a completion reward");
                corrupt = ItemSnapshotCodec.CopyValues(saved); corrupt.run.exitReason = (RunExitReason)99;
                Throws<InvalidDataException>(() => AccountInvariants.Validate(corrupt, registry), Check, terrain + ": undefined saved reason rejected");
            }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "PASS", checks }, Formatting.Indented));
            return "PASS " + checks.Count;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = e.ToString() }, Formatting.Indented)); throw; }
        finally { UnityEngine.Random.state = random; }
    }

    public static void VerifyPlay(string output, Action<bool, string> check)
    {
        if (!EditorApplication.isPlaying || string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) throw new InvalidOperationException("Isolated Play required.");
        var original = AccountGameplaySession.Current;
        var owner = PlayerAccountInventoryService.Instance;
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var baseline = original.Read(); var random = UnityEngine.Random.state; var oldPhase = WorldSessionState.Phase;
        var setPhase = typeof(WorldSessionState).GetMethod("SetPhase", BindingFlags.Static | BindingFlags.NonPublic);
        var service = UnityEngine.Object.FindFirstObjectByType<MerchantStockRefreshService>();
        var chance = typeof(MerchantStockRefreshService).GetField("successfulRunRefreshChance", BindingFlags.Instance | BindingFlags.NonPublic);
        if (!service) throw new InvalidOperationException("Actual merchant stock service required.");
        float oldChance = (float)chance.GetValue(service);
        AccountGameplaySession fixture = null;
        try
        {
            chance.SetValue(service, 1f); // A skipped refresh cannot pass merely because its random roll failed.
            foreach (var terrain in new[] { MapTerrainKind.Cave, MapTerrainKind.Desert })
            foreach (string mode in new[] { "escape", "completed", "death", "abandon" })
            {
                string id = "escape-play-" + terrain + "-" + mode;
                var state = Fixture(registry, terrain, id);
                AccountMerchantProjection.RefreshForSuccessfulRun(state, registry);
                if (mode == "completed") Complete(state, registry);
                check(state.merchants.Any(m => m.stock.Any(s => !string.IsNullOrEmpty(s))), id + ": initialized real merchant stock fixture");
                string stock = Stock(state); string loot = state.items.Single(i => i.originRunId == id).instanceId;
                var store = new EasySaveAccountStore(Path.Combine(output, id)); store.Save(state, "initial");
                fixture = new AccountGameplaySession(owner, registry, store, state, true); fixture.Attach();
                AccountGameplayProjection.Restore(state, owner, registry);
                setPhase.Invoke(null, new object[] { WorldPhase.Run });
                var session = new AccountRunSession(fixture);
                byte[] before = ES3.Serialize(fixture.Read());
                store.FaultInjector = p => { if (p == "before-write") throw new IOException("escape Play fixture failure"); };
                Throws<IOException>(() => session.ResolveOutcome(DateTime.UtcNow.Ticks, mode == "death", abandoned: mode == "abandon", emergencyRequested: true), check, id + ": session settlement fault injected");
                check(before.SequenceEqual(ES3.Serialize(fixture.Read())) && Stock(fixture.Read()) == stock && WorldSessionState.Phase == WorldPhase.Run,
                    id + ": failed session save publishes no loot, stock or world transition");
                store.FaultInjector = null;
                var outcome = session.ResolveOutcome(DateTime.UtcNow.Ticks, mode == "death", abandoned: mode == "abandon", emergencyRequested: true);
                var saved = store.Load(); bool success = mode == "escape" || mode == "completed";
                check(outcome == (success ? RunOutcome.Extracted : RunOutcome.Failed) && !fixture.NeedsProjectionRecovery, id + ": durable outcome and actual projection succeed");
                check((mode == "completed") != (Stock(saved) == stock), id + ": only completed run refreshes merchant stock with chance forced to one");
                check(saved.items.Any(i => i.instanceId == loot && i.originRunId == null) == success, id + ": extraction preserves loot; death or abandonment wins over escape");
                check(saved.run.exitReason == (mode == "escape" ? RunExitReason.EmergencyEscape : mode == "completed" ? RunExitReason.Completed : RunExitReason.None), id + ": terminal reason matches actual outcome");
                var projected = AccountGameplayProjection.Capture(ItemSnapshotCodec.CopyValues(saved), owner, registry);
                check(Stock(projected) == Stock(saved), id + ": live shop inventory matches saved stock after settlement");
                long revision = fixture.Revision;
                check(session.ResolveOutcome(DateTime.UtcNow.Ticks, false, emergencyRequested: true) == RunOutcome.None
                    && fixture.Revision == revision && Stock(fixture.Read()) == Stock(saved), id + ": repeated request has no write or second refresh");
                fixture.Detach(); fixture = null;
            }
        }
        finally
        {
            fixture?.Detach(); original.Attach(); AccountGameplayProjection.Restore(baseline, owner, registry);
            setPhase.Invoke(null, new object[] { oldPhase }); chance.SetValue(service, oldChance); UnityEngine.Random.state = random;
        }
    }
}
