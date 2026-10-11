using UnityEngine;
using UnityEngine.UI;
using Unity.Profiling;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class MinimapMarkerGraphic : MaskableGraphic
{
    public struct Marker
    {
        public Vector2 Position;
        public float Size;
        public Color32 Color;
        public EnemyGradeType Grade;
        public int Id;
        public float Rotation;
    }

    [SerializeField] private Texture2D atlas;
    [SerializeField] private bool singleIcon;
    private readonly Marker[] markers = new Marker[MinimapEnemySource.Capacity];
    private static readonly ProfilerMarker MeshMarker = new ProfilerMarker("Minimap.Mesh");
    public override Texture mainTexture => atlas != null ? atlas : s_WhiteTexture;
    public int MarkerCount { get; private set; }
    public int MeshBuildCount { get; private set; }
    public Texture2D Atlas => atlas;

    public Marker GetMarker(int index) => markers[index];

    public void SetMarkers(Marker[] values, int count)
    {
        count = Mathf.Clamp(count, 0, markers.Length);
        bool changed = count != MarkerCount;
        for (int i = 0; i < count; i++)
        {
            Marker a = markers[i], b = values[i];
            if (a.Id != b.Id || a.Grade != b.Grade || a.Size != b.Size || a.Rotation != b.Rotation || a.Color.r != b.Color.r
                || a.Color.g != b.Color.g || a.Color.b != b.Color.b || a.Color.a != b.Color.a
                || (a.Position - b.Position).sqrMagnitude >= 0.0625f) changed = true;
        }
        if (!changed) return;
        for (int i = 0; i < count; i++) markers[i] = values[i];
        MarkerCount = count;
        SetVerticesDirty();
    }

    public void Clear()
    {
        if (MarkerCount == 0) return;
        MarkerCount = 0;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        using (MeshMarker.Auto())
        {
            vh.Clear();
            MeshBuildCount++;
            for (int i = 0; i < MarkerCount; i++)
            {
                Marker m = markers[i];
                float r = m.Size * 0.5f;
                float u = singleIcon ? 0 : Mathf.Clamp((int)m.Grade, 0, 3) * 0.25f;
                float span = singleIcon ? 1 : .25f;
                float radians = m.Rotation * Mathf.Deg2Rad;
                var right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * r;
                var up = new Vector2(-right.y, right.x);
                int start = vh.currentVertCount;
                vh.AddVert(m.Position - right - up, m.Color, new Vector2(u, 0));
                vh.AddVert(m.Position - right + up, m.Color, new Vector2(u, 1));
                vh.AddVert(m.Position + right + up, m.Color, new Vector2(u + span, 1));
                vh.AddVert(m.Position + right - up, m.Color, new Vector2(u + span, 0));
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }
    }
}
