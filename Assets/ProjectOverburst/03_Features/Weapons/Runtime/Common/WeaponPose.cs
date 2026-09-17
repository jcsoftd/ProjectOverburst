using System.Collections;
using UnityEngine;

public enum WeaponPoseSlot // 무기 포즈 슬롯
{
    Hold,
    Back,
    Aim,
    Guard
}

public class WeaponPose : MonoBehaviour // 무기 포즈 제어
{
    [Header("References")]
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private Transform poseTarget;
    [SerializeField] private WeaponGripMount gripMount;

    [Header("Update")]
    [SerializeField] private bool autoUpdate = true;

    [Header("Hold Pose")]
    [SerializeField] private Vector3 holdLocalPosition = new Vector3(-0.02169801f, 0.01338662f, -0.0154142f);
    [SerializeField] private Vector3 holdLocalRotation = new Vector3(-1.95f, 0f, 0f);

    [Header("Back Floating Pose")]
    [SerializeField] private bool useBackFloatingPose = true;
    [SerializeField] private Vector3 backAnchorLocalPosition = new Vector3(-0.18f, 1.28f, -0.32f);
    [SerializeField] private Vector3 backAnchorLocalRotation = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 backLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 backLocalRotation = new Vector3(18f, 100f, 78f);

    [Header("Aim Pose")]
    [SerializeField] private Vector3 aimLocalPosition = new Vector3(-0.036f, 0.003f, -0.012f);
    [SerializeField] private Vector3 aimLocalRotation = new Vector3(-17.242f, 0f, 0f);

    [Header("Guard Pose")]
    [SerializeField] private Vector3 guardLocalPosition = new Vector3(-0.036f, 0.003f, -0.012f);
    [SerializeField] private Vector3 guardLocalRotation = new Vector3(-17.242f, 0f, 0f);

    [Header("Smooth")]
    [SerializeField] private float poseChangeSpeed = 14f;
    [SerializeField] private bool snapWhenReturningToBack = true;
    [SerializeField] private bool enableBackFloatLag = true;
    [SerializeField] private float backFollowSmoothTime = 0.08f;
    [SerializeField] private float backFloatMaxLagDistance = 0.12f;
    [SerializeField] private float backFloatLagStrength = 0.8f;
    [SerializeField] private float backFloatBobAmount = 0.015f;
    [SerializeField] private float backFloatBobSpeed = 2.2f;

    [Header("Fade")]
    [SerializeField] private bool enableWeaponFade = true;
    [SerializeField] private float fadeInSpeed = 12f;
    [SerializeField] private float fadeOutSpeed = 18f;

    private Transform inHandParent; // 손 parent
    private Transform backAnchor; // 등 anchor
    private float activePoseUntil; // 활성 포즈 시간
    private float quickFirePoseUntil; // 퀵파이어 포즈 시간
    private Vector3 previousBackAnchorPosition; // 이전 anchor 위치
    private Vector3 backLagLocalOffset; // 등 lag offset
    private Vector3 backLagVelocity; // 등 lag 속도
    private bool hasBackAnchorPosition; // 위치 캐시
    private Renderer[] weaponRenderers; // 렌더러 캐시
    private MaterialPropertyBlock[] materialPropertyBlocks; // 페이드 차단
    private Color[] baseRendererColors; // 원본 색
    private float currentFadeAlpha; // 현재 alpha
    private bool fadingOut; // fade out 중
    private bool missingBackAnchorWarningLogged; // anchor 누락 경고
    private bool wasUsingActivePose;
    private const string BaseColorProperty = "_BaseColor"; // URP 색상
    private const string ColorProperty = "_Color"; // 기본 색상

    private void Awake()
    {
        if (poseTarget == null)
            poseTarget = transform;

        ResolveGripMount();

        inHandParent = poseTarget.parent;

        if (playerController == null)
            playerController = GetComponentInParent<PlayerMovement>();

        if (playerEquipment == null)
            playerEquipment = GetComponentInParent<PlayerEquipment>();

        EnsureBackAnchor(); // 등 anchor
        CacheRenderers(); // renderer 캐시
        currentFadeAlpha = enableWeaponFade ? 0f : 1f; // 초기 alpha
        ApplyRendererFade(currentFadeAlpha); // alpha 적용
    }

    private void Start()
    {
        SnapToCurrentPose();
        wasUsingActivePose = ShouldUseActiveWeaponPose();
    }

    private void LateUpdate()
    {
        if (!autoUpdate)
            return;

        ApplyPoseFrame();
    }

    public void SetAutoUpdate(bool value)
    {
        autoUpdate = value;
    }

    public void ApplyPoseFrame()
    {
        if (ResolvePoseTarget() == null)
            return;

        bool useActivePose = ShouldUseActiveWeaponPose(); // 활성 포즈
        ApplyPose(useActivePose); // 포즈 적용
        UpdateFade(!fadingOut); // fade 갱신
    }

    public void FadeOutAndDestroy()
    {
        FadeOutAndDestroy(gameObject);
    }

    public void FadeOutAndDestroy(GameObject objectToDestroy)
    {
        if (!Application.isPlaying || !enableWeaponFade || weaponRenderers == null || weaponRenderers.Length == 0)
        {
            Destroy(objectToDestroy != null ? objectToDestroy : gameObject); // 즉시 제거
            return;
        }

        StartCoroutine(FadeOutAndDestroyRoutine(objectToDestroy != null ? objectToDestroy : gameObject)); // fade 제거
    }

    public void BeginQuickFirePose(float holdTime)
    {
        BeginActivePose(holdTime);
        quickFirePoseUntil = Mathf.Max(quickFirePoseUntil, Time.time + Mathf.Max(0f, holdTime)); // 퀵파이어 유지
    }

    public void BeginActivePose(float holdTime)
    {
        activePoseUntil = Mathf.Max(activePoseUntil, Time.time + Mathf.Max(0f, holdTime)); // 활성 포즈 유지

        if (ResolvePoseTarget() == null)
            return;

        WeaponPoseSlot activeSlot = GetCurrentActivePoseSlot();
        UpdatePoseParent(true); // 손 parent
        ApplyPoseInstant(activeSlot);
        ResetBackLag();
        wasUsingActivePose = true;
    }

    public void PreviewPoseInstant(WeaponPoseSlot poseSlot)
    {
        if (ResolvePoseTarget() == null)
            return;

        bool useActiveParent = poseSlot != WeaponPoseSlot.Back;
        UpdatePoseParent(useActiveParent);
        ApplyPoseInstant(poseSlot);
        ResetBackLag();
    }

    public void CaptureCurrentTransformAsPose(WeaponPoseSlot poseSlot)
    {
        if (ResolvePoseTarget() == null)
            return;

        ResolveGripMount();
        WeaponGripAnchor gripAnchor = GetGripAnchor(poseSlot);
        if (gripMount != null
            && gripMount.TryGetCurrentGripPose(
                poseTarget,
                gripAnchor,
                out Vector3 gripPosition,
                out Quaternion gripRotation))
        {
            SetPoseLocalPosition(poseSlot, gripPosition);
            SetPoseLocalRotation(poseSlot, NormalizeEuler(gripRotation.eulerAngles));
            return;
        }

        SetPoseLocalPosition(poseSlot, poseTarget.localPosition);
        SetPoseLocalRotation(poseSlot, NormalizeEuler(poseTarget.localEulerAngles));
    }

    public Transform GetPoseTargetForTuning()
    {
        return ResolvePoseTarget();
    }

    public Vector3 GetPoseLocalPositionForTuning(WeaponPoseSlot poseSlot)
    {
        return GetPoseLocalPosition(poseSlot);
    }

    public Vector3 GetPoseLocalRotationForTuning(WeaponPoseSlot poseSlot)
    {
        return GetPoseLocalRotation(poseSlot);
    }

    public void SetPoseForTuning(WeaponPoseSlot poseSlot, Vector3 localPosition, Vector3 localRotation)
    {
        SetPoseLocalPosition(poseSlot, localPosition);
        SetPoseLocalRotation(poseSlot, NormalizeEuler(localRotation));
        PreviewPoseInstant(poseSlot);
    }

    private void ApplyPose(bool useActivePose)
    {
        UpdatePoseParent(useActivePose); // parent 선택

        WeaponPoseSlot activeSlot = GetCurrentActivePoseSlot();
        if (!useActivePose && wasUsingActivePose && snapWhenReturningToBack && useBackFloatingPose)
        {
            ApplyBackPoseInstant();
            wasUsingActivePose = false;
            return;
        }

        Vector3 desiredGripPosition = useActivePose || !useBackFloatingPose ? (useActivePose ? GetPoseLocalPosition(activeSlot) : holdLocalPosition) : GetBackFloatingTargetPosition();
        Vector3 desiredGripEuler = useActivePose || !useBackFloatingPose ? (useActivePose ? GetPoseLocalRotation(activeSlot) : holdLocalRotation) : backLocalRotation;
        WeaponGripAnchor gripAnchor = useActivePose || !useBackFloatingPose
            ? WeaponGripAnchor.RightHand
            : WeaponGripAnchor.Back;
        ResolveRootLocalPose(gripAnchor, desiredGripPosition, desiredGripEuler, out Vector3 targetPosition, out Quaternion targetRotation);
        float t = 1f - Mathf.Exp(-poseChangeSpeed * Time.deltaTime); // 보간값

        poseTarget.localPosition = Vector3.Lerp(poseTarget.localPosition, targetPosition, t); // 위치 보간
        poseTarget.localRotation = Quaternion.Slerp(poseTarget.localRotation, targetRotation, t); // 회전 보간

        if (useActivePose)
            ResetBackLag(); // lag 초기화

        wasUsingActivePose = useActivePose;
    }

    private void ApplyBackPoseInstant()
    {
        UpdatePoseParent(false);
        ResetBackLag();
        ApplyPoseInstant(WeaponPoseSlot.Back);
    }

    public void SnapToCurrentPose()
    {
        if (ResolvePoseTarget() == null)
            return;

        bool useActivePose = ShouldUseActiveWeaponPose();
        WeaponPoseSlot activeSlot = GetCurrentActivePoseSlot();
        UpdatePoseParent(useActivePose);

        WeaponPoseSlot poseSlot = useActivePose ? activeSlot : useBackFloatingPose ? WeaponPoseSlot.Back : WeaponPoseSlot.Hold;
        ApplyPoseInstant(poseSlot);
        ResetBackLag();
        wasUsingActivePose = useActivePose;
    }

    private WeaponPoseSlot GetCurrentActivePoseSlot()
    {
        if (playerController != null
            && playerController.IsMeleeGuarding)
            return WeaponPoseSlot.Guard;

        return WeaponPoseSlot.Aim;
    }

    private bool ShouldUseActiveWeaponPose()
    {
        bool sharedCombatModeActive = PlayerCombatModeController.IsSharedCombatModeActive();

        if (GameplayInputBlocker.IsGameplayInputBlocked && !sharedCombatModeActive)
            return false; // UI 차단

        if (sharedCombatModeActive)
            return true;

        if (playerController != null && playerController.IsWeaponAimInputActive)
            return true; // 조준/자세 포즈

        if (Time.time < quickFirePoseUntil)
            return true; // 퀵파이어 포즈

        if (Time.time < activePoseUntil)
            return true; // 활성 포즈

        return false;
    }

    private void UpdatePoseParent(bool useActivePose)
    {
        if (ResolvePoseTarget() == null || !useBackFloatingPose)
            return;

        Transform targetParent = useActivePose ? inHandParent : backAnchor; // parent 선택

        if (targetParent == null || poseTarget.parent == targetParent)
            return;

        poseTarget.SetParent(targetParent, true); // world pose 유지
    }

    private void EnsureBackAnchor()
    {
        if (!useBackFloatingPose)
            return;

        Transform playerRoot = playerController != null ? playerController.transform : transform.root;
        if (playerRoot == null)
            return;

        backAnchor = FindDirectChild(playerRoot, "BackWeaponAnchor");

        if (backAnchor == null)
        {
            if (!missingBackAnchorWarningLogged)
            {
                Debug.LogWarning("BackWeaponAnchor is missing. Create it as a direct child of the player root.", this);
                missingBackAnchorWarningLogged = true;
            }

            return;
        }

        missingBackAnchorWarningLogged = false;
    }

    private Transform ResolvePoseTarget()
    {
        if (poseTarget == null)
            poseTarget = transform;

        if (inHandParent == null && poseTarget != null)
            inHandParent = poseTarget.parent;

        if (playerController == null)
            playerController = GetComponentInParent<PlayerMovement>();

        if (playerEquipment == null)
            playerEquipment = GetComponentInParent<PlayerEquipment>();

        ResolveGripMount();
        EnsureBackAnchor();
        return poseTarget;
    }

    private void ApplyPoseInstant(WeaponPoseSlot poseSlot)
    {
        ResolveRootLocalPose(
            GetGripAnchor(poseSlot),
            GetPoseLocalPosition(poseSlot),
            GetPoseLocalRotation(poseSlot),
            out Vector3 rootPosition,
            out Quaternion rootRotation);
        poseTarget.localPosition = rootPosition;
        poseTarget.localRotation = rootRotation;
    }

    private void ResolveRootLocalPose(
        WeaponGripAnchor gripAnchor,
        Vector3 desiredGripPosition,
        Vector3 desiredGripEuler,
        out Vector3 rootPosition,
        out Quaternion rootRotation)
    {
        Quaternion desiredGripRotation = Quaternion.Euler(desiredGripEuler);
        ResolveGripMount();
        if (gripMount != null
            && gripMount.TryResolveRootLocalPose(
                poseTarget,
                gripAnchor,
                desiredGripPosition,
                desiredGripRotation,
                out rootPosition,
                out rootRotation))
        {
            return;
        }

        rootPosition = desiredGripPosition;
        rootRotation = desiredGripRotation;
    }

    private WeaponGripAnchor GetGripAnchor(WeaponPoseSlot poseSlot)
    {
        return poseSlot == WeaponPoseSlot.Back
            ? WeaponGripAnchor.Back
            : WeaponGripAnchor.RightHand;
    }

    private void ResolveGripMount()
    {
        if (gripMount != null)
            return;

        gripMount = GetComponent<WeaponGripMount>();
        if (gripMount == null)
            gripMount = GetComponentInChildren<WeaponGripMount>(true);
    }

    private Vector3 GetPoseLocalPosition(WeaponPoseSlot poseSlot)
    {
        switch (poseSlot)
        {
            case WeaponPoseSlot.Back:
                return backLocalPosition;
            case WeaponPoseSlot.Aim:
                return aimLocalPosition;
            case WeaponPoseSlot.Guard:
                return guardLocalPosition;
            default:
                return holdLocalPosition;
        }
    }

    private Vector3 GetPoseLocalRotation(WeaponPoseSlot poseSlot)
    {
        switch (poseSlot)
        {
            case WeaponPoseSlot.Back:
                return backLocalRotation;
            case WeaponPoseSlot.Aim:
                return aimLocalRotation;
            case WeaponPoseSlot.Guard:
                return guardLocalRotation;
            default:
                return holdLocalRotation;
        }
    }

    private void SetPoseLocalPosition(WeaponPoseSlot poseSlot, Vector3 localPosition)
    {
        switch (poseSlot)
        {
            case WeaponPoseSlot.Back:
                backLocalPosition = localPosition;
                break;
            case WeaponPoseSlot.Aim:
                aimLocalPosition = localPosition;
                break;
            case WeaponPoseSlot.Guard:
                guardLocalPosition = localPosition;
                break;
            default:
                holdLocalPosition = localPosition;
                break;
        }
    }

    private void SetPoseLocalRotation(WeaponPoseSlot poseSlot, Vector3 localRotation)
    {
        switch (poseSlot)
        {
            case WeaponPoseSlot.Back:
                backLocalRotation = localRotation;
                break;
            case WeaponPoseSlot.Aim:
                aimLocalRotation = localRotation;
                break;
            case WeaponPoseSlot.Guard:
                guardLocalRotation = localRotation;
                break;
            default:
                holdLocalRotation = localRotation;
                break;
        }
    }

    private Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }

    private Vector3 GetBackFloatingTargetPosition()
    {
        if (!enableBackFloatLag || backAnchor == null)
            return backLocalPosition; // 기본 위치

        Vector3 anchorPosition = backAnchor.position;

        if (!hasBackAnchorPosition)
        {
            previousBackAnchorPosition = anchorPosition; // 기준 위치
            hasBackAnchorPosition = true; // 캐시 완료
        }

        Vector3 worldDelta = anchorPosition - previousBackAnchorPosition; // 이동량
        previousBackAnchorPosition = anchorPosition; // 이전값 갱신
        Vector3 targetLag = backAnchor.InverseTransformVector(-worldDelta * Mathf.Max(0f, backFloatLagStrength));
        targetLag = Vector3.ClampMagnitude(targetLag, Mathf.Max(0f, backFloatMaxLagDistance)); // lag 제한

        float smoothTime = Mathf.Max(0.001f, backFollowSmoothTime);
        backLagLocalOffset = Vector3.SmoothDamp(backLagLocalOffset, targetLag, ref backLagVelocity, smoothTime); // lag 보간

        float bob = backFloatBobAmount > 0f ? Mathf.Sin(Time.time * backFloatBobSpeed) * backFloatBobAmount : 0f; // 부유감
        return backLocalPosition + backLagLocalOffset + new Vector3(0f, bob, 0f); // 최종 위치
    }

    private void ResetBackLag()
    {
        backLagLocalOffset = Vector3.zero;
        backLagVelocity = Vector3.zero;

        if (backAnchor != null)
        {
            previousBackAnchorPosition = backAnchor.position;
            hasBackAnchorPosition = true;
        }
        else
        {
            hasBackAnchorPosition = false;
        }
    }

    private void CacheRenderers()
    {
        weaponRenderers = GetComponentsInChildren<Renderer>(true);
        materialPropertyBlocks = new MaterialPropertyBlock[weaponRenderers.Length];
        baseRendererColors = new Color[weaponRenderers.Length];

        for (int i = 0; i < weaponRenderers.Length; i++)
        {
            materialPropertyBlocks[i] = new MaterialPropertyBlock();
            baseRendererColors[i] = GetRendererBaseColor(weaponRenderers[i]);
        }
    }

    private Color GetRendererBaseColor(Renderer targetRenderer)
    {
        if (targetRenderer == null || targetRenderer.sharedMaterial == null)
            return Color.white;

        Material material = targetRenderer.sharedMaterial;

        if (material.HasProperty(BaseColorProperty))
            return material.GetColor(BaseColorProperty);

        if (material.HasProperty(ColorProperty))
            return material.GetColor(ColorProperty);

        return Color.white;
    }

    private void UpdateFade(bool shouldShow)
    {
        if (!enableWeaponFade)
        {
            if (!Mathf.Approximately(currentFadeAlpha, 1f))
            {
                currentFadeAlpha = 1f;
                ApplyRendererFade(currentFadeAlpha);
            }

            return;
        }

        float targetAlpha = shouldShow ? 1f : 0f;
        float speed = shouldShow ? fadeInSpeed : fadeOutSpeed;
        float t = 1f - Mathf.Exp(-Mathf.Max(0f, speed) * Time.deltaTime);
        currentFadeAlpha = Mathf.Lerp(currentFadeAlpha, targetAlpha, t);
        ApplyRendererFade(currentFadeAlpha);
    }

    private IEnumerator FadeOutAndDestroyRoutine(GameObject objectToDestroy)
    {
        fadingOut = true;

        while (currentFadeAlpha > 0.02f)
            yield return null;

        Destroy(objectToDestroy != null ? objectToDestroy : gameObject);
    }

    private void ApplyRendererFade(float alpha)
    {
        if (weaponRenderers == null || materialPropertyBlocks == null)
            return;

        for (int i = 0; i < weaponRenderers.Length; i++)
        {
            Renderer targetRenderer = weaponRenderers[i];
            if (targetRenderer == null)
                continue;

            MaterialPropertyBlock block = materialPropertyBlocks[i];
            targetRenderer.GetPropertyBlock(block);
            Color color = i < baseRendererColors.Length ? baseRendererColors[i] : Color.white;
            color.a *= Mathf.Clamp01(alpha);

            if (targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty(BaseColorProperty))
                block.SetColor(BaseColorProperty, color);

            if (targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty(ColorProperty))
                block.SetColor(ColorProperty, color);

            targetRenderer.SetPropertyBlock(block);
        }
    }

    private Transform FindDirectChild(Transform root, string childName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);

            if (child.name == childName)
                return child;
        }

        return null;
    }
}

