using System.Collections.Generic;
using UnityEngine;

public enum MeleeAttackTrajectoryBakeStatus
{
    Missing,
    Current,
    Stale,
    Failed
}

[System.Serializable]
public struct MeleeAttackTrajectoryRawSample
{
    [Range(0f, 1f)] public float normalizedTime;
    public Vector3 localPosition;
    public float unwrappedAngle;
    public float forwardDistance;
}

[System.Serializable]
public struct MeleeAttackTrajectoryProgressSample
{
    [Range(0f, 1f)] public float normalizedTime;
    [Range(0f, 1f)] public float rawProgress;
    [Range(0f, 1f)] public float resolvedProgress;
}

[System.Serializable]
public sealed class MeleeAttackPhaseTrajectoryBakeData
{
    public int phaseIndex;
    public AttackProgressSource progressSource;
    public bool canApplyHits;
    [Range(0f, 1f)] public float hitEntryNormalizedTime;
    public MeleeAttackTrajectoryProgressSample[] progressSamples;

    public bool IsUsableFor(AttackProgressSource source)
    {
        return phaseIndex >= 0
            && progressSource == source
            && progressSamples != null
            && progressSamples.Length >= 2;
    }

    public bool TryEvaluate(
        float attackNormalizedTime,
        out float rawProgress,
        out float resolvedProgress)
    {
        rawProgress = 0f;
        resolvedProgress = 0f;
        if (progressSamples == null || progressSamples.Length == 0)
            return false;

        float time = Mathf.Clamp01(attackNormalizedTime);
        if (!canApplyHits || time + 0.000001f < hitEntryNormalizedTime)
            return false;

        if (time <= progressSamples[0].normalizedTime)
        {
            rawProgress = progressSamples[0].rawProgress;
            resolvedProgress = progressSamples[0].resolvedProgress;
            return true;
        }

        int lastIndex = progressSamples.Length - 1;
        if (time >= progressSamples[lastIndex].normalizedTime)
        {
            rawProgress = progressSamples[lastIndex].rawProgress;
            resolvedProgress = progressSamples[lastIndex].resolvedProgress;
            return true;
        }

        for (int i = 1; i < progressSamples.Length; i++)
        {
            MeleeAttackTrajectoryProgressSample right = progressSamples[i];
            if (time > right.normalizedTime)
                continue;

            MeleeAttackTrajectoryProgressSample left = progressSamples[i - 1];
            float interval = Mathf.Max(0.000001f, right.normalizedTime - left.normalizedTime);
            float t = Mathf.Clamp01((time - left.normalizedTime) / interval);
            rawProgress = Mathf.Lerp(left.rawProgress, right.rawProgress, t);
            resolvedProgress = Mathf.Lerp(left.resolvedProgress, right.resolvedProgress, t);
            return true;
        }

        return false;
    }
}

[System.Serializable]
public sealed class MeleeAttackStepTrajectoryBakeData
{
    public string attackId;
    public MeleeAttackTrajectoryRawSample[] rawSamples;
    public MeleeAttackPhaseTrajectoryBakeData[] phases;

    public bool TryGetPhase(
        int phaseIndex,
        AttackProgressSource source,
        out MeleeAttackPhaseTrajectoryBakeData phase)
    {
        phase = null;
        if (phases == null || phaseIndex < 0 || phaseIndex >= phases.Length)
            return false;

        MeleeAttackPhaseTrajectoryBakeData candidate = phases[phaseIndex];
        if (candidate == null || !candidate.IsUsableFor(source))
            return false;

        phase = candidate;
        return true;
    }
}

[System.Serializable]
public sealed class MeleeAttackTrajectoryBakeData
{
    public const int CurrentSchemaVersion = 1;

    public int schemaVersion;
    public string sourceFingerprint;
    public string derivedFingerprint;
    public string bakedUtc;
    public MeleeAttackStepTrajectoryBakeData[] steps;

    public bool IsStructurallyValid => schemaVersion == CurrentSchemaVersion
        && !string.IsNullOrEmpty(sourceFingerprint)
        && !string.IsNullOrEmpty(derivedFingerprint)
        && steps != null
        && steps.Length > 0;
}

[CreateAssetMenu(
    fileName = "MeleeComboDefinition",
    menuName = "OVERBURST/Weapons/Melee Combo Definition")]
public sealed class MeleeComboDefinition : ScriptableObject
{
    [Header("콤보 흐름")]
    [InspectorName("콤보 초기화 대기시간")]
    [Min(0.01f)] public float resetDelay = 0.5f;
    [InspectorName("기본 애니메이션 속도")]
    [Min(0.01f)] public float baseAnimationSpeed = 1f;
    [InspectorName("첫 타 진입 블렌딩 시간")]
    [Min(0f)] public float entryTransitionDuration;

    [Header("콤보 타수")]
    [InspectorName("콤보 타수 목록")]
    public MeleeComboStepData[] steps;

    [SerializeField, HideInInspector]
    private MeleeAttackTrajectoryBakeData attackTrajectoryBakeData;

    public bool HasSteps => steps != null && steps.Length > 0;
    public int StepCount => steps != null ? steps.Length : 0;
    public MeleeAttackTrajectoryBakeData AttackTrajectoryBakeData => attackTrajectoryBakeData;

    public MeleeComboStepData GetStep(int index)
    {
        return steps[index];
    }

    public bool TryGetAttackTrajectoryStep(
        int stepIndex,
        out MeleeAttackStepTrajectoryBakeData trajectoryStep,
        out string error)
    {
        trajectoryStep = null;
        if (attackTrajectoryBakeData == null || !attackTrajectoryBakeData.IsStructurallyValid)
        {
            error = "공격 궤적 베이크 데이터가 없거나 구버전입니다.";
            return false;
        }

        if (stepIndex < 0
            || stepIndex >= StepCount
            || stepIndex >= attackTrajectoryBakeData.steps.Length)
        {
            error = "공격 궤적 타수 인덱스가 올바르지 않습니다: " + stepIndex;
            return false;
        }

        MeleeAttackStepTrajectoryBakeData candidate = attackTrajectoryBakeData.steps[stepIndex];
        if (candidate == null
            || !string.Equals(candidate.attackId, steps[stepIndex].attackId, System.StringComparison.Ordinal)
            || candidate.rawSamples == null
            || candidate.rawSamples.Length < 2)
        {
            error = "현재 타수와 공격 궤적 베이크 데이터가 일치하지 않습니다: " + steps[stepIndex].attackId;
            return false;
        }

        trajectoryStep = candidate;
        error = null;
        return true;
    }

#if UNITY_EDITOR
    public void EditorAssignAttackTrajectoryBakeData(MeleeAttackTrajectoryBakeData value)
    {
        attackTrajectoryBakeData = value; // 원자 베이크 커밋 전용
    }
#endif

    public bool TryGetStableAttackIds(out string[] attackIds, out string error)
    {
        attackIds = null;

        if (!HasSteps)
        {
            error = "Combo steps are missing.";
            return false;
        }

        string[] resolvedIds = new string[steps.Length];
        HashSet<string> uniqueIds = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < steps.Length; i++)
        {
            string attackId = steps[i].attackId;
            if (string.IsNullOrWhiteSpace(attackId))
            {
                error = "Attack ID is missing at combo step " + i + ".";
                return false;
            }

            if (!string.Equals(attackId, attackId.Trim(), System.StringComparison.Ordinal))
            {
                error = "Attack ID has leading or trailing whitespace at combo step " + i + ": " + attackId;
                return false;
            }

            if (!uniqueIds.Add(attackId))
            {
                error = "Duplicate attack ID: " + attackId;
                return false;
            }

            resolvedIds[i] = attackId; // 배열 순서와 별개인 저장 신원
        }

        attackIds = resolvedIds;
        error = null;
        return true;
    }
}
