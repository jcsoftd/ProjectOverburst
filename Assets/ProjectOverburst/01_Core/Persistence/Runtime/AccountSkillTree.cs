using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Overburst.Persistence
{
    [Serializable] public sealed class SkillTreeSnapshot
    {
        public int version = 1;
        public string catalogVersion;
        public int earnedPoints;
        public List<string> learnedNodeIds = new List<string>();
        public List<SkillPointGrant> grants = new List<SkillPointGrant>();
        public SkillTreeSnapshot Copy() => new SkillTreeSnapshot { version = version, catalogVersion = catalogVersion, earnedPoints = earnedPoints, learnedNodeIds = new List<string>(learnedNodeIds), grants = grants.Select(g => new SkillPointGrant { sourceId = g.sourceId, points = g.points }).ToList() };
    }
    [Serializable] public sealed class SkillPointGrant { public string sourceId; public int points; }

    public static class AccountSkillTree
    {
        static OverburstSkillTreeCatalog catalog;
        public static OverburstSkillTreeCatalog Catalog
        {
            get
            {
                if (catalog != null) return catalog;
                var asset = Resources.Load<TextAsset>("UI/SkillTree/CommonAttackTreeCatalog");
                if (!asset) throw new InvalidDataException("Missing skill-tree definitions.");
                catalog = JsonUtility.FromJson<OverburstSkillTreeCatalog>(asset.text); catalog.Validate(); return catalog;
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { catalog = null; SkillTreeBonuses.Clear(); }
        public static SkillTreeSnapshot GrantEligible(SkillTreeSnapshot source, int level)
        {
            var next = source?.Copy() ?? new SkillTreeSnapshot { catalogVersion = Catalog.version };
            var claimed = new HashSet<string>(next.grants.Select(g => g.sourceId));
            foreach (var grant in Catalog.rewards)
                if (grant.level <= level && claimed.Add(grant.sourceId))
                { next.grants.Add(new SkillPointGrant { sourceId = grant.sourceId, points = grant.points }); next.earnedPoints = checked(next.earnedPoints + grant.points); }
            next.catalogVersion = Catalog.version;
            Validate(next, level); return next;
        }
        public static void Validate(SkillTreeSnapshot tree, int level)
        {
            // Missing optional field is a pre-foundation account. Bootstrap initializes it durably.
            if (tree == null) return;
            if (tree.version != 1 || tree.learnedNodeIds == null || tree.grants == null || tree.earnedPoints < 0 || string.IsNullOrWhiteSpace(tree.catalogVersion)) throw new InvalidDataException("Invalid skill-tree save header.");
            var rewards = Catalog.rewards.ToDictionary(r => r.sourceId, StringComparer.Ordinal); var seen = new HashSet<string>(); int earned = 0;
            foreach (var grant in tree.grants)
            {
                if (grant == null || grant.sourceId == null || !seen.Add(grant.sourceId) || !rewards.TryGetValue(grant.sourceId, out var definition) || definition.points != grant.points || definition.level > level) throw new InvalidDataException("Invalid skill-point grant ledger.");
                earned = checked(earned + grant.points);
            }
            if (earned != tree.earnedPoints) throw new InvalidDataException("Skill-point ledger total differs.");
            try { new OverburstSkillTreePlan(Catalog, earned, tree.learnedNodeIds); }
            catch (InvalidOperationException error) { throw new InvalidDataException("Invalid learned skill nodes.", error); }
        }
        public static void Allocate(AccountSnapshot candidate, SkillTreeSnapshot expected, IEnumerable<string> ids)
        {
            var tree = candidate.skillTree;
            if (tree == null || expected == null || tree.earnedPoints != expected.earnedPoints || !tree.learnedNodeIds.SequenceEqual(expected.learnedNodeIds) || !tree.grants.Select(g => g.sourceId).SequenceEqual(expected.grants.Select(g => g.sourceId))) throw new InvalidOperationException("포인트 또는 습득 노드가 변경되었습니다. 창을 다시 열어 주세요.");
            var next = tree.Copy(); next.learnedNodeIds = ids.OrderBy(id => id, StringComparer.Ordinal).ToList(); next.catalogVersion = Catalog.version;
            Validate(next, candidate.level); candidate.skillTree = next;
        }
    }

    /// <summary>Cached projection of the committed account. Never reads or clones saves per movement frame.</summary>
    public static class SkillTreeBonuses
    {
        public static float AttackPercent { get; private set; }
        public static float ArmorFlat { get; private set; }
        public static float HealthPercent { get; private set; }
        public static float MovePercent { get; private set; }
        public static void Clear() { AttackPercent = ArmorFlat = HealthPercent = MovePercent = 0; }
        public static void Project(SkillTreeSnapshot tree)
        {
            Clear(); if (tree == null) return;
            var ids = new HashSet<string>(tree.learnedNodeIds);
            foreach (var n in AccountSkillTree.Catalog.nodes)
            {
                if (!ids.Contains(n.id)) continue;
                switch (n.stat) { case "attack": AttackPercent += n.value; break; case "defense": ArmorFlat += n.value; break; case "hp": HealthPercent += n.value; break; case "move": MovePercent += n.value; break; }
            }
        }
    }
}
