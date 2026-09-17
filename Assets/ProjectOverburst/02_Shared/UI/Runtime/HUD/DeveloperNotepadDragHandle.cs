using UnityEngine;
using UnityEngine.EventSystems;

public class DeveloperNotepadDragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    [SerializeField] private RectTransform targetWindow;
    [SerializeField] private Canvas canvas;

    public void Configure(RectTransform window, Canvas rootCanvas)
    {
        targetWindow = window;
        canvas = rootCanvas;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (targetWindow != null)
            targetWindow.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (targetWindow == null)
            return;

        float scaleFactor = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        targetWindow.anchoredPosition += eventData.delta / scaleFactor;
    }
}
