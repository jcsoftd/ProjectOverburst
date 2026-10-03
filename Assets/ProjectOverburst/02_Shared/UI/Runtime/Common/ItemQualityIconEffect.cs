using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>기존 아이템 Image만 조절한다. 자식의 원소·분류 배지와 슬롯 등급 프레임은 그대로 둔다.</summary>
[DisallowMultipleComponent]
public sealed class ItemQualityIconEffect : MonoBehaviour
{
    private const float ShineDuration = 1.15f;
    private static Shader shineShader;
    private static readonly int ProgressId = Shader.PropertyToID("_ShineProgress");
    private static readonly int StrengthId = Shader.PropertyToID("_ShineStrength");
    private static readonly int RectId = Shader.PropertyToID("_IconRect");
    private Image icon;
    private Material originalMaterial;
    private Material shineMaterial;
    private Coroutine pass;
    private ItemData shownItem;
    private Sprite shownSprite;
    private ItemInscriptionQualityResult quality;
    private bool allowShine;

    public static void Present(Image target, ItemData item, Color? baseColor = null, bool playShine = true)
    {
        if (!target) return;
        bool supported = ItemInscriptionQuality.TryEvaluate(item, out var result) && target.sprite;
        var effect = target.GetComponent<ItemQualityIconEffect>();
        if (!effect && !supported) return;
        if (!effect) effect = target.gameObject.AddComponent<ItemQualityIconEffect>();
        effect.Bind(target, supported ? item : null, result, baseColor ?? (item != null ? item.iconColor : Color.white), playShine);
    }

    private void Bind(Image target, ItemData item, ItemInscriptionQualityResult value, Color baseColor, bool playShine)
    {
        icon = target;
        if (icon.material != shineMaterial) originalMaterial = icon.material;
        bool changed = !ReferenceEquals(shownItem, item) || shownSprite != target.sprite || quality.Tier != value.Tier;
        shownItem = item; shownSprite = target.sprite; quality = value; allowShine = playShine;
        float brightness = item != null ? value.IconBrightness : 1f;
        icon.color = new Color(baseColor.r * brightness, baseColor.g * brightness, baseColor.b * brightness, baseColor.a);
        if (item == null || !playShine || value.ShineStrength <= 0f) StopPass();
        else if (changed && isActiveAndEnabled && icon.enabled && icon.gameObject.activeInHierarchy) PlayPass();
    }

    private void OnEnable()
    {
        if (icon && shownItem != null && allowShine && quality.ShineStrength > 0f) PlayPass();
    }

    private void OnDisable() { StopPass(); ReleaseMaterial(); }

    private void OnDestroy()
    {
        StopPass();
        ReleaseMaterial();
    }

    private void OnRectTransformDimensionsChange() => UpdateIconRect();

    private void UpdateIconRect()
    {
        if (!icon || !shineMaterial) return;
        Rect rect = icon.rectTransform.rect;
        Vector4 bounds = new Vector4(rect.xMin, rect.yMin, Mathf.Max(1f, rect.width), Mathf.Max(1f, rect.height));
        shineMaterial.SetVector(RectId, bounds);
        if (icon.material == shineMaterial) icon.materialForRendering.SetVector(RectId, bounds);
    }

    private void PlayPass()
    {
        StopPass();
        if (!Application.isPlaying || !icon || !icon.enabled || !icon.gameObject.activeInHierarchy) return;
        if (!shineShader) shineShader = Resources.Load<Shader>("Shaders/ItemQualityShineUI");
        if (!shineShader || !shineShader.isSupported) return;
        if (!shineMaterial) shineMaterial = new Material(shineShader) { name = "Item Quality Shine", hideFlags = HideFlags.HideAndDontSave };
        UpdateIconRect();
        shineMaterial.SetFloat(StrengthId, quality.ShineStrength);
        shineMaterial.SetFloat(ProgressId, 0f);
        icon.material = shineMaterial;
        pass = StartCoroutine(Shine());
    }

    private IEnumerator Shine()
    {
        float elapsed = 0f;
        while (elapsed < ShineDuration && icon && icon.sprite == shownSprite)
        {
            shineMaterial.SetFloat(ProgressId, Mathf.Clamp01(elapsed / ShineDuration));
            // Mask의 stencil용 사본에도 진행률을 전달한다. 이 Image의 고유 재질을 기반으로 한 사본만 조절한다.
            if (icon.material == shineMaterial) icon.materialForRendering.SetFloat(ProgressId, Mathf.Clamp01(elapsed / ShineDuration));
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        RestoreMaterial();
        pass = null;
    }

    private void StopPass()
    {
        if (pass != null) { StopCoroutine(pass); pass = null; }
        RestoreMaterial();
    }

    private void RestoreMaterial()
    {
        if (icon && shineMaterial && icon.material == shineMaterial) icon.material = originalMaterial;
    }

    private void ReleaseMaterial()
    {
        if (!shineMaterial) return;
        if (Application.isPlaying) Destroy(shineMaterial); else DestroyImmediate(shineMaterial);
        shineMaterial = null;
    }
}
