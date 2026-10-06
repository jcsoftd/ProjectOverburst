using UnityEngine;
using UnityEngine.UI;

/// <summary>Persistent ownership rim and independent focus brackets, using the shared UI material.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OverburstSkillTreeNodeAccent : MaskableGraphic
{
    public OverburstSkillTreeNodeView.NodeState State { get; private set; }
    public bool Focused { get; private set; }
    bool square, configured;
    public void Present(OverburstSkillTreeNodeView.NodeState state, bool focused, bool squareFrame)
    {
        raycastTarget = false;
        if (configured && State == state && Focused == focused && square == squareFrame) return;
        State = state; Focused = focused; square = squareFrame; configured = true; SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var state = State;
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .5f - 1;
        bool learned = state == OverburstSkillTreeNodeView.NodeState.Learned;
        bool pending = state == OverburstSkillTreeNodeView.NodeState.PurchaseDraft;
        bool refund = state == OverburstSkillTreeNodeView.NodeState.RefundDraft;
        bool available = state == OverburstSkillTreeNodeView.NodeState.Available || state == OverburstSkillTreeNodeView.NodeState.Insufficient;
        if (learned)
            Rim(mesh, radius - 1.7f, 3.4f, new Color(1, .69f, .20f, .23f), false);
        if (learned || pending || refund || available)
        {
            var tint = learned ? new Color(1, .80f, .32f) : pending ? new Color(.93f, .94f, .92f) : refund ? new Color(.94f, .40f, .27f) : new Color(.52f, .61f, .68f, .85f);
            Rim(mesh, radius - .7f, learned || refund ? 1.7f : 1, tint, pending);
        }
        if (!Focused) return;
        // Focus never changes a node's ownership fill or icon. Four ivory corners remain distinct from gold.
        var focus = new Color(.82f, .89f, .94f);
        float x = rectTransform.rect.width * .5f - 1, y = rectTransform.rect.height * .5f - 1;
        float length = Mathf.Min(6, radius * .28f);
        foreach (float sx in new[] { -1f, 1f }) foreach (float sy in new[] { -1f, 1f })
        {
            var corner = new Vector2(sx * x, sy * y);
            Strip(mesh, corner, corner - new Vector2(sx * length, 0), 1.25f, focus);
            Strip(mesh, corner, corner - new Vector2(0, sy * length), 1.25f, focus);
        }
    }
    void Rim(VertexHelper mesh, float radius, float width, Color tint, bool dashed)
    {
        if (square)
        {
            var points = new[] { new Vector2(-radius,-radius),new Vector2(radius,-radius),new Vector2(radius,radius),new Vector2(-radius,radius) };
            for (int i = 0; i < 4; i++)
            {
                var a = points[i]; var b = points[(i + 1) % 4];
                if (!dashed) Strip(mesh, a, b, width, tint);
                else for (float t = 0; t < 1; t += .25f) Strip(mesh, Vector2.Lerp(a,b,t), Vector2.Lerp(a,b,t+.16f), width, tint);
            }
            return;
        }
        const int count = 64;
        for (int i = 0; i < count; i++)
        {
            if (dashed && i % 8 >= 5) continue;
            float a = i * Mathf.PI * 2 / count, b = (i + 1) * Mathf.PI * 2 / count;
            var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var v = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
            Quad(mesh, u*(radius-width*.5f),u*(radius+width*.5f),v*(radius+width*.5f),v*(radius-width*.5f),tint);
        }
    }
    static void Strip(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
    {
        var d = (b-a).normalized; var normal = new Vector2(-d.y,d.x)*width*.5f;
        Quad(mesh,a-normal,a+normal,b+normal,b-normal,tint);
    }
    static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int start=mesh.currentVertCount;
        mesh.AddVert(a,tint,Vector2.zero);mesh.AddVert(b,tint,Vector2.zero);mesh.AddVert(c,tint,Vector2.zero);mesh.AddVert(d,tint,Vector2.zero);
        mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
    }
}
