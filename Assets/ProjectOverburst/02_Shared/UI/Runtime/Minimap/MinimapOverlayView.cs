using UnityEngine;

// Shares the run's explored outline and the circular map's already-filtered enemy candidates.
public sealed class MinimapOverlayView : MonoBehaviour
{
    public RectTransform viewport;
    public UnityEngine.UI.RawImage outline;
    public MinimapMarkerGraphic markers;
    public MinimapGuideView guides;
    public RectTransform playerMarker;
    readonly MinimapMarkerGraphic.Marker[] projected = new MinimapMarkerGraphic.Marker[MinimapEnemySource.Capacity];
    Rect bounds;
    public bool IsReady => viewport && outline && markers && playerMarker;
    public float Radius => Mathf.Max(1, viewport.rect.height * .5f);

    public void Bind(MinimapExplorationMap map)
    {
        outline.texture = map?.OutlineTexture;
        bounds = map != null ? map.Bounds : default;
        if (map == null) SetVisible(false);
    }
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        if (!visible) markers.Clear();
        if (!visible && guides) guides.Clear();
    }
    public void Render(Vector3 origin, float yaw, float facing, float worldRadius, float opacity)
    {
        if (!outline.texture) return;
        var parent = (RectTransform)transform.parent;
        Canvas canvas = GetComponentInParent<Canvas>().rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Rect safe = Screen.safeArea;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.min, camera, out var low);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.max, camera, out var high);
        viewport.localPosition = new Vector3((low.x + high.x) * .5f, (low.y + high.y) * .5f, 0);
        viewport.sizeDelta = Vector2.Scale(high - low, new Vector2(.72f, .68f));
        // A diagonal-sized quad covers the rectangular clip at every camera yaw.
        float diameter = viewport.rect.size.magnitude;
        outline.rectTransform.sizeDelta = Vector2.one * diameter;
        float span = diameter * worldRadius / Radius;
        var uvSize = new Vector2(span / bounds.width, span / bounds.height);
        var uvCenter = new Vector2((origin.x - bounds.xMin) / bounds.width, (origin.z - bounds.yMin) / bounds.height);
        outline.uvRect = new Rect(uvCenter - uvSize * .5f, uvSize);
        outline.rectTransform.localRotation = Quaternion.Euler(0, 0, yaw);
        outline.color = new Color(1, 1, 1, opacity);
        playerMarker.localRotation = Quaternion.Euler(0, 0, facing);
    }
    public void SetEnemies(MinimapMarkerGraphic.Marker[] values, int count, float circleRadius, float circleWorldRadius, float worldRadius)
    {
        float ratio = Radius / worldRadius * circleWorldRadius / circleRadius;
        for (int i = 0; i < count; i++)
        {
            var marker = values[i]; marker.Position *= ratio;
            marker.Color.a = 191; projected[i] = marker;
        }
        markers.SetMarkers(projected, count);
    }
}
