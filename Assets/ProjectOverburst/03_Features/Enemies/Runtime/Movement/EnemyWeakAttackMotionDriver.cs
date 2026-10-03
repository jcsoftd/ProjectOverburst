using System;
using UnityEngine;

// Produces the V3 movement request before EnemyMovement consumes it. The legacy
// baked-pelvis extractor must not also produce movement during this execution.
[DefaultExecutionOrder(-30)]
[DisallowMultipleComponent]
public sealed class EnemyWeakAttackMotionDriver : MonoBehaviour
{
    private const float MaximumStepMeters = .3f;
    [SerializeField] private EnemyMeleeAttackController owner;
    [SerializeField] private EnemyMovement movement;
    private readonly EnemyWeakAttackAdvance advance = new EnemyWeakAttackAdvance();
    private Quaternion facing;
    public bool IsActive => advance.IsActive;
    public float Budget => advance.Budget;
    public float ConsumedDistance => advance.ConsumedDistance;

    public void Configure(EnemyMeleeAttackController executor, EnemyMovement actorMovement)
    { End(); owner = executor; movement = actorMovement; }
    private void Awake() => ResolveReferences();
    private void OnDisable() => End();
    public bool CanUse(EnemyWeakAttackExecutionProfile profile)
    {
        ResolveReferences();
        return isActiveAndEnabled && owner != null && movement != null && movement.isActiveAndEnabled
            && profile != null && profile.IsValid;
    }

    public void Begin(EnemyWeakAttackExecutionProfile profile, float distance, Quaternion startingFacing)
    {
        if (!CanUse(profile)) throw new InvalidOperationException("약공 이동 실행기가 연결되지 않았습니다.");
        float norm = Quaternion.Dot(startingFacing, startingFacing);
        if (float.IsNaN(norm) || float.IsInfinity(norm) || Mathf.Abs(norm - 1f) > .01f)
            throw new ArgumentException("약공 시작 회전이 유효하지 않습니다.");
        End(); facing = startingFacing;
        advance.Begin(profile, distance, startingFacing * Vector3.forward);
    }

    private void FixedUpdate()
    {
        if (!IsActive) return;
        if (owner == null || owner.ActiveWeakExecution == null || movement == null || !movement.isActiveAndEnabled
            || movement.IsStatusMovementLocked || owner.StatusActionSpeedMultiplier <= 0f)
        { End(); return; }
        movement.ApplyActionLock(.2f);
        if (!owner.HasEnteredWeakAttack) return;
        Vector3 step = advance.Consume(owner.WeakAttackNormalizedTime, MaximumStepMeters);
        movement.RequestBoundedAttackDisplacement(step, facing);
    }

    public void End()
    {
        if (!advance.IsActive) return;
        advance.Reset(); movement?.ClearAttackDisplacement();
    }
    private void ResolveReferences()
    {
        if (owner == null) owner = GetComponent<EnemyMeleeAttackController>();
        if (movement == null) movement = GetComponent<EnemyMovement>();
    }
}
