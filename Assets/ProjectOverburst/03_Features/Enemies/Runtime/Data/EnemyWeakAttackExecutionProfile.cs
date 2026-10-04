using System;
using UnityEngine;

public enum EnemyWeakAttackMotionPolicy
{
    Stationary,
    ShortAdvance,
    VisualJump,
    FlightMelee,
    Ranged,
    Channel
}

// Optional V3 data. Abilities without this profile retain their current execution.
// Reach is measured in the final actor/target coordinate basis. It does not enlarge hit geometry.
[CreateAssetMenu(menuName = "OVERBURST/Enemies/Weak Attack Execution Profile", fileName = "EWEP_Attack")]
public sealed class EnemyWeakAttackExecutionProfile : ScriptableObject
{
    [SerializeField] private string selectionKey;
    [SerializeField] private AnimationClip originalClip;
    [SerializeField] private AnimationClip runtimeClip;
    [SerializeField] private Vector2 sourceTrimSeconds;
    [SerializeField] private EnemyWeakAttackMotionPolicy motionPolicy;
    [SerializeField, Min(.01f)] private float stationaryStartRange;
    [SerializeField, Min(0f)] private float maxAdvanceDistance;
    [SerializeField] private Vector2 advanceWindow;
    [SerializeField] private AnimationCurve advanceProgress;
    [SerializeField] private string poseRootBonePath;
    [SerializeField] private Vector2[] contactWindows;
    [SerializeField] private EnemyWeakAttackContactGeometry[] contactGeometry;

    public int ContactGeometryCount => contactGeometry != null ? contactGeometry.Length : 0;
    public bool HasContactGeometry => ContactGeometryCount != 0;
    public EnemyWeakAttackContactGeometry GetContactGeometry(int phase)
        => (uint)phase < (uint)ContactGeometryCount ? contactGeometry[phase] : null;
    public EnemyWeakAttackContactGeometry[] CopyContactGeometry()
        => contactGeometry != null ? (EnemyWeakAttackContactGeometry[])contactGeometry.Clone() : null;
    public float MaximumContactPlanarReach
    {
        get
        {
            float reach = 0f;
            for (int i = 0; i < ContactGeometryCount; i++)
                if (contactGeometry[i] != null) reach = Mathf.Max(reach,contactGeometry[i].MaximumPlanarReach);
            return reach;
        }
    }
    private bool ContactGeometryValid()
    {
        if (!HasContactGeometry) return true;
        if (ContactGeometryCount != ContactWindowCount || ContactGeometryCount > 3) return false;
        for (int i = 0; i < ContactGeometryCount; i++)
            if (contactGeometry[i] == null || !contactGeometry[i].HasData) return false;
        return true;
    }

    public int ContactWindowCount => contactWindows != null ? contactWindows.Length : 0;
    public bool HasContactWindows => ContactWindowCount != 0;
    public bool TryGetContactWindow(int phase, out Vector2 window)
    {
        window = Vector2.zero;
        if ((uint)phase >= (uint)ContactWindowCount) return false;
        window = contactWindows[phase]; return true;
    }

    public bool MatchesContactWindows(int hitCount, float first, float second, float third)
    {
        if (!HasContactWindows) return true;
        if (hitCount != ContactWindowCount || !ContactWindowsValid()) return false;
        for (int i = 0; i < hitCount; i++)
        {
            float time = i == 0 ? first : i == 1 ? second : third;
            if (!Finite(time) || time < contactWindows[i].x || time > contactWindows[i].y) return false;
        }
        return true;
    }

    private bool ContactWindowsValid()
    {
        if (!HasContactWindows) return true;
        if (ContactWindowCount > 3) return false;
        for (int i = 0; i < ContactWindowCount; i++)
            if (!ValidWindow(contactWindows[i]) || i > 0 && (contactWindows[i].x < contactWindows[i-1].x
                || contactWindows[i].y < contactWindows[i-1].y)) return false;
        return true;
    }

    public string SelectionKey => selectionKey;
    public AnimationClip OriginalClip => originalClip;
    public AnimationClip RuntimeClip => runtimeClip;
    public float OriginalFps => originalClip != null ? originalClip.frameRate : 0f;
    public Vector2 SourceTrimSeconds => sourceTrimSeconds;
    public EnemyWeakAttackMotionPolicy MotionPolicy => motionPolicy;
    public float StationaryStartRange => stationaryStartRange;
    public float MaxAdvanceDistance => maxAdvanceDistance;
    public Vector2 AdvanceWindow => advanceWindow;
    public string PoseRootBonePath => poseRootBonePath;
    public bool UsesAdvance => motionPolicy == EnemyWeakAttackMotionPolicy.ShortAdvance;
    public bool IsStationaryMotion => motionPolicy == EnemyWeakAttackMotionPolicy.Stationary
        || motionPolicy == EnemyWeakAttackMotionPolicy.VisualJump || motionPolicy == EnemyWeakAttackMotionPolicy.FlightMelee;
    public float ApproachStartRange => stationaryStartRange + (UsesAdvance ? maxAdvanceDistance : 0f);

    public bool IsValid => !string.IsNullOrWhiteSpace(selectionKey)
        && originalClip != null && runtimeClip != null
        && FinitePositive(stationaryStartRange) && FiniteNonNegative(maxAdvanceDistance)
        && (uint)motionPolicy <= (uint)EnemyWeakAttackMotionPolicy.Channel
        && (!UsesAdvance || maxAdvanceDistance > 0f && ValidWindow(advanceWindow) && advanceProgress != null)
        && (UsesAdvance || maxAdvanceDistance == 0f) && ContactWindowsValid() && ContactGeometryValid();

    public float ResolveAdvanceBudget(float startingDistance)
    {
        if (!UsesAdvance || !FiniteNonNegative(startingDistance)) return 0f;
        return Mathf.Clamp(startingDistance - stationaryStartRange, 0f, maxAdvanceDistance);
    }

    public float EvaluateAdvanceFraction(float normalizedTime)
    {
        if (!UsesAdvance || !ValidWindow(advanceWindow) || advanceProgress == null || !FiniteNonNegative(normalizedTime)) return 0f;
        if (normalizedTime <= advanceWindow.x) return 0f;
        if (normalizedTime >= advanceWindow.y) return 1f;
        return Mathf.Clamp01(advanceProgress.Evaluate(Mathf.InverseLerp(advanceWindow.x, advanceWindow.y, normalizedTime)));
    }

    public bool ValidateAuthoring(out string reason)
    {
        if (!IsValid) { reason = "약공 원본·선택 키·거리·이동 구간이 유효하지 않습니다."; return false; }
        if (!FinitePositive(originalClip.length) || !FinitePositive(runtimeClip.length) || !FinitePositive(OriginalFps)
            || !FiniteNonNegative(sourceTrimSeconds.x) || !FinitePositive(sourceTrimSeconds.y)
            || sourceTrimSeconds.y <= sourceTrimSeconds.x || sourceTrimSeconds.y > originalClip.length + .001f)
        { reason = "원본 FPS·잘라 쓰는 구간이 유효하지 않습니다."; return false; }
        float duration = sourceTrimSeconds.y - sourceTrimSeconds.x;
        if (Mathf.Abs(runtimeClip.length - duration) > Mathf.Max(.001f, 1f / OriginalFps + .001f))
        { reason = "실제 클립 길이와 원본 사용 구간이 맞지 않습니다."; return false; }
        for (int i = 0; i < ContactGeometryCount; i++)
            if (!contactGeometry[i].Validate(contactWindows[i],out reason)) return false;
        if (UsesAdvance)
        {
            var keys = advanceProgress.keys;
            if (keys.Length < 2 || Mathf.Abs(keys[0].time) > .0001f || Mathf.Abs(keys[0].value) > .0001f
                || Mathf.Abs(keys[keys.Length-1].time-1f) > .0001f || Mathf.Abs(keys[keys.Length-1].value-1f) > .0001f)
            { reason = "전진 곡선은 0에서 시작해 1로 끝나야 합니다."; return false; }
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (!FiniteNonNegative(key.time) || !FiniteNonNegative(key.value) || key.time > 1f || key.value > 1f
                    || !Finite(key.inTangent) || !Finite(key.outTangent) || key.weightedMode != WeightedMode.None)
                { reason = "전진 곡선은 유한한 비가중 접선과 0~1의 키가 필요합니다."; return false; }
                if (i == 0) continue;
                var previous = keys[i-1];
                float width = key.time - previous.time;
                if (width <= 0f || key.value < previous.value)
                { reason = "전진 곡선의 키는 감소 없이 진행해야 합니다."; return false; }
                // An unweighted segment is a cubic Hermite curve. Check the exact
                // derivative minimum, including between keys, rather than a sample grid.
                float a = 2f*previous.value-2f*key.value+(previous.outTangent+key.inTangent)*width;
                float b = -3f*previous.value+3f*key.value-(2f*previous.outTangent+key.inTangent)*width;
                float c = previous.outTangent*width;
                float minimum = Mathf.Min(c,3f*a+2f*b+c);
                if (a > 0f)
                {
                    float turning = -b/(3f*a);
                    if (turning > 0f && turning < 1f) minimum = Mathf.Min(minimum,3f*a*turning*turning+2f*b*turning+c);
                }
                if (!Finite(a) || !Finite(b) || !Finite(c) || !Finite(minimum) || minimum < -.0001f)
                { reason = "전진 곡선은 키 사이에서도 감소 없이 진행해야 합니다."; return false; }
            }
        }
        reason = string.Empty; return true;
    }

    public void Configure(string key, AnimationClip source, AnimationClip motion, Vector2 trimSeconds,
        EnemyWeakAttackMotionPolicy policy, float stationaryReach, float advanceMeters,
        Vector2 motionWindow, AnimationCurve progress, string rootBonePath, Vector2[] authoredContactWindows = null,
        EnemyWeakAttackContactFrame[][] authoredContactGeometry = null)
    {
        // Validate on a temporary profile before replacing authored data on an existing asset.
        var candidate = CreateInstance<EnemyWeakAttackExecutionProfile>();
        try
        {
            candidate.selectionKey = key;
            candidate.originalClip = source; candidate.runtimeClip = motion;
            candidate.sourceTrimSeconds = trimSeconds; candidate.motionPolicy = policy;
            candidate.stationaryStartRange = stationaryReach; candidate.maxAdvanceDistance = advanceMeters;
            candidate.advanceWindow = motionWindow;
            candidate.advanceProgress = progress != null ? new AnimationCurve(progress.keys) : null;
            candidate.poseRootBonePath = rootBonePath ?? string.Empty;
            candidate.contactWindows = authoredContactWindows != null ? (Vector2[])authoredContactWindows.Clone() : null;
            if (authoredContactGeometry != null)
            {
                if (authoredContactGeometry.Length != candidate.ContactWindowCount || authoredContactGeometry.Length == 0)
                    throw new ArgumentException("타격별 판정과 접촉 구간 수가 다릅니다.");
                candidate.contactGeometry = new EnemyWeakAttackContactGeometry[authoredContactGeometry.Length];
                for (int i = 0; i < authoredContactGeometry.Length; i++)
                    candidate.contactGeometry[i] = EnemyWeakAttackContactGeometry.Create(authoredContactGeometry[i],candidate.contactWindows[i]);
            }
            if (!candidate.ValidateAuthoring(out string reason)) throw new ArgumentException(reason);
            selectionKey = candidate.selectionKey; originalClip = source; runtimeClip = motion;
            sourceTrimSeconds = trimSeconds; motionPolicy = policy; stationaryStartRange = stationaryReach;
            maxAdvanceDistance = advanceMeters; advanceWindow = motionWindow;
            advanceProgress = candidate.advanceProgress; poseRootBonePath = candidate.poseRootBonePath;
            contactWindows = candidate.contactWindows; contactGeometry = candidate.contactGeometry;
        }
        finally
        {
            if (Application.isPlaying) Destroy(candidate); else DestroyImmediate(candidate);
        }
    }

    private static bool FinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    private static bool ValidWindow(Vector2 window) => FiniteNonNegative(window.x) && FinitePositive(window.y)
        && window.x < window.y && window.y <= 1f;
}
