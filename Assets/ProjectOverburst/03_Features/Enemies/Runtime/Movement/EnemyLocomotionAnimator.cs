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
    private Animator animator;
    private Quaternion turnStart, turnEnd;
    private float turnDirection, turnBegan;
    private bool turnEntered;
    private string turnState;
    public bool IsTurning { get; private set; }

    public bool BeginFacingTurn(Vector3 direction)
    {
        ResolveReferences();
        if(IsTurning || animator==null || movement==null || !movement.Profile.HasTurnAnimation || animationBridge.IsBlockingActionActive)return false;
        direction.y=0f;if(direction.sqrMagnitude<.0001f)return false;
        float angle=Mathf.Clamp(Vector3.SignedAngle(transform.forward,direction,Vector3.up),-90f,90f);
        if(Mathf.Abs(angle)<=5f)return false;
        turnDirection=angle;turnStart=transform.rotation;turnEnd=Quaternion.AngleAxis(angle,Vector3.up)*turnStart;
        turnState=angle<0f?"FacingTurnLeft":"FacingTurnRight";
        animator.SetFloat("TurnMagnitude",Mathf.Abs(angle)/90f);
        animator.CrossFadeInFixedTime(turnState,.06f,0,0f);
        IsTurning=true;turnEntered=false;turnBegan=Time.time;
        return true;
    }

    public void CancelFacingTurn() { IsTurning=false; turnEntered=false; }

    public bool TickFacingTurn(EnemyMotor motor)
    {
        if(!IsTurning)return false;
        var state=animator.GetCurrentAnimatorStateInfo(0);
        if(!state.IsName(turnState) && animator.IsInTransition(0))state=animator.GetNextAnimatorStateInfo(0);
        motor.HoldPosition();
        if(state.IsName(turnState))
        {
            turnEntered=true;
            motor.ApplyFacingRotation(Quaternion.Slerp(turnStart,turnEnd,movement.Profile.TurnProgress(turnDirection,state.normalizedTime)));
        }
        else if(turnEntered || Time.time-turnBegan>.75f)CancelFacingTurn();
        return IsTurning;
    }

    private void OnEnable() { previousPosition = transform.position; observedSpeed = 0f; requestedAmount = 0f; lastMovingAmount=1f;lastMovingReference=1f;CancelFacingTurn(); }
    private void OnDisable() { CancelFacingTurn(); }

    private void LateUpdate()
    {
        Vector3 displacement = transform.position - previousPosition;
        previousPosition = transform.position; displacement.y = 0f;
        if(IsTurning)return;
        if (movement == null || movement.Profile == null || !movement.Profile.MatchAnimationToActualMovement || animationBridge == null) return;
        float dt = Time.deltaTime;
        if (dt <= .00001f) return;
        // Render-frame displacement includes actual collision progress. A teleport is not a stride.
        if (displacement.magnitude > Mathf.Max(.75f, movement.ActiveMoveSpeed * dt * 4f))
        { observedSpeed = 0f; return; }
        observedSpeed = Mathf.Lerp(observedSpeed, displacement.magnitude / dt, 1f - Mathf.Exp(-dt / .06f));
        bool moving = observedSpeed > .06f && !movement.IsActionLocked && !animationBridge.IsBlockingActionActive && !animationBridge.IsFrozen;
        float gait = Mathf.Abs(requestedAmount) > .01f ? requestedAmount : lastMovingAmount;
        float strideReference = Mathf.Abs(requestedAmount) > .01f ? referenceSpeed : lastMovingReference;
        animationBridge.SetMoveAmount(moving ? gait : 0f);
        float speed = movement.Profile.ResolveCrowdAnimationSpeed(movement.ActiveMoveSpeed, observedSpeed);
        animationBridge.SetMoveAnimSpeed(moving ? Mathf.Max(.01f, speed / strideReference) : 1f);
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
        if(frozen)CancelFacingTurn();
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
