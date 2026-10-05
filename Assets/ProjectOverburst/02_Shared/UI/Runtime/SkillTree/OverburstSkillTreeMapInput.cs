using UnityEngine;
using UnityEngine.EventSystems;

public sealed class OverburstSkillTreeMapInput : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public OverburstSkillTreeUI owner;
    Vector2 start, pan;
    public void OnScroll(PointerEventData e)
    {
        if (owner == null) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(owner.viewport, e.position, e.enterEventCamera, out var point)) owner.ZoomAt(owner.Zoom + Mathf.Sign(e.scrollDelta.y) * .1f, point);
        e.Use();
    }
    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || owner == null) return;
        owner.HideTooltip(); pan = owner.Pan;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(owner.viewport, e.position, e.pressEventCamera, out start);
    }
    public void OnDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || owner == null) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(owner.viewport, e.position, e.pressEventCamera, out var point)) owner.SetPan(pan + point - start);
    }
    public void OnEndDrag(PointerEventData e) => owner?.HideTooltip();
}
