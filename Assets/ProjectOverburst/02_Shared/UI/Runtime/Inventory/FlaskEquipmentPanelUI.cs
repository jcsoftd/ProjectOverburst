using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class FlaskEquipmentPanelUI : MonoBehaviour
{
    [SerializeField] private Button[] flaskButtons = new Button[PlayerFlaskController.SlotCount];
    [SerializeField] private Image[] flaskIcons = new Image[PlayerFlaskController.SlotCount];
    [SerializeField] private TMP_Text[] flaskNames = new TMP_Text[PlayerFlaskController.SlotCount];
    [SerializeField] private TMP_Text[] flaskKeys = new TMP_Text[PlayerFlaskController.SlotCount];
    [SerializeField] private GameObject keyPicker;
    [SerializeField] private Button[] keyButtons = new Button[InventoryQuickSlotBindingController.SlotCount];
    [SerializeField] private TMP_Text[] keyLabels = new TMP_Text[InventoryQuickSlotBindingController.SlotCount];
    [SerializeField] private Button pickerCloseButton;

    private InventoryQuickSlotBindingController quickSlots;
    private InventoryItemActionService actions;
    private int selectedSlot = -1;
    private bool listenersBound;
    private static readonly string[] EmptyNames = { "물약 1", "물약 2", "물약 3" };
    private readonly Presentation[] shown = new Presentation[PlayerFlaskController.SlotCount];
    private struct Presentation
    {
        public bool Ready;
        public Sprite Icon;
        public string Name, KeyLabel;
        public int Key;
        public bool HasItem, IconVisible;
    }

    public void Configure(Button[] buttons, Image[] icons, TMP_Text[] names, TMP_Text[] keys,
        GameObject picker, Button[] pickerButtons, TMP_Text[] pickerLabels, Button closeButton)
    {
        flaskButtons = buttons;
        flaskIcons = icons;
        flaskNames = names;
        flaskKeys = keys;
        keyPicker = picker;
        keyButtons = pickerButtons;
        keyLabels = pickerLabels;
        pickerCloseButton = closeButton;
        InvalidatePresentation();
    }

    private void Awake() { Resolve(); BindListeners(); }
    private void OnEnable() { InvalidatePresentation(); Resolve(); BindListeners(); Refresh(); }
    private void OnDisable() { CloseKeyPicker(); }
    private void Update() { Refresh(); }

    private void Resolve()
    {
        if (quickSlots == null)
            quickSlots = FindFirstObjectByType<InventoryQuickSlotBindingController>(FindObjectsInactive.Include);
        if (actions == null)
            actions = FindFirstObjectByType<InventoryItemActionService>(FindObjectsInactive.Include);
    }

    private void BindListeners()
    {
        if (listenersBound) return;
        listenersBound = true;
        for (int i = 0; i < flaskButtons.Length; i++)
        {
            int slot = i;
            if (flaskButtons[i] != null) flaskButtons[i].onClick.AddListener(() => OpenKeyPicker(slot));
        }
        for (int i = 0; i < keyButtons.Length; i++)
        {
            int key = i + InventoryQuickSlotBindingController.FirstKey;
            if (keyButtons[i] != null) keyButtons[i].onClick.AddListener(() => ChooseKey(key));
        }
        if (pickerCloseButton != null) pickerCloseButton.onClick.AddListener(CloseKeyPicker);
    }

    public void Refresh()
    {
        Resolve();
        PlayerFlaskController flasks = PlayerFlaskController.Current;
        if (flasks == null) return;
        for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
        {
            ItemData item = flasks.GetItem(i);
            int key = quickSlots != null ? quickSlots.GetFlaskKey(item) : 0;
            PresentSlot(i, item != null, item != null ? item.icon : null,
                item != null ? item.itemName : EmptyNames[i], key,
                item != null && key > 0 ? QuickSlotKeyLabels.Numbered(key) : null);
        }
        if (keyPicker != null && keyPicker.activeSelf && selectedSlot >= 0)
        {
            ItemData selected = flasks.GetItem(selectedSlot);
            if (selected == null) { keyPicker.SetActive(false); selectedSlot = -1; }
        }
    }

    private void InvalidatePresentation()
    {
        for (int i = 0; i < shown.Length; i++) shown[i].Ready = false;
    }

    private void PresentSlot(int i, bool hasItem, Sprite icon, string name, int key, string keyLabel)
    {
        Presentation previous = shown[i];
        bool iconVisible = hasItem && icon != null;
        if (!previous.Ready || previous.Icon != icon || previous.IconVisible != iconVisible)
        {
            if (flaskIcons != null && i < flaskIcons.Length && flaskIcons[i] != null)
            {
                flaskIcons[i].sprite = icon;
                flaskIcons[i].enabled = iconVisible;
            }
        }
        if ((!previous.Ready || previous.Name != name) && flaskNames != null && i < flaskNames.Length && flaskNames[i] != null)
            flaskNames[i].text = name;
        if (!previous.Ready || previous.HasItem != hasItem || previous.Key != key || previous.KeyLabel != keyLabel)
        {
            if (flaskKeys != null && i < flaskKeys.Length && flaskKeys[i] != null)
                flaskKeys[i].text = !hasItem ? "빈 장착칸" : key > 0 ? keyLabel + " 등록" : "번호 미등록";
        }
        shown[i] = new Presentation { Ready = true, HasItem = hasItem, IconVisible = iconVisible, Icon = icon, Name = name, Key = key, KeyLabel = keyLabel };
    }

    private void OpenKeyPicker(int slot)
    {
        if (!PlayerFlaskController.CanChangeLoadout) return;
        PlayerFlaskController flasks = PlayerFlaskController.Current;
        if (flasks == null || flasks.GetItem(slot) == null || keyPicker == null) return;
        if (selectedSlot == slot && keyPicker.activeSelf)
        { CloseKeyPicker(); return; }
        selectedSlot = slot;
        for (int i = 0; i < keyLabels.Length; i++)
        {
            int key = i + InventoryQuickSlotBindingController.FirstKey;
            ItemData boundFlask = quickSlots != null ? quickSlots.GetBoundFlask(key) : null;
            bool selected = boundFlask == flasks.GetItem(slot);
            bool occupied = boundFlask != null || (quickSlots != null
                && (quickSlots.GetBoundConsumable(key) != null || quickSlots.GetBoundSkill(key) != null));
            if (keyLabels[i] != null)
            {
                keyLabels[i].text = QuickSlotKeyLabels.Numbered(key);
                keyLabels[i].color = selected ? new Color(.62f, .96f, .72f)
                    : occupied ? new Color(.98f, .76f, .48f) : Color.white;
            }
            if (keyButtons[i] != null && keyButtons[i].targetGraphic != null)
                keyButtons[i].targetGraphic.color = selected ? new Color(.12f, .26f, .19f, .96f)
                    : occupied ? new Color(.29f, .2f, .13f, .96f) : new Color(.08f, .095f, .12f, .96f);
        }
        keyPicker.SetActive(true);
    }

    private void ChooseKey(int key)
    {
        PlayerFlaskController flasks = PlayerFlaskController.Current;
        ItemData item = flasks != null && selectedSlot >= 0 ? flasks.GetItem(selectedSlot) : null;
        if (item != null && actions != null) actions.BindQuickSlot(key, item);
        CloseKeyPicker();
        Refresh();
    }

    private void CloseKeyPicker()
    {
        if (keyPicker != null) keyPicker.SetActive(false);
        selectedSlot = -1;
    }
}
