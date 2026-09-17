using UnityEngine;

public static class EnemyAiTickScheduler // 대량 웨이브 AI 판단 주기 정책
{
    public const int FullRateEnemyLimit = 40;
    public const int MidWaveEnemyLimit = 100;
    public const int LargeWaveEnemyLimit = 200;
    public const float FullRateDistance = 20f;
    public const float MidDistance = 40f;
    public const float FarDistance = 70f;

    private const float VisibleMidInterval = 0.05f;
    private const float VisibleFarInterval = 0.1f;
    private const float HiddenMidInterval = 0.1f;
    private const float HiddenFarInterval = 0.25f;
    private const float HiddenVeryFarInterval = 0.5f;

    private static Camera cachedMainCamera;
    private static int cameraCacheFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cachedMainCamera = null;
        cameraCacheFrame = -1;
    }

    public static float ResolveInterval(
        int activeEnemyCount,
        float sqrDistance,
        bool isVisible,
        bool requiresFullRate)
    {
        if (requiresFullRate
            || activeEnemyCount <= FullRateEnemyLimit
            || sqrDistance <= FullRateDistance * FullRateDistance)
        {
            return 0f;
        }

        bool isMidDistance = sqrDistance <= MidDistance * MidDistance;
        if (activeEnemyCount <= MidWaveEnemyLimit)
            return isVisible ? VisibleMidInterval : HiddenMidInterval;

        if (activeEnemyCount <= LargeWaveEnemyLimit)
        {
            if (isVisible)
                return isMidDistance ? VisibleMidInterval : VisibleFarInterval;
            return isMidDistance ? HiddenMidInterval : HiddenFarInterval;
        }

        if (isVisible)
            return isMidDistance ? VisibleMidInterval : VisibleFarInterval;

        return sqrDistance <= FarDistance * FarDistance
            ? HiddenFarInterval
            : HiddenVeryFarInterval;
    }

    public static float ResolveStaggerDelay(float interval, int instanceId)
    {
        if (interval <= 0f)
            return 0f;

        uint hash = unchecked((uint)instanceId);
        hash ^= hash >> 16;
        hash *= 0x7feb352d;
        hash ^= hash >> 15;
        hash *= 0x846ca68b;
        hash ^= hash >> 16;
        float phase = ((hash % 997u) + 0.5f) / 997f;
        return interval * phase; // 같은 프레임 생성 개체 분산
    }

    public static bool IsLikelyVisible(Renderer[] renderers, Vector3 position)
    {
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer targetRenderer = renderers[i];
                if (targetRenderer != null && targetRenderer.enabled && targetRenderer.isVisible)
                    return true;
            }
        }

        Camera mainCamera = ResolveMainCamera();
        if (mainCamera == null)
            return true; // 카메라 미확정 시 보수적으로 고주기 유지

        Vector3 viewport = mainCamera.WorldToViewportPoint(position);
        return viewport.z > 0f
            && viewport.x >= 0f
            && viewport.x <= 1f
            && viewport.y >= 0f
            && viewport.y <= 1f;
    }

    public static float ResolveCameraSqrDistance(Vector3 position)
    {
        Camera mainCamera = ResolveMainCamera();
        if (mainCamera == null)
            return float.PositiveInfinity;

        Vector3 delta = mainCamera.transform.position - position;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    private static Camera ResolveMainCamera()
    {
        int frame = Time.frameCount;
        if (cameraCacheFrame == frame)
            return cachedMainCamera;

        cameraCacheFrame = frame;
        if (cachedMainCamera == null || !cachedMainCamera.isActiveAndEnabled)
            cachedMainCamera = Camera.main; // 프레임당 최대 1회 태그 조회
        return cachedMainCamera;
    }
}
