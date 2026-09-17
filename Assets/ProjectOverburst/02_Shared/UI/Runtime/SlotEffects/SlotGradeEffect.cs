using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SlotGradeEffect : MonoBehaviour // 등급 연출
{
    private const int GradientTextureSize = 64; // 등급 그라데이션 해상도
    private const float GradeGradientAreaRadiusScale = 1.18f; // 그라데이션 영역 반경
    private const float GradeGradientOuterZeroThreshold = 0.18f; // 그라데이션 영역 바깥 0 구간
    private const float GradeOverlayInset = 5f; // 그라데이션 크기 축소 여백
    private const float UncommonOutlineThickness = 0.026f; // 슬롯 외곽선
    private const float RareOutlineThickness = 0.026f; // 슬롯 외곽선
    private const float EpicOutlineThickness = 0.026f; // 슬롯 외곽선
    private const float LegendaryOutlineThickness = 0.028f; // 슬롯 외곽선
    private const float ArtifactOutlineThickness = 0.028f; // 슬롯 외곽선
    private const float MythicCursedOutlineThickness = 0.030f; // 슬롯 외곽선
    private static readonly bool UseOrbitDots = false; // true면 고등급 회전 점 복구
    private static readonly List<SlotGradeEffect> ActiveEffects = new List<SlotGradeEffect>(); // 런타임 토글 대상

    private static Sprite radialGradientSprite; // 공용 원형 그라데이션

    [SerializeField] private float orbitSpeed = 150f; // 회전 속도
    [SerializeField] private float slotSize = 55f; // 슬롯 크기
    [SerializeField] private float dotSize = 3.4f; // 점 크기
    [SerializeField] private int trailSegmentCount = 18; // 잔상 개수
    [SerializeField] private float trailAngleSpacing = 6.5f; // 잔상 간격
    [SerializeField] private float trailAlpha = 0.56f; // 잔상 alpha
    [SerializeField] private float overlayAlpha = 0.48f; // 배경 alpha
    [SerializeField] private bool useExperimentalOutline = true; // 등급 외곽선
    [SerializeField] private bool useCircleOutline; // 원형 슬롯

    private readonly List<OrbitDotVisual> orbitDots = new List<OrbitDotVisual>(); // 회전 점
    private Image gradeOverlay; // 등급 배경
    private ExperimentalSlotOutlineEffect experimentalOutline; // 등급 외곽선
    private float orbitAngle; // 회전 각도
    private Color originalOverlayColor = Color.clear; // 원본 색
    private ItemGrade currentGrade; // 현재 등급
    private Color currentGradeColor = Color.clear; // 현재 등급색
    private bool hasCurrentGrade; // 현재 등급 보유 여부

    public static bool RuntimeOutlineEnabled { get; private set; } = true; // 실험용 런타임 토글

    public static void SetRuntimeOutlineEnabled(bool enabled)
    {
        if (RuntimeOutlineEnabled == enabled)
            return;

        RuntimeOutlineEnabled = enabled;
        RefreshActiveOutlines();
    }

    private static void RefreshActiveOutlines()
    {
        for (int i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            SlotGradeEffect effect = ActiveEffects[i];
            if (effect == null)
            {
                ActiveEffects.RemoveAt(i);
                continue;
            }

            effect.RefreshRuntimeOutline();
        }
    }

    public void Init(Image icon, Image background)
    {
        EnsureGradeOverlay();
        EnsureExperimentalOutline();
        MoveOverlayBehindIcon(icon, background);
    }

    private void OnEnable()
    {
        if (!ActiveEffects.Contains(this))
            ActiveEffects.Add(this);

        RefreshRuntimeOutline();
    }

    private void OnDisable()
    {
        ActiveEffects.Remove(this);
    }

    private void Update()
    {
        if (orbitDots.Count == 0)
            return;

        orbitAngle += orbitSpeed * Time.unscaledDeltaTime;
        float halfSize = slotSize * 0.5f - dotSize; // 궤도 반경

        for (int i = 0; i < orbitDots.Count; i++)
        {
            float angle = orbitAngle + 360f / orbitDots.Count * i; // 점 각도
            UpdateOrbitDot(orbitDots[i], angle, halfSize);
        }
    }

    public void SetGrade(ItemGrade grade, Color gradeColor)
    {
        EnsureGradeOverlay();
        Clear();

        if (gradeOverlay != null)
        {
            Color overlayColor = gradeColor; // 등급색
            overlayColor.a = GetOverlayAlpha(grade);
            gradeOverlay.color = overlayColor;
            originalOverlayColor = overlayColor;
        }

        currentGrade = grade;
        currentGradeColor = gradeColor;
        hasCurrentGrade = true;
        SetExperimentalOutline(grade, gradeColor);

        int dotCount = GetDotCount(grade); // 등급 점 수

        for (int i = 0; i < dotCount; i++)
        {
            orbitDots.Add(CreateOrbitDot(i, gradeColor));
        }
    }

    public void Clear()
    {
        if (gradeOverlay != null)
            gradeOverlay.color = Color.clear;

        if (experimentalOutline != null)
            experimentalOutline.gameObject.SetActive(false);

        for (int i = 0; i < orbitDots.Count; i++)
        {
            orbitDots[i].Destroy();
        }

        orbitDots.Clear();
        orbitAngle = 0f;
        originalOverlayColor = Color.clear;
        currentGradeColor = Color.clear;
        hasCurrentGrade = false;
    }

    public void ClearFadeDots()
    {
        for (int i = 0; i < orbitDots.Count; i++)
            orbitDots[i].HideTrail();
    }

    public Color GetOverlayColor()
    {
        return originalOverlayColor;
    }

    public bool TryGetCurrentGrade(out ItemGrade grade)
    {
        grade = currentGrade;
        return hasCurrentGrade;
    }

    private int GetDotCount(ItemGrade grade)
    {
        if (!UseOrbitDots)
            return 0;

        if (grade == ItemGrade.Legendary)
            return 1;

        if (grade == ItemGrade.Artifact)
            return 2;

        if (grade == ItemGrade.Mythic)
            return 3;

        return 0;
    }

    private Vector2 GetRectPosition(float angle, float halfSize)
    {
        float radians = angle * Mathf.Deg2Rad; // 라디안
        float cos = Mathf.Cos(radians); // x축
        float sin = Mathf.Sin(radians); // y축
        float absCos = Mathf.Abs(cos); // x 절댓값
        float absSin = Mathf.Abs(sin); // y 절댓값

        if (absCos > absSin)
            return new Vector2(Mathf.Sign(cos) * halfSize, sin / absCos * halfSize);

        return new Vector2(cos / absSin * halfSize, Mathf.Sign(sin) * halfSize);
    }

    private OrbitDotVisual CreateOrbitDot(int index, Color gradeColor)
    {
        OrbitDotVisual visual = new OrbitDotVisual();
        int segmentCount = Mathf.Max(0, trailSegmentCount);

        visual.trailRects = new RectTransform[segmentCount];
        visual.trailImages = new Image[segmentCount];

        for (int i = segmentCount - 1; i >= 0; i--)
        {
            GameObject trail = new GameObject("OrbitDot" + index + "_Trail" + i);
            trail.transform.SetParent(transform, false);

            Image image = trail.AddComponent<Image>();
            image.color = GetTrailColor(gradeColor, i, segmentCount);
            image.raycastTarget = false;

            RectTransform rect = trail.GetComponent<RectTransform>();
            float size = GetTrailSize(i, segmentCount);
            rect.sizeDelta = new Vector2(size, size);

            visual.trailRects[i] = rect;
            visual.trailImages[i] = image;
        }

        GameObject dot = new GameObject("OrbitDot" + index); // 회전 점
        dot.transform.SetParent(transform, false);

        Image dotImage = dot.AddComponent<Image>();
        dotImage.color = gradeColor;
        dotImage.raycastTarget = false;

        RectTransform dotRect = dot.GetComponent<RectTransform>();
        dotRect.sizeDelta = new Vector2(dotSize, dotSize);

        visual.headRect = dotRect;
        visual.headImage = dotImage;
        return visual;
    }

    private void UpdateOrbitDot(OrbitDotVisual visual, float angle, float halfSize)
    {
        if (visual == null)
            return;

        if (visual.headRect != null)
            visual.headRect.anchoredPosition = GetRectPosition(angle, halfSize);

        if (visual.trailRects == null)
            return;

        for (int i = 0; i < visual.trailRects.Length; i++)
        {
            RectTransform rect = visual.trailRects[i];
            if (rect == null)
                continue;

            float trailAngle = angle - (i + 1) * trailAngleSpacing;
            rect.anchoredPosition = GetRectPosition(trailAngle, halfSize);
        }
    }

    private Color GetTrailColor(Color gradeColor, int index, int segmentCount)
    {
        Color color = gradeColor;
        if (segmentCount <= 0)
        {
            color.a = 0f;
            return color;
        }

        float normalized = 1f - index / (float)segmentCount;
        color.a = trailAlpha * Mathf.Pow(normalized, 1.18f);
        return color;
    }

    private float GetTrailSize(int index, int segmentCount)
    {
        if (segmentCount <= 1)
            return dotSize * 0.55f;

        float normalized = 1f - index / (float)(segmentCount - 1);
        return Mathf.Lerp(dotSize * 0.38f, dotSize * 0.86f, normalized);
    }

    private void EnsureGradeOverlay()
    {
        if (gradeOverlay != null)
            return;

        Transform existing = transform.Find("GradeOverlay"); // 기존 배경
        GameObject overlayObject = existing != null ? existing.gameObject : new GameObject("GradeOverlay"); // 배경 오브젝트
        overlayObject.transform.SetParent(transform, false);
        overlayObject.transform.SetAsFirstSibling();

        gradeOverlay = overlayObject.GetComponent<Image>();
        if (gradeOverlay == null)
            gradeOverlay = overlayObject.AddComponent<Image>();

        gradeOverlay.sprite = EnsureRadialGradientSprite();
        gradeOverlay.type = Image.Type.Simple;
        gradeOverlay.preserveAspect = true;
        gradeOverlay.color = Color.clear;
        gradeOverlay.raycastTarget = false;

        RectTransform rect = overlayObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * GradeOverlayInset;
        rect.offsetMax = -Vector2.one * GradeOverlayInset;
    }

    private void EnsureExperimentalOutline()
    {
        if (!useExperimentalOutline || experimentalOutline != null)
            return;

        Transform existing = transform.Find("ExperimentalGradeOutline");
        GameObject outlineObject = existing != null ? existing.gameObject : new GameObject("ExperimentalGradeOutline");
        outlineObject.transform.SetParent(transform, false);
        outlineObject.transform.SetAsFirstSibling();

        Image image = outlineObject.GetComponent<Image>();
        if (image == null)
            image = outlineObject.AddComponent<Image>();

        image.color = Color.white;
        image.raycastTarget = false;

        experimentalOutline = outlineObject.GetComponent<ExperimentalSlotOutlineEffect>();
        if (experimentalOutline == null)
            experimentalOutline = outlineObject.AddComponent<ExperimentalSlotOutlineEffect>();

        RectTransform rect = outlineObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        outlineObject.SetActive(false);
    }

    private void SetExperimentalOutline(ItemGrade grade, Color gradeColor)
    {
        if (!useExperimentalOutline)
            return;

        if (grade == ItemGrade.Common)
        {
            if (experimentalOutline != null)
                experimentalOutline.gameObject.SetActive(false);

            return;
        }

        if (!RuntimeOutlineEnabled)
        {
            if (experimentalOutline != null)
                experimentalOutline.gameObject.SetActive(false);

            return;
        }

        EnsureExperimentalOutline();

        if (experimentalOutline == null)
            return;

        experimentalOutline.gameObject.SetActive(true);

        ExperimentalSlotOutlineMode selectedMode = ExperimentalSlotOutlineModeState.CurrentMode;
        ExperimentalSlotOutlineMode shaderMode = ResolveExperimentalShaderMode(grade, selectedMode);

        experimentalOutline.SetGradeColor(gradeColor);
        experimentalOutline.SetCircleShape(useCircleOutline);
        experimentalOutline.SetMode(shaderMode);
        experimentalOutline.SetIntensity(
            GetExperimentalOutlineThickness(grade, selectedMode),
            GetExperimentalGlowIntensity(grade, selectedMode),
            GetExperimentalGlowSize(grade, selectedMode),
            GetExperimentalNoiseStrength(grade, selectedMode),
            GetExperimentalPulseSpeed(grade, selectedMode),
            GetExperimentalAlpha(grade, selectedMode));
        ApplyExperimentalOutlinePadding(selectedMode);
    }

    private void RefreshRuntimeOutline()
    {
        if (!hasCurrentGrade || !useExperimentalOutline)
        {
            if (experimentalOutline != null)
                experimentalOutline.gameObject.SetActive(false);

            return;
        }

        SetExperimentalOutline(currentGrade, currentGradeColor);
    }

    private Sprite EnsureRadialGradientSprite()
    {
        if (radialGradientSprite != null)
            return radialGradientSprite;

        Texture2D texture = new Texture2D(GradientTextureSize, GradientTextureSize, TextureFormat.RGBA32, false);
        texture.name = "SlotGradeRadialGradient";
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((GradientTextureSize - 1) * 0.5f, (GradientTextureSize - 1) * 0.5f);
        float radius = center.x * GradeGradientAreaRadiusScale;

        for (int y = 0; y < GradientTextureSize; y++)
        {
            for (int x = 0; x < GradientTextureSize; x++)
            {
                float distance01 = Vector2.Distance(new Vector2(x, y), center) / radius;
                float radialAlpha = Mathf.Clamp01(1f - distance01);
                float alpha = Mathf.InverseLerp(GradeGradientOuterZeroThreshold, 1f, radialAlpha);
                alpha = Mathf.SmoothStep(0f, 1f, alpha);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(false, true);
        radialGradientSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, GradientTextureSize, GradientTextureSize),
            new Vector2(0.5f, 0.5f),
            GradientTextureSize);
        radialGradientSprite.name = "SlotGradeRadialGradientSprite";
        radialGradientSprite.hideFlags = HideFlags.HideAndDontSave;
        return radialGradientSprite;
    }

    private float GetOverlayAlpha(ItemGrade grade)
    {
        if (grade == ItemGrade.Common)
            return overlayAlpha * 0.45f;

        if (grade == ItemGrade.Uncommon || grade == ItemGrade.Rare || grade == ItemGrade.Epic)
            return overlayAlpha * 0.85f;

        return overlayAlpha;
    }

    private ExperimentalSlotOutlineMode ResolveExperimentalShaderMode(ItemGrade grade, ExperimentalSlotOutlineMode mode)
    {
        if (mode != ExperimentalSlotOutlineMode.TieredFlameElectric)
            return mode;

        if (grade == ItemGrade.Uncommon || grade == ItemGrade.Rare)
            return ExperimentalSlotOutlineMode.InnerElectric;

        return ExperimentalSlotOutlineMode.TightFlameOrbit;
    }

    private float GetExperimentalOutlineThickness(ItemGrade grade, ExperimentalSlotOutlineMode mode)
    {
        if (mode == ExperimentalSlotOutlineMode.TieredFlameElectric)
        {
            if (grade == ItemGrade.Uncommon)
                return UncommonOutlineThickness;

            if (grade == ItemGrade.Rare)
                return RareOutlineThickness;

            if (grade == ItemGrade.Epic)
                return EpicOutlineThickness;

            if (grade == ItemGrade.Legendary)
                return LegendaryOutlineThickness;

            if (grade == ItemGrade.Artifact)
                return ArtifactOutlineThickness;

            return MythicCursedOutlineThickness;
        }

        if ((int)mode >= 9)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.032f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.045f;

            return 0.062f;
        }

        if (mode == ExperimentalSlotOutlineMode.SharpLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.014f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.02f;

            return 0.028f;
        }

        if (mode == ExperimentalSlotOutlineMode.ArcPulse)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.022f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.032f;

            return 0.042f;
        }

        if (mode == ExperimentalSlotOutlineMode.EmberEdge)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.026f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.038f;

            return 0.052f;
        }

        if (mode == ExperimentalSlotOutlineMode.OverchargeAura)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.034f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.048f;

            return 0.064f;
        }

        if (mode == ExperimentalSlotOutlineMode.RiftLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.022f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.032f;

            return 0.045f;
        }

        if (mode == ExperimentalSlotOutlineMode.NovaBloom)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.04f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.055f;

            return 0.075f;
        }

        if (mode == ExperimentalSlotOutlineMode.OutlineInlineGradient)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.026f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.034f;

            return 0.044f;
        }

        if (grade == ItemGrade.Uncommon)
            return 0.018f;

        if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
            return 0.026f;

        return 0.034f;
    }

    private float GetExperimentalGlowIntensity(ItemGrade grade, ExperimentalSlotOutlineMode mode)
    {
        if (mode == ExperimentalSlotOutlineMode.TieredFlameElectric)
        {
            if (grade == ItemGrade.Common)
                return 1.2f;

            if (grade == ItemGrade.Uncommon)
                return 2.8f;

            if (grade == ItemGrade.Rare)
                return 4.1f;

            if (grade == ItemGrade.Epic)
                return 6.6f;

            if (grade == ItemGrade.Legendary)
                return 7.4f;

            return 8f;
        }

        if ((int)mode >= 9)
        {
            if (grade == ItemGrade.Uncommon)
                return 4.6f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 6.6f;

            return 8f;
        }

        if (mode == ExperimentalSlotOutlineMode.SharpLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 4.2f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 6.2f;

            return 7.6f;
        }

        if (mode == ExperimentalSlotOutlineMode.ArcPulse)
        {
            if (grade == ItemGrade.Uncommon)
                return 3.2f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 4.5f;

            return 6f;
        }

        if (mode == ExperimentalSlotOutlineMode.EmberEdge)
        {
            if (grade == ItemGrade.Uncommon)
                return 2.8f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 4f;

            return 5.2f;
        }

        if (mode == ExperimentalSlotOutlineMode.OverchargeAura)
        {
            if (grade == ItemGrade.Uncommon)
                return 4.4f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 6.3f;

            return 7.7f;
        }

        if (mode == ExperimentalSlotOutlineMode.RiftLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 5f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 7f;

            return 8f;
        }

        if (mode == ExperimentalSlotOutlineMode.NovaBloom)
        {
            if (grade == ItemGrade.Uncommon)
                return 3.8f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 5.8f;

            return 7.2f;
        }

        if (mode == ExperimentalSlotOutlineMode.OutlineInlineGradient)
        {
            if (grade == ItemGrade.Uncommon)
                return 3.6f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 5.4f;

            return 7f;
        }

        if (grade == ItemGrade.Uncommon)
            return 2.8f;

        if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
            return 4.1f;

        return 5.4f;
    }

    private float GetExperimentalGlowSize(ItemGrade grade, ExperimentalSlotOutlineMode mode)
    {
        if (mode == ExperimentalSlotOutlineMode.TieredFlameElectric)
        {
            if (grade == ItemGrade.Common)
                return 0.01f;

            if (grade == ItemGrade.Uncommon)
                return 0.018f;

            if (grade == ItemGrade.Rare)
                return 0.026f;

            if (grade == ItemGrade.Epic)
                return 0.085f;

            if (grade == ItemGrade.Legendary)
                return 0.105f;

            return 0.125f;
        }

        if ((int)mode >= 9)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.07f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.095f;

            return 0.125f;
        }

        if (mode == ExperimentalSlotOutlineMode.SharpLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.012f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.018f;

            return 0.024f;
        }

        if (mode == ExperimentalSlotOutlineMode.ArcPulse)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.024f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.034f;

            return 0.045f;
        }

        if (mode == ExperimentalSlotOutlineMode.EmberEdge)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.038f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.05f;

            return 0.07f;
        }

        if (mode == ExperimentalSlotOutlineMode.OverchargeAura)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.055f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.075f;

            return 0.1f;
        }

        if (mode == ExperimentalSlotOutlineMode.RiftLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.035f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.05f;

            return 0.068f;
        }

        if (mode == ExperimentalSlotOutlineMode.NovaBloom)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.075f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.1f;

            return 0.13f;
        }

        if (mode == ExperimentalSlotOutlineMode.OutlineInlineGradient)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.064f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.086f;

            return 0.11f;
        }

        if (grade == ItemGrade.Uncommon)
            return 0.018f;

        if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
            return 0.026f;

        return 0.036f;
    }

    private float GetExperimentalNoiseStrength(ItemGrade grade, ExperimentalSlotOutlineMode mode)
    {
        if (mode == ExperimentalSlotOutlineMode.TieredFlameElectric)
        {
            if (grade == ItemGrade.Common)
                return 0.22f;

            if (grade == ItemGrade.Uncommon)
                return 0.75f;

            if (grade == ItemGrade.Rare)
                return 1.05f;

            if (grade == ItemGrade.Epic)
                return 1.25f;

            if (grade == ItemGrade.Legendary)
                return 1.45f;

            return 1.65f;
        }

        if ((int)mode >= 9)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.9f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1.25f;

            return 1.65f;
        }

        if (mode == ExperimentalSlotOutlineMode.SharpLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 1f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1.35f;

            return 1.7f;
        }

        if (mode == ExperimentalSlotOutlineMode.ArcPulse)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.45f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.65f;

            return 0.85f;
        }

        if (mode == ExperimentalSlotOutlineMode.EmberEdge)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.7f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.95f;

            return 1.2f;
        }

        if (mode == ExperimentalSlotOutlineMode.OverchargeAura)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.95f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1.2f;

            return 1.45f;
        }

        if (mode == ExperimentalSlotOutlineMode.RiftLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 1.2f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1.55f;

            return 1.9f;
        }

        if (mode == ExperimentalSlotOutlineMode.NovaBloom)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.8f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1.1f;

            return 1.45f;
        }

        if (mode == ExperimentalSlotOutlineMode.OutlineInlineGradient)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.72f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.96f;

            return 1.24f;
        }

        if (grade == ItemGrade.Uncommon)
            return 0.75f;

        if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
            return 1.05f;

        return 1.35f;
    }

    private float GetExperimentalPulseSpeed(ItemGrade grade, ExperimentalSlotOutlineMode mode)
    {
        if (mode == ExperimentalSlotOutlineMode.TieredFlameElectric)
        {
            if (grade == ItemGrade.Common)
                return 0.35f;

            if (grade == ItemGrade.Uncommon)
                return 0.7f;

            if (grade == ItemGrade.Rare)
                return 1.2f;

            if (grade == ItemGrade.Epic)
                return 1.65f;

            if (grade == ItemGrade.Legendary)
                return 2f;

            return 2.2f;
        }

        if ((int)mode >= 9)
        {
            if (grade == ItemGrade.Uncommon)
                return 1.2f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1.65f;

            return 2.2f;
        }

        if (mode == ExperimentalSlotOutlineMode.SharpLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 2.6f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 3.4f;

            return 4.2f;
        }

        if (mode == ExperimentalSlotOutlineMode.ArcPulse)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.8f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1f;

            return 1.2f;
        }

        if (mode == ExperimentalSlotOutlineMode.EmberEdge)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.5f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.75f;

            return 1f;
        }

        if (mode == ExperimentalSlotOutlineMode.OverchargeAura)
        {
            if (grade == ItemGrade.Uncommon)
                return 1.2f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1.7f;

            return 2.2f;
        }

        if (mode == ExperimentalSlotOutlineMode.RiftLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 3.4f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 4.6f;

            return 5.8f;
        }

        if (mode == ExperimentalSlotOutlineMode.NovaBloom)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.9f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1.25f;

            return 1.65f;
        }

        if (mode == ExperimentalSlotOutlineMode.OutlineInlineGradient)
            return 0f;

        if (grade == ItemGrade.Uncommon)
            return 0.7f;

        if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
            return 1.2f;

        return 1.8f;
    }

    private float GetExperimentalAlpha(ItemGrade grade, ExperimentalSlotOutlineMode mode)
    {
        if (mode == ExperimentalSlotOutlineMode.TieredFlameElectric)
        {
            return 0.96f;
        }

        if ((int)mode >= 9)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.86f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.96f;

            return 1f;
        }

        if (mode == ExperimentalSlotOutlineMode.SharpLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.84f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.96f;

            return 1f;
        }

        if (mode == ExperimentalSlotOutlineMode.ArcPulse)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.72f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.86f;

            return 1f;
        }

        if (mode == ExperimentalSlotOutlineMode.EmberEdge)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.68f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.82f;

            return 0.94f;
        }

        if (mode == ExperimentalSlotOutlineMode.OverchargeAura)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.86f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.96f;

            return 1f;
        }

        if (mode == ExperimentalSlotOutlineMode.RiftLightning)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.9f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 1f;

            return 1f;
        }

        if (mode == ExperimentalSlotOutlineMode.NovaBloom)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.78f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.92f;

            return 1f;
        }

        if (mode == ExperimentalSlotOutlineMode.OutlineInlineGradient)
        {
            if (grade == ItemGrade.Uncommon)
                return 0.78f;

            if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
                return 0.88f;

            return 0.96f;
        }

        if (grade == ItemGrade.Uncommon)
            return 0.72f;

        if (grade == ItemGrade.Rare || grade == ItemGrade.Epic)
            return 0.9f;

        return 1f;
    }

    private void ApplyExperimentalOutlinePadding(ExperimentalSlotOutlineMode mode)
    {
        if (experimentalOutline == null)
            return;

        RectTransform rect = experimentalOutline.transform as RectTransform;
        if (rect == null)
            return;

        float padding = GetExperimentalOutlinePadding(mode);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-padding, -padding);
        rect.offsetMax = new Vector2(padding, padding);
    }

    private float GetExperimentalOutlinePadding(ExperimentalSlotOutlineMode mode)
    {
        switch (mode)
        {
            case ExperimentalSlotOutlineMode.OrbitLightningParticles:
                return 7f;
            case ExperimentalSlotOutlineMode.FlameOrbit:
                return 10f;
            case ExperimentalSlotOutlineMode.StarDust:
                return 8f;
            case ExperimentalSlotOutlineMode.PrismSurge:
                return 7f;
            case ExperimentalSlotOutlineMode.DoubleHalo:
                return 9f;
            case ExperimentalSlotOutlineMode.CometTrail:
                return 11f;
            case ExperimentalSlotOutlineMode.ChaosFestival:
                return 12f;
            case ExperimentalSlotOutlineMode.PrismCometTrail:
                return 3f;
            case ExperimentalSlotOutlineMode.CrystalSparkRing:
                return 2f;
            default:
                return 0f;
        }
    }

    private void MoveOverlayBehindIcon(Image icon, Image background)
    {
        if (gradeOverlay == null)
            return;

        int targetIndex = 0;
        if (background != null && background.transform.parent == transform)
            background.transform.SetSiblingIndex(targetIndex++);

        if (experimentalOutline != null)
            experimentalOutline.transform.SetSiblingIndex(Mathf.Min(targetIndex++, transform.childCount - 1));

        gradeOverlay.transform.SetSiblingIndex(Mathf.Min(targetIndex++, transform.childCount - 1));

        if (icon == null || icon.transform.parent != transform)
            return;

        icon.transform.SetSiblingIndex(Mathf.Min(targetIndex, transform.childCount - 1));
    }

    private sealed class OrbitDotVisual
    {
        public RectTransform headRect;
        public Image headImage;
        public RectTransform[] trailRects;
        public Image[] trailImages;

        public void HideTrail()
        {
            if (trailImages == null)
                return;

            for (int i = 0; i < trailImages.Length; i++)
            {
                if (trailImages[i] == null)
                    continue;

                Color color = trailImages[i].color;
                color.a = 0f;
                trailImages[i].color = color;
            }
        }

        public void Destroy()
        {
            if (headRect != null)
                Object.Destroy(headRect.gameObject);

            if (trailRects == null)
                return;

            for (int i = 0; i < trailRects.Length; i++)
            {
                if (trailRects[i] != null)
                    Object.Destroy(trailRects[i].gameObject);
            }
        }
    }
}

internal static class SlotOutlineRuntimeToggleButton
{
    private const string ButtonName = "SlotOutlineRuntimeToggleButtonUI";
    private const string LabelName = "Label";
    private const float ButtonWidth = 150f;
    private const float ButtonHeight = 28f;
    private const float ButtonSpacing = 6f;

    private static Button button;
    private static Image background;
    private static TextMeshProUGUI label;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureButton();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureButton();
    }

    private static void EnsureButton()
    {
        if (!IsDebugUiAllowed())
            return;

        RectTransform parent = ResolveParent();
        if (parent == null)
            return;

        bool createdButton = false;
        GameObject buttonObject = ResolveExistingButton(parent);
        if (buttonObject == null)
        {
            buttonObject = new GameObject(ButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            createdButton = true;
        }

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        ConfigureButtonRect(rect, parent);

        background = buttonObject.GetComponent<Image>();
        background.raycastTarget = true;

        button = buttonObject.GetComponent<Button>();
        ConfigureButtonColors();

        if (createdButton || buttonObject.GetComponentInChildren<TextMeshProUGUI>(true) == null)
            CreateLabel(buttonObject.transform, parent);
        else
            label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);

        RegisterButton();
        RefreshVisual();

        if (buttonObject.activeInHierarchy && UnityEngine.EventSystems.EventSystem.current == null)
            Debug.LogWarning("[SlotOutlineToggle] EventSystem이 없어 외곽선 토글 버튼 클릭 입력을 받을 수 없습니다.");
    }

    private static RectTransform ResolveParent()
    {
        return SlotEffectTuningControlsRegistry.GetRoot();
    }

    private static GameObject ResolveExistingButton(RectTransform parent)
    {
        Transform existingInParent = parent.Find(ButtonName);
        if (existingInParent != null)
            return existingInParent.gameObject;

        GameObject existingActive = GameObject.Find(ButtonName);
        if (existingActive == null)
            return null;

        existingActive.transform.SetParent(parent, false);
        return existingActive;
    }

    private static void ConfigureButtonRect(RectTransform rect, RectTransform parent)
    {
        rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

        if (parent.name == "DebugPanel")
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = ResolveDebugPanelPosition(parent);
            return;
        }

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(18f, -18f);
    }

    private static Vector2 ResolveDebugPanelPosition(RectTransform parent)
    {
        float x = 18f;
        float top = 58f;

        for (int i = 0; i < parent.childCount; i++)
        {
            RectTransform child = parent.GetChild(i) as RectTransform;
            if (child == null || child.name == ButtonName)
                continue;

            if (child.anchorMin != Vector2.zero || child.anchorMax != Vector2.zero)
                continue;

            x = child.anchoredPosition.x;
            top = Mathf.Max(top, child.anchoredPosition.y + Mathf.Max(ButtonHeight, child.sizeDelta.y));
        }

        return new Vector2(x, top + ButtonSpacing);
    }

    private static void CreateLabel(Transform parent, RectTransform fontSourceRoot)
    {
        GameObject labelObject = new GameObject(LabelName, typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        label = labelObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.fontSize = 13f;
        label.color = new Color(0.93f, 0.97f, 1f, 1f);
        label.raycastTarget = false;

        CopyFontFromExistingDebugLabel(fontSourceRoot);
    }

    private static void CopyFontFromExistingDebugLabel(RectTransform root)
    {
        if (label == null || root == null)
            return;

        TextMeshProUGUI[] labels = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (TryCopyFontFromLabels(labels))
            return;

        labels = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        TryCopyFontFromLabels(labels);
    }

    private static bool TryCopyFontFromLabels(TextMeshProUGUI[] labels)
    {
        if (label == null || labels == null)
            return false;

        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI source = labels[i];
            if (source == null || source == label || source.font == null)
                continue;

            label.font = source.font;
            label.fontSharedMaterial = source.fontSharedMaterial;
            return true;
        }

        return false;
    }

    private static void CacheReferences(GameObject buttonObject)
    {
        button = buttonObject.GetComponent<Button>();
        background = buttonObject.GetComponent<Image>();
        label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private static void RegisterButton()
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(ToggleOutline);
        button.onClick.AddListener(ToggleOutline);
    }

    private static void ToggleOutline()
    {
        SlotGradeEffect.SetRuntimeOutlineEnabled(!SlotGradeEffect.RuntimeOutlineEnabled);
        RefreshVisual();
    }

    private static void RefreshVisual()
    {
        if (label != null)
            label.text = SlotGradeEffect.RuntimeOutlineEnabled ? "외곽선 ON" : "외곽선 OFF";

        if (background != null)
            background.color = SlotGradeEffect.RuntimeOutlineEnabled
                ? new Color(0.06f, 0.20f, 0.18f, 0.9f)
                : new Color(0.18f, 0.07f, 0.07f, 0.9f);

        ConfigureButtonColors();
    }

    private static void ConfigureButtonColors()
    {
        if (button == null)
            return;

        Color normalColor = SlotGradeEffect.RuntimeOutlineEnabled
            ? new Color(0.06f, 0.20f, 0.18f, 0.9f)
            : new Color(0.18f, 0.07f, 0.07f, 0.9f);

        ColorBlock colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = new Color(0.14f, 0.24f, 0.26f, 0.96f);
        colors.pressedColor = new Color(0.04f, 0.31f, 0.28f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private static bool IsDebugUiAllowed()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }
}
