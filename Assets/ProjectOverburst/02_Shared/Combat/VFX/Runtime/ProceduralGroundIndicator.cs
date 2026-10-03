using System.Collections.Generic;
using UnityEngine;

public enum GroundIndicatorShape { Sector, Circle, Donut, Rectangle }

/// <summary>원본 Telegraph의 불꽃과 시계를 유지하는 미터 단위 판정창.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class ProceduralGroundIndicator : MonoBehaviour
{
    [SerializeField] private GameObject coneSource, novaSource, rectangleSource;
    [SerializeField] private int designVersion;
    [SerializeField] private GroundIndicatorShape shape = GroundIndicatorShape.Sector;
    [SerializeField, Min(.05f)] private float outerRadius = 4f;
    [SerializeField, Min(0f)] private float innerRadius = .72f;
    [SerializeField, Range(1f, 360f)] private float angle = 80f;
    [SerializeField, Min(.05f)] private float width = 1f, length = 4f;
    [SerializeField, Min(0f)] private float corridorCapRadius;
    [SerializeField, Min(.01f)] private float flameWidth = .12f;
    [SerializeField, Range(0f, 1f)] private float progress = .72f;
    [SerializeField] private bool visible = true;
    private readonly List<Mesh> ownedMeshes = new List<Mesh>();
    private readonly GameObject[] instances = new GameObject[3];
    private readonly ParticleSystem[][] clocks = new ParticleSystem[3][];
    private ParticleSystem fill, border;
    private GameObject activeRoot;
    private int activeKind = -1;
    private Vector4 lastGeometry, lastExtra;
    private GroundIndicatorShape lastShape;
    private float simulatedTime = -1f;
    private bool refreshing;

    public GroundIndicatorShape Shape => shape;
    public float OuterRadius => outerRadius;
    public float InnerRadius => shape == GroundIndicatorShape.Circle || shape == GroundIndicatorShape.Rectangle ? 0f : innerRadius;
    public float Angle => angle;
    public float Width => width;
    public float Length => length;
    public float CorridorCapRadius => corridorCapRadius;
    public float FlameWidth => flameWidth;
    public float Progress => progress;
    public bool IsVisible => isActiveAndEnabled && visible;
    public bool UsesApprovedDesign => designVersion == 2 && coneSource != null && novaSource != null && rectangleSource != null;
    public bool UsesAuthoredEdgeFade => UsesApprovedDesign;
    public ParticleSystemRenderer Surface => fill != null ? fill.GetComponent<ParticleSystemRenderer>() : null;
    public ParticleSystemRenderer Border => border != null ? border.GetComponent<ParticleSystemRenderer>() : null;

    public void InitializeSources(GameObject cone, GameObject nova, GameObject rectangle)
    {
        ReleaseRuntime(); coneSource = cone; novaSource = nova; rectangleSource = rectangle; designVersion = 2; Refresh();
    }
    public void Configure(GroundIndicatorShape kind, float outer, float inner = 0f, float degrees = 360f,
        float rectangleWidth = 1f, float rectangleLength = 4f, float endCapRadius = 0f)
    {
        shape = kind; outerRadius = outer; innerRadius = inner; angle = degrees;
        width = rectangleWidth; length = rectangleLength; corridorCapRadius = endCapRadius; Refresh();
    }
    public void SetCorridorCapRadius(float meters)
    { corridorCapRadius = Mathf.Max(0f, Finite(meters, 0f)); Refresh(); }
    public void SetFlameWidth(float meters) { flameWidth = meters; Refresh(); }
    // Kept for old saved previews. Native texture masks now supply the fade.
    public void SetAuthoredEdgeFade(AnimationCurve curve) { }
    public void SetProgress(float value)
    {
        progress = Mathf.Clamp01(Finite(value, 0f));
        if (!IsVisible || activeKind < 0 || clocks[activeKind] == null) return;
        float target = progress * 4.2f;
        if (Mathf.Abs(target - simulatedTime) < .0001f) return;
        bool restart = simulatedTime < 0f || target < simulatedTime;
        foreach (var p in clocks[activeKind])
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            p.Simulate(restart ? target : target - simulatedTime, false, restart, false);
            p.Pause(false);
        }
        simulatedTime = target;
    }
    public void SetVisible(bool value)
    {
        visible = value;
        if (activeRoot == null && value) Refresh();
        if (activeRoot == null) return;
        if (!value) StopCurrent();
        activeRoot.SetActive(value);
        if (value) SetProgress(progress);
    }
    public void Refresh()
    {
        if (refreshing || !gameObject.scene.IsValid() || !UsesApprovedDesign) return;
        refreshing = true;
        try
        {
            outerRadius = Mathf.Clamp(Finite(outerRadius, 4f), .05f, 1000f);
            innerRadius = Mathf.Clamp(Finite(innerRadius, 0f), 0f, outerRadius - .01f);
            if (shape == GroundIndicatorShape.Sector)
                innerRadius = Mathf.Max(innerRadius, Mathf.Min(outerRadius * .05f, outerRadius - .01f));
            angle = Mathf.Clamp(Finite(angle, 80f), 1f, 360f);
            width = Mathf.Clamp(Finite(width, 1f), .05f, 1000f);
            length = Mathf.Clamp(Finite(length, 4f), .05f, 1000f);
            flameWidth = Mathf.Clamp(Finite(flameWidth, .12f), .01f, 10f);
            corridorCapRadius = Mathf.Clamp(Finite(corridorCapRadius, 0f), 0f, width * .5f);
            var geometry = new Vector4(outerRadius, InnerRadius, angle, width);
            var extra = new Vector4(length, corridorCapRadius, flameWidth, 0f);
            int kind = shape == GroundIndicatorShape.Sector ? 0 : shape == GroundIndicatorShape.Rectangle ? 2 : 1;
            bool switched = kind != activeKind || activeRoot == null;
            if (switched)
            {
                StopCurrent();
                if (activeRoot != null) activeRoot.SetActive(false);
                activeKind = kind;
                if (instances[kind] == null)
                {
                    var source = kind == 0 ? coneSource : kind == 1 ? novaSource : rectangleSource;
                    instances[kind] = Instantiate(source, transform, false);
                    instances[kind].name = "Authored " + source.name;
                    instances[kind].hideFlags = HideFlags.DontSave;
                    foreach (var p in instances[kind].GetComponentsInChildren<ParticleSystem>(true)) Tune(p);
                }
                activeRoot = instances[kind];
                var systems = activeRoot.GetComponentsInChildren<ParticleSystem>(true);
                fill = Find(systems, "fill_add_soft"); border = Find(systems, "border_add_soft");
                if (fill == null || border == null) return;
            }
            activeRoot.transform.localPosition = Vector3.zero;
            activeRoot.transform.localRotation = Quaternion.identity;
            // Configuration is in world meters even under a scaled authoring parent.
            var scale = transform.lossyScale;
            activeRoot.transform.localScale = new Vector3(1f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                1f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
            if (switched || ownedMeshes.Count == 0 || geometry != lastGeometry || extra != lastExtra || shape != lastShape)
            {
                StopCurrent();
                ClearMeshes();
                BuildGeometry();
                clocks[kind] = activeRoot.GetComponentsInChildren<ParticleSystem>(true);
                lastGeometry = geometry; lastExtra = extra; lastShape = shape;
                simulatedTime = -1f;
            }
            activeRoot.SetActive(visible);
            if (visible) SetProgress(progress);
        }
        finally { refreshing = false; }
    }
    private void BuildGeometry()
    {
        Mesh surface = shape == GroundIndicatorShape.Sector ? SectorMesh()
            : shape == GroundIndicatorShape.Rectangle ? RectangleMesh() : RadialMesh(SurfaceSourceMesh(), InnerRadius, false);
        Mesh rim = shape == GroundIndicatorShape.Sector || shape == GroundIndicatorShape.Rectangle
            ? surface : RadialMesh(BorderSourceMesh(), InnerRadius, false);
        Bind(fill, surface); Bind(border, rim);
        if (shape == GroundIndicatorShape.Rectangle)
        {
            RectangleProperties(Surface); RectangleProperties(Border);
        }
        var systems = activeRoot.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var p in systems)
        {
            if (p.name.StartsWith("Inner native")) p.gameObject.SetActive(false);
            if (p.name.StartsWith("Fuzz"))
            {
                var mesh = CopyAmbient(surface);
                Bind(p, mesh);
                var r = p.GetComponent<ParticleSystemRenderer>();
                var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block);
                block.SetVector("_DetailVertexOffsetChannel", Vector4.zero); r.SetPropertyBlock(block);
            }
            else if (p.name.StartsWith("Flecks"))
            {
                float densityScale = Mathf.Max(.05f, (shape == GroundIndicatorShape.Rectangle ? Mathf.Min(width, length) : outerRadius) / 3f * .75f);
                p.transform.localPosition = Vector3.zero; p.transform.localScale = Vector3.one * densityScale;
                p.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
                var emitter = p.shape;
                if (shape == GroundIndicatorShape.Rectangle)
                {
                    emitter.shapeType = ParticleSystemShapeType.Box;
                    emitter.scale = new Vector3(width / densityScale, length / densityScale, .01f);
                    emitter.position = new Vector3(0f, -length * .5f / densityScale, 0f);
                }
                else
                {
                    emitter.shapeType = ParticleSystemShapeType.Donut;
                    emitter.radius = (outerRadius + InnerRadius) * .5f / densityScale;
                    emitter.donutRadius = (outerRadius - InnerRadius) * .5f / densityScale;
                    emitter.arc = shape == GroundIndicatorShape.Sector ? angle : 360f;
                    emitter.rotation = new Vector3(0f, 0f, shape == GroundIndicatorShape.Sector ? -90f - angle * .5f : 0f);
                }
            }
        }
        if (shape == GroundIndicatorShape.Donut && InnerRadius > 0f)
        {
            var innerFill = InnerLayer(fill, "Inner native fill outside hole");
            var innerBorder = InnerLayer(border, "Inner native border outside hole");
            Bind(innerFill, RadialMesh(SurfaceSourceMesh(), InnerRadius, true));
            Bind(innerBorder, RadialMesh(BorderSourceMesh(), InnerRadius, true));
            InnerProperties(innerFill.GetComponent<ParticleSystemRenderer>());
            InnerProperties(innerBorder.GetComponent<ParticleSystemRenderer>());
        }
    }
    private Mesh SurfaceSourceMesh() => Find(novaSource.GetComponentsInChildren<ParticleSystem>(true), "fill_add_soft").GetComponent<ParticleSystemRenderer>().mesh;
    private Mesh BorderSourceMesh() => Find(novaSource.GetComponentsInChildren<ParticleSystem>(true), "border_add_soft").GetComponent<ParticleSystemRenderer>().mesh;
    private ParticleSystem InnerLayer(ParticleSystem original, string label)
    {
        var child = activeRoot.transform.Find(label);
        if (child != null) { child.gameObject.SetActive(true); return child.GetComponent<ParticleSystem>(); }
        var clone = Instantiate(original.gameObject, activeRoot.transform).GetComponent<ParticleSystem>();
        clone.name = label; return clone;
    }
    private void Bind(ParticleSystem p, Mesh mesh)
    {
        p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = p.main; main.startSize = 1f; main.simulationSpeed = 1f;
        var sizing = p.sizeOverLifetime; sizing.enabled = false;
        p.transform.localScale = Vector3.one; p.transform.localPosition = Vector3.zero;
        p.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
        var r = p.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh;
        r.alignment = ParticleSystemRenderSpace.Local; r.mesh = mesh;
    }
    private Mesh SectorMesh()
    {
        int segments = Mathf.Clamp(Mathf.CeilToInt(angle), 8, 720);
        const int rings = 32;
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        for (int s = 0; s <= segments; s++) for (int r = 0; r <= rings; r++)
        {
            float t = (float)s / segments, distance = Mathf.Lerp(InnerRadius, outerRadius, (float)r / rings);
            float theta = (t - .5f) * angle * Mathf.Deg2Rad;
            vertices.Add(new Vector3(Mathf.Sin(theta) * distance, -Mathf.Cos(theta) * distance, 0f));
            float originalAngle = (t - .5f) * 53.270964f * Mathf.Deg2Rad, nativeRadius = distance / outerRadius;
            uv.Add(new Vector2(.5f + Mathf.Sin(originalAngle) * .8876953f * nativeRadius,
                .05737305f + Mathf.Cos(originalAngle) * .8876953f * nativeRadius));
            if (s > 0 && r > 0) AddQuad(triangles, s * (rings + 1) + r, rings + 1);
        }
        return MeshFrom("Authored cone UV with curved near edge", vertices, uv, triangles);
    }
    private Mesh RadialMesh(Mesh original, float holeMeters, bool reflect)
    {
        var originalVertices = original.vertices; var originalUv = original.uv;
        var profile = new float[11];
        for (int ring = 0; ring <= 10; ring++)
        {
            int found = -1;
            for (int i = 0; i < originalUv.Length; i++) if (Mathf.Abs(originalUv[i].y - ring * .1f) < .0001f) { found = i; break; }
            profile[ring] = found >= 0 ? new Vector2(originalVertices[found].x, originalVertices[found].y).magnitude : ring * .1f;
        }
        float hole = holeMeters / outerRadius;
        var distances = new List<float> { hole, 1f };
        foreach (float value in profile)
        {
            float d = reflect ? hole + 1f - value : value;
            if (d > hole + .00001f && d < .99999f) distances.Add(d);
        }
        distances.Sort();
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        const int segments = 512;
        for (int s = 0; s <= segments; s++) for (int r = 0; r < distances.Count; r++)
        {
            float t = (float)s / segments, d = distances[r], theta = -t * Mathf.PI * 2f;
            vertices.Add(new Vector3(Mathf.Sin(theta), Mathf.Cos(theta), 0f) * d * outerRadius);
            float sourceD = reflect ? 1f - (d - hole) : d, sourceV = 1f;
            for (int i = 1; i <= 10; i++) if (sourceD <= profile[i])
            { sourceV = (i - 1 + Mathf.InverseLerp(profile[i - 1], profile[i], sourceD)) * .1f; break; }
            uv.Add(new Vector2(t, sourceV));
            if (s > 0 && r > 0) AddQuad(triangles, s * distances.Count + r, distances.Count);
        }
        return MeshFrom(reflect ? "Native flames outside inner circle" : "Native radial UV without compression", vertices, uv, triangles);
    }
    private Mesh RectangleMesh()
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        var zRows = new List<float>(); float cap = corridorCapRadius;
        if (cap > 0f)
        {
            for (int i = 0; i <= 16; i++) zRows.Add(-cap * Mathf.Cos(i * Mathf.PI * .5f / 16f));
            zRows.Add(length);
            for (int i = 1; i <= 16; i++) zRows.Add(length + cap * Mathf.Sin(i * Mathf.PI * .5f / 16f));
        }
        else for (int i = 0; i <= 32; i++) zRows.Add(length * i / 32f);
        const int columns = 16;
        for (int y = 0; y < zRows.Count; y++) for (int x = 0; x <= columns; x++)
        {
            float z = zRows[y], half = width * .5f;
            if (cap > 0f && (z < 0f || z > length))
            {
                float offset = z < 0f ? z : z - length;
                half = Mathf.Sqrt(Mathf.Max(0f, cap * cap - offset * offset));
            }
            float u = (float)x / columns, v = (z + cap) / (length + cap * 2f);
            vertices.Add(new Vector3((u - .5f) * half * 2f, -z, 0f));
            // Native Square layers are rotated 90 degrees around their plane.
            uv.Add(new Vector2(v, 1f - u));
            if (x > 0 && y > 0) AddQuad(triangles, y * (columns + 1) + x, columns + 1);
        }
        return MeshFrom(cap > 0f ? "Charge corridor including sphere-cast ends" : "Numeric native rectangle", vertices, uv, triangles);
    }
    private Mesh CopyAmbient(Mesh surface)
    {
        var vertices = surface.vertices; var uv = new List<Vector2>(vertices.Length); var nativeUv = surface.uv;
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (shape == GroundIndicatorShape.Sector)
                uv.Add(new Vector2(.5f + (nativeUv[i].x - .5f) / 1.43f, .5f + (nativeUv[i].y - .5f) / 1.43f));
            else if (shape == GroundIndicatorShape.Rectangle)
                uv.Add(new Vector2(.5f + v.x / 11.792f, .5f + (v.y + length * .5f) / 11.792f));
            else uv.Add(new Vector2(.5f + v.x / (outerRadius * 4.4f), .5f + v.y * .7682213f / (outerRadius * 4.4f)));
        }
        return MeshFrom("Authored haze clipped to exact ground shape", new List<Vector3>(vertices), uv, new List<int>(surface.triangles));
    }
    private Mesh MeshFrom(string label, List<Vector3> vertices, List<Vector2> uv, List<int> triangles)
    {
        var mesh = new Mesh { name = label, hideFlags = HideFlags.DontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); ownedMeshes.Add(mesh); return mesh;
    }
    private static void AddQuad(List<int> triangles, int n, int stride)
    { triangles.Add(n - stride - 1); triangles.Add(n - stride); triangles.Add(n); triangles.Add(n - stride - 1); triangles.Add(n); triangles.Add(n - 1); }
    private void RectangleProperties(ParticleSystemRenderer r)
    {
        var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block);
        TextureScale(r.sharedMaterial, block, (length + corridorCapRadius * 2f) / 4.6f, width / 4.6f);
        r.SetPropertyBlock(block);
    }
    private void InnerProperties(ParticleSystemRenderer r)
    {
        var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block);
        TextureScale(r.sharedMaterial, block, InnerRadius / outerRadius, 1f); r.SetPropertyBlock(block);
    }
    private static void TextureScale(Material material, MaterialPropertyBlock block, float u, float v)
    {
        if (material == null) return;
        foreach (string key in new[] { "_MainTex", "_DetailNoise" })
        {
            if (!material.HasProperty(key)) continue;
            var scale = material.GetTextureScale(key); var offset = material.GetTextureOffset(key);
            block.SetVector(key + "_ST", new Vector4(scale.x * u, scale.y * v, offset.x, offset.y));
        }
    }
    private static void Tune(ParticleSystem p)
    {
        p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = p.main; main.playOnAwake = false; main.simulationSpeed = 1f;
        main.startColor = Tint(main.startColor, p.name.StartsWith("Fuzz") ? .2f : 1f);
        var r = p.GetComponent<ParticleSystemRenderer>(); var material = r.sharedMaterial;
        if (material == null) return;
        var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block);
        if (material.HasProperty("_LastColor")) { var c = material.GetColor("_LastColor"); c.r *= .08f; c.g *= .08f; c.b *= .08f; block.SetColor("_LastColor", c); }
        if (material.HasProperty("_WhiteColor")) { var c = material.GetColor("_WhiteColor"); c.g *= .04f; c.b = 0f; block.SetColor("_WhiteColor", c); }
        if (material.HasProperty("_MidColor")) { var c = material.GetColor("_MidColor"); c.g *= .45f; c.b = 0f; block.SetColor("_MidColor", c); }
        r.SetPropertyBlock(block);
    }
    private static ParticleSystem.MinMaxGradient Tint(ParticleSystem.MinMaxGradient source, float alpha)
    {
        Color Adjust(Color c) { c.g *= .04f; c.b = 0f; c.a *= alpha; return c; }
        Gradient AdjustGradient(Gradient original)
        {
            var gradient = new Gradient(); var colors = original.colorKeys; var alphas = original.alphaKeys;
            for (int i = 0; i < colors.Length; i++) colors[i].color = Adjust(colors[i].color);
            for (int i = 0; i < alphas.Length; i++) alphas[i].alpha *= alpha;
            gradient.SetKeys(colors, alphas); gradient.mode = original.mode; return gradient;
        }
        switch (source.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Adjust(source.color));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Adjust(source.colorMin), Adjust(source.colorMax));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(AdjustGradient(source.gradientMin), AdjustGradient(source.gradientMax));
            default: return new ParticleSystem.MinMaxGradient(AdjustGradient(source.gradient));
        }
    }
    private static ParticleSystem Find(ParticleSystem[] systems, string part)
    { foreach (var p in systems) if (p.name.Contains(part)) return p; return null; }
    private void StopCurrent()
    {
        if (activeKind >= 0 && clocks[activeKind] != null) foreach (var p in clocks[activeKind])
            if (p != null) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        simulatedTime = -1f;
    }
    private void ClearMeshes()
    {
        foreach (var mesh in ownedMeshes) DestroyOwned(mesh); ownedMeshes.Clear();
    }
    public void ReleaseRuntime()
    {
        StopCurrent();
        for (int i = 0; i < instances.Length; i++) { DestroyOwned(instances[i]); instances[i] = null; clocks[i] = null; }
        ClearMeshes(); activeRoot = null; fill = null; border = null; activeKind = -1;
    }
    private static void DestroyOwned(Object value)
    { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    private static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    private void OnEnable() { simulatedTime = -1f; if (visible) Refresh(); }
    private void OnDisable() { StopCurrent(); }
    private void OnDestroy() { ReleaseRuntime(); }
    private void OnValidate() { if (!refreshing) Refresh(); }
}
