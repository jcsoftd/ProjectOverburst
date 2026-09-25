using UnityEngine;
using UnityEngine.EventSystems;

public sealed class GearEquipmentPanelUI : MonoBehaviour
{
    private static readonly string[] SlotNames =
    {
        "Slot • 투구", "Slot • 갑옷", "Slot • 장갑", "Slot • 신발",
        "Slot • 귀걸이 1", "Slot • 귀걸이 2", "Slot • 목걸이"
    };
    private readonly OverburstUIItemSlotView[] views = new OverburstUIItemSlotView[7];
    private readonly ItemData[] shown = new ItemData[7];

    public void Bind()
    {
        Transform layout = transform.Find("Layout");
        if (layout == null) return;
        for (int i = 0; i < views.Length; i++)
        {
            Transform slot = layout.Find(SlotNames[i]);
            if (slot == null) continue;
            views[i] = slot.GetComponent<OverburstUIItemSlotView>();
            if (views[i] != null) views[i].Present(null, ItemGrade.Common);
            GearEquipmentSlotUI pointer = slot.GetComponent<GearEquipmentSlotUI>();
            if (pointer == null) pointer = slot.gameObject.AddComponent<GearEquipmentSlotUI>();
            pointer.Bind(i);
        }
    }

    public void Refresh(PlayerEquipment equipment)
    {
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] == null) continue;
            ItemData item = equipment != null ? equipment.GetGearSlotItem(i) : null;
            if (shown[i] == item) continue;
            shown[i] = item;
            views[i].Present(item != null ? item.icon : null, item != null ? item.grade : ItemGrade.Common);
        }
    }
}

public sealed class GearEquipmentSlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private int gearSlot;
    public void Bind(int slot) => gearSlot = slot;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null) return;
        bool unequip = eventData.button == PointerEventData.InputButton.Right
            || eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2;
        if (!unequip) return;
        if (GearEquipmentService.UnequipToInventory(gearSlot)) TooltipManager.Instance?.HideTooltip();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        PlayerEquipment equipment = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActorEquipment : null;
        ItemData item = equipment != null ? equipment.GetGearSlotItem(gearSlot) : null;
        if (item != null) TooltipManager.Instance?.ShowTooltip(item);
    }

    public void OnPointerExit(PointerEventData eventData) => TooltipManager.Instance?.HideTooltip();
}
