using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Stages a reference only. Inventory ownership is unchanged until the run command succeeds.</summary>
public sealed class RunTransferDropTarget : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    private OverburstRunUi owner;
    public void Bind(OverburstRunUi view) { owner = view; }
    public void OnDrop(PointerEventData eventData)
    {
        if(owner==null)return;
        var gear=eventData.pointerDrag!=null?eventData.pointerDrag.GetComponent<RunTransferEquipmentSource>():null;
        if(gear!=null){owner.TryStageTransfer(gear.Item?.runtimeInstanceId);owner.SetTransferHover(false);return;}
        if (!DragSlot.IsDragging) return;
        var item = DragSlot.DraggedItem;
        // Consume the UI gesture even when ineligible; never turn a rejected drop into a world drop.
        DragSlot.MarkDropHandled();
        owner.TryStageTransfer(item.runtimeInstanceId);
        owner.SetTransferHover(false);
    }
    public void OnPointerEnter(PointerEventData eventData)
    { if (DragSlot.IsDragging) owner?.SetTransferHover(true); }
    public void OnPointerExit(PointerEventData eventData) { owner?.SetTransferHover(false); }
}

