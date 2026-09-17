using UnityEngine;

public sealed class EnemyLocomotionAnimator : MonoBehaviour // 이동 애니메이션 전담
{
    [SerializeField] private EnemyAnimationBridge animationBridge; // Animator 연결

    public bool IsFrozen { get { return animationBridge != null && animationBridge.IsFrozen; } }

    public void SetMovement(bool isMoving, float moveSpeed, float referenceSpeed)
    {
        SetMovement(isMoving ? 1f : 0f, moveSpeed, referenceSpeed);
    }

    public void SetMovement(float movementAmount, float moveSpeed, float referenceSpeed)
    {
        if (animationBridge == null)
            ResolveReferences();
        if (animationBridge == null)
            return;

        animationBridge.SetMoveAmount(Mathf.Clamp(movementAmount, -1f, 2f));
        float resolvedReferenceSpeed = Mathf.Max(0.01f, referenceSpeed);
        animationBridge.SetMoveAnimSpeed(
            Mathf.Max(0.01f, moveSpeed) / resolvedReferenceSpeed);
    }

    public void Stop(float moveSpeed, float referenceSpeed)
    {
        SetMovement(false, moveSpeed, referenceSpeed);
    }

    public void SetFrozen(bool frozen)
    {
        if (animationBridge == null)
            ResolveReferences();
        animationBridge?.SetFrozen(frozen);
    }

    public bool AllowsMovement(EnemyLocomotionMode locomotionMode)
    {
        if (animationBridge == null)
            ResolveReferences();

        return animationBridge == null || animationBridge.AllowsMovement(locomotionMode);
    }

    public void ResolveReferences()
    {
        if (animationBridge == null)
            animationBridge = GetComponent<EnemyAnimationBridge>();
    }
}
