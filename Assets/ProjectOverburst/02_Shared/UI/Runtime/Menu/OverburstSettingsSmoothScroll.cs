using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// The viewport handles wheel events before its parent ScrollRect. Native dragging remains on ScrollRect.
[DisallowMultipleComponent]
public sealed class OverburstSettingsSmoothScroll : MonoBehaviour, IScrollHandler
{
    public ScrollRect scroll;
    public float unitsPerNotch = 76.8f;
    public float smoothTime = .10f;

    float target, velocity, lastApplied;
    bool pending;
    PointerEventData pointer;

    public void Cancel() { pending = false; velocity = 0f; pointer = null; }
    void OnDisable() => Cancel();

    public void OnScroll(PointerEventData data)
    {
        if (!isActiveAndEnabled || !scroll || !scroll.isActiveAndEnabled || !scroll.vertical || !scroll.content || !scroll.viewport) return;
        float delta = Mathf.Abs(data.scrollDelta.x) > Mathf.Abs(data.scrollDelta.y) ? data.scrollDelta.x : -data.scrollDelta.y;
        if (Mathf.Approximately(delta, 0f)) return;
        var module = data.currentInputModule as InputSystemUIInputModule;
        float tick = module ? Mathf.Max(.01f, module.scrollDeltaPerTick) : 1f;
        float step = delta / tick * unitsPerNotch;
        float current = scroll.content.anchoredPosition.y;
        if (!pending || Mathf.Abs(current - lastApplied) > .1f || Mathf.Sign(step) != Mathf.Sign(target - current))
        { target = current; velocity = 0f; }
        target = Mathf.Clamp(target + step, 0f, Maximum());
        lastApplied = current; pending = Mathf.Abs(target - current) > .01f; pointer = data;
        scroll.StopMovement(); data.Use();
    }

    float Maximum() => Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);

    void LateUpdate()
    {
        if (!pending) return;
        if (!scroll || !scroll.isActiveAndEnabled || !scroll.content || !scroll.viewport || (pointer != null && (pointer.dragging || pointer.pointerPress != null)))
        { Cancel(); return; }
        var position = scroll.content.anchoredPosition;
        // Keyboard focus, folding or programmatic scrolling takes priority over an earlier wheel target.
        if (Mathf.Abs(position.y - lastApplied) > .1f) { Cancel(); return; }
        target = Mathf.Clamp(target, 0f, Maximum());
        position.y = Mathf.SmoothDamp(position.y, target, ref velocity, Mathf.Max(.01f, smoothTime), Mathf.Infinity, Time.unscaledDeltaTime);
        position.y = Mathf.Clamp(position.y, 0f, Maximum());
        if (Mathf.Abs(position.y - target) < .05f) { position.y = target; Cancel(); }
        scroll.content.anchoredPosition = position; lastApplied = position.y;
    }
}
