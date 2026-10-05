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
    static readonly Color Unplanned = new Color(.27f, .26f, .23f, 1);
    static readonly Color Planned = new Color(.61f, .51f, .32f, 1);
    public void Configure(OverburstSkillTreeCatalog data, OverburstSkillTreePlan allocation, float mapScale, Vector2 mapPan)
    { catalog = data; plan = allocation; scale = mapScale; pan = mapPan; raycastTarget = false; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear(); if (catalog == null) return;
        foreach (var segment in catalog.segments)
        {
            bool learned = false;
            for (int i = 0; i < segment.sources.Length; i++) if (plan.Has(segment.sources[i]) && plan.Has(segment.targets[i])) { learned = true; break; }
            Vector2 a = segment.A * scale + pan, b = segment.B * scale + pan;
            var direction = b - a; if (direction.sqrMagnitude < .0001f) continue;
            var normal = new Vector2(-direction.y, direction.x).normalized * (learned ? .85f : .65f);
            int start = helper.currentVertCount;
            var tint = learned ? Planned : Unplanned;
            helper.AddVert(a - normal, tint, Vector2.zero); helper.AddVert(a + normal, tint, Vector2.zero);
            helper.AddVert(b + normal, tint, Vector2.zero); helper.AddVert(b - normal, tint, Vector2.zero);
            helper.AddTriangle(start, start + 1, start + 2); helper.AddTriangle(start, start + 2, start + 3);
        }
    }
}
