using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Approved trigger states, including the original one-pixel pressed movement.</summary>
public sealed class OverburstHudMenuTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    public OverburstHudMenu owner;
    public Image background;
    public Sprite normal, hover, expanded, menuHover, closeHover;
    bool hovered, focused, pressed;
    public void Refresh()
    {
        bool highlight=hovered||focused;
        background.sprite=owner.IsOpen?expanded:highlight?hover:normal;
        owner.triggerIcon.sprite=owner.IsOpen?(highlight?closeHover:owner.closeIcon):(highlight?menuHover:owner.menuIcon);
        ((RectTransform)transform).anchoredPosition=new Vector2(0,pressed?-1:0);
    }
    public void OnPointerEnter(PointerEventData e){hovered=true;Refresh();}
    public void OnPointerExit(PointerEventData e){hovered=false;pressed=false;Refresh();}
    public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left){pressed=true;Refresh();}}
    public void OnPointerUp(PointerEventData e){pressed=false;Refresh();}
    public void OnSelect(BaseEventData e){focused=!(e is PointerEventData);Refresh();}
    public void OnDeselect(BaseEventData e){focused=false;Refresh();}
    void OnDisable(){hovered=focused=pressed=false;if(owner&&background)Refresh();}
}
