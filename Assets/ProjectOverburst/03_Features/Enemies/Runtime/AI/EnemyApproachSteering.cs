using System.Collections.Generic;
using UnityEngine;

public readonly struct EnemyApproachNeighbor // 실제 현재 위치만 담는 조향 입력
{
    public EnemyApproachNeighbor(Vector3 position, float bodyRadius)
    {
        Position = position;
        BodyRadius = Mathf.Max(0.05f, bodyRadius);
    }

    public Vector3 Position { get; }
    public float BodyRadius { get; }
}

public readonly struct EnemyApproachSteeringInput // 상태를 갖지 않는 한 번의 계산 입력
{
    public EnemyApproachSteeringInput(
        Vector3 agentPosition,
        Vector3 targetPosition,
        Vector3 navigationDirection,
        Vector3 separationDirection,
        float preferredRadius,
        float bodyRadius,
        int stableId,
        int currentTurnSign = 0,
        bool allowTurnSwitch = true,
        float neighborQueryRadius = 0f,
        float separationWeight = 1f)
    {
        AgentPosition = agentPosition;
        TargetPosition = targetPosition;
        NavigationDirection = navigationDirection;
        SeparationDirection = separationDirection;
        PreferredRadius = preferredRadius;
        BodyRadius = bodyRadius;
        StableId = stableId;
        CurrentTurnSign = currentTurnSign;
        AllowTurnSwitch = allowTurnSwitch;
        NeighborQueryRadius = neighborQueryRadius;
        SeparationWeight = separationWeight;
    }

    public Vector3 AgentPosition { get; }
    public Vector3 TargetPosition { get; }
    public Vector3 NavigationDirection { get; }
    public Vector3 SeparationDirection { get; }
    public float PreferredRadius { get; }
    public float BodyRadius { get; }
    public int StableId { get; }
    public int CurrentTurnSign { get; }
    public bool AllowTurnSwitch { get; }
    public float NeighborQueryRadius { get; }
    public float SeparationWeight { get; }
}

public readonly struct EnemyApproachSteeringResult // Chase 연결 전 검증 가능한 순수 결과
{
    public EnemyApproachSteeringResult(
        Vector3 direction,
        Vector3 radialCorrection,
        Vector3 tangentDirection,
        float leftDensity,
        float rightDensity,
        float localBlend,
        float congestion,
        int turnSign)
    {
        Direction = direction;
        RadialCorrection = radialCorrection;
        TangentDirection = tangentDirection;
        LeftDensity = leftDensity;
        RightDensity = rightDensity;
        LocalBlend = localBlend;
        Congestion = congestion;
        TurnSign = turnSign;
    }

    public Vector3 Direction { get; }
    public Vector3 RadialCorrection { get; }
    public Vector3 TangentDirection { get; }
    public float LeftDensity { get; }
    public float RightDensity { get; }
    public float LocalBlend { get; }
    public float Congestion { get; }
    public int TurnSign { get; }
}

public static class EnemyApproachSteering // 예약 없는 실제 이웃 밀도 조향 계산
{
    public const float LocalSteeringStartDistance = 6f;
    public const float FullLocalSteeringDistance = 3f;
    public const float TurnDecisionInterval = 0.2f;
    public const float TurnLockDuration = 0.6f;
    public const float TurnSwitchDensityRatio = 0.75f;
    public const float StuckDuration = 0.75f;
    public const float MinimumProgressDistance = 0.08f;
    public const float ShortHorizonSeconds = 0.35f;
    public const float MinimumShortHorizonDistance = 0.35f;
    public const float MaximumShortHorizonDistance = 0.9f;

    private const float MinimumBodyRadius = 0.05f;
    private const float MinimumDirectionSqrMagnitude = 0.0001f;
    private const float MinimumDensityDifference = 0.001f;
    private const float DensityForFullCongestion = 1.5f;
    private const float MinimumNavigationWeight = 0.35f;
    private const float RadialWeight = 1.15f;
    private const float TangentWeight = 1.1f;

    public static EnemyApproachSteeringResult Resolve(
        EnemyApproachSteeringInput input,
        IReadOnlyList<EnemyApproachNeighbor> neighbors)
    {
        Vector3 toTarget = Flatten(input.TargetPosition - input.AgentPosition);
        float targetDistance = toTarget.magnitude;
        Vector3 inward = ResolveInwardDirection(toTarget, input.NavigationDirection, input.StableId);
        Vector3 outward = -inward;
        Vector3 navigation = NormalizeOrFallback(Flatten(input.NavigationDirection), inward);
        Vector3 leftTangent = new Vector3(-inward.z, 0f, inward.x);
        float bodyRadius = Mathf.Max(MinimumBodyRadius, input.BodyRadius);
        float queryRadius = input.NeighborQueryRadius > 0f
            ? Mathf.Max(bodyRadius, input.NeighborQueryRadius)
            : ResolveNeighborQueryRadius(bodyRadius);

        CollectSideDensity(
            input.AgentPosition,
            inward,
            leftTangent,
            bodyRadius,
            queryRadius,
            neighbors,
            out float leftDensity,
            out float rightDensity);

        int turnSign = ResolveTurnSign(
            input.CurrentTurnSign,
            leftDensity,
            rightDensity,
            input.AllowTurnSwitch,
            input.StableId);
        Vector3 tangentDirection = leftTangent * turnSign; // +1 왼쪽, -1 오른쪽
        Vector3 radialCorrection = ResolveRadialCorrection(
            outward,
            inward,
            targetDistance,
            input.PreferredRadius,
            bodyRadius);
        float localBlend = ResolveLocalBlend(targetDistance);
        float congestion = Mathf.Clamp01((leftDensity + rightDensity) / DensityForFullCongestion);
        Vector3 separation = Vector3.ClampMagnitude(Flatten(input.SeparationDirection), 1f);
        float separationStrength = Mathf.Max(0f, input.SeparationWeight);

        float navigationWeight = Mathf.Lerp(1f, MinimumNavigationWeight, localBlend);
        float separationWeight = Mathf.Lerp(0.35f, 1f, localBlend);
        Vector3 combined = navigation * navigationWeight
            + radialCorrection * (RadialWeight * localBlend)
            + tangentDirection * (TangentWeight * congestion * localBlend)
            + separation * (separationWeight * separationStrength);
        Vector3 direction = NormalizeOrFallback(combined, navigation);

        return new EnemyApproachSteeringResult(
            direction,
            radialCorrection,
            tangentDirection,
            leftDensity,
            rightDensity,
            localBlend,
            congestion,
            turnSign);
    }

    public static int ResolveTurnSign(
        int currentTurnSign,
        float leftDensity,
        float rightDensity,
        bool allowTurnSwitch,
        int stableId)
    {
        int current = currentTurnSign > 0 ? 1 : currentTurnSign < 0 ? -1 : 0;
        float left = Mathf.Max(0f, leftDensity);
        float right = Mathf.Max(0f, rightDensity);
        if (current == 0)
        {
            if (left + MinimumDensityDifference < right)
                return 1;
            if (right + MinimumDensityDifference < left)
                return -1;
            return ResolveStableTurnSign(stableId);
        }

        if (!allowTurnSwitch)
            return current;

        float currentDensity = current > 0 ? left : right;
        float alternativeDensity = current > 0 ? right : left;
        if (alternativeDensity + MinimumDensityDifference
            < currentDensity * TurnSwitchDensityRatio)
        {
            return -current;
        }

        return current;
    }

    public static int ResolveStableTurnSign(int stableId)
    {
        uint hash = unchecked((uint)stableId);
        hash ^= hash >> 16;
        hash *= 0x7feb352d;
        hash ^= hash >> 15;
        return (hash & 1u) == 0u ? 1 : -1;
    }

    public static float ResolveNeighborQueryRadius(float bodyRadius)
    {
        return Mathf.Max(2f, Mathf.Max(MinimumBodyRadius, bodyRadius) * 4f);
    }

    public static Vector3 ResolveShortHorizonDestination(
        Vector3 origin,
        Vector3 direction,
        float moveSpeed)
    {
        Vector3 horizontalDirection = NormalizeOrZero(Flatten(direction));
        if (horizontalDirection.sqrMagnitude <= MinimumDirectionSqrMagnitude)
            return origin;

        float distance = Mathf.Clamp(
            Mathf.Abs(moveSpeed) * ShortHorizonSeconds,
            MinimumShortHorizonDistance,
            MaximumShortHorizonDistance);
        return origin + horizontalDirection * distance;
    }

    private static void CollectSideDensity(
        Vector3 agentPosition,
        Vector3 inward,
        Vector3 leftTangent,
        float bodyRadius,
        float queryRadius,
        IReadOnlyList<EnemyApproachNeighbor> neighbors,
        out float leftDensity,
        out float rightDensity)
    {
        leftDensity = 0f;
        rightDensity = 0f;
        if (neighbors == null)
            return;

        float queryRadiusSqr = queryRadius * queryRadius;
        for (int i = 0; i < neighbors.Count; i++)
        {
            EnemyApproachNeighbor neighbor = neighbors[i];
            Vector3 delta = Flatten(neighbor.Position - agentPosition);
            float distanceSqr = delta.sqrMagnitude;
            if (distanceSqr <= MinimumDirectionSqrMagnitude || distanceSqr > queryRadiusSqr)
                continue;

            float distance = Mathf.Sqrt(distanceSqr);
            Vector3 direction = delta / distance;
            float proximity = 1f - distance / queryRadius;
            float bodyWeight = Mathf.Clamp(neighbor.BodyRadius / bodyRadius, 0.5f, 2f);
            float forwardAmount = Vector3.Dot(direction, inward);
            float forwardWeight = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((forwardAmount + 0.25f) / 1.25f));
            float pressure = proximity * proximity * bodyWeight * forwardWeight;
            float sideOffset = Vector3.Dot(delta, leftTangent);
            float centerBand = Mathf.Max(0.05f, (bodyRadius + neighbor.BodyRadius) * 0.15f);
            if (Mathf.Abs(sideOffset) <= centerBand)
            {
                leftDensity += pressure * 0.5f;
                rightDensity += pressure * 0.5f;
            }
            else if (sideOffset > 0f)
            {
                leftDensity += pressure;
            }
            else
            {
                rightDensity += pressure;
            }
        }
    }

    private static Vector3 ResolveRadialCorrection(
        Vector3 outward,
        Vector3 inward,
        float targetDistance,
        float preferredRadius,
        float bodyRadius)
    {
        float radius = Mathf.Max(bodyRadius, preferredRadius);
        float error = targetDistance - radius;
        float deadZone = Mathf.Max(0.1f, bodyRadius * 0.25f);
        float excess = Mathf.Abs(error) - deadZone;
        if (excess <= 0f)
            return Vector3.zero;

        float strength = Mathf.Clamp01(excess / Mathf.Max(0.35f, bodyRadius));
        return error > 0f ? inward * strength : outward * strength;
    }

    private static float ResolveLocalBlend(float targetDistance)
    {
        float range = LocalSteeringStartDistance - FullLocalSteeringDistance;
        return Mathf.Clamp01((LocalSteeringStartDistance - targetDistance) / range);
    }

    private static Vector3 ResolveInwardDirection(
        Vector3 toTarget,
        Vector3 navigationDirection,
        int stableId)
    {
        Vector3 inward = NormalizeOrZero(toTarget);
        if (inward.sqrMagnitude > MinimumDirectionSqrMagnitude)
            return inward;

        inward = NormalizeOrZero(Flatten(navigationDirection));
        if (inward.sqrMagnitude > MinimumDirectionSqrMagnitude)
            return inward;

        float angle = (unchecked((uint)stableId) % 3600u) * 0.1f * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        Vector3 normalized = NormalizeOrZero(value);
        return normalized.sqrMagnitude > MinimumDirectionSqrMagnitude
            ? normalized
            : NormalizeOrZero(fallback);
    }

    private static Vector3 NormalizeOrZero(Vector3 value)
    {
        value.y = 0f;
        float sqrMagnitude = value.sqrMagnitude;
        return sqrMagnitude > MinimumDirectionSqrMagnitude
            ? value / Mathf.Sqrt(sqrMagnitude)
            : Vector3.zero;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }
}
