using UnityEngine;
using UnityEngine.SceneManagement;

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
    public readonly int PhaseIndex;
    public readonly CombatHealth Target;
    public readonly CombatImpactShape ImpactShape;
    public readonly Vector3 ImpactDirection;

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
        bool isLethal = false,
        int phaseIndex = 0,
        CombatHealth target = null,
        CombatImpactShape impactShape = CombatImpactShape.Sweep,
        Vector3 impactDirection = default)
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
        PhaseIndex = phaseIndex;
        Target = target;
        ImpactShape = impactShape;
        ImpactDirection = impactDirection.sqrMagnitude > .0001f ? impactDirection : worldDirection;
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
    // Bounded history handles interleaved attacks without allocating per victim.
    private struct HitGroup
    {
        public CombatHitFeedbackRequest Request;
        public bool Occupied, Pending, Dispatched;
        public bool AnyCritical, AnyLethal;
    }
    private readonly HitGroup[] groups = new HitGroup[128];
    private int nextGroup;

    private void OnEnable() => SceneManager.activeSceneChanged += OnSceneChanged;
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        ClearGroups();
    }
    private void OnSceneChanged(Scene previous, Scene current) => ClearGroups();
    private void ClearGroups()
    {
        System.Array.Clear(groups, 0, groups.Length);
        nextGroup = 0;
        OverburstTimeEffectArbiter.ClearOwner(this);
    }

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
        if (request.Target != null && request.Target.TryGetComponent<EnemyDeathPresentation>(out var presentation))
        {
            CombatImpactFeel.Play(presentation.Surface,
                request.ImpactShape, request.HitPoint, request.ImpactDirection, request.IsCritical,
                lethal: request.IsLethal);
        }

        int index = -1;
        if (request.Source != null && request.AttackSequenceId > 0)
            for (int i = 0; i < groups.Length; i++)
                if (groups[i].Occupied && groups[i].Request.Source == request.Source
                    && groups[i].Request.AttackSequenceId == request.AttackSequenceId
                    && groups[i].Request.PhaseIndex == request.PhaseIndex)
                { index = i; break; }

        if (index < 0)
        {
            index = nextGroup;
            nextGroup = (nextGroup + 1) % groups.Length;
            groups[index] = new HitGroup { Occupied = true, Request = request };
            MeleeElementSfxService.TryPlayHit(request.Element, request.HitPoint);
        }
        if (!request.AllowGlobalFeedback || request.Profile == null || groups[index].Dispatched) return;
        groups[index].AnyCritical |= request.IsCritical;
        groups[index].AnyLethal |= request.IsLethal;
        if (!groups[index].Pending || Strength(request) > Strength(groups[index].Request))
            groups[index].Request = request;
        groups[index].Pending = true;
    }

    private static int Strength(CombatHitFeedbackRequest request)
        => (request.IsLethal ? 2 : 0) + (request.IsCritical ? 1 : 0);

    private void LateUpdate()
    {
        // Select the strongest contact in this frame, then never extend this group's stop.
        // LateUpdate still precedes the camera render and the next simulation step.
        for (int i = 0; i < groups.Length; i++)
        {
            if (!groups[i].Pending) continue;
            groups[i].Pending = false;
            groups[i].Dispatched = true;
            var request = groups[i].Request;
            DispatchGlobal(new CombatHitFeedbackRequest(request.Source, request.AttackSequenceId, request.Profile,
                groups[i].AnyCritical, request.Element, request.HitPoint, request.AllowGlobalFeedback,
                request.CameraRequestKind, request.WorldDirection, request.CameraPriority,
                request.HasCameraDirectionOverride, request.CameraDirectionOverride, groups[i].AnyLethal,
                request.PhaseIndex, request.Target, request.ImpactShape, request.ImpactDirection));
        }
    }

    private void DispatchGlobal(CombatHitFeedbackRequest request)
    {

        if (request.Target == null || request.Target.GetComponent<EnemyDeathPresentation>() == null)
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
