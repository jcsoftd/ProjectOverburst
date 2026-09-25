using UnityEngine;

[DefaultExecutionOrder(520)]
public class QuarterViewCamera : MonoBehaviour // 쿼터뷰 카메라
{
    public const float DefaultYaw = 45f;
    public static QuarterViewCamera ActiveInstance { get; private set; }
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 targetOffset;

    [Header("View")]
    [SerializeField] private float distance = 20f;
    [SerializeField] private float pitch = 55f;
    // 방향은 기본 대각선 시점과 씬 전환 SetYaw에서만 결정한다.
    private float yaw = DefaultYaw;

    [Header("Mouse Wheel Zoom")]
    [SerializeField] private bool enableMouseWheelZoom = true;
    [SerializeField] private float zoomSpeed = 1.25f;
    [SerializeField] private float minDistance = 6f;
    [SerializeField] private float maxZoomDistance = 28f;
    [SerializeField] private float defaultZoomDistance = 20f;
    [SerializeField] private float zoomSharpness = 18f;

    [Header("Close-up Framing")]
    [Tooltip("이 거리보다 가까워지면 상반신 구도로 부드럽게 전환한다.")]
    [SerializeField, Min(0f)] private float closeUpStartDistance = 14f;
    [SerializeField, Range(0f, 89f)] private float closeUpPitch = 12f;
    [Tooltip("최대 확대에서 기본 targetOffset에 더할 상체 시선 높이 (월드 단위).")]
    [SerializeField, Min(0f)] private float closeUpFocusHeight = 1.3f;
    [Tooltip("최대 확대에서 기존 줌 화면 크기에 적용할 배율.")]
    [SerializeField, Range(0.05f, 1f)] private float closeUpFrameScale = 0.32f;

    [Header("Follow Smoothing")]
    [SerializeField] private float followSharpness = 24f;
    [SerializeField] private float targetSwitchSharpness = 7.5f;
    [SerializeField] private float targetSwitchCompleteDistance = 0.03f;

    [Header("Cinemachine 3 Adapter")]
    [SerializeField] private OverburstCinemachineCameraRig cinemachineRig;

    private Vector3 focusPosition; // 추적 위치
    private bool hasFocusPosition; // 추적 초기화
    private bool smoothingTargetSwitch;
    private float targetDistance; // 목표 거리
    private float impactEndUnscaledTime; // 타격 카메라 종료
    private float impactDuration; // 타격 카메라 길이
    private float impactPositionAmplitude; // 위치 충격량
    private float impactRollAmplitude; // 회전 충격량
    private Vector2 impactDirection; // 화면 기준 충격 방향
    private float impactPriority;
    private float impactKickStartUnscaledTime;
    private float impactKickDuration;
    private float impactKickAmplitude;
    private float impactMicroShakeDuration;
    private float impactMicroShakeStartUnscaledTime;
    private float impactMicroShakeAmplitude;
    private float impactPositionSafetyLimit;
    private float impactRollSafetyLimit;
    private CombatCameraRequestKind activeImpactKind;
    private float queuedGroundStepAmplitude;
    private float queuedGroundStepDuration;
    private float lastGroundStepUnscaledTime = float.NegativeInfinity;
    private float lastGroundStepAmplitude;
    [SerializeField, Range(0f, 1f)] private float groundStepCameraStrength = 1f;
    public int GroundStepEmissionCount { get; private set; }
    public float GroundStepCameraStrength
    {
        get => groundStepCameraStrength;
        set => groundStepCameraStrength = Mathf.Clamp01(value);
    }
    private Camera cachedCamera;
    private bool forceCameraCut;

    public Transform CurrentTarget => target;
    public float CurrentDistance => distance;
    public float CurrentYaw => yaw;
    public float CloseUpBlend
    {
        get
        {
            float start = Mathf.Clamp(closeUpStartDistance, minDistance, maxZoomDistance);
            if (start <= minDistance)
                return 0f;
            float blend = Mathf.InverseLerp(start, minDistance, distance);
            return Mathf.SmoothStep(0f, 1f, blend);
        }
    }
    public float CurrentPitch => Mathf.Lerp(pitch, closeUpPitch, CloseUpBlend);
    public OverburstCinemachineCameraRig CinemachineRig => cinemachineRig;
    public bool UsesCinemachine => cinemachineRig != null && cinemachineRig.IsConfigured;

    private void Awake()
    {
        targetDistance = Mathf.Clamp(distance, minDistance, maxZoomDistance); // 줌 초기값
        cachedCamera = GetComponent<Camera>(); // 카메라 캐시
        if (cinemachineRig == null)
            cinemachineRig = FindFirstObjectByType<OverburstCinemachineCameraRig>(FindObjectsInactive.Include);
    }

    private void OnEnable() => ActiveInstance = this;

    private void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
        queuedGroundStepAmplitude = 0f;
        queuedGroundStepDuration = 0f;
        cinemachineRig?.CancelCombatImpact();
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        UpdateZoomInput();
        UpdateZoomDistance();
        UpdateFocusPosition();
        ApplyCameraTransform();
        FlushGroundStepRequest();
        ApplyCombatImpact();
        if (UsesCinemachine)
            cinemachineRig.RenderNow();
    }

    public void SetTarget(Transform newTarget)
    {
        bool canSmoothSwitch = target != null && newTarget != null && target != newTarget && hasFocusPosition;
        target = newTarget; // 추적 대상

        if (canSmoothSwitch)
        {
            smoothingTargetSwitch = true;
            return;
        }

        smoothingTargetSwitch = false;
        hasFocusPosition = false; // 위치 재초기화
        forceCameraCut = true;
    }

    public void SetYaw(float newYaw)
    {
        yaw = newYaw; // yaw 고정
        hasFocusPosition = false; // 즉시 재정렬
        forceCameraCut = true;
    }

    public void RequestCombatImpact(
        CombatCameraRequestKind requestKind,
        Vector3 worldDirection,
        Vector3 directionOverride,
        bool hasDirectionOverride,
        float duration,
        float positionAmplitude,
        float rollAmplitude,
        float returnRatio,
        float microShakeDuration,
        float microShakeAmplitude,
        float priority,
        float positionSafetyLimit,
        float rollSafetyLimit)
    {
        float resolvedDuration = Mathf.Max(0f, duration);
        float resolvedPositionAmplitude = Mathf.Max(0f, positionAmplitude);
        float resolvedRollAmplitude = Mathf.Max(0f, rollAmplitude);
        if (resolvedDuration <= 0f
            || (resolvedPositionAmplitude <= 0f && resolvedRollAmplitude <= 0f))
        {
            return;
        }

        float now = Time.unscaledTime;
        float incomingStrength = resolvedPositionAmplitude + resolvedRollAmplitude * 0.1f;
        float activeStrength = impactPositionAmplitude + impactRollAmplitude * 0.1f;
        bool active = now < impactEndUnscaledTime;
        bool lowerPriority = priority < impactPriority;
        bool samePriorityWeaker = Mathf.Approximately(priority, impactPriority) && incomingStrength < activeStrength;
        if (active && (lowerPriority || samePriorityWeaker))
        {
            impactMicroShakeAmplitude = Mathf.Max(impactMicroShakeAmplitude, resolvedPositionAmplitude * Mathf.Clamp01(microShakeAmplitude));
            if (UsesCinemachine)
            {
                cinemachineRig.BoostCombatMicroShake(
                    resolvedPositionAmplitude,
                    microShakeAmplitude,
                    positionSafetyLimit);
            }
            return; // 약한 요청은 잔진동만 제한 합산
        }

        impactDuration = resolvedDuration;
        impactEndUnscaledTime = now + resolvedDuration;
        impactPositionAmplitude = resolvedPositionAmplitude;
        impactRollAmplitude = resolvedRollAmplitude;
        impactPriority = Mathf.Max(0f, priority);
        activeImpactKind = requestKind;
        impactKickStartUnscaledTime = now;
        impactKickDuration = resolvedDuration * Mathf.Clamp01(returnRatio);
        impactKickAmplitude = resolvedPositionAmplitude;
        impactMicroShakeDuration = Mathf.Max(0f, microShakeDuration);
        impactMicroShakeAmplitude = resolvedPositionAmplitude * Mathf.Clamp01(microShakeAmplitude);
        impactMicroShakeStartUnscaledTime = now + Mathf.Max(0f, resolvedDuration - impactMicroShakeDuration);
        impactPositionSafetyLimit = Mathf.Max(0f, positionSafetyLimit);
        impactRollSafetyLimit = Mathf.Max(0f, rollSafetyLimit);
        Vector3 direction = hasDirectionOverride ? directionOverride : worldDirection;
        if (direction.sqrMagnitude <= 0.0001f)
            impactDirection = Vector2.right;
        else
        {
            direction.Normalize();
            Vector2 projected = new Vector2(Vector3.Dot(direction, transform.right), Vector3.Dot(direction, transform.up));
            impactDirection = projected.sqrMagnitude > 0.0001f ? projected.normalized : Vector2.right;
        }

        if (UsesCinemachine)
        {
            cinemachineRig.EmitCombatImpact(
                requestKind,
                impactDirection,
                resolvedDuration,
                resolvedPositionAmplitude,
                resolvedRollAmplitude,
                returnRatio,
                impactMicroShakeDuration,
                microShakeAmplitude,
                impactPositionSafetyLimit,
                impactRollSafetyLimit);
        }
    }

    // Footfalls are queued until the camera update so several elites can submit at
    // once. They never enter the lower-priority microshake accumulation path.
    public void QueueGroundStep(float positionAmplitude, float duration = .09f)
    {
        if (groundStepCameraStrength <= 0f || !isActiveAndEnabled)
            return;
        float scaled = Mathf.Clamp(positionAmplitude, 0f, .05f) * groundStepCameraStrength;
        if (scaled <= queuedGroundStepAmplitude) return;
        queuedGroundStepAmplitude = scaled;
        queuedGroundStepDuration = Mathf.Clamp(duration, .08f, .16f);
    }

    private void FlushGroundStepRequest()
    {
        float amplitude = queuedGroundStepAmplitude;
        float duration = queuedGroundStepDuration;
        queuedGroundStepAmplitude = 0f;
        queuedGroundStepDuration = 0f;
        if (amplitude <= 0f)
            return;

        float now = Time.unscaledTime;
        bool impactActive = now < impactEndUnscaledTime;
        if (impactActive && activeImpactKind != CombatCameraRequestKind.GroundStep)
            return;
        if (now - lastGroundStepUnscaledTime < 0.24f && amplitude <= lastGroundStepAmplitude)
            return;
        if (impactActive && amplitude <= impactPositionAmplitude)
            return;

        RequestCombatImpact(CombatCameraRequestKind.GroundStep, transform.up, transform.up, true,
            duration, amplitude, 0f, 0.78f, 0f, 0f, 0f, .05f, 0f);
        lastGroundStepUnscaledTime = now;
        lastGroundStepAmplitude = amplitude;
        GroundStepEmissionCount++;
    }

    private void UpdateZoomInput()
    {
        if (!enableMouseWheelZoom || GameplayInputBlocker.IsGameplayInputBlocked)
            return;

        // GOAL A2: 휠 직접 읽기 대신 Gameplay Zoom 값을 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null)
            return;

        // 클릭과 스크롤이 겹치면 기본 구도 복귀를 우선한다.
        if (facade.ZoomResetPressedThisFrame)
        {
            ResetZoom();
            return;
        }

        float scroll = facade.ZoomValue.y; // 휠 입력
        if (Mathf.Abs(scroll) <= 0.01f)
            return;

        targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * zoomSpeed, minDistance, maxZoomDistance); // 목표 줌
    }

    public void ResetZoom()
    {
        targetDistance = Mathf.Clamp(defaultZoomDistance, minDistance, maxZoomDistance);
    }

    private void UpdateZoomDistance()
    {
        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxZoomDistance); // 범위 보정
        float t = 1f - Mathf.Exp(-Mathf.Max(0f, zoomSharpness) * Time.deltaTime); // 보간값
        distance = Mathf.Lerp(distance, targetDistance, t); // 거리 보간
    }

    private void UpdateFocusPosition()
    {
        Vector3 targetPosition = target.position + targetOffset; // 목표 위치

        if (!hasFocusPosition)
        {
            focusPosition = targetPosition; // 즉시 이동
            hasFocusPosition = true; // 초기화 완료
            return;
        }

        float sharpness = smoothingTargetSwitch ? targetSwitchSharpness : followSharpness;
        if (sharpness <= 0f)
        {
            focusPosition = targetPosition;
            smoothingTargetSwitch = false;
            return;
        }

        float t = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
        focusPosition = Vector3.Lerp(focusPosition, targetPosition, t); // 추적 위치

        if (smoothingTargetSwitch && (targetPosition - focusPosition).sqrMagnitude <= targetSwitchCompleteDistance * targetSwitchCompleteDistance)
        {
            focusPosition = targetPosition;
            smoothingTargetSwitch = false;
        }
    }

    private void ApplyCameraTransform()
    {
        // 이미 보간된 줌 거리 하나로 각도/시선/화면 크기를 함께 계산한다.
        // 이동 추적 위치에 누적하지 않아 확대/축소 반전에서도 구도가 뒤늦게 따라오지 않는다.
        float blend = CloseUpBlend;
        float viewPitch = Mathf.Lerp(pitch, closeUpPitch, blend);
        Vector3 viewFocus = focusPosition + Vector3.up * (closeUpFocusHeight * blend);
        float frameScale = Mathf.Lerp(1f, closeUpFrameScale, blend);
        if (UsesCinemachine)
        {
            cinemachineRig.SynchronizeView(viewFocus, viewPitch, yaw, distance, forceCameraCut, frameScale);
            forceCameraCut = false;
            return;
        }

        Quaternion viewRotation = Quaternion.Euler(viewPitch, yaw, 0f); // 뷰 회전
        Vector3 cameraOffset = viewRotation * Vector3.back * Mathf.Max(0.01f, distance * frameScale);
        Vector3 cameraPosition = viewFocus + cameraOffset;
        if (cachedCamera != null && cachedCamera.orthographic)
            cachedCamera.orthographicSize = Mathf.Max(0.01f, distance * frameScale * Mathf.Tan(cachedCamera.fieldOfView * 0.5f * Mathf.Deg2Rad));

        transform.position = cameraPosition;
        transform.rotation = viewRotation;
    }

    private void ApplyCombatImpact()
    {
        float now = Time.unscaledTime;
        if (now >= impactEndUnscaledTime || impactDuration <= 0f)
            return;

        float progress = impactKickDuration <= 0f
            ? 1f
            : Mathf.Clamp01((now - impactKickStartUnscaledTime) / impactKickDuration);
        float envelope = 1f - Mathf.SmoothStep(0f, 1f, progress);
        if (UsesCinemachine)
            return;

        float screenScale = Mathf.Tan((cachedCamera != null ? cachedCamera.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad)
            * Mathf.Max(0.01f, distance) * 0.1f;
        Vector3 positionOffset = (
            transform.right * impactDirection.x
            + transform.up * impactDirection.y)
            * impactKickAmplitude
            * screenScale
            * envelope;
        if (impactMicroShakeDuration > 0f && now >= impactMicroShakeStartUnscaledTime && now < impactEndUnscaledTime)
        {
            float shakeProgress = Mathf.Clamp01((now - impactMicroShakeStartUnscaledTime) / impactMicroShakeDuration);
            float shakeEnvelope = (1f - shakeProgress) * impactMicroShakeAmplitude * screenScale;
            positionOffset += (transform.right * impactDirection.y - transform.up * impactDirection.x)
                * Mathf.Sin(now * 90f) * shakeEnvelope;
        }
        positionOffset = Vector3.ClampMagnitude(positionOffset, impactPositionSafetyLimit * screenScale);
        transform.position += positionOffset;
        transform.rotation *= Quaternion.AngleAxis(
            Mathf.Clamp(impactRollAmplitude * impactDirection.x * envelope, -impactRollSafetyLimit, impactRollSafetyLimit),
            Vector3.forward);
    }
}
