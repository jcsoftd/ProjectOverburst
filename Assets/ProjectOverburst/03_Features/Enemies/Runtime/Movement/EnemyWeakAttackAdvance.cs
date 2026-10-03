using System;
using UnityEngine;

// One attack owns one immutable planar budget. Failed/clipped requests consume
// their authored portion too, so a wall cannot accumulate a later burst.
public sealed class EnemyWeakAttackAdvance
{
    private EnemyWeakAttackExecutionProfile profile;
    private Vector3 direction;
    private float consumedDistance;
    public float Budget { get; private set; }
    public float ConsumedDistance => consumedDistance;
    public Vector3 Direction => direction;
    public bool IsActive => profile != null;

    public void Begin(EnemyWeakAttackExecutionProfile execution, float startingDistance, Vector3 startingForward)
    {
        if (execution == null || !execution.ValidateAuthoring(out _)
            || !Finite(startingDistance) || startingDistance < 0f
            || !Finite(startingForward.x) || !Finite(startingForward.y) || !Finite(startingForward.z))
            throw new ArgumentException("약공 이동 시작 값이 유효하지 않습니다.");
        startingForward.y = 0f;
        float budget = execution.ResolveAdvanceBudget(startingDistance);
        if (budget > 0f && startingForward.sqrMagnitude < .000001f)
            throw new ArgumentException("전진 공격의 시작 방향이 없습니다.");
        profile = execution;
        direction = startingForward.sqrMagnitude > .000001f ? startingForward.normalized : Vector3.forward;
        Budget = budget; consumedDistance = 0f;
    }

    public Vector3 Consume(float normalizedTime, float maximumStep)
    {
        if (!IsActive || !Finite(normalizedTime) || normalizedTime < 0f
            || !Finite(maximumStep) || maximumStep <= 0f) return Vector3.zero;
        float due = Budget * profile.EvaluateAdvanceFraction(normalizedTime);
        float request = Mathf.Clamp(due - consumedDistance, 0f, maximumStep);
        // Drop both a blocked portion and excessive catch-up after a frame gap.
        // Remaining future portions of the curve can still be attempted normally.
        consumedDistance = Mathf.Max(consumedDistance, due);
        return direction * request;
    }

    public void Reset() { profile = null; Budget = consumedDistance = 0f; direction = Vector3.zero; }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
