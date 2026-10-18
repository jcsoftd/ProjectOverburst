using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler // 슬롯 드래그
{
    private static GameObject dragIcon; // 드래그 아이콘
    private static ISlotInteractionBridge originBridge; // 출발 Bridge
    private static ISlotInteractionBridge previewBridge;
    public static ItemData DraggedItem { get; private set; }
    public static EquipmentSlotInteraction EquipmentOrigin { get; private set; }
    public static ActionSlotHudSlotUI QuickOrigin { get; private set; }
    private static object quickBinding;
    public static bool IsDragging => OriginSlot != null || EquipmentOrigin != null || QuickOrigin != null;
    public static bool HasCurrentOrigin => EquipmentOrigin != null ? ReferenceEquals(EquipmentOrigin.DisplayItem, DraggedItem)
        : QuickOrigin != null ? quickBinding != null && Equals(QuickOrigin.Binding, quickBinding)
        : OriginSlot != null && OriginSlot.DisplayItem != null && OriginSlot.DisplayItem.IsSameRuntimeItem(DraggedItem);

    private SlotUI slotUI; // 출발 슬롯
    private Canvas canvas; // UI 캔버스
    private static bool dropHandled; // 드롭 처리
    private static DropSlot pendingDropSlot; // 명시 드롭 대상

    public static SlotUI OriginSlot { get; private set; } // 출발 슬롯
    public static bool DropHandled => dropHandled;

    public void Init(SlotUI slot)
    {
        slotUI = slot; // 슬롯 연결

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(); // 캔버스 루트
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        slotUI?.ResetClickSequence();
        if (!MapDungeonPortalPanel.AllowsSlotDrag(slotUI)) return;
        if (slotUI == null || slotUI.DisplayItem == null || slotUI.IsLocked)
            return;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(); // 캔버스 루트

        if (canvas == null)
            return;

        OriginSlot = slotUI; // 출발 슬롯
        DraggedItem = slotUI.DisplayItem; // 드래그 아이템
        dropHandled = false; // 드롭 초기화
        pendingDropSlot = null;
        originBridge = slotUI.OwnerBridge; // 정책 Bridge
        originBridge?.BeginDragPreview(slotUI); // 미리보기 시작

        if (TooltipManager.Instance != null)
            TooltipManager.Instance.HideTooltip(); // Tooltip 정리

        CreateDragIcon(slotUI.DisplayItem, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragIcon != null)
            dragIcon.transform.position = eventData.position; // 포인터 추적
    }

    public static bool BeginExternalDrag(SlotUI originSlot, ItemData item, Canvas sourceCanvas, Vector2 position)
    {
        if (originSlot == null || item == null || sourceCanvas == null)
            return false;

        OriginSlot = originSlot; // 출발 슬롯
        DraggedItem = item; // 드래그 아이템
        dropHandled = false; // 드롭 초기화
        pendingDropSlot = null;
        originBridge = originSlot.OwnerBridge; // 정책 Bridge
        originBridge?.BeginDragPreview(originSlot); // 미리보기 시작

        if (TooltipManager.Instance != null)
            TooltipManager.Instance.HideTooltip(); // Tooltip 정리

        CreateDragIcon(sourceCanvas, item, position);
        return true;
    }

    public static void BeginEquipmentDrag(EquipmentSlotInteraction source, Canvas canvas, Vector2 position)
    {
        if (source == null || source.DisplayItem == null || canvas == null) return;
        ClearDragState(); EquipmentOrigin = source; DraggedItem = source.DisplayItem;
        TooltipManager.Instance?.HideTooltip();
        CreateDragIcon(canvas, DraggedItem, position);
    }

    public static void BeginQuickSlotDrag(ActionSlotHudSlotUI source, Canvas canvas, Vector2 position)
    {
        if (source == null || source.Binding == null || canvas == null) return;
        ClearDragState(); QuickOrigin = source; quickBinding = source.Binding;
        TooltipManager.Instance?.HideTooltip();
        CreateDragIcon(canvas, source.DragSprite, Color.white, position);
    }

    public static void MoveExternalDrag(Vector2 position)
    {
        if (dragIcon != null)
            dragIcon.transform.position = position; // 포인터 추적
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        CompleteDrag(eventData); // 단일 완료
    }

    public static bool IsPointerOverAnyUi(PointerEventData eventData)
    {
        if (eventData == null || EventSystem.current == null)
            return false;

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results); // UI 광선 검사

        for (int i = 0; i < results.Count; i++)
        {
            GameObject target = results[i].gameObject;
            if (target == null || dragIcon != null && target.transform.IsChildOf(dragIcon.transform)) // 아이콘 제외
                continue;

            return true;
        }

        return false;
    }

    public static void MarkDropHandled()
    {
        dropHandled = true; // 드롭 완료
    }

    public static void TrackPreviewBridge(ISlotInteractionBridge bridge)
    {
        if (previewBridge != bridge) previewBridge?.ClearDragPreview();
        previewBridge = bridge;
    }

    public static void RegisterDropTarget(DropSlot dropSlot)
    {
        if (dropSlot == null || !HasCurrentOrigin)
            return;

        pendingDropSlot = dropSlot; // 명시 슬롯
    }

    public static void CompleteDrag(PointerEventData eventData)
    {
        if (!HasCurrentOrigin)
        {
            ClearDragState();
            return;
        }

        if (StashUI.ShouldCancelDragForPendingClose(OriginSlot))
        {
            ClearDragState(); // 닫기와 클릭 해제가 같은 프레임이면 소유권을 변경하지 않는다.
            return;
        }

        if (!dropHandled) TryHandleUiDrop(eventData); // An outside drop cancels; discarding is an explicit menu action.

        ClearDragState();
    }

    private static bool TryHandleUiDrop(PointerEventData eventData)
    {
        if (dropHandled || eventData == null || EventSystem.current == null || !IsDragging)
            return false;

        if (TryHandleFallbackDropSlot(pendingDropSlot, eventData))
            return true;

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results); // UI 대상 광선 검사

        for (int i = 0; i < results.Count; i++)
        {
            GameObject target = results[i].gameObject;
            if (target == null || dragIcon != null && target.transform.IsChildOf(dragIcon.transform)) // 아이콘 제외
                continue;

            var equipment = target.GetComponentInParent<EquipmentSlotInteraction>();
            if (equipment != null) { equipment.ReceiveDrop(); return true; }
            var quick = target.GetComponentInParent<ActionSlotHudSlotUI>();
            if (quick != null) { quick.ReceiveDrop(eventData.position); return true; }
            DropSlot dropSlot = target.GetComponentInParent<DropSlot>();
            if (TryHandleFallbackDropSlot(dropSlot, eventData)) return true;
            return false; // Do not drop through a foreground panel into a covered slot.
        }

        return false;
    }

    public static void ClearDragState()
    {
        EquipmentOrigin = null; QuickOrigin = null; quickBinding = null;
        OriginSlot = null; // 출발 해제
        DraggedItem = null; // 아이템 해제
        dropHandled = false; // 드롭 초기화
        pendingDropSlot = null; // 대상 해제
        originBridge?.ClearDragPreview(); // 미리보기 정리
        if (previewBridge != originBridge) previewBridge?.ClearDragPreview();
        previewBridge = null;
        originBridge = null; // Bridge 해제

        if (dragIcon == null)
            return;

        Destroy(dragIcon); // 아이콘 제거
        dragIcon = null; // 참조 해제
    }

    private void OnDisable()
    {
        if (OriginSlot == slotUI)
            ClearDragState();
    }

    private void CreateDragIcon(ItemData item, Vector2 position)
    {
        CreateDragIcon(canvas, item, position);
    }

    private static void CreateDragIcon(Canvas sourceCanvas, ItemData item, Vector2 position)
    {
        CreateDragIcon(sourceCanvas, item.icon, item.icon != null ? item.iconColor : new Color(item.color.r, item.color.g, item.color.b, .65f), position);
    }

    private static void CreateDragIcon(Canvas sourceCanvas, Sprite sprite, Color color, Vector2 position)
    {
        ClearDragIconOnly();

        dragIcon = new GameObject("DragIcon"); // 드래그 아이콘
        dragIcon.transform.SetParent(sourceCanvas.transform, false);
        dragIcon.transform.SetAsLastSibling(); // 최상단
        dragIcon.transform.position = position; // 시작 위치

        Image image = dragIcon.AddComponent<Image>(); // 아이콘 Image
        image.raycastTarget = false; // 입력 통과
        image.sprite = sprite;
        image.color = color;
        image.preserveAspect = true; // 비율 유지

        RectTransform rect = dragIcon.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(50f, 50f); // 아이콘 크기
    }

    private static void ClearDragIconOnly()
    {
        if (dragIcon == null)
            return;

        Destroy(dragIcon); // 아이콘 제거
        dragIcon = null; // 참조 해제
    }

    private static bool TryHandleFallbackDropSlot(DropSlot dropSlot, PointerEventData eventData)
    {
        if (dropSlot == null || !dropSlot.isActiveAndEnabled || dropSlot.Slot == null)
            return false;

        if (EquipmentOrigin != null || QuickOrigin != null)
        {
            SlotUI target = dropSlot.Slot;
            if (EquipmentOrigin != null && target.OwnerBridge is InventorySlotBridge && !target.IsWeaponSlot && !target.IsBagSlot && !target.IsLocked)
                EquipmentOrigin.Unequip(target.SlotIndex);
            MarkDropHandled();
            return true;
        }
        SlotDropContext context = SlotDropContext.Create(dropSlot, eventData); // 슬롯 대체 경로
        if (context == null)
            return false;

        context.Bridge.HandleSlotDrop(context); // 정책 위임
        MarkDropHandled(); // UI 드롭
        return true;
    }

    private static void TryHandleExternalDropFromOrigin(PointerEventData eventData)
    {
        Vector2 screenPosition = eventData != null ? eventData.position : Vector2.zero; // 드롭 위치

        if (OriginSlot != null && OriginSlot.DisplayItem != null && OriginSlot.OwnerBridge != null)
        {
            OriginSlot.OwnerBridge.HandleExternalDrop(OriginSlot, screenPosition); // 슬롯 외부
            return;
        }

    }
}
