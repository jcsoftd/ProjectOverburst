using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class QuestUiLineIcon : MaskableGraphic
{
    public int kind;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if(kind==3)
        {
            var r=rectTransform.rect;vh.AddVert(new Vector2(r.xMin,r.yMax),color,Vector2.zero);
            vh.AddVert(new Vector2(r.xMax,r.yMax),color,Vector2.zero);vh.AddVert(new Vector2(r.center.x,r.yMin),color,Vector2.zero);
            vh.AddTriangle(0,1,2);return;
        }
        if (kind == 0)
        {
            Path(vh, new Vector2(3, 3), new Vector2(21, 3), new Vector2(21, 17), new Vector2(8, 17), new Vector2(3, 22), new Vector2(3, 3));
            Path(vh, new Vector2(7, 7), new Vector2(17, 7)); Path(vh, new Vector2(7, 11), new Vector2(14, 11));
        }
        else if (kind == 1)
        {
            Path(vh, new Vector2(3, 6), new Vector2(21, 6), new Vector2(17, 2));
            Path(vh, new Vector2(21, 6), new Vector2(17, 10));
            Path(vh, new Vector2(21, 18), new Vector2(3, 18), new Vector2(7, 14));
            Path(vh, new Vector2(3, 18), new Vector2(7, 22));
        }
        else
        {
            Path(vh, new Vector2(12, 5), new Vector2(12, 22));
            Path(vh, new Vector2(12, 5), new Vector2(7, 2), new Vector2(2, 3), new Vector2(2, 20), new Vector2(7, 19), new Vector2(12, 22), new Vector2(17, 19), new Vector2(22, 20), new Vector2(22, 3), new Vector2(17, 2), new Vector2(12, 5));
            Path(vh, new Vector2(5, 7), new Vector2(9, 8)); Path(vh, new Vector2(5, 11), new Vector2(9, 12));
            Path(vh, new Vector2(15, 8), new Vector2(19, 7)); Path(vh, new Vector2(15, 12), new Vector2(19, 11));
        }
    }
    private void Path(VertexHelper vh, params Vector2[] points)
    {
        var r = rectTransform.rect;
        for (int i = 1; i < points.Length; i++)
        {
            var a = new Vector2(r.xMin + points[i - 1].x / 24 * r.width, r.yMax - points[i - 1].y / 24 * r.height);
            var b = new Vector2(r.xMin + points[i].x / 24 * r.width, r.yMax - points[i].y / 24 * r.height);
            var normal = new Vector2(-(b - a).y, (b - a).x).normalized * .7f;
            int v = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero); vh.AddVert(a + normal, color, Vector2.zero);
            vh.AddVert(b + normal, color, Vector2.zero); vh.AddVert(b - normal, color, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v + 2, v + 3, v);
        }
    }
}
