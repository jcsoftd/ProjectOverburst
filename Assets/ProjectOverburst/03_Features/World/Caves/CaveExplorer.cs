using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overburst.Caves
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class CaveExplorer : MonoBehaviour
    {
        public CaveWorld world;
        public Camera view;
        public float speed = 6;
        public bool reviewInputSuppressed;
        public bool showGuide = true;
        public bool HasRoute => route.Count > 0;
        readonly Queue<Vector3> route = new Queue<Vector3>();
        CharacterController body;
        float vertical;

        void Start() { body = GetComponent<CharacterController>(); Teleport(world.courts[0].center); }
        void Update()
        {
            var keyboard = reviewInputSuppressed ? null : Keyboard.current;
            var mouse = reviewInputSuppressed ? null : Mouse.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame && !world.authoredLayout)
            { world.Generate(unchecked(world.seed * 1664525 + 1013904223) & int.MaxValue); Teleport(world.courts[0].center); }
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) showGuide = !showGuide;
            var input = keyboard == null ? Vector2.zero : new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0), (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            if (input.sqrMagnitude > .01f)
            {
                route.Clear(); var forward = view.transform.forward; forward.y = 0; var right = view.transform.right; right.y = 0;
                Step((forward.normalized * input.y + right.normalized * input.x).normalized, Time.deltaTime * (keyboard.leftShiftKey.isPressed ? 1.4f : 1));
            }
            else
            {
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    if (Physics.Raycast(view.ScreenPointToRay(mouse.position.ReadValue()), out var hit, 600) && hit.collider.GetComponentInParent<CaveWalkSurface>()) MoveTo(hit.point);
                }
                if (route.Count > 0)
                {
                    var delta = route.Peek() - transform.position; delta.y = 0;
                    if (delta.magnitude < .18f) route.Dequeue();
                    else Step(delta.normalized, Mathf.Min(Time.deltaTime, delta.magnitude / speed));
                }
                else Step(Vector3.zero, Time.deltaTime);
            }
            if (transform.position.y < -15) Teleport(world.courts[0].center);
        }
        public bool Step(Vector3 direction, float dt)
        {
            if (!body) body = GetComponent<CharacterController>();
            float remaining = Mathf.Clamp(dt, 0, .5f); bool success = true;
            while (remaining > .00001f)
            {
                float slice = Mathf.Min(.025f, remaining); remaining -= slice;
                var delta = direction * speed * slice; var next = transform.position + delta;
                if (direction.sqrMagnitude > .001f && !world.CanStand(next, .4f)) { delta = Vector3.zero; success = false; }
                vertical = body.isGrounded ? -2 : Mathf.Max(-20, vertical - 24 * slice);
                body.Move(delta + Vector3.up * vertical * slice);
            }
            return success;
        }
        public void Teleport(Vector3 position)
        {
            if (!body) body = GetComponent<CharacterController>();
            if (world.Ground(position, out var hit)) position = hit.point;
            body.enabled = false; transform.position = position + Vector3.up * .06f; body.enabled = true;
            vertical = 0; route.Clear(); Physics.SyncTransforms();
            if (view) view.GetComponent<CaveCamera>()?.Snap();
        }
        public bool MoveTo(Vector3 destination)
        { var path = world.FindRoute(transform.position, destination); route.Clear(); foreach (var p in path) route.Enqueue(p); return route.Count > 0; }
        void OnGUI()
        {
            if (!showGuide) return;
            GUI.Box(new Rect(20, 20, 510, 62), "CAVES · tiered platforms · seed " + world.seed);
            GUI.Label(new Rect(32, 47, 485, 25), world.authoredLayout
                ? "WASD / click: Move   Shift: Run   Wheel: Zoom   MMB: Orbit   H: Hide"
                : "WASD / click: Move   Shift: Run   Wheel: Zoom   MMB: Orbit   R: New seed   H: Hide");
        }
    }
}
