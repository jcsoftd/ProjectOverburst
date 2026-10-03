using UnityEngine;

// 60D 빛·암흑 강공 타격감: 타격마다 공간 왜곡 충격파, 뒤 타일수록 센 카메라 흔들림.
// 2026-09-30 결정: 빛 1·2·3타 · 암흑 내려치기·폭발에 충격파, 빛 2·3타 · 암흑 폭발에 카메라 흔들림.
// (암흑 흡인 중 조여드는 왜곡 링은 같은 날 사용자 확인 후 제거.)
public static class UpperHeavyImpactFeedback
{
    public const string ShockwavePath = "Combat/VFX/VFX_Heavy_Shockwave";
    private const float ShockwaveDiameterPerRadius = 2.2f; // 충격파 지름 = 강공 반경 × 2.2
    private const float ShockwaveHeight = 0.4f;
    private const int PoolCapacity = 6;

    private static GameObject shockwave;
    private static bool loaded;

    public static int ShockwaveCount { get; private set; }
    public static int CameraCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        shockwave = null;
        loaded = false;
        ShockwaveCount = CameraCount = 0;
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        shockwave = Resources.Load<GameObject>(ShockwavePath);
    }

    // 강공 타격 한 번의 공간 왜곡 충격파. radius는 그 타격의 피해 반경.
    public static void PlayShockwave(Vector3 center, float radius)
    {
        Load();
        if (shockwave == null || radius <= 0f) return;
        float scale = radius * ShockwaveDiameterPerRadius;
        TransientVfxPool.Spawn(shockwave, center + Vector3.up * ShockwaveHeight, Quaternion.identity, 0f, PoolCapacity,
            prepareBeforeActivation: instance => instance.transform.localScale = shockwave.transform.localScale * scale,
            returnMode: TransientVfxReturnMode.NaturalParticleCompletion,
            contentSceneHandle: WorldSessionState.ContentScene.IsValid() ? WorldSessionState.ContentScene.handle : 0);
        ShockwaveCount++;
    }

    // strength 0..1: 빛 2타 0.5 · 빛 마지막 타 0.75 · 암흑 폭발 1.
    public static void RequestCamera(Vector3 from, Vector3 center, float strength)
    {
        QuarterViewCamera camera = QuarterViewCamera.ActiveInstance;
        if (camera == null) return;
        float k = Mathf.Clamp01(strength);
        Vector3 direction = center - from;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        float position = Mathf.Lerp(0.06f, 0.16f, k);
        float roll = Mathf.Lerp(0.3f, 0.75f, k);
        camera.RequestCombatImpact(CombatCameraRequestKind.AttackHit, direction, Vector3.zero, false,
            Mathf.Lerp(0.12f, 0.2f, k), position, roll, 0.72f, 0.05f, 0.16f,
            1.5f, 0.26f, 4f);
        CameraCount++;
    }
}
