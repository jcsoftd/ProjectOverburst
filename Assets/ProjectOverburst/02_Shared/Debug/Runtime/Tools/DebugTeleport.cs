#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 플레이어 순간이동. 옛 HUD 테마 패널(2026-10-01 삭제)의 순간이동과 같은 순서다:
    /// CharacterController를 끄고 옮긴 뒤 켜고, 이동 상태를 초기화하고, 물리 변환을 맞춘다.
    /// </summary>
    public static class DebugTeleport
    {
        private const float RayDistance = 500f;

        public static Transform Player
        {
            get
            {
                PlayerContext context = PlayerContext.Instance;
                PlayerActorRuntime actor = context != null ? context.CurrentActor : null;
                return actor != null ? actor.transform : null;
            }
        }

        public static bool HasPlayer => Player != null;

        public static DebugResult To(Vector3 position)
        {
            Transform player = Player;
            if (player == null)
                return DebugResult.Fail("플레이어가 없어요");
            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled)
                controller.enabled = false;
            player.position = position;
            if (wasEnabled)
                controller.enabled = true;
            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            if (movement != null)
                movement.ResetMotionAfterTeleport();
            Physics.SyncTransforms();
            return DebugResult.Ok($"({position.x:0.0}, {position.z:0.0})");
        }

        /// <summary>마우스가 가리키는 바닥으로 옮긴다. 바닥을 못 찾으면 플레이어 높이의 평면을 쓴다.</summary>
        public static DebugResult ToCursor()
        {
            Transform player = Player;
            Camera camera = Camera.main;
            if (player == null || camera == null)
                return DebugResult.Fail("플레이어나 카메라가 없어요");
            if (Mouse.current == null)
                return DebugResult.Fail("마우스가 없어요");
            Ray ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!TryGround(ray, player, out Vector3 point))
                return DebugResult.Fail("바닥을 찾지 못했어요");
            return To(point + Vector3.up * 0.05f);
        }

        private static bool TryGround(Ray ray, Transform player, out Vector3 point)
        {
            int mask = LayerMask.GetMask("Ground", "Default");
            RaycastHit[] hits = Physics.RaycastAll(ray, RayDistance, mask == 0 ? Physics.DefaultRaycastLayers : mask,
                QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            point = default;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].transform.IsChildOf(player) || hits[i].normal.y < 0.5f || hits[i].distance >= best)
                    continue;
                best = hits[i].distance;
                point = hits[i].point;
            }
            if (best < float.MaxValue)
                return true;
            var plane = new Plane(Vector3.up, player.position);
            if (!plane.Raycast(ray, out float distance))
                return false;
            point = ray.GetPoint(distance);
            return true;
        }
    }
}
#endif
