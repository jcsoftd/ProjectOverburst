using UnityEngine;

public class VfxPlaceholder : MonoBehaviour // 대체 VFX
{
    [SerializeField] private Color vfxColor = Color.white;
    [SerializeField] private Vector3 visualScale = Vector3.one * 0.25f;
    [SerializeField] private PrimitiveType primitiveType = PrimitiveType.Sphere;
    [SerializeField] private float lifetime = 0.5f;
    [SerializeField] private bool destroyAfterLifetime = true;
    [SerializeField] private bool pulseScale = true;

    private Transform visual;
    private Vector3 baseScale;
    private float startTime;

    private void Awake()
    {
        EnsureVisual(); // 표시체 준비
    }

    private void OnEnable()
    {
        startTime = Time.time;

        if (destroyAfterLifetime && lifetime > 0f)
            Destroy(gameObject, lifetime); // 잔여물 방지
    }

    private void Update()
    {
        if (!pulseScale || visual == null || lifetime <= 0f)
            return; // 고정 크기

        float normalized = Mathf.Clamp01((Time.time - startTime) / lifetime);
        float scale = 1f + Mathf.Sin(normalized * Mathf.PI) * 0.35f;
        visual.localScale = baseScale * scale;
    }

    private void EnsureVisual()
    {
        if (visual != null)
            return;

        GameObject visualObject = GameObject.CreatePrimitive(primitiveType);
        visualObject.name = "Visual";
        visualObject.transform.SetParent(transform, false);
        visualObject.transform.localPosition = Vector3.zero;
        visualObject.transform.localRotation = Quaternion.identity;
        visualObject.transform.localScale = visualScale;

        Collider collider = visualObject.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider); // 충돌 제외

        Renderer renderer = visualObject.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = vfxColor; // 표시색

        visual = visualObject.transform;
        baseScale = visualScale;
    }
}
