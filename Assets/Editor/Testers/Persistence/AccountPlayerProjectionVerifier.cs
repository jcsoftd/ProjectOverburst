using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;

public static class AccountPlayerProjectionVerifier
{
    public static string Run(string outputDirectory)
    {
        var property = typeof(PlayerAccountInventoryService).GetProperty("Loadout", BindingFlags.Static | BindingFlags.NonPublic);
        var original = property.GetValue(null);
        property.SetValue(null, Activator.CreateInstance(property.PropertyType, true));
        var fixture = new GameObject("Account projection fixture"); fixture.SetActive(false);
        try
        {
            var registry = AccountContentRegistryBuilder.Build();
            var inventory = fixture.AddComponent<PlayerInventory>();
            var stash = fixture.AddComponent<PlayerStash>();
            var currency = AssetDatabase.FindAssets("t:CurrencyItemData", new[] { "Assets/ProjectOverburst" })
                .Select(g => AssetDatabase.LoadAssetAtPath<CurrencyItemData>(AssetDatabase.GUIDToAssetPath(g))).First(x => x != null);
            var old = new ItemData(currency, 1, ItemGrade.Common, 5);
            var loot = new ItemData(currency, 1, ItemGrade.Common, 5) { originRunId = "fixture-run" };
            Check(inventory.SetUnlockedSlotCount(3) && inventory.AddItem(old) && inventory.AddItem(loot), "seed items");
            Check(inventory.Items.Count(x => x != null) == 2, "different origins must not merge");
            Check(inventory.SplitStackAt(1, 2), "split loot");
            Check(inventory.GetItemAt(2).originRunId == loot.originRunId && inventory.GetItemAt(2).runtimeInstanceId != loot.runtimeInstanceId, "split preserves origin and creates identity");
            Check(inventory.ConsumeItem(old, 2) && old.stackCount == 3 && !inventory.ConsumeItem(old, 4), "consumption exact quantity");
            Check(inventory.SetUnlockedSlotCount(2) && inventory.IsOverCapacity, "capacity shrink preserves overflow");
            Check(!inventory.AddItem(new ItemData(currency, 1, ItemGrade.Common)), "overflow rejects acquisition including merge");
            var snapshot = new AccountSnapshot { baseUnlockedSlots = 2, run = new RunSnapshot { runId = "fixture-run", phase = RunPhase.Active, map = new MapInstanceState() } };
            AccountPlayerProjection.Capture(snapshot, inventory, stash, null, registry);
            AccountInvariants.Validate(snapshot, registry);
            string directory = Path.Combine(outputDirectory, "projection-" + Guid.NewGuid().ToString("N"));
            var store = new EasySaveAccountStore(directory);
            store.Save(snapshot, "projection");
            var loaded = new EasySaveAccountStore(directory).Load();
            AccountPlayerProjection.Restore(loaded, inventory, stash, null, registry, false);
            Check(inventory.IsOverCapacity && inventory.GetItemAt(2).originRunId == "fixture-run", "disk reload preserves overflow and origin");
            AccountRunCommands.Fail(loaded, "fixture-run");
            AccountPlayerProjection.Restore(loaded, inventory, stash, null, registry, false);
            Check(inventory.GetItemAt(0).stackCount == 3 && inventory.Items.Count(x => x != null) == 1, "failure removes loot and preserves consumed existing quantity");
            inventory.Clear();
            inventory.SetUnlockedSlotCount(4);
            var overflow = new ItemData(currency, 1, ItemGrade.Common, 4);
            Check(inventory.SetItemAt(3, overflow), "seed overflow item");
            inventory.SetUnlockedSlotCount(2);
            Check(inventory.FindFirstItemByBaseData(currency) == overflow && inventory.CountItemsByBaseData(currency) == 4, "overflow remains searchable for consumption");
            Check(inventory.ConsumeItem(overflow, 1) && overflow.stackCount == 3, "consume overflow item");
            var incoming = new ItemData(currency, 1, ItemGrade.Common);
            Check(!inventory.SetItemAt(0, incoming) && !inventory.TryReplaceOwnedItemAt(0, null, incoming), "slot APIs reject incoming while over capacity");
            Check(inventory.SetItemAt(0, overflow) && !inventory.IsOverCapacity && inventory.GetItemAt(3) == null, "move retained item into available capacity");
            inventory.SetUnlockedSlotCount(0);
            Check(inventory.TryReplaceOwnedItemAt(0, overflow, null), "remove owned overflow item");
            inventory.SetUnlockedSlotCount(4);
            inventory.SetItemAt(3, incoming);
            inventory.SetUnlockedSlotCount(2);
            Check(inventory.SetItemAt(3, null) && !inventory.IsOverCapacity, "clear overflow through slot API");
            string result = "PASS player component capture/ES3/restore; overflow persistence; origin-separated stacks and split; failure retains existing consumption; overflow lookup, consumption, incoming rejection, retained movement and removal";
            File.WriteAllText(Path.Combine(directory, "result.txt"), result);
            return result + "\n" + directory;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
            property.SetValue(null, original);
        }
    }

    private static void Check(bool result, string message)
    { if (!result) throw new Exception("FAIL " + message); }
}
