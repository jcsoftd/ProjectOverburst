using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;

public static class ElementGemVerifier
{
    public static string Data(string output)
    {
        var checks = new List<string>();
        var definitions = Resources.LoadAll<ElementGemItemData>("Items/ElementGems");
        Require(definitions.Length == 32, "32 definitions", checks);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var wrappers = new HashSet<GameObject>();
        int samples = 0;
        var before = UnityEngine.Random.state;
        foreach (var data in definitions)
        {
            Require(data.icon != null && data.worldPickupPrefab != null && data.worldPickupPrefab.GetComponent<WorldItemPickup>() != null, data.name + " references", checks);
            Require(ElementGemItemData.IsAllowed(data.element, data.fixedGrade), data.name + " legal element/grade", checks);
            Require(registry.Resolve<ElementGemItemData>(registry.IdFor(data)) == data, data.name + " stable ID", checks);
            wrappers.Add(data.worldPickupPrefab);
            foreach (ElementGemArchetype archetype in Enum.GetValues(typeof(ElementGemArchetype)))
            for (int seed = 0; seed < 100; seed++)
            {
                var first = ElementGemQuality.Roll(data, seed, archetype);
                var second = ElementGemQuality.Roll(data, seed, archetype);
                if (!ElementGemQuality.IsValid(data, 1, data.fixedGrade, 1, first)) throw new Exception("Invalid generated gem " + data.name);
                if (JsonConvert.SerializeObject(first) != JsonConvert.SerializeObject(second)) throw new Exception("Seed reproducibility failed.");
                var clone = first.Copy();
                if (ReferenceEquals(first.rolls, clone.rolls) || ReferenceEquals(first.rolls[0].stars, clone.rolls[0].stars)) throw new Exception("Shallow gem copy.");
                var item = new ItemData(data, 100, data.fixedGrade) { gemState = first };
                var copy = item.CopyStack(1, false);
                if (ReferenceEquals(item.gemState, copy.gemState)) throw new Exception("Shallow item copy.");
                item.EnsureRuntimeState();
                var values = ElementGemQuality.Calculate(item);
                foreach (var row in first.rolls)
                {
                    float value = ElementGemQuality.Value(item, row);
                    if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new Exception("Invalid calculated value.");
                }
                var invalid = first.Copy(); invalid.rolls[1].optionId = invalid.rolls[0].optionId;
                if (ElementGemQuality.IsValid(data, 1, data.fixedGrade, 1, invalid)) throw new Exception("Duplicate accepted.");
                samples++;
            }
        }
        Require(wrappers.Count == 5, "5 gameplay wrappers", checks);
        Require(ElementGemQuality.Catalog.Count == 21, "12 element and 9 common options", checks);
        Require(UnityEngine.Random.state.Equals(before), "Unity RNG preserved for gem generation/copy", checks);
        Require(!ElementGemItemData.IsAllowed(WeaponElement.Light, ItemGrade.Epic) && !ElementGemItemData.IsAllowed(WeaponElement.Dark, ItemGrade.Common), "upper element floor", checks);
        string result = JsonConvert.SerializeObject(new { goal = 1, status = "PASS", samples, checks }, Formatting.Indented);
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "GOAL01.json"), result);
        return result;
    }
    public static void Require(bool condition, string label, List<string> checks)
    { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }

    public static string Persistence(string output)
    {
        var checks = new List<string>();
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var state = NewAccountFactory.Create(registry);
        Require(state.schemaVersion == 2 && state.gear.Count == 6 && state.level == 1 && state.experience == 0 && state.elementalGemInstanceId == null, "explicit fresh account", checks);
        var definition = registry.Resolve<ElementGemItemData>("item.elementgem.fire.legendary");
        var gem = new ItemData(definition, 20, definition.fixedGrade);
        gem.EnsureAcquisitionOrder();
        var saved = ItemSnapshotCodec.Capture(gem, registry);
        state.items.Add(saved); state.inventory[0] = saved.instanceId;
        AccountInvariants.Validate(state, registry);
        var rng = UnityEngine.Random.state;
        string quality = JsonConvert.SerializeObject(saved.gemState);
        for (int i = 0; i < 3; i++)
        {
            var restored = ItemSnapshotCodec.Restore(saved, registry);
            restored.EnsureRuntimeState();
            Require(JsonConvert.SerializeObject(restored.gemState) == quality, "restore quality " + i, checks);
            Require(!ReferenceEquals(saved.gemState.rolls[0].stars, restored.gemState.rolls[0].stars), "restore deep copy " + i, checks);
            Require(JsonConvert.SerializeObject(ItemSnapshotCodec.Capture(restored.CopyStack(1, false), registry).gemState) == quality, "copy capture quality " + i, checks);
        }
        Require(UnityEngine.Random.state.Equals(rng), "restore/copy/capture RNG 0", checks);
        state.inventory[0] = null; state.elementalGemInstanceId = saved.instanceId;
        AccountInvariants.Validate(state, registry);
        var bad = ItemSnapshotCodec.CopyValues(state); bad.inventory[1] = saved.instanceId;
        Reject(() => AccountInvariants.Validate(bad, registry), "multiply owned gem", checks);
        bad = ItemSnapshotCodec.CopyValues(state); bad.elementalGemInstanceId = null;
        Reject(() => AccountInvariants.Validate(bad, registry), "orphan gem", checks);
        bad = ItemSnapshotCodec.CopyValues(state); bad.schemaVersion = 1;
        Reject(() => AccountInvariants.Validate(bad, registry), "old schema", checks);
        var invalid = ItemSnapshotCodec.CopyValues(saved); invalid.balanceVersion--;
        Reject(() => ItemSnapshotCodec.Restore(invalid, registry), "unsupported balance version", checks);
        invalid = ItemSnapshotCodec.CopyValues(saved); invalid.contentId = "missing.gem";
        Reject(() => ItemSnapshotCodec.Restore(invalid, registry), "missing content", checks);
        invalid = ItemSnapshotCodec.CopyValues(saved); invalid.gemState.rolls[0].stars.Add((WeaponGradeStarType)99);
        Reject(() => ItemSnapshotCodec.Restore(invalid, registry), "invalid gem color", checks);
        invalid = ItemSnapshotCodec.CopyValues(saved); invalid.count = 2;
        Reject(() => ItemSnapshotCodec.Restore(invalid, registry), "stacked gem", checks);
        string directory = Path.Combine(output, "GOAL02_Account_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new EasySaveAccountStore(directory); store.Save(state, "first");
        store.Save(state, "second");
        for (int i = 0; i < 2; i++)
            Require(JsonConvert.SerializeObject(new EasySaveAccountStore(directory).Load()) == JsonConvert.SerializeObject(state), "disk reboot " + i, checks);
        using (new FileStream(Path.Combine(directory, "account.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Reject(() => new EasySaveAccountStore(directory).Load(), "writer lock exclusion", checks);
        var transactions = new AccountTransactions(state, store, registry, true);
        store.FaultInjector = point => { if (point == "before-write") throw new IOException("intentional test fault"); };
        transactions.Execute("memory-change", transactions.Revision, candidate => candidate.experience = 1);
        Reject(() => transactions.FlushPendingSave(), "periodic flush fault", checks);
        Require(transactions.HasPendingSave && transactions.Read().experience == 1, "committed memory and dirty preserved", checks);
        long revision = transactions.Revision;
        Reject(() => transactions.Execute("immediate-change", revision, candidate => candidate.experience = 2, true), "immediate save fault", checks);
        Require(transactions.Revision == revision && transactions.Read().experience == 1, "unpublished immediate candidate preserved", checks);
        store.FaultInjector = null; transactions.FlushPendingSave();
        transactions.GrantExperience("progression-fast", transactions.Revision, 1, out _, out _);
        Require(JsonConvert.SerializeObject(transactions.Read().items.Single(x => x.instanceId == saved.instanceId).gemState) == quality, "progression fast path preserves gem", checks);
        store.FaultInjector = point => { if (point == "after-write") throw new IOException("uncertain test fault"); };
        transactions.FlushPendingSave();
        Require(!transactions.HasPendingSave && new EasySaveAccountStore(directory).Load().experience == 2, "uncertain write readback reconciliation", checks);
        store.FaultInjector = null;
        string a = Path.Combine(directory, "account-A.es3"), b = Path.Combine(directory, "account-B.es3");
        byte[] aBytes = File.ReadAllBytes(a), bBytes = File.ReadAllBytes(b);
        File.WriteAllText(b, "intentionally corrupt inactive/current fixture");
        Require(new EasySaveAccountStore(directory).Load() != null, "valid other generation fallback", checks);
        File.WriteAllBytes(b, bBytes);
        var future = new AccountSaveEnvelope { profileId = "default", schemaVersion = 99, generation = 100,
            transactionId = "future", payload = ES3.Serialize(state), writtenAtUtcTicks = DateTime.UtcNow.Ticks };
        future.checksum = EasySaveAccountStore.Checksum(future);
        ES3.Save("account", future, new ES3Settings(b));
        byte[] futureBytes = File.ReadAllBytes(b);
        Reject(() => new EasySaveAccountStore(directory).Load(), "latest unsupported schema rejects fallback", checks);
        Require(futureBytes.SequenceEqual(File.ReadAllBytes(b)) && aBytes.SequenceEqual(File.ReadAllBytes(a)), "rejected files preserved", checks);
        File.WriteAllBytes(b, bBytes);
        string result = JsonConvert.SerializeObject(new { goal = 2, status = "PASS", account = directory, checks }, Formatting.Indented);
        File.WriteAllText(Path.Combine(output, "GOAL02.json"), result);
        return result;
    }
    static void Reject(Action action, string label, List<string> checks)
    {
        bool rejected = false;
        try { action(); } catch (IOException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
        Require(rejected, label, checks);
    }
}
