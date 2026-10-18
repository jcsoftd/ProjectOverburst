using UnityEngine;

public class InventorySelectionController : MonoBehaviour
{
    private SlotUI selectedSlot;
    private EquipmentSlotInteraction selectedEquipment;

    public SlotUI SelectedSlot => selectedSlot;
    public ItemData SelectedItem => selectedSlot != null ? selectedSlot.DisplayItem : null;

    public void Select(SlotUI slot)
    {
        if (selectedSlot == slot)
            return;

        Clear();
        selectedSlot = slot;

        if (selectedSlot != null)
            selectedSlot.SetContextSelected(true);
    }

    public void Clear()
    {
        if (selectedSlot != null)
            selectedSlot.SetContextSelected(false);

        selectedSlot = null;
        if (selectedEquipment != null) selectedEquipment.SetSelected(false);
        selectedEquipment = null;
    }

    public void Select(EquipmentSlotInteraction slot)
    {
        if (selectedEquipment == slot) return;
        Clear();
        selectedEquipment = slot;
        if (selectedEquipment != null) selectedEquipment.SetSelected(true);
    }

    private void OnDisable()
    {
        Clear();
    }
}
