using UnityEngine;

public class ShopSlotBridge : MonoBehaviour, ISlotInteractionBridge, ISlotSingleClickInteractionBridge, ISlotRightClickInteractionBridge
{
    [SerializeField] private ShopUI shopUI;

    private SlotUI previewOriginSlot;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Init(ShopUI owner)
    {
        shopUI = owner;
    }

    public void BeginDragPreview(SlotUI originSlot)
    {
        previewOriginSlot = originSlot;
        ClearDragPreview();
    }

    public void ShowDragPreviewForTarget(SlotUI targetSlot)
    {
        SlotUI originSlot = DragSlot.OriginSlot != null ? DragSlot.OriginSlot : previewOriginSlot;
        ClearDragPreview();
        if (targetSlot != null && shopUI != null && shopUI.CanConsumeShopSlotDrop(originSlot, targetSlot))
            targetSlot.SetDragOverlay(SlotDragOverlayState.WillUnlock);
    }

    public void ClearDragPreview()
    {
        shopUI?.ClearShopSlotDragPreview();
    }

    public bool HandleSlotSingleClick(SlotClickContext context)
    {
        return HandleShopSlotSingleClick(context);
    }

    public bool HandleSlotRightClick(SlotClickContext context)
    {
        if (context == null || context.Slot == null || shopUI == null)
            return false;

        return shopUI.HandleShopSlotRightClicked(context.Slot, context.EventData);
    }

    public bool HandleSlotClick(SlotClickContext context)
    {
        return HandleShopSlotClick(context);
    }

    public bool HandleSlotDrop(SlotDropContext context)
    {
        if (context == null || shopUI == null)
            return false;

        return shopUI.HandleShopSlotDrop(context.OriginSlot, context.TargetSlot);
    }

    public bool HandleExternalDrop(SlotUI sourceSlot, Vector2 screenPosition)
    {
        return shopUI != null && shopUI.ContainsShopSlot(sourceSlot);
    }

    private bool HandleShopSlotClick(SlotClickContext context)
    {
        if (context == null || context.Slot == null || shopUI == null)
            return false;

        return shopUI.HandleShopSlotClicked(context.Slot);
    }

    public bool HandleShopSlotSingleClick(SlotClickContext context)
    {
        if (context == null || context.Slot == null || shopUI == null)
            return false;

        return shopUI.HandleShopSlotSingleClicked(context.Slot);
    }

    private void ResolveReferences()
    {
        if (shopUI == null)
            shopUI = GetComponent<ShopUI>() ?? GetComponentInParent<ShopUI>(true);
    }
}
