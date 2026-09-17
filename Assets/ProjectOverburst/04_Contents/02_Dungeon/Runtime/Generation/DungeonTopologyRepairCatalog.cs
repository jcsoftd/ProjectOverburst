using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public enum DungeonTopologyRepairShape
{
    None = 0,
    I = 1,
    L = 2,
    T = 3
}

public static class DungeonTopologyRepairId
{
    public static bool TryParse(
        string topologyId,
        out DungeonTopologyRepairShape shape,
        out int priority)
    {
        shape = DungeonTopologyRepairShape.None;
        priority = 0;
        string value = topologyId?.Trim() ?? string.Empty;
        if (value.Length < 2)
            return false;

        shape = value[0] switch
        {
            'I' => DungeonTopologyRepairShape.I,
            'L' => DungeonTopologyRepairShape.L,
            'T' => DungeonTopologyRepairShape.T,
            _ => DungeonTopologyRepairShape.None
        };
        string priorityText = value.Substring(1);
        if (shape == DungeonTopologyRepairShape.None
            || priorityText[0] == '0'
            || !int.TryParse(
                priorityText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out priority)
            || priority < 1)
        {
            shape = DungeonTopologyRepairShape.None;
            priority = 0;
            return false;
        }

        return true;
    }

    public static int Compare(
        DungeonTopologyRepairCandidateGroup left,
        DungeonTopologyRepairCandidateGroup right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        int shapeComparison = left.Shape.CompareTo(right.Shape);
        if (shapeComparison != 0)
            return shapeComparison;

        int priorityComparison =
            left.Priority.CompareTo(right.Priority);
        return priorityComparison != 0
            ? priorityComparison
            : string.Compare(
                left.TopologyId,
                right.TopologyId,
                StringComparison.Ordinal);
    }
}

[Serializable]
public sealed class DungeonTopologyRepairCandidateGroup
{
    [SerializeField] private string topologyId;
    [SerializeField] private bool allowQuarterTurns;
    [SerializeField] private List<GameObject> variants = new();

    public string TopologyId => topologyId?.Trim() ?? string.Empty;
    public DungeonTopologyRepairShape Shape =>
        DungeonTopologyRepairId.TryParse(
            TopologyId,
            out DungeonTopologyRepairShape shape,
            out _)
            ? shape
            : DungeonTopologyRepairShape.None;
    public int Priority =>
        DungeonTopologyRepairId.TryParse(
            TopologyId,
            out _,
            out int priority)
            ? priority
            : int.MaxValue;
    public bool AllowQuarterTurns => allowQuarterTurns;
    public IReadOnlyList<GameObject> Variants => variants;

    public void Configure(
        string configuredTopologyId,
        bool configuredAllowQuarterTurns,
        IEnumerable<GameObject> configuredVariants)
    {
        topologyId = configuredTopologyId?.Trim() ?? string.Empty;
        allowQuarterTurns = configuredAllowQuarterTurns;
        variants = configuredVariants?
            .Where(prefab => prefab != null)
            .Distinct()
            .OrderBy(prefab => prefab.name, StringComparer.Ordinal)
            .ToList()
            ?? new List<GameObject>();
    }
}

[Serializable]
public sealed class DungeonTopologyRepairRule
{
    [SerializeField] private GameObject sourceFamilyPrefab;
    [SerializeField] private List<DungeonTopologyRepairCandidateGroup>
        candidateGroups = new();

    public GameObject SourceFamilyPrefab => sourceFamilyPrefab;
    public string SourceFamilyName =>
        DungeonTileVisualFamilyName.Resolve(
            sourceFamilyPrefab != null
                ? sourceFamilyPrefab.name
                : string.Empty);
    public IReadOnlyList<DungeonTopologyRepairCandidateGroup>
        CandidateGroups => candidateGroups;

    public bool Matches(GameObject sourcePrefab)
    {
        return sourcePrefab != null
            && !string.IsNullOrEmpty(SourceFamilyName)
            && string.Equals(
                SourceFamilyName,
                DungeonTileVisualFamilyName.Resolve(sourcePrefab.name),
                StringComparison.Ordinal);
    }

    public void ConfigureSource(GameObject configuredSourceFamilyPrefab)
    {
        sourceFamilyPrefab = configuredSourceFamilyPrefab;
    }

    public void UpsertGroup(
        string topologyId,
        bool allowQuarterTurns,
        IEnumerable<GameObject> variants)
    {
        string normalizedId = topologyId?.Trim() ?? string.Empty;
        if (!DungeonTopologyRepairId.TryParse(
                normalizedId,
                out _,
                out _))
        {
            throw new ArgumentException(
                "형태 ID는 I1, L1, T1 형식이어야 합니다.",
                nameof(topologyId));
        }

        DungeonTopologyRepairCandidateGroup group =
            candidateGroups.FirstOrDefault(item =>
                item != null
                && string.Equals(
                    item.TopologyId,
                    normalizedId,
                    StringComparison.Ordinal));
        if (group == null)
        {
            group = new DungeonTopologyRepairCandidateGroup();
            candidateGroups.Add(group);
        }

        group.Configure(
            normalizedId,
            allowQuarterTurns,
            variants);
        candidateGroups = candidateGroups
            .Where(item => item != null)
            .ToList();
        candidateGroups.Sort(DungeonTopologyRepairId.Compare);
    }
}

[CreateAssetMenu(
    fileName = "DungeonTopologyRepairCatalog",
    menuName = "OVERBURST/World/Dungeon Topology Repair Catalog")]
public sealed class DungeonTopologyRepairCatalog : ScriptableObject
{
    [SerializeField] private List<DungeonTopologyRepairRule> rules = new();

    public IReadOnlyList<DungeonTopologyRepairRule> Rules => rules;

    public bool TryGetRule(
        GameObject sourcePrefab,
        out DungeonTopologyRepairRule rule)
    {
        rule = rules.FirstOrDefault(item =>
            item != null && item.Matches(sourcePrefab));
        return rule != null;
    }

    public IReadOnlyCollection<GameObject> GetCandidatePrefabs()
    {
        return rules
            .Where(rule => rule != null)
            .SelectMany(rule => rule.CandidateGroups)
            .Where(group => group != null)
            .SelectMany(group => group.Variants)
            .Where(prefab => prefab != null)
            .Distinct()
            .ToArray();
    }

    public DungeonTopologyRepairRule GetOrCreateRule(
        GameObject sourceFamilyPrefab)
    {
        DungeonTopologyRepairRule rule = rules.FirstOrDefault(item =>
            item != null
            && item.Matches(sourceFamilyPrefab));
        if (rule == null)
        {
            rule = new DungeonTopologyRepairRule();
            rules.Add(rule);
        }

        rule.ConfigureSource(sourceFamilyPrefab);
        rules = rules
            .Where(item => item != null)
            .OrderBy(item => item.SourceFamilyName, StringComparer.Ordinal)
            .ToList();
        return rule;
    }
}

public static class DungeonTileVisualFamilyName
{
    public static string Resolve(string prefabName)
    {
        string value = prefabName?.Trim() ?? string.Empty;
        int separatorIndex = value.LastIndexOf('_');
        if (separatorIndex <= 0
            || separatorIndex >= value.Length - 1)
        {
            return value;
        }

        string suffix = value.Substring(separatorIndex + 1);
        for (int i = 0; i < suffix.Length; i++)
        {
            if (suffix[i] < 'A' || suffix[i] > 'Z')
                return value;
        }

        return value.Substring(0, separatorIndex);
    }
}
