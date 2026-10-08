using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public sealed class BuffIconSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image baseImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private BuffPolarityIndicatorUI polarityIndicator;
    [SerializeField] private UnityEngine.UI.Image hoverTarget;
    [SerializeField] private BuffTooltipUI tooltip;
    private object tooltipSource;
    private float tooltipPrimary, tooltipSecondary;
    private int tooltipStacks, tooltipMaximum;
    public string TooltipName { get; private set; }
    public string TooltipEffect { get; private set; }
    public float TooltipRemaining { get; private set; }
    public bool TooltipPermanent { get; private set; }
    public string TooltipLifetime { get; private set; }

    private static Sprite defaultArrowSprite;
    private int lastNumber = int.MinValue;
    public string DisplayedKey { get; private set; }
    public Sprite DisplayedSprite => fillImage != null && fillImage.enabled ? fillImage.sprite : null;
    public bool DisplayedIsDebuff { get; private set; }

    private void Awake()
    {
        BindVisuals();
        SetVisible(false);
    }

    public void SetBuff(BuffInstance instance)
    {
        BindVisuals();

        if (instance == null || instance.Definition == null)
        {
            SetVisible(false);
            return;
        }

        SetEffect(instance.BuffId, StatusBuffIcons.Buff(instance.Definition) ?? GetDefaultArrowSprite(), instance.RemainingTime,
            instance.Definition.duration, isDebuff: instance.Definition.isDebuff);
        if (tooltipSource != instance.Definition)
        {
            tooltipSource = instance.Definition;
            TooltipName = BuffTooltipText.BuffName(instance.Definition);
            TooltipEffect = BuffTooltipText.BuffEffect(instance.Definition);
        }
    }

    public void SetFlask(string key, FlaskEffectSnapshot snapshot)
    {
        SetEffect(key, StatusBuffIcons.Flask(snapshot.Data), snapshot.Remaining, snapshot.Duration);
        if (tooltipSource != snapshot.Data || tooltipPrimary != snapshot.Stats.primary || tooltipSecondary != snapshot.Stats.secondary)
        {
            tooltipSource = snapshot.Data; tooltipPrimary = snapshot.Stats.primary; tooltipSecondary = snapshot.Stats.secondary;
            TooltipName = snapshot.Data.itemName;
            TooltipEffect = BuffTooltipText.FlaskEffectText(snapshot);
        }
    }

    public void SetRadiance(int stacks, int maximum)
    {
        SetEffect("radiance", StatusBuffIcons.Status("radiance"), stacks: stacks, permanent: true);
        TooltipLifetime = "강공 시 소모";
        if (TooltipName == null || tooltipStacks != stacks || tooltipMaximum != maximum)
        {
            tooltipStacks = stacks; tooltipMaximum = maximum; TooltipName = "광휘";
            TooltipEffect = "빛 강공 강화 · " + BuffTooltipText.Highlight(stacks + " / " + maximum + "중첩");
        }
    }

    public void SetMapBuff(string key, MapBuffKind kind, int stacks, float bonus)
    {
        SetEffect(key, StatusBuffIcons.Map(kind), stacks: stacks, permanent: true);
        TooltipLifetime = "던전 종료까지";
        if (TooltipName == null || tooltipStacks != stacks || tooltipPrimary != bonus)
        {
            tooltipStacks = stacks; tooltipPrimary = bonus;
            TooltipName = BuffTooltipText.MapName(kind);
            TooltipEffect = BuffTooltipText.MapEffect(kind, stacks, bonus);
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => tooltip?.Show(this);
    public void OnPointerExit(PointerEventData eventData) => tooltip?.Hide(this);
    private void OnDisable() => tooltip?.Hide(this);

    public void SetEffect(string key, Sprite sprite, float remaining = 0f, float duration = 0f,
        int stacks = 0, bool permanent = false, bool isDebuff = false)
    {
        BindVisuals();
        if (sprite == null) { SetVisible(false); return; }
        if (DisplayedKey != key) { tooltipSource = null; TooltipName = TooltipEffect = null; }
        DisplayedKey = key;
        DisplayedIsDebuff = isDebuff;
        TooltipRemaining = remaining;
        TooltipPermanent = permanent;
        TooltipLifetime = "지속 중";
        float alpha = permanent ? 1f : ExpireBlinkAlpha(remaining, duration);
        if (polarityIndicator != null) polarityIndicator.SetAppearance(isDebuff, alpha);
        ConfigureImage(baseImage, sprite, new Color(1f, 1f, 1f, .22f * alpha), Image.Type.Simple);
        ConfigureImage(fillImage, sprite, new Color(1f, 1f, 1f, alpha), permanent ? Image.Type.Simple : Image.Type.Filled);
        if (fillImage != null)
        {
            fillImage.fillMethod = Image.FillMethod.Radial360;
            fillImage.fillOrigin = (int)Image.Origin360.Top;
            fillImage.fillClockwise = true;
            fillImage.fillAmount = permanent ? 1f : Mathf.Clamp01(remaining / Mathf.Max(.01f, duration));
        }
        if (baseImage != null) baseImage.rectTransform.localEulerAngles = Vector3.zero;
        if (fillImage != null) fillImage.rectTransform.localEulerAngles = Vector3.zero;
        int number = stacks > 1 ? stacks : permanent ? 0 : Mathf.CeilToInt(remaining);
        if (valueText != null)
        {
            if (lastNumber != number) { valueText.text = number > 0 ? number.ToString() : string.Empty; lastNumber = number; }
            valueText.alpha = alpha;
        }
        SetVisible(true);
    }

    public void SetVisible(bool visible)
    {
        if (baseImage != null)
            baseImage.enabled = visible;
        if (fillImage != null)
            fillImage.enabled = visible;
        if (valueText != null) valueText.enabled = visible;
        if (polarityIndicator != null) polarityIndicator.enabled = visible;
        if (hoverTarget != null) hoverTarget.enabled = visible;
        if (!visible)
        {
            tooltip?.Hide(this);
            DisplayedKey = null; DisplayedIsDebuff = false;
            tooltipSource = null; TooltipName = TooltipEffect = null;
        }
    }

    // 2026-09-30: 끝나기 3초 전부터 깜빡이고, 마지막 1초는 더 빠르게 깜빡여 곧 사라진다는 것을 알린다.
    public const float ExpireWarningSeconds = 3f;

    public static float ExpireBlinkAlpha(float remaining, float duration)
    {
        if (duration <= ExpireWarningSeconds || remaining > ExpireWarningSeconds || remaining <= 0f)
            return 1f;
        float hz = remaining > 1f ? 2.5f : 5f;
        return .3f + .7f * Mathf.Abs(Mathf.Cos(Time.unscaledTime * Mathf.PI * hz));
    }

    private void BindVisuals()
    {
        if (baseImage == null)
            baseImage = transform.Find("Base") != null ? transform.Find("Base").GetComponent<Image>() : null;

        if (fillImage == null)
            fillImage = transform.Find("Fill") != null ? transform.Find("Fill").GetComponent<Image>() : null;
        if (valueText == null)
            valueText = transform.Find("Value") != null ? transform.Find("Value").GetComponent<TMP_Text>() : null;
        if (polarityIndicator == null)
            polarityIndicator = transform.Find("Polarity")?.GetComponent<BuffPolarityIndicatorUI>();
        if (hoverTarget == null) hoverTarget = GetComponent<UnityEngine.UI.Image>();
    }

    private static void ConfigureImage(Image image, Sprite sprite, Color color, Image.Type type)
    {
        if (image == null)
            return;

        image.sprite = sprite;
        image.color = color;
        image.type = type;
        image.raycastTarget = false;
        image.preserveAspect = true;
    }

    private static Sprite GetDefaultArrowSprite()
    {
        if (defaultArrowSprite != null)
            return defaultArrowSprite;

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "BuffDefaultArrowSprite"
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size;
                float ny = (y + 0.5f) / size;
                bool shaft = nx >= 0.36f && nx <= 0.64f && ny <= 0.62f;
                bool head = ny > 0.42f && Mathf.Abs(nx - 0.5f) <= (ny - 0.42f) * 0.85f;
                texture.SetPixel(x, y, shaft || head ? Color.white : Color.clear);
            }
        }

        texture.Apply();
        defaultArrowSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        defaultArrowSprite.name = "BuffDefaultArrowSprite";
        return defaultArrowSprite;
    }
}
