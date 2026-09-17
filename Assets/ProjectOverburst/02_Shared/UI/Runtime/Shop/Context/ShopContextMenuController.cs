using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum ShopContextMenuTarget
{
    MerchantInventory,
    PlayerInventory,
    MerchantOffer,
    PlayerOffer
}

public class ShopContextMenuController : MonoBehaviour
{
    private const float MinMenuWidth = 180f;
    private const float MaxMenuWidth = 440f;
    private const float ButtonHeight = 34f;

    [Header("Objectized View")]
    [SerializeField] private RectTransform blockerRoot;
    [SerializeField] private RectTransform menuRoot;
    [SerializeField] private Button tradeButton;
    [SerializeField] private Button splitTradeButton;
    [SerializeField] private Button removeButton;
    [SerializeField] private Button infoButton;
    [SerializeField] private Button closeButton;

    [Header("Split Trade Popup")]
    [SerializeField] private RectTransform splitPopupRoot;
    [SerializeField] private TextMeshProUGUI splitTitleText;
    [SerializeField] private TextMeshProUGUI splitHintText;
    [SerializeField] private TMP_InputField splitAmountInput;
    [SerializeField] private Button splitIncreaseButton;
    [SerializeField] private Button splitDecreaseButton;
    [SerializeField] private Button splitOkButton;
    [SerializeField] private Button splitCancelButton;

    private ShopUI owner;
    private Canvas canvas;
    private RectTransform canvasRect;
    private TMP_FontAsset fontAsset;
    private TooltipManager tooltipManager;
    private SlotUI selectedSlot;
    private ShopContextMenuTarget selectedTarget;
    private Vector2 lastScreenPosition;
    private float currentMenuWidth = MinMenuWidth;
    private int splitMaxValue = 1;
    private bool controlsBound;
    private bool missingReferenceLogged;

    public void Init(ShopUI ownerShop, TMP_FontAsset ownerFontAsset)
    {
        owner = ownerShop;
        fontAsset = ownerFontAsset;
        canvas = GetComponentInParent<Canvas>(true);
        canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        tooltipManager = TooltipManager.Instance;
        ApplyFont(fontAsset);
        BindControls();
        BindBlocker();
        HideViews();
    }

    public bool Open(ShopContextMenuTarget target, SlotUI slot, PointerEventData eventData)
    {
        if (owner == null || slot == null || slot.DisplayItem == null || slot.IsLocked)
            return false;

        if (!ValidateViewReferences())
            return false;

        selectedTarget = target;
        selectedSlot = slot;
        lastScreenPosition = eventData != null ? eventData.position : Vector2.zero;

        tooltipManager = TooltipManager.Instance;
        tooltipManager?.SetSuppressed(true);
        tooltipManager?.HideNow();

        blockerRoot.gameObject.SetActive(true);
        splitPopupRoot.gameObject.SetActive(false);
        BuildRootMenu();
        PositionRect(menuRoot, lastScreenPosition, currentMenuWidth);
        return true;
    }

    public void Close()
    {
        HideViews();
        selectedSlot = null;
        tooltipManager?.SetSuppressed(false);
    }

    private void OnDisable()
    {
        Close();
    }

    private void BuildRootMenu()
    {
        if (!ValidateViewReferences())
            return;

        menuRoot.gameObject.SetActive(true);
        splitPopupRoot.gameObject.SetActive(false);

        bool offerSlot = selectedTarget == ShopContextMenuTarget.MerchantOffer
            || selectedTarget == ShopContextMenuTarget.PlayerOffer;

        SetButtonVisible(tradeButton, !offerSlot);
        SetButtonVisible(splitTradeButton, !offerSlot && owner != null && owner.CanShopContextSplitTrade(selectedTarget, selectedSlot));
        SetButtonVisible(removeButton, offerSlot);
        SetButtonVisible(infoButton, true);
        SetButtonVisible(closeButton, true);

        if (!offerSlot)
        {
            bool canTrade = owner != null && owner.CanShopContextTrade(selectedTarget, selectedSlot);
            tradeButton.interactable = canTrade;
            if (!canTrade)
            {
                string message = owner != null ? owner.GetShopContextBlockedTradeMessage(selectedTarget, selectedSlot) : string.Empty;
                if (!string.IsNullOrWhiteSpace(message))
                    owner.ShowShopContextStatus(message);
            }
        }

        RefreshResponsiveMenuWidth();
    }

    private void ShowSplitTradePopup()
    {
        if (!ValidateViewReferences())
            return;

        ItemData item = selectedSlot != null ? selectedSlot.DisplayItem : null;
        if (item == null || item.stackCount <= 1)
        {
            Close();
            return;
        }

        splitMaxValue = Mathf.Max(1, item.stackCount - 1);
        int defaultValue = Mathf.Clamp(Mathf.Max(1, item.stackCount / 2), 1, splitMaxValue);

        if (splitTitleText != null)
            splitTitleText.text = "나눠서 거래";

        if (splitHintText != null)
            splitHintText.text = "최대 " + splitMaxValue + "개";

        if (splitAmountInput != null)
        {
            splitAmountInput.characterLimit = Mathf.Max(1, splitMaxValue.ToString().Length);
            splitAmountInput.SetTextWithoutNotify(defaultValue.ToString());
        }

        menuRoot.gameObject.SetActive(false);
        splitPopupRoot.gameObject.SetActive(true);
        splitPopupRoot.SetAsLastSibling();
        PositionRect(splitPopupRoot, lastScreenPosition, splitPopupRoot.rect.width);
    }

    private void TradeSelectedSlot()
    {
        owner?.HandleShopContextTrade(selectedTarget, selectedSlot);
        Close();
    }

    private void RemoveSelectedOffer()
    {
        owner?.HandleShopContextRemoveOffer(selectedTarget, selectedSlot);
        Close();
    }

    private void ShowSelectedItemInfo()
    {
        owner?.ShowShopContextItemInfo(selectedSlot != null ? selectedSlot.DisplayItem : null);
        Close();
    }

    private void ConfirmSplitTrade()
    {
        int amount = GetSplitAmount();
        owner?.HandleShopContextSplitTrade(selectedTarget, selectedSlot, amount);
        Close();
    }

    private void ChangeSplitAmount(int delta)
    {
        if (splitAmountInput == null)
            return;

        int clamped = Mathf.Clamp(GetSplitAmount() + delta, 1, splitMaxValue);
        splitAmountInput.SetTextWithoutNotify(clamped.ToString());
    }

    private void ClampSplitInput(string value)
    {
        if (splitAmountInput == null || string.IsNullOrWhiteSpace(value))
            return;

        int parsed;
        if (!int.TryParse(value, out parsed))
            return;

        int clamped = Mathf.Clamp(parsed, 1, splitMaxValue);
        if (clamped != parsed)
            splitAmountInput.SetTextWithoutNotify(clamped.ToString());
    }

    private int GetSplitAmount()
    {
        int amount;
        if (splitAmountInput == null || !int.TryParse(splitAmountInput.text, out amount))
            amount = 1;

        return Mathf.Clamp(amount, 1, splitMaxValue);
    }

    private void BindControls()
    {
        if (controlsBound)
            return;

        if (tradeButton != null)
        {
            tradeButton.onClick.RemoveListener(TradeSelectedSlot);
            tradeButton.onClick.AddListener(TradeSelectedSlot);
            SetButtonLabel(tradeButton, "거래");
        }

        if (splitTradeButton != null)
        {
            splitTradeButton.onClick.RemoveListener(ShowSplitTradePopup);
            splitTradeButton.onClick.AddListener(ShowSplitTradePopup);
            SetButtonLabel(splitTradeButton, "나눠서 거래");
        }

        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(RemoveSelectedOffer);
            removeButton.onClick.AddListener(RemoveSelectedOffer);
            SetButtonLabel(removeButton, "빼기");
        }

        if (infoButton != null)
        {
            infoButton.onClick.RemoveListener(ShowSelectedItemInfo);
            infoButton.onClick.AddListener(ShowSelectedItemInfo);
            SetButtonLabel(infoButton, "정보");
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
            SetButtonLabel(closeButton, "닫기");
        }

        if (splitIncreaseButton != null)
        {
            splitIncreaseButton.onClick.RemoveAllListeners();
            splitIncreaseButton.onClick.AddListener(() => ChangeSplitAmount(1));
        }

        if (splitDecreaseButton != null)
        {
            splitDecreaseButton.onClick.RemoveAllListeners();
            splitDecreaseButton.onClick.AddListener(() => ChangeSplitAmount(-1));
        }

        if (splitOkButton != null)
        {
            splitOkButton.onClick.RemoveListener(ConfirmSplitTrade);
            splitOkButton.onClick.AddListener(ConfirmSplitTrade);
            SetButtonLabel(splitOkButton, "OK");
        }

        if (splitCancelButton != null)
        {
            splitCancelButton.onClick.RemoveListener(Close);
            splitCancelButton.onClick.AddListener(Close);
            SetButtonLabel(splitCancelButton, "Cancel");
        }

        if (splitAmountInput != null)
        {
            splitAmountInput.onValueChanged.RemoveListener(ClampSplitInput);
            splitAmountInput.onValueChanged.AddListener(ClampSplitInput);
        }

        controlsBound = true;
    }

    private void BindBlocker()
    {
        if (blockerRoot == null)
            return;

        ShopContextMenuBlocker blocker = blockerRoot.GetComponent<ShopContextMenuBlocker>();
        blocker?.Init(this);
    }

    private bool ValidateViewReferences()
    {
        BindControls();

        bool valid = blockerRoot != null
            && menuRoot != null
            && tradeButton != null
            && splitTradeButton != null
            && removeButton != null
            && infoButton != null
            && closeButton != null
            && splitPopupRoot != null
            && splitAmountInput != null
            && splitIncreaseButton != null
            && splitDecreaseButton != null
            && splitOkButton != null
            && splitCancelButton != null;

        if (valid)
            return true;

        if (!missingReferenceLogged)
        {
            missingReferenceLogged = true;
            Debug.LogError("[ShopContextMenuController] Shop context UI references are missing. Reconnect serialized references or run ProjectVTP/Codex/Objectizers/[위험] Objectize Shop Context UI Preserve Layout.", this);
        }

        return false;
    }

    private void HideViews()
    {
        if (blockerRoot != null)
            blockerRoot.gameObject.SetActive(false);

        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);

        if (splitPopupRoot != null)
            splitPopupRoot.gameObject.SetActive(false);
    }

    private void SetButtonVisible(Button button, bool visible)
    {
        if (button == null)
            return;

        button.gameObject.SetActive(visible);
        button.interactable = visible;
    }

    private void SetButtonLabel(Button button, string label)
    {
        if (button == null)
            return;

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
            text.text = label;
    }

    private void ApplyFont(TMP_FontAsset targetFont)
    {
        if (targetFont == null)
            return;

        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null)
                continue;

            texts[i].font = targetFont;
            texts[i].fontSharedMaterial = targetFont.material;
        }

        if (splitAmountInput != null)
        {
            TextMeshProUGUI inputText = splitAmountInput.textComponent as TextMeshProUGUI;
            if (inputText != null)
            {
                inputText.font = targetFont;
                inputText.fontSharedMaterial = targetFont.material;
            }

            TextMeshProUGUI placeholder = splitAmountInput.placeholder as TextMeshProUGUI;
            if (placeholder != null)
            {
                placeholder.font = targetFont;
                placeholder.fontSharedMaterial = targetFont.material;
            }
        }
    }

    private void RefreshResponsiveMenuWidth()
    {
        if (menuRoot == null || canvasRect == null)
            return;

        Canvas.ForceUpdateCanvases();

        float preferredWidth = MinMenuWidth;
        for (int i = 0; i < menuRoot.childCount; i++)
        {
            Transform child = menuRoot.GetChild(i);
            if (child == null || !child.gameObject.activeSelf)
                continue;

            LayoutElement layoutElement = child.GetComponent<LayoutElement>();
            if (layoutElement != null && layoutElement.preferredWidth > 0f)
                preferredWidth = Mathf.Max(preferredWidth, layoutElement.preferredWidth + 12f);

            TextMeshProUGUI text = child.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
                preferredWidth = Mathf.Max(preferredWidth, text.preferredWidth + 34f);
        }

        float canvasLimit = Mathf.Min(MaxMenuWidth, Mathf.Max(MinMenuWidth, canvasRect.rect.width - 16f));
        currentMenuWidth = Mathf.Clamp(preferredWidth, MinMenuWidth, canvasLimit);
        menuRoot.sizeDelta = new Vector2(currentMenuWidth, menuRoot.sizeDelta.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(menuRoot);
    }

    private void PositionRect(RectTransform target, Vector2 screenPosition, float width)
    {
        if (target == null)
            return;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(true);

        canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect == null)
            return;

        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
            target.anchoredPosition = ClampToCanvas(target, localPoint, width);
    }

    private Vector2 ClampToCanvas(RectTransform target, Vector2 localPoint, float width)
    {
        if (canvasRect == null || target == null)
            return localPoint;

        Canvas.ForceUpdateCanvases();
        float height = Mathf.Max(ButtonHeight, target.rect.height);
        Rect rect = canvasRect.rect;
        float x = Mathf.Clamp(localPoint.x, rect.xMin + 8f, rect.xMax - Mathf.Max(width, target.rect.width) - 8f);
        float y = Mathf.Clamp(localPoint.y, rect.yMin + height + 8f, rect.yMax - 8f);
        return new Vector2(x, y);
    }
}
