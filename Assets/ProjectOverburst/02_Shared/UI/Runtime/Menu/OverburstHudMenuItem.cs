using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class OverburstHudMenuItem : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public Button button;
    public Image icon, separator;
    public Text label, arrow;
    public Image highlight;
    public Sprite hoverBackground, pressedBackground;
    Sprite normalIcon, hoverIcon;
    bool hovered, focused, pressed;
    OverburstHudMenu owner;
    int index;
    public void Bind(OverburstHudMenu menu, int itemIndex, OverburstHudMenu.Entry entry)
    {
        owner = menu; index = itemIndex;
        label.text = entry.label; normalIcon=entry.icon;hoverIcon=entry.hoverIcon?entry.hoverIcon:entry.icon;
        button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => owner.Activate(index));
        Refresh();
    }
    void Refresh(){bool active=hovered||focused;highlight.enabled=active||pressed;highlight.sprite=pressed?pressedBackground:hoverBackground;icon.sprite=active?hoverIcon:normalIcon;label.color=active?new Color32(255,240,211,255):new Color32(237,227,209,255);}
    void OnDisable(){hovered=focused=pressed=false;if(highlight)Refresh();}
    public void OnSelect(BaseEventData eventData){focused=!(eventData is PointerEventData);Refresh();owner?.KeepVisible(index);}
    public void OnDeselect(BaseEventData e){focused=false;Refresh();}
    public void OnPointerEnter(PointerEventData e){hovered=true;Refresh();}
    public void OnPointerExit(PointerEventData e){hovered=false;pressed=false;Refresh();}
    public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left){pressed=true;Refresh();}}
    public void OnPointerUp(PointerEventData e){pressed=false;Refresh();}
}
