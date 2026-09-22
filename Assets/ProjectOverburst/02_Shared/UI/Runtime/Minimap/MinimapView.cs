using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MinimapView : MonoBehaviour
{
    [SerializeField] private GameObject viewRoot;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private MinimapMarkerGraphic markers;
    [SerializeField] private RectTransform playerMarker;
    [SerializeField] private RectTransform facingCone;
    [SerializeField] private Canvas minimapCanvas;
    [SerializeField] private GraphicRaycaster raycaster;
    [SerializeField] private Button zoomInButton;
    [SerializeField] private Button zoomOutButton;
    [SerializeField] private TextMeshProUGUI zoomValueText;
    [SerializeField] private float edgeInset = 4f;
    private bool visible, blocked;
    private float lastZoom = -1f, lastFacing = float.NaN;
    private int width, height;
    private Rect safeArea;

    public bool IsReady => viewRoot != null && viewport != null && markers != null && markers.Atlas != null
        && playerMarker != null && facingCone != null && minimapCanvas != null
        && raycaster != null && zoomInButton != null && zoomOutButton != null && zoomValueText != null;
    public float Radius => Mathf.Max(1f, Mathf.Min(viewport.rect.width, viewport.rect.height) * 0.5f - edgeInset);
    public MinimapMarkerGraphic Markers => markers;
    public Button ZoomInButton => zoomInButton;
    public Button ZoomOutButton => zoomOutButton;
    public GameObject ViewRoot => viewRoot;

    public void SetVisible(bool value)
    {
        if (visible == value && viewRoot != null && viewRoot.activeSelf == value) return;
        visible = value;
        if (viewRoot != null && viewRoot.activeSelf != value) viewRoot.SetActive(value);
        if (!value && markers != null) markers.Clear();
        if (raycaster != null) raycaster.enabled = value && !blocked;
    }

    public void SetBlocked(bool value)
    {
        blocked = value;
        if (minimapCanvas != null)
        {
            minimapCanvas.overrideSorting = true;
            minimapCanvas.sortingOrder = value ? -10 : 45;
        }
        if (raycaster != null) raycaster.enabled = visible && !value;
        lastZoom = -1f;
    }

    public void SetZoom(float radius, float defaultRadius, float min, float max)
    {
        if (lastZoom == radius) return;
        lastZoom = radius;
        zoomValueText.text = $"x{defaultRadius / radius:0.0}";
        zoomInButton.interactable = !blocked && radius > min;
        zoomOutButton.interactable = !blocked && radius < max;
    }

    public void SetFacing(float degrees)
    {
        if (!float.IsNaN(lastFacing) && Mathf.Abs(Mathf.DeltaAngle(lastFacing, degrees)) < 0.1f) return;
        lastFacing = degrees;
        Quaternion rotation = Quaternion.Euler(0, 0, degrees);
        playerMarker.localRotation = rotation;
        facingCone.localRotation = rotation;
    }

    public void UpdateSafeArea()
    {
        if (width == Screen.width && height == Screen.height && safeArea == Screen.safeArea) return;
        width = Screen.width; height = Screen.height; safeArea = Screen.safeArea;
        if (!(transform.parent is RectTransform parent)) return;
        Canvas root = minimapCanvas.rootCanvas;
        Camera camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safeArea.max, camera, out Vector2 corner)) return;
        // The authored root is top-right anchored. Convert screen padding through the actual parent transform.
        RectTransform rect = (RectTransform)transform;
        rect.anchoredPosition = corner - parent.rect.max - new Vector2(16f, 16f);
    }
}
