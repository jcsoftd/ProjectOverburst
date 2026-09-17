using UnityEngine;

public class LightningChainBeamVfx : MonoBehaviour // 번개 체인 빔
{
    private const int PointCount = 5; // 꺾임 수

    [SerializeField] private float lifetime = 0.18f;
    [SerializeField] private float startWidth = 0.11f;
    [SerializeField] private float endWidth = 0.045f;
    [SerializeField] private float jitter = 0.42f;

    private float destroyTime; // 삭제 시간
    private Material runtimeMaterial; // 실행 중 재질

    public static void Spawn(Vector3 start, Vector3 end, GameObject beamPrefab = null)
    {
        if (beamPrefab != null)
        {
            GameObject prefabInstance = VfxPrefabFactory.Spawn(beamPrefab, (start + end) * 0.5f, Quaternion.LookRotation((end - start).normalized, Vector3.up));
            if (prefabInstance != null)
                Destroy(prefabInstance, 0.35f);
        }

        GameObject instance = new GameObject("LightningChainBeam_Runtime");
        LightningChainBeamVfx beam = instance.AddComponent<LightningChainBeamVfx>();
        beam.Configure(start, end);
    }

    private void Configure(Vector3 start, Vector3 end)
    {
        LineRenderer line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = PointCount;
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.startColor = new Color(0.06f, 0.95f, 1f, 1f);
        line.endColor = new Color(1f, 1f, 1f, 0.18f);
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            runtimeMaterial = new Material(shader);
            runtimeMaterial.name = "Runtime_LightningChainBeam";
            SetColorIfPresent(runtimeMaterial, "_BaseColor", new Color(0.12f, 0.95f, 1f, 0.85f));
            SetColorIfPresent(runtimeMaterial, "_Color", new Color(0.12f, 0.95f, 1f, 0.85f));
            SetColorIfPresent(runtimeMaterial, "_EmissionColor", new Color(0.4f, 1f, 1f, 1f));
            SetFloatIfPresent(runtimeMaterial, "_Surface", 1f);
            SetFloatIfPresent(runtimeMaterial, "_Blend", 1f);
            SetFloatIfPresent(runtimeMaterial, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloatIfPresent(runtimeMaterial, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetFloatIfPresent(runtimeMaterial, "_ZWrite", 0f);
            runtimeMaterial.EnableKeyword("_EMISSION");
            runtimeMaterial.renderQueue = 3000;
            line.material = runtimeMaterial;
        }

        Vector3 direction = end - start; // 빔 방향
        Vector3 side = Vector3.Cross(Vector3.up, direction.normalized); // 흔들림 축
        if (side.sqrMagnitude <= 0.0001f)
            side = Vector3.right;

        side.Normalize();
        for (int i = 0; i < PointCount; i++)
        {
            float t = PointCount <= 1 ? 0f : (float)i / (PointCount - 1); // 진행률
            Vector3 point = Vector3.Lerp(start, end, t); // 기준점
            if (i > 0 && i < PointCount - 1)
                point += side * Random.Range(-jitter, jitter) + Vector3.up * Random.Range(-jitter * 0.35f, jitter * 0.55f);

            line.SetPosition(i, point);
        }

        destroyTime = Time.time + Mathf.Max(0.02f, lifetime);
    }

    private void Update()
    {
        if (Time.time >= destroyTime)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeMaterial);
        else
            DestroyImmediate(runtimeMaterial);
    }

    private static void SetColorIfPresent(Material material, string property, Color color)
    {
        if (material != null && material.HasProperty(property))
            material.SetColor(property, color);
    }

    private static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material != null && material.HasProperty(property))
            material.SetFloat(property, value);
    }
}
