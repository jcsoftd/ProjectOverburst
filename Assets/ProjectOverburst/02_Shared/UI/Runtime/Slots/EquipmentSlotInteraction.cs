using UnityEngine;
using UnityEngine.EventSystems;

public enum EquipmentSlotKind { Gear, Gem, Flask }

// The existing equipment views keep presenting their own icons and grade effects.
public class EquipmentSlotInteraction : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
    IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public EquipmentSlotKind Kind { get; private set; }
    public int SlotIndex { get; private set; }
    private SlotStateMarker marker;
    private bool selected, hovered;
    private bool dragPreview;
    private ItemData selectedItem;
    private float lastClick = float.NegativeInfinity;
    public ItemData DisplayItem => Kind == EquipmentSlotKind.Flask ? PlayerFlaskController.Current?.GetItem(SlotIndex)
        : Kind == EquipmentSlotKind.Gem ? PlayerContext.Instance?.CurrentActorEquipment?.EquippedElementGem
        : PlayerContext.Instance?.CurrentActorEquipment?.GetGearSlotItem(SlotIndex);
    private InventoryItemActionService Actions => FindFirstObjectByType<InventoryItemActionService>(FindObjectsInactive.Include);
    public void Configure(EquipmentSlotKind kind, int index) { Kind = kind; SlotIndex = index; }
    public void SetSelected(bool value) { selected = value; selectedItem = value ? DisplayItem : null; Present(null); }
    private void LateUpdate()
    {
        if (selected && !ReferenceEquals(selectedItem, DisplayItem)) { SetSelected(false); return; }
        if (dragPreview && !DragSlot.IsDragging) Present(null);
    }
    private void Present(string label)
    {
        dragPreview = label != null;
        if (marker == null) marker = SlotStateMarker.Create(transform);
        marker.Present(label ?? (selected && DisplayItem != null ? "선택" : null), label != null || selected && DisplayItem != null, false, hovered);
    }
    public void OnPointerClick(PointerEventData data)
    {
        if (data == null || data.dragging || DragSlot.IsDragging || DisplayItem == null) return;
        var selection = FindFirstObjectByType<InventorySelectionController>(FindObjectsInactive.Include);
        if (data.button == PointerEventData.InputButton.Right)
        {
            lastClick = float.NegativeInfinity;
            selection?.Select(this);
            FindFirstObjectByType<InventoryContextMenuController>(FindObjectsInactive.Include)?.OpenForEquipment(this, data.position);
        }
        else if (data.button == PointerEventData.InputButton.Left)
        {
            selection?.Select(this);
            if (Time.unscaledTime - lastClick <= .3f)
            { lastClick = float.NegativeInfinity; Unequip(); TooltipManager.Instance?.HideTooltip(); }
            else lastClick = Time.unscaledTime;
        }
    }
    public bool Unequip(int inventorySlot = -1)
    {
        return Kind == EquipmentSlotKind.Gear ? GearEquipmentService.UnequipToInventory(SlotIndex, inventorySlot)
            : Kind == EquipmentSlotKind.Gem ? ElementGemEquipmentService.UnequipToInventory(inventorySlot)
            : Actions != null && Actions.UnequipFlask(DisplayItem, inventorySlot);
    }
    public bool CanReceive(SlotUI source)
    {
        if (source == null || source.IsLocked || source.IsWeaponSlot || source.IsBagSlot || !(source.OwnerBridge is InventorySlotBridge)) return false;
        var item = source.DisplayItem;
        if (item == null || !ReferenceEquals(PlayerContext.Instance?.CurrentActorInventory?.GetItemAt(source.SlotIndex), item)) return false;
        if (Kind == EquipmentSlotKind.Gear) return item.baseData is GearItemData gear && GearItemData.Fits(gear.kind, (GearSlot)SlotIndex);
        if (Kind == EquipmentSlotKind.Gem) return item.baseData is ElementGemItemData;
        return item.baseData is FlaskItemData && PlayerFlaskController.CanChangeLoadout;
    }
    public bool CanReturnTo(SlotUI target)
    {
        if (target == null || target.IsLocked || target.IsWeaponSlot || target.IsBagSlot || !(target.OwnerBridge is InventorySlotBridge)) return false;
        if (Kind == EquipmentSlotKind.Flask && !PlayerFlaskController.CanChangeLoadout) return false;
        return target.DisplayItem == null || CanReceive(target);
    }
    public bool ReceiveDrop()
    {
        var source = DragSlot.OriginSlot;
        if (!DragSlot.HasCurrentOrigin || !CanReceive(source)) return false;
        return Kind == EquipmentSlotKind.Gear ? GearEquipmentService.EquipFromInventorySlot(source.SlotIndex, SlotIndex)
            : Kind == EquipmentSlotKind.Gem ? ElementGemEquipmentService.EquipFromInventorySlot(source.SlotIndex, source.DisplayItem.runtimeInstanceId)
            : Actions != null && Actions.EquipFlaskToSlot(source.DisplayItem, SlotIndex);
    }
    public void OnDrop(PointerEventData data) { if (DragSlot.HasCurrentOrigin) { ReceiveDrop(); DragSlot.MarkDropHandled(); } Present(null); }
    public void OnBeginDrag(PointerEventData data)
    {
        lastClick = float.NegativeInfinity;
        if (data.button == PointerEventData.InputButton.Left) DragSlot.BeginEquipmentDrag(this, GetComponentInParent<Canvas>(), data.position);
    }
    public void OnDrag(PointerEventData data) => DragSlot.MoveExternalDrag(data.position);
    public void OnEndDrag(PointerEventData data) => DragSlot.CompleteDrag(data);
    public void OnPointerEnter(PointerEventData data)
    {
        hovered = true;
        Present(DragSlot.HasCurrentOrigin && CanReceive(DragSlot.OriginSlot) ? DisplayItem == null ? "장착" : "교환" : null);
        if (!DragSlot.IsDragging && DisplayItem != null) TooltipManager.Instance?.ShowTooltip(DisplayItem);
    }
    public void OnPointerExit(PointerEventData data) { hovered = false; Present(null); TooltipManager.Instance?.HideTooltip(); }
    protected virtual void OnDisable()
    {
        lastClick = float.NegativeInfinity; selected = hovered = false;
        if (marker != null) marker.gameObject.SetActive(false);
        if (DragSlot.EquipmentOrigin == this) DragSlot.ClearDragState();
    }
}
