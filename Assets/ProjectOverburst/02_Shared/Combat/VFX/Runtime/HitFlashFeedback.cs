using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class HitFlashFeedback : MonoBehaviour // 피격 flash
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.08f;
    [SerializeField, Min(1f)] private float flashBrightness = 2.5f;

    private MaterialPropertyBlock[] propertyBlocks; // 재질 속성 묶음
    private Color[] baseColors; // 원본 색
    private MaterialPropertyBlock[] beforeFlash;
    private bool flashApplied;
    private Coroutine flashRoutine; // flash 루틴
    private const string BaseColorProperty = "_BaseColor"; // URP 색상
    private const string ColorProperty = "_Color"; // 기본 색상

    private void Awake()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();

        CacheRenderers();
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDamaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDamaged -= HandleDamaged;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        RestoreColors();
        flashRoutine = null;
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (info.isDamageOverTime || !info.triggersOnHitEffects)
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        // Preserve tint and other presentation properties, even on repeated hits.
        if (!flashApplied)
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null)
                {
                    renderers[i].GetPropertyBlock(beforeFlash[i]);
                    baseColors[i] = beforeFlash[i].HasColor(Shader.PropertyToID(BaseColorProperty))
                        ? beforeFlash[i].GetColor(BaseColorProperty) : GetRendererColor(renderers[i]);
                }
        flashApplied = true;

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        ApplyFlashColor();
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            yield return null;
            elapsed += Time.deltaTime;
            float weight = 1f - Mathf.Clamp01(elapsed / Mathf.Max(.001f, flashDuration));
            for (int i = 0; i < renderers.Length; i++)
                ApplyColor(i, Color.Lerp(baseColors[i], BrightFlash(), weight));
        }

        RestoreColors();
        flashRoutine = null;
    }

    private void CacheRenderers()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        propertyBlocks = new MaterialPropertyBlock[renderers.Length];
        beforeFlash = new MaterialPropertyBlock[renderers.Length];
        baseColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            propertyBlocks[i] = new MaterialPropertyBlock();
            beforeFlash[i] = new MaterialPropertyBlock();
            baseColors[i] = GetRendererColor(renderers[i]);
        }
    }

    private Color GetRendererColor(Renderer targetRenderer)
    {
        if (targetRenderer == null || targetRenderer.sharedMaterial == null)
            return Color.white;

        Material material = targetRenderer.sharedMaterial;
        if (material.HasProperty(BaseColorProperty))
            return material.GetColor(BaseColorProperty);

        if (material.HasProperty(ColorProperty))
            return material.GetColor(ColorProperty);

        return Color.white;
    }

    private void ApplyFlashColor()
    {
        ApplyColor(BrightFlash());
    }

    private Color BrightFlash() => new Color(flashColor.r * flashBrightness,
        flashColor.g * flashBrightness, flashColor.b * flashBrightness, flashColor.a);

    private void RestoreColors()
    {
        if (!flashApplied || renderers == null || baseColors == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].SetPropertyBlock(beforeFlash[i]);
        flashApplied = false;
    }

    private void ApplyColor(Color color)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
            ApplyColor(i, color);
    }

    private void ApplyColor(int index, Color color)
    {
        if (renderers == null || index < 0 || index >= renderers.Length || renderers[index] == null)
            return;

        Material material = renderers[index].sharedMaterial;
        if (material == null)
            return;

        MaterialPropertyBlock block = propertyBlocks != null && index < propertyBlocks.Length ? propertyBlocks[index] : null;
        if (block == null)
            block = new MaterialPropertyBlock();

        renderers[index].GetPropertyBlock(block);

        if (material.HasProperty(BaseColorProperty))
            block.SetColor(BaseColorProperty, color);

        if (material.HasProperty(ColorProperty))
            block.SetColor(ColorProperty, color);

        renderers[index].SetPropertyBlock(block);
    }
}
