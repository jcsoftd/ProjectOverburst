using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class SwordDistortionTrail : MonoBehaviour, IWeaponTrailController
{
    [SerializeField] private OneHandSwordDistortionStyle style;
    [SerializeField] private Transform bladeTip;
    [SerializeField] private Transform bladeGrip;
    [SerializeField] private Material distortionMaterial;
    [SerializeField, Min(.03f)] private float lifetime = .24f;
    private struct Sample { public Vector3 tip, inner; public float time; }
    private readonly List<Sample> samples = new List<Sample>(64);
    private readonly List<Vector3> vertices = new List<Vector3>(128);
    private readonly List<Vector2> uv = new List<Vector2>(128);
    private readonly List<Color> colors = new List<Color>(128);
    private readonly List<int> triangles = new List<int>(378);
    private Mesh mesh;
    private MeshRenderer surface;
    private bool swinging;
    public bool IsEmitting => swinging;
    public int VisibleVertexCount => surface && surface.enabled && mesh ? mesh.vertexCount : 0;

    public void BeginTrail()
    {
        ClearTrail();
        swinging = style && style.version == SwordDistortionVersion.BladeTrail
            && bladeTip && bladeGrip && distortionMaterial;
        if (swinging) { EnsureSurface(); AddSample(); }
        var equipment = GetComponentInParent<PlayerEquipment>();
        if (equipment && equipment.CurrentWeaponItem != null)
            MeleeElementSfxService.TryPlaySlash(equipment.CurrentWeaponItem.ResolvedElement, transform.position);
    }

    public void EndTrail() => swinging = false;

    public void ClearTrail()
    {
        SwordDistortionSurfaces.Set(this, false);
        swinging = false;
        samples.Clear();
        if (mesh) mesh.Clear(false);
        if (surface) surface.enabled = false;
    }

    private void EnsureSurface()
    {
        if (mesh) return;
        mesh = new Mesh { name = "SwordDistortionRibbon_Runtime" };
        mesh.MarkDynamic();
        var go = new GameObject("SwordDistortionRibbon_Runtime");
        go.transform.SetParent(transform, false);
        go.layer = gameObject.layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        surface = go.AddComponent<MeshRenderer>();
        surface.sharedMaterial = distortionMaterial;
        surface.shadowCastingMode = ShadowCastingMode.Off;
        surface.receiveShadows = false;
        surface.enabled = false;
    }

    private void AddSample()
    {
        Vector3 tip = bladeTip.position;
        Vector3 inner = Vector3.Lerp(bladeGrip.position, tip, .22f);
        if (samples.Count > 0)
        {
            float distance = Vector3.Distance(samples[samples.Count - 1].tip, tip);
            if (distance > 3f) samples.Clear(); // 순간 이동 때 화면을 가로지르는 띠 방지
            else if (distance < .012f) return;
        }
        if (samples.Count == 64) samples.RemoveAt(0);
        samples.Add(new Sample { tip = tip, inner = inner, time = Time.time });
    }

    private void LateUpdate()
    {
        if (!style || style.version != SwordDistortionVersion.BladeTrail)
        {
            if (samples.Count > 0 || swinging) ClearTrail();
            return;
        }
        while (samples.Count > 0 && Time.time - samples[0].time >= lifetime)
            samples.RemoveAt(0);
        if (swinging) AddSample();
        if (samples.Count < 2)
        {
            SwordDistortionSurfaces.Set(this, false);
            if (surface) surface.enabled = false;
            return;
        }
        EnsureSurface();
        vertices.Clear(); uv.Clear(); colors.Clear(); triangles.Clear();
        // 월드 위치를 저장하고 매 프레임 현재 검 좌표로 옮겨 이미 지난 궤적이 검을 따라 돌지 않게 한다.
        for (int i = 0; i < samples.Count; i++)
        {
            Sample s = samples[i];
            float age = Mathf.Clamp01((Time.time - s.time) / lifetime);
            float fade = Mathf.Pow(1f - age, 1.5f);
            Vector3 middle = (s.inner + s.tip) * .5f;
            float taper = Mathf.SmoothStep(0, 1, 1f - age);
            vertices.Add(transform.InverseTransformPoint(Vector3.Lerp(middle, s.inner, taper)));
            vertices.Add(transform.InverseTransformPoint(Vector3.Lerp(middle, s.tip, taper)));
            float u = 1f - (float)i / (samples.Count - 1);
            uv.Add(new Vector2(u, 0)); uv.Add(new Vector2(u, 1));
            colors.Add(new Color(1,1,1,fade)); colors.Add(new Color(1,1,1,fade));
            if (i == 0) continue;
            int n = i * 2;
            triangles.Add(n-2); triangles.Add(n-1); triangles.Add(n);
            triangles.Add(n); triangles.Add(n-1); triangles.Add(n+1);
        }
        mesh.Clear(false);
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        surface.enabled = true;
        SwordDistortionSurfaces.Set(this, true);
    }

    private void OnDisable() => ClearTrail();
    private void OnDestroy() { if (mesh) Destroy(mesh); }
}
