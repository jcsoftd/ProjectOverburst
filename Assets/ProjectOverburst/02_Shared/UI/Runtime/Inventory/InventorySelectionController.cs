using UnityEngine;

public class InventorySelectionController : MonoBehaviour
{
    private SlotUI selectedSlot;

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
    }

    private void OnDisable()
    {
        Clear();
    }
}
