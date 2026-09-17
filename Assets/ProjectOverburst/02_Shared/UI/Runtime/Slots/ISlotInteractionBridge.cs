using UnityEngine;

public interface ISlotInteractionBridge // 슬롯 정책 연결
{
    void BeginDragPreview(SlotUI originSlot);
    void ShowDragPreviewForTarget(SlotUI targetSlot);
    void ClearDragPreview();
    bool HandleSlotClick(SlotClickContext context);
    bool HandleSlotDrop(SlotDropContext context);
    bool HandleExternalDrop(SlotUI sourceSlot, Vector2 screenPosition);
}

public interface ISlotSingleClickInteractionBridge
{
    bool HandleSlotSingleClick(SlotClickContext context);
}

public interface ISlotRightClickInteractionBridge
{
    bool HandleSlotRightClick(SlotClickContext context);
}
