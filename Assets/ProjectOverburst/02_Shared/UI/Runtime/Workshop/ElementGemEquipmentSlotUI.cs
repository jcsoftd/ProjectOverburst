using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ElementGemEquipmentSlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IDropHandler
{
    [SerializeField] private Text feedback;
    private float clearAt;
    public void Configure(Text label) => feedback = label;
    public void OnPointerClick(PointerEventData data)
    {
        if (data == null || !(data.button == PointerEventData.InputButton.Right || data.button == PointerEventData.InputButton.Left && data.clickCount >= 2)) return;
        if (ElementGemEquipmentService.UnequipToInventory()) TooltipManager.Instance?.HideTooltip();
        else Fail("빈 슬롯 필요");
    }
    public void OnDrop(PointerEventData data)
    {
        var source = DragSlot.OriginSlot;
        if (source == null || source.IsBagSlot || source.IsWeaponSlot || !(source.OwnerBridge is InventorySlotBridge) || !(source.DisplayItem?.baseData is ElementGemItemData)) return;
        bool success = ElementGemEquipmentService.EquipFromInventorySlot(source.SlotIndex,source.DisplayItem.runtimeInstanceId);
        DragSlot.MarkDropHandled();
        if (!success) Fail("장착 실패");
    }
    public void OnPointerEnter(PointerEventData data)
    {
        var item = PlayerContext.Instance?.CurrentActorEquipment?.EquippedElementGem;
        if (item != null) TooltipManager.Instance?.ShowTooltip(item);
    }
    public void OnPointerExit(PointerEventData data) => TooltipManager.Instance?.HideTooltip();
    private void Fail(string message) { if (feedback != null) { feedback.text = message; clearAt = Time.unscaledTime + 2; } }
    private void Update() { if (clearAt > 0 && Time.unscaledTime >= clearAt) { clearAt = 0; if (feedback != null) feedback.text = "원소보석"; } }
    private void OnDisable() { clearAt = 0; if (feedback != null) feedback.text = "원소보석"; }
}
