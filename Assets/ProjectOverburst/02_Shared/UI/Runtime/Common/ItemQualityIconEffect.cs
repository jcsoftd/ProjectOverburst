using UnityEngine;
using UnityEngine.UI;

/// <summary>주 아이콘의 지속 발광과 광택. 배지·등급 프레임·원본 alpha는 보존한다.</summary>
[DisallowMultipleComponent]
public sealed class ItemQualityIconEffect : MonoBehaviour
{
    private const float ShineDuration = 1.15f;
    private const float GlowPeriod = 4.8f;
    private static Shader shineShader;
    private static readonly int ClockId = Shader.PropertyToID("_OverburstItemQualityTime");
    private static readonly int StartId = Shader.PropertyToID("_AnimationStartTime");
    private static readonly int AnimatedId = Shader.PropertyToID("_AnimateEffects");
    private static readonly int StrengthId = Shader.PropertyToID("_ShineStrength");
    private static readonly int GlowId = Shader.PropertyToID("_GlowStrength");
    private static readonly int PeriodId = Shader.PropertyToID("_GlowPeriod");
    private static readonly int OffsetId = Shader.PropertyToID("_PulseOffset");
    private static readonly int IntervalId = Shader.PropertyToID("_ShineInterval");
    private static readonly int DurationId = Shader.PropertyToID("_ShineDuration");
    private static readonly int RectId = Shader.PropertyToID("_IconRect");
    private static int activeEffects;
    private static int clockFrame = -1;
    private Image icon;
    private Material originalMaterial;
    private Material shineMaterial;
    private ItemData shownItem;
    private Sprite shownSprite;
    private ItemInscriptionQualityResult quality;
    private bool allowShine;
    private bool clockRegistered;

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
        if (item == null || !playShine || value.GlowStrength <= 0f || !isActiveAndEnabled || !icon.enabled || !icon.gameObject.activeInHierarchy)
            StopAnimation();
        else if (changed || !clockRegistered || icon.material != shineMaterial) StartAnimation();
    }

    private void OnEnable()
    {
        if (icon && shownItem != null && allowShine && quality.GlowStrength > 0f) StartAnimation();
    }

    private void OnDisable() => StopAnimation();
    private void OnDestroy() => StopAnimation();
    private void OnRectTransformDimensionsChange() => UpdateIconRect();

    private void UpdateIconRect()
    {
        if (!icon || !shineMaterial) return;
        Rect rect = icon.rectTransform.rect;
        Vector4 bounds = new Vector4(rect.xMin, rect.yMin, Mathf.Max(1f, rect.width), Mathf.Max(1f, rect.height));
        shineMaterial.SetVector(RectId, bounds);
        if (icon.material == shineMaterial) icon.materialForRendering.SetVector(RectId, bounds);
    }

    private void StartAnimation()
    {
        StopAnimation();
        if (!Application.isPlaying || !icon || !icon.enabled || !icon.gameObject.activeInHierarchy) return;
        if (!shineShader) shineShader = Resources.Load<Shader>("Shaders/ItemQualityShineUI");
        if (!shineShader || !shineShader.isSupported) return;
        shineMaterial = new Material(shineShader) { name = "Item Quality Glow", hideFlags = HideFlags.HideAndDontSave };
        shineMaterial.SetFloat(AnimatedId, 1f);
        shineMaterial.SetFloat(StartId, Time.unscaledTime);
        shineMaterial.SetFloat(StrengthId, quality.ShineStrength);
        shineMaterial.SetFloat(GlowId, quality.GlowStrength);
        shineMaterial.SetFloat(PeriodId, GlowPeriod);
        shineMaterial.SetFloat(OffsetId, (GetInstanceID() & 1023) / 1024f * GlowPeriod);
        shineMaterial.SetFloat(IntervalId, quality.ShineInterval);
        shineMaterial.SetFloat(DurationId, ShineDuration);
        UpdateIconRect();
        icon.material = shineMaterial;
        clockRegistered = true;
        if (activeEffects++ == 0) Canvas.willRenderCanvases += UpdateClock;
        UpdateClock();
    }

    // 모든 활성 아이콘이 같은 unscaled 시계를 공유한다. 프레임별 개별 Update/코루틴은 사용하지 않는다.
    private static void UpdateClock()
    {
        if (clockFrame == Time.frameCount) return;
        clockFrame = Time.frameCount;
        Shader.SetGlobalFloat(ClockId, Time.unscaledTime);
    }

    private void StopAnimation()
    {
        if (clockRegistered)
        {
            clockRegistered = false;
            if (--activeEffects == 0) { Canvas.willRenderCanvases -= UpdateClock; clockFrame = -1; }
        }
        if (icon && shineMaterial && icon.material == shineMaterial) icon.material = originalMaterial;
        ReleaseMaterial();
    }

    private void ReleaseMaterial()
    {
        if (!shineMaterial) return;
        if (Application.isPlaying) Destroy(shineMaterial); else DestroyImmediate(shineMaterial);
        shineMaterial = null;
    }
}