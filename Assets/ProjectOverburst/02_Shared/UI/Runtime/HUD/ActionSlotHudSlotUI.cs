using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ActionSlotHudSlotUI : MonoBehaviour
{
    private static readonly Color SlotBackgroundColor = Color.white;
    private static readonly Color KeyTextColor = new Color(0.9f, 0.94f, 1f, 0.95f);
    private static readonly Color ActiveKeyTextColor = new Color(0.36f, 0.82f, 1f, 1f);
    private static readonly Color CooldownOverlayColor = new Color(0f, 0f, 0f, 0.58f);
    private const float KeyTextFontSize = 14f;
    private const float ActiveKeyTextFontSize = 18f;

    [SerializeField] private Image slotBackground;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI keyText;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private TextMeshProUGUI cooldownText;
    [SerializeField] private Image cooldownOverlay;
    [SerializeField] private GameObject activeBorder;
    [SerializeField] private SlotGradeEffect gradeEffect;

    private BaseItemData displayedBaseData;
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
        SetEmpty(false);
    }

    public void SetKeyNumber(int keyNumber)
    {
        BindVisuals();
        if (keyText != null)
        {
            keyText.text = keyNumber > 0 ? keyNumber.ToString() : string.Empty;
            NormalizeKeyTextRect();
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
        BindVisuals();

        if (consumableData == null)
        {
            SetEmpty(false);
            return;
        }

        ItemGrade grade = displayItem != null ? displayItem.grade : ItemGrade.Common;
        ApplyItemVisual(consumableData, grade, count, false, !consumableData.IsPermanentSingleItem);
    }

    public void SetCooldown(float remainingSeconds)
    {
        BindVisuals();

        if (cooldownOverlay == null)
            return;

        float remaining = Mathf.Max(0f, remainingSeconds);
        bool active = remaining > 0f;
        EnsureCooldownOverlayRect();
        cooldownOverlay.color = CooldownOverlayColor;
        cooldownOverlay.type = Image.Type.Simple;
        cooldownOverlay.raycastTarget = false;
        cooldownOverlay.gameObject.SetActive(active);
        cooldownOverlay.fillAmount = 1f;
        cooldownOverlay.transform.SetAsLastSibling();

        if (countText != null)
            countText.transform.SetAsLastSibling();
        if (keyText != null)
            keyText.transform.SetAsLastSibling();
        if (cooldownText != null)
        {
            cooldownText.gameObject.SetActive(active);
            cooldownText.text = active ? FormatCooldownText(remaining) : string.Empty;
            cooldownText.transform.SetAsLastSibling();
        }
        if (activeBorder != null && activeBorder.activeSelf)
            activeBorder.transform.SetAsLastSibling();
    }

    public void SetEmpty(bool active)
    {
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
        displayedCount = 0;
        displayedActive = active;
        displayedShowCount = false;

        if (itemIcon != null)
        {
            itemIcon.gameObject.SetActive(false);
            itemIcon.sprite = null;
            itemIcon.color = Color.clear;
        }

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
            gradeEffect?.SetGrade(grade, GradeConfig.GetGradeColor(grade));

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
        if (slotBackground != null)
            slotBackground.color = SlotBackgroundColor;
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
        if (keyText == null)
            return;

        keyText.fontStyle = FontStyles.Bold;
        keyText.fontSize = active ? ActiveKeyTextFontSize : KeyTextFontSize;
        keyText.color = active ? ActiveKeyTextColor : KeyTextColor;
    }

    private void EnsureCooldownOverlayRect()
    {
        if (cooldownOverlay == null)
            return;

        RectTransform rect = cooldownOverlay.rectTransform;
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(6f, 6f);
        rect.offsetMax = new Vector2(-6f, -6f);
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

}
