using UnityEngine;

[CreateAssetMenu(fileName = "EnemyMovementProfile", menuName = "OVERBURST/Enemies/Movement Profile")]
public sealed class EnemyMovementProfile : ScriptableObject // 몬스터 이동 수치의 단일 원본
{
    public const float MinimumMoveSpeed = 1f;
    public const float MinimumRunSpeedMultiplier = 1.4f;
    public const float DefaultRunSpeedMultiplier = 1.6f;
    public const float DefaultDodgeSpeedMultiplier = 2.6f;

    [SerializeField] private string profileId = "Default"; // 프로필 ID
    [SerializeField, Min(MinimumMoveSpeed)] private float moveSpeed = MinimumMoveSpeed; // 초당 이동 거리
    [SerializeField, Min(0f)] private float turnSpeed = 360f; // 초당 회전 각도
    [SerializeField, Min(0.01f)] private float animationReferenceSpeed = MinimumMoveSpeed; // 걷기 원본 자연 속도
    [SerializeField, Min(0.01f)] private float runAnimationReferenceSpeed = MinimumMoveSpeed; // 달리기 원본 자연 속도
    [SerializeField, Min(0f)] private float backpedalAnimationReferenceSpeed; // 0: 기존 걷기 기준 사용
    [SerializeField, Min(MinimumRunSpeedMultiplier)] private float runSpeedMultiplier = DefaultRunSpeedMultiplier; // 달리기 배율
    [SerializeField, Min(1f)] private float dodgeSpeedMultiplier = DefaultDodgeSpeedMultiplier; // 회피 이동 배율
    [SerializeField, Range(0f, 100f)] private float knockbackReductionPercent; // 넉백 이동 감소율
    [SerializeField, Min(0.1f)] private float crowdWeight = 1f; // 군집 보정 양보 비율
    [SerializeField, Min(0f)] private float crowdAnimationSpeedLimit; // 0: 기존 동작, 1 이상: 명령 속도 대비 배속 상한

    public string ProfileId { get { return profileId; } }
    public float MoveSpeed { get { return Mathf.Max(MinimumMoveSpeed, moveSpeed); } }
    public float TurnSpeed { get { return Mathf.Max(0f, turnSpeed); } }
    public float AnimationReferenceSpeed { get { return Mathf.Max(0.01f, animationReferenceSpeed); } }
    public float RunAnimationReferenceSpeed { get { return Mathf.Max(0.01f, runAnimationReferenceSpeed); } }
    public float RunSpeedMultiplier { get { return Mathf.Max(MinimumRunSpeedMultiplier, runSpeedMultiplier); } }
    public float DodgeSpeedMultiplier { get { return Mathf.Max(1f, dodgeSpeedMultiplier); } }
    public float KnockbackReductionPercent { get { return Mathf.Clamp(knockbackReductionPercent, 0f, 100f); } }
    public float CrowdWeight { get { return Mathf.Max(0.1f, crowdWeight); } }
    public bool MatchAnimationToActualMovement => crowdAnimationSpeedLimit > 0f;
    [SerializeField] private Vector2 turnAnimationReferenceSpeeds;
    public bool HasTurnAnimation => turnAnimationReferenceSpeeds.x > 0f && turnAnimationReferenceSpeeds.y > 0f;
    public float TurnAnimationReferenceSpeed(float direction) => direction < 0f ? turnAnimationReferenceSpeeds.x : turnAnimationReferenceSpeeds.y;
    public void ConfigureTurnAnimation(float left, float right) => turnAnimationReferenceSpeeds = new Vector2(left, right);

    public void ConfigureCrowdAnimationSpeedLimit(float multiplier)
    {
        crowdAnimationSpeedLimit = multiplier > 0f ? Mathf.Max(1f, multiplier) : 0f;
    }

    public float ResolveCrowdAnimationSpeed(float commandedSpeed, float actualSpeed)
    {
        float speed = Mathf.Max(commandedSpeed, actualSpeed);
        return crowdAnimationSpeedLimit > 0f
            ? Mathf.Min(Mathf.Max(0f, actualSpeed), commandedSpeed * Mathf.Max(1f, crowdAnimationSpeedLimit))
            : speed;
    }

    public void Configure(string id, float speed, float rotationSpeed, float referenceSpeed) // Editor 에셋 생성
    {
        Configure(id, speed, rotationSpeed, referenceSpeed, DefaultRunSpeedMultiplier, DefaultDodgeSpeedMultiplier);
    }

    public void Configure(string id, float speed, float rotationSpeed, float referenceSpeed, float runMultiplier, float dodgeMultiplier)
    {
        profileId = string.IsNullOrWhiteSpace(id) ? "Default" : id;
        moveSpeed = Mathf.Max(MinimumMoveSpeed, speed);
        turnSpeed = Mathf.Max(0f, rotationSpeed);
        animationReferenceSpeed = Mathf.Max(0.01f, referenceSpeed);
        runSpeedMultiplier = Mathf.Max(MinimumRunSpeedMultiplier, runMultiplier);
        dodgeSpeedMultiplier = Mathf.Max(1f, dodgeMultiplier);
    }

    public void ConfigureRunSpeedMultiplier(float multiplier) // 달리기 배율만 조정
    {
        runSpeedMultiplier = Mathf.Max(MinimumRunSpeedMultiplier, multiplier);
    }

    public void ConfigureAnimationReferenceSpeeds(float walkSpeed, float runSpeed)
    {
        animationReferenceSpeed = Mathf.Max(0.01f, walkSpeed);
        runAnimationReferenceSpeed = Mathf.Max(0.01f, runSpeed);
    }

    public void ConfigureBackpedalAnimationReferenceSpeed(float speed)
    {
        backpedalAnimationReferenceSpeed = Mathf.Max(.01f, speed);
    }

    public float GetAnimationReferenceSpeed(EnemyLocomotionMode mode)
    {
        if (mode == EnemyLocomotionMode.Backpedal && backpedalAnimationReferenceSpeed > 0f)
            return backpedalAnimationReferenceSpeed;
        return mode == EnemyLocomotionMode.Run
            ? RunAnimationReferenceSpeed
            : Mathf.Max(0.01f, animationReferenceSpeed);
    }

    public void ConfigureKnockbackReductionPercent(float percent) // 넉백 감소율만 조정
    {
        knockbackReductionPercent = Mathf.Clamp(percent, 0f, 100f);
    }

    public void ConfigureCrowdWeight(float weight) // 군집 체급만 조정
    {
        crowdWeight = Mathf.Max(0.1f, weight);
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(MinimumMoveSpeed, moveSpeed);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        animationReferenceSpeed = Mathf.Max(0.01f, animationReferenceSpeed);
        runAnimationReferenceSpeed = Mathf.Max(0.01f, runAnimationReferenceSpeed);
        runSpeedMultiplier = Mathf.Max(MinimumRunSpeedMultiplier, runSpeedMultiplier);
        dodgeSpeedMultiplier = Mathf.Max(1f, dodgeSpeedMultiplier);
        knockbackReductionPercent = Mathf.Clamp(knockbackReductionPercent, 0f, 100f);
        crowdWeight = Mathf.Max(0.1f, crowdWeight);
    }
}
