using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Image))]
public class ExperimentalSlotOutlineEffect : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");
    private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
    private static readonly int GlowSizeId = Shader.PropertyToID("_GlowSize");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseSpeedId = Shader.PropertyToID("_NoiseSpeed");
    private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int ShapeModeId = Shader.PropertyToID("_ShapeMode");
    private static readonly int EffectModeId = Shader.PropertyToID("_EffectMode");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int CornerCutId = Shader.PropertyToID("_CornerCut");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    [SerializeField] private Image targetImage;
    [SerializeField] private Shader outlineShader;
    [SerializeField] private Color gradeColor = new Color(1f, 0.38f, 0.12f, 1f);
    [SerializeField] private bool forceVisibleImageColor = true;
    [SerializeField] private bool useCircleShape;
    [SerializeField] private ExperimentalSlotOutlineMode effectMode = ExperimentalSlotOutlineMode.TieredFlameElectric;
    [SerializeField, Range(0.001f, 0.5f)] private float outlineThickness = 0.026f;
    [SerializeField, Range(0f, 8f)] private float glowIntensity = 4.1f;
    [SerializeField, Range(0f, 0.5f)] private float glowSize = 0.026f;
    [SerializeField, Range(1f, 80f)] private float noiseScale = 42f;
    [SerializeField, Range(0f, 10f)] private float noiseSpeed = 2f;
    [SerializeField, Range(0f, 2f)] private float noiseStrength = 0.85f;
    [SerializeField, Range(0f, 10f)] private float pulseSpeed = 1.3f;
    [SerializeField, Range(0.001f, 0.2f)] private float edgeSoftness = 0.035f;
    [SerializeField, Range(0f, 0.18f)] private float cornerCut = 0.06f;
    [SerializeField, Range(0f, 1f)] private float alpha = 1f;

    private Material runtimeMaterial;

    public void SetGradeColor(Color color)
    {
        gradeColor = color;
        ApplyProperties();
    }

    public void SetCircleShape(bool value)
    {
        useCircleShape = value;
        ApplyProperties();
    }

    public void SetMode(ExperimentalSlotOutlineMode mode)
    {
        effectMode = mode;
        ApplyProperties();
    }

    public void SetIntensity(float thickness, float glow, float size, float noise, float pulse, float opacity)
    {
        outlineThickness = thickness;
        glowIntensity = glow;
        glowSize = size;
        noiseStrength = noise;
        pulseSpeed = pulse;
        alpha = opacity;
        ApplyProperties();
    }

    private void OnEnable()
    {
        ResolveTargetImage();
        EnsureRuntimeMaterial();
        ApplyProperties();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;

        ResolveTargetImage();
        EnsureRuntimeMaterial();
        ApplyProperties();
    }

    private void Update()
    {
        if (runtimeMaterial == null)
            EnsureRuntimeMaterial();

        ApplyProperties();
    }

    private void OnDestroy()
    {
        if (targetImage != null && targetImage.material == runtimeMaterial)
            targetImage.material = null;

        if (runtimeMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeMaterial);
        else
            DestroyImmediate(runtimeMaterial);
    }

    private void ResolveTargetImage()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
    }

    private void EnsureVisibleImageColor()
    {
        if (!forceVisibleImageColor || targetImage == null)
            return;

        Color imageColor = targetImage.color;
        if (imageColor.a > 0.01f)
            return;

        imageColor.a = 1f;
        targetImage.color = imageColor;
    }

    private void EnsureRuntimeMaterial()
    {
        if (targetImage == null)
            return;

        Shader shader = outlineShader;
        if (shader == null)
            shader = Resources.Load<Shader>("Shaders/ExperimentalSlotOutlineUI");
        if (shader == null)
            shader = Shader.Find("OVERBURST/UI/Experimental Slot Outline UI");

        if (shader == null)
        {
            HideImageWhenShaderIsMissing();
            return;
        }

        if (runtimeMaterial == null || runtimeMaterial.shader != shader)
        {
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(runtimeMaterial);
                else
                    DestroyImmediate(runtimeMaterial);
            }

            runtimeMaterial = new Material(shader)
            {
                name = "Runtime_ExperimentalSlotOutlineUI",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (targetImage.material != runtimeMaterial)
            targetImage.material = runtimeMaterial;

        EnsureVisibleImageColor();
    }

    private void HideImageWhenShaderIsMissing()
    {
        if (targetImage == null)
            return;

        targetImage.material = null;
        Color imageColor = targetImage.color;
        imageColor.a = 0f;
        targetImage.color = imageColor;
    }

    private void ApplyProperties()
    {
        if (runtimeMaterial == null)
            return;

        runtimeMaterial.SetColor(ColorId, gradeColor);
        runtimeMaterial.SetFloat(OutlineThicknessId, outlineThickness);
        runtimeMaterial.SetFloat(GlowIntensityId, glowIntensity);
        runtimeMaterial.SetFloat(GlowSizeId, glowSize);
        runtimeMaterial.SetFloat(NoiseScaleId, noiseScale);
        runtimeMaterial.SetFloat(NoiseSpeedId, noiseSpeed);
        runtimeMaterial.SetFloat(NoiseStrengthId, noiseStrength);
        runtimeMaterial.SetFloat(PulseSpeedId, pulseSpeed);
        runtimeMaterial.SetFloat(ShapeModeId, useCircleShape ? 1f : 0f);
        runtimeMaterial.SetFloat(EffectModeId, (float)effectMode);
        runtimeMaterial.SetFloat(EdgeSoftnessId, edgeSoftness);
        runtimeMaterial.SetFloat(CornerCutId, cornerCut);
        runtimeMaterial.SetFloat(AlphaId, alpha);
    }
}

public enum ExperimentalSlotOutlineMode
{
    InnerElectric = 1,
    SharpLightning = 2,
    ArcPulse = 3,
    EmberEdge = 4,
    OverchargeAura = 5,
    RiftLightning = 6,
    NovaBloom = 7,
    OutlineInlineGradient = 8,
    OrbitLightningParticles = 9,
    FlameOrbit = 10,
    StarDust = 11,
    PrismSurge = 12,
    DoubleHalo = 13,
    CometTrail = 14,
    ChaosFestival = 15,
    TightFlameOrbit = 16,
    TightPrismSurge = 17,
    PrismCometTrail = 18,
    CrystalSparkRing = 19,
    TieredFlameElectric = 20
}

public static class ExperimentalSlotOutlineModeState
{
    public static ExperimentalSlotOutlineMode CurrentMode => ExperimentalSlotOutlineMode.TieredFlameElectric;
}
