using System.Globalization;
using DuloGames.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class OverburstSettingsGothicRow : MonoBehaviour, IPointerEnterHandler
{
    public OverburstSettingsGothicView owner;
    public int tabIndex;
    public string title, description, tip;
    public Sprite icon;
    public Image highlight, outline;
    public Slider slider;
    public Toggle toggle;
    public UISwitchSelect selector;
    public OverburstSettingsNumberRow number;
    public OverburstKeyBindingRow key;
    public Text descriptionSource;
    public string CurrentDescription => descriptionSource ? descriptionSource.text : description;

    void Awake()
    {
        if (slider) slider.onValueChanged.AddListener(_ => owner.RequestRefresh());
        if (toggle) toggle.onValueChanged.AddListener(_ => owner.RequestRefresh());
        if (selector) selector.onChange.AddListener((_, value) => owner.RequestRefresh());
        if (number)
        {
            number.increase.onClick.AddListener(owner.RequestRefresh);
            number.decrease.onClick.AddListener(owner.RequestRefresh);
        }
        foreach (var control in GetComponentsInChildren<Selectable>(true))
        {
            var trigger = control.GetComponent<EventTrigger>() ?? control.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            entry.callback.AddListener(_ => owner.SelectRow(this, OverburstGameMenu.NavigationRequestedThisFrame));
            trigger.triggers.Add(entry);
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => owner.SelectRow(this);
    public void SetHighlighted(bool value)
    {
        if (highlight) highlight.color = value ? new Color(.42f,.32f,.18f,.23f) : Color.clear;
        if (outline)
            foreach (var edge in outline.GetComponentsInChildren<Image>(true))
                edge.color = value ? new Color(.51f,.40f,.25f,.86f) : Color.clear;
    }

    public string DisplayValue()
    {
        if (number && number.valueLabel) return number.valueLabel.text;
        if (selector) return selector.value;
        if (key && key.keyText) return key.keyText.text;
        if (toggle && !slider) return toggle.isOn ? "켬" : "끔";
        if (slider) return Mathf.RoundToInt(slider.value * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        return "—";
    }
}
