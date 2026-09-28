using UnityEngine;

/// <summary>Displays the connected gameplay minimap view in the management scene.</summary>
public sealed class OverburstUIMinimapPreview : MonoBehaviour
{
    [SerializeField] private MinimapView view;

    private readonly MinimapMarkerGraphic.Marker[] sampleMarkers =
    {
        new MinimapMarkerGraphic.Marker
        {
            Position = new Vector2(-28f, 32f), Grade = EnemyGradeType.Normal, Id = 101,
            Size = 4.6f, Color = new Color32(255, 74, 65, 245)
        },
        new MinimapMarkerGraphic.Marker
        {
            Position = new Vector2(38f, -15f), Grade = EnemyGradeType.Elite, Id = 102,
            Size = 6.2f, Color = new Color32(255, 139, 48, 255)
        },
        new MinimapMarkerGraphic.Marker
        {
            Position = new Vector2(5f, 64f), Grade = EnemyGradeType.Boss, Id = 103,
            Size = 8f, Color = new Color32(255, 211, 105, 255)
        }
    };

    private float zoomRadius = 55f;
    private bool blocked;

    private void Start()
    {
        if (view != null && view.IsReady)
        {
            view.ZoomInButton.onClick.AddListener(ZoomIn);
            view.ZoomOutButton.onClick.AddListener(ZoomOut);
        }
        Show();
    }

    private void OnDestroy()
    {
        if (view == null || !view.IsReady)
            return;
        view.ZoomInButton.onClick.RemoveListener(ZoomIn);
        view.ZoomOutButton.onClick.RemoveListener(ZoomOut);
    }

    public void Show(bool inputBlocked = false)
    {
        if (view == null || !view.IsReady)
            return;

        blocked = inputBlocked;
        view.SetVisible(true);
        view.SetBlocked(blocked);
        view.SetFacing(35f);
        RefreshZoom();
    }

    private void RefreshZoom()
    {
        view.SetZoom(zoomRadius, 55f, 20f, 80f);
        float scale = 55f / zoomRadius;
        sampleMarkers[0].Position = new Vector2(-28f, 32f) * scale;
        sampleMarkers[1].Position = new Vector2(38f, -15f) * scale;
        sampleMarkers[2].Position = new Vector2(5f, 64f) * scale;
        view.Markers.SetMarkers(sampleMarkers, sampleMarkers.Length);
    }

    public void Hide() => view?.SetVisible(false);

    private void ZoomIn()
    {
        if (blocked) return;
        zoomRadius = Mathf.Max(20f, zoomRadius - 10f);
        RefreshZoom();
    }

    private void ZoomOut()
    {
        if (blocked) return;
        zoomRadius = Mathf.Min(80f, zoomRadius + 10f);
        RefreshZoom();
    }
}
