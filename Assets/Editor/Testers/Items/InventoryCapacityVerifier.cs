using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;

public static class InventoryCapacityVerifier
{
    public static string Run(string outputDirectory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var checks = new List<string>();
        void Check(bool pass, string name)
        {
            if (!pass) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        var fresh = NewAccountFactory.Create(registry);
        Check(fresh.baseUnlockedSlots == 24 && fresh.unlockedSlots == 24, "New character starts with 24 slots");
        Check(fresh.inventoryCapacity == 60 && fresh.inventory.Count == 60, "Physical storage has 60 slots");
        Check(BagQuality.BaseInventorySlots + BagQuality.BaseSlots(100) + 2 * BagQuality.MaxStarsPerRow == 55
            && BagQuality.InventoryCapacity >= 55, "Largest bag bonus fits without truncation");

        var legacy = Legacy(registry);
        AccountCharacterCommands.EnsureLegacy(legacy);
        var dormant = Legacy(registry);
        foreach (var item in dormant.items) item.acquisitionOrder = legacy.nextAcquisitionOrder++;
        dormant.nextAcquisitionOrder = legacy.nextAcquisitionOrder;
        legacy.characters.Add(new AccountCharacterSnapshot { id = Guid.NewGuid().ToString("N"), name = "보관기사", personal = dormant });
        AccountInvariants.Validate(legacy, registry);
        var beforeInventory = legacy.inventory.ToArray();
        string beforeItems = JsonUtility.ToJson(legacy);
        string output = Path.Combine(Path.GetFullPath(outputDirectory), "Capacity-" + Guid.NewGuid().ToString("N"));
        var store = new EasySaveAccountStore(output);
        store.Save(legacy, "legacy-capacity-fixture");
        store.FaultInjector = stage => { if (stage == "before-write") throw new IOException("capacity fault fixture"); };
        bool failed = false;
        try { MapAccountMigration.Load(store, registry); } catch (IOException) { failed = true; }
        Check(failed && store.Load().baseUnlockedSlots == 16, "Failed migration preserves previous durable save");
        Check(JsonUtility.ToJson(legacy) == beforeItems, "Migration never mutates the caller snapshot");
        store.FaultInjector = null;
        var migrated = MapAccountMigration.Load(store, registry);
        AccountInvariants.Validate(migrated, registry);
        Check(migrated.baseUnlockedSlots == 24 && migrated.unlockedSlots == 24 && migrated.inventory.Count == 60,
            "Existing active character expands on normal account load");
        Check(migrated.characters[1].personal.baseUnlockedSlots == 24 && migrated.characters[1].personal.inventory.Count == 60,
            "Dormant character expands in the same transaction");
        Check(migrated.inventory.Take(48).SequenceEqual(beforeInventory)
            && JsonUtility.ToJson(new Items { values = migrated.items }) == JsonUtility.ToJson(new Items { values = legacy.items }),
            "Existing slot positions and item values are preserved");
        Check(migrated.weapons.SequenceEqual(legacy.weapons) && migrated.elementalGemInstanceId == legacy.elementalGemInstanceId,
            "Equipment ownership is preserved");
        long generation = store.Generation;
        var again = MapAccountMigration.Load(store, registry);
        Check(store.Generation == generation && JsonUtility.ToJson(again) == JsonUtility.ToJson(migrated),
            "Second load is idempotent and makes no extra write");
        var custom = NewAccountFactory.Create(registry);
        custom.baseUnlockedSlots = custom.unlockedSlots = 2;
        BagAccountMigration.ExpandInventory(custom, registry);
        Check(custom.baseUnlockedSlots == 2 && custom.unlockedSlots == 2, "Nonstandard fixture base is preserved");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BagInventoryCapacityBuilder.PrefabPath);
        var grid = prefab.GetComponentInChildren<UnityEngine.UI.GridLayoutGroup>(true);
        Check(grid.GetComponentsInChildren<SlotUI>(true).Length == 60 && grid.constraintCount == 6,
            "Authored inventory contains 60 slots in six columns");
        var result = "PASS " + checks.Count + "\n" + string.Join("\n", checks);
        File.WriteAllText(Path.Combine(output, "result.txt"), result);
        return result;
    }
    [Serializable] private sealed class Items { public List<ItemSnapshot> values; }
    private static AccountSnapshot Legacy(AccountContentRegistry registry)
    {
        var state = NewAccountFactory.Create(registry);
        state.baseUnlockedSlots = state.unlockedSlots = 16;
        state.inventoryCapacity = 48;
        state.inventory = state.inventory.GetRange(0, 48);
        // Keep a deliberately placed item to catch accidental sorting during expansion.
        state.inventory[14] = state.inventory[0]; state.inventory[0] = null;
        return state;
    }
}
