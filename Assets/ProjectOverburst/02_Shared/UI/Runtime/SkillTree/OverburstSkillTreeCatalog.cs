using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Stable node IDs, undirected travel connections and separate mandatory prerequisites.</summary>
[Serializable]
public sealed class OverburstSkillTreeCatalog
{
    public string version, rewardVersion;
    public int planningBudget;
    public string[] initialPlan;
    public Node[] nodes;
    public Edge[] connections;
    public Segment[] segments;
    public Reward[] rewards;
    [Serializable] public sealed class Reward { public string sourceId; public int level, points; }
    [Serializable] public sealed class Edge { public string a, b; }
    [Serializable] public sealed class Node
    {
        public string id, name, shortName, area, kind, trigger, stat, description;
        public bool IsReserved => kind == "effect" || kind == "active" || kind == "keystone";
        public float x, y, cooldown, value;
        public int cost, icon;
        public string[] requires, effects;
        public Vector2 Position => new Vector2(x, -y);
        public string Effect(WeaponElement element) => effects != null && effects.Length == 6 ? effects[ElementIndex(element)] : string.Empty;
    }
    [Serializable] public sealed class Segment
    {
        public float x1, y1, x2, y2;
        public string[] sources, targets;
        public Vector2 A => new Vector2(x1, -y1);
        public Vector2 B => new Vector2(x2, -y2);
    }
    public static int ElementIndex(WeaponElement element)
    {
        switch (element) { case WeaponElement.Fire: return 0; case WeaponElement.Ice: return 1; case WeaponElement.Electric: return 2; case WeaponElement.Dark: return 3; case WeaponElement.Light: return 4; default: return 5; }
    }
    public static string ElementName(WeaponElement element) => new[] { "불", "얼음", "번개", "어둠", "빛", "미장착" }[ElementIndex(element)];
    public static string AreaName(string area)
    {
        switch (area) { case "W": return "약공 콤보"; case "H": return "강공"; case "D": return "대시 약공"; case "Q": return "대시 강공"; default: return "공통"; }
    }
    public static string KindName(string kind) => kind == "stat" ? "능력치" : kind == "effect" ? "추가 효과" : kind == "active" ? "액티브" : kind == "keystone" ? "최종 핵심" : "기본 경로";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(version) || nodes == null || connections == null || segments == null || rewards == null) throw new InvalidOperationException("Incomplete skill-tree catalog.");
        var index = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            if (n == null || string.IsNullOrWhiteSpace(n.id) || !index.TryAdd(n.id, n) || n.cost < 0 || n.requires == null || float.IsNaN(n.x) || float.IsInfinity(n.x) || float.IsNaN(n.y) || float.IsInfinity(n.y) || float.IsNaN(n.value) || float.IsInfinity(n.value)) throw new InvalidOperationException("Invalid node definition.");
            if (n.kind != "root" && n.kind != "guide" && n.kind != "stat" && !n.IsReserved) throw new InvalidOperationException("Unsupported foundation node " + n.id);
            if (!n.IsReserved && n.kind != "stat" && (n.cost != 0 || !string.IsNullOrEmpty(n.stat) || n.value != 0 || n.requires.Length != 0)) throw new InvalidOperationException("Free guide cannot own bonuses or mandatory prerequisites.");
            if (n.IsReserved && (n.cost != 0 || !string.IsNullOrEmpty(n.stat) || n.value != 0 || string.IsNullOrWhiteSpace(n.description) || n.effects == null || n.effects.Length != 6)) throw new InvalidOperationException("Reserved effect cannot grant bonuses or consume points.");
            if (n.kind == "stat" && (n.cost == 0 || n.value <= 0 || !new[] { "attack", "defense", "hp", "move" }.Contains(n.stat))) throw new InvalidOperationException("Invalid stat node " + n.id);
        }
        if (!index.TryGetValue("ROOT", out var root) || root.cost != 0) throw new InvalidOperationException("Free ROOT required.");
        var neighbors = nodes.ToDictionary(n => n.id, n => new List<string>());
        var edges = new HashSet<string>();
        foreach (var e in connections)
        {
            if (e == null || !index.ContainsKey(e.a) || !index.ContainsKey(e.b) || e.a == e.b || !edges.Add(string.CompareOrdinal(e.a, e.b) < 0 ? e.a + ":" + e.b : e.b + ":" + e.a)) throw new InvalidOperationException("Invalid or duplicate tree connection.");
            neighbors[e.a].Add(e.b); neighbors[e.b].Add(e.a);
        }
        var visited = new HashSet<string>(); var queue = new Queue<string>(); queue.Enqueue("ROOT");
        while (queue.Count > 0) { var id = queue.Dequeue(); if (!visited.Add(id)) continue; foreach (var other in neighbors[id]) queue.Enqueue(other); }
        if (visited.Count != nodes.Length) throw new InvalidOperationException("Disconnected catalog node.");
        var states = new Dictionary<string, int>();
        void Visit(string id)
        {
            if (states.TryGetValue(id, out int state)) { if (state == 1) throw new InvalidOperationException("Cyclic mandatory prerequisite."); return; }
            states[id] = 1;
            foreach (var p in index[id].requires) { if (!index.ContainsKey(p)) throw new InvalidOperationException("Unknown prerequisite."); Visit(p); }
            states[id] = 2;
        }
        foreach (var n in nodes) Visit(n.id);
        foreach (var s in segments)
            if (s == null || s.sources == null || s.targets == null || s.sources.Length != s.targets.Length || s.sources.Length == 0 || s.sources.Any(id => !index.ContainsKey(id)) || s.targets.Any(id => !index.ContainsKey(id))) throw new InvalidOperationException("Invalid route segment.");
        var grantIds = new HashSet<string>();
        foreach (var r in rewards) if (r == null || string.IsNullOrWhiteSpace(r.sourceId) || !grantIds.Add(r.sourceId) || r.points <= 0 || r.level < 1 || r.level > OverburstGrowthRules.MaximumLevel) throw new InvalidOperationException("Invalid point reward.");
    }
}

/// <summary>Reversible draft. Authority and durability belong to the account transaction.</summary>
public sealed class OverburstSkillTreePlan
{
    readonly Dictionary<string, OverburstSkillTreeCatalog.Node> index;
    readonly Dictionary<string, List<string>> neighbors;
    HashSet<string> planned, applied, active, committedActive;
    public int Budget { get; private set; }
    public OverburstSkillTreePlan(OverburstSkillTreeCatalog catalog, int budget = 0, IEnumerable<string> allocation = null)
    {
        catalog.Validate(); index = catalog.nodes.ToDictionary(n => n.id, StringComparer.Ordinal);
        neighbors = catalog.nodes.ToDictionary(n => n.id, n => new List<string>());
        foreach (var e in catalog.connections) { neighbors[e.a].Add(e.b); neighbors[e.b].Add(e.a); }
        Load(budget, allocation ?? Array.Empty<string>());
    }
    public int Remaining => Budget - planned.Sum(id => index[id].cost);
    public bool Changed => !planned.SetEquals(applied);
    public string[] Planned => planned.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    public string[] Applied => applied.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    public bool IsApplied(string id) => applied.Contains(id);
    public bool IsCommitted(string id) => committedActive.Contains(id);
    public void ClearDraft() { planned.Clear(); active = Reach(planned); }
    public bool Has(string id) => active.Contains(id);
    public bool Available(OverburstSkillTreeCatalog.Node n) => !n.IsReserved && n.requires.All(Has) && (n.id == "ROOT" || neighbors[n.id].Any(Has));
    HashSet<string> Reach(HashSet<string> allocated)
    {
        var result = new HashSet<string>(); var queue = new Queue<string>(); queue.Enqueue("ROOT");
        while (queue.Count > 0)
        {
            var id = queue.Dequeue(); if (!result.Add(id)) continue;
            foreach (var next in neighbors[id]) if (!index[next].IsReserved && (index[next].cost == 0 || allocated.Contains(next))) if (!result.Contains(next)) queue.Enqueue(next);
        }
        return result;
    }
    HashSet<string> Trim(HashSet<string> kept)
    {
        bool changed;
        do { var reachable = Reach(kept); changed = false; foreach (var id in kept.ToArray()) if (!reachable.Contains(id) || index[id].requires.Any(p => !reachable.Contains(p))) { kept.Remove(id); changed = true; } } while (changed);
        return kept;
    }
    public void Load(int budget, IEnumerable<string> allocation)
    {
        if (budget < 0 || allocation == null) throw new InvalidOperationException("Invalid skill points.");
        var ids = allocation.ToArray(); var next = new HashSet<string>(ids, StringComparer.Ordinal);
        if (next.Count != ids.Length || next.Any(id => !index.ContainsKey(id) || index[id].cost == 0) || !Trim(new HashSet<string>(next)).SetEquals(next) || next.Sum(id => index[id].cost) > budget) throw new InvalidOperationException("Invalid saved allocation; preserve account data.");
        Budget = budget; planned = next; applied = new HashSet<string>(next); active = Reach(planned); committedActive = Reach(applied);
    }
    public string[] Refunds(string id)
    {
        var kept = new HashSet<string>(planned); kept.Remove(id); kept = Trim(kept);
        return planned.Except(kept).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
    public bool Toggle(string id)
    {
        if (!index.TryGetValue(id, out var n) || n.cost == 0) return false;
        if (planned.Contains(id)) foreach (var child in Refunds(id)) planned.Remove(child);
        else { if (!Available(n) || Remaining < n.cost) return false; planned.Add(id); }
        active = Reach(planned); return true;
    }
    public void Apply() { applied = new HashSet<string>(planned); committedActive = Reach(applied); }
    public void Cancel() { planned = new HashSet<string>(applied); active = Reach(planned); }
    public float Total(string stat) => planned.Where(id => index[id].stat == stat).Sum(id => index[id].value);
    public float AppliedTotal(string stat) => applied.Where(id => index[id].stat == stat).Sum(id => index[id].value);
}
