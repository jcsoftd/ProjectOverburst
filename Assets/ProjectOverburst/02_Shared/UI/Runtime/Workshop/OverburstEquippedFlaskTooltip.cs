using UnityEngine;
using UnityEngine.EventSystems;
public sealed class OverburstEquippedFlaskTooltip:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerClickHandler
{
    public int index;
    public void OnPointerClick(PointerEventData e)
    {
        // 공통 장비 입력이 있는 슬롯에서는 문맥 메뉴/더블클릭을 한 번만 처리한다.
        if (GetComponent<EquipmentSlotInteraction>() != null) return;
        if (e == null || (e.button != PointerEventData.InputButton.Right
            && (e.button != PointerEventData.InputButton.Left || e.clickCount < 2))) return;
        var item = PlayerFlaskController.Current?.GetItem(index);
        var actions = GetComponentInParent<InventoryItemActionService>();
        if (item != null && actions != null && actions.UnequipFlask(item)) TooltipManager.Instance?.HideTooltip();
    }
    public void OnPointerEnter(PointerEventData e){var f=PlayerFlaskController.Current;var item=f?f.GetItem(index):null;if(item!=null)TooltipManager.Instance?.ShowTooltip(item);}
    public void OnPointerExit(PointerEventData e)=>TooltipManager.Instance?.HideTooltip();
    private void OnDisable()=>TooltipManager.Instance?.HideTooltip();
}
