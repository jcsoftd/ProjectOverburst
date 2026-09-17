using UnityEngine.EventSystems;

public class SlotClickContext // 클릭 정보
{
    public SlotUI Slot { get; private set; }
    public ItemData Item { get; private set; }
    public PointerEventData EventData { get; private set; }
    public ISlotInteractionBridge Bridge { get; private set; }
    public bool IsDoubleClick { get; private set; }

    public bool IsWeaponSlot => Slot != null && Slot.IsWeaponSlot;
    public int SlotIndex => Slot != null ? Slot.SlotIndex : -1;

    public static SlotClickContext Create(SlotUI slot, PointerEventData eventData, bool isDoubleClick)
    {
        if (slot == null || slot.DisplayItem == null || slot.OwnerBridge == null)
            return null; // 처리 대상 없음

        return new SlotClickContext
        {
            Slot = slot,
            Item = slot.DisplayItem,
            EventData = eventData,
            Bridge = slot.OwnerBridge,
            IsDoubleClick = isDoubleClick
        };
    }
}
