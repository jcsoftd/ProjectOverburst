using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEngine;

/// <summary>Product graph, reward and durable account transaction checks; never touches the real account.</summary>
public static class SkillTreeFoundationVerifier
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
        void Reject(Action action, string name) { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } catch (InvalidDataException) { failed = true; } Check(failed, name); }
        try
        {
            var catalog = AccountSkillTree.Catalog; catalog.Validate();
            Check(catalog.nodes.Length == 53 && catalog.connections.Length == 64 && catalog.nodes.Count(n => n.IsReserved) == 16, "53 nodes / 64 connections / 16 reserved effect-active-keystone nodes");
            var plan = new OverburstSkillTreePlan(catalog, 32);
            foreach (var n in catalog.nodes.Where(n => n.IsReserved)) Check(!plan.Has(n.id) && !plan.Available(n) && !plan.Toggle(n.id), "Reserved visible node cannot activate or consume points " + n.id);
            Check(plan.Has("ROOT") && !plan.Toggle("ROOT") && !plan.Toggle("S_W3"), "Free root and adjacency gate");
            Check(plan.Toggle("S_W1") && plan.Toggle("S_W3") && plan.Remaining == 30, "Purchase path consumes points");
            Check(plan.Refunds("S_W1").SequenceEqual(new[] { "S_W1", "S_W3" }), "Refund previews dependent disconnection");
            plan.Cancel(); Check(plan.Remaining == 32 && !plan.Changed, "Cancel restores baseline");
            foreach (var n in catalog.nodes.Where(n => n.cost > 0)) Check(plan.Toggle(n.id), "Purchase " + n.id);
            Check(plan.Remaining == 0 && plan.Total("attack") == 24 && plan.Total("defense") == 8 && plan.Total("hp") == 32 && plan.Total("move") == 12, "Expanded available stat totals (32 cost; temporary level100 budget99)");
            Reject(() => new OverburstSkillTreePlan(catalog, 1, new[] { "S_W1", "S_W3" }), "Overspend rejected");
            Reject(() => new OverburstSkillTreePlan(catalog, 16, new[] { "UNKNOWN" }), "Unknown saved node rejected without cleaning");
            Reject(() => new OverburstSkillTreePlan(catalog, 16, new[] { "S_W1", "S_W1" }), "Duplicate saved node rejected");
            foreach (int count in new[] { 100, 300, 1000 })
            {
                var large = Chain(count); var largePlan = new OverburstSkillTreePlan(large, count);
                foreach (var n in large.nodes.Skip(1)) Check(largePlan.Toggle(n.id), "Graph " + count + " purchase " + n.id);
                Check(largePlan.Refunds("N1").Length == count - 1, "Graph " + count + " refund connected component");
            }
            var loop = Chain(4); loop.connections = new[] { Edge("ROOT", "N1"), Edge("N1", "N2"), Edge("N2", "N3"), Edge("N3", "ROOT") };
            var alternate = new OverburstSkillTreePlan(loop, 3, new[] { "N1", "N2", "N3" });
            Check(alternate.Refunds("N1").SequenceEqual(new[] { "N1" }), "Physical cycle retains alternate root path");
            loop.nodes[2].requires = new[] { "N1" }; var required = new OverburstSkillTreePlan(loop, 3, new[] { "N1", "N2", "N3" });
            Check(required.Refunds("N1").SequenceEqual(new[] { "N1", "N2" }), "Mandatory dependency separate from alternate path");
            loop.nodes[1].requires = new[] { "N2" }; Reject(loop.Validate, "Mandatory prerequisite cycle rejected");
            Check(AccountSkillTree.GrantEligible(null,1).earnedPoints==0&&AccountSkillTree.GrantEligible(null,2).earnedPoints==1,"Temporary rule starts with zero and grants one at level2");
            var legacy=new SkillTreeSnapshot{catalogVersion=catalog.version,earnedPoints=4,learnedNodeIds=new List<string>{"S_W1","S_W3"},grants=Enumerable.Range(1,4).Select(i=>new SkillPointGrant{sourceId="level:"+(i*5).ToString("000"),points=1}).ToList()};
            var updated=AccountSkillTree.GrantEligible(legacy,20);
            Check(updated.earnedPoints==19&&updated.grants.Count==19&&updated.learnedNodeIds.SequenceEqual(legacy.learnedNodeIds)&&legacy.grants.All(g=>updated.grants.Count(n=>n.sourceId==g.sourceId)==1),"Existing grant IDs and allocation remain valid under temporary reward table");
            var tree = AccountSkillTree.GrantEligible(null, 20);
            Check(tree.earnedPoints == 19 && AccountSkillTree.GrantEligible(tree, 20).earnedPoints == 19, "Legacy level backfill and duplicate grant prevention");
            Check(AccountSkillTree.GrantEligible(tree, 100).earnedPoints == 99, "Multi-level grants through level 100");
            var bad = tree.Copy(); bad.grants.Add(new SkillPointGrant { sourceId = "level:005", points = 1 }); Reject(() => AccountSkillTree.Validate(bad, 20), "Duplicate grant ledger rejected");
            var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var state = NewAccountFactory.Create(registry); state.skillTree = AccountSkillTree.GrantEligible(null, 1);
            var store = new EasySaveAccountStore(Path.Combine(output, "TransactionAccount")); store.Save(state, state.lastTransactionId);
            var tx = new AccountTransactions(state, store, registry);
            int xp = Enumerable.Range(1, 19).Sum(OverburstGrowthRules.ExperienceToNext);
            Check(tx.GrantExperience("xp-level20", tx.Revision, xp, out int level, out int experience) && level == 20 && tx.Read().skillTree.earnedPoints == 19, "Real XP transaction atomically grants nineteen points");
            Check(!tx.GrantExperience("xp-level20", tx.Revision, xp, out level, out experience) && tx.Read().skillTree.earnedPoints == 19, "Repeated XP transaction cannot duplicate rewards");
            var expected = tx.Read().skillTree; var baseline = tx.Read();
            store.FaultInjector = phase => { if (phase == "before-write") throw new IOException("Owned save failure fixture"); };
            bool failedSave = false;
            try { tx.Execute("failed-allocation", tx.Revision, c => AccountSkillTree.Allocate(c, expected, new[] { "S_W1", "S_W3" }), true); } catch (IOException) { failedSave = true; }
            Check(failedSave && tx.Revision == baseline.revision && tx.Read().skillTree.learnedNodeIds.Count == 0 && new EasySaveAccountStore(Path.Combine(output, "TransactionAccount")).Load().skillTree.learnedNodeIds.Count == 0, "Failed save preserves RAM and disk allocation");
            store.FaultInjector = null;
            Check(tx.Execute("apply-allocation", tx.Revision, c => AccountSkillTree.Allocate(c, expected, new[] { "S_W1", "S_W3" }), true), "Durable allocation checkpoint");
            var loaded = new EasySaveAccountStore(Path.Combine(output, "TransactionAccount")).Load();
            AccountInvariants.Validate(loaded, registry);
            Check(loaded.skillTree.learnedNodeIds.SequenceEqual(new[] { "S_W1", "S_W3" }) && loaded.skillTree.earnedPoints == 19 && loaded.weapons.SequenceEqual(baseline.weapons), "Fresh store reopens learned IDs, ledger and existing inventory");
            Reject(() => tx.Execute("stale-allocation", tx.Revision, c => AccountSkillTree.Allocate(c, expected, Array.Empty<string>()), true), "Stale allocation rejected despite current account revision");
            var copied = ItemSnapshotCodec.CopyValues(loaded); copied.skillTree.learnedNodeIds.Clear(); Check(loaded.skillTree.learnedNodeIds.Count == 2, "ES3 nested skill-tree copy isolated");
            var card = NewAccountFactory.Create(registry);
            card.skillTree = AccountSkillTree.GrantEligible(null, 1);
            AccountRunCommands.PrepareEntry(card, "skill-card", new MapInstanceState(), null);
            AccountRunCommands.Activate(card, "skill-card");
            AccountRunCommands.ClaimEventCard(card, "skill-card", "first-level", OverburstGrowthRules.ExperienceToNext(1));
            Check(card.level == 2 && card.experience == 0 && card.skillTree.earnedPoints == 1, "XP card grants first level point atomically");
            Reject(() => AccountRunCommands.ClaimEventCard(card, "skill-card", "first-level", 1), "Repeated XP card rejected");
            Check(card.skillTree.earnedPoints == 1 && card.run.rewardedEncounters.Count == 1, "Rejected card cannot duplicate grant ledger");
            AccountRunCommands.ClaimEventCard(card, "skill-card", "multiple-levels", Enumerable.Range(2, 3).Sum(OverburstGrowthRules.ExperienceToNext));
            Check(card.level == 5 && card.skillTree.earnedPoints == 4, "XP card grants every crossed level point");
            AccountRunCommands.ClaimEventCard(card, "skill-card", "no-xp", 0);
            AccountRunCommands.ClaimEventCard(card, "skill-card", "partial-xp", 1);
            Check(card.level == 5 && card.experience == 1 && card.skillTree.earnedPoints == 4, "Zero and partial card XP cannot duplicate points");
            var cap = ItemSnapshotCodec.CopyValues(card); cap.level = 99; cap.experience = 0; cap.skillTree = AccountSkillTree.GrantEligible(null, 99);
            AccountRunCommands.ClaimEventCard(cap, "skill-card", "cap-level", OverburstGrowthRules.ExperienceToNext(99));
            AccountRunCommands.ClaimEventCard(cap, "skill-card", "already-cap", 1);
            Check(cap.level == 100 && cap.experience == 0 && cap.skillTree.earnedPoints == 99, "XP card grants final level and respects cap");
            var cardStore = new EasySaveAccountStore(Path.Combine(output, "CardAccount")); cardStore.Save(card, "card-baseline");
            var cardTx = new AccountTransactions(card, cardStore, registry); long cardRevision = cardTx.Revision;
            cardStore.FaultInjector = phase => { if (phase == "before-write") throw new IOException("Owned XP card save failure"); };
            bool cardSaveFailed = false;
            try { cardTx.Execute("failed-card", cardRevision, c => AccountRunCommands.ClaimEventCard(c, "skill-card", "durable-card", OverburstGrowthRules.ExperienceToNext(5)), true); }
            catch (IOException) { cardSaveFailed = true; }
            finally { cardStore.FaultInjector = null; }
            var cardDisk = new EasySaveAccountStore(Path.Combine(output, "CardAccount")).Load();
            Check(cardSaveFailed && cardTx.Revision == cardRevision && cardTx.Read().level == 5 && cardTx.Read().skillTree.earnedPoints == 4 && !cardTx.Read().run.rewardedEncounters.Contains("durable-card"), "Failed XP card save retains RAM level points and claim");
            Check(cardDisk.level == 5 && cardDisk.skillTree.earnedPoints == 4 && !cardDisk.run.rewardedEncounters.Contains("durable-card"), "Failed XP card save retains disk level points and claim");
            Check(cardTx.Execute("durable-card", cardTx.Revision, c => AccountRunCommands.ClaimEventCard(c, "skill-card", "durable-card", OverburstGrowthRules.ExperienceToNext(5)), true), "XP card retry durably commits");
            cardDisk = new EasySaveAccountStore(Path.Combine(output, "CardAccount")).Load();
            Check(cardDisk.level == 6 && cardDisk.experience == 1 && cardDisk.skillTree.earnedPoints == 5 && cardDisk.run.rewardedEncounters.Contains("durable-card"), "Fresh store reopens XP card level points and claim together");
            Check(cardTx.GrantExperience("normal-xp-after-card", cardTx.Revision, 1, out _, out _) && cardTx.Read().skillTree.earnedPoints == 5, "Normal XP after card cannot duplicate level grants");
            File.WriteAllText(Path.Combine(output, "foundation-results.json"), JsonConvert.SerializeObject(new { status = "PASS_SCOPED", checkCount = checks.Count, checks, graphScale = "100/300/1000 rules only; render performance NOT_RUN" }, Formatting.Indented));
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "foundation-results.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = error.ToString() }, Formatting.Indented)); throw; }
    }
    static OverburstSkillTreeCatalog.Edge Edge(string a, string b) => new OverburstSkillTreeCatalog.Edge { a = a, b = b };
    static OverburstSkillTreeCatalog Chain(int count)
    {
        var nodes = Enumerable.Range(0, count).Select(i => new OverburstSkillTreeCatalog.Node { id = i == 0 ? "ROOT" : "N" + i, name = "Fixture", kind = i == 0 ? "root" : "stat", stat = i == 0 ? null : "attack", cost = i == 0 ? 0 : 1, value = i == 0 ? 0 : 1, requires = Array.Empty<string>() }).ToArray();
        return new OverburstSkillTreeCatalog { version = "fixture", nodes = nodes, connections = Enumerable.Range(1, count - 1).Select(i => Edge(nodes[i - 1].id, nodes[i].id)).ToArray(), segments = Array.Empty<OverburstSkillTreeCatalog.Segment>(), rewards = Array.Empty<OverburstSkillTreeCatalog.Reward>() };
    }
}
