using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class QuestUiGradient : MaskableGraphic
{
    public Color top = Color.white, bottom = Color.white;
    public bool horizontal;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect;
        vh.AddVert(new Vector3(r.xMin, r.yMin), bottom * color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMin, r.yMax), (horizontal ? bottom : top) * color, Vector2.up);
        vh.AddVert(new Vector3(r.xMax, r.yMax), top * color, Vector2.one);
        vh.AddVert(new Vector3(r.xMax, r.yMin), (horizontal ? top : bottom) * color, Vector2.right);
        vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
    }
}
