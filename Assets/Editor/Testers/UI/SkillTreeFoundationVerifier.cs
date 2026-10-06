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
            Check(plan.Remaining == 0 && plan.Total("attack") == 24 && plan.Total("defense") == 8 && plan.Total("hp") == 32 && plan.Total("move") == 12, "Expanded available stat totals (32 cost; policy earns 20)");
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
            var tree = AccountSkillTree.GrantEligible(null, 20);
            Check(tree.earnedPoints == 4 && AccountSkillTree.GrantEligible(tree, 20).earnedPoints == 4, "Legacy level backfill and duplicate grant prevention");
            Check(AccountSkillTree.GrantEligible(tree, 100).earnedPoints == 20, "Multi-level grants through level 100");
            var bad = tree.Copy(); bad.grants.Add(new SkillPointGrant { sourceId = "level:005", points = 1 }); Reject(() => AccountSkillTree.Validate(bad, 20), "Duplicate grant ledger rejected");
            var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var state = NewAccountFactory.Create(registry); state.skillTree = AccountSkillTree.GrantEligible(null, 1);
            var store = new EasySaveAccountStore(Path.Combine(output, "TransactionAccount")); store.Save(state, state.lastTransactionId);
            var tx = new AccountTransactions(state, store, registry);
            int xp = Enumerable.Range(1, 19).Sum(OverburstGrowthRules.ExperienceToNext);
            Check(tx.GrantExperience("xp-level20", tx.Revision, xp, out int level, out int experience) && level == 20 && tx.Read().skillTree.earnedPoints == 4, "Real XP transaction atomically grants four points");
            Check(!tx.GrantExperience("xp-level20", tx.Revision, xp, out level, out experience) && tx.Read().skillTree.earnedPoints == 4, "Repeated XP transaction cannot duplicate rewards");
            var expected = tx.Read().skillTree; var baseline = tx.Read();
            store.FaultInjector = phase => { if (phase == "before-write") throw new IOException("Owned save failure fixture"); };
            bool failedSave = false;
            try { tx.Execute("failed-allocation", tx.Revision, c => AccountSkillTree.Allocate(c, expected, new[] { "S_W1", "S_W3" }), true); } catch (IOException) { failedSave = true; }
            Check(failedSave && tx.Revision == baseline.revision && tx.Read().skillTree.learnedNodeIds.Count == 0 && new EasySaveAccountStore(Path.Combine(output, "TransactionAccount")).Load().skillTree.learnedNodeIds.Count == 0, "Failed save preserves RAM and disk allocation");
            store.FaultInjector = null;
            Check(tx.Execute("apply-allocation", tx.Revision, c => AccountSkillTree.Allocate(c, expected, new[] { "S_W1", "S_W3" }), true), "Durable allocation checkpoint");
            var loaded = new EasySaveAccountStore(Path.Combine(output, "TransactionAccount")).Load();
            AccountInvariants.Validate(loaded, registry);
            Check(loaded.skillTree.learnedNodeIds.SequenceEqual(new[] { "S_W1", "S_W3" }) && loaded.skillTree.earnedPoints == 4 && loaded.weapons.SequenceEqual(baseline.weapons), "Fresh store reopens learned IDs, ledger and existing inventory");
            Reject(() => tx.Execute("stale-allocation", tx.Revision, c => AccountSkillTree.Allocate(c, expected, Array.Empty<string>()), true), "Stale allocation rejected despite current account revision");
            var copied = ItemSnapshotCodec.CopyValues(loaded); copied.skillTree.learnedNodeIds.Clear(); Check(loaded.skillTree.learnedNodeIds.Count == 2, "ES3 nested skill-tree copy isolated");
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
