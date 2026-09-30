using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ActionSlotHudSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private ItemData tooltipItem;
    private TooltipManager flaskTooltip;
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipItem == null) return;
        if (flaskTooltip == null) flaskTooltip = TooltipManager.Instance != null ? TooltipManager.Instance : FindFirstObjectByType<TooltipManager>();
        flaskTooltip?.ShowTooltip(tooltipItem);
    }
    public void OnPointerExit(PointerEventData eventData) { flaskTooltip?.HideTooltip(); }
    private void OnDisable() { flaskTooltip?.HideTooltip(); EndReadyFlash(); }

    // 2026-09-30: 쿨다운이 끝나 다시 쓸 수 있게 된 순간 슬롯을 한 번 번쩍이고 작은 소리를 낸다.
    private const float ReadyFlashSeconds = .38f;
    private static readonly Color ReadyFlashColor = new Color(1f, .9f, .55f, 1f);
    private Image readyFlash;
    private object cooldownOwner;
    private float lastCooldownRemaining;
    private float readyFlashStartedAt = -1f;

    private void Update()
    {
        if (readyFlashStartedAt < 0f) return;
        float t = (Time.unscaledTime - readyFlashStartedAt) / ReadyFlashSeconds;
        if (t >= 1f) { EndReadyFlash(); return; }
        float fade = (1f - t) * (1f - t);
        if (readyFlash != null) { Color c = ReadyFlashColor; c.a = .85f * fade; readyFlash.color = c; }
        if (itemIcon != null) itemIcon.rectTransform.localScale = Vector3.one * (1f + .18f * fade);
    }
    private static readonly Color KeyTextColor = new Color(0.9f, 0.94f, 1f, 0.95f);
    private static readonly Color ActiveKeyTextColor = new Color(0.36f, 0.82f, 1f, 1f);
    // 2026-10-01: 쿨다운 가림막 기본값. 빌더가 프리팹에 같은 값을 저장하고 런타임도 같은 함수로 되돌린다.
    public static readonly Color CooldownOverlayColor = new Color(0f, 0f, 0f, 0.58f);
    public const float CooldownOverlayInset = 6f;
    // 2026-10-01: 슬롯 배경은 프리팹에 저장된 색을 그대로 쓴다(예전에는 런타임에 흰색으로 덮었다).
    private Color authoredSlotBackgroundColor = Color.white;
    private bool hasAuthoredSlotBackgroundColor;
    private const float KeyTextFontSize = 14f;
    private const float ActiveKeyTextFontSize = 18f;

    [SerializeField] private Image slotBackground;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI keyText;
    [SerializeField] private Text legacyKeyText;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private TextMeshProUGUI cooldownText;
    [SerializeField] private Image cooldownOverlay;
    [SerializeField] private GameObject activeBorder;
    [SerializeField] private SlotGradeEffect gradeEffect;
    [SerializeField] private bool useVendorSlotVisual;

    private BaseItemData displayedBaseData;
    private IQuickSlotSkill displayedSkill;
    private ItemGrade displayedGrade;
    private int displayedCount;
    private bool displayedActive;
    private bool displayedShowCount;
    private bool hasDisplayedItem;
    private bool hasDisplayedGrade;
    private bool hasDisplayedState;

    private void Awake()
    {
        BindVisuals();
        if (GetComponentInParent<OverburstUIWorkshop>(true) == null)
            SetEmpty(false);
    }

    public void SetKeyNumber(int keyNumber)
    {
        BindVisuals();
        string number = keyNumber > 0 ? (keyNumber % 10).ToString() : string.Empty;
        if (keyText != null)
        {
            keyText.text = number;
            NormalizeKeyTextRect();
            RefreshKeyVisual(displayedActive);
        }
        if (legacyKeyText != null)
        {
            legacyKeyText.text = number;
            legacyKeyText.raycastTarget = false;
            RefreshKeyVisual(displayedActive);
        }
    }

    public void SetItem(ItemData item, int count, bool active, bool showCount)
    {
        BindVisuals();

        if (item == null || !item.HasValidBaseData)
        {
            SetEmpty(active);
            return;
        }

        ApplyItemVisual(item.baseData, item.grade, count, active, showCount);
    }

    public void SetConsumable(ConsumableItemData consumableData, ItemData displayItem, int count)
    {
        tooltipItem = displayItem;
        BindVisuals();
        if (slotBackground != null) slotBackground.raycastTarget = displayItem != null;

        if (consumableData == null)
        {
            SetEmpty(false);
            return;
        }

        ItemGrade grade = displayItem != null ? displayItem.grade : ItemGrade.Common;
        ApplyItemVisual(consumableData, grade, count, false, !consumableData.IsPermanentSingleItem);
    }

    public void SetFlask(ItemData item, float remaining, float cooldownRemaining, bool matchesWeapon)
    {
        BindVisuals();
        if (slotBackground != null) slotBackground.raycastTarget = item != null;
        if (item == null) { SetEmpty(false); SetCooldown(0f); return; }
        var stats = FlaskRuntime.Stats(item);
        SetItem(item, 0, remaining > 0f, false);
        tooltipItem = item;
        SetCooldown(cooldownRemaining);
        if (cooldownText != null)
        {
            // Show either active time or individual cooldown in the slot.
            RectTransform durationRect = cooldownText.rectTransform;
            durationRect.anchorMin = useVendorSlotVisual ? Vector2.zero : new Vector2(0f, 1f);
            durationRect.anchorMax = Vector2.one;
            durationRect.pivot = useVendorSlotVisual ? new Vector2(.5f, .5f) : new Vector2(.5f, 1f);
            durationRect.offsetMin = useVendorSlotVisual ? new Vector2(8f, 8f) : new Vector2(4f, -24f);
            durationRect.offsetMax = useVendorSlotVisual ? new Vector2(-8f, -8f) : new Vector2(-4f, -4f);
            cooldownText.alignment = TextAlignmentOptions.Center;
            cooldownText.fontSize = useVendorSlotVisual ? 28f : 14f;
        }
        if (countText != null) countText.gameObject.SetActive(false);
        if (itemIcon != null) itemIcon.color = matchesWeapon && (cooldownRemaining <= 0f || remaining > 0f) ? Color.white : new Color(.4f,.4f,.4f,1f);
        if (cooldownOverlay != null && remaining > 0f)
        {
            cooldownOverlay.gameObject.SetActive(true);
            // 효과가 끝나기 3초 전부터 버프 아이콘과 같은 규칙으로 깜빡인다.
            float blink = BuffIconSlotUI.ExpireBlinkAlpha(remaining, stats.duration);
            cooldownOverlay.color = new Color(.12f,.6f,.42f,.27f * blink);
            cooldownOverlay.type = Image.Type.Filled;
            cooldownOverlay.fillMethod = Image.FillMethod.Vertical;
            cooldownOverlay.fillOrigin = 0;
            cooldownOverlay.fillAmount = Mathf.Clamp01(remaining / Mathf.Max(.1f, stats.duration));
        }
        if (cooldownText != null && remaining > 0f)
        {
            cooldownText.gameObject.SetActive(true);
            cooldownText.text = FormatCooldownText(remaining);
            cooldownText.alpha = BuffIconSlotUI.ExpireBlinkAlpha(remaining, stats.duration);
        }
    }

    public void SetSkill(IQuickSlotSkill skill)
    {
        tooltipItem = null;
        BindVisuals();
        if (skill == null) { SetEmpty(false); SetCooldown(0f); return; }
        if (ReferenceEquals(displayedSkill, skill)) { SetCooldown(skill.CooldownRemaining); return; }
        if (slotBackground != null) slotBackground.raycastTarget = false;
        if (itemIcon != null)
        {
            itemIcon.gameObject.SetActive(true);
            itemIcon.sprite = skill.Icon;
            itemIcon.color = skill.Icon != null ? Color.white : new Color(.38f, .7f, 1f, .6f);
            itemIcon.preserveAspect = true;
        }
        if (countText != null) countText.gameObject.SetActive(false);
        gradeEffect?.Clear();
        SetActiveBorder(false);
        ResetSlotBackgroundColor();
        RefreshKeyVisual(false);
        displayedBaseData = null;
        displayedSkill = skill;
        displayedActive = false;
        hasDisplayedItem = true;
        hasDisplayedGrade = false;
        hasDisplayedState = true;
        SetCooldown(skill.CooldownRemaining);
    }

    public void SetCooldown(float remainingSeconds)
    {
        BindVisuals();

        float remaining = Mathf.Max(0f, remainingSeconds);
        // 같은 물건의 쿨다운이 0으로 떨어진 프레임만 "다시 사용 가능"으로 본다(비우기·교체는 제외).
        object owner = hasDisplayedItem ? (displayedSkill != null ? (object)displayedSkill : displayedBaseData) : null;
        bool becameReady = owner != null && ReferenceEquals(owner, cooldownOwner)
            && lastCooldownRemaining > 0f && remaining <= 0f;
        cooldownOwner = owner;
        lastCooldownRemaining = remaining;
        if (becameReady) PlayReadyFlash();

        if (cooldownOverlay == null)
            return;

        bool active = remaining > 0f;
        ApplyCooldownOverlayDefaults(cooldownOverlay);
        cooldownOverlay.gameObject.SetActive(active);
        cooldownOverlay.fillAmount = 1f;
        cooldownOverlay.transform.SetAsLastSibling();

        if (countText != null)
            countText.transform.SetAsLastSibling();
        if (keyText != null)
            keyText.transform.SetAsLastSibling();
        if (legacyKeyText != null && legacyKeyText.transform.parent != null)
            legacyKeyText.transform.parent.SetAsLastSibling();
        if (cooldownText != null)
        {
            cooldownText.gameObject.SetActive(active);
            cooldownText.text = active ? FormatCooldownText(remaining) : string.Empty;
            cooldownText.alpha = 1f;
            cooldownText.transform.SetAsLastSibling();
        }
        if (activeBorder != null && activeBorder.activeSelf)
            activeBorder.transform.SetAsLastSibling();
    }

    public void SetEmpty(bool active)
    {
        tooltipItem = null;
        BindVisuals();

        if (hasDisplayedState && !hasDisplayedItem && displayedActive == active)
        {
            SetActiveBorder(active);
            ResetSlotBackgroundColor();
            RefreshKeyVisual(active);
            return;
        }

        hasDisplayedState = true;
        hasDisplayedItem = false;
        hasDisplayedGrade = false;
        displayedBaseData = null;
        displayedSkill = null;
        displayedCount = 0;
        displayedActive = active;
        displayedShowCount = false;

        if (itemIcon != null)
        {
            itemIcon.gameObject.SetActive(false);
            itemIcon.sprite = null;
            itemIcon.color = Color.clear;
        }
        GetComponent<OverburstUISlotGradePreview>()?.Refresh();

        if (countText != null)
        {
            countText.gameObject.SetActive(false);
            countText.text = string.Empty;
        }

        if (cooldownOverlay != null)
        {
            cooldownOverlay.fillAmount = 0f;
            cooldownOverlay.gameObject.SetActive(false);
        }

        if (cooldownText != null)
        {
            cooldownText.gameObject.SetActive(false);
            cooldownText.text = string.Empty;
        }

        gradeEffect?.Clear();
        SetActiveBorder(active);
        ResetSlotBackgroundColor();
        RefreshKeyVisual(active);
    }

    private void ApplyItemVisual(BaseItemData baseData, ItemGrade grade, int count, bool active, bool showCount)
    {
        if (baseData == null)
        {
            SetEmpty(active);
            return;
        }

        displayedSkill = null;

        bool itemChanged = !hasDisplayedItem || displayedBaseData != baseData;
        bool gradeChanged = itemChanged || !hasDisplayedGrade || displayedGrade != grade;
        bool countChanged = itemChanged || displayedCount != count || displayedShowCount != showCount;
        bool activeChanged = itemChanged || displayedActive != active;

        if (itemIcon != null)
        {
            itemIcon.gameObject.SetActive(true);
            if (itemChanged)
            {
                itemIcon.sprite = baseData.icon;
                itemIcon.color = baseData.icon != null ? Color.white : new Color(baseData.color.r, baseData.color.g, baseData.color.b, 0.55f);
            }
            itemIcon.preserveAspect = true;
        }

        if (countText != null && countChanged)
        {
            bool countVisible = showCount;
            countText.gameObject.SetActive(countVisible);
            countText.text = countVisible ? "x" + Mathf.Max(0, count) : string.Empty;
        }

        ResetSlotBackgroundColor();

        if (gradeChanged)
        {
            gradeEffect?.SetGrade(grade, GradeConfig.GetGradeColor(grade));
            GetComponent<OverburstUISlotGradePreview>()?.Present(grade);
        }

        if (activeChanged)
            SetActiveBorder(active);
        RefreshKeyVisual(active);

        displayedBaseData = baseData;
        displayedGrade = grade;
        displayedCount = count;
        displayedActive = active;
        displayedShowCount = showCount;
        hasDisplayedState = true;
        hasDisplayedItem = true;
        hasDisplayedGrade = true;

        if (countText != null && countText.gameObject.activeSelf)
            countText.transform.SetAsLastSibling();
        if (keyText != null)
            keyText.transform.SetAsLastSibling();
        if (activeBorder != null && activeBorder.activeSelf)
            activeBorder.transform.SetAsLastSibling();
    }

    private void BindVisuals()
    {
        if (slotBackground == null)
            slotBackground = FindChildImage("SlotBackground") ?? GetComponent<Image>();
        if (slotBackground != null && !hasAuthoredSlotBackgroundColor)
        {
            authoredSlotBackgroundColor = slotBackground.color;
            hasAuthoredSlotBackgroundColor = true;
        }

        if (itemIcon == null)
            itemIcon = FindChildImage("ItemIcon");

        if (keyText == null)
            keyText = FindChildText("KeyText");
        NormalizeKeyTextRect();

        if (countText == null)
            countText = FindChildText("CountText");

        if (cooldownOverlay == null)
            cooldownOverlay = FindChildImage("CooldownOverlay");

        if (cooldownText == null)
            cooldownText = FindChildText("CooldownText") ?? CreateCooldownText();

        if (activeBorder == null)
        {
            Transform border = transform.Find("ActiveBorder");
            activeBorder = border != null ? border.gameObject : null;
        }

        if (gradeEffect == null)
            gradeEffect = GetComponent<SlotGradeEffect>();

        if (gradeEffect != null)
            gradeEffect.Init(itemIcon, slotBackground);

    }

    private Image FindChildImage(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<Image>() : null;
    }

    private TextMeshProUGUI FindChildText(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private void SetActiveBorder(bool active)
    {
        if (activeBorder != null)
            activeBorder.SetActive(false);
    }

    private void ResetSlotBackgroundColor()
    {
        if (slotBackground != null && hasAuthoredSlotBackgroundColor)
            slotBackground.color = authoredSlotBackgroundColor;
    }

    private void NormalizeKeyTextRect()
    {
        if (keyText == null)
            return;

        RectTransform rect = keyText.rectTransform;
        if (rect != null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 2f);
            rect.offsetMax = new Vector2(-5f, -3f);
        }

        keyText.alignment = TextAlignmentOptions.BottomRight;
        keyText.textWrappingMode = TextWrappingModes.NoWrap;
        keyText.raycastTarget = false;
    }

    private void RefreshKeyVisual(bool active)
    {
        if (keyText != null)
        {
            keyText.fontStyle = FontStyles.Bold;
            keyText.fontSize = active ? ActiveKeyTextFontSize : KeyTextFontSize;
            keyText.color = active ? ActiveKeyTextColor : KeyTextColor;
        }
        if (legacyKeyText != null)
            legacyKeyText.color = active ? ActiveKeyTextColor : Color.white;
    }

    /// <summary>쿨다운 가림막의 기본 모양(색·채움 방식·클릭 통과·칸 안쪽 여백). 빌더와 런타임이 함께 쓴다.</summary>
    public static void ApplyCooldownOverlayDefaults(Image overlay)
    {
        if (overlay == null)
            return;

        overlay.color = CooldownOverlayColor;
        overlay.type = Image.Type.Simple;
        overlay.raycastTarget = false;

        RectTransform rect = overlay.rectTransform;
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(CooldownOverlayInset, CooldownOverlayInset);
        rect.offsetMax = new Vector2(-CooldownOverlayInset, -CooldownOverlayInset);
    }

    private TextMeshProUGUI CreateCooldownText()
    {
        GameObject textObject = new GameObject("CooldownText");
        textObject.transform.SetParent(transform, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(4f, 2f);
        rect.offsetMax = new Vector2(-4f, -3f);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.BottomRight;
        text.fontSize = 13f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.gameObject.SetActive(false);
        return text;
    }

    private string FormatCooldownText(float remaining)
    {
        if (remaining >= 10f)
            return Mathf.CeilToInt(remaining).ToString() + "s";

        return remaining.ToString("0.0") + "s";
    }

    private void PlayReadyFlash()
    {
        if (!isActiveAndEnabled) return;
        if (readyFlash == null) readyFlash = CreateReadyFlash();
        readyFlash.gameObject.SetActive(true);
        readyFlash.transform.SetAsLastSibling();
        readyFlashStartedAt = Time.unscaledTime;
        CombatActionSfxService.PlayQuickSlotReady();
    }

    private void EndReadyFlash()
    {
        readyFlashStartedAt = -1f;
        if (readyFlash != null) readyFlash.gameObject.SetActive(false);
        if (itemIcon != null) itemIcon.rectTransform.localScale = Vector3.one;
    }

    private Image CreateReadyFlash()
    {
        var flashObject = new GameObject("ReadyFlash", typeof(RectTransform));
        flashObject.transform.SetParent(transform, false);
        var rect = (RectTransform)flashObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(4f, 4f);
        rect.offsetMax = new Vector2(-4f, -4f);
        var image = flashObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.clear;
        flashObject.SetActive(false);
        return image;
    }

}
