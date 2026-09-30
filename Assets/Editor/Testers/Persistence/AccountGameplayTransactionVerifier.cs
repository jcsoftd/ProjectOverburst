using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;

public static class AccountGameplayTransactionVerifier
{
    public static string RunPlay(string outputDirectory)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("This verifier requires Play mode.");
        var originalSession = AccountGameplaySession.Current;
        var account = PlayerAccountInventoryService.Instance;
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var definitions = registry.Entries.Select(x => x.asset).OfType<MerchantDefinition>().ToArray();
        foreach (var definition in definitions)
        {
            MerchantStockRefreshService.GetOrCreateInventory(definition);
            MerchantReputationService.AddReputationExperience(definition, 7);
        }
        var baseline = AccountGameplayProjection.Capture(new AccountSnapshot(), account, registry);
        string directory = Path.Combine(outputDirectory, "gameplay-transactions-" + Guid.NewGuid().ToString("N"));
        var store = new EasySaveAccountStore(directory);
        store.Save(baseline, "baseline");
        var session = new AccountGameplaySession(account, registry, store, baseline);
        session.Attach();
        int notifications = 0;
        Action changed = () => notifications++;
        account.Inventory.Changed += changed;
        try
        {
            var definition = definitions.First(x => MerchantStockRefreshService.GetOrCreateInventory(x).Items.Any(i => i != null));
            var merchant = MerchantStockRefreshService.GetOrCreateInventory(definition);
            int slot = Enumerable.Range(0, merchant.Capacity).First(i => merchant.GetItemAt(i) != null);
            string id = merchant.GetItemAt(slot).runtimeInstanceId;
            store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture write failure"); };
            try
            {
                session.Execute(() =>
                {
                    var item = merchant.GetItemAt(slot);
                    if (!merchant.RemoveItemAt(slot, item, item.stackCount) || !account.Inventory.AddItem(item)) throw new Exception("Fixture transfer rejected");
                    if (notifications != 0) throw new Exception("Inventory notification escaped before save");
                    return true;
                });
                throw new Exception("Write fault did not fire");
            }
            catch (IOException) { }
            Check(ReferenceEquals(merchant, MerchantStockRefreshService.GetOrCreateInventory(definition)), "cached merchant reference preserved");
            Check(merchant.GetItemAt(slot)?.runtimeInstanceId == id && !account.Inventory.Items.Any(x => x?.runtimeInstanceId == id), "both owners rolled back");
            Check(session.Revision == baseline.revision, "failed save revision unchanged");
            store.FaultInjector = null;
            notifications = 0;
            Check(session.Execute(() =>
            {
                var item = merchant.GetItemAt(slot);
                if (!merchant.RemoveItemAt(slot, item, item.stackCount) || !account.Inventory.AddItem(item)) return false;
                Check(notifications == 0, "notification before commit");
                return true;
            }), "successful transfer commit");
            Check(notifications == 1, "one inventory notification after commit");
            Check(session.Execute(() => account.Inventory.AddItem(MerchantTradeItemUtility.CreateGoldItem(20))), "fund purchase");
            int purchaseSlot = Enumerable.Range(0, merchant.Capacity).First(i => merchant.GetItemAt(i) != null);
            string purchaseId = merchant.GetItemAt(purchaseSlot).runtimeInstanceId;
            int merchantGold = merchant.GetCurrencyAmount(CurrencyType.Gold);
            int playerGold = MerchantTradeItemUtility.GetInventoryGoldAmount(account.Inventory);
            Func<MerchantTradeTransactionPlan> purchase = () => MerchantTradeTransactionPlan.Create(
                new[] { new MerchantTradeOffer(MerchantTradeOfferSide.Merchant, purchaseSlot, merchant.GetItemAt(purchaseSlot)) },
                new MerchantTradeOffer[0], 10, 0);
            var committer = new MerchantTradeCommitter();
            store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture trade write failure"); };
            var failedTrade = committer.Commit(purchase(), account.Inventory, merchant, account.CurrencyService);
            Check(!failedTrade.success && merchant.GetItemAt(purchaseSlot)?.runtimeInstanceId == purchaseId
                && merchant.GetCurrencyAmount(CurrencyType.Gold) == merchantGold
                && MerchantTradeItemUtility.GetInventoryGoldAmount(account.Inventory) == playerGold, "real trade rollback includes both gold balances");
            store.FaultInjector = null;
            var completedTrade = committer.Commit(purchase(), account.Inventory, merchant, account.CurrencyService);
            Check(completedTrade.success && merchant.GetItemAt(purchaseSlot) == null
                && merchant.GetCurrencyAmount(CurrencyType.Gold) == merchantGold + 10
                && MerchantTradeItemUtility.GetInventoryGoldAmount(account.Inventory) == playerGold - 10, "real trade commits item and payment together");
            int stashGold = account.CurrencyService.GetAmount(CurrencyType.Gold);
            long currencyRevision = session.Revision;
            store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture currency write failure"); };
            Check(!account.CurrencyService.TryAddCurrency(CurrencyType.Gold, 17)
                && account.CurrencyService.GetAmount(CurrencyType.Gold) == stashGold && session.Revision == currencyRevision, "direct currency grant rollback");
            store.FaultInjector = null;
            Check(account.CurrencyService.TryAddCurrency(CurrencyType.Gold, 17)
                && account.CurrencyService.GetAmount(CurrencyType.Gold) == stashGold + 17 && session.Revision == currencyRevision + 1, "direct currency grant commits once");
            store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture currency spend failure"); };
            Check(!account.CurrencyService.TrySpend(new CurrencyAmount(CurrencyType.Gold, 11))
                && account.CurrencyService.GetAmount(CurrencyType.Gold) == stashGold + 17 && session.Revision == currencyRevision + 1, "direct currency spend rollback");
            store.FaultInjector = null;
            Check(account.CurrencyService.TrySpend(new CurrencyAmount(CurrencyType.Gold, 11))
                && account.CurrencyService.GetAmount(CurrencyType.Gold) == stashGold + 6 && session.Revision == currencyRevision + 2, "direct currency spend commits once");
            VerifyConsumptionAndEquipment(account, registry, store);
            var loaded = new EasySaveAccountStore(directory).Load();
            var rng = JsonUtility.ToJson(UnityEngine.Random.state);
            AccountGameplayProjection.Restore(loaded, account, registry);
            Check(rng == JsonUtility.ToJson(UnityEngine.Random.state), "restore rerolled random state");
            Check(account.Inventory.Items.Any(x => x?.runtimeInstanceId == id) && merchant.GetItemAt(slot) == null, "committed ownership restored");
            foreach (var value in loaded.merchants)
            {
                var asset = registry.Resolve<MerchantDefinition>(value.contentId);
                Check(MerchantReputationService.GetExperience(asset) == value.reputationExperience && MerchantReputationService.GetLevel(asset) == value.reputationLevel, "merchant reputation restored");
            }
            var roundtrip = AccountGameplayProjection.Capture(loaded, account, registry);
            Check(JsonUtility.ToJson(loaded) == JsonUtility.ToJson(roundtrip), "all player and merchant values match after restore");
            string result = "PASS Play direct currency grant/spend rollback and single commit; player/merchant exact save; transfer and payment rollback; deferred notification; consumption failure has no heal/cooldown/count change; equipment failure rollback and success; progression failure rollback and success; RNG unchanged";
            File.WriteAllText(Path.Combine(directory, "result.txt"), result);
            return result + "\n" + directory;
        }
        finally
        {
            account.Inventory.Changed -= changed;
            session.Detach();
            AccountGameplayProjection.Restore(baseline, account, registry);
            originalSession?.Attach();
        }
    }

    private static void Check(bool value, string description)
    { if (!value) throw new Exception("FAIL " + description); }

    private static void VerifyConsumptionAndEquipment(PlayerAccountInventoryService account, AccountContentRegistry registry, EasySaveAccountStore store)
    {
        var healData = registry.Entries.Select(x => x.asset).OfType<ConsumableItemData>()
            .First(x => !(x is FlaskItemData) && x.consumeOnUse && x.consumableType == ConsumableType.HealHp && x.effectValue > 0);
        var potion = new ItemData(healData, 1, ItemGrade.Common, 2);
        Check(account.Inventory.AddItem(potion), "seed consumable");
        var bindings = UnityEngine.Object.FindFirstObjectByType<InventoryQuickSlotBindingController>(FindObjectsInactive.Include);
        var actions = UnityEngine.Object.FindFirstObjectByType<InventoryItemActionService>(FindObjectsInactive.Include);
        Check(bindings != null && actions != null && bindings.Bind(1, potion), "bind consumable");
        var health = PlayerContext.Instance.CurrentActorHealth;
        float originalMaximum = health.MaxHp;
        health.SetMaxHp(originalMaximum * 2, false);
        float beforeHealth = health.CurrentHp;
        int beforeQuantity = account.Inventory.CountItemsByBaseData(healData);
        store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture action write failure"); };
        Check(!actions.UseQuickSlot(1), "failed consumption reports failure");
        Check(health.CurrentHp == beforeHealth && account.Inventory.CountItemsByBaseData(healData) == beforeQuantity
            && actions.GetConsumableCooldownRemaining(healData) <= 0, "failed consumption has no effects");
        store.FaultInjector = null;
        Check(actions.UseQuickSlot(1) && health.CurrentHp > beforeHealth && account.Inventory.CountItemsByBaseData(healData) == beforeQuantity - 1, "consumption effect after commit");
        health.SetMaxHp(originalMaximum, false);

        var gearData = registry.Entries.Select(x => x.asset).OfType<GearItemData>().First(x => GearItemData.Fits(x.kind, (GearSlot)0));
        var gear = new ItemData(gearData, 1, ItemGrade.Common);
        Check(account.Inventory.AddItem(gear), "seed gear");
        int slot = account.Inventory.FindFirstMatchingItemIndex(gear);
        var equipment = PlayerContext.Instance.CurrentActorEquipment;
        string previousGear = equipment.GetGearSlotItem(0)?.runtimeInstanceId;
        store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture equipment write failure"); };
        Check(!GearEquipmentService.EquipFromInventorySlot(slot, 0), "equipment failure reports false");
        Check(account.Inventory.GetItemAt(slot)?.runtimeInstanceId == gear.runtimeInstanceId && equipment.GetGearSlotItem(0)?.runtimeInstanceId == previousGear, "equipment ownership rollback");
        store.FaultInjector = null;
        Check(GearEquipmentService.EquipFromInventorySlot(slot, 0) && equipment.GetGearSlotItem(0)?.runtimeInstanceId == gear.runtimeInstanceId, "equipment commit");

        var progression = PlayerProgression.Current;
        int level = progression.Level, xp = progression.Experience;
        store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture XP write failure"); };
        progression.AddExperience(1);
        Check(!progression.FlushPendingExperience() && progression.Level == level && progression.Experience == xp, "XP rollback retains pending award");
        store.FaultInjector = null;
        progression.AddExperience(1);
        Check(progression.FlushPendingExperience() && progression.Level >= level && (progression.Experience != xp || progression.Level != level), "XP batch retry commit");
    }
}
