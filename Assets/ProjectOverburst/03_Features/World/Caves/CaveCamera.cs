using UnityEngine;
using UnityEngine.InputSystem;

namespace Overburst.Caves
{
    [RequireComponent(typeof(Camera))]
    public sealed class CaveCamera : MonoBehaviour
    {
        public CaveExplorer target;
        public float distance = 27;
        public float yaw = 0;
        Vector3 focus;
        void Start() { Snap(); }
        void LateUpdate()
        {
            if (!target) return;
            var mouse = target.reviewInputSuppressed ? null : Mouse.current;
            if (mouse != null)
            {
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y / 120 * 2, 14, 48);
                if (mouse.middleButton.isPressed) yaw += mouse.delta.ReadValue().x * .15f;
            }
            focus = Vector3.Lerp(focus, target.transform.position, 1 - Mathf.Exp(-15 * Time.deltaTime)); Apply();
        }
        public void Snap() { if (target) focus = target.transform.position; Apply(); }
        void Apply()
        {
            var rotation = Quaternion.Euler(57, yaw, 0);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
            var camera = GetComponent<Camera>(); camera.fieldOfView = 48; camera.nearClipPlane = .1f; camera.farClipPlane = 800;
        }
    }
}
