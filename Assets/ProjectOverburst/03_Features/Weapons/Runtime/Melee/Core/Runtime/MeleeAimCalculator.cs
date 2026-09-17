using UnityEngine;

public static class MeleeAimCalculator // 근접 조준 계산
{
    public static bool TryGetMouseDirectionFromPlayer(Transform playerRoot, Camera aimCamera, out Vector3 direction)
    {
        return TryGetMouseDirectionFromPlayer(playerRoot, aimCamera, 0.01f, out direction);
    }

    public static bool TryGetMouseDirectionFromPlayer(
        Transform playerRoot,
        Camera aimCamera,
        float minimumPlanarDistance,
        out Vector3 direction)
    {
        // GOAL A2: Mouse 직접 읽기 대신 UI Point 포인터 위치를 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null)
        {
            direction = Vector3.zero;
            return false;
        }

        return TryGetDirectionFromPlayer(playerRoot, aimCamera, facade.PointerPosition, minimumPlanarDistance, out direction);
    }

    public static bool TryGetDirectionFromPlayer(
        Transform playerRoot,
        Camera aimCamera,
        Vector2 screenPosition,
        float minimumPlanarDistance,
        out Vector3 direction)
    {
        direction = Vector3.zero;

        if (playerRoot == null)
            return false;

        Camera camera = aimCamera != null ? aimCamera : Camera.main;

        if (camera == null)
            return false;

        Ray ray = camera.ScreenPointToRay(screenPosition); // 포인터 ray
        Plane aimPlane = new Plane(Vector3.up, new Vector3(0f, playerRoot.position.y, 0f)); // 수평 평면

        if (!aimPlane.Raycast(ray, out float distance))
            return false;

        Vector3 target = ray.GetPoint(distance); // 목표점
        direction = target - playerRoot.position;
        direction.y = 0f; // XZ 평면

        float safeMinimumDistance = Mathf.Max(0.01f, minimumPlanarDistance);
        if (direction.sqrMagnitude <= safeMinimumDistance * safeMinimumDistance)
            return false;

        direction.Normalize();
        return true;
    }
}
