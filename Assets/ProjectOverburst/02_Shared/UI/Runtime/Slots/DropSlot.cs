using UnityEngine;
using UnityEngine.EventSystems;

public class DropSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler // 슬롯 드롭
{
    private SlotUI slotUI; // 대상 슬롯

    public SlotUI Slot => slotUI;

    public void Init(SlotUI slot)
    {
        slotUI = slot; // 슬롯 연결
    }

    public void OnDrop(PointerEventData eventData)
    {
        DragSlot.RegisterDropTarget(this); // 명시 슬롯

        if (slotUI != null && slotUI.OwnerBridge != null)
            slotUI.OwnerBridge.ClearDragPreview(); // 미리보기 정리
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!DragSlot.IsDragging || slotUI == null || slotUI.OwnerBridge == null)
            return;

        slotUI.OwnerBridge.ShowDragPreviewForTarget(slotUI); // 대상 미리보기
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!DragSlot.IsDragging || slotUI == null || slotUI.OwnerBridge == null)
            return;

        slotUI.OwnerBridge.ShowDragPreviewForTarget(null); // 미리보기 해제
    }

}
