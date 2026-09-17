using UnityEngine;

public sealed class EnemyFlowField // 보행 셀 기반 공유 방향장
{
    private const int UnvisitedCost = -1;
    private const float MinimumDirectionSqrMagnitude = 0.0001f;
    private static readonly int[] CardinalX = { 1, -1, 0, 0 };
    private static readonly int[] CardinalZ = { 0, 0, 1, -1 };

    private readonly RunWalkableArea area;
    private readonly int[] integrationCosts;
    private readonly int[] visitVersions;
    private readonly int[] openQueue;
    private int buildVersion;
    private int queueHead;
    private int queueTail;
    private int targetX;
    private int targetZ;

    public EnemyFlowField(RunWalkableArea area, int targetX, int targetZ)
    {
        this.area = area ?? throw new System.ArgumentNullException(nameof(area));
        int cellCount = Mathf.Max(1, area.Width * area.Height);
        integrationCosts = new int[cellCount];
        visitVersions = new int[cellCount];
        openQueue = new int[cellCount];
        if (!Rebuild(targetX, targetZ))
            throw new System.ArgumentException("Flow Field target cell must be walkable.");
    }

    public RunWalkableArea Area => area;
    public int TargetX => targetX;
    public int TargetZ => targetZ;
    public bool IsComplete { get; private set; }
    public int ProcessedCellCount { get; private set; }
    public int ReachableCellCount { get; private set; }

    public bool Rebuild(int newTargetX, int newTargetZ)
    {
        if (!area.IsWalkableCell(newTargetX, newTargetZ))
            return false;

        AdvanceBuildVersion();
        targetX = newTargetX;
        targetZ = newTargetZ;
        queueHead = 0;
        queueTail = 0;
        ProcessedCellCount = 0;
        ReachableCellCount = 1;
        IsComplete = false;

        int targetIndex = ResolveIndex(targetX, targetZ);
        SetCost(targetIndex, 0);
        openQueue[queueTail++] = targetIndex;
        return true;
    }

    public int Advance(int maximumCells)
    {
        if (IsComplete || maximumCells <= 0)
            return 0;

        int processed = 0;
        while (queueHead < queueTail && processed < maximumCells)
        {
            int currentIndex = openQueue[queueHead++];
            ResolveCoordinates(currentIndex, out int currentX, out int currentZ);
            int nextCost = integrationCosts[currentIndex] + 1;
            for (int i = 0; i < CardinalX.Length; i++)
                TryEnqueue(currentX + CardinalX[i], currentZ + CardinalZ[i], nextCost);

            processed++;
            ProcessedCellCount++;
        }

        if (queueHead >= queueTail)
            IsComplete = true;
        return processed;
    }

    public bool TryGetDirection(Vector3 worldPosition, out Vector3 direction, out Vector3 waypoint)
    {
        direction = Vector3.zero;
        waypoint = worldPosition;
        if (!TryResolveWalkableCell(worldPosition, out int sourceX, out int sourceZ)
            || !TryGetCost(sourceX, sourceZ, out int sourceCost))
        {
            return false;
        }

        int bestX = sourceX;
        int bestZ = sourceZ;
        int bestCost = sourceCost;
        float bestTargetSqrDistance = float.PositiveInfinity;
        Vector2 targetCenter = area.GetCellCenter(targetX, targetZ);
        for (int xOffset = -1; xOffset <= 1; xOffset++)
        {
            for (int zOffset = -1; zOffset <= 1; zOffset++)
            {
                if (xOffset == 0 && zOffset == 0)
                    continue;

                int candidateX = sourceX + xOffset;
                int candidateZ = sourceZ + zOffset;
                if (!CanTraverse(sourceX, sourceZ, candidateX, candidateZ)
                    || !TryGetCost(candidateX, candidateZ, out int candidateCost)
                    || candidateCost >= sourceCost)
                {
                    continue;
                }

                Vector2 candidateCenter = area.GetCellCenter(candidateX, candidateZ);
                float targetSqrDistance = (candidateCenter - targetCenter).sqrMagnitude;
                if (candidateCost > bestCost
                    || (candidateCost == bestCost && targetSqrDistance >= bestTargetSqrDistance))
                {
                    continue;
                }

                bestX = candidateX;
                bestZ = candidateZ;
                bestCost = candidateCost;
                bestTargetSqrDistance = targetSqrDistance;
            }
        }

        Vector2 nextCenter = area.GetCellCenter(bestX, bestZ);
        waypoint = new Vector3(nextCenter.x, worldPosition.y, nextCenter.y);
        Vector3 delta = waypoint - worldPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude <= MinimumDirectionSqrMagnitude)
            return false;

        direction = delta.normalized;
        return true;
    }

    public bool TryGetIntegrationCost(int x, int z, out int cost)
    {
        return TryGetCost(x, z, out cost);
    }

    private void TryEnqueue(int x, int z, int cost)
    {
        if (!area.IsWalkableCell(x, z))
            return;

        int index = ResolveIndex(x, z);
        if (visitVersions[index] == buildVersion)
            return;

        SetCost(index, cost);
        openQueue[queueTail++] = index;
        ReachableCellCount++;
    }

    private bool TryResolveWalkableCell(Vector3 worldPosition, out int x, out int z)
    {
        if (area.TryGetCell(worldPosition, out x, out z) && area.IsWalkableCell(x, z))
            return true;
        return area.TryFindNearestWalkableCell(worldPosition, out x, out z);
    }

    private bool CanTraverse(int sourceX, int sourceZ, int targetCellX, int targetCellZ)
    {
        if (!area.IsWalkableCell(targetCellX, targetCellZ))
            return false;

        int xOffset = targetCellX - sourceX;
        int zOffset = targetCellZ - sourceZ;
        if (xOffset == 0 || zOffset == 0)
            return true;

        return area.IsWalkableCell(sourceX + xOffset, sourceZ)
            && area.IsWalkableCell(sourceX, sourceZ + zOffset); // 대각선 모서리 관통 금지
    }

    private bool TryGetCost(int x, int z, out int cost)
    {
        cost = UnvisitedCost;
        if (x < 0 || x >= area.Width || z < 0 || z >= area.Height)
            return false;

        int index = ResolveIndex(x, z);
        if (visitVersions[index] != buildVersion)
            return false;

        cost = integrationCosts[index];
        return true;
    }

    private void SetCost(int index, int cost)
    {
        visitVersions[index] = buildVersion;
        integrationCosts[index] = cost;
    }

    private int ResolveIndex(int x, int z)
    {
        return z * area.Width + x;
    }

    private void ResolveCoordinates(int index, out int x, out int z)
    {
        z = index / area.Width;
        x = index - z * area.Width;
    }

    private void AdvanceBuildVersion()
    {
        if (buildVersion == int.MaxValue)
        {
            System.Array.Clear(visitVersions, 0, visitVersions.Length);
            buildVersion = 1;
            return;
        }

        buildVersion++;
    }
}
