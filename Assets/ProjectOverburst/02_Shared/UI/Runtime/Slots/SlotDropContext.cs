using UnityEngine.EventSystems;

public class SlotDropContext // 드롭 정보
{
    public DropSlot TargetDrop { get; private set; }
    public SlotUI TargetSlot { get; private set; }
    public ItemData TargetItem { get; private set; }
    public SlotUI OriginSlot { get; private set; }
    public ItemData OriginItem { get; private set; }
    public PointerEventData EventData { get; private set; }
    public ISlotInteractionBridge Bridge { get; private set; }

    public bool TargetIsWeaponSlot => TargetSlot != null && TargetSlot.IsWeaponSlot;
    public int TargetSlotIndex => TargetSlot != null ? TargetSlot.SlotIndex : -1;
    public bool OriginIsWeaponSlot => OriginSlot != null && OriginSlot.IsWeaponSlot;
    public int OriginSlotIndex => OriginSlot != null ? OriginSlot.SlotIndex : -1;

    public static SlotDropContext Create(DropSlot targetDrop, PointerEventData eventData)
    {
        if (targetDrop == null || targetDrop.Slot == null)
            return null; // target 없음

        SlotUI originSlot = DragSlot.OriginSlot;
        if (originSlot == null)
            return null; // source 없음

        ISlotInteractionBridge targetBridge = targetDrop.Slot.OwnerBridge;
        ISlotInteractionBridge originBridge = originSlot != null ? originSlot.OwnerBridge : null;
        ISlotInteractionBridge bridge = targetBridge is ShopSlotBridge || originBridge is ShopSlotBridge
            ? targetBridge is ShopSlotBridge ? targetBridge : originBridge
            : targetBridge is StashSlotBridge || originBridge is StashSlotBridge
            ? targetBridge is StashSlotBridge ? targetBridge : originBridge
            : targetBridge ?? originBridge;

        if (bridge == null)
            return null; // bridge 없음

        return new SlotDropContext
        {
            TargetDrop = targetDrop,
            TargetSlot = targetDrop.Slot,
            TargetItem = targetDrop.Slot.DisplayItem,
            OriginSlot = originSlot,
            OriginItem = originSlot != null ? originSlot.DisplayItem : DragSlot.DraggedItem,
            EventData = eventData,
            Bridge = bridge,
        };
    }
}
