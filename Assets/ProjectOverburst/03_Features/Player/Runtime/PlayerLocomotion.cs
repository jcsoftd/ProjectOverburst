using UnityEngine;

// GOAL B2: 일반 이동의 방향 해석, 수평 가감속 명령과 회전 실행을 소유한다.
// PlayerMovement는 입력/전투 상태와 authored 수치를 유지하고 이 컴포넌트에 실행 의도를 전달한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(OverburstCharacterMotor3D))]
public sealed class PlayerLocomotion : MonoBehaviour
{
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private OverburstCharacterMotor3D motor;

    private Vector3 horizontalVelocity;

    public PlayerMovement BoundMovement => movement;
    public OverburstCharacterMotor3D Motor => motor;
    public Vector3 HorizontalVelocity => horizontalVelocity;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Bind(PlayerMovement owner, OverburstCharacterMotor3D characterMotor)
    {
        if (owner != null)
            movement = owner;
        if (characterMotor != null)
            motor = characterMotor;
        ResolveReferences();
    }

    public Vector3 ResolveMoveDirection(
        Vector2 input,
        bool cameraRelative,
        Transform movementCamera)
    {
        if (input.sqrMagnitude <= 0.001f)
            return Vector3.zero;

        if (!cameraRelative || movementCamera == null)
            return new Vector3(input.x, 0f, input.y).normalized;

        Vector3 forward = movementCamera.forward;
        Vector3 right = movementCamera.right;
        forward.y = 0f;
        right.y = 0f;
        if (forward.sqrMagnitude <= 0.001f || right.sqrMagnitude <= 0.001f)
            return new Vector3(input.x, 0f, input.y).normalized;

        forward.Normalize();
        right.Normalize();
        return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
    }

    public MotorStepResult Step(
        ActorMovementIntent intent,
        float baseMoveSpeed,
        float acceleration,
        float deceleration,
        float airControl,
        float jumpVelocity,
        float deltaTime)
    {
        ResolveReferences();
        if (motor == null)
            return default;

        Vector3 desiredDirection = intent.ShouldMove
            ? intent.DesiredDirection
            : Vector3.zero;
        desiredDirection.y = 0f;
        desiredDirection = desiredDirection.sqrMagnitude > 0.001f
            ? desiredDirection.normalized
            : Vector3.zero;

        Vector3 targetHorizontal = desiredDirection
            * Mathf.Max(0f, baseMoveSpeed)
            * Mathf.Max(0f, intent.SpeedMultiplier);
        MotorStepCommand command = new MotorStepCommand
        {
            currentHorizontalVelocity = horizontalVelocity,
            targetHorizontalVelocity = targetHorizontal,
            moveRate = intent.ShouldMove ? Mathf.Max(0f, acceleration) : Mathf.Max(0f, deceleration),
            airControl = Mathf.Max(0f, airControl),
            jumpVelocity = Mathf.Max(0f, jumpVelocity),
        };

        MotorStepResult result = motor.Step(command, Mathf.Max(0f, deltaTime));
        horizontalVelocity = result.horizontalVelocity;
        return result;
    }

    public void RotateTowards(Vector3 direction, float rotateSpeed, float speedMultiplier, float deltaTime)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        float rate = Mathf.Max(0f, rotateSpeed) * Mathf.Max(0f, speedMultiplier);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rate * Mathf.Max(0f, deltaTime));
    }

    public void RotateSmooth(Vector3 direction, float sharpness, float deltaTime)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        float t = 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * Mathf.Max(0f, deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
    }

    public void FaceImmediately(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    public void SetHorizontalVelocity(Vector3 value)
    {
        value.y = 0f;
        horizontalVelocity = value;
    }

    public void Stop()
    {
        horizontalVelocity = Vector3.zero;
    }

    public void ResetMotion()
    {
        Stop();
    }

    private void ResolveReferences()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
        if (motor == null)
            motor = GetComponent<OverburstCharacterMotor3D>();
    }
}
