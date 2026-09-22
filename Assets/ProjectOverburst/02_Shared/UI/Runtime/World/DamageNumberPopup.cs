using System;
using TMPro;
using UnityEngine;
using MoreMountains.Feedbacks;

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class DamageNumberPopup : MMFloatingText
{
    public const float ReactionFontSize = 18f; // 원소반응 문구 크기 축소
    public const float ReactionCharacterSpacing = 4f;
    public const float VaporizeDamageHorizontalOffsetRange = 18f;
    public const float SpawnScreenMargin = 24f;
    public const float ExitScreenMargin = 64f;

    [SerializeField] private float lifetime = 1.5f;
    [SerializeField] private float fadeStartDelay = 0.8f;
    [SerializeField] private float riseSpeed = 52f;
    [SerializeField] private Vector2 randomOffsetRange = Vector2.zero;
    [SerializeField] private TMP_FontAsset koreanFontAsset;

    private static TMP_FontAsset cachedFont;

    private RectTransform rectTransform;
    private RectTransform projectionRoot;
    private TextMeshProUGUI text;
    private Action<DamageNumberPopup> onFinished;
    private Camera targetCamera;
    private Vector3 worldPosition;
    private Vector2 randomOffset;
    private Color startColor;
    private bool initialized;
    private static readonly AnimationCurve RiseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private static readonly AnimationCurve NormalScale = new AnimationCurve(new Keyframe(0f, .72f), new Keyframe(.09f, 1.12f), new Keyframe(.22f, 1f), new Keyframe(1f, 1f));
    private static readonly AnimationCurve CriticalScale = new AnimationCurve(new Keyframe(0f, .65f), new Keyframe(.09f, 1.42f), new Keyframe(.24f, 1f), new Keyframe(1f, 1f));
    private AnimationCurve opacityCurve;

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
        if (GetTime() - _startedAt >= _lifetime || !RefreshProjectedPosition(0f, ExitScreenMargin))
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
        RefreshProjectedPosition(_newPosition.y, ExitScreenMargin);
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
            isCritical && damageColor == Color.white ? new Color(1f, .77f, .24f) : damageColor,
            position,
            isCritical ? 34f : 24f,
            finishedCallback,
            isCritical ? FontStyles.Bold : FontStyles.Normal,
            false,
            horizontalRandomOffsetRange,
            true,
            Mathf.Max(1, Mathf.RoundToInt(damage)),
            isCritical);
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
        bool isCritical = false)
    {
        ResolveText();
        if (projectionRoot == null)
            projectionRoot = transform.parent as RectTransform;

        if (targetCamera == null)
            targetCamera = Camera.main;
        onFinished = finishedCallback;
        _startedAt = GetTime();
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
            Material baseFontMaterial = ApplyFont();
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

        bool projected = RefreshProjectedPosition(0f, ExitScreenMargin);
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
        SetProperties(text.text, lifetime, Vector3.up, true,
            MMFloatingTextSpawner.AlignmentModes.Fixed, Vector3.up, false, null,
            false, null, 0f, 0f, true, RiseCurve, 0f, riseSpeed * lifetime * (isCritical ? 1.15f : 1f),
            false, null, 0f, 0f, true, opacityCurve, 0f, 1f,
            true, isCritical ? CriticalScale : NormalScale, 0f, 1f, false, null);
    }

    private bool RefreshProjectedPosition(float rise, float screenMargin)
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

        Vector2 screenRise = Vector2.up * rise;
        Vector2 finalPosition = anchoredPosition + randomOffset + screenRise;
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

    private Material ApplyFont()
    {
        TMP_FontAsset font = koreanFontAsset != null ? koreanFontAsset : ResolveImportedFont();
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
}
