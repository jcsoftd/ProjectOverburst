using UnityEngine;

public static class RunWalkableContext // 현재 런 보행 영역
{
    private static RunWalkableArea current;
    private static float currentMinimumSurfaceY;
    private static int revision;

    public static RunWalkableArea Current
    {
        get { return current; }
    }

    public static int Revision => revision;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        current = null;
        currentMinimumSurfaceY = 0f;
        revision = 0;
    }

    public static void SetCurrent(RunWalkableArea walkableArea)
    {
        SetCurrent(walkableArea, 0f);
    }

    public static void SetCurrent(
        RunWalkableArea walkableArea,
        float minimumSurfaceY)
    {
        current = walkableArea; // 런 시작
        currentMinimumSurfaceY = minimumSurfaceY;
        AdvanceRevision();
    }

    public static void Clear()
    {
        current = null; // 런 종료
        currentMinimumSurfaceY = 0f;
        AdvanceRevision();
    }

    public static bool TryAttachFallGuard(GameObject target)
    {
        return TryAttachFallGuard(target, RunFallGuardMode.ClampToLastSafePosition, Vector3.zero);
    }

    public static bool TryAttachFallGuard(GameObject target, RunFallGuardMode mode)
    {
        return TryAttachFallGuard(target, mode, Vector3.zero);
    }

    public static bool TryAttachFallGuard(GameObject target, RunFallGuardMode mode, Vector3 respawnPosition)
    {
        if (target == null || current == null)
            return false; // 대상 없음

        RunFallGuard fallGuard = target.GetComponent<RunFallGuard>();
        if (fallGuard == null)
            fallGuard = target.AddComponent<RunFallGuard>(); // 최후 방어선

        fallGuard.Configure(current, mode, respawnPosition, currentMinimumSurfaceY);
        return true;
    }

    public static bool TryConfigureExistingFallGuard(
        RunFallGuard fallGuard,
        RunFallGuardMode mode,
        Vector3 respawnPosition)
    {
        if (fallGuard == null)
            return false;
        if (current == null)
        {
            fallGuard.Configure(null, mode, respawnPosition, 0f);
            return false; // Hideout 등 런 보행 영역이 없는 씬
        }

        fallGuard.Configure(current, mode, respawnPosition, currentMinimumSurfaceY);
        return true;
    }

    private static void AdvanceRevision()
    {
        revision = revision == int.MaxValue ? 1 : revision + 1; // 맵 교체 감지
    }
}
