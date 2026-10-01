using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;

public static class BagQualityVerifier
{
    public static string Run(string outputDirectory)
    {
        string directory = Path.Combine(Path.GetFullPath(outputDirectory), "Static-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var passed = new List<string>();
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var bagData = registry.Entries.Select(x => x.asset).OfType<BagItemData>().First();
        string rng = JsonUtility.ToJson(UnityEngine.Random.state);
        foreach (ItemGrade grade in Enum.GetValues(typeof(ItemGrade)))
            for (int seed = 0; seed < 512; seed++)
            {
                var state = BagQuality.Roll(grade, seed);
                Require(BagQuality.IsValid(state, grade), "quality contract " + grade + " seed " + seed);
                Require(JsonUtility.ToJson(state) == JsonUtility.ToJson(BagQuality.Roll(grade, seed)), "deterministic seed");
                Require(!BagQuality.Roll(grade, seed, true).rows.Any(x => x.stat == BagStat.KillExperience), "max level pool");
            }
        Require(rng == JsonUtility.ToJson(UnityEngine.Random.state), "quality consumes no Unity RNG");
        passed.Add("4096 grade/seed cases: totals, row cap, main minimum, distinct options, deterministic roll, max-level XP exclusion");
        var green = BagQuality.Roll(ItemGrade.Rare, 10);
        green.rows[0].stars = new List<WeaponGradeStarType> { WeaponGradeStarType.Green, WeaponGradeStarType.Green };
        Require(BagQuality.AdditionalSlots(100, green) == 22, "sum green weights before flooring");
        foreach (int level in new[] { 1, 10, 11, 50, 51, 90, 91, 100 })
            Require(BagQuality.BaseSlots(level) == new[] { 5, 5, 6, 9, 11, 17, 19, 19 }[Array.IndexOf(new[] { 1, 10, 11, 50, 51, 90, 91, 100 }, level)], "level bracket");
        Require(BagQuality.BaseSlots(100) + 12 + 16 == 47, "max capacity fits 48 physical slots");
        passed.Add("ten level brackets, green-star flooring and 47/48 maximum capacity");

        var old = EmptyAccount(); old.level = 73; old.inventoryCapacity = 35; old.inventory = Enumerable.Repeat<string>(null, 35).ToList();
        var merchantData = registry.Entries.Select(x => x.asset).OfType<MerchantDefinition>().First();
        old.merchants.Add(new MerchantSnapshot { contentId = registry.IdFor(merchantData), capacity = 1, stockInitialized = true, stock = new List<string> { null } });
        for (int i = 0; i < 4; i++)
        {
            var item = ItemSnapshotCodec.Capture(new ItemData(bagData, 3, ItemGrade.Mythic), registry);
            item.bag = null;
            item.bagRolls = new List<BagRandomOptionRoll> {
                new BagRandomOptionRoll { optionType = BagRandomOptionType.MaxStamina, value = 11 },
                new BagRandomOptionRoll { optionType = BagRandomOptionType.MoveSpeedPercent, value = 5 },
                new BagRandomOptionRoll { optionType = BagRandomOptionType.MaxHp, value = 17 } };
            old.items.Add(item);
            if (i == 0) old.inventory[34] = item.instanceId;
            if (i == 1) old.stashTabs[1].slots[62] = item.instanceId;
            if (i == 2) old.bags[0] = item.instanceId;
            if (i == 3) old.merchants[0].stock[0] = item.instanceId;
        }
        string original = JsonUtility.ToJson(old);
        // Real ES3 payload with the newly added properties absent, as on a pre-upgrade disk.
        var wire = JToken.Parse(Encoding.UTF8.GetString(ES3.Serialize(old)));
        foreach (var property in ((JContainer)wire).Descendants().OfType<JProperty>().Where(x => x.Name == "bag" || x.Name == "bagGoldCarry" || x.Name == "bagExperienceCarry").ToArray()) property.Remove();
        var legacy = ES3.Deserialize<AccountSnapshot>(Encoding.UTF8.GetBytes(wire.ToString(Newtonsoft.Json.Formatting.None)));
        var upgraded = BagAccountMigration.Upgrade(legacy, registry, out bool changed);
        Require(changed && upgraded.inventoryCapacity == 48 && upgraded.inventory.Count == 48, "legacy expansion");
        Require(upgraded.items.All(x => x.level == 73 && x.bagRolls.Count == 0 && BagQuality.IsValid(x.bag, x.grade)), "all owned bags migrate before validation");
        Require(upgraded.inventory[34] == old.inventory[34] && upgraded.items.Select(x => x.instanceId).SequenceEqual(old.items.Select(x => x.instanceId)), "identities and overflow order preserved");
        Require(original == JsonUtility.ToJson(old), "legacy source is not mutated");
        AccountInvariants.Validate(upgraded, registry);
        Require(ReferenceEquals(upgraded, BagAccountMigration.Upgrade(upgraded, registry, out changed)) && !changed, "second migration is no-op");
        foreach (var saved in upgraded.items)
        {
            var runtime = ItemSnapshotCodec.Restore(saved, registry);
            runtime.EnsureRuntimeState();
            Require(JsonUtility.ToJson(saved) == JsonUtility.ToJson(ItemSnapshotCodec.Capture(runtime, registry)), "RestoreSaved quality stable");
            Require(JsonUtility.ToJson(runtime.bagState) == JsonUtility.ToJson(runtime.CopyStack(1, true).bagState), "copy retains stars");
        }
        Require(rng == JsonUtility.ToJson(UnityEngine.Random.state), "migration/restore RNG stable");
        var corrupt = ItemSnapshotCodec.CopyValues(upgraded.items[0]); corrupt.bag.version++;
        try { ItemSnapshotCodec.Restore(corrupt, registry); throw new Exception("Future bag version accepted"); } catch (InvalidDataException) { }
        passed.Add("old ES3 missing fields, four ownership containers, source/identity/order preservation, once-only migration, strict RestoreSaved, independent deep copy, future-version rejection");

        var store = new EasySaveAccountStore(Path.Combine(directory, "Account")); store.Save(upgraded, "migration-fixture");
        var loaded = new EasySaveAccountStore(Path.Combine(directory, "Account")).Load();
        Require(JsonUtility.ToJson(upgraded) == JsonUtility.ToJson(loaded), "A/B save quality round trip");
        var commands = new AccountTransactions(upgraded, store, registry);
        long revision = commands.Revision;
        store.FaultInjector = point => { if (point == "before-write") throw new IOException("fixture"); };
        try { commands.GrantExperience("xp-failure", revision, 1, out _, out _, BagQuality.BonusUnits(1, 2)); throw new Exception("Fault not injected"); } catch (IOException) { }
        Require(commands.Revision == revision && commands.Read().bagExperienceCarry == 0 && commands.Read().experience == 0, "XP/carry rollback together");
        store.FaultInjector = null;
        for (int i = 0; i < 50; i++) commands.GrantExperience("xp-" + i, commands.Revision, 1, out _, out _, BagQuality.BonusUnits(1, 2));
        Require(commands.Read().experience == 51 && commands.Read().bagExperienceCarry == 0, "small kill bonus accumulates exactly");
        var loadedAgain = new EasySaveAccountStore(Path.Combine(directory, "Account")).Load();
        Require(loadedAgain.experience == 51 && loadedAgain.bagExperienceCarry == 0, "XP/carry disk round trip");
        int carry = 0, gold = 0;
        for (int i = 0; i < 100; i++) gold += BagQuality.ApplyReward(1, BagQuality.BonusUnits(1, 3), carry, out carry);
        Require(gold == 103 && carry == 0, "small gold bonus exact");
        passed.Add("A/B disk readback, XP/carry atomic failure, 50 tiny kills -> 51 XP, 100 tiny gold rewards -> 103 gold");

        var weights = new[] { 40f, 25f, 15f, 10f, 5f, 3f, 2f };
        int[] counts = new int[7];
        for (int i = 0; i < 108000; i++) counts[(int)BagFarmingLoot.SelectGrade((i + .5f) / 108000f, weights, 6, 8)]++;
        Require(counts[0] == 42023 || Mathf.Abs(counts[0] - 42023) < 3, "rare weights normalized");
        Require(Mathf.Abs((float)counts[2] / counts[6] - 7.5f) < .01f, "relative Rare/Mythic weight retained");
        var junk = ScriptableObject.CreateInstance<JunkItemData>();
        try { Require(!BagFarmingLoot.Eligible(junk), "junk excluded"); }
        finally { UnityEngine.Object.DestroyImmediate(junk); }
        passed.Add("108000-point grade distribution, normalized Rare+ multiplier and unchanged relative high-grade weights");
        passed.Add(AccountUiLifetimeVerifier.Run());
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BagInventoryCapacityBuilder.PrefabPath);
        Require(prefab.GetComponentInChildren<UnityEngine.UI.GridLayoutGroup>(true).GetComponentsInChildren<SlotUI>(true).Length == 48, "authored grid 48 slots");
        Require(prefab.GetComponentsInChildren<Transform>(true).Sum(x => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(x.gameObject)) == 0, "prefab missing scripts");
        passed.Add("authored prefab 48 slots and missing scripts 0");
        string result = string.Join("\n", passed.Select(x => "PASS " + x));
        File.WriteAllText(Path.Combine(directory, "result.txt"), result);
        return result + "\n" + directory;
    }
    public static AccountSnapshot EmptyAccount()
    {
        var state = new AccountSnapshot();
        state.inventory = Enumerable.Repeat<string>(null, 48).ToList();
        for (int i = 0; i < 3; i++) state.stashTabs.Add(new ItemContainerSnapshot { slots = Enumerable.Repeat<string>(null, 63).ToList() });
        state.weapons.Add(null); state.gear.AddRange(Enumerable.Repeat<string>(null, 7)); state.bags.Add(null);
        state.flasks.AddRange(Enumerable.Repeat<string>(null, 3));
        for (int i = 0; i < 10; i++) state.quickSlots.Add(new QuickSlotSnapshot());
        return state;
    }
    private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException("FAIL " + detail); }
}
