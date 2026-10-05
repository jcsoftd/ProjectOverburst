using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Shared presentation definitions. Element changes resolve text, never tree identity or allocation.</summary>
[Serializable]
public sealed class OverburstSkillTreeCatalog
{
    public string version;
    public int planningBudget;
    public string[] initialPlan;
    public Node[] nodes;
    public Segment[] segments;
    [Serializable] public sealed class Node
    {
        public string id, name, shortName, area, kind, trigger, stat;
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
    public static string KindName(string kind)
    {
        switch (kind) { case "stat": return "능력치"; case "basic": return "기본 강화"; case "advanced": return "심화 강화"; case "keystone": return "핵심 강화"; case "active": return "액티브"; case "bridge": return "공격 연계"; default: return "기본 경로"; }
    }
    public void Validate()
    {
        if (nodes == null || nodes.Length != 49 || segments == null || segments.Length != 104) throw new InvalidOperationException("Invalid common tree catalog.");
        var ids = new HashSet<string>(nodes.Select(n => n.id));
        if (ids.Count != nodes.Length || planningBudget < 0) throw new InvalidOperationException("Invalid common node IDs or planning budget.");
        foreach (var n in nodes)
            if (n.requires == null || n.requires.Any(id => !ids.Contains(id)) || (n.cost > 0 && n.kind != "stat" && (n.effects == null || n.effects.Length != 6))) throw new InvalidOperationException("Invalid common node: " + n.id);
    }
}

/// <summary>Ephemeral UI planning only. Does not grant points, change account saves, or apply combat modifiers.</summary>
public sealed class OverburstSkillTreePlan
{
    readonly OverburstSkillTreeCatalog catalog;
    readonly Dictionary<string, OverburstSkillTreeCatalog.Node> index;
    HashSet<string> planned, applied;
    public OverburstSkillTreePlan(OverburstSkillTreeCatalog value)
    {
        catalog = value; catalog.Validate(); index = catalog.nodes.ToDictionary(n => n.id);
        planned = Clean(catalog.initialPlan); applied = new HashSet<string>(planned);
    }
    public int Remaining => catalog.planningBudget - planned.Sum(id => index[id].cost);
    public bool Changed => !planned.SetEquals(applied);
    public string[] Planned => planned.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    public bool Has(string id) => index.TryGetValue(id, out var n) && (n.cost == 0 || planned.Contains(id));
    public bool Available(OverburstSkillTreeCatalog.Node n) => n.requires.All(Has);
    HashSet<string> Clean(IEnumerable<string> input)
    {
        var result = new HashSet<string>((input ?? Array.Empty<string>()).Where(id => index.ContainsKey(id) && index[id].cost > 0));
        bool changed;
        do { changed = false; foreach (var id in result.ToArray()) if (index[id].requires.Any(p => index[p].cost > 0 && !result.Contains(p))) { result.Remove(id); changed = true; } } while (changed);
        if (result.Sum(id => index[id].cost) > catalog.planningBudget) result.Clear();
        return result;
    }
    public string[] Refunds(string id)
    {
        var kept = new HashSet<string>(planned); kept.Remove(id); kept = Clean(kept);
        return planned.Except(kept).ToArray();
    }
    public bool Toggle(string id)
    {
        if (!index.TryGetValue(id, out var n) || n.cost == 0) return false;
        if (planned.Contains(id)) foreach (var child in Refunds(id)) planned.Remove(child);
        else { if (!Available(n) || Remaining < n.cost) return false; planned.Add(id); }
        return true;
    }
    public void Apply() => applied = new HashSet<string>(planned);
    public void Cancel() => planned = new HashSet<string>(applied);
    public float Total(string stat) => planned.Where(id => index[id].stat == stat).Sum(id => index[id].value);
}
