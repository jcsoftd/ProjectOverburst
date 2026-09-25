using UnityEngine;
using UnityEngine.EventSystems;
public sealed class OverburstEquippedFlaskTooltip:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
{
    public int index;
    public void OnPointerEnter(PointerEventData e){var f=PlayerFlaskController.Current;var item=f?f.GetItem(index):null;if(item!=null)TooltipManager.Instance?.ShowTooltip(item);}
    public void OnPointerExit(PointerEventData e)=>TooltipManager.Instance?.HideTooltip();
    private void OnDisable()=>TooltipManager.Instance?.HideTooltip();
}
