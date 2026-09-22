using UnityEngine;

public sealed class EnemyLocomotionAnimator : MonoBehaviour // 이동 애니메이션 전담
{
    [SerializeField] private EnemyAnimationBridge animationBridge; // Animator 연결
    private EnemyMovement movement;
    private Vector3 previousPosition;
    private float observedSpeed;
    private float requestedAmount;
    private float referenceSpeed = 1f;
    private float lastMovingAmount = 1f;
    private float lastMovingReference = 1f;
    private float previousYaw;
    private Animator animator;
    private static readonly int TurnHash = Animator.StringToHash("Turn");

    private void OnEnable() { previousPosition = transform.position; previousYaw = transform.eulerAngles.y; observedSpeed = 0f; requestedAmount = 0f; }

    private void LateUpdate()
    {
        Vector3 displacement = transform.position - previousPosition;
        float yaw = transform.eulerAngles.y;
        float turnDelta = Mathf.DeltaAngle(previousYaw, yaw); previousYaw = yaw;
        previousPosition = transform.position; displacement.y = 0f;
        if (movement == null || movement.Profile == null || !movement.Profile.MatchAnimationToActualMovement || animationBridge == null) return;
        float dt = Time.deltaTime;
        if (dt <= .00001f) return;
        // Render-frame displacement includes actual collision progress. A teleport is not a stride.
        if (displacement.magnitude > Mathf.Max(.75f, movement.ActiveMoveSpeed * dt * 4f))
        { observedSpeed = 0f; return; }
        observedSpeed = Mathf.Lerp(observedSpeed, displacement.magnitude / dt, 1f - Mathf.Exp(-dt / .06f));
        bool moving = observedSpeed > .06f && !movement.IsActionLocked && !animationBridge.IsBlockingActionActive && !animationBridge.IsFrozen;
        bool turning = !moving && movement.Profile.HasTurnAnimation && Mathf.Abs(turnDelta) / dt > 8f && !animationBridge.IsFrozen;
        if (animator != null && movement.Profile.HasTurnAnimation)
            animator.SetFloat(TurnHash, turning ? Mathf.Sign(turnDelta) : 0f, .06f, dt);
        float gait = Mathf.Abs(requestedAmount) > .01f ? requestedAmount : lastMovingAmount;
        float strideReference = Mathf.Abs(requestedAmount) > .01f ? referenceSpeed : lastMovingReference;
        animationBridge.SetMoveAmount(moving ? gait : 0f);
        float speed = movement.Profile.ResolveCrowdAnimationSpeed(movement.ActiveMoveSpeed, observedSpeed);
        animationBridge.SetMoveAnimSpeed(moving ? Mathf.Max(.01f, speed / strideReference)
            : turning ? Mathf.Clamp(Mathf.Abs(turnDelta) / dt / movement.Profile.TurnAnimationReferenceSpeed(turnDelta), .35f, 1.5f) : 1f);
    }

    public bool IsFrozen { get { return animationBridge != null && animationBridge.IsFrozen; } }

    public void SetMovement(bool isMoving, float moveSpeed, float referenceSpeed)
    {
        SetMovement(isMoving ? 1f : 0f, moveSpeed, referenceSpeed);
    }

    public void SetMovement(float movementAmount, float moveSpeed, float referenceSpeed)
    {
        if (animationBridge == null || movement == null)
            ResolveReferences();
        if (animationBridge == null)
            return;

        requestedAmount = Mathf.Clamp(movementAmount, -1f, 2f);
        this.referenceSpeed = Mathf.Max(.01f, referenceSpeed);
        if (Mathf.Abs(requestedAmount) > .01f) { lastMovingAmount = requestedAmount; lastMovingReference = this.referenceSpeed; }
        if (movement != null && movement.Profile != null && movement.Profile.MatchAnimationToActualMovement)
            return; // 테마 액터는 LateUpdate에서 실제 이동량으로 발놀림 동기화

        animationBridge.SetMoveAmount(Mathf.Clamp(movementAmount, -1f, 2f));
        float resolvedReferenceSpeed = Mathf.Max(0.01f, referenceSpeed);
        animationBridge.SetMoveAnimSpeed(
            Mathf.Max(0.01f, moveSpeed) / resolvedReferenceSpeed);
    }

    public void Stop(float moveSpeed, float referenceSpeed)
    {
        SetMovement(0f, 1f, 1f); // Idle은 직전 달리기 클립의 기준 속도를 물려받지 않음
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
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (movement == null) movement = GetComponent<EnemyMovement>();
        if (animationBridge == null)
            animationBridge = GetComponent<EnemyAnimationBridge>();
    }
}
