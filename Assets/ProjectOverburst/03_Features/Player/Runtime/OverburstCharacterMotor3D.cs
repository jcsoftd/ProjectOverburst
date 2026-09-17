using UnityEngine;

// GOAL B1: 접지·경사·중력·CharacterController 이동을 소유하는 프로젝트 모터.
// PlayerMovement는 입력/의도/속도 정책과 어댑터 API를 유지하고, 한 프레임 이동 명령만 전달한다.
// balance 수치는 PlayerMovement 직렬화 값을 그대로 사용한다. 새 기본값을 임의로 만들지 않는다.

// PlayerMovement 직렬화 값의 복사본. 작성 소유권은 PlayerMovement에 있으며 모터는 실행만 한다.
[System.Serializable]
public struct CharacterMotorSettings
{
    public float skinWidthRadiusRatio;
    public float slopeLimit;
    public float stepOffset;
    public float minMoveDistance;
    public float groundCheckRadius;
    public LayerMask groundLayer;
    public float groundProbeStartOffset;
    public float groundSnapDistance;
    public float groundNormalSharpness;
    public float gravity;
    public float groundStickVelocity;
    public float maxFallSpeed;
    public float externalVelocityDecay;
    public float maxExternalSpeed;
}

// 한 프레임 이동 명령. 목표 수평 속도와 가감속·공중 제어·점프를 전달한다.
public struct MotorStepCommand
{
    public Vector3 currentHorizontalVelocity;
    public Vector3 targetHorizontalVelocity;
    public float moveRate;
    public float airControl;
    public float jumpVelocity;
}

// 한 프레임 이동 결과. PlayerMovement가 기존 필드·프로퍼티에 그대로 복사한다.
public struct MotorStepResult
{
    public Vector3 horizontalVelocity;
    public float verticalVelocity;
    public bool isGrounded;
    public Vector3 groundNormal;
    public Vector3 groundContactNormal;
    public bool hasWalkableGround;
    public float groundGap;
    public float groundSurfaceHeight;
    public bool groundSnapActive;
    public bool didLand;
    public float landingFallSpeed;
}
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class OverburstCharacterMotor3D : MonoBehaviour
{
    // 기존 무제한 낙하와 동등한 migration 값.
    // 근거: 중력 -25에서 20m 낙하 도달 속도는 약 31.6m/s이며, 정상 플레이 낙하가 이 한계에 닿지 않아
    // 기존 감각과 동일하고 극단 낙하의 터널링만 방지한다. fps/지형 검증에서 동등성을 수치로 확인한다.
    public const float DefaultMaxFallSpeed = 60f;

    private const float GroundContactTolerance = 0.06f;
    private const int GroundProbeCapacity = 8;
    private const int HeightQueryCapacity = 32;
    private const float DefaultSkinWidthRadiusRatio = 0.1f;

    [SerializeField] private PlayerMovement movement;

    private CharacterController controller;
    private Rigidbody legacyRigidbody;
    private CapsuleCollider legacyCapsuleCollider;
    private Transform groundCheck;
    private CharacterMotorSettings settings;
    private readonly RaycastHit[] groundProbeHits = new RaycastHit[GroundProbeCapacity];
    private readonly Collider[] currentHeightOverlaps = new Collider[HeightQueryCapacity];
    private readonly Collider[] targetHeightOverlaps = new Collider[HeightQueryCapacity];

    private bool controllerShapeInitialized;
    private float standingHeight;
    private Vector3 externalVelocity;
    private Transform currentPlatform;
    private Vector3 platformLocalAnchor;
    private Vector3 platformWorldAnchor;
    private Quaternion platformRotation = Quaternion.identity;
    private Vector3 pendingPlatformDisplacement;
    private Quaternion pendingPlatformRotation = Quaternion.identity;
    private Vector3 platformVelocity;

    private float verticalVelocity;
    private bool jumpStartedThisStep;
    private bool isGrounded;
    private Vector3 groundNormal = Vector3.up;
    private Vector3 groundContactNormal = Vector3.up;
    private float groundSurfaceHeight = float.NegativeInfinity;
    private float groundGap = float.PositiveInfinity;
    private bool hasWalkableGround;
    private bool groundSnapActive;
    private float lastAirVerticalVelocity;
    private bool didLandThisStep;
    private float landingFallSpeed;

    public CharacterController Controller
    {
        get
        {
            EnsureInitialized();
            return controller;
        }
    }

    public PlayerMovement BoundMovement => movement;

    public CharacterMotorSettings ActiveSettings => settings;

    public float VerticalVelocity => verticalVelocity;
    public bool IsGrounded => isGrounded;
    public Vector3 GroundNormal => groundNormal;
    public Vector3 GroundContactNormal => groundContactNormal;
    public float GroundSurfaceHeight => groundSurfaceHeight;
    public float GroundGap => groundGap;
    public bool HasWalkableGround => hasWalkableGround;
    public bool GroundSnapActive => groundSnapActive;
    public bool DidLandThisStep => didLandThisStep;
    public float LandingFallSpeed => landingFallSpeed;
    public Vector3 ExternalVelocity => externalVelocity;
    public Transform CurrentPlatform => currentPlatform;
    public Vector3 PlatformVelocity => platformVelocity;
    public float StandingHeight
    {
        get
        {
            EnsureInitialized();
            return standingHeight;
        }
    }
    public float CurrentHeight
    {
        get
        {
            EnsureInitialized();
            return controller != null ? controller.height : 0f;
        }
    }

    public Vector3 ControllerPlanarVelocity
    {
        get
        {
            if (controller == null)
                return Vector3.zero;
            Vector3 velocity = controller.velocity;
            return new Vector3(velocity.x, 0f, velocity.z);
        }
    }

    private void Awake()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
        EnsureInitialized();
    }

    private void OnDisable()
    {
        ResetMotion();
    }

    public void BindMovement(PlayerMovement value)
    {
        if (value != null)
            movement = value;
    }

    public void Configure(CharacterMotorSettings motorSettings)
    {
        settings = motorSettings;
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();
        if (controller == null)
            controller = gameObject.AddComponent<CharacterController>();

        if (legacyRigidbody == null)
            legacyRigidbody = GetComponent<Rigidbody>();
        if (legacyCapsuleCollider == null)
            legacyCapsuleCollider = GetComponent<CapsuleCollider>();
        if (!controllerShapeInitialized)
        {
            ConfigureCharacterControllerFromCapsule();
            controllerShapeInitialized = true;
            standingHeight = controller != null ? controller.height : 0f;
        }
        else
        {
            ConfigureCharacterControllerSettings();
        }
        ConfigureLegacyPhysicsComponents();

        if (groundCheck == null)
        {
            Transform found = transform.Find("GroundCheck");
            if (found != null)
                groundCheck = found;
        }

        if (movement == null)
            movement = GetComponent<PlayerMovement>();
    }

    // 프레임 시작 접지 probe. PlayerMovement.Update의 CheckGround와 같은 순서에 호출한다.
    public void ProbeGround(float deltaTime)
    {
        EnsureInitialized();
        bool wasGrounded = isGrounded;
        bool controllerGrounded = controller != null && controller.isGrounded;
        hasWalkableGround = TryProbeWalkableGround(out RaycastHit groundHit, out groundGap);
        bool withinContact = hasWalkableGround && groundGap <= GroundContactTolerance;
        groundSnapActive = !jumpStartedThisStep
            && verticalVelocity <= 0f
            && hasWalkableGround
            && groundGap <= Mathf.Max(GroundContactTolerance, settings.groundSnapDistance)
            && (wasGrounded || controllerGrounded);
        isGrounded = controllerGrounded || withinContact || groundSnapActive;
        UpdateGroundNormal(wasGrounded, groundHit, deltaTime);
        UpdatePlatformTracking(
            isGrounded && groundHit.collider != null ? groundHit.collider.transform : null,
            deltaTime);

        didLandThisStep = false;
        landingFallSpeed = 0f;
        if (!isGrounded)
        {
            lastAirVerticalVelocity = verticalVelocity;
            return;
        }

        if (!wasGrounded)
        {
            didLandThisStep = true;
            landingFallSpeed = lastAirVerticalVelocity;
        }
    }

    // 수평 명령 + 점프 소비 + 단일 CharacterController.Move + 이동 후 접지 갱신.
    public MotorStepResult Step(MotorStepCommand command, float deltaTime)
    {
        EnsureInitialized();
        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        Vector3 platformDisplacement = pendingPlatformDisplacement;
        Quaternion platformRotationDelta = pendingPlatformRotation;
        Vector3 externalVelocityForStep = externalVelocity;
        pendingPlatformDisplacement = Vector3.zero;
        pendingPlatformRotation = Quaternion.identity;
        if (command.jumpVelocity > 0f)
        {
            DetachFromPlatformInternal(true, false);
            verticalVelocity = command.jumpVelocity;
            jumpStartedThisStep = true;
            isGrounded = false;
            hasWalkableGround = false;
            groundSnapActive = false;
            groundContactNormal = Vector3.up;
            groundSurfaceHeight = float.NegativeInfinity;
        }

        bool startedGrounded = isGrounded && !jumpStartedThisStep;
        Vector3 currentHorizontal = new Vector3(
            command.currentHorizontalVelocity.x, 0f, command.currentHorizontalVelocity.z);
        float controlRate = isGrounded ? 1f : command.airControl;
        Vector3 nextHorizontal = Vector3.MoveTowards(
            currentHorizontal,
            Planar(command.targetHorizontalVelocity),
            command.moveRate * controlRate * safeDeltaTime);
        Vector3 surfaceVelocity = ResolveSurfaceVelocity(nextHorizontal);
        Vector3 motion = platformDisplacement
            + surfaceVelocity * safeDeltaTime
            + externalVelocityForStep * safeDeltaTime
            + ResolveVerticalMotion(safeDeltaTime, surfaceVelocity);
        float verticalVelocityBeforeMove = verticalVelocity;

        CollisionFlags flags = ExecuteMove(motion);
        ApplyPlatformRotation(platformRotationDelta);
        ResolveExternalCollisions(flags);
        DecayExternalVelocity(safeDeltaTime);
        bool controllerGrounded = controller.isGrounded || (flags & CollisionFlags.Below) != 0;
        bool followedGround = RefreshGroundAfterMove(startedGrounded, controllerGrounded);
        isGrounded = controllerGrounded || followedGround;
        groundSnapActive = followedGround && !controllerGrounded;

        if (!isGrounded)
        {
            lastAirVerticalVelocity = verticalVelocityBeforeMove;
        }
        else if (!startedGrounded && !jumpStartedThisStep)
        {
            didLandThisStep = true;
            landingFallSpeed = Mathf.Min(verticalVelocityBeforeMove, lastAirVerticalVelocity);
        }

        if (!jumpStartedThisStep && isGrounded)
            verticalVelocity = 0f;

        jumpStartedThisStep = false;
        ReanchorPlatform();
        return new MotorStepResult
        {
            horizontalVelocity = nextHorizontal,
            verticalVelocity = verticalVelocity,
            isGrounded = isGrounded,
            groundNormal = groundNormal,
            groundContactNormal = groundContactNormal,
            hasWalkableGround = hasWalkableGround,
            groundGap = groundGap,
            groundSurfaceHeight = groundSurfaceHeight,
            groundSnapActive = groundSnapActive,
            didLand = didLandThisStep,
            landingFallSpeed = landingFallSpeed,
        };
    }

    // 회피·무기 root motion 같은 직접 변위 경로. 적 관통 방지는 호출자(PlayerMovement)가 먼저 적용한다.
    public void MoveDirect(Vector3 displacement)
    {
        EnsureInitialized();
        if (controller == null)
            return;

        CollisionFlags flags = ExecuteMove(displacement);
        isGrounded = controller.isGrounded || (flags & CollisionFlags.Below) != 0;
        ResolveExternalCollisions(flags);
        ReanchorPlatform();
    }

    public void AttachToPlatform(Transform platform)
    {
        EnsureInitialized();
        if (platform == null || platform == transform || platform.IsChildOf(transform))
            return;

        if (currentPlatform == platform)
        {
            ReanchorPlatform();
            return;
        }

        currentPlatform = platform;
        pendingPlatformDisplacement = Vector3.zero;
        pendingPlatformRotation = Quaternion.identity;
        platformVelocity = Vector3.zero;
        ReanchorPlatform();
    }

    public void DetachFromPlatform(bool inheritVelocity = true)
    {
        DetachFromPlatformInternal(inheritVelocity, true);
    }

    public void AddExternalForce(Vector3 velocityChange)
    {
        float maxSpeed = Mathf.Max(0f, settings.maxExternalSpeed);
        externalVelocity += velocityChange;
        if (maxSpeed > 0f)
            externalVelocity = Vector3.ClampMagnitude(externalVelocity, maxSpeed);
    }

    public void ClearExternalForces()
    {
        externalVelocity = Vector3.zero;
    }

    // 실제 crouch 상태는 B2 범위가 아니다. 후속 기능이 안전하게 높이만 요청할 수 있는 기반 API다.
    public bool SetHeightWithHeadroom(float targetHeight)
    {
        EnsureInitialized();
        if (controller == null)
            return false;

        float minimumHeight = Mathf.Max(0.01f, controller.radius * 2f);
        float clampedHeight = Mathf.Max(minimumHeight, targetHeight);
        float currentHeight = controller.height;
        if (Mathf.Approximately(currentHeight, clampedHeight))
            return true;

        Vector3 targetCenter = controller.center;
        targetCenter.y += (clampedHeight - currentHeight) * 0.5f;
        if (clampedHeight > currentHeight
            && !HasExpansionHeadroom(currentHeight, controller.center, clampedHeight, targetCenter))
        {
            return false;
        }

        controller.height = clampedHeight;
        controller.center = targetCenter;
        return true;
    }

    public void HaltMotion()
    {
        verticalVelocity = 0f;
        jumpStartedThisStep = false;
        lastAirVerticalVelocity = 0f;
        didLandThisStep = false;
        landingFallSpeed = 0f;
        ClearExternalForces();
    }

    public void ResetMotion()
    {
        HaltMotion();
        isGrounded = false;
        groundNormal = Vector3.up;
        groundContactNormal = Vector3.up;
        groundSurfaceHeight = float.NegativeInfinity;
        groundGap = float.PositiveInfinity;
        hasWalkableGround = false;
        groundSnapActive = false;
        DetachFromPlatformInternal(false, true);
    }

    private Vector3 ResolveSurfaceVelocity(Vector3 horizontal)
    {
        if (!isGrounded
            || !hasWalkableGround
            || horizontal.sqrMagnitude <= 0.0001f)
        {
            return horizontal;
        }

        Vector3 normal = groundContactNormal.sqrMagnitude > 0.0001f
            ? groundContactNormal.normalized
            : Vector3.up;
        Vector3 tangent = Vector3.ProjectOnPlane(horizontal, normal);
        return tangent.sqrMagnitude > 0.0001f
            ? tangent.normalized * horizontal.magnitude
            : horizontal;
    }

    private Vector3 ResolveVerticalMotion(float deltaTime, Vector3 surfaceVelocity)
    {
        if (!jumpStartedThisStep && isGrounded)
        {
            verticalVelocity = 0f;
            bool isFlatGround = groundContactNormal.y >= 0.999f;
            bool isDescendingSlope = surfaceVelocity.y < -0.001f;
            if (!isFlatGround && !isDescendingSlope)
                return Vector3.zero;

            float stickDistance = Mathf.Abs(settings.groundStickVelocity) * deltaTime;
            float snapDistance = groundSnapActive
                ? Mathf.Min(Mathf.Max(0f, groundGap), Mathf.Max(0f, settings.groundSnapDistance))
                : 0f;
            return Vector3.down * Mathf.Max(stickDistance, snapDistance);
        }

        verticalVelocity = Mathf.Max(
            verticalVelocity + settings.gravity * deltaTime,
            -Mathf.Max(0.01f, settings.maxFallSpeed));
        return Vector3.up * (verticalVelocity * deltaTime);
    }

    private bool RefreshGroundAfterMove(bool startedGrounded, bool controllerGrounded)
    {
        if (!startedGrounded || jumpStartedThisStep || verticalVelocity > 0f)
            return false;

        if (!TryProbeWalkableGroundAt(
                transform.position,
                0f,
                Mathf.Max(GroundContactTolerance, settings.groundSnapDistance),
                out RaycastHit groundHit,
                out float surfaceOffset))
        {
            if (!controllerGrounded)
            {
                hasWalkableGround = false;
                groundGap = float.PositiveInfinity;
                groundSurfaceHeight = float.NegativeInfinity;
                groundContactNormal = Vector3.up;
            }

            return false;
        }

        float snapDistance = Mathf.Max(0f, -surfaceOffset);
        float followDistance = Mathf.Max(GroundContactTolerance, settings.groundSnapDistance);
        if (snapDistance > followDistance)
            return false;

        hasWalkableGround = true;
        groundGap = snapDistance;
        groundSurfaceHeight = GetCharacterFootPosition(transform.position).y + surfaceOffset;
        groundContactNormal = groundHit.normal.sqrMagnitude > 0.0001f
            ? groundHit.normal.normalized
            : Vector3.up;

        return controllerGrounded || groundGap <= followDistance;
    }

    private bool TryProbeWalkableGround(out RaycastHit bestHit, out float bestGap)
    {
        bool foundGround = TryProbeWalkableGroundAt(
            transform.position,
            0f,
            Mathf.Max(GroundContactTolerance, settings.groundSnapDistance),
            out bestHit,
            out float surfaceOffset);
        bestGap = foundGround
            ? Mathf.Max(0f, -surfaceOffset)
            : float.PositiveInfinity;
        groundSurfaceHeight = foundGround
            ? GetCharacterFootPosition(transform.position).y + surfaceOffset
            : float.NegativeInfinity;
        return foundGround;
    }

    private bool TryProbeWalkableGroundAt(
        Vector3 rootPosition,
        float upwardAllowance,
        float downwardAllowance,
        out RaycastHit bestHit,
        out float bestSurfaceOffset)
    {
        bestHit = default;
        bestSurfaceOffset = float.NegativeInfinity;
        if (controller == null)
            return false;

        float controllerRadius = Mathf.Max(0.05f, controller.radius);
        float probeRadius = Mathf.Clamp(
            settings.groundCheckRadius,
            0.05f,
            Mathf.Max(0.05f, controllerRadius * 0.95f));
        float startOffset = Mathf.Max(0.01f, settings.groundProbeStartOffset);
        float safeUpwardAllowance = Mathf.Max(0f, upwardAllowance);
        float safeDownwardAllowance = Mathf.Max(GroundContactTolerance, downwardAllowance);
        float skinWidth = Mathf.Max(0.01f, controller.skinWidth);
        Vector3 foot = GetCharacterFootPosition(rootPosition);
        Vector3 origin = foot + Vector3.up * (probeRadius + startOffset + safeUpwardAllowance);
        float castDistance = safeUpwardAllowance + startOffset + safeDownwardAllowance + skinWidth;
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            probeRadius,
            Vector3.down,
            groundProbeHits,
            castDistance,
            settings.groundLayer,
            QueryTriggerInteraction.Ignore);
        float slopeLimit = controller != null
            ? Mathf.Max(0f, controller.slopeLimit)
            : 45f;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundProbeHits[i];
            Collider collider = hit.collider;
            if (collider == null
                || collider.transform.IsChildOf(transform)
                || Vector3.Angle(hit.normal, Vector3.up) > slopeLimit + 0.01f)
            {
                continue;
            }

            if (hit.distance >= bestDistance)
                continue;

            bestHit = hit;
            bestDistance = hit.distance;
            bestSurfaceOffset = safeUpwardAllowance + startOffset - hit.distance;
        }

        return bestHit.collider != null;
    }

    private Vector3 GetCharacterFootPosition(Vector3 rootPosition)
    {
        if (controller == null)
            return rootPosition;

        float controllerRadius = Mathf.Max(0.05f, controller.radius);
        Vector3 centerOffset = transform.TransformVector(controller.center);
        Vector3 center = rootPosition + centerOffset;
        return center - Vector3.up * Mathf.Max(controllerRadius, controller.height * 0.5f);
    }

    private void UpdateGroundNormal(bool wasGrounded, RaycastHit groundHit, float deltaTime)
    {
        if (!hasWalkableGround || groundHit.collider == null)
        {
            groundContactNormal = Vector3.up;
            groundNormal = Vector3.up;
            return;
        }

        Vector3 targetNormal = groundHit.normal.sqrMagnitude > 0.0001f
            ? groundHit.normal.normalized
            : Vector3.up;
        groundContactNormal = targetNormal;
        if (!wasGrounded || settings.groundNormalSharpness <= 0f)
        {
            groundNormal = targetNormal;
            return;
        }

        float t = 1f - Mathf.Exp(-settings.groundNormalSharpness * Mathf.Max(0f, deltaTime));
        groundNormal = Vector3.Slerp(groundNormal, targetNormal, t).normalized;
    }

    private void UpdatePlatformTracking(Transform groundedPlatform, float deltaTime)
    {
        if (groundedPlatform == null)
        {
            if (currentPlatform != null)
                DetachFromPlatformInternal(true, true);
            return;
        }

        if (currentPlatform != groundedPlatform)
        {
            AttachToPlatform(groundedPlatform);
            return;
        }

        Vector3 nextWorldAnchor = currentPlatform.TransformPoint(platformLocalAnchor);
        pendingPlatformDisplacement = nextWorldAnchor - platformWorldAnchor;
        pendingPlatformRotation = currentPlatform.rotation * Quaternion.Inverse(platformRotation);
        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        platformVelocity = safeDeltaTime > 0.00001f
            ? pendingPlatformDisplacement / safeDeltaTime
            : Vector3.zero;
        platformWorldAnchor = nextWorldAnchor;
        platformRotation = currentPlatform.rotation;
    }

    private void ReanchorPlatform()
    {
        if (currentPlatform == null)
            return;

        platformLocalAnchor = currentPlatform.InverseTransformPoint(transform.position);
        platformWorldAnchor = currentPlatform.TransformPoint(platformLocalAnchor);
        platformRotation = currentPlatform.rotation;
    }

    private void DetachFromPlatformInternal(bool inheritVelocity, bool clearPendingMotion)
    {
        if (inheritVelocity && platformVelocity.sqrMagnitude > 0.000001f)
            AddExternalForce(platformVelocity);

        currentPlatform = null;
        platformLocalAnchor = Vector3.zero;
        platformWorldAnchor = Vector3.zero;
        platformRotation = Quaternion.identity;
        platformVelocity = Vector3.zero;
        if (clearPendingMotion)
        {
            pendingPlatformDisplacement = Vector3.zero;
            pendingPlatformRotation = Quaternion.identity;
        }
    }

    private void ApplyPlatformRotation(Quaternion rotationDelta)
    {
        if (rotationDelta == Quaternion.identity)
            return;

        float yaw = rotationDelta.eulerAngles.y;
        if (yaw > 180f)
            yaw -= 360f;
        if (Mathf.Abs(yaw) <= 0.0001f)
            return;

        transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * transform.rotation;
    }

    private void ResolveExternalCollisions(CollisionFlags flags)
    {
        if ((flags & CollisionFlags.Above) != 0 && externalVelocity.y > 0f)
            externalVelocity.y = 0f;
        if ((flags & CollisionFlags.Below) != 0 && externalVelocity.y < 0f)
            externalVelocity.y = 0f;
    }

    private void DecayExternalVelocity(float deltaTime)
    {
        float decay = Mathf.Max(0f, settings.externalVelocityDecay);
        if (decay <= 0f || deltaTime <= 0f)
            return;
        externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero, decay * deltaTime);
    }

    private bool HasExpansionHeadroom(
        float currentHeight,
        Vector3 currentCenter,
        float targetHeight,
        Vector3 targetCenter)
    {
        int currentCount = OverlapControllerCapsule(
            currentHeight,
            currentCenter,
            currentHeightOverlaps);
        int targetCount = OverlapControllerCapsule(
            targetHeight,
            targetCenter,
            targetHeightOverlaps);
        if (currentCount >= currentHeightOverlaps.Length
            || targetCount >= targetHeightOverlaps.Length)
        {
            return false;
        }

        for (int i = 0; i < targetCount; i++)
        {
            Collider candidate = targetHeightOverlaps[i];
            if (candidate == null
                || candidate.transform == transform
                || candidate.transform.IsChildOf(transform))
            {
                continue;
            }

            bool alreadyOverlapping = false;
            for (int j = 0; j < currentCount; j++)
            {
                if (currentHeightOverlaps[j] == candidate)
                {
                    alreadyOverlapping = true;
                    break;
                }
            }

            if (!alreadyOverlapping)
                return false;
        }

        return true;
    }

    private int OverlapControllerCapsule(float height, Vector3 center, Collider[] results)
    {
        Vector3 scale = transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        float heightScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float radius = Mathf.Max(0.01f, controller.radius * radiusScale - controller.skinWidth);
        float worldHeight = Mathf.Max(radius * 2f, height * heightScale);
        float halfSegment = Mathf.Max(0f, worldHeight * 0.5f - radius);
        Vector3 worldCenter = transform.TransformPoint(center);
        Vector3 up = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        return Physics.OverlapCapsuleNonAlloc(
            worldCenter + up * halfSegment,
            worldCenter - up * halfSegment,
            radius,
            results,
            ~0,
            QueryTriggerInteraction.Ignore);
    }

    private void ConfigureCharacterControllerFromCapsule()
    {
        if (controller == null)
            return;

        if (legacyCapsuleCollider != null)
        {
            controller.center = legacyCapsuleCollider.center;
            controller.radius = legacyCapsuleCollider.radius;
            controller.height = legacyCapsuleCollider.height;
        }

        ConfigureCharacterControllerSettings();
    }

    private void ConfigureCharacterControllerSettings()
    {
        if (controller == null)
            return;

        float skinWidthRatio = settings.skinWidthRadiusRatio > 0f
            ? settings.skinWidthRadiusRatio
            : DefaultSkinWidthRadiusRatio;
        controller.skinWidth = Mathf.Max(0.01f, controller.radius * skinWidthRatio);
        controller.slopeLimit = Mathf.Max(0f, settings.slopeLimit);
        controller.stepOffset = Mathf.Max(0f, settings.stepOffset);
        controller.minMoveDistance = Mathf.Max(0f, settings.minMoveDistance);
    }

    private void ConfigureLegacyPhysicsComponents()
    {
        if (legacyCapsuleCollider != null && !legacyCapsuleCollider.isTrigger)
            legacyCapsuleCollider.enabled = false;

        if (legacyRigidbody == null)
            return;

        // 이미 kinematic인 Rigidbody에 velocity를 쓰면 Unity 6가 매 프레임 경고를 낸다.
        // 전환 직전의 동적 상태에서만 잔여 속도를 비운다.
        if (!legacyRigidbody.isKinematic)
        {
            legacyRigidbody.linearVelocity = Vector3.zero;
            legacyRigidbody.angularVelocity = Vector3.zero;
        }
        legacyRigidbody.useGravity = false;
        legacyRigidbody.isKinematic = true;
        legacyRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    // 모든 플레이어 변위는 이 한 지점에서만 CharacterController에 적용한다.
    // 이동/회피/공격 루트 모션의 정책은 호출자가 유지하되 실제 충돌 이동 소유자는 모터 하나다.
    private CollisionFlags ExecuteMove(Vector3 displacement)
    {
        if (controller == null || !controller.enabled)
            return CollisionFlags.None;

        CollisionFlags flags = controller.Move(displacement);
        CombatTargetRegistry.NotifySpatialChanged(transform);
        return flags;
    }

    private static Vector3 Planar(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.001f ? value.normalized * value.magnitude : Vector3.zero;
    }
}
