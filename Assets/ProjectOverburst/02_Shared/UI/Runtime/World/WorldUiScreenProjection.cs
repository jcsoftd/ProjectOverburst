using UnityEngine;

public static class WorldUiScreenProjection
{
    private const float ScreenMargin = 64f;

    public static bool TryProject(
        RectTransform overlayRoot,
        Camera targetCamera,
        Vector3 worldPosition,
        out Vector2 anchoredPosition)
    {
        return TryProject(overlayRoot, targetCamera, worldPosition, out anchoredPosition, ScreenMargin);
    }

    public static bool TryProject(
        RectTransform overlayRoot,
        Camera targetCamera,
        Vector3 worldPosition,
        out Vector2 anchoredPosition,
        float screenMargin)
    {
        anchoredPosition = Vector2.zero;
        if (overlayRoot == null || targetCamera == null)
            return false;

        Vector3 screenPoint = targetCamera.WorldToScreenPoint(worldPosition);
        float resolvedMargin = Mathf.Max(0f, screenMargin);
        if (screenPoint.z <= 0f
            || screenPoint.x < -resolvedMargin
            || screenPoint.y < -resolvedMargin
            || screenPoint.x > Screen.width + resolvedMargin
            || screenPoint.y > Screen.height + resolvedMargin)
        {
            return false;
        }

        Canvas canvas = overlayRoot.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                overlayRoot,
                screenPoint,
                eventCamera,
                out anchoredPosition))
        {
            return false;
        }

        anchoredPosition.x = Mathf.Round(anchoredPosition.x);
        anchoredPosition.y = Mathf.Round(anchoredPosition.y);
        return true;
    }
}
