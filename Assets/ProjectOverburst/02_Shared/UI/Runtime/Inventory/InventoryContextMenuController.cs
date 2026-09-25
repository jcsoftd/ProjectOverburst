using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventoryContextMenuController : MonoBehaviour
{
    private const float MinMenuWidth = 240f;
    private const float MaxMenuWidth = 440f;
    private const float ButtonHeight = 38f;

    [Header("Objectized View")]
    [SerializeField] private RectTransform blockerRoot;
    [SerializeField] private RectTransform menuRoot;
    [SerializeField] private Button[] menuButtons;
    [SerializeField] private TextMeshProUGUI[] menuButtonTexts;

    [Header("Objectized Split Popup")]
    [SerializeField] private RectTransform splitPopupRoot;
    [SerializeField] private TextMeshProUGUI splitTitleText;
    [SerializeField] private TextMeshProUGUI splitHintText;
    [SerializeField] private TMP_InputField splitAmountInput;
    [SerializeField] private Button splitIncreaseButton;
    [SerializeField] private Button splitDecreaseButton;
    [SerializeField] private Button splitOkButton;
    [SerializeField] private Button splitCancelButton;

    private Canvas canvas;
    private RectTransform canvasRect;
    private TooltipManager tooltipManager;
    private InventorySelectionController selectionController;
    private InventoryItemActionService actionService;
    private InventoryQuickSlotBindingController quickSlots;
    private TMP_FontAsset fontAsset;
    private SlotUI selectedSlot;
    private int selectedWeaponSlotIndex = -1;
    private Vector2 lastScreenPosition;
    private float currentMenuWidth = MinMenuWidth;
    private int objectizedButtonIndex;
    private bool warnedMissingObjectizedMenu;
    private bool warnedMissingObjectizedSplit;

    public bool IsOpen => blockerRoot != null && blockerRoot.gameObject.activeSelf;

    public void Init(
        Canvas ownerCanvas,
        TooltipManager ownerTooltipManager,
        InventorySelectionController ownerSelectionController,
        InventoryItemActionService ownerActionService,
        InventoryQuickSlotBindingController ownerQuickSlots,
        TMP_FontAsset ownerFontAsset)
    {
        canvas = ownerCanvas;
        tooltipManager = ownerTooltipManager;
        selectionController = ownerSelectionController;
        actionService = ownerActionService;
        quickSlots = ownerQuickSlots;
        fontAsset = ownerFontAsset;
        canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        BindObjectizedControls();
        HideObjectizedViews();
    }

    public void OpenForSlot(SlotUI slot, PointerEventData eventData)
    {
        if (slot == null || slot.IsLocked)
            return;

        if (slot.IsWeaponSlot)
        {
            OpenForWeaponSlotInternal(slot.SlotIndex, eventData, slot);
            return;
        }

        if (slot.DisplayItem == null)
            return;

        EnsureView();
        if (blockerRoot == null || menuRoot == null)
            return;

        selectedSlot = slot;
        selectedWeaponSlotIndex = -1;
        lastScreenPosition = eventData != null ? eventData.position : Vector2.zero;
        selectionController?.Select(slot);
        tooltipManager?.SetSuppressed(true);
        tooltipManager?.HideNow();

        blockerRoot.gameObject.SetActive(true);
        blockerRoot.SetAsLastSibling();
        BuildRootMenu(slot.DisplayItem);
        RefreshResponsiveMenuWidth();
        PositionMenu(lastScreenPosition);
    }

    public void OpenForWeaponSlot(int weaponSlotIndex, PointerEventData eventData)
    {
        OpenForWeaponSlotInternal(weaponSlotIndex, eventData, null);
    }

    public void Close()
    {
        if (blockerRoot != null)
            blockerRoot.gameObject.SetActive(false);

        if (splitPopupRoot != null)
            splitPopupRoot.gameObject.SetActive(false);

        ClearMenu();
        selectedSlot = null;
        selectedWeaponSlotIndex = -1;
        selectionController?.Clear();
        tooltipManager?.SetSuppressed(false);
    }

    private void OpenForWeaponSlotInternal(int weaponSlotIndex, PointerEventData eventData, SlotUI slot)
    {
        ItemData weapon = actionService != null ? actionService.GetWeaponSlotItem(weaponSlotIndex) : null;
        if (weapon == null)
            return;

        EnsureView();
        if (blockerRoot == null || menuRoot == null)
            return;

        selectedSlot = slot;
        selectedWeaponSlotIndex = weaponSlotIndex;
        lastScreenPosition = eventData != null ? eventData.position : Vector2.zero;

        if (slot != null)
            selectionController?.Select(slot);
        else
            selectionController?.Clear();

        tooltipManager?.SetSuppressed(true);
        tooltipManager?.HideNow();

        blockerRoot.gameObject.SetActive(true);
        blockerRoot.SetAsLastSibling();
        BuildWeaponSlotMenu();
        RefreshResponsiveMenuWidth();
        PositionMenu(lastScreenPosition);
    }

    private void OnDisable()
    {
        Close();
    }

    private void BuildRootMenu(ItemData item)
    {
        ClearMenu();
        ShowRootMenu();

        if (item == null)
            return;

        if (selectedSlot != null && selectedSlot.OwnerBridge is StashSlotBridge)
        {
            AddButton("정보", true, ShowSelectedItemInfo);
            AddSplitButtonIfAvailable(item);
            AddButton("닫기", true, Close);
            return;
        }

        if (selectedSlot != null && selectedSlot.IsBagSlot)
        {
            AddButton("장착 해제", true, UnequipSelectedBagSlot);
            AddButton("정보", true, ShowSelectedItemInfo);
            AddButton("닫기", true, Close);
            return;
        }

        if (item.baseData is FlaskItemData)
        {
            var flasks = PlayerFlaskController.Current;
            bool equipped = false;
            for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
                if (flasks != null && flasks.GetItem(i) == item)
                {
                    equipped = true;
                    break;
                }
            if (equipped)
            {
                AddButton("퀵슬롯 번호 변경", PlayerFlaskController.CanChangeLoadout, ShowQuickSlotSubmenu);
                AddButton("장착 해제", PlayerFlaskController.CanChangeLoadout, () => { actionService?.UnequipFlask(item); Close(); });
            }
            else
                AddButton("장착", PlayerFlaskController.CanChangeLoadout, () =>
                {
                    if (actionService != null && actionService.EquipFlask(item)) Close();
                    else ShowFlaskEquipSubmenu();
                });
            AddButton("정보", true, ShowSelectedItemInfo);
            AddButton("버리기", PlayerFlaskController.CanChangeLoadout, DropSelectedItem);
            AddButton("닫기", true, Close);
            return;
        }
        switch (item.itemType)
        {
            case "Gear":
                AddButton("장착", true, () =>
                {
                    if (selectedSlot != null && GearEquipmentService.EquipFromInventorySlot(selectedSlot.SlotIndex)) Close();
                });
                AddButton("정보", true, ShowSelectedItemInfo);
                AddButton("버리기", true, DropSelectedItem);
                AddButton("닫기", true, Close);
                break;
            case "Bag":
                AddButton("장착", true, EquipSelectedBag);
                AddButton("정보", true, ShowSelectedItemInfo);
                AddButton("버리기", true, DropSelectedItem);
                AddButton("닫기", true, Close);
                break;
            case "Weapon":
                AddButton("장착", true, EquipSelectedWeapon);
                AddButton("정보", true, ShowSelectedItemInfo);
                AddButton("버리기", true, DropSelectedItem);
                AddButton("닫기", true, Close);
                break;
            case "ComboGem":
                AddButton("정보", true, ShowSelectedItemInfo);
                AddButton("버리기", true, DropSelectedItem);
                AddButton("닫기", true, Close);
                break;
            case "Consumable":
                AddButton("사용", true, UseSelectedConsumable);
                AddButton("퀵슬롯 등록", true, ShowQuickSlotSubmenu);
                AddSplitButtonIfAvailable(item);
                AddButton("정보", true, ShowSelectedItemInfo);
                AddButton("버리기", true, DropSelectedItem);
                AddButton("닫기", true, Close);
                break;
            default:
                AddButton("정보", true, ShowSelectedItemInfo);
                AddSplitButtonIfAvailable(item);
                AddButton("버리기", true, DropSelectedItem);
                AddButton("닫기", true, Close);
                break;
        }
    }

    private void BuildWeaponSlotMenu()
    {
        ClearMenu();
        ShowRootMenu();
        AddButton("장착해제", true, UnequipSelectedWeaponSlot);
        AddButton("닫기", true, Close);
    }

    private void EquipSelectedWeapon()
    {
        actionService?.EquipWeapon(selectedSlot, 0);
        Close();
    }

    private void EquipSelectedBag()
    {
        if (actionService != null && actionService.EquipBag(selectedSlot))
            Close();
    }

    private void UnequipSelectedBagSlot()
    {
        if (selectedSlot != null && actionService != null && actionService.UnequipBagSlot(selectedSlot.SlotIndex))
            Close();
    }

    private void ShowQuickSlotSubmenu()
    {
        ClearMenu();
        ShowRootMenu();
        ItemData item = selectedSlot != null ? selectedSlot.DisplayItem : null;

        for (int key = InventoryQuickSlotBindingController.FirstKey; key <= InventoryQuickSlotBindingController.SlotCount; key++)
        {
            int bindKey = key;
            string label = actionService != null ? actionService.GetQuickSlotLabel(key) : key + " : 비어있음";
            AddButton(label, item != null && item.itemType == "Consumable", () =>
            {
                actionService?.BindQuickSlot(bindKey, item);
                Close();
            });
        }

        AddButton("닫기", true, Close);
        PositionMenu(lastScreenPosition);
    }

    private void ShowFlaskEquipSubmenu()
    {
        ClearMenu();
        ShowRootMenu();
        ItemData item = selectedSlot != null ? selectedSlot.DisplayItem : null;
        PlayerFlaskController flasks = PlayerFlaskController.Current;
        for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
        {
            int target = i;
            ItemData current = flasks != null ? flasks.GetItem(i) : null;
            string label = "물약 " + (i + 1) + "칸" + (current != null ? " · " + current.itemName : " · 비어있음");
            AddButton(label, item != null, () =>
            {
                if (actionService != null && actionService.EquipFlaskToSlot(item, target)) Close();
            });
        }
        AddButton("뒤로", true, () => BuildRootMenu(item));
        PositionMenu(lastScreenPosition);
    }

    private void ShowSplitSubmenu()
    {
        ClearMenu();
        ItemData item = selectedSlot != null ? selectedSlot.DisplayItem : null;
        if (item == null || item.stackCount <= 1)
        {
            Close();
            return;
        }

        if (TryShowObjectizedSplitPopup(item))
            return;

        Close();
    }

    private void UseSelectedConsumable()
    {
        actionService?.UseConsumable(selectedSlot);
        Close();
    }

    private void SplitSelectedStack(TMP_InputField inputField)
    {
        int amount = 0;
        if (inputField == null || !int.TryParse(inputField.text, out amount))
            amount = 1;

        if (TrySplitSelectedStack(amount))
            Close();
    }

    private void DropSelectedItem()
    {
        actionService?.DropItem(selectedSlot, lastScreenPosition);
        Close();
    }

    private void UnequipSelectedWeaponSlot()
    {
        actionService?.UnequipWeaponSlot(selectedWeaponSlotIndex);
        Close();
    }

    private void ShowSelectedItemInfo()
    {
        ItemData item = selectedSlot != null ? selectedSlot.DisplayItem : null;
        Close();
        if (item != null) tooltipManager?.ShowTooltip(item);
    }

    private void EnsureView()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        if (canvas == null)
            return;

        canvasRect = canvas.transform as RectTransform;
        BindObjectizedControls();

        if (HasObjectizedMenuReferences())
            return;

        if (!warnedMissingObjectizedMenu)
        {
            warnedMissingObjectizedMenu = true;
            Debug.LogError("[InventoryContextMenuController] Inventory context menu objectized references are incomplete. Reconnect serialized fields on InventoryContextMenuController.", this);
        }
    }

    private void BindObjectizedControls()
    {
        if (blockerRoot != null)
        {
            InventoryContextMenuBlocker blocker = blockerRoot.GetComponent<InventoryContextMenuBlocker>();
            blocker?.Init(this);
        }

        BindSplitButton(splitIncreaseButton, () => ChangeSplitAmount(splitAmountInput, 1, GetSplitMaxValue()));
        BindSplitButton(splitDecreaseButton, () => ChangeSplitAmount(splitAmountInput, -1, GetSplitMaxValue()));

        if (splitOkButton != null)
        {
            splitOkButton.onClick.RemoveListener(ConfirmObjectizedSplit);
            splitOkButton.onClick.AddListener(ConfirmObjectizedSplit);
            SetButtonText(splitOkButton, "확인");
        }

        if (splitCancelButton != null)
        {
            splitCancelButton.onClick.RemoveListener(Close);
            splitCancelButton.onClick.AddListener(Close);
            SetButtonText(splitCancelButton, "취소");
        }
    }

    private void BindSplitButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void HideObjectizedViews()
    {
        if (blockerRoot != null)
            blockerRoot.gameObject.SetActive(false);

        if (menuRoot != null && HasObjectizedMenuReferences())
            menuRoot.gameObject.SetActive(false);

        if (splitPopupRoot != null)
            splitPopupRoot.gameObject.SetActive(false);
    }

    private void ShowRootMenu()
    {
        if (menuRoot != null)
            menuRoot.gameObject.SetActive(true);

        if (splitPopupRoot != null)
            splitPopupRoot.gameObject.SetActive(false);
    }

    private bool HasObjectizedMenuReferences()
    {
        if (blockerRoot == null || menuRoot == null || menuButtons == null || menuButtons.Length == 0)
            return false;

        for (int i = 0; i < menuButtons.Length; i++)
        {
            if (menuButtons[i] == null)
                return false;
        }

        return true;
    }

    private bool HasAnyObjectizedMenuReference()
    {
        if (blockerRoot != null || menuRoot != null)
            return true;

        if (menuButtons != null)
        {
            for (int i = 0; i < menuButtons.Length; i++)
            {
                if (menuButtons[i] != null)
                    return true;
            }
        }

        return false;
    }

    private bool HasObjectizedSplitReferences()
    {
        return splitPopupRoot != null
            && splitAmountInput != null
            && splitIncreaseButton != null
            && splitDecreaseButton != null
            && splitOkButton != null
            && splitCancelButton != null;
    }

    private void AddObjectizedButton(string label, bool interactable, Action action)
    {
        if (menuButtons == null || objectizedButtonIndex >= menuButtons.Length)
        {
            Debug.LogWarning("[InventoryContextMenuController] Inventory context menu has too few objectized buttons for current menu entries.", this);
            return;
        }

        Button button = menuButtons[objectizedButtonIndex];
        objectizedButtonIndex++;
        if (button == null)
            return;

        button.gameObject.SetActive(true);
        button.interactable = interactable;
        button.onClick.RemoveAllListeners();
        if (interactable && action != null)
            button.onClick.AddListener(() => action.Invoke());

        SetButtonText(button, label);
    }

    private void SetButtonText(Button button, string label)
    {
        if (button == null)
            return;

        TextMeshProUGUI text = GetObjectizedButtonText(button);
        if (text == null)
            return;

        text.text = label;
        if (fontAsset != null)
        {
            text.font = fontAsset;
            text.fontSharedMaterial = fontAsset.material;
        }
    }

    private TextMeshProUGUI GetObjectizedButtonText(Button button)
    {
        if (button == null)
            return null;

        if (menuButtons != null && menuButtonTexts != null)
        {
            for (int i = 0; i < menuButtons.Length && i < menuButtonTexts.Length; i++)
            {
                if (menuButtons[i] == button)
                    return menuButtonTexts[i] != null ? menuButtonTexts[i] : button.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        return button.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private bool TryShowObjectizedSplitPopup(ItemData item)
    {
        if (!HasObjectizedMenuReferences())
            return false;

        if (!HasObjectizedSplitReferences())
        {
            if (!warnedMissingObjectizedSplit)
            {
                warnedMissingObjectizedSplit = true;
                Debug.LogWarning("[InventoryContextMenuController] Inventory split popup references are missing. Reconnect serialized split popup fields.", this);
            }

            return false;
        }

        int maxValue = Mathf.Max(1, item.stackCount - 1);
        int defaultValue = Mathf.Clamp(Mathf.Max(1, item.stackCount / 2), 1, maxValue);

        if (splitTitleText != null)
            splitTitleText.text = "나누기";

        if (splitHintText != null)
            splitHintText.text = "최대 " + maxValue + "개";

        splitAmountInput.characterLimit = Mathf.Max(1, maxValue.ToString().Length);
        splitAmountInput.SetTextWithoutNotify(defaultValue.ToString());
        splitAmountInput.onValueChanged.RemoveListener(ClampObjectizedSplitInput);
        splitAmountInput.onValueChanged.AddListener(ClampObjectizedSplitInput);

        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);

        splitPopupRoot.gameObject.SetActive(true);
        splitPopupRoot.SetAsLastSibling();
        PositionRect(splitPopupRoot, lastScreenPosition, splitPopupRoot.rect.width);
        return true;
    }

    private int GetSplitMaxValue()
    {
        ItemData item = selectedSlot != null ? selectedSlot.DisplayItem : null;
        return item != null ? Mathf.Max(1, item.stackCount - 1) : 1;
    }

    private void ClampObjectizedSplitInput(string value)
    {
        if (splitAmountInput == null || string.IsNullOrWhiteSpace(value))
            return;

        int parsed;
        if (!int.TryParse(value, out parsed))
            return;

        int clamped = Mathf.Clamp(parsed, 1, GetSplitMaxValue());
        if (clamped != parsed)
            splitAmountInput.SetTextWithoutNotify(clamped.ToString());
    }

    private void ConfirmObjectizedSplit()
    {
        SplitSelectedStack(splitAmountInput);
    }

    private void AddButton(string label, bool interactable, Action action)
    {
        if (HasObjectizedMenuReferences())
        {
            AddObjectizedButton(label, interactable, action);
        }
    }

    private void AddSplitButtonIfAvailable(ItemData item)
    {
        if (item == null || item.stackCount <= 1 || !CanSplitSelectedStack())
            return;

        AddButton("나누기", true, ShowSplitSubmenu);
    }

    private bool CanSplitSelectedStack()
    {
        if (selectedSlot == null)
            return false;

        if (selectedSlot.OwnerBridge is StashSlotBridge stashBridge)
            return stashBridge.CanSplitStackFromContextMenu(selectedSlot);

        return actionService != null && actionService.CanSplitStack(selectedSlot);
    }

    private bool TrySplitSelectedStack(int amount)
    {
        if (selectedSlot == null)
            return false;

        if (selectedSlot.OwnerBridge is StashSlotBridge stashBridge)
            return stashBridge.SplitStackFromContextMenu(selectedSlot, amount);

        return actionService != null && actionService.SplitStack(selectedSlot, amount);
    }

    private void ChangeSplitAmount(TMP_InputField input, int delta, int maxValue)
    {
        if (input == null)
            return;

        int current;
        if (!int.TryParse(input.text, out current))
            current = 1;

        int clamped = Mathf.Clamp(current + delta, 1, Mathf.Max(1, maxValue));
        input.SetTextWithoutNotify(clamped.ToString());
    }

    private void ClearMenu()
    {
        if (menuRoot == null)
            return;

        if (HasObjectizedMenuReferences() || HasAnyObjectizedMenuReference())
        {
            objectizedButtonIndex = 0;
            for (int i = 0; menuButtons != null && i < menuButtons.Length; i++)
            {
                if (menuButtons[i] == null)
                    continue;

                menuButtons[i].onClick.RemoveAllListeners();
                menuButtons[i].interactable = false;
                menuButtons[i].gameObject.SetActive(false);
            }

            if (splitPopupRoot != null)
                splitPopupRoot.gameObject.SetActive(false);

            return;
        }
    }

    private void RefreshResponsiveMenuWidth()
    {
        if (menuRoot == null)
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
            if (text == null)
                continue;

            preferredWidth = Mathf.Max(preferredWidth, text.preferredWidth + 34f);
        }

        float canvasLimit = MaxMenuWidth;
        if (canvasRect != null)
            canvasLimit = Mathf.Min(canvasLimit, Mathf.Max(MinMenuWidth, canvasRect.rect.width - 16f));

        currentMenuWidth = Mathf.Clamp(preferredWidth, MinMenuWidth, canvasLimit);
        menuRoot.sizeDelta = new Vector2(currentMenuWidth, menuRoot.sizeDelta.y);
    }

    private void PositionMenu(Vector2 screenPosition)
    {
        if (menuRoot == null || canvasRect == null)
            return;

        RefreshResponsiveMenuWidth();

        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
            menuRoot.anchoredPosition = ClampToCanvas(localPoint);
    }

    private void PositionRect(RectTransform target, Vector2 screenPosition, float width)
    {
        if (target == null)
            return;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect == null)
            return;

        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
            target.anchoredPosition = ClampToCanvas(target, localPoint, width);
    }

    private Vector2 ClampToCanvas(Vector2 localPoint)
    {
        if (canvasRect == null)
            return localPoint;

        Canvas.ForceUpdateCanvases();
        float height = Mathf.Max(ButtonHeight, menuRoot.rect.height);
        Rect rect = canvasRect.rect;
        float x = Mathf.Clamp(localPoint.x, rect.xMin + 8f, rect.xMax - currentMenuWidth - 8f);
        float y = Mathf.Clamp(localPoint.y, rect.yMin + height + 8f, rect.yMax - 8f);
        return new Vector2(x, y);
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
