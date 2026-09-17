using UnityEngine;

public readonly struct CombatHitFeedbackRequest
{
    public readonly CombatCameraRequestKind CameraRequestKind;
    public readonly Vector3 WorldDirection;
    public readonly bool HasCameraDirectionOverride;
    public readonly Vector3 CameraDirectionOverride;
    public readonly float CameraPriority;
    public readonly Object Source;
    public readonly int AttackSequenceId;
    public readonly CombatHitFeedbackProfile Profile;
    public readonly bool IsCritical;
    public readonly WeaponElement Element;
    public readonly Vector3 HitPoint;
    public readonly bool AllowGlobalFeedback;
    public readonly bool IsLethal;

    public CombatHitFeedbackRequest(
        Object source,
        int attackSequenceId,
        CombatHitFeedbackProfile profile,
        bool isCritical,
        WeaponElement element,
        Vector3 hitPoint,
        bool allowGlobalFeedback,
        CombatCameraRequestKind cameraRequestKind = CombatCameraRequestKind.AttackHit,
        Vector3 worldDirection = default,
        float cameraPriority = 1f,
        bool hasCameraDirectionOverride = false,
        Vector3 cameraDirectionOverride = default,
        bool isLethal = false)
    {
        Source = source;
        AttackSequenceId = attackSequenceId;
        Profile = profile;
        IsCritical = isCritical;
        Element = element;
        HitPoint = hitPoint;
        AllowGlobalFeedback = allowGlobalFeedback;
        CameraRequestKind = cameraRequestKind;
        WorldDirection = worldDirection;
        HasCameraDirectionOverride = hasCameraDirectionOverride;
        CameraDirectionOverride = cameraDirectionOverride;
        CameraPriority = Mathf.Max(0f, cameraPriority);
        IsLethal = isLethal;
    }
}

public enum CombatCameraRequestKind
{
    AttackHit,
    PlayerDamage
}

[DefaultExecutionOrder(-900)]
public sealed class CombatHitFeedbackService : MonoBehaviour
{
    private static CombatHitFeedbackService instance;
    private Object lastRequestSource;
    private int lastAttackSequenceId = -1;
    private int lastRequestStrength;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject root = new GameObject(nameof(CombatHitFeedbackService));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<CombatHitFeedbackService>();
    }

    public static void Request(CombatHitFeedbackRequest request)
    {
        if (instance == null)
            Bootstrap();

        instance?.HandleRequest(request);
    }

    public static void SuspendForExternalTimeEffect()
    {
        if (instance != null)
            OverburstTimeEffectArbiter.ClearOwner(instance);
    }

    private void HandleRequest(CombatHitFeedbackRequest request)
    {
        MeleeElementHitVfxService.TryPlay(request.Element, request.HitPoint); // 유효 적중점마다 재생

        bool isSameAttack = request.Source == lastRequestSource
            && request.AttackSequenceId > 0
            && request.AttackSequenceId == lastAttackSequenceId;
        if (!isSameAttack)
            MeleeElementSfxService.TryPlayHit(request.Element, request.HitPoint); // 타수당 첫 적중만

        int requestStrength = request.IsLethal ? 2 : request.IsCritical ? 1 : 0;
        if (isSameAttack && requestStrength <= lastRequestStrength)
            return; // 같은 타수는 일반 < 치명타 < 사망으로 강화되는 경우만 갱신

        lastRequestSource = request.Source;
        lastAttackSequenceId = request.AttackSequenceId;
        lastRequestStrength = requestStrength;
        if (!request.AllowGlobalFeedback || request.Profile == null)
            return;

        OverburstFeelFeedbackHub.Request(
            request.IsLethal ? OverburstFeelCue.Death
                : request.IsCritical ? OverburstFeelCue.StrongHit
                : OverburstFeelCue.WeakHit,
            request.HitPoint,
            request.IsLethal ? 1.35f : request.IsCritical ? 1.15f : 1f);
        RequestCameraImpact(request);
        RequestHitStop(request.Profile, request.IsCritical);
    }

    private static void RequestCameraImpact(CombatHitFeedbackRequest request)
    {
        QuarterViewCamera cameraController = QuarterViewCamera.ActiveInstance;
        if (cameraController == null)
            return;

        cameraController.RequestCombatImpact(
            request.CameraRequestKind,
            request.WorldDirection,
            request.HasCameraDirectionOverride ? request.CameraDirectionOverride : Vector3.zero,
            request.HasCameraDirectionOverride,
            request.Profile.CameraDuration,
            request.Profile.ResolveCameraPositionAmplitude(request.IsCritical),
            request.Profile.ResolveCameraRollAmplitude(request.IsCritical),
            request.Profile.CameraKickReturnRatio,
            request.Profile.CameraMicroShakeDuration,
            request.Profile.CameraMicroShakeAmplitude,
            request.CameraPriority,
            request.Profile.CameraPositionSafetyLimit,
            request.Profile.CameraRollSafetyLimit);
    }

    public static void RequestPlayerDamage(Vector3 worldDirection, float duration, float amplitude)
    {
        QuarterViewCamera.ActiveInstance?.RequestCombatImpact(
            CombatCameraRequestKind.PlayerDamage, worldDirection, Vector3.zero, false,
            duration, amplitude, amplitude * 2f, 0.82f, duration * 0.45f, 0.16f,
            2f, amplitude * 1.5f, amplitude * 3f);
    }

    private void RequestHitStop(CombatHitFeedbackProfile profile, bool isCritical)
    {
        float duration = profile.ResolveHitStopDuration(isCritical);
        if (duration <= 0f)
            return;

        OverburstTimeEffectArbiter.Request(
            this,
            OverburstTimeEffectKind.HitStop,
            profile.HitStopTimeScale,
            duration);
    }
}
