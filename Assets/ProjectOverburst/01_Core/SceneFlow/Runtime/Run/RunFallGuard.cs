using UnityEngine;

public enum RunFallGuardMode // 낙하 처리 모드
{
    ClampToLastSafePosition,
    TeleportToRespawn,
    KillOnExit
}

[DisallowMultipleComponent]
public sealed class RunFallGuard : MonoBehaviour // 런 낙하 방어선
{
    private const float VerticalFallRecoveryDepth = 1.5f;
    private const float RecoverySurfaceOffset = 0.08f;

    private RunWalkableArea walkableArea; // 보행 영역
    private Rigidbody targetRigidbody; // 물리 대상
    private Vector2 lastSafePositionXZ; // 마지막 안전 위치
    private Vector3 respawnPosition; // 복귀 위치
    private float minimumAllowedY; // 수직 낙하 판정선
    private float verticalRecoveryY; // 마지막 안전 XZ 복귀 높이
    private RunFallGuardMode mode; // 처리 모드
    private bool hasLastSafePosition; // 안전 위치 존재
    private bool killTriggered; // 사망 중복 방지

    public static event System.Action<Transform> TargetTeleported; // 복구 완료 알림

    public void Configure(RunWalkableArea area)
    {
        Configure(area, RunFallGuardMode.ClampToLastSafePosition, Vector3.zero);
    }

    public void Configure(RunWalkableArea area, RunFallGuardMode guardMode)
    {
        Configure(area, guardMode, Vector3.zero);
    }

    public void Configure(
        RunWalkableArea area,
        RunFallGuardMode guardMode,
        Vector3 targetRespawnPosition)
    {
        Configure(area, guardMode, targetRespawnPosition, targetRespawnPosition.y);
    }

    public void Configure(
        RunWalkableArea area,
        RunFallGuardMode guardMode,
        Vector3 targetRespawnPosition,
        float minimumSurfaceY)
    {
        walkableArea = area;
        mode = guardMode;
        respawnPosition = targetRespawnPosition;
        minimumAllowedY = minimumSurfaceY - VerticalFallRecoveryDepth;
        verticalRecoveryY = minimumSurfaceY + RecoverySurfaceOffset;
        targetRigidbody = GetComponent<Rigidbody>();
        hasLastSafePosition = false;
        killTriggered = false;
        RefreshLastSafePosition();
        enabled = walkableArea != null;
    }

    private void Awake()
    {
        targetRigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        ClampToWalkableArea();
    }

    private void LateUpdate()
    {
        ClampToWalkableArea();
    }

    private void RefreshLastSafePosition()
    {
        if (walkableArea == null)
            return;

        Vector3 position = GetCurrentPosition(); // 현재 위치
        Vector2 positionXZ = new Vector2(position.x, position.z); // XZ 위치

        if (walkableArea.IsWalkable(positionXZ))
        {
            lastSafePositionXZ = positionXZ;
            hasLastSafePosition = true;
            return;
        }

        if (walkableArea.TryFindNearestWalkable(positionXZ, out Vector2 nearest))
        {
            lastSafePositionXZ = nearest;
            hasLastSafePosition = true;
        }
    }

    private void ClampToWalkableArea()
    {
        if (walkableArea == null)
            return;

        Vector3 position = GetCurrentPosition(); // 현재 위치
        Vector2 positionXZ = new Vector2(position.x, position.z); // XZ 위치

        if (position.y < minimumAllowedY)
        {
            HandleVerticalFall(position);
            return;
        }

        if (walkableArea.IsWalkable(positionXZ))
        {
            lastSafePositionXZ = positionXZ;
            hasLastSafePosition = true;
            return;
        }

        if (!hasLastSafePosition)
            RefreshLastSafePosition();

        if (!hasLastSafePosition)
            return;

        HandleOutsideWalkableArea(position);
    }

    private void HandleVerticalFall(Vector3 currentPosition)
    {
        switch (mode)
        {
            case RunFallGuardMode.TeleportToRespawn:
                ApplyTeleportPosition(respawnPosition);
                RefreshLastSafePosition();
                break;
            case RunFallGuardMode.KillOnExit:
                KillTargetByFall(currentPosition);
                break;
            default:
                if (!hasLastSafePosition)
                    RefreshLastSafePosition();

                if (!hasLastSafePosition)
                    return;

                ApplyTeleportPosition(new Vector3(lastSafePositionXZ.x, verticalRecoveryY, lastSafePositionXZ.y));
                RefreshLastSafePosition();
                break;
        }
    }

    private void HandleOutsideWalkableArea(Vector3 currentPosition)
    {
        switch (mode)
        {
            case RunFallGuardMode.TeleportToRespawn:
                ApplyTeleportPosition(respawnPosition);
                RefreshLastSafePosition();
                break;
            case RunFallGuardMode.KillOnExit:
                KillTargetByFall(currentPosition);
                break;
            default:
                Vector3 correctedPosition = new Vector3(lastSafePositionXZ.x, currentPosition.y, lastSafePositionXZ.y); // 보정 위치
                ApplyCorrectedPosition(correctedPosition);
                break;
        }
    }

    private Vector3 GetCurrentPosition()
    {
        return targetRigidbody != null && !targetRigidbody.isKinematic
            ? targetRigidbody.position
            : transform.position;
    }

    private void ApplyCorrectedPosition(Vector3 correctedPosition)
    {
        if (targetRigidbody != null)
        {
            bool preserveVelocity = !targetRigidbody.isKinematic;
            Vector3 velocity = preserveVelocity ? targetRigidbody.linearVelocity : Vector3.zero; // 속도 유지
            targetRigidbody.position = correctedPosition;
            if (preserveVelocity)
                targetRigidbody.linearVelocity = velocity;
        }

        transform.position = correctedPosition;
        Physics.SyncTransforms();
        CombatTargetRegistry.NotifySpatialChanged(transform);
    }

    private void ApplyTeleportPosition(Vector3 targetPosition)
    {
        PlayerActorRuntime actor = GetComponent<PlayerActorRuntime>()
            ?? GetComponentInParent<PlayerActorRuntime>();
        actor?.PlayerKit?.CancelCurrentActions(WeaponActionCancelReason.Recovery);
        ActorTeleportUtility.TeleportSafely(transform, targetPosition, transform.rotation); // 공용 복구
        TargetTeleported?.Invoke(transform); // 추적 시스템 즉시 갱신
    }

    private void KillTargetByFall(Vector3 hitPoint)
    {
        if (killTriggered)
            return;

        CombatHealth health = GetComponent<CombatHealth>() ?? GetComponentInParent<CombatHealth>(); // 사망 대상
        if (health == null || health.IsDead)
            return;

        killTriggered = true;
        DamageInfo fallDamage = new DamageInfo( // 낙하 피해
            health.MaxHp,
            hitPoint,
            null,
            Vector3.zero,
            0f,
            false,
            false,
            false);
        health.TakeDamage(fallDamage);
    }
}
