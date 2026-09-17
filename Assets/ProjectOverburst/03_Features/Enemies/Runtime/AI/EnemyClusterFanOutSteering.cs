using UnityEngine;

public readonly struct EnemyClusterFanOutInput // 군집 부채꼴 전개 순수 입력
{
    public EnemyClusterFanOutInput(
        EnemyClusterFanOutMemberData member,
        Vector3 navigationDirection,
        Vector3 separationDirection,
        int currentSideSign,
        int stableId)
    {
        Member = member;
        NavigationDirection = navigationDirection;
        SeparationDirection = separationDirection;
        CurrentSideSign = currentSideSign;
        StableId = stableId;
    }

    public EnemyClusterFanOutMemberData Member { get; }
    public Vector3 NavigationDirection { get; }
    public Vector3 SeparationDirection { get; }
    public int CurrentSideSign { get; }
    public int StableId { get; }
}

public readonly struct EnemyClusterFanOutResult // 앞열 유지와 중·뒷열 전개 결과
{
    public EnemyClusterFanOutResult(
        bool isActive,
        Vector3 direction,
        Vector3 outwardDirection,
        float fanStrength,
        float speedMultiplier,
        int sideSign)
    {
        IsActive = isActive;
        Direction = direction;
        OutwardDirection = outwardDirection;
        FanStrength = fanStrength;
        SpeedMultiplier = speedMultiplier;
        SideSign = sideSign;
    }

    public bool IsActive { get; }
    public Vector3 Direction { get; }
    public Vector3 OutwardDirection { get; }
    public float FanStrength { get; }
    public float SpeedMultiplier { get; }
    public int SideSign { get; }
}

public static class EnemyClusterFanOutSteering // 군집 중심 기준 앞열 유지와 뒷열 양측 전개
{
    public const int MinimumClusterSize = 8;
    public const float MinimumRearRatio = 0.25f;
    public const float MinimumExpansionStrength = 0.08f;
    public const float MinimumSpeedMultiplier = 1.1f;
    public const float MaximumSpeedMultiplier = 1.2f;

    private const float MinimumDirectionSqrMagnitude = 0.0001f;
    private const float MinimumNavigationAlignment = 0.25f;

    public static EnemyClusterFanOutResult Resolve(EnemyClusterFanOutInput input)
    {
        EnemyClusterFanOutMemberData member = input.Member;
        Vector3 forward = NormalizeOrZero(member.Forward);
        Vector3 navigation = NormalizeOrFallback(input.NavigationDirection, forward);
        int sideSign = input.CurrentSideSign > 0
            ? 1
            : input.CurrentSideSign < 0
                ? -1
                : member.SuggestedSideSign != 0
                    ? member.SuggestedSideSign
                    : EnemyApproachSteering.ResolveStableTurnSign(input.StableId);
        Vector3 outward = NormalizeOrZero(member.Right) * sideSign;
        float rearStrength = Mathf.Clamp01(
            (member.RearRatio - MinimumRearRatio)
            / Mathf.Max(0.01f, 1f - MinimumRearRatio));
        rearStrength = rearStrength * rearStrength * (3f - 2f * rearStrength); // 부드러운 열 전환
        float fanStrength = rearStrength * Mathf.Clamp01(member.ExpansionStrength);
        bool isActive = member.ClusterSize >= MinimumClusterSize
            && member.RearRatio > MinimumRearRatio
            && member.ExpansionStrength >= MinimumExpansionStrength
            && Vector3.Dot(navigation, forward) >= MinimumNavigationAlignment
            && outward.sqrMagnitude > MinimumDirectionSqrMagnitude;
        if (!isActive)
        {
            return new EnemyClusterFanOutResult(
                false,
                navigation,
                outward,
                0f,
                1f,
                sideSign);
        }

        Vector3 separation = Vector3.ClampMagnitude(Flatten(input.SeparationDirection), 1f);
        float forwardWeight = Mathf.Lerp(0.55f, 0.3f, fanStrength);
        float outwardWeight = Mathf.Lerp(0.45f, 1.05f, fanStrength);
        Vector3 combined = navigation * 0.25f
            + forward * forwardWeight
            + outward * outwardWeight
            + separation * 0.25f;
        Vector3 direction = NormalizeOrFallback(combined, outward + forward * 0.3f);
        float speedMultiplier = Mathf.Lerp(
            MinimumSpeedMultiplier,
            MaximumSpeedMultiplier,
            fanStrength);

        return new EnemyClusterFanOutResult(
            true,
            direction,
            outward,
            fanStrength,
            speedMultiplier,
            sideSign);
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
