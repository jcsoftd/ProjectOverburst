using UnityEngine;
using UnityEngine.UI;

/// <summary>One mesh for all unique orthogonal routes, clipped by the native map viewport.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OverburstSkillTreeRoutes : MaskableGraphic
{
    OverburstSkillTreeCatalog catalog;
    OverburstSkillTreePlan plan;
    Vector2 pan;
    float scale;
    float pixelScale = 1;
    static readonly Color Unplanned = new Color(.34f, .31f, .25f, 1);
    static readonly Color Planned = new Color(.76f, .61f, .32f, 1);
    static readonly Color Pending = new Color(.79f,.75f,.65f,1), Refund = new Color(.58f,.29f,.21f,1);
    public void Configure(OverburstSkillTreeCatalog data, OverburstSkillTreePlan allocation, float mapScale, Vector2 mapPan, float canvasPixelScale = 1)
    { catalog = data; plan = allocation; scale = mapScale; pan = mapPan; pixelScale = Mathf.Clamp(canvasPixelScale, .1f, 1); raycastTarget = false; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear(); if (catalog == null) return;
        foreach (var segment in catalog.segments)
        {
            bool learned = false, committed = false;
            for (int i = 0; i < segment.sources.Length; i++) { learned |= plan.Has(segment.sources[i]) && plan.Has(segment.targets[i]); committed |= plan.IsCommitted(segment.sources[i]) && plan.IsCommitted(segment.targets[i]); }
            Vector2 a = segment.A * scale + pan, b = segment.B * scale + pan;
            var direction = b - a; if (direction.sqrMagnitude < .0001f) continue;
            // Keep a visible stroke below the reference resolution; subpixel lines can disappear at 720p.
            var normal = new Vector2(-direction.y, direction.x).normalized * (learned ? 1.1f : .8f) / pixelScale;
            int start = helper.currentVertCount;
            var tint = learned ? committed ? Planned : Pending : committed ? Refund : Unplanned;
            helper.AddVert(a - normal, tint, Vector2.zero); helper.AddVert(a + normal, tint, Vector2.zero);
            helper.AddVert(b + normal, tint, Vector2.zero); helper.AddVert(b - normal, tint, Vector2.zero);
            helper.AddTriangle(start, start + 1, start + 2); helper.AddTriangle(start, start + 2, start + 3);
        }
    }
}
