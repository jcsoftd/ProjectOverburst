using System.Collections.Generic;
using UnityEngine;

public sealed class AttackTargetSnapshot
{
    public readonly struct Entry
    {
        public readonly int TargetId;
        public readonly CombatTarget Target;
        public readonly float InitialRequiredProgress;

        public Entry(int targetId, CombatTarget target, float initialRequiredProgress)
        {
            TargetId = targetId;
            Target = target;
            InitialRequiredProgress = initialRequiredProgress;
        }
    }

    private readonly List<Entry> entries = new List<Entry>(32);
    private readonly List<CombatTarget> candidates = new List<CombatTarget>(64);
    private readonly HashSet<int> collectedIds = new HashSet<int>();

    public IReadOnlyList<Entry> Entries => entries;

    public void Clear()
    {
        entries.Clear();
        candidates.Clear();
        collectedIds.Clear();
    }

    public void Capture(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        CombatTarget source)
    {
        Clear();

        if (source == null)
            return;

        Vector3 center = basis.GetPatternOrigin(pattern);
        float broadphaseRadius = Mathf.Max(0.1f, AttackPatternEvaluator.ResolveBroadphaseRadius(pattern));
        CombatTargetRegistry.CollectPotentialTargets(center, broadphaseRadius, candidates);

        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (target == null || !CombatTargetFilter.CanDamage(source, target))
                continue;

            int targetId = target.TargetId;
            if (!collectedIds.Add(targetId))
                continue;

            if (!AttackPatternEvaluator.TryEvaluate(
                    pattern,
                    basis,
                    target.CurrentVolume,
                    out float requiredProgress))
            {
                continue;
            }

            entries.Add(new Entry(targetId, target, requiredProgress));
        }

        entries.Sort(CompareEntries);
    }

    private static int CompareEntries(Entry left, Entry right)
    {
        int progressComparison = left.InitialRequiredProgress.CompareTo(right.InitialRequiredProgress);
        return progressComparison != 0
            ? progressComparison
            : left.TargetId.CompareTo(right.TargetId);
    }
}
