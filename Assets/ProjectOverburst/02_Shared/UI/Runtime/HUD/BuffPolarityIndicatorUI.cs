using UnityEngine;

// A small double chevron; its direction and color describe the effect, not the icon artwork.
[AddComponentMenu("OVERBURST/UI/Buff Polarity Indicator")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BuffPolarityIndicatorUI : UnityEngine.UI.MaskableGraphic
{
    [SerializeField] private bool isDebuff;
    public bool IsDebuff => isDebuff;
    public static readonly Color BuffColor = new Color(.22f, 1f, .38f, 1f);
    public static readonly Color DebuffColor = new Color(.98f, .26f, .23f, 1f);

    public void SetAppearance(bool debuff, float alpha = 1f)
    {
        if (isDebuff != debuff)
        {
            isDebuff = debuff;
            SetVerticesDirty();
        }
        Color tint = debuff ? DebuffColor : BuffColor;
        tint.a = Mathf.Clamp01(alpha);
        color = tint;
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
    {
        vertices.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Vector2 center = rect.center;
        Vector2 scale = new Vector2(rect.width / 10f, rect.height / 12f);
        Color32 shadow = new Color(0f, 0f, 0f, color.a * .9f);
        // Draw both outlines first so the lower chevron does not cover the upper one.
        Chevron(vertices, center, scale, 5.2f, 4.7f, 3.7f, 2.7f, shadow);
        Chevron(vertices, center, scale, .7f, 4.7f, 3.7f, 2.7f, shadow);
        Chevron(vertices, center, scale, 4.5f, 4f, 3.2f, 1.4f, color);
        Chevron(vertices, center, scale, 0f, 4f, 3.2f, 1.4f, color);
    }

    private void Chevron(UnityEngine.UI.VertexHelper vertices, Vector2 center, Vector2 scale,
        float peak, float halfWidth, float drop, float thickness, Color32 tint)
    {
        Vector2 Point(float x, float y) => center + Vector2.Scale(new Vector2(x, isDebuff ? -y : y), scale);
        Quad(vertices, Point(-halfWidth, peak - drop), Point(0f, peak),
            Point(0f, peak - thickness), Point(-halfWidth, peak - drop - thickness), tint);
        Quad(vertices, Point(0f, peak), Point(halfWidth, peak - drop),
            Point(halfWidth, peak - drop - thickness), Point(0f, peak - thickness), tint);
    }

    private static void Quad(UnityEngine.UI.VertexHelper vertices, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
    {
        int first = vertices.currentVertCount;
        vertices.AddVert(a, tint, Vector2.zero);
        vertices.AddVert(b, tint, Vector2.zero);
        vertices.AddVert(c, tint, Vector2.zero);
        vertices.AddVert(d, tint, Vector2.zero);
        vertices.AddTriangle(first, first + 1, first + 2);
        vertices.AddTriangle(first + 2, first + 3, first);
    }
}
