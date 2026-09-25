using System;
using TMPro;
using UnityEngine;
using MoreMountains.Feedbacks;

public enum DamageNumberFeelPreset { Original, Pop, Burst, Scatter, Bounce }
public enum DamageNumberFontChoice { Pretendard, Spoqa, Noonnu, Hakgyo }
public enum DamageNumberWeightChoice { Regular, Bold }
public enum DamageNumberSizeChoice { Compact, Balanced, Original }

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class DamageNumberPopup : MMFloatingText
{
    public const float ReactionFontSize = 18f; // 원소반응 문구 크기 축소
    public const float ReactionCharacterSpacing = 4f;
    public const float VaporizeDamageHorizontalOffsetRange = 18f;
    public const float SpawnScreenMargin = 24f;
    public const float ExitScreenMargin = 64f;
    public static readonly Color CriticalAttackColor = new Color(1f, .77f, .24f);

    [SerializeField] private float lifetime = 1.5f;
    [SerializeField] private float fadeStartDelay = 0.8f;
    [SerializeField] private float riseSpeed = 52f;
    [SerializeField] private Vector2 randomOffsetRange = Vector2.zero;
    [SerializeField] private TMP_FontAsset koreanFontAsset;

    private static TMP_FontAsset cachedFont;
    private static readonly TMP_FontAsset[] DamageFonts = new TMP_FontAsset[4];

    private RectTransform rectTransform;
    private RectTransform projectionRoot;
    private TextMeshProUGUI text;
    private Action<DamageNumberPopup> onFinished;
    private Camera targetCamera;
    private Vector3 worldPosition;
    private Vector2 randomOffset;
    private Color startColor;
    private bool initialized;
    public static DamageNumberFeelPreset SelectedPreset { get; private set; } = DamageNumberFeelPreset.Bounce;
    public static DamageNumberFontChoice SelectedFont { get; private set; } = DamageNumberFontChoice.Spoqa;
    public static DamageNumberWeightChoice SelectedWeight { get; private set; } = DamageNumberWeightChoice.Regular;
    public static DamageNumberSizeChoice SelectedSize { get; private set; } = DamageNumberSizeChoice.Original;

    private static readonly AnimationCurve RiseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private static readonly AnimationCurve FastRiseCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(.16f, .66f), new Keyframe(.44f, .9f), new Keyframe(1f, 1f));
    private static readonly AnimationCurve BurstRiseCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(.12f, .62f), new Keyframe(.36f, .86f), new Keyframe(1f, 1f));
    private static readonly AnimationCurve SideCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(.2f, .72f), new Keyframe(.52f, .94f), new Keyframe(1f, 1f));
    private static readonly AnimationCurve NormalScale = new AnimationCurve(new Keyframe(0f, .72f), new Keyframe(.09f, 1.12f), new Keyframe(.22f, 1f), new Keyframe(1f, 1f));
    private static readonly AnimationCurve CriticalScale = new AnimationCurve(new Keyframe(0f, .65f), new Keyframe(.09f, 1.42f), new Keyframe(.24f, 1f), new Keyframe(1f, 1f));
    private static readonly AnimationCurve PopScale = new AnimationCurve(new Keyframe(0f, .42f), new Keyframe(.1f, 1.38f), new Keyframe(.3f, 1f), new Keyframe(1f, .96f));
    private static readonly AnimationCurve PopCriticalScale = new AnimationCurve(new Keyframe(0f, .38f), new Keyframe(.1f, 1.62f), new Keyframe(.3f, 1.08f), new Keyframe(1f, .98f));
    private static readonly AnimationCurve BurstScale = new AnimationCurve(new Keyframe(0f, .34f), new Keyframe(.12f, 1.52f), new Keyframe(.3f, 1.04f), new Keyframe(1f, .94f));
    private static readonly AnimationCurve BurstCriticalScale = new AnimationCurve(new Keyframe(0f, .3f), new Keyframe(.12f, 1.8f), new Keyframe(.32f, 1.12f), new Keyframe(1f, .96f));
    private static readonly AnimationCurve ScatterScale = new AnimationCurve(new Keyframe(0f, .65f), new Keyframe(.12f, 1.2f), new Keyframe(.28f, 1f), new Keyframe(1f, .9f));
    private static readonly AnimationCurve ScatterCriticalScale = new AnimationCurve(new Keyframe(0f, .56f), new Keyframe(.12f, 1.5f), new Keyframe(.3f, 1.05f), new Keyframe(1f, .92f));
    private static readonly AnimationCurve BounceScale = new AnimationCurve(new Keyframe(0f, .4f), new Keyframe(.1f, 1.5f), new Keyframe(.25f, .82f), new Keyframe(.4f, 1.08f), new Keyframe(1f, .95f));
    private static readonly AnimationCurve BounceCriticalScale = new AnimationCurve(new Keyframe(0f, .34f), new Keyframe(.1f, 1.78f), new Keyframe(.25f, .86f), new Keyframe(.42f, 1.16f), new Keyframe(1f, .96f));
    private static readonly AnimationCurve PopOpacity = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.36f, 1f), new Keyframe(1f, 0f));
    private static readonly AnimationCurve BurstOpacity = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.32f, 1f), new Keyframe(1f, 0f));
    private static readonly AnimationCurve ScatterOpacity = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.4f, 1f), new Keyframe(1f, 0f));
    private static readonly AnimationCurve BounceOpacity = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.38f, 1f), new Keyframe(1f, 0f));
    private AnimationCurve opacityCurve;

    public static void SelectPreset(DamageNumberFeelPreset preset)
    {
        if (preset < DamageNumberFeelPreset.Original || preset > DamageNumberFeelPreset.Bounce)
            throw new ArgumentOutOfRangeException(nameof(preset));
        SelectedPreset = preset;
    }

    public static void SelectFont(DamageNumberFontChoice font)
    {
        if (font < DamageNumberFontChoice.Pretendard || font > DamageNumberFontChoice.Hakgyo)
            throw new ArgumentOutOfRangeException(nameof(font));
        SelectedFont = font;
    }

    public static void SelectWeight(DamageNumberWeightChoice weight)
    {
        if (weight < DamageNumberWeightChoice.Regular || weight > DamageNumberWeightChoice.Bold)
            throw new ArgumentOutOfRangeException(nameof(weight));
        SelectedWeight = weight;
    }

    public static void SelectSize(DamageNumberSizeChoice size)
    {
        if (size < DamageNumberSizeChoice.Compact || size > DamageNumberSizeChoice.Original)
            throw new ArgumentOutOfRangeException(nameof(size));
        SelectedSize = size;
    }

    public static string FontLabel(DamageNumberFontChoice font)
    {
        switch (font)
        {
            case DamageNumberFontChoice.Spoqa: return "스포카";
            case DamageNumberFontChoice.Noonnu: return "눈누";
            case DamageNumberFontChoice.Hakgyo: return "학교안심";
            default: return "프리텐다드";
        }
    }

    public static string WeightLabel(DamageNumberWeightChoice weight) =>
        weight == DamageNumberWeightChoice.Bold ? "굵게" : "보통";

    public static string SizeLabel(DamageNumberSizeChoice size)
    {
        switch (size)
        {
            case DamageNumberSizeChoice.Compact: return "작게";
            case DamageNumberSizeChoice.Balanced: return "중간";
            default: return "기존";
        }
    }

    public static float FontSize(DamageNumberSizeChoice size, bool critical)
    {
        switch (size)
        {
            case DamageNumberSizeChoice.Compact: return critical ? 25f : 18f;
            case DamageNumberSizeChoice.Balanced: return critical ? 29f : 21f;
            default: return critical ? 34f : 24f;
        }
    }

    public static string PresetLabel(DamageNumberFeelPreset preset)
    {
        switch (preset)
        {
            case DamageNumberFeelPreset.Pop: return "팡";
            case DamageNumberFeelPreset.Burst: return "방사";
            case DamageNumberFeelPreset.Scatter: return "산개";
            case DamageNumberFeelPreset.Bounce: return "강타";
            default: return "기존";
        }
    }

    public static float PresetLifetime(DamageNumberFeelPreset preset)
    {
        switch (preset)
        {
            case DamageNumberFeelPreset.Pop: return .72f;
            case DamageNumberFeelPreset.Burst: return .68f;
            case DamageNumberFeelPreset.Scatter: return .84f;
            case DamageNumberFeelPreset.Bounce: return .78f;
            default: return 1.5f;
        }
    }

    private void Awake()
    {
        rectTransform = transform as RectTransform;
        ResolveText();
    }

    protected override void Initialization()
    {
        // The existing screen-space pool owns following/projection; no MMFollowTarget is needed.
        _startedAt = GetTime();
        rectTransform = transform as RectTransform;
        MovingPart = transform;
        ResolveText();
    }

    protected override void UpdateFloatingText()
    {
        if (!initialized) return;
        if (GetTime() - _startedAt >= _lifetime
            || !RefreshProjectedPosition(new Vector2(_newPosition.x, _newPosition.y), ExitScreenMargin))
        {
            CompleteAndReturn();
            return;
        }
        base.UpdateFloatingText();
    }

    protected override void HandleMovement()
    {
        base.HandleMovement();
        // Feel supplies the rise curve; retain the game's camera/canvas projection contract.
        RefreshProjectedPosition(new Vector2(_newPosition.x, _newPosition.y), ExitScreenMargin);
    }

    protected override void HandleAlignment() { }
    protected override void TurnOff() => CompleteAndReturn();
    public override void SetText(string value) { ResolveText(); text.text = value; }
    public override void SetColor(Color color) { ResolveText(); text.color = color; }
    public override void SetOpacity(float opacity) { var color = text.color; color.a = startColor.a * opacity; text.color = color; }

    public void SetProjectionRoot(RectTransform root)
    {
        projectionRoot = root;
    }

    public void SetTargetCamera(Camera camera)
    {
        targetCamera = camera;
    }

    public void Initialize(
        float damage,
        bool isCritical,
        Color damageColor,
        Vector3 position,
        Action<DamageNumberPopup> finishedCallback = null,
        float horizontalRandomOffsetRange = 0f)
    {
        InitializeText(
            null,
            isCritical && damageColor == Color.white ? CriticalAttackColor : damageColor,
            position,
            FontSize(SelectedSize, isCritical),
            finishedCallback,
            SelectedWeight == DamageNumberWeightChoice.Bold ? FontStyles.Bold : FontStyles.Normal,
            false,
            horizontalRandomOffsetRange,
            true,
            Mathf.Max(1, Mathf.RoundToInt(damage)),
            isCritical,
            true);
    }

    public void InitializeCustom(
        string displayText,
        Color color,
        Vector3 position,
        float fontSize,
        Action<DamageNumberPopup> finishedCallback = null,
        FontStyles fontStyle = FontStyles.Normal)
    {
        InitializeText(displayText, color, position, fontSize, finishedCallback, fontStyle, false, 0f, false, 0);
    }

    public void InitializeReaction(
        string displayText,
        Color color,
        Vector3 position,
        Action<DamageNumberPopup> finishedCallback = null)
    {
        InitializeText(
            displayText,
            color,
            position,
            ReactionFontSize,
            finishedCallback,
            FontStyles.Bold,
            true,
            0f,
            false,
            0);
    }

    private void InitializeText(
        string displayText,
        Color color,
        Vector3 position,
        float fontSize,
        Action<DamageNumberPopup> finishedCallback,
        FontStyles fontStyle,
        bool isReaction,
        float horizontalRandomOffsetRange,
        bool useIntegerText,
        int integerValue,
        bool isCritical = false,
        bool isDamage = false)
    {
        ResolveText();
        if (projectionRoot == null)
            projectionRoot = transform.parent as RectTransform;

        if (targetCamera == null)
            targetCamera = Camera.main;
        DamageNumberFeelPreset preset = isDamage ? SelectedPreset : DamageNumberFeelPreset.Original;
        SetUseUnscaledTime(isDamage && preset != DamageNumberFeelPreset.Original, false);
        onFinished = finishedCallback;
        _startedAt = GetTime();
        _newPosition = Vector3.zero;
        startColor = color;
        worldPosition = position;
        float resolvedHorizontalOffsetRange = horizontalRandomOffsetRange > 0f
            ? horizontalRandomOffsetRange
            : randomOffsetRange.x;
        randomOffset = isReaction
            ? Vector2.zero
            : new Vector2(
                UnityEngine.Random.Range(
                    -resolvedHorizontalOffsetRange,
                    resolvedHorizontalOffsetRange),
                UnityEngine.Random.Range(0f, randomOffsetRange.y));

        if (rectTransform != null)
        {
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }

        if (text != null)
        {
            Material baseFontMaterial = ApplyFont(isDamage);
            if (useIntegerText)
                text.SetText(isCritical ? "{0:0}!" : "{0:0}", integerValue);
            else
                text.text = displayText;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.fontWeight = fontStyle == FontStyles.Bold ? FontWeight.Bold : FontWeight.Regular;
            text.characterSpacing = isReaction ? ReactionCharacterSpacing : 0f;
            text.alignment = TextAlignmentOptions.Center;
            if (isReaction)
                FloatingFeedbackTextStyle.ApplyReaction(text, baseFontMaterial);
            else
                FloatingFeedbackTextStyle.Apply(text, baseFontMaterial);
        }

        bool projected = RefreshProjectedPosition(Vector2.zero, ExitScreenMargin);
        if (text != null)
        {
            Color displayColor = startColor;
            displayColor.a = projected ? startColor.a : 0f;
            text.color = displayColor;
        }

        if (!projected)
        {
            CompleteAndReturn();
            return;
        }
        initialized = true;
        gameObject.SetActive(true);
        MovingPart = transform;
        opacityCurve ??= new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(Mathf.Clamp01(fadeStartDelay / Mathf.Max(.01f, lifetime)), 1f), new Keyframe(1f, 0f));
        float activeLifetime = lifetime;
        float rise = riseSpeed * lifetime * (isCritical ? 1.15f : 1f);
        float lateralRange = 0f;
        AnimationCurve verticalCurve = RiseCurve;
        AnimationCurve scaleCurve = isCritical ? CriticalScale : NormalScale;
        AnimationCurve activeOpacity = opacityCurve;
        if (isDamage)
        {
            switch (preset)
            {
                case DamageNumberFeelPreset.Pop:
                    activeLifetime = .72f; rise = 72f; lateralRange = 12f;
                    verticalCurve = FastRiseCurve; scaleCurve = isCritical ? PopCriticalScale : PopScale;
                    activeOpacity = PopOpacity; break;
                case DamageNumberFeelPreset.Burst:
                    activeLifetime = .68f; rise = 96f; lateralRange = 38f;
                    verticalCurve = BurstRiseCurve; scaleCurve = isCritical ? BurstCriticalScale : BurstScale;
                    activeOpacity = BurstOpacity; break;
                case DamageNumberFeelPreset.Scatter:
                    activeLifetime = .84f; rise = 58f; lateralRange = 66f;
                    verticalCurve = FastRiseCurve; scaleCurve = isCritical ? ScatterCriticalScale : ScatterScale;
                    activeOpacity = ScatterOpacity; break;
                case DamageNumberFeelPreset.Bounce:
                    activeLifetime = .78f; rise = 62f; lateralRange = 18f;
                    verticalCurve = FastRiseCurve; scaleCurve = isCritical ? BounceCriticalScale : BounceScale;
                    activeOpacity = BounceOpacity; break;
            }
        }
        if (isCritical && preset != DamageNumberFeelPreset.Original)
            rise *= 1.15f;
        float lateral = lateralRange > 0f
            ? (UnityEngine.Random.value < .5f ? -1f : 1f) * UnityEngine.Random.Range(lateralRange * .5f, lateralRange)
            : 0f;
        SetProperties(text.text, activeLifetime, Vector3.up, true,
            MMFloatingTextSpawner.AlignmentModes.Fixed, Vector3.up, false, null,
            lateralRange > 0f, SideCurve, 0f, lateral, true, verticalCurve, 0f, rise,
            false, null, 0f, 0f, true, activeOpacity, 0f, 1f,
            true, scaleCurve, 0f, 1f, false, null);
    }

    private bool RefreshProjectedPosition(Vector2 motion, float screenMargin)
    {
        if (targetCamera == null) targetCamera = Camera.main;
        bool projected = WorldUiScreenProjection.TryProject(
            projectionRoot,
            targetCamera,
            worldPosition,
            out Vector2 anchoredPosition,
            screenMargin);
        if (rectTransform == null || !projected)
            return projected;

        Vector2 finalPosition = anchoredPosition + randomOffset + motion;
        finalPosition.x = Mathf.Round(finalPosition.x);
        finalPosition.y = Mathf.Round(finalPosition.y);
        rectTransform.anchoredPosition = finalPosition;
        return true;
    }

    public void ResetForPool()
    {
        onFinished = null;
        targetCamera = null;
        worldPosition = Vector3.zero;
        randomOffset = Vector2.zero;
        startColor = Color.clear;
        initialized = false;
        _newPosition = Vector3.zero;
        SetUseUnscaledTime(false, false);

        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }

        if (text != null)
        {
            text.text = string.Empty;
            text.color = Color.clear;
            text.fontSize = 24f;
            text.fontStyle = FontStyles.Normal;
            text.fontWeight = FontWeight.Regular;
            text.characterSpacing = 0f;
            text.alignment = TextAlignmentOptions.Center;
        }
    }

    private void CompleteAndReturn()
    {
        initialized = false;
        Action<DamageNumberPopup> finishedCallback = onFinished;
        onFinished = null;
        gameObject.SetActive(false);
        if (finishedCallback != null)
            finishedCallback(this);
        else
            ResetForPool();
    }

    private void ResolveText()
    {
        if (text == null)
            text = GetComponent<TextMeshProUGUI>();
        if (text != null)
            text.raycastTarget = false;
    }

    private Material ApplyFont(bool isDamage)
    {
        TMP_FontAsset font = isDamage ? ResolveDamageFont(SelectedFont) : null;
        if (font == null)
            font = koreanFontAsset != null ? koreanFontAsset : ResolveImportedFont();
        if (text == null || font == null)
            return text != null ? text.fontSharedMaterial : null;

        if (text.font != font)
            text.font = font;

        return font.material != null ? font.material : text.fontSharedMaterial;
    }

    private static TMP_FontAsset ResolveImportedFont()
    {
        if (cachedFont == null)
            cachedFont = Resources.Load<TMP_FontAsset>("UI/Fonts/DamageFloating/Pretendard_Medium SDF");
        return cachedFont;
    }

    private static TMP_FontAsset ResolveDamageFont(DamageNumberFontChoice choice)
    {
        if (choice == DamageNumberFontChoice.Pretendard)
            return ResolveImportedFont();

        int index = (int)choice;
        if (DamageFonts[index] != null)
            return DamageFonts[index];

        string name;
        switch (choice)
        {
            case DamageNumberFontChoice.Spoqa: name = "TMP_SpoqaHanSansNeo_Body"; break;
            case DamageNumberFontChoice.Noonnu: name = "TMP_NoonnuBasicGothic_Button"; break;
            case DamageNumberFontChoice.Hakgyo: name = "TMP_HakgyoansimYeohaeng_Title"; break;
            default: return ResolveImportedFont();
        }
        DamageFonts[index] = Resources.Load<TMP_FontAsset>("UI/Fonts/ProjectMT/FontAssets/" + name);
        if (DamageFonts[index] == null)
            Debug.LogError("[DamageNumberPopup] Missing damage font: " + name);
        return DamageFonts[index];
    }
}
