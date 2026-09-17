using System;
using UnityEngine;

public sealed class AttackMovementExecutor
{
    public const float ComboMovementSpeedMultiplier = 2f;
    public const float ComboMovementDistanceMultiplier = 0.6f;

    private AttackMovementPhaseData[] phases;
    private Vector3[] appliedLocalDisplacements;
    private Vector3 forward;
    private Action<Vector3> applyDisplacement;

    public bool Begin(
        AttackMovementPhaseData[] movementPhases,
        Vector3 movementDirection,
        Action<Vector3> displacementCallback)
    {
        Cancel();

        if (movementPhases == null || movementPhases.Length == 0)
            return true;

        if (!MeleeComboStepValidator.TryValidateMovementPhases(movementPhases, out string error))
        {
            Debug.LogError("[AttackMovementExecutor] " + error);
            return false;
        }

        forward = FlattenDirection(movementDirection);
        applyDisplacement = displacementCallback;
        phases = movementPhases;
        appliedLocalDisplacements = new Vector3[phases.Length];
        return applyDisplacement != null;
    }

    public void Tick(float attackNormalizedTime)
    {
        if (phases == null || applyDisplacement == null)
            return;

        float normalizedTime = Mathf.Clamp01(attackNormalizedTime);
        for (int i = 0; i < phases.Length; i++)
        {
            AttackMovementPhaseData phase = phases[i];
            if (normalizedTime < phase.SafeStart)
                continue;

            float progress = Mathf.Clamp01(
                Mathf.InverseLerp(phase.SafeStart, phase.SafeEnd, normalizedTime)
                * ComboMovementSpeedMultiplier); // 동일 거리를 기존 시간의 1/2에 이동
            Vector3 targetLocalDisplacement = phase.EvaluateLocalDisplacement(progress)
                * ComboMovementDistanceMultiplier;
            Vector3 deltaLocalDisplacement = targetLocalDisplacement - appliedLocalDisplacements[i];
            if (deltaLocalDisplacement.sqrMagnitude <= 0.0000001f)
                continue;

            Vector3 worldDisplacement = forward * deltaLocalDisplacement.z;
            applyDisplacement(worldDisplacement);
            appliedLocalDisplacements[i] = targetLocalDisplacement;
        }
    }

    public void Cancel()
    {
        phases = null;
        appliedLocalDisplacements = null;
        forward = Vector3.forward;
        applyDisplacement = null;
    }

    private static Vector3 FlattenDirection(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.forward;
    }
}

public sealed class ComboMovementCollisionPusher
{
    public const float KnockbackStrength = 2.5f;

    private const float PreCollisionMargin = 0.25f;
    private const float ForwardDotThreshold = 0.01f;
    private const int OverlapCapacity = 32;

    private readonly Collider[] overlapResults = new Collider[OverlapCapacity];
    private readonly AttackHitRegistry pushedTargets = new AttackHitRegistry();
    private PlayerMovement cachedMovement;
    private CharacterController cachedCharacterController;

    public void BeginComboStep()
    {
        pushedTargets.Clear(); // 타수별 한 번만 밀기
    }

    public void PushBeforeMove(
        PlayerMovement movement,
        CombatTarget sourceTarget,
        Vector3 plannedDisplacement)
    {
        plannedDisplacement.y = 0f;
        float plannedDistance = plannedDisplacement.magnitude;
        if (movement == null || sourceTarget == null || plannedDistance <= 0.0001f)
            return; // 실제 콤보 이동량이 없으면 미사용

        CharacterController characterController = ResolveCharacterController(movement);
        if (characterController == null || !characterController.enabled)
            return;

        Vector3 scale = movement.transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        float heightScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float baseRadius = Mathf.Max(0.01f, characterController.radius * radiusScale);
        float expandedRadius = baseRadius + PreCollisionMargin + plannedDistance; // 이번 이동 전에 닿을 대상까지 선행 감지
        float height = Mathf.Max(baseRadius * 2f, characterController.height * heightScale);
        float halfSegment = Mathf.Max(0f, height * 0.5f - expandedRadius);
        Vector3 center = movement.transform.TransformPoint(characterController.center);
        Vector3 up = movement.transform.up.normalized;
        Vector3 top = center + up * halfSegment;
        Vector3 bottom = center - up * halfSegment;
        Vector3 moveDirection = plannedDisplacement / plannedDistance;

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            top,
            bottom,
            expandedRadius,
            overlapResults,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < overlapCount; i++)
            TryPushTarget(overlapResults[i], sourceTarget, center, moveDirection);
    }

    private void TryPushTarget(
        Collider candidateCollider,
        CombatTarget sourceTarget,
        Vector3 playerCenter,
        Vector3 moveDirection)
    {
        CombatTarget target = CombatTarget.Resolve(candidateCollider);
        if (!CombatTargetFilter.CanDamage(sourceTarget, target))
            return;

        Vector3 toTarget = target.WorldCenter - playerCenter;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f
            || Vector3.Dot(moveDirection, toTarget.normalized) < ForwardDotThreshold)
        {
            return; // 진행 방향 뒤쪽 대상 제외
        }

        EnemyMovementReaction movementReaction = target.GetComponentInParent<EnemyMovementReaction>();
        if (movementReaction == null || !pushedTargets.TryRegister(target.TargetId))
            return;

        movementReaction.ApplyKnockback(moveDirection, KnockbackStrength); // 피해 없는 이동 충돌 밀어내기
    }

    private CharacterController ResolveCharacterController(PlayerMovement movement)
    {
        if (cachedMovement == movement && cachedCharacterController != null)
            return cachedCharacterController;

        cachedMovement = movement;
        cachedCharacterController = movement.GetComponent<CharacterController>();
        return cachedCharacterController;
    }
}
