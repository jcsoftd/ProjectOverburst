using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class HitFlashFeedback : MonoBehaviour // 피격 flash
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.08f;

    private MaterialPropertyBlock[] propertyBlocks; // 재질 속성 묶음
    private Color[] baseColors; // 원본 색
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

        RestoreColors();
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (info.isDamageOverTime || !info.triggersOnHitEffects)
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        ApplyFlashColor();

        if (flashDuration > 0f)
            yield return new WaitForSeconds(flashDuration);

        RestoreColors();
        flashRoutine = null;
    }

    private void CacheRenderers()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        propertyBlocks = new MaterialPropertyBlock[renderers.Length];
        baseColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            propertyBlocks[i] = new MaterialPropertyBlock();
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
        ApplyColor(flashColor);
    }

    private void RestoreColors()
    {
        if (renderers == null || baseColors == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
            ApplyColor(i, baseColors[i]);
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
