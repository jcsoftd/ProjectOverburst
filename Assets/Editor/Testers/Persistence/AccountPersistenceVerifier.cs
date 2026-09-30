using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using Overburst.Persistence;

public static class AccountPersistenceVerifier
{
    public static string Run(string outputDirectory)
    {
        var registry = AccountContentRegistryBuilder.Build();
        string directory = Path.Combine(Path.GetFullPath(outputDirectory), "fixture-" + Guid.NewGuid().ToString("N"));
        var passed = new List<string>();
        var state = new AccountSnapshot();
        state.inventory.AddRange(Enumerable.Repeat<string>(null, state.inventoryCapacity));
        for (int i = 0; i < 3; i++) state.stashTabs.Add(new ItemContainerSnapshot { slots = Enumerable.Repeat<string>(null, state.stashCapacity).ToList() });
        state.weapons.Add(null); state.gear.AddRange(Enumerable.Repeat<string>(null, 7)); state.bags.Add(null);
        state.flasks.AddRange(Enumerable.Repeat<string>(null, 3));
        for (int i = 0; i < 10; i++) state.quickSlots.Add(new QuickSlotSnapshot());
        var store = new EasySaveAccountStore(directory);
        Check(store.Load() == null, "new profile", passed);
        var commands = new AccountTransactions(state, store, registry);
        commands.Execute("initial", 0, x => x.experience = 10);
        Check(new EasySaveAccountStore(directory).Load().experience == 10, "ES3 round trip", passed);
        Check(!commands.Execute("initial", -1, x => x.experience = 999), "duplicate transaction", passed);
        store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture before-write"); };
        try { commands.Execute("failed", 1, x => x.experience = 99); throw new Exception("Fault did not fire"); } catch (IOException) { }
        Check(commands.Read().experience == 10 && commands.Revision == 1, "write failure rollback", passed);
        store.FaultInjector = point => { if (point == "after-write") throw new IOException("fixture after-write"); };
        commands.Execute("uncertain", 1, x => x.experience = 20);
        Check(commands.Read().experience == 20 && new EasySaveAccountStore(directory).Load().experience == 20, "uncertain commit resolved by readback", passed);
        try { commands.Execute("initial", 0, x => x.experience = 999); throw new Exception("Old request was replayed"); } catch (InvalidOperationException) { }
        Check(commands.Read().experience == 20 && commands.Revision == 2, "old duplicate rejected by revision without history", passed);
        store.FaultInjector = null;
        var candidates = AssetDatabase.FindAssets("t:BaseItemData", new[] { "Assets/ProjectOverburst" })
            .Select(g => AssetDatabase.LoadAssetAtPath<BaseItemData>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).ToArray();
        var data = candidates.First(x => x is CurrencyItemData);
        var runtime = new ItemData(data, 1, (ItemGrade)0);
        var item = ItemSnapshotCodec.Capture(runtime, registry);
        var randomBefore = UnityEngine.Random.state;
        var restored = ItemSnapshotCodec.Restore(item, registry);
        Check(UnityEngine.JsonUtility.ToJson(randomBefore) == UnityEngine.JsonUtility.ToJson(UnityEngine.Random.state), "restore does not consume RNG", passed);
        Check(UnityEngine.JsonUtility.ToJson(item) == UnityEngine.JsonUtility.ToJson(ItemSnapshotCodec.Capture(restored, registry)), "exact instance restore", passed);
        int qualityCases = 0;
        foreach (var asset in candidates.Where(x => x is WeaponItemData || x is GearItemData || x is BagItemData || x is FlaskItemData))
            foreach (ItemGrade grade in Enum.GetValues(typeof(ItemGrade)))
            {
                if (asset is FlaskItemData && grade == ItemGrade.Cursed) continue;
                var sourceItem = new ItemData(asset, 73, grade);
                var saved = ItemSnapshotCodec.Capture(sourceItem, registry);
                var rng = UnityEngine.JsonUtility.ToJson(UnityEngine.Random.state);
                var loaded = ItemSnapshotCodec.Restore(saved, registry);
                loaded.EnsureRuntimeState(); loaded.EnsureWeaponGradeStatRolls();
                if (asset is FlaskItemData) FlaskRuntime.State(loaded);
                if (rng != UnityEngine.JsonUtility.ToJson(UnityEngine.Random.state) || UnityEngine.JsonUtility.ToJson(saved) != UnityEngine.JsonUtility.ToJson(ItemSnapshotCodec.Capture(loaded, registry)))
                    throw new Exception("Quality restore changed: " + asset.name + " / " + grade);
                qualityCases++;
            }
        passed.Add("all equipment quality restore " + qualityCases);
        commands.Execute("prepare", commands.Revision, x => AccountRunCommands.PrepareEntry(x, "run-1", new MapInstanceState(), null));
        commands.Execute("ready", commands.Revision, x => AccountRunCommands.Activate(x, "run-1"));
        item.count = 7;
        commands.Execute("pickup", commands.Revision, x => AccountRunCommands.Acquire(x, "run-1", item));
        commands.Execute("transfer", commands.Revision, x => AccountRunCommands.Transfer(x, "run-1", "object-1", item.instanceId, 0, 0));
        try { commands.Execute("transfer-again", commands.Revision, x => AccountRunCommands.Transfer(x, "run-1", "object-1", item.instanceId, 0, 1)); throw new Exception("Transfer object was reused"); } catch (InvalidOperationException) { passed.Add("transfer object one use"); }
        var lost = ItemSnapshotCodec.CopyValues(item); lost.instanceId = Guid.NewGuid().ToString("N");
        commands.Execute("pickup-2", commands.Revision, x => AccountRunCommands.Acquire(x, "run-1", lost));
        commands.Execute("xp", commands.Revision, x => x.experience = 30);
        commands.Execute("clear", commands.Revision, x => AccountRunCommands.ClearBoss(x, "run-1", DateTime.UtcNow.Ticks));
        commands.Execute("clear-again", commands.Revision, x => AccountRunCommands.ClearBoss(x, "run-1", DateTime.UtcNow.Ticks));
        Check(commands.Read().bossClearCount == 1, "boss clear counted once without lifetime run ID list", passed);
        var restarted = new EasySaveAccountStore(directory).Load();
        AccountRunCommands.RecoverInterrupted(restarted);
        Check(restarted.items.Count == 1 && restarted.items[0].count == 7 && restarted.items[0].originRunId == null && restarted.experience == 30 && restarted.bossClearCount == 1, "restart failure preserves XP transfer and clear", passed);
        var latest = store.Generation % 2 == 1 ? "account-A.es3" : "account-B.es3";
        File.WriteAllText(Path.Combine(directory, latest), "corrupt fixture");
        var recovered = new EasySaveAccountStore(directory).Load();
        Check(recovered != null && recovered.revision == commands.Revision - 1, "corrupt latest generation fallback", passed);
        File.WriteAllText(Path.Combine(directory, latest == "account-A.es3" ? "account-B.es3" : "account-A.es3"), "corrupt fixture");
        try { new EasySaveAccountStore(directory).Load(); throw new Exception("Corruption was silently reset"); } catch (InvalidDataException) { passed.Add("both corrupt preserved and blocked"); }
        string result = string.Join("\n", passed.Select(x => "PASS " + x));
        File.WriteAllText(Path.Combine(directory, "result.txt"), result);
        return result + "\n" + directory;
    }

    private static void Check(bool success, string name, List<string> passed)
    {
        if (!success) throw new Exception("FAIL " + name);
        passed.Add(name);
    }
}
