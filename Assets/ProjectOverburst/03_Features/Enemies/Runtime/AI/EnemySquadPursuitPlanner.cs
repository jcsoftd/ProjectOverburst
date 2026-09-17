using System.Collections.Generic;
using UnityEngine;

public enum EnemySquadPursuitSlotKind // 고정 8점에 교대 배치할 추격 역할
{
    Direct,
    FrontRightDiagonal,
    RightBypass,
    RearRightDiagonal,
    Rear,
    RearLeftDiagonal,
    LeftBypass,
    FrontLeftDiagonal
}

public readonly struct EnemySquadPursuitSlot // 런타임과 시뮬레이터가 공유할 슬롯 결과
{
    public EnemySquadPursuitSlot(
        int index,
        int pointIndex,
        EnemySquadPursuitSlotKind kind,
        Vector3 position,
        Vector3 outwardDirection,
        float radius)
    {
        Index = index;
        PointIndex = pointIndex;
        Kind = kind;
        Position = position;
        OutwardDirection = outwardDirection;
        Radius = radius;
    }

    public int Index { get; } // 월드 나침반 기준 물리 점 인덱스
    public int PointIndex { get; } // 월드 나침반 기준 고정 점 인덱스
    public EnemySquadPursuitSlotKind Kind { get; }
    public Vector3 Position { get; }
    public Vector3 OutwardDirection { get; }
    public float Radius { get; }
}

public readonly struct EnemySquadPursuitRoute // 할당 없는 최대 5점 부대 경로
{
    public EnemySquadPursuitRoute(int waypointCount, Vector3 first, Vector3 second, Vector3 third)
        : this(waypointCount, first, second, third, Vector3.zero, Vector3.zero)
    {
    }

    public EnemySquadPursuitRoute(
        int waypointCount,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector3 fourth,
        Vector3 fifth)
    {
        WaypointCount = Mathf.Clamp(waypointCount, 0, 5);
        First = first;
        Second = second;
        Third = third;
        Fourth = fourth;
        Fifth = fifth;
    }

    public int WaypointCount { get; }
    public Vector3 First { get; }
    public Vector3 Second { get; }
    public Vector3 Third { get; }
    public Vector3 Fourth { get; }
    public Vector3 Fifth { get; }

    public Vector3 GetWaypoint(int index)
    {
        switch (index)
        {
            case 0:
                return First;
            case 1:
                return Second;
            case 2:
                return Third;
            case 3:
                return Fourth;
            case 4:
                return Fifth;
            default:
                return WaypointCount > 0 ? First : Vector3.zero;
        }
    }
}

public static class EnemySquadPursuitPlanner // 대량 웨이브 부대 편성과 추격 슬롯 순수 계산
{
    public const int DefaultActivationCount = 41;
    public const int DefaultMinimumSquadSize = 8;
    public const int DefaultMaximumSquadSize = 12;
    public const float DefaultSlotRadius = 5.5f;
    public const float DefaultDirectCommitRadius = 8.5f;
    public const float DefaultNearReleaseDistance = 4.25f;
    public const float DefaultFarActivationDistance = 11f;
    public const float DefaultReserveSpeedMultiplier = 0.85f;
    public const float DefaultRemnantRatio = 0.4f;
    public const float DefaultRearSlotClaimAngle = 30f;
    public const float DefaultRearSlotReleaseAngle = 45f;
    public const float DefaultReserveOrbitRadius = 13f;
    public const float DefaultReserveOrbitMargin = 3.5f;
    public const float DefaultReserveRingSpacing = 1.15f;
    public const int DefaultReserveRingCount = 3;
    public const float DefaultRouteRefreshMoveThreshold = 1f;
    public const float DefaultRouteRefreshMinimumInterval = 0.25f;
    public const float DefaultRouteRefreshHeadingThreshold = 15f;
    public const int DefaultMaximumRouteRefreshPerFrame = 2;
    public const float DefaultFullReformationInterval = 10f;
    public const float DefaultFullReformationMoveTrigger = 12f;
    public const float DefaultFullReformationEmergencyCooldown = 5f;
    public const float DefaultCohesionDeadZone = 0.75f;
    public const float DefaultCohesionLookAhead = 3f;
    public const float DefaultCohesionCorrectionWeight = 0.55f;
    public const float DefaultCohesionMaximumCorrection = 1.2f;
    public const float DefaultCohesionCatchUpStart = 1.5f;
    public const float DefaultCohesionCatchUpFullDistance = 5f;
    public const float DefaultCohesionMaximumSpeedBoost = 0.12f;

    private const float MinimumDirectionSqrMagnitude = 0.0001f;
    private const float DiagonalDirection = 0.70710678f;
    private const float ReserveGoldenAngle = 137.50776f;
    private const float ReserveFinalAlignment = 0.94f;
    private const float ReserveOrbitTurnWeight = 0.5f;
    private const float FrontDiagonalCurveMinimum = 1.25f;
    private const float FrontDiagonalCurveWeight = 0.145f;
    private const float FrontDiagonalSideRatio = 0.31f;
    private const float SideBypassCurveMinimum = 3.75f;
    private const float SideBypassCurveWeight = 0.43f;
    private const float SideBypassSideRatio = 0.8f;
    private const float RearDiagonalCurveMinimum = 5.1f;
    private const float RearDiagonalCurveWeight = 0.56f;
    private const float RearDiagonalSideRatio = 1.02f;
    private const double SlotPriorityTieBreakWeight = 0.0000001d;

    private static readonly Vector3[] FixedPointDirections = // 월드 나침반 기준 위치는 회전시키지 않음
    {
        new Vector3(1f, 0f, 0f),
        new Vector3(DiagonalDirection, 0f, -DiagonalDirection),
        new Vector3(0f, 0f, -1f),
        new Vector3(-DiagonalDirection, 0f, -DiagonalDirection),
        new Vector3(-1f, 0f, 0f),
        new Vector3(-DiagonalDirection, 0f, DiagonalDirection),
        new Vector3(0f, 0f, 1f),
        new Vector3(DiagonalDirection, 0f, DiagonalDirection)
    };

    public static void BuildMaximumFirstSquadSizes(
        int monsterCount,
        int minimumSquadSize,
        int maximumSquadSize,
        List<int> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();
        int minimum = Mathf.Max(1, minimumSquadSize);
        int maximum = Mathf.Max(minimum, maximumSquadSize);
        int count = Mathf.Max(0, monsterCount);
        if (count < minimum)
            return;

        int fullSquadCount = count / maximum;
        for (int i = 0; i < fullSquadCount; i++)
            results.Add(maximum); // 앞 부대부터 최대 인원 우선 충전

        int remainder = count - fullSquadCount * maximum;
        if (remainder >= minimum)
            results.Add(remainder); // 최소 미만 잔여는 Unassigned로 다음 충원까지 대기
    }

    public static Vector3 ResolveDirectOutward(Vector3 playerPosition, Vector3 nearestSquadCenter)
    {
        Vector3 outward = Flatten(nearestSquadCenter - playerPosition);
        if (outward.sqrMagnitude <= MinimumDirectionSqrMagnitude)
            return Vector3.forward;

        return outward.normalized;
    }

    public static void BuildSlots(
        Vector3 playerPosition,
        Vector3 directOutward,
        float slotRadius,
        List<EnemySquadPursuitSlot> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();
        Vector3 resolvedOutward = NormalizeOrFallback(directOutward, Vector3.forward);
        int directPointIndex = ResolveDirectPointIndex(resolvedOutward);
        float radius = Mathf.Max(0.5f, slotRadius);
        for (int pointIndex = 0; pointIndex < 8; pointIndex++)
        {
            int roleIndex = (pointIndex - directPointIndex + 8) % 8;
            EnemySquadPursuitSlotKind kind = (EnemySquadPursuitSlotKind)roleIndex;
            Vector3 direction = ResolveFixedPointDirection(pointIndex);
            results.Add(new EnemySquadPursuitSlot(
                pointIndex,
                pointIndex,
                kind,
                playerPosition + direction * radius,
                direction,
                radius));
        }
    }

    public static int ResolveDirectPointIndex(Vector3 directOutward)
    {
        Vector3 resolvedOutward = NormalizeOrFallback(directOutward, Vector3.forward);
        int bestIndex = 0;
        float bestDot = float.NegativeInfinity;
        for (int pointIndex = 0; pointIndex < 8; pointIndex++)
        {
            float dot = Vector3.Dot(resolvedOutward, ResolveFixedPointDirection(pointIndex));
            if (dot > bestDot)
            {
                bestDot = dot;
                bestIndex = pointIndex;
            }
        }

        return bestIndex;
    }

    public static EnemySquadPursuitRoute BuildRoute(
        Vector3 squadCenter,
        Vector3 playerPosition,
        Vector3 directOutward,
        EnemySquadPursuitSlot slot,
        IReadOnlyList<EnemySquadPursuitSlot> slots)
    {
        return BuildRoute(
            squadCenter,
            playerPosition,
            directOutward,
            slot,
            slots,
            1f);
    }

    public static EnemySquadPursuitRoute BuildRoute(
        Vector3 squadCenter,
        Vector3 playerPosition,
        Vector3 directOutward,
        EnemySquadPursuitSlot slot,
        IReadOnlyList<EnemySquadPursuitSlot> slots,
        float curveRadiusMultiplier)
    {
        Vector3 resolvedOutward = TryFindSlot(slots, EnemySquadPursuitSlotKind.Direct, out EnemySquadPursuitSlot directSlot)
            ? NormalizeOrFallback(directSlot.OutwardDirection, directOutward)
            : NormalizeOrFallback(directOutward, Vector3.forward);
        Vector3 right = RotateRight(resolvedOutward);
        if (TryResolveCurveProfile(
            slot.Kind,
            right,
            out Vector3 curveSide,
            out float curveMinimum,
            out float curveWeight,
            out float minimumSideRatio))
        {
            float curveScale = Mathf.Clamp(curveRadiusMultiplier, 0.4f, 1.5f);
            curveMinimum *= curveScale;
            curveWeight *= curveScale;
            minimumSideRatio *= curveScale;
            Vector3 start = Flatten(squadCenter);
            Vector3 end = Flatten(slot.Position);
            float distance = Mathf.Sqrt(HorizontalSqrDistance(start, end));
            float curveOffset = Mathf.Max(curveMinimum, distance * curveWeight);
            Vector3 control = (start + end) * 0.5f + curveSide * curveOffset;
            float minimumSideDistance = Mathf.Max(curveMinimum, slot.Radius * minimumSideRatio);
            float controlSideDistance = Vector3.Dot(Flatten(control - playerPosition), curveSide);
            if (controlSideDistance < minimumSideDistance)
                control += curveSide * (minimumSideDistance - controlSideDistance);

            return new EnemySquadPursuitRoute(
                5,
                EvaluateQuadratic(start, control, end, 0.2f),
                EvaluateQuadratic(start, control, end, 0.4f),
                EvaluateQuadratic(start, control, end, 0.6f),
                EvaluateQuadratic(start, control, end, 0.8f),
                end);
        }

        return new EnemySquadPursuitRoute(1, slot.Position, playerPosition, Vector3.zero);
    }

    public static float MeasureRemainingRouteDistance(
        Vector3 currentPosition,
        EnemySquadPursuitRoute route,
        int waypointIndex)
    {
        if (route.WaypointCount <= 0)
            return 0f;

        int startIndex = Mathf.Clamp(waypointIndex, 0, route.WaypointCount - 1);
        float distance = 0f;
        Vector3 previous = currentPosition;
        for (int index = startIndex; index < route.WaypointCount; index++)
        {
            Vector3 waypoint = route.GetWaypoint(index);
            distance += Mathf.Sqrt(HorizontalSqrDistance(previous, waypoint));
            previous = waypoint;
        }

        return distance;
    }

    public static bool ShouldRefreshRoute(float movedDistance, float now, float nextRefreshTime, bool pending)
    {
        return !pending
            && movedDistance >= DefaultRouteRefreshMoveThreshold
            && now >= nextRefreshTime;
    }

    public static bool ShouldRefreshFullReformation(
        float movedDistance,
        float now,
        float nextPeriodicTime,
        float lastRefreshTime)
    {
        bool periodic = now >= nextPeriodicTime;
        bool emergency = movedDistance >= DefaultFullReformationMoveTrigger
            && now - lastRefreshTime >= DefaultFullReformationEmergencyCooldown;
        return periodic || emergency;
    }

    public static EnemySquadPursuitRoute TranslateRoute(EnemySquadPursuitRoute route, Vector3 delta)
    {
        Vector3 horizontalDelta = Flatten(delta);
        if (route.WaypointCount <= 0 || horizontalDelta.sqrMagnitude <= MinimumDirectionSqrMagnitude)
            return route;

        return new EnemySquadPursuitRoute(
            route.WaypointCount,
            route.First + horizontalDelta,
            route.Second + horizontalDelta,
            route.Third + horizontalDelta,
            route.Fourth + horizontalDelta,
            route.Fifth + horizontalDelta);
    }

    public static Vector3 ResolveCohesionDestination(
        Vector3 memberPosition,
        Vector3 squadCenter,
        Vector3 formationOffset,
        Vector3 squadDestination,
        float formationOffsetScale)
    {
        Vector3 current = Flatten(memberPosition);
        Vector3 scaledOffset = Flatten(formationOffset) * Mathf.Max(0f, formationOffsetScale);
        Vector3 baseDestination = Flatten(squadDestination) + scaledOffset;
        Vector3 baseDelta = baseDestination - current;
        if (baseDelta.sqrMagnitude <= MinimumDirectionSqrMagnitude)
            return PreserveHeight(baseDestination, memberPosition.y);

        Vector3 formationPosition = Flatten(squadCenter) + scaledOffset;
        Vector3 formationError = formationPosition - current;
        float formationDistance = formationError.magnitude;
        if (formationDistance <= DefaultCohesionDeadZone)
            return PreserveHeight(baseDestination, memberPosition.y);

        float lookAhead = Mathf.Min(DefaultCohesionLookAhead, baseDelta.magnitude);
        float correction = Mathf.Min(
            DefaultCohesionMaximumCorrection,
            (formationDistance - DefaultCohesionDeadZone) * DefaultCohesionCorrectionWeight);
        Vector3 steeringDestination = current
            + baseDelta.normalized * lookAhead
            + formationError.normalized * correction;
        return PreserveHeight(steeringDestination, memberPosition.y);
    }

    public static float ResolveCohesionSpeedMultiplier(
        Vector3 memberPosition,
        Vector3 squadCenter,
        Vector3 squadDestination)
    {
        Vector3 forward = Flatten(squadDestination - squadCenter);
        if (forward.sqrMagnitude <= MinimumDirectionSqrMagnitude)
            return 1f;

        float trailingDistance = Vector3.Dot(
            Flatten(squadCenter - memberPosition),
            forward.normalized);
        float catchUp = Mathf.InverseLerp(
            DefaultCohesionCatchUpStart,
            DefaultCohesionCatchUpFullDistance,
            trailingDistance);
        return 1f + catchUp * DefaultCohesionMaximumSpeedBoost;
    }

    public static bool IsWithinSlotAngularSector(
        Vector3 playerPosition,
        Vector3 squadCenter,
        Vector3 slotOutwardDirection,
        float maximumAngle)
    {
        Vector3 toSquad = Flatten(squadCenter - playerPosition);
        Vector3 outward = Flatten(slotOutwardDirection);
        if (toSquad.sqrMagnitude <= MinimumDirectionSqrMagnitude
            || outward.sqrMagnitude <= MinimumDirectionSqrMagnitude)
        {
            return false;
        }

        float minimumDot = Mathf.Cos(Mathf.Clamp(maximumAngle, 0f, 180f) * Mathf.Deg2Rad);
        return Vector3.Dot(toSquad.normalized, outward.normalized) >= minimumDot;
    }

    public static int GetPreferredSlotIndex(int priorityOrder)
    {
        switch (priorityOrder)
        {
            case 0:
                return 0; // 가장 가까운 부대 직선
            case 1:
                return 2; // 우측 우회
            case 2:
                return 6; // 좌측 우회
            case 3:
                return 1; // 정면 우측 대각
            case 4:
                return 7; // 정면 좌측 대각
            case 5:
                return 3; // 후방 우측 대각
            case 6:
                return 5; // 후방 좌측 대각
            case 7:
                return 4; // 후방은 마지막
            default:
                return -1;
        }
    }

    public static void BuildBalancedSlotIndices(
        IReadOnlyList<EnemySquadPursuitSlot> candidateSlots,
        int desiredCount,
        List<int> selectedSlotIndices)
    {
        if (candidateSlots == null)
            throw new System.ArgumentNullException(nameof(candidateSlots));
        if (selectedSlotIndices == null)
            throw new System.ArgumentNullException(nameof(selectedSlotIndices));

        selectedSlotIndices.Clear();
        int[] candidateListIndices = new int[candidateSlots.Count];
        int candidateCount = 0;
        int directCandidateIndex = -1;
        for (int index = 0; index < candidateSlots.Count; index++)
        {
            EnemySquadPursuitSlot slot = candidateSlots[index];
            if (slot.Kind == EnemySquadPursuitSlotKind.Rear)
                continue;
            candidateListIndices[candidateCount] = index;
            if (slot.Kind == EnemySquadPursuitSlotKind.Direct)
                directCandidateIndex = candidateCount;
            candidateCount++;
        }

        int targetCount = Mathf.Clamp(desiredCount, 0, candidateCount);
        if (targetCount <= 0)
            return;

        int bestMask = 0;
        int bestMaximumGap = int.MaxValue;
        float bestCentroidSqr = float.PositiveInfinity;
        int bestPriorityScore = int.MaxValue;
        int combinationCount = 1 << candidateCount;
        for (int mask = 1; mask < combinationCount; mask++)
        {
            if (CountSetBits(mask) != targetCount)
                continue;
            if (directCandidateIndex >= 0 && (mask & (1 << directCandidateIndex)) == 0)
                continue;

            int pointMask = 0;
            int priorityScore = 0;
            Vector3 directionSum = Vector3.zero;
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                if ((mask & (1 << candidateIndex)) == 0)
                    continue;
                EnemySquadPursuitSlot slot = candidateSlots[candidateListIndices[candidateIndex]];
                pointMask |= 1 << slot.PointIndex;
                directionSum += slot.OutwardDirection;
                priorityScore += ResolveSlotPriority(slot.Kind);
            }

            int maximumGap = ResolveMaximumPointGap(pointMask);
            float centroidSqr = directionSum.sqrMagnitude / (targetCount * targetCount);
            bool isBetter = maximumGap < bestMaximumGap
                || maximumGap == bestMaximumGap && centroidSqr < bestCentroidSqr - 0.0001f
                || maximumGap == bestMaximumGap
                    && Mathf.Abs(centroidSqr - bestCentroidSqr) <= 0.0001f
                    && priorityScore < bestPriorityScore;
            if (!isBetter)
                continue;

            bestMask = mask;
            bestMaximumGap = maximumGap;
            bestCentroidSqr = centroidSqr;
            bestPriorityScore = priorityScore;
        }

        for (int priorityOrder = 0; priorityOrder < 8; priorityOrder++)
        {
            EnemySquadPursuitSlotKind preferredKind = (EnemySquadPursuitSlotKind)GetPreferredSlotIndex(priorityOrder);
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                if ((bestMask & (1 << candidateIndex)) == 0)
                    continue;
                EnemySquadPursuitSlot slot = candidateSlots[candidateListIndices[candidateIndex]];
                if (slot.Kind == preferredKind)
                {
                    selectedSlotIndices.Add(slot.Index);
                    break;
                }
            }
        }
    }

    public static void BuildMinimumDistanceAssignment(
        IReadOnlyList<Vector3> squadCenters,
        IReadOnlyList<Vector3> slotPositions,
        List<int> squadIndexBySlot)
    {
        if (squadCenters == null)
            throw new System.ArgumentNullException(nameof(squadCenters));
        if (slotPositions == null)
            throw new System.ArgumentNullException(nameof(slotPositions));
        if (squadIndexBySlot == null)
            throw new System.ArgumentNullException(nameof(squadIndexBySlot));

        squadIndexBySlot.Clear();
        for (int slotIndex = 0; slotIndex < slotPositions.Count; slotIndex++)
            squadIndexBySlot.Add(-1);

        int slotCount = slotPositions.Count;
        int squadCount = squadCenters.Count;
        bool rowsAreSlots = slotCount <= squadCount;
        int rowCount = Mathf.Min(slotCount, squadCount);
        int columnCount = Mathf.Max(slotCount, squadCount);
        if (rowCount <= 0 || columnCount <= 0)
            return;

        double[] rowPotential = new double[rowCount + 1];
        double[] columnPotential = new double[columnCount + 1];
        int[] rowByColumn = new int[columnCount + 1];
        int[] previousColumn = new int[columnCount + 1];
        double[] minimumCost = new double[columnCount + 1];
        bool[] usedColumn = new bool[columnCount + 1];

        for (int row = 1; row <= rowCount; row++)
        {
            rowByColumn[0] = row;
            int currentColumn = 0;
            for (int column = 0; column <= columnCount; column++)
            {
                minimumCost[column] = double.PositiveInfinity;
                usedColumn[column] = false;
                previousColumn[column] = 0;
            }

            do
            {
                usedColumn[currentColumn] = true;
                int currentRow = rowByColumn[currentColumn];
                double delta = double.PositiveInfinity;
                int nextColumn = 0;

                for (int column = 1; column <= columnCount; column++)
                {
                    if (usedColumn[column])
                        continue;

                    int slotIndex = rowsAreSlots ? currentRow - 1 : column - 1;
                    int squadIndex = rowsAreSlots ? column - 1 : currentRow - 1;
                    double cost = System.Math.Sqrt(HorizontalSqrDistance(
                        slotPositions[slotIndex],
                        squadCenters[squadIndex]));
                    cost += slotIndex * SlotPriorityTieBreakWeight;
                    double reducedCost = cost - rowPotential[currentRow] - columnPotential[column];
                    if (reducedCost < minimumCost[column])
                    {
                        minimumCost[column] = reducedCost;
                        previousColumn[column] = currentColumn;
                    }
                    if (minimumCost[column] < delta)
                    {
                        delta = minimumCost[column];
                        nextColumn = column;
                    }
                }

                for (int column = 0; column <= columnCount; column++)
                {
                    if (usedColumn[column])
                    {
                        rowPotential[rowByColumn[column]] += delta;
                        columnPotential[column] -= delta;
                    }
                    else
                    {
                        minimumCost[column] -= delta;
                    }
                }
                currentColumn = nextColumn;
            }
            while (rowByColumn[currentColumn] != 0);

            do
            {
                int nextColumn = previousColumn[currentColumn];
                rowByColumn[currentColumn] = rowByColumn[nextColumn];
                currentColumn = nextColumn;
            }
            while (currentColumn != 0);
        }

        for (int column = 1; column <= columnCount; column++)
        {
            int assignedRow = rowByColumn[column] - 1;
            if (assignedRow < 0 || assignedRow >= rowCount)
                continue;
            if (rowsAreSlots)
                squadIndexBySlot[assignedRow] = column - 1;
            else
                squadIndexBySlot[column - 1] = assignedRow;
        }
    }

    public static Vector3 ResolveReserveOrbitDirection(int stableSeed, int squadId)
    {
        return ResolveReserveOrbitDirection(stableSeed, squadId, 0f);
    }

    public static Vector3 ResolveReserveOrbitDirection(int stableSeed, int squadId, float orbitAngleDegrees)
    {
        float seedOffset = ResolveStableSigned01(unchecked(stableSeed * 486187739)) * 180f;
        float angle = seedOffset + Mathf.Max(0, squadId - 1) * ReserveGoldenAngle + orbitAngleDegrees;
        double radians = angle * System.Math.PI / 180d;
        return new Vector3(
            (float)System.Math.Cos(radians),
            0f,
            (float)-System.Math.Sin(radians));
    }

    public static float ResolveLegacyReserveOrbitBaseRadius(
        float maximumSlotRadius,
        float farActivationDistance)
    {
        return Mathf.Max(
            Mathf.Max(0.5f, maximumSlotRadius) + DefaultReserveOrbitMargin,
            Mathf.Max(0.5f, farActivationDistance) + 2f);
    }

    public static float ResolveReserveOrbitRadius(float baseOrbitRadius, int squadId)
    {
        float baseRadius = Mathf.Max(0.5f, baseOrbitRadius);
        int lane = Mathf.Abs(squadId) % DefaultReserveRingCount;
        return baseRadius + lane * DefaultReserveRingSpacing;
    }

    public static Vector3 ResolveReserveOrbitDestination(
        Vector3 playerPosition,
        Vector3 squadCenter,
        Vector3 targetOutward,
        float orbitRadius,
        int squadId)
    {
        Vector3 targetDirection = NormalizeOrFallback(targetOutward, Vector3.forward);
        Vector3 currentOffset = Flatten(squadCenter - playerPosition);
        Vector3 currentDirection = NormalizeOrFallback(currentOffset, targetDirection);
        float radius = Mathf.Max(0.5f, orbitRadius);
        if (Vector3.Dot(currentDirection, targetDirection) >= ReserveFinalAlignment)
            return playerPosition + targetDirection * radius;

        Vector3 right = RotateRight(currentDirection);
        float rightScore = Vector3.Dot(right, targetDirection);
        Vector3 tangent;
        if (Mathf.Abs(rightScore) <= 0.0001f)
            tangent = (squadId & 1) == 0 ? right : -right;
        else
            tangent = rightScore > 0f ? right : -right;

        Vector3 nextDirection = NormalizeOrFallback(
            currentDirection + tangent * ReserveOrbitTurnWeight,
            targetDirection);
        float turnProjection = Mathf.Max(0.1f, Vector3.Dot(currentDirection, nextDirection));
        float protectedRadius = radius / turnProjection; // 직선 보간 중에도 외곽 반경 안으로 파고들지 않음
        return playerPosition + nextDirection * protectedRadius;
    }

    public static string ResolveSlotDisplayName(EnemySquadPursuitSlotKind kind)
    {
        switch (kind)
        {
            case EnemySquadPursuitSlotKind.Direct:
                return "정면 직선";
            case EnemySquadPursuitSlotKind.FrontRightDiagonal:
                return "정면 우측 대각";
            case EnemySquadPursuitSlotKind.RightBypass:
                return "우측 우회";
            case EnemySquadPursuitSlotKind.RearRightDiagonal:
                return "후방 우측 대각";
            case EnemySquadPursuitSlotKind.Rear:
                return "후방";
            case EnemySquadPursuitSlotKind.RearLeftDiagonal:
                return "후방 좌측 대각";
            case EnemySquadPursuitSlotKind.LeftBypass:
                return "좌측 우회";
            case EnemySquadPursuitSlotKind.FrontLeftDiagonal:
                return "정면 좌측 대각";
            default:
                return "미지정";
        }
    }

    private static Vector3 ResolveFixedPointDirection(int pointIndex)
    {
        return FixedPointDirections[pointIndex % 8];
    }

    private static Vector3 RotateRight(Vector3 direction)
    {
        Vector3 horizontal = NormalizeOrFallback(direction, Vector3.forward);
        return new Vector3(horizontal.z, 0f, -horizontal.x);
    }

    private static bool TryFindSlot(
        IReadOnlyList<EnemySquadPursuitSlot> slots,
        EnemySquadPursuitSlotKind kind,
        out EnemySquadPursuitSlot result)
    {
        if (slots != null)
        {
            for (int index = 0; index < slots.Count; index++)
            {
                if (slots[index].Kind != kind)
                    continue;
                result = slots[index];
                return true;
            }
        }

        result = default;
        return false;
    }

    private static int CountSetBits(int value)
    {
        int count = 0;
        int remaining = value;
        while (remaining != 0)
        {
            count += remaining & 1;
            remaining >>= 1;
        }
        return count;
    }

    private static int ResolveMaximumPointGap(int pointMask)
    {
        int firstPoint = -1;
        int previousPoint = -1;
        int maximumGap = 0;
        for (int pointIndex = 0; pointIndex < 8; pointIndex++)
        {
            if ((pointMask & (1 << pointIndex)) == 0)
                continue;
            if (firstPoint < 0)
                firstPoint = pointIndex;
            if (previousPoint >= 0)
                maximumGap = Mathf.Max(maximumGap, pointIndex - previousPoint);
            previousPoint = pointIndex;
        }

        if (firstPoint < 0)
            return 8;
        return Mathf.Max(maximumGap, firstPoint + 8 - previousPoint);
    }

    private static int ResolveSlotPriority(EnemySquadPursuitSlotKind kind)
    {
        for (int priorityOrder = 0; priorityOrder < 8; priorityOrder++)
        {
            if (GetPreferredSlotIndex(priorityOrder) == (int)kind)
                return priorityOrder;
        }
        return 8;
    }

    private static bool TryResolveCurveProfile(
        EnemySquadPursuitSlotKind kind,
        Vector3 right,
        out Vector3 side,
        out float minimum,
        out float weight,
        out float minimumSideRatio)
    {
        side = right;
        minimum = 0f;
        weight = 0f;
        minimumSideRatio = 0f;
        switch (kind)
        {
            case EnemySquadPursuitSlotKind.FrontRightDiagonal:
                minimum = FrontDiagonalCurveMinimum;
                weight = FrontDiagonalCurveWeight;
                minimumSideRatio = FrontDiagonalSideRatio;
                return true;
            case EnemySquadPursuitSlotKind.FrontLeftDiagonal:
                side = -right;
                minimum = FrontDiagonalCurveMinimum;
                weight = FrontDiagonalCurveWeight;
                minimumSideRatio = FrontDiagonalSideRatio;
                return true;
            case EnemySquadPursuitSlotKind.RightBypass:
                minimum = SideBypassCurveMinimum;
                weight = SideBypassCurveWeight;
                minimumSideRatio = SideBypassSideRatio;
                return true;
            case EnemySquadPursuitSlotKind.LeftBypass:
                side = -right;
                minimum = SideBypassCurveMinimum;
                weight = SideBypassCurveWeight;
                minimumSideRatio = SideBypassSideRatio;
                return true;
            case EnemySquadPursuitSlotKind.RearRightDiagonal:
                minimum = RearDiagonalCurveMinimum;
                weight = RearDiagonalCurveWeight;
                minimumSideRatio = RearDiagonalSideRatio;
                return true;
            case EnemySquadPursuitSlotKind.RearLeftDiagonal:
                side = -right;
                minimum = RearDiagonalCurveMinimum;
                weight = RearDiagonalCurveWeight;
                minimumSideRatio = RearDiagonalSideRatio;
                return true;
            default:
                return false;
        }
    }

    private static Vector3 EvaluateQuadratic(
        Vector3 start,
        Vector3 control,
        Vector3 end,
        float time)
    {
        float t = Mathf.Clamp01(time);
        float inverse = 1f - t;
        return inverse * inverse * start
            + 2f * inverse * t * control
            + t * t * end;
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        Vector3 horizontal = Flatten(value);
        if (horizontal.sqrMagnitude > MinimumDirectionSqrMagnitude)
            return horizontal.normalized;

        horizontal = Flatten(fallback);
        return horizontal.sqrMagnitude > MinimumDirectionSqrMagnitude
            ? horizontal.normalized
            : Vector3.forward;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static Vector3 PreserveHeight(Vector3 value, float height)
    {
        value.y = height;
        return value;
    }

    private static float HorizontalSqrDistance(Vector3 from, Vector3 to)
    {
        return Flatten(to - from).sqrMagnitude;
    }

    private static float ResolveStableSigned01(int value)
    {
        uint hash = unchecked((uint)value);
        hash ^= hash >> 16;
        hash *= 0x7feb352d;
        hash ^= hash >> 15;
        hash *= 0x846ca68b;
        hash ^= hash >> 16;
        return (hash / (float)uint.MaxValue) * 2f - 1f;
    }
}
