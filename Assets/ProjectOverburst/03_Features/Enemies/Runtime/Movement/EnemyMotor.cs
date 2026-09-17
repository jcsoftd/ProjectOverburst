using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class EnemyMotor : MonoBehaviour // Rigidbody 이동과 회전만 담당
{
    [SerializeField] private Rigidbody body; // 이동 대상
    [SerializeField, Min(0f)] private float facingRefreshInterval = 0.3f; // 방향 갱신 간격
    [SerializeField, Min(0f)] private float rotationDeadZone = 0.03f; // 미세 회전 무시

    private Vector3 desiredFacingDirection; // 목표 방향
    private float nextFacingRefreshTime; // 다음 방향 갱신
    private Rigidbody constraintSourceBody; // 원본 제약을 저장한 바디
    private RigidbodyConstraints movementConstraints; // 이동 가능한 원본 제약
    private bool isPositionHeld; // 정지 상태 XZ 고정 여부
    private bool isFrozen; // 빙결 위치·회전 하드 락

    public bool IsPositionHeld { get { return isPositionHeld; } }
    public bool IsFrozen { get { return isFrozen; } }
    public Vector3 Position { get { return body != null ? body.position : transform.position; } }

    private void Awake()
    {
        ResolveReferences();
    }

    public void Move(Vector3 direction, float speed, float turnSpeed)
    {
        Move(direction, speed, turnSpeed, direction);
    }

    public void Move(Vector3 direction, float speed, float turnSpeed, Vector3 facingDirection)
    {
        if (isFrozen)
        {
            HoldPosition();
            return;
        }

        ReleasePositionHold();
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f || speed <= 0f)
        {
            Stop();
            return;
        }

        Vector3 normalizedDirection = direction.normalized;
        facingDirection.y = 0f;
        RefreshFacingDirection(facingDirection.sqrMagnitude > 0.0001f ? facingDirection.normalized : normalizedDirection);
        Vector3 currentPosition = body != null ? body.position : transform.position;
        Vector3 nextPosition = currentPosition + normalizedDirection * speed * Time.fixedDeltaTime;

        if (body != null && !body.isKinematic)
        {
            body.MovePosition(nextPosition);
            CombatTargetRegistry.NotifySpatialMovement(transform, nextPosition);
        }
        else
        {
            transform.position = nextPosition;
            CombatTargetRegistry.NotifySpatialChanged(transform);
        }

        Rotate(turnSpeed);
    }

    public void MoveToPosition(Vector3 position)
    {
        if (isFrozen)
        {
            HoldPosition();
            return;
        }

        ReleasePositionHold();
        if (body != null && !body.isKinematic)
        {
            body.MovePosition(position);
            CombatTargetRegistry.NotifySpatialMovement(transform, position);
        }
        else
        {
            transform.position = position;
            CombatTargetRegistry.NotifySpatialChanged(transform);
        }
    }

    public void MoveToPosition(Vector3 position, float turnSpeed, Vector3 facingDirection)
    {
        if (isFrozen)
        {
            HoldPosition();
            return;
        }

        ReleasePositionHold();
        facingDirection.y = 0f;
        if (facingDirection.sqrMagnitude > 0.0001f)
            RefreshFacingDirection(facingDirection.normalized);

        if (body != null && !body.isKinematic)
        {
            body.MovePosition(position);
            CombatTargetRegistry.NotifySpatialMovement(transform, position);
        }
        else
        {
            transform.position = position;
            CombatTargetRegistry.NotifySpatialChanged(transform);
        }

        Rotate(turnSpeed);
    }

    public void Face(Vector3 direction, float turnSpeed)
    {
        if (isFrozen)
            return;

        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        desiredFacingDirection = direction.normalized;
        nextFacingRefreshTime = 0f;
        Rotate(turnSpeed);
    }

    public void Stop()
    {
        if (body == null || body.isKinematic)
            return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    public void HoldPosition()
    {
        Stop();
        if (body == null || body.isKinematic)
            return;

        CacheMovementConstraints();
        body.constraints = movementConstraints
            | RigidbodyConstraints.FreezePositionX
            | RigidbodyConstraints.FreezePositionZ
            | (isFrozen ? RigidbodyConstraints.FreezeRotationY : RigidbodyConstraints.None);
        isPositionHeld = true; // 군집 충돌에 의한 Idle 슬라이딩 차단
    }

    public void SetFrozen(bool frozen)
    {
        if (isFrozen == frozen)
        {
            if (frozen)
                HoldPosition();
            return;
        }

        isFrozen = frozen;
        Stop();
        if (body == null || body.isKinematic)
            return;

        CacheMovementConstraints();
        body.constraints = movementConstraints
            | RigidbodyConstraints.FreezePositionX
            | RigidbodyConstraints.FreezePositionZ
            | (frozen ? RigidbodyConstraints.FreezeRotationY : RigidbodyConstraints.None);
        isPositionHeld = true; // 해제 뒤 다음 정상 이동까지 제자리 유지
    }

    public void ResolveReferences()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();

        CacheMovementConstraints();
    }

    private void ReleasePositionHold()
    {
        if (isFrozen)
            return;

        if (!isPositionHeld)
            return;

        if (body != null)
            body.constraints = movementConstraints;
        isPositionHeld = false;
    }

    private void CacheMovementConstraints()
    {
        if (body == null || constraintSourceBody == body)
            return;

        constraintSourceBody = body;
        movementConstraints = body.constraints;
        isPositionHeld = false;
    }

    private void RefreshFacingDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude <= rotationDeadZone * rotationDeadZone)
            return;

        if (Time.time < nextFacingRefreshTime && desiredFacingDirection.sqrMagnitude > 0.0001f)
            return;

        desiredFacingDirection = direction.normalized;
        nextFacingRefreshTime = Time.time + facingRefreshInterval;
    }

    private void Rotate(float turnSpeed)
    {
        if (desiredFacingDirection.sqrMagnitude <= 0.0001f || turnSpeed <= 0f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(desiredFacingDirection, Vector3.up);
        Quaternion nextRotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);

        if (body != null && !body.isKinematic)
            body.MoveRotation(nextRotation);
        else
            transform.rotation = nextRotation;
    }
}
