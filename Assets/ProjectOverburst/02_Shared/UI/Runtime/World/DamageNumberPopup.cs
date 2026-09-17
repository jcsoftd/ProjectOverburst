using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class DamageNumberPopup : MonoBehaviour
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
    private float spawnTime;

    private void Awake()
    {
        rectTransform = transform as RectTransform;
        ResolveText();
    }

    private void Update()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        float age = Time.time - spawnTime;
        float fadeDuration = Mathf.Max(0.0001f, lifetime - fadeStartDelay);
        float fadeProgress = age > fadeStartDelay ? Mathf.Clamp01((age - fadeStartDelay) / fadeDuration) : 0f;
        bool projected = RefreshProjectedPosition(age, ExitScreenMargin);

        if (!projected)
        {
            CompleteAndReturn();
            return;
        }

        if (text != null)
        {
            Color color = startColor;
            color.a = Mathf.Lerp(startColor.a, 0f, fadeProgress);
            text.color = color;
        }

        if (age >= lifetime)
        {
            CompleteAndReturn();
        }
    }

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
            damageColor,
            position,
            isCritical ? 27f : 24f,
            finishedCallback,
            isCritical ? FontStyles.Bold : FontStyles.Normal,
            false,
            horizontalRandomOffsetRange,
            true,
            Mathf.Max(1, Mathf.RoundToInt(damage)));
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
        int integerValue)
    {
        ResolveText();
        if (projectionRoot == null)
            projectionRoot = transform.parent as RectTransform;

        if (targetCamera == null)
            targetCamera = Camera.main;
        onFinished = finishedCallback;
        spawnTime = Time.time;
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
                text.SetText("{0:0}", integerValue);
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

        gameObject.SetActive(true);
        if (!projected)
            CompleteAndReturn();
    }

    private bool RefreshProjectedPosition(float age, float screenMargin)
    {
        bool projected = WorldUiScreenProjection.TryProject(
            projectionRoot,
            targetCamera,
            worldPosition,
            out Vector2 anchoredPosition,
            screenMargin);
        if (rectTransform == null || !projected)
            return projected;

        Vector2 screenRise = Vector2.up * riseSpeed * Mathf.Max(0f, age);
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
        spawnTime = 0f;

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
