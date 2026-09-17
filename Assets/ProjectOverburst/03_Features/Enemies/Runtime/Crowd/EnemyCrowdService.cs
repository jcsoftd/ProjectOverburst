using System.Collections.Generic;
using UnityEngine;

public static class EnemyCrowdService // 몬스터 XZ 공간 그리드와 이웃 조회
{
    public const float CellSize = 2f;
    public const float MaximumCentralCorrection = 0.08f;
    public const int CentralSolverPassCount = 2;
    private const float ReserveYieldSteeringWeight = EnemyCrowdPrioritySolver.ReserveYieldSteeringWeight;

    private static readonly HashSet<EnemyCrowdAgent> RegisteredAgents = new HashSet<EnemyCrowdAgent>();
    private static readonly Dictionary<GridCell, List<EnemyCrowdAgent>> Cells =
        new Dictionary<GridCell, List<EnemyCrowdAgent>>(64);
    private static readonly Stack<List<EnemyCrowdAgent>> BucketPool =
        new Stack<List<EnemyCrowdAgent>>(64);
    private static readonly List<EnemyCrowdAgent> StaleAgents = new List<EnemyCrowdAgent>(16);
    private static readonly List<EnemyCrowdAgent> SpawnNeighbors = new List<EnemyCrowdAgent>(32);
    private static readonly List<MovementIntent> MovementIntents = new List<MovementIntent>(256);
    private static readonly Dictionary<EnemyCrowdAgent, int> MovementIntentByAgent =
        new Dictionary<EnemyCrowdAgent, int>(256);
    private static readonly List<EnemyCrowdPriorityBody> SolverBodies =
        new List<EnemyCrowdPriorityBody>(256);
    private static readonly List<EnemyCrowdAgent> SolverAgents = new List<EnemyCrowdAgent>(256);
    private static readonly List<int> SolverIntentIndices = new List<int>(256);
    private static readonly List<Vector3> SolverCorrections = new List<Vector3>(256);
    private static readonly List<Vector3> SolverYieldCorrections = new List<Vector3>(256);
    private static readonly List<float> SolverYieldPressures = new List<float>(256);
    private static readonly Dictionary<GridCell, List<int>> SolverCells =
        new Dictionary<GridCell, List<int>>(128);
    private static readonly Stack<List<int>> SolverBucketPool = new Stack<List<int>>(128);

    private static float snapshotFixedTime = float.NaN;
    private static float snapshotMaxBodyRadius = 0.5f;
    private static bool snapshotDirty = true;
    private static float movementIntentFixedTime = float.NaN;
    private static float solverMaxBodyRadius = 0.5f;
    private static bool movementFrameResolved;
    private static bool centralMovementResolutionEnabled = true;
    private static EnemyCrowdMovementDriver movementDriver;

    public static int RegisteredCount => RegisteredAgents.Count;
    public static bool CentralMovementResolutionEnabled => centralMovementResolutionEnabled;
    public static bool IsEnemySelfCollisionDisabled
    {
        get
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            return enemyLayer >= 0 && Physics.GetIgnoreLayerCollision(enemyLayer, enemyLayer);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        RegisteredAgents.Clear();
        ReleaseBuckets();
        BucketPool.Clear();
        StaleAgents.Clear();
        SpawnNeighbors.Clear();
        ClearMovementFrame();
        SolverBodies.Clear();
        SolverAgents.Clear();
        SolverIntentIndices.Clear();
        SolverCorrections.Clear();
        SolverYieldCorrections.Clear();
        SolverYieldPressures.Clear();
        ReleaseSolverBuckets();
        SolverBucketPool.Clear();
        snapshotFixedTime = float.NaN;
        snapshotMaxBodyRadius = 0.5f;
        snapshotDirty = true;
        movementIntentFixedTime = float.NaN;
        solverMaxBodyRadius = 0.5f;
        movementFrameResolved = false;
        centralMovementResolutionEnabled = true;
        movementDriver = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigurePhysicsCollisionPolicy()
    {
        EnsurePhysicsCollisionPolicy();
    }

    public static void EnsurePhysicsCollisionPolicy()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0 && !Physics.GetIgnoreLayerCollision(enemyLayer, enemyLayer))
            Physics.IgnoreLayerCollision(enemyLayer, enemyLayer, true); // 군집 시스템이 Enemy 상호 겹침 단독 소유
    }

    public static void Register(EnemyCrowdAgent agent)
    {
        EnsurePhysicsCollisionPolicy();
        EnsureMovementDriver();
        if (agent == null || !RegisteredAgents.Add(agent))
            return;

        snapshotDirty = true;
    }

    public static void Unregister(EnemyCrowdAgent agent)
    {
        if (agent == null || !RegisteredAgents.Remove(agent))
            return;

        MovementIntentByAgent.Remove(agent);
        snapshotDirty = true;
    }

    public static void SetCentralMovementResolutionEnabled(bool enabled)
    {
        if (centralMovementResolutionEnabled == enabled)
            return;

        centralMovementResolutionEnabled = enabled;
        ClearMovementFrame();
        movementIntentFixedTime = float.NaN;
        movementFrameResolved = false;
        if (enabled)
            EnsureMovementDriver();
    }

    public static bool SubmitMovementIntent(
        EnemyMovement movement,
        EnemyCrowdAgent agent,
        Vector3 currentPosition,
        Vector3 desiredPosition,
        float turnSpeed,
        Vector3 facingDirection,
        float moveSpeed)
    {
        if (!centralMovementResolutionEnabled
            || !Application.isPlaying
            || movement == null
            || agent == null
            || !agent.IsCrowdActive
            || !EnsureMovementDriver())
        {
            return false;
        }

        BeginMovementFrame(Time.fixedTime);
        if (movementFrameResolved)
            return false; // 실행 순서가 중앙 드라이버보다 늦으면 기존 단방향 경로로 안전 폴백

        MovementIntent intent = new MovementIntent(
            movement,
            agent,
            currentPosition,
            desiredPosition,
            turnSpeed,
            facingDirection,
            moveSpeed);
        if (MovementIntentByAgent.TryGetValue(agent, out int index))
        {
            MovementIntents[index] = intent;
        }
        else
        {
            MovementIntentByAgent.Add(agent, MovementIntents.Count);
            MovementIntents.Add(intent);
        }
        return true;
    }

    internal static void ResolveSubmittedMovement()
    {
        if (!centralMovementResolutionEnabled || !Application.isPlaying)
            return;

        BeginMovementFrame(Time.fixedTime);
        if (movementFrameResolved)
            return;
        movementFrameResolved = true;
        if (MovementIntents.Count == 0)
            return;

        BuildSolverBodies();
        for (int pass = 0; pass < CentralSolverPassCount; pass++)
        {
            if (!ResolveCentralSolverPass())
                break;
        }

        ApplyResolvedMovement();
        snapshotDirty = true;
    }

    internal static void NotifyMovementDriverDestroyed(EnemyCrowdMovementDriver driver)
    {
        if (movementDriver == driver)
            movementDriver = null;
    }

    public static int CollectNeighbors(
        Vector3 center,
        float radius,
        List<EnemyCrowdAgent> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();
        float resolvedRadius = Mathf.Max(0f, radius);
        if (resolvedRadius <= 0f || RegisteredAgents.Count == 0)
            return 0;

        EnsureSnapshot();
        float radiusSqr = resolvedRadius * resolvedRadius;
        int minX = ToCellCoordinate(center.x - resolvedRadius);
        int maxX = ToCellCoordinate(center.x + resolvedRadius);
        int minZ = ToCellCoordinate(center.z - resolvedRadius);
        int maxZ = ToCellCoordinate(center.z + resolvedRadius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (!Cells.TryGetValue(new GridCell(x, z), out List<EnemyCrowdAgent> bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    EnemyCrowdAgent agent = bucket[i];
                    if (agent == null || !agent.IsCrowdActive)
                        continue;

                    Vector3 delta = agent.SnapshotPosition - center;
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= radiusSqr)
                        results.Add(agent);
                }
            }
        }

        return results.Count;
    }

    public static int CollectPotentialOverlaps(
        Vector3 center,
        float bodyRadius,
        List<EnemyCrowdAgent> results)
    {
        EnsureSnapshot();
        float queryRadius = Mathf.Max(0.05f, bodyRadius) + snapshotMaxBodyRadius;
        return CollectNeighbors(center, queryRadius, results);
    }

    public static int CollectAgentsForTarget(
        Transform target,
        List<EnemyCrowdAgent> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();
        if (target == null || RegisteredAgents.Count == 0)
            return 0;

        EnsureSnapshot();
        foreach (EnemyCrowdAgent agent in RegisteredAgents)
        {
            if (agent == null || !agent.IsCrowdActive)
                continue;

            EnemyAIController controller = agent.Controller;
            if (controller != null
                && controller.isActiveAndEnabled
                && controller.Target == target)
            {
                results.Add(agent); // 같은 타겟 군집 스냅샷 입력
            }
        }

        return results.Count;
    }

    public static Vector3 ResolvePairDirection(EnemyCrowdAgent own, EnemyCrowdAgent other)
    {
        int ownId = own != null ? own.GetInstanceID() : 0;
        int otherId = other != null ? other.GetInstanceID() : 0;
        return EnemyCrowdPrioritySolver.ResolveStablePairDirection(ownId, otherId);
    }

    public static bool TryFindSpawnPosition(
        Vector3 center,
        float searchRadius,
        int attempts,
        out Vector3 position)
    {
        return TryFindSpawnPosition(center, searchRadius, 0.5f, attempts, out position);
    }

    public static bool TryFindSpawnPosition(
        Vector3 center,
        float searchRadius,
        float bodyRadius,
        int attempts,
        out Vector3 position)
    {
        position = center;
        int resolvedAttempts = Mathf.Max(1, attempts);
        float resolvedSearchRadius = Mathf.Max(0f, searchRadius);
        float resolvedBodyRadius = Mathf.Max(0.05f, bodyRadius);
        bool foundWalkable = false;
        float bestPenetration = float.PositiveInfinity;
        float startAngle = ResolveSpawnStartAngle(center);

        for (int i = 0; i < resolvedAttempts; i++)
        {
            Vector3 candidate = center;
            if (i > 0 && resolvedSearchRadius > 0f)
            {
                float ratio = Mathf.Sqrt(i / Mathf.Max(1f, resolvedAttempts - 1f));
                float angle = startAngle + i * 137.50776f * Mathf.Deg2Rad;
                candidate += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                    * resolvedSearchRadius * ratio;
            }

            RunWalkableArea walkableArea = RunWalkableContext.Current;
            if (walkableArea != null && !walkableArea.IsWalkable(candidate))
                continue;

            foundWalkable = true;
            float penetration = CalculatePenetration(candidate, resolvedBodyRadius, null, SpawnNeighbors);
            if (penetration <= 0.0001f)
            {
                position = candidate;
                return true;
            }
            if (penetration >= bestPenetration)
                continue;

            bestPenetration = penetration;
            position = candidate;
        }

        return foundWalkable;
    }

    public static float CalculatePenetration(
        Vector3 position,
        float bodyRadius,
        EnemyCrowdAgent excludedAgent,
        List<EnemyCrowdAgent> neighbors)
    {
        CollectPotentialOverlaps(position, bodyRadius, neighbors);
        float penetration = 0f;
        float ownRadius = Mathf.Max(0.05f, bodyRadius);
        for (int i = 0; i < neighbors.Count; i++)
        {
            EnemyCrowdAgent other = neighbors[i];
            if (other == null || other == excludedAgent || !other.IsCrowdActive)
                continue;

            Vector3 delta = position - other.SnapshotPosition;
            delta.y = 0f;
            float minimumDistance = ownRadius + other.BodyRadius;
            float distance = Mathf.Sqrt(delta.sqrMagnitude);
            penetration += Mathf.Max(0f, minimumDistance - distance);
        }

        return penetration;
    }

    private static void EnsureSnapshot()
    {
        float currentFixedTime = Time.fixedTime;
        if (!snapshotDirty && snapshotFixedTime == currentFixedTime)
            return;

        RebuildSnapshot(currentFixedTime);
    }

    private static void BeginMovementFrame(float currentFixedTime)
    {
        if (movementIntentFixedTime == currentFixedTime)
            return;

        ClearMovementFrame();
        movementIntentFixedTime = currentFixedTime;
        movementFrameResolved = false;
    }

    private static void ClearMovementFrame()
    {
        MovementIntents.Clear();
        MovementIntentByAgent.Clear();
    }

    private static bool EnsureMovementDriver()
    {
        if (!Application.isPlaying)
            return false;
        if (movementDriver != null)
            return true;

        GameObject driverObject = new GameObject("~EnemyCrowdMovementDriver");
        driverObject.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(driverObject);
        movementDriver = driverObject.AddComponent<EnemyCrowdMovementDriver>();
        return movementDriver != null;
    }

    private static void BuildSolverBodies()
    {
        SolverBodies.Clear();
        SolverAgents.Clear();
        SolverIntentIndices.Clear();
        SolverCorrections.Clear();
        SolverYieldCorrections.Clear();
        SolverYieldPressures.Clear();
        solverMaxBodyRadius = 0.5f;

        foreach (EnemyCrowdAgent agent in RegisteredAgents)
        {
            if (agent == null || !agent.IsCrowdActive)
                continue;

            int intentIndex = MovementIntentByAgent.TryGetValue(agent, out int foundIndex)
                ? foundIndex
                : -1;
            MovementIntent intent = intentIndex >= 0
                ? MovementIntents[intentIndex]
                : default;
            bool hasIntent = intentIndex >= 0
                && intent.Movement != null
                && intent.Movement.isActiveAndEnabled;
            Vector3 currentPosition = hasIntent ? intent.CurrentPosition : agent.transform.position;
            Vector3 desiredPosition = hasIntent ? intent.DesiredPosition : currentPosition;
            int movePriority = agent.MovePriority;
            if (hasIntent
                && movePriority == 0
                && agent.IsPriorityYieldEligible
                && agent.TryGetPriorityYieldDirection(out Vector3 yieldDirection))
            {
                Vector3 travel = desiredPosition - currentPosition;
                travel.y = 0f;
                if (travel.sqrMagnitude > 0.000001f)
                {
                    Vector3 biasedDirection = (travel.normalized
                        + yieldDirection * ReserveYieldSteeringWeight).normalized;
                    Vector3 biasedPosition = currentPosition + biasedDirection * travel.magnitude;
                    biasedPosition.y = desiredPosition.y;
                    if (intent.Movement.IsWalkablePosition(biasedPosition))
                        desiredPosition = biasedPosition;
                }
            }

            float bodyRadius = agent.BodyRadius;
            SolverBodies.Add(new EnemyCrowdPriorityBody
            {
                StableId = agent.GetInstanceID(),
                DesiredPosition = desiredPosition,
                ResolvedPosition = desiredPosition,
                BodyRadius = bodyRadius,
                CrowdWeight = agent.EffectiveCrowdWeight,
                MovePriority = movePriority,
                CanMove = hasIntent && !agent.IsPositionLocked,
                IsForcedMotion = hasIntent
                    && intent.Movement.LocomotionMode == EnemyLocomotionMode.Dodge,
                YieldCorrection = Vector3.zero,
                YieldPressure = 0f
            });
            SolverAgents.Add(agent);
            SolverIntentIndices.Add(intentIndex);
            SolverCorrections.Add(Vector3.zero);
            SolverYieldCorrections.Add(Vector3.zero);
            SolverYieldPressures.Add(0f);
            solverMaxBodyRadius = Mathf.Max(solverMaxBodyRadius, bodyRadius);
        }
    }

    private static bool ResolveCentralSolverPass()
    {
        RebuildSolverCells();
        for (int i = 0; i < SolverCorrections.Count; i++)
        {
            SolverCorrections[i] = Vector3.zero;
            SolverYieldCorrections[i] = Vector3.zero;
            SolverYieldPressures[i] = 0f;
        }

        bool hasCorrectableOverlap = false;
        for (int i = 0; i < SolverBodies.Count; i++)
        {
            EnemyCrowdPriorityBody body = SolverBodies[i];
            float queryRadius = Mathf.Max(0.05f, body.BodyRadius) + solverMaxBodyRadius;
            int minX = ToCellCoordinate(body.ResolvedPosition.x - queryRadius);
            int maxX = ToCellCoordinate(body.ResolvedPosition.x + queryRadius);
            int minZ = ToCellCoordinate(body.ResolvedPosition.z - queryRadius);
            int maxZ = ToCellCoordinate(body.ResolvedPosition.z + queryRadius);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!SolverCells.TryGetValue(new GridCell(x, z), out List<int> bucket))
                        continue;

                    for (int bucketIndex = 0; bucketIndex < bucket.Count; bucketIndex++)
                    {
                        int otherIndex = bucket[bucketIndex];
                        if (otherIndex <= i)
                            continue; // 같은 쌍은 한 패스에 한 번만 계산

                        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                            body,
                            SolverBodies[otherIndex],
                            out EnemyCrowdPairCorrection pairCorrection))
                        {
                            continue;
                        }

                        SolverCorrections[i] += pairCorrection.CorrectionA;
                        SolverCorrections[otherIndex] += pairCorrection.CorrectionB;
                        SolverYieldCorrections[i] += pairCorrection.YieldCorrectionA;
                        SolverYieldCorrections[otherIndex] += pairCorrection.YieldCorrectionB;
                        SolverYieldPressures[i] = Mathf.Max(
                            SolverYieldPressures[i],
                            pairCorrection.Penetration);
                        SolverYieldPressures[otherIndex] = Mathf.Max(
                            SolverYieldPressures[otherIndex],
                            pairCorrection.Penetration);
                        hasCorrectableOverlap = true;
                    }
                }
            }
        }

        if (!hasCorrectableOverlap)
            return false;

        for (int i = 0; i < SolverBodies.Count; i++)
        {
            EnemyCrowdPriorityBody body = SolverBodies[i];
            Vector3 applied = EnemyCrowdPrioritySolver.ApplyAccumulatedCorrection(
                ref body,
                SolverCorrections[i],
                MaximumCentralCorrection);
            if (applied.sqrMagnitude > 0.0000001f
                && SolverYieldCorrections[i].sqrMagnitude > 0.0000001f)
            {
                body.YieldCorrection += SolverYieldCorrections[i];
                body.YieldPressure = Mathf.Max(body.YieldPressure, SolverYieldPressures[i]);
            }
            SolverBodies[i] = body;
        }
        return true;
    }

    private static void ApplyResolvedMovement()
    {
        for (int i = 0; i < SolverBodies.Count; i++)
        {
            int intentIndex = SolverIntentIndices[i];
            if (intentIndex < 0 || intentIndex >= MovementIntents.Count)
                continue;

            MovementIntent intent = MovementIntents[intentIndex];
            EnemyCrowdAgent agent = SolverAgents[i];
            if (intent.Movement == null
                || agent == null
                || !agent.IsCrowdActive
                || !intent.Movement.isActiveAndEnabled)
            {
                continue;
            }

            EnemyCrowdPriorityBody body = SolverBodies[i];
            Vector3 finalPosition = intent.Movement.ResolveCentralCrowdCandidate(
                body.DesiredPosition,
                body.ResolvedPosition,
                agent);
            Vector3 actualCorrection = finalPosition - body.DesiredPosition;
            actualCorrection.y = 0f;
            if (body.YieldCorrection.sqrMagnitude > 0.0001f
                && Vector3.Dot(actualCorrection, body.YieldCorrection) > 0f)
            {
                agent.RegisterPriorityYield(actualCorrection, body.YieldPressure);
            }

            intent.Movement.ApplyCentralCrowdMovement(
                intent.CurrentPosition,
                finalPosition,
                intent.TurnSpeed,
                intent.FacingDirection,
                intent.MoveSpeed);
        }
    }

    private static void RebuildSolverCells()
    {
        ReleaseSolverBuckets();
        for (int i = 0; i < SolverBodies.Count; i++)
        {
            GridCell cell = GridCell.FromPosition(SolverBodies[i].ResolvedPosition);
            if (!SolverCells.TryGetValue(cell, out List<int> bucket))
            {
                bucket = SolverBucketPool.Count > 0
                    ? SolverBucketPool.Pop()
                    : new List<int>(8);
                SolverCells.Add(cell, bucket);
            }
            bucket.Add(i);
        }
    }

    private static void ReleaseSolverBuckets()
    {
        foreach (KeyValuePair<GridCell, List<int>> pair in SolverCells)
        {
            pair.Value.Clear();
            SolverBucketPool.Push(pair.Value);
        }
        SolverCells.Clear();
    }

    private static void RebuildSnapshot(float currentFixedTime)
    {
        ReleaseBuckets();
        StaleAgents.Clear();
        snapshotMaxBodyRadius = 0.5f;

        foreach (EnemyCrowdAgent agent in RegisteredAgents)
        {
            if (agent == null)
            {
                StaleAgents.Add(agent);
                continue;
            }
            if (!agent.IsCrowdActive)
                continue;

            agent.CaptureSnapshot();
            snapshotMaxBodyRadius = Mathf.Max(snapshotMaxBodyRadius, agent.BodyRadius);
            GridCell cell = GridCell.FromPosition(agent.SnapshotPosition);
            if (!Cells.TryGetValue(cell, out List<EnemyCrowdAgent> bucket))
            {
                bucket = AcquireBucket();
                Cells.Add(cell, bucket);
            }

            bucket.Add(agent);
        }

        for (int i = 0; i < StaleAgents.Count; i++)
            RegisteredAgents.Remove(StaleAgents[i]);
        StaleAgents.Clear();
        snapshotFixedTime = currentFixedTime;
        snapshotDirty = false;
    }

    private static List<EnemyCrowdAgent> AcquireBucket()
    {
        return BucketPool.Count > 0
            ? BucketPool.Pop()
            : new List<EnemyCrowdAgent>(8);
    }

    private static void ReleaseBuckets()
    {
        foreach (KeyValuePair<GridCell, List<EnemyCrowdAgent>> pair in Cells)
        {
            pair.Value.Clear();
            BucketPool.Push(pair.Value);
        }

        Cells.Clear();
    }

    private static int ToCellCoordinate(float value)
    {
        return Mathf.FloorToInt(value / CellSize);
    }

    private static float ResolveSpawnStartAngle(Vector3 center)
    {
        int x = Mathf.RoundToInt(center.x * 10f);
        int z = Mathf.RoundToInt(center.z * 10f);
        uint hash = unchecked((uint)(x * 397 ^ z ^ RegisteredAgents.Count * 31));
        hash ^= hash >> 16;
        hash *= 0x7feb352d;
        hash ^= hash >> 15;
        return (hash % 3600u) * 0.1f * Mathf.Deg2Rad;
    }

    private struct GridCell : System.IEquatable<GridCell>
    {
        private readonly int x;
        private readonly int z;

        public GridCell(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public static GridCell FromPosition(Vector3 position)
        {
            return new GridCell(ToCellCoordinate(position.x), ToCellCoordinate(position.z));
        }

        public bool Equals(GridCell other)
        {
            return x == other.x && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCell other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((x * 397) ^ z);
        }
    }

    private struct MovementIntent
    {
        public MovementIntent(
            EnemyMovement movement,
            EnemyCrowdAgent agent,
            Vector3 currentPosition,
            Vector3 desiredPosition,
            float turnSpeed,
            Vector3 facingDirection,
            float moveSpeed)
        {
            Movement = movement;
            Agent = agent;
            CurrentPosition = currentPosition;
            DesiredPosition = desiredPosition;
            TurnSpeed = turnSpeed;
            FacingDirection = facingDirection;
            MoveSpeed = moveSpeed;
        }

        public EnemyMovement Movement;
        public EnemyCrowdAgent Agent;
        public Vector3 CurrentPosition;
        public Vector3 DesiredPosition;
        public float TurnSpeed;
        public Vector3 FacingDirection;
        public float MoveSpeed;
    }
}

[DefaultExecutionOrder(10000)]
internal sealed class EnemyCrowdMovementDriver : MonoBehaviour // 모든 EnemyMovement 뒤에서 중앙 후보를 한 번 적용
{
    private void FixedUpdate()
    {
        EnemyCrowdService.ResolveSubmittedMovement();
    }

    private void OnDestroy()
    {
        EnemyCrowdService.NotifyMovementDriverDestroyed(this);
    }
}
