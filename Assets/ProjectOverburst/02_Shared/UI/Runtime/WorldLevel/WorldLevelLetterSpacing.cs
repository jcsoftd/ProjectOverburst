using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public sealed class WorldLevelLetterSpacing : BaseMeshEffect
{
    public float spacing;
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || spacing == 0) return;
        var label = GetComponent<Text>(); var lines = label.cachedTextGenerator.lines;
        int quads = mesh.currentVertCount / 4;
        for (int line = 0; line < lines.Count; line++)
        {
            int start = lines[line].startCharIdx, end = line + 1 < lines.Count ? lines[line + 1].startCharIdx : quads;
            end = Mathf.Min(end, quads); float offset = 0;
            if (label.alignment == TextAnchor.UpperCenter || label.alignment == TextAnchor.MiddleCenter || label.alignment == TextAnchor.LowerCenter)
                offset = -(end - start - 1) * spacing * .5f;
            for (int i = start; i < end; i++) for (int vertex = 0; vertex < 4; vertex++)
            { var value = new UIVertex(); mesh.PopulateUIVertex(ref value, i * 4 + vertex); value.position.x += (i - start) * spacing + offset; mesh.SetUIVertex(value, i * 4 + vertex); }
        }
    }
}
