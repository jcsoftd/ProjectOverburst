using UnityEngine;

public sealed class MinimapGuideView : MonoBehaviour
{
    public MinimapMarkerGraphic icons, arrows;
    readonly MinimapMarkerGraphic.Marker[] iconBuffer = new MinimapMarkerGraphic.Marker[MinimapGuideSource.Capacity];
    readonly MinimapMarkerGraphic.Marker[] arrowBuffer = new MinimapMarkerGraphic.Marker[MinimapGuideSource.Capacity];
    public bool IsReady => icons && arrows && icons.Atlas && arrows.Atlas;
    public void Clear() { if (icons) icons.Clear(); if (arrows) arrows.Clear(); }
    public void Render(MinimapGuideSource source, Vector3 origin, float yaw, float scale, Vector2 limits, bool circle, float opacity)
    {
        if (!IsReady) return;
        int iconCount = 0, arrowCount = 0;
        for (int i = 0; i < source.Count; i++)
        {
            var hint = source.Get(i);
            Vector2 point = MinimapProjection.Project(hint.Position, origin, yaw, 1, scale);
            Vector2 clamped = Clamp(point, limits - Vector2.one * 10, circle);
            bool outside = (point - clamped).sqrMagnitude > .01f;
            bool arrow = outside || hint.Type == MinimapGuideSource.Kind.Frontier;
            var color = hint.Type == MinimapGuideSource.Kind.Portal ? new Color32(120, 220, 255, 255) : new Color32(255, 211, 105, 255);
            color.a = (byte)Mathf.RoundToInt(255 * opacity);
            bool portal = hint.Type == MinimapGuideSource.Kind.Portal || hint.Type == MinimapGuideSource.Kind.EmergencyExit;
            var marker = new MinimapMarkerGraphic.Marker { Id = hint.Id, Position = clamped, Color = color,
                Grade = portal || hint.Type == MinimapGuideSource.Kind.Enemy ? EnemyGradeType.Boss : EnemyGradeType.Elite,
                Size = portal ? 17 : hint.Type == MinimapGuideSource.Kind.Enemy ? 10 : 14 };
            if (arrow)
            {
                Vector2 direction = outside ? point : MinimapProjection.Project(hint.Position + hint.Direction, hint.Position, yaw, 1, 1);
                marker.Rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90;
                marker.Size = 14; arrowBuffer[arrowCount++] = marker;
            }
            else iconBuffer[iconCount++] = marker;
        }
        icons.SetMarkers(iconBuffer, iconCount); arrows.SetMarkers(arrowBuffer, arrowCount);
    }
    public static Vector2 Clamp(Vector2 point, Vector2 limits, bool circle)
    {
        limits = Vector2.Max(Vector2.one, limits);
        if (circle) return Vector2.ClampMagnitude(point, Mathf.Min(limits.x, limits.y));
        float ratio = Mathf.Max(Mathf.Abs(point.x) / limits.x, Mathf.Abs(point.y) / limits.y);
        return ratio > 1 ? point / ratio : point;
    }
}
