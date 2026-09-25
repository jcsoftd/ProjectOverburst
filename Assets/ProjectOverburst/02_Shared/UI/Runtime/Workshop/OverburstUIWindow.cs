using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Presentation-only window lifetime; gameplay input ownership remains with the game UI services.</summary>
[DisallowMultipleComponent]
public sealed class OverburstUIWindow : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] private RectTransform windowRect;
    [SerializeField] private Vector2 homePosition;
    public RectTransform WindowRect => windowRect;

    public void Configure(RectTransform rect) { windowRect = rect; homePosition = rect.anchoredPosition; }
    public void Show() { gameObject.SetActive(true); Canvas.ForceUpdateCanvases(); foreach(var scroll in GetComponentsInChildren<UnityEngine.UI.ScrollRect>()){scroll.StopMovement();scroll.verticalNormalizedPosition=1;scroll.content.anchoredPosition=Vector2.zero;} Focus(); }
    public void Close() => gameObject.SetActive(false);
    public void Toggle() { if (gameObject.activeSelf) Close(); else Show(); }
    public void Focus() => transform.SetAsLastSibling();
    public void ResetPosition() { if (windowRect != null) windowRect.anchoredPosition = homePosition; }
    public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) Focus(); }
}
