using System.Collections.Generic;
using UnityEngine;

public readonly struct EnemyChaseBypassInput // Chase 전방 정체 우회 입력
{
    public EnemyChaseBypassInput(
        Vector3 agentPosition,
        Vector3 targetPosition,
        Vector3 navigationDirection,
        Vector3 separationDirection,
        float bodyRadius,
        int stableId,
        int currentTurnSign = 0,
        bool allowTurnSwitch = true)
    {
        AgentPosition = agentPosition;
        TargetPosition = targetPosition;
        NavigationDirection = navigationDirection;
        SeparationDirection = separationDirection;
        BodyRadius = bodyRadius;
        StableId = stableId;
        CurrentTurnSign = currentTurnSign;
        AllowTurnSwitch = allowTurnSwitch;
    }

    public Vector3 AgentPosition { get; }
    public Vector3 TargetPosition { get; }
    public Vector3 NavigationDirection { get; }
    public Vector3 SeparationDirection { get; }
    public float BodyRadius { get; }
    public int StableId { get; }
    public int CurrentTurnSign { get; }
    public bool AllowTurnSwitch { get; }
}

public readonly struct EnemyChaseBypassResult // 정체 감지와 나선형 우회 결과
{
    public EnemyChaseBypassResult(
        bool isActive,
        Vector3 direction,
        Vector3 tangentDirection,
        int forwardBlockerCount,
        float frontPressure,
        float leftDensity,
        float rightDensity,
        float speedMultiplier,
        int turnSign)
    {
        IsActive = isActive;
        Direction = direction;
        TangentDirection = tangentDirection;
        ForwardBlockerCount = forwardBlockerCount;
        FrontPressure = frontPressure;
        LeftDensity = leftDensity;
        RightDensity = rightDensity;
        SpeedMultiplier = speedMultiplier;
        TurnSign = turnSign;
    }

    public bool IsActive { get; }
    public Vector3 Direction { get; }
    public Vector3 TangentDirection { get; }
    public int ForwardBlockerCount { get; }
    public float FrontPressure { get; }
    public float LeftDensity { get; }
    public float RightDensity { get; }
    public float SpeedMultiplier { get; }
    public int TurnSign { get; }
}

public static class EnemyChaseBypassSteering // 뒤쪽 몬스터의 예약 없는 나선형 추월 조향
{
    public const float MaximumActivationDistance = 10f;
    public const float NeighborQueryRadius = 4f;
    public const int MinimumForwardBlockerCount = 2;
    public const float TurnDecisionInterval = 0.25f;
    public const float TurnLockDuration = 0.9f;
    public const float MinimumSpeedMultiplier = 1.15f;
    public const float MaximumSpeedMultiplier = 1.25f;

    private const float MinimumBodyRadius = 0.05f;
    private const float MinimumDirectionSqrMagnitude = 0.0001f;
    private const float MinimumNavigationAlignment = 0.35f;
    private const float MinimumForwardDot = 0.55f;
    private const float CorridorPadding = 0.3f;
    private const float FrontPressureForFullBypass = 1.25f;

    public static EnemyChaseBypassResult Resolve(
        EnemyChaseBypassInput input,
        IReadOnlyList<EnemyApproachNeighbor> neighbors)
    {
        Vector3 toTarget = Flatten(input.TargetPosition - input.AgentPosition);
        float targetDistance = toTarget.magnitude;
        Vector3 inward = NormalizeOrFallback(toTarget, input.NavigationDirection);
        Vector3 navigation = NormalizeOrFallback(input.NavigationDirection, inward);
        Vector3 leftTangent = new Vector3(-inward.z, 0f, inward.x);
        float bodyRadius = Mathf.Max(MinimumBodyRadius, input.BodyRadius);

        CollectPressure(
            input.AgentPosition,
            input.TargetPosition,
            inward,
            leftTangent,
            bodyRadius,
            neighbors,
            out int forwardBlockerCount,
            out float frontPressure,
            out float leftDensity,
            out float rightDensity);

        int turnSign = EnemyApproachSteering.ResolveTurnSign(
            input.CurrentTurnSign,
            leftDensity,
            rightDensity,
            input.AllowTurnSwitch,
            input.StableId);
        Vector3 tangentDirection = leftTangent * turnSign;
        bool canBypass = targetDistance <= MaximumActivationDistance
            && forwardBlockerCount >= MinimumForwardBlockerCount
            && Vector3.Dot(navigation, inward) >= MinimumNavigationAlignment;
        if (!canBypass)
        {
            return new EnemyChaseBypassResult(
                false,
                navigation,
                tangentDirection,
                forwardBlockerCount,
                frontPressure,
                leftDensity,
                rightDensity,
                1f,
                turnSign);
        }

        float blockerCongestion = Mathf.Clamp01(
            (forwardBlockerCount - MinimumForwardBlockerCount + 1f) / 3f);
        float pressureCongestion = Mathf.Clamp01(frontPressure / FrontPressureForFullBypass);
        float congestion = Mathf.Max(blockerCongestion, pressureCongestion);
        Vector3 separation = Vector3.ClampMagnitude(Flatten(input.SeparationDirection), 1f);
        float inwardWeight = Mathf.Lerp(0.35f, 0.2f, congestion);
        float tangentWeight = Mathf.Lerp(0.8f, 1.1f, congestion);
        Vector3 combined = navigation * 0.2f
            + inward * inwardWeight
            + tangentDirection * tangentWeight
            + separation * 0.25f;
        Vector3 direction = NormalizeOrFallback(combined, tangentDirection + inward * 0.25f);
        float speedMultiplier = Mathf.Lerp(
            MinimumSpeedMultiplier,
            MaximumSpeedMultiplier,
            congestion);

        return new EnemyChaseBypassResult(
            true,
            direction,
            tangentDirection,
            forwardBlockerCount,
            frontPressure,
            leftDensity,
            rightDensity,
            speedMultiplier,
            turnSign);
    }

    private static void CollectPressure(
        Vector3 agentPosition,
        Vector3 targetPosition,
        Vector3 inward,
        Vector3 leftTangent,
        float bodyRadius,
        IReadOnlyList<EnemyApproachNeighbor> neighbors,
        out int forwardBlockerCount,
        out float frontPressure,
        out float leftDensity,
        out float rightDensity)
    {
        forwardBlockerCount = 0;
        frontPressure = 0f;
        leftDensity = 0f;
        rightDensity = 0f;
        if (neighbors == null)
            return;

        float ownTargetDistance = Flatten(targetPosition - agentPosition).magnitude;
        float queryRadiusSqr = NeighborQueryRadius * NeighborQueryRadius;
        for (int i = 0; i < neighbors.Count; i++)
        {
            EnemyApproachNeighbor neighbor = neighbors[i];
            Vector3 delta = Flatten(neighbor.Position - agentPosition);
            float distanceSqr = delta.sqrMagnitude;
            if (distanceSqr <= MinimumDirectionSqrMagnitude || distanceSqr > queryRadiusSqr)
                continue;

            float neighborTargetDistance = Flatten(targetPosition - neighbor.Position).magnitude;
            if (neighborTargetDistance + bodyRadius * 0.1f >= ownTargetDistance)
                continue; // 타겟보다 뒤에 있는 개체는 정체 원인에서 제외

            float distance = Mathf.Sqrt(distanceSqr);
            Vector3 direction = delta / distance;
            float proximity = 1f - distance / NeighborQueryRadius;
            float bodyWeight = Mathf.Clamp(neighbor.BodyRadius / bodyRadius, 0.5f, 2f);
            float pressure = proximity * proximity * bodyWeight;
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

            float forwardDot = Vector3.Dot(direction, inward);
            float corridorHalfWidth = bodyRadius + neighbor.BodyRadius + CorridorPadding;
            if (forwardDot < MinimumForwardDot || Mathf.Abs(sideOffset) > corridorHalfWidth)
                continue;

            forwardBlockerCount++;
            frontPressure += pressure * Mathf.Lerp(0.5f, 1f, forwardDot);
        }
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        Vector3 normalized = NormalizeOrZero(Flatten(value));
        return normalized.sqrMagnitude > MinimumDirectionSqrMagnitude
            ? normalized
            : NormalizeOrZero(Flatten(fallback));
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
