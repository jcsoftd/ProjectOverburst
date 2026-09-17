using DunGen;
using UnityEngine;

public static class DungeonLayoutQualityEvaluator
{
    public const int DefaultCandidateCount = 6;
    public const int DefaultMinimumBranchTileCount = 1;
    public const float DefaultMaximumPlanarAspectRatio = 2.35f;

    public static DungeonLayoutQualityEvaluation Evaluate(
        Dungeon dungeon,
        int minimumBranchTileCount,
        float maximumPlanarAspectRatio,
        TileSet endCapTileSet = null)
    {
        if (dungeon == null)
        {
            return DungeonLayoutQualityEvaluation.Rejected(
                0,
                float.PositiveInfinity,
                Vector2.zero,
                "Dungeon 인스턴스 누락");
        }

        int branchTileCount =
            DungeonEndCapContract.CountNonEndCapBranchTiles(
                dungeon,
                endCapTileSet);
        Vector3 size = dungeon.Bounds.size;
        float width = Mathf.Abs(size.x);
        float depth = Mathf.Abs(size.z);
        float shorterSide = Mathf.Min(width, depth);
        float longerSide = Mathf.Max(width, depth);
        float planarAspectRatio = shorterSide > 0.01f
            ? longerSide / shorterSide
            : float.PositiveInfinity;

        int requiredBranches = Mathf.Max(0, minimumBranchTileCount);
        float allowedAspect = Mathf.Max(
            1f,
            maximumPlanarAspectRatio);
        bool hasEnoughBranches =
            branchTileCount >= requiredBranches;
        bool hasAcceptableAspect =
            planarAspectRatio <= allowedAspect;
        if (hasEnoughBranches && hasAcceptableAspect)
        {
            return DungeonLayoutQualityEvaluation.Accepted(
                branchTileCount,
                planarAspectRatio,
                new Vector2(width, depth));
        }

        string reason;
        if (!hasEnoughBranches && !hasAcceptableAspect)
        {
            reason =
                $"분기 {branchTileCount}/{requiredBranches}, "
                + $"평면 비율 {planarAspectRatio:F2}/{allowedAspect:F2}";
        }
        else if (!hasEnoughBranches)
        {
            reason = $"분기 {branchTileCount}/{requiredBranches}";
        }
        else
        {
            reason =
                $"평면 비율 {planarAspectRatio:F2}/{allowedAspect:F2}";
        }

        return DungeonLayoutQualityEvaluation.Rejected(
            branchTileCount,
            planarAspectRatio,
            new Vector2(width, depth),
            reason);
    }

    public static int GetCandidateSeed(
        int requestedSeed,
        int candidateIndex)
    {
        if (candidateIndex <= 0)
            return requestedSeed;

        unchecked
        {
            uint value = (uint)requestedSeed;
            value += 0x9E3779B9u * (uint)candidateIndex;
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            value *= 0xC2B2AE35u;
            value ^= value >> 16;

            int candidate = (int)(value & 0x7FFFFFFFu);
            if (candidate == 0)
                candidate = candidateIndex;
            if (candidate == requestedSeed)
                candidate = candidate == int.MaxValue
                    ? candidate - 1
                    : candidate + 1;
            return candidate;
        }
    }
}

public readonly struct DungeonLayoutQualityEvaluation
{
    private DungeonLayoutQualityEvaluation(
        bool isAccepted,
        int branchTileCount,
        float planarAspectRatio,
        Vector2 planarSize,
        string rejectionReason)
    {
        IsAccepted = isAccepted;
        BranchTileCount = branchTileCount;
        PlanarAspectRatio = planarAspectRatio;
        PlanarSize = planarSize;
        RejectionReason = rejectionReason ?? string.Empty;
    }

    public bool IsAccepted { get; }
    public int BranchTileCount { get; }
    public float PlanarAspectRatio { get; }
    public Vector2 PlanarSize { get; }
    public string RejectionReason { get; }

    public static DungeonLayoutQualityEvaluation Accepted(
        int branchTileCount,
        float planarAspectRatio,
        Vector2 planarSize)
    {
        return new DungeonLayoutQualityEvaluation(
            true,
            branchTileCount,
            planarAspectRatio,
            planarSize,
            string.Empty);
    }

    public static DungeonLayoutQualityEvaluation Rejected(
        int branchTileCount,
        float planarAspectRatio,
        Vector2 planarSize,
        string reason)
    {
        return new DungeonLayoutQualityEvaluation(
            false,
            branchTileCount,
            planarAspectRatio,
            planarSize,
            reason);
    }
}
