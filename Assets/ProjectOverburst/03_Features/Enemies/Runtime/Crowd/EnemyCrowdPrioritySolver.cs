using UnityEngine;

public struct EnemyCrowdPriorityBody // 게임·시뮬레이터 공용 군집 쌍 입력
{
    public int StableId;
    public Vector3 DesiredPosition;
    public Vector3 ResolvedPosition;
    public float BodyRadius;
    public float CrowdWeight;
    public int MovePriority;
    public bool CanMove;
    public bool IsForcedMotion;
    public Vector3 YieldCorrection;
    public float YieldPressure;
}

public struct EnemyCrowdPairCorrection // 한 쌍을 같은 스냅샷에서 계산한 양방향 보정
{
    public Vector3 CorrectionA;
    public Vector3 CorrectionB;
    public Vector3 YieldCorrectionA;
    public Vector3 YieldCorrectionB;
    public float Penetration;
}

public static class EnemyCrowdPrioritySolver // 처리 순서와 무관한 우선권 쌍 분배 계산
{
    public const float PriorityOwnerCorrectionShare = 0.1f;
    public const float YieldActivationDistance = 0.015f;
    public const float YieldDirectionHoldDuration = 0.3f;
    public const float YieldDirectionLockDuration = 0.15f;
    public const float YieldReversePressureMultiplier = 1.3f;
    public const float ReserveYieldSteeringWeight = 0.35f;

    public static bool TryCalculatePair(
        EnemyCrowdPriorityBody bodyA,
        EnemyCrowdPriorityBody bodyB,
        out EnemyCrowdPairCorrection correction)
    {
        correction = default;
        Vector3 delta = bodyA.ResolvedPosition - bodyB.ResolvedPosition;
        delta.y = 0f;
        float minimumDistance = Mathf.Max(0.05f, bodyA.BodyRadius)
            + Mathf.Max(0.05f, bodyB.BodyRadius);
        float distanceSqr = delta.sqrMagnitude;
        if (distanceSqr >= minimumDistance * minimumDistance)
            return false;

        float distance = Mathf.Sqrt(distanceSqr);
        float penetration = minimumDistance - distance;
        Vector3 direction = distance > 0.0001f
            ? delta / distance
            : ResolveStablePairDirection(bodyA.StableId, bodyB.StableId);
        ResolveCorrectionShares(bodyA, bodyB, out float shareA, out float shareB);

        correction.Penetration = penetration;
        correction.CorrectionA = direction * penetration * shareA;
        correction.CorrectionB = -direction * penetration * shareB;

        bool aYields = bodyA.CanMove
            && !bodyA.IsForcedMotion
            && (bodyA.MovePriority < bodyB.MovePriority
                || bodyB.IsForcedMotion && !bodyA.IsForcedMotion);
        bool bYields = bodyB.CanMove
            && !bodyB.IsForcedMotion
            && (bodyB.MovePriority < bodyA.MovePriority
                || bodyA.IsForcedMotion && !bodyB.IsForcedMotion);
        if (aYields)
            correction.YieldCorrectionA = correction.CorrectionA;
        if (bYields)
            correction.YieldCorrectionB = correction.CorrectionB;
        return correction.CorrectionA.sqrMagnitude > 0.0000001f
            || correction.CorrectionB.sqrMagnitude > 0.0000001f;
    }

    public static Vector3 ApplyAccumulatedCorrection(
        ref EnemyCrowdPriorityBody body,
        Vector3 accumulatedCorrection,
        float maximumCorrection)
    {
        if (!body.CanMove || accumulatedCorrection.sqrMagnitude <= 0.0000001f)
            return Vector3.zero;

        accumulatedCorrection.y = 0f;
        Vector3 before = body.ResolvedPosition;
        Vector3 currentCorrection = before - body.DesiredPosition;
        currentCorrection.y = 0f;
        Vector3 clamped = Vector3.ClampMagnitude(
            currentCorrection + accumulatedCorrection,
            Mathf.Max(0f, maximumCorrection));
        body.ResolvedPosition = body.DesiredPosition + clamped;
        body.ResolvedPosition.y = body.DesiredPosition.y;
        return body.ResolvedPosition - before;
    }

    public static void ResolveCorrectionShares(
        EnemyCrowdPriorityBody bodyA,
        EnemyCrowdPriorityBody bodyB,
        out float shareA,
        out float shareB)
    {
        if (!bodyA.CanMove && !bodyB.CanMove)
        {
            shareA = 0f;
            shareB = 0f;
            return;
        }
        if (!bodyA.CanMove)
        {
            shareA = 0f;
            shareB = 1f;
            return;
        }
        if (!bodyB.CanMove)
        {
            shareA = 1f;
            shareB = 0f;
            return;
        }
        if (bodyA.IsForcedMotion && !bodyB.IsForcedMotion)
        {
            shareA = 0f;
            shareB = 1f;
            return;
        }
        if (bodyB.IsForcedMotion && !bodyA.IsForcedMotion)
        {
            shareA = 1f;
            shareB = 0f;
            return;
        }

        shareA = ResolvePriorityCorrectionShare(
            bodyA.CrowdWeight,
            bodyB.CrowdWeight,
            bodyA.MovePriority,
            bodyB.MovePriority);
        shareB = 1f - shareA;
    }

    public static float ResolvePriorityCorrectionShare(
        float ownWeight,
        float otherWeight,
        int ownPriority,
        int otherPriority)
    {
        if (ownPriority > otherPriority)
            return PriorityOwnerCorrectionShare;
        if (ownPriority < otherPriority)
            return 1f - PriorityOwnerCorrectionShare;

        float own = Mathf.Max(0.1f, ownWeight);
        float other = Mathf.Max(0.1f, otherWeight);
        return other / (own + other);
    }

    public static Vector3 ResolveStablePairDirection(int ownId, int otherId)
    {
        int lowerId = Mathf.Min(ownId, otherId);
        int upperId = Mathf.Max(ownId, otherId);
        uint hash = unchecked((uint)(lowerId * 397 ^ upperId));
        hash ^= hash >> 16;
        hash *= 0x7feb352d;
        hash ^= hash >> 15;
        hash *= 0x846ca68b;
        hash ^= hash >> 16;
        float radians = (hash % 3600u) * 0.1f * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
        return ownId <= otherId ? direction : -direction;
    }
}
