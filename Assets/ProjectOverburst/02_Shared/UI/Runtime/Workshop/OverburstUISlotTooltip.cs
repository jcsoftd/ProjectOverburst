using UnityEngine;
using UnityEngine.EventSystems;

public sealed class OverburstUISlotTooltip : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerMoveHandler
{
    private OverburstUITooltipHost host;
    private OverburstUIItemSlotView slot;
    public void OnPointerEnter(PointerEventData data){if(gameObject.layer==31)return;slot=GetComponent<OverburstUIItemSlotView>();host=GetComponentInParent<OverburstUIWorkshop>()?.GetComponentInChildren<OverburstUITooltipHost>(true);if(host)host.Show(slot,data);}
    public void OnPointerMove(PointerEventData data){if(host)host.Place(data.position);}
    public void OnPointerExit(PointerEventData data){if(host)host.Hide(slot);}
    private void OnDisable(){if(host)host.Hide(slot);}
}
