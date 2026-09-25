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
    }

    private void Awake() { Resolve(); BindListeners(); }
    private void OnEnable() { Resolve(); BindListeners(); Refresh(); }
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
            if (flaskIcons != null && i < flaskIcons.Length && flaskIcons[i] != null)
            {
                flaskIcons[i].sprite = item != null ? item.icon : null;
                flaskIcons[i].enabled = item != null && item.icon != null;
            }
            if (flaskNames != null && i < flaskNames.Length && flaskNames[i] != null)
                flaskNames[i].text = item != null ? item.itemName : "물약 " + (i + 1);
            if (flaskKeys != null && i < flaskKeys.Length && flaskKeys[i] != null)
            {
                int key = quickSlots != null ? quickSlots.GetFlaskKey(item) : 0;
                flaskKeys[i].text = item == null ? "빈 장착칸" : key > 0 ? key + "번 등록" : "번호 미등록";
            }
        }
        if (keyPicker != null && keyPicker.activeSelf && selectedSlot >= 0)
        {
            ItemData selected = flasks.GetItem(selectedSlot);
            if (selected == null) { keyPicker.SetActive(false); selectedSlot = -1; }
        }
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
                keyLabels[i].text = (key % 10) + "번";
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
