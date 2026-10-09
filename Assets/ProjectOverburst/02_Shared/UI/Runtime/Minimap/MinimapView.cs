using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MinimapView : MonoBehaviour
{
    [SerializeField] private GameObject viewRoot;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RawImage terrainImage;
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
    private Rect terrainBounds;
    private Vector3 terrainOrigin;
    private float terrainYaw = float.NaN, terrainZoom = -1f, terrainRadius = -1f;

    public bool IsReady => viewRoot != null && viewport != null && markers != null && markers.Atlas != null
        && playerMarker != null && facingCone != null && minimapCanvas != null
        && raycaster != null && zoomInButton != null && zoomOutButton != null && zoomValueText != null;
    public float Radius => Mathf.Max(1f, Mathf.Min(viewport.rect.width, viewport.rect.height) * 0.5f - edgeInset);
    public MinimapMarkerGraphic Markers => markers;
    public Button ZoomInButton => zoomInButton;
    public Button ZoomOutButton => zoomOutButton;
    public GameObject ViewRoot => viewRoot;
    public RawImage TerrainImage => terrainImage;

    public void SetTerrain(Texture2D texture, Rect worldBounds)
    {
        if (terrainImage == null) return;
        terrainImage.texture = texture;
        terrainBounds = worldBounds;
        terrainYaw = float.NaN;
        bool valid = texture != null && worldBounds.width > 0f && worldBounds.height > 0f;
        terrainImage.gameObject.SetActive(valid);
    }

    public void UpdateTerrain(Vector3 origin, float yaw, float zoom)
    {
        if (terrainImage == null || terrainImage.texture == null || !terrainImage.gameObject.activeSelf) return;
        float radius = Radius;
        if (terrainOrigin.x == origin.x && terrainOrigin.z == origin.z
            && terrainYaw == yaw && terrainZoom == zoom && terrainRadius == radius) return;
        terrainOrigin = origin; terrainYaw = yaw; terrainZoom = zoom; terrainRadius = radius;
        RectTransform rect = terrainImage.rectTransform;
        Vector2 size = viewport.rect.size;
        float metresPerUnit = Mathf.Max(1f, zoom) / radius;
        Vector2 uvSize = new Vector2(size.x * metresPerUnit / terrainBounds.width,
            size.y * metresPerUnit / terrainBounds.height);
        Vector2 uvCenter = new Vector2((origin.x - terrainBounds.xMin) / terrainBounds.width,
            (origin.z - terrainBounds.yMin) / terrainBounds.height);
        // Keep the quad viewport-sized; scrolling the UVs avoids rasterizing a map-sized quad.
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        terrainImage.uvRect = new Rect(uvCenter - uvSize * .5f, uvSize);
        rect.localRotation = Quaternion.Euler(0f, 0f, yaw);
    }

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
