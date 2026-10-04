#if DEVELOPMENT_BUILD && OVERBURST_SAVE_RECOVERY_QA
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEngine;

// QA 전용 빌드 정의와 전용 계정/출력 환경 변수가 모두 있어야 실행된다.
// 실제 저장/런/부팅 코드를 호출하고, 외부 제어기가 이 Player PID만 종료한다.
public sealed class SaveRecoveryPlayerProbe : MonoBehaviour
{
    const string Prefix = "OVERBURST_RECOVERY_QA_";
    static string output, accountDirectory, mode, caseName;
    static AccountSnapshot rawBeforeBoot;
    AccountGameplaySession session;
    AccountContentRegistry registry;
    EasySaveAccountStore store;
    string mapId, transferId, lostId, stashSeedId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ReadDurableBeforeRecovery()
    {
        mode = Environment.GetEnvironmentVariable(Prefix + "MODE");
        if (string.IsNullOrEmpty(mode)) return;
        output = Environment.GetEnvironmentVariable(Prefix + "OUTPUT");
        accountDirectory = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
        caseName = Environment.GetEnvironmentVariable(Prefix + "CASE");
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(accountDirectory))
            throw new InvalidOperationException("QA requires explicit output and isolated account.");
        output = Path.GetFullPath(output);
        accountDirectory = Path.GetFullPath(accountDirectory);
        string marker = Path.Combine(output, "qa-owner.txt");
        string allowedSegment = Path.DirectorySeparatorChar + "개인파일" + Path.DirectorySeparatorChar + "코덱스산출" + Path.DirectorySeparatorChar;
        if (output.IndexOf(allowedSegment, StringComparison.OrdinalIgnoreCase) < 0
            || !accountDirectory.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(marker) || File.ReadAllText(marker).Trim() != "KAN-26-20261005")
            throw new InvalidOperationException("QA paths or owner marker do not match.");
        rawBeforeBoot = new EasySaveAccountStore(accountDirectory).Load();
        Write("preboot-" + mode + ".json", rawBeforeBoot);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        if (string.IsNullOrEmpty(mode)) return;
        var go = new GameObject("KAN26_SaveRecoveryPlayerProbe");
        DontDestroyOnLoad(go);
        Application.runInBackground = true;
        go.AddComponent<SaveRecoveryPlayerProbe>().StartCoroutine(WaitAndRun());
    }

    static IEnumerator WaitAndRun()
    {
        float end = Time.realtimeSinceStartup + 150;
        while ((!AccountBootstrap.Ready || !WorldSessionState.IsHideout)
            && Time.realtimeSinceStartup < end) yield return null;
        var probe = FindFirstObjectByType<SaveRecoveryPlayerProbe>();
        try { probe.Execute(); }
        catch (Exception error)
        {
            Write("failure-" + mode + ".json", new { status = "FAIL", error = error.ToString(), bootstrap = AccountBootstrap.Error });
            Application.Quit(2);
        }
        while (true) yield return null;
    }

    void Execute()
    {
        Require(AccountBootstrap.Ready && WorldSessionState.IsHideout, "Actual account and Hideout boot complete");
        Require(Path.GetFullPath(AccountBootstrap.SaveDirectory) == accountDirectory, "Player uses owned isolated account");
        session = AccountGameplaySession.Current;
        registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        Require(session != null && registry != null, "Actual session/content registry");
        var transactions = typeof(AccountGameplaySession).GetField("transactions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        store = (EasySaveAccountStore)typeof(AccountTransactions).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(transactions);
        if (mode == "verify") { VerifyRestart(); return; }
        Require(mode == "crash", "Supported QA mode");
        Seed();
        if (caseName == "checkpoint")
        {
            Require(session.ExecuteState("qa-checkpoint", s => s.experience += 7), "Saved checkpoint");
            Hold("after-checkpoint", session.Read());
            return;
        }
        if (caseName == "entry-pending" || caseName == "entry-active")
        {
            int writes = 0;
            store.FaultInjector = point =>
            {
                if (point != "after-write") return;
                writes++;
                if (writes == (caseName == "entry-pending" ? 1 : 2)) Hold(point, session.Read());
            };
            var map = session.Read().items.Single(i => i.instanceId == mapId).map;
            string sceneName = Environment.GetEnvironmentVariable(Prefix + "SCENE");
            Require(!string.IsNullOrWhiteSpace(sceneName), "Explicit real dungeon build scene");
            Require(PersistentSceneFlow.EnsureInstance().EnterRun(sceneName, map, mapId), "Actual scene-flow EnterRun accepted");
            Write("waiting-entry.json", new { status = "RUNNING", caseName });
            return;
        }
        var run = new AccountRunSession(session);
        string runId = Guid.NewGuid().ToString("N");
        var selectedMap = session.Read().items.Single(i => i.instanceId == mapId).map;
        Require(run.Prepare(runId, selectedMap, mapId) && run.Activate(runId), "Production run preparation and map consumption");
        var currency = registry.Entries.Select(e => e.asset).OfType<CurrencyItemData>().First(c => c.maxStack >= 32);
        var transfer = ItemSnapshotCodec.Capture(new ItemData(currency, 1, ItemGrade.Common), registry);
        transfer.instanceId = transferId; transfer.count = 7;
        var lost = ItemSnapshotCodec.CopyValues(transfer); lost.instanceId = lostId; lost.count = 3;
        Require(session.ExecuteState("qa-acquire", s => { AccountRunCommands.Acquire(s, runId, transfer); AccountRunCommands.Acquire(s, runId, lost); }), "Run loot acquired durably");
        Write("before-boundary.json", session.Read());
        if (caseName == "transfer-before" || caseName == "transfer-after")
        {
            Arm(caseName.EndsWith("before") ? "before-write" : "after-write");
            Require(run.Transfer(runId, "qa-transfer-object", transferId), "Actual transfer");
        }
        else if (caseName == "settlement-before" || caseName == "settlement-after")
        {
            Require(run.Transfer(runId, "qa-transfer-object", transferId), "Transferred loot before settlement");
            Require(run.ClaimEventCard(runId, "qa-card", 11), "One-time reward claim");
            Require(run.ClearBoss(runId, DateTime.UtcNow.Ticks), "Boss clear checkpoint");
            Write("before-boundary.json", session.Read());
            Arm(caseName.EndsWith("before") ? "before-write" : "after-write");
            Require(run.Extract(runId), "Actual extraction/settlement");
        }
        else throw new InvalidOperationException("Unknown crash case: " + caseName);
        throw new InvalidOperationException("Crash boundary did not stop execution.");
    }

    void Seed()
    {
        Require(!File.Exists(Path.Combine(output, "seed.json")), "Fresh case output");
        var mapDefinition = registry.Entries.Select(e => e.asset).OfType<MapItemData>().First();
        var mapItem = new ItemData(mapDefinition, 1, ItemGrade.Common)
        {
            mapState = new MapInstanceState { mapContentId = registry.IdFor(mapDefinition), level = 1, grade = ItemGrade.Common }
        };
        var map = ItemSnapshotCodec.Capture(mapItem, registry);
        mapId = map.instanceId;
        transferId = Guid.NewGuid().ToString("N"); lostId = Guid.NewGuid().ToString("N");
        var currency = registry.Entries.Select(e => e.asset).OfType<CurrencyItemData>().First(c => c.maxStack >= 32);
        var stashSeed = ItemSnapshotCodec.Capture(new ItemData(currency, 1, ItemGrade.Common, 5), registry);
        stashSeedId = stashSeed.instanceId;
        Require(session.ExecuteState("qa-seed", s =>
        {
            int slot = s.inventory.FindIndex(0, s.unlockedSlots, string.IsNullOrEmpty);
            if (slot < 0) throw new InvalidOperationException("Fresh account has no free map slot.");
            s.items.Add(map); s.inventory[slot] = mapId;
            int stashSlot = s.stashTabs[0].slots.FindIndex(string.IsNullOrEmpty);
            if (stashSlot < 0) throw new InvalidOperationException("Fresh account has no free stash slot.");
            s.items.Add(stashSeed); s.stashTabs[0].slots[stashSlot] = stashSeedId;
        }), "Traceable map seed checkpoint");
        Write("seed.json", new { caseName, mapId, transferId, lostId, stashSeedId, snapshot = session.Read() });
    }

    void Arm(string boundary)
    {
        store.FaultInjector = point => { if (point == boundary) Hold(point, session.Read()); };
    }

    static void Hold(string boundary, AccountSnapshot visible)
    {
        Write("crash-ready.json", new { status = "READY_FOR_OS_KILL", boundary, caseName,
            pid = System.Diagnostics.Process.GetCurrentProcess().Id, utc = DateTime.UtcNow, memory = visible });
        // 정상 종료·Application.Quit·OnApplicationQuit을 거치지 않고 외부 제어기가 이 PID를 종료한다.
        while (true) Thread.Sleep(100);
    }

    void VerifyRestart()
    {
        Require(rawBeforeBoot != null, "Durable generation loaded before bootstrap");
        var seed = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(output, "seed.json")));
        string savedMapId = (string)seed["mapId"], savedTransferId = (string)seed["transferId"],
            savedLostId = (string)seed["lostId"], savedStashId = (string)seed["stashSeedId"];
        var baseline = seed["snapshot"].ToObject<AccountSnapshot>();
        string priorResultPath = Path.Combine(output, "verify-result.json");
        bool repeatedRestart = File.Exists(priorResultPath);
        if (repeatedRestart)
        {
            var priorResult = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(priorResultPath));
            Require((string)priorResult["status"] == "PASS" && (string)priorResult["caseName"] == caseName, "Same case first restart succeeded");
            var previous = priorResult["snapshot"].ToObject<AccountSnapshot>();
            Require(JsonConvert.SerializeObject(previous) == JsonConvert.SerializeObject(rawBeforeBoot), "Second restart retains the exact first recovered durable account");
        }
        else if (caseName == "checkpoint") Require(rawBeforeBoot.experience == baseline.experience + 7, "Checkpoint XP durable exactly once");
        else
        {
            RunPhase phase = caseName == "entry-pending" ? RunPhase.EntryPending
                : caseName == "settlement-before" ? RunPhase.BossCleared
                : caseName == "settlement-after" ? RunPhase.Extracted : RunPhase.Active;
            Require(rawBeforeBoot.run != null && rawBeforeBoot.run.phase == phase, "Expected durable run phase at OS kill");
            Require(rawBeforeBoot.items.Any(i => i.instanceId == savedMapId) == (caseName == "entry-pending"), "Map held before activation and consumed once afterwards");
            if (caseName.StartsWith("transfer") || caseName.StartsWith("settlement"))
            {
                bool transferred = caseName != "transfer-before";
                Require(rawBeforeBoot.run.transferredObjects.Count == (transferred ? 1 : 0), "Transfer object ledger exactly once");
                Require(rawBeforeBoot.items.Single(i => i.instanceId == savedStashId).count == (transferred ? 12 : 5), "Merged stash quantity conserved");
                Require(rawBeforeBoot.items.Any(i => i.instanceId == savedTransferId) == !transferred, "Merged source identity removed only after durable transfer");
                Require(rawBeforeBoot.items.Single(i => i.instanceId == savedLostId).count == 3, "Unextracted loot quantity present in raw durable save");
            }
            if (caseName.StartsWith("settlement"))
            {
                Require(rawBeforeBoot.experience == baseline.experience + 11, "Event reward granted once");
                Require(rawBeforeBoot.bossClearCount == baseline.bossClearCount + 1, "Boss reward counted once");
                Require(rawBeforeBoot.run.rewardedEncounters.Count == 1, "Event reward ledger contains one identity");
            }
        }
        var expected = ItemSnapshotCodec.CopyValues(rawBeforeBoot);
        bool interrupted = AccountRunCommands.RecoverInterrupted(expected);
        var actual = session.Read();
        AccountInvariants.Validate(actual, registry);
        long revision = actual.revision;
        expected.revision = actual.revision = 0;
        expected.lastTransactionId = actual.lastTransactionId = null;
        Require(JsonConvert.SerializeObject(expected) == JsonConvert.SerializeObject(actual), "Recovered ownership, quantities, reward/run state exactly match durable policy");
        actual.revision = revision;
        var inventory = PlayerAccountInventoryService.SharedInventory;
        Require(inventory != null && inventory.Items.Count(i => i != null) == actual.inventory.Count(i => !string.IsNullOrEmpty(i)), "Actual live inventory projection");
        foreach (var id in actual.inventory.Where(i => !string.IsNullOrEmpty(i)))
        {
            var item = actual.items.Single(i => i.instanceId == id);
            Require(inventory.Items.Any(i => i != null && i.runtimeInstanceId == id && i.stackCount == item.count), "Live item identity and quantity: " + id);
        }
        Require(!repeatedRestart || !interrupted, "Recovered run is not recovered a second time");
        Write("verify-result.json", new { status = "PASS", caseName, repeatedRestart, interrupted, revision,
            uniqueItems = actual.items.Count, snapshot = session.Read(), actualInventoryProjection = true });
        Application.Quit(0);
    }

    static void Require(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }
    static void Write(string file, object data)
    {
        File.WriteAllText(Path.Combine(output, file), JsonConvert.SerializeObject(data, Formatting.Indented));
    }
}
#endif
