using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class WorldLevelButtonSkin : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    public RawImage artwork;
    public Texture normal, hover, pressed, focus, disabled;
    public Text label;
    public bool row;
    public RawImage[] closeLayers;
    public GameObject closeFocus;
    public Material closeHover;
    private Vector2 closePosition;
    private bool closeInitialized;
    private bool over, down, focused, rowSelected;
    private Button button;
    private Color labelColor;
    private void Awake() { button = GetComponent<Button>(); if (label) labelColor = label.color; }
    private void OnEnable() { Refresh(); }
    private void OnDisable() { over = down = focused = false; }
    public void SetRowSelected(bool value) { rowSelected = value; Refresh(); }
    public void Refresh()
    {
        if (!button) button = GetComponent<Button>();
        bool enabled = button.IsInteractable();
        if (closeLayers != null && closeLayers.Length > 0)
        {
            var rect = (RectTransform)transform;
            if (!closeInitialized) { closePosition = rect.anchoredPosition; closeInitialized = true; }
            foreach (var layer in closeLayers) layer.material = enabled && over ? closeHover : null;
            if (closeFocus) closeFocus.SetActive(enabled && focused && !over);
            rect.anchoredPosition = closePosition + (enabled && down ? Vector2.down : Vector2.zero);
            return;
        }
        if (!artwork) return;
        artwork.texture = row ? (rowSelected ? focus : normal) : !enabled ? disabled : down ? pressed : over ? hover : focused ? focus : normal;
        artwork.color = row ? new Color(over ? 1.12f : 1, over ? 1.12f : 1, over ? 1.12f : 1, rowSelected ? .62f : 1)
            : Color.white;
        if (label && !row) { if (labelColor.a == 0) labelColor = label.color; var color = labelColor; color.a = enabled ? 1f : .48f; label.color = color; }
    }
    public void OnPointerEnter(PointerEventData data) { over = true; Refresh(); }
    public void OnPointerExit(PointerEventData data) { over = down = false; Refresh(); }
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) down = true; Refresh(); }
    public void OnPointerUp(PointerEventData data) { down = false; Refresh(); }
    public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
    public void OnDeselect(BaseEventData data) { focused = down = false; Refresh(); }
}
