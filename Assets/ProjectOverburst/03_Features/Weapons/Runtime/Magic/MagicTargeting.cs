using UnityEngine;

public static class MagicTargeting // 마법 조준 계산
{
    public static bool TryGetDirectionalAimLine(
        Transform casterTransform,
        PlayerEquipment playerEquipment,
        Camera aimCamera,
        WeaponFinalStats currentStats,
        float fallbackCastHeight,
        float fallbackCastForwardOffset,
        out MagicAimLine aimLine)
    {
        aimLine = default;
        if (casterTransform == null)
            return false;

        Vector3 origin = GetCastOriginBase(casterTransform, playerEquipment, fallbackCastHeight, out bool hasCastOrigin); // 시전 시작점
        Vector3 direction;
        if (!TryGetDirectionFromOrigin(origin, aimCamera, out direction))
            direction = casterTransform.forward; // 방향 fallback

        direction.y = 0f; // XZ 평면
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        if (!hasCastOrigin)
            origin += direction * fallbackCastForwardOffset; // 대체 위치 보정

        if (TryGetDirectionFromOrigin(origin, aimCamera, out Vector3 refinedDirection))
            direction = refinedDirection; // 보정 방향

        aimLine.origin = origin; // 시작점
        aimLine.direction = direction.normalized; // 방향
        aimLine.range = Mathf.Max(0.1f, currentStats.range); // 표시 거리
        return true;
    }

    private static Vector3 GetCastOriginBase(Transform casterTransform, PlayerEquipment playerEquipment, float fallbackCastHeight, out bool hasCastOrigin)
    {
        hasCastOrigin = false;
        WeaponAimSource aimSource = playerEquipment != null ? playerEquipment.CurrentWeaponAimSource : null;
        if (aimSource != null)
        {
            aimSource.RefreshMissingPoints(); // anchor 보정
            if (aimSource.HasAttackSpawnPoint)
            {
                hasCastOrigin = true;
                return aimSource.GetAttackSpawnPosition(); // 생성 위치
            }
        }

        return casterTransform.position + Vector3.up * fallbackCastHeight; // 대체 기준점
    }

    private static bool TryGetDirectionFromOrigin(Vector3 origin, Camera aimCamera, out Vector3 direction)
    {
        direction = Vector3.zero;
        Camera camera = aimCamera != null ? aimCamera : Camera.main;
        // GOAL A2: Mouse 직접 읽기 대신 UI Point 포인터 위치를 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (camera == null || facade == null)
            return false;

        Ray ray = camera.ScreenPointToRay(facade.PointerPosition); // 포인터 ray
        Plane aimPlane = new Plane(Vector3.up, origin); // 수평 평면
        if (!aimPlane.Raycast(ray, out float distance))
            return false;

        Vector3 target = ray.GetPoint(distance); // 목표점
        direction = target - origin;
        direction.y = 0f; // XZ 평면
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        return true;
    }
}
