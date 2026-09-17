using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler // 슬롯 드래그
{
    private static GameObject dragIcon; // 드래그 아이콘
    private static ISlotInteractionBridge originBridge; // 출발 Bridge
    public static ItemData DraggedItem { get; private set; }
    public static bool IsDragging => DraggedItem != null;
    public static EquippedComboGemInventoryDropSource EquippedComboGemSource { get; private set; }

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
        if (slotUI == null || slotUI.DisplayItem == null || slotUI.IsLocked)
            return;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(); // 캔버스 루트

        if (canvas == null)
            return;

        OriginSlot = slotUI; // 출발 슬롯
        DraggedItem = slotUI.DisplayItem; // 드래그 아이템
        EquippedComboGemSource = null; // 일반 슬롯 출처
        dropHandled = false; // 드롭 초기화
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
        EquippedComboGemSource = null; // 일반 외부 출처
        dropHandled = false; // 드롭 초기화
        originBridge = originSlot.OwnerBridge; // 정책 Bridge
        originBridge?.BeginDragPreview(originSlot); // 미리보기 시작

        if (TooltipManager.Instance != null)
            TooltipManager.Instance.HideTooltip(); // Tooltip 정리

        CreateDragIcon(sourceCanvas, item, position);
        return true;
    }

    public static bool BeginEquippedComboGemDrag(
        EquippedComboGemInventoryDropSource source,
        Canvas sourceCanvas,
        Vector2 position)
    {
        if (source == null || !source.MatchesCurrentSource() || sourceCanvas == null)
            return false;

        OriginSlot = null; // 팝업 슬롯은 SlotUI가 아님
        DraggedItem = source.Gem;
        EquippedComboGemSource = source;
        dropHandled = false;
        originBridge = null;

        if (TooltipManager.Instance != null)
            TooltipManager.Instance.HideTooltip();

        CreateDragIcon(sourceCanvas, source.Gem, position);
        return true;
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

    public static void RegisterDropTarget(DropSlot dropSlot)
    {
        if (dropSlot == null)
            return;

        pendingDropSlot = dropSlot; // 명시 슬롯
    }

    public static void CompleteDrag(PointerEventData eventData)
    {
        if (!IsDragging)
        {
            ClearDragState();
            return;
        }

        bool handled = dropHandled || TryHandleUiDrop(eventData); // UI 우선
        if (!handled && !IsPointerOverAnyUi(eventData))
            TryHandleExternalDropFromOrigin(eventData); // UI 밖

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

            DropSlot dropSlot = target.GetComponentInParent<DropSlot>();
            if (TryHandleFallbackDropSlot(dropSlot, eventData))
                return true;
        }

        return false;
    }

    public static void ClearDragState()
    {
        OriginSlot = null; // 출발 해제
        DraggedItem = null; // 아이템 해제
        EquippedComboGemSource = null; // 장착 보석 출처 해제
        dropHandled = false; // 드롭 초기화
        pendingDropSlot = null; // 대상 해제
        originBridge?.ClearDragPreview(); // 미리보기 정리
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
        ClearDragIconOnly();

        dragIcon = new GameObject("DragIcon"); // 드래그 아이콘
        dragIcon.transform.SetParent(sourceCanvas.transform, false);
        dragIcon.transform.SetAsLastSibling(); // 최상단
        dragIcon.transform.position = position; // 시작 위치

        Image image = dragIcon.AddComponent<Image>(); // 아이콘 Image
        image.raycastTarget = false; // 입력 통과
        image.sprite = item.icon;
        image.color = item.icon != null ? item.iconColor : new Color(item.color.r, item.color.g, item.color.b, 0.65f);
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
