using UnityEngine;

// Reused circular cue. It does not draw a direction line or decide collision geometry.
public sealed class EnemyStrongAttackWarning : MonoBehaviour
{
    private GameObject visual;
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock block;
    private bool parryable;
    private float radius;
    public bool IsVisible => visual != null && visual.activeSelf;
    public bool FinalSignal { get; private set; }

    public void Show(float size, bool canParry)
    {
        if (visual == null)
        {
            visual = new GameObject("Strong attack warning");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.up * .045f;
            mesh = new Mesh { name = "Strong warning ring" };
            const int segments = 48;
            var vertices = new Vector3[segments * 2];
            var indices = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var v = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices[i * 2] = v; vertices[i * 2 + 1] = v * .91f;
                int n = ((i + 1) % segments) * 2, k = i * 6;
                indices[k] = i * 2; indices[k + 1] = i * 2 + 1; indices[k + 2] = n;
                indices[k + 3] = n; indices[k + 4] = i * 2 + 1; indices[k + 5] = n + 1;
            }
            mesh.vertices = vertices; mesh.triangles = indices; mesh.RecalculateBounds();
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = visual.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = Resources.Load<Material>("Enemies/Balance/StrongAttackWarning");
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            block = new MaterialPropertyBlock();
        }
        radius = size; parryable = canParry; FinalSignal = false;
        visual.SetActive(true); SetRemaining(1f);
    }
    public void SetRemaining(float seconds)
    {
        if (visual == null) return;
        if (seconds < -.08f) { Hide(); return; }
        if (!FinalSignal && seconds <= .30f) CombatActionSfxService.PlayStrongWarning(transform.position);
        FinalSignal = seconds <= .30f;
        Color color = FinalSignal ? (parryable ? new Color(1f, .95f, .35f) : new Color(1f, .25f, .2f))
            : new Color(1f, .45f, .12f);
        block.SetColor("_BaseColor", color); meshRenderer.SetPropertyBlock(block);
        float pulse = FinalSignal ? 1f + .06f * Mathf.Sin(Time.time * 32f) : 1f;
        visual.transform.localScale = new Vector3(radius * pulse, 1, radius * pulse);
    }
    public void Hide() { if (visual != null) visual.SetActive(false); FinalSignal = false; }
    private void OnDisable() => Hide();
    private void OnDestroy() { if (mesh != null) Destroy(mesh); }
}
