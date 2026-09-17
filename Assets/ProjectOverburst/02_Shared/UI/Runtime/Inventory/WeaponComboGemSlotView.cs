using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class WeaponComboGemSlotView : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private Image inputImage;
    [SerializeField] private RectTransform nativeSlotRoot;
    [SerializeField] private Image iconImage;
    [SerializeField] private SlotGradeEffect gradeEffect;
    [SerializeField] private GameObject lockOverlay;
    [SerializeField] private Image dragOverlay;
    [SerializeField] private TextMeshProUGUI roleText;

    private string attackId;
    private int slotIndex = -1;
    private bool isUnlocked;
    private bool isOccupied;
    private string gemRuntimeInstanceId;
    private bool ownsEquippedGemDrag;

    public event Action<WeaponComboGemSlotDropIntent> Dropped;
    public event Action<WeaponComboGemInstalledDragIntent> InstalledDragRequested;

    public RectTransform NativeSlotRoot => nativeSlotRoot;

    public void Bind(string boundAttackId, WeaponComboGemSlotViewData data)
    {
        if (data == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        attackId = boundAttackId ?? string.Empty;
        slotIndex = data.SlotIndex;
        isUnlocked = data.IsUnlocked;
        isOccupied = data.IsOccupied;
        gemRuntimeInstanceId = data.GemRuntimeInstanceId;

        if (inputImage != null)
            inputImage.raycastTarget = true;
        if (lockOverlay != null)
            lockOverlay.SetActive(!isUnlocked);
        if (roleText != null)
            roleText.text = !isUnlocked ? "잠금" : data.Role == WeaponComboGemSlotRole.Element ? "원소" : "연계·강화";

        bool showGem = isUnlocked && data.IsOccupied;
        if (iconImage != null)
        {
            iconImage.sprite = showGem ? data.GemIcon : null;
            iconImage.color = showGem && data.GemIcon != null ? data.GemIconColor : Color.clear;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        if (gradeEffect != null)
        {
            if (showGem)
                gradeEffect.SetGrade(data.GemGrade, GradeConfig.GetGradeColor(data.GemGrade));
            else
                gradeEffect.Clear();
        }
        SetDragPreview(false, false);
    }

    public void Clear()
    {
        attackId = string.Empty;
        slotIndex = -1;
        isUnlocked = false;
        isOccupied = false;
        gemRuntimeInstanceId = string.Empty;
        ownsEquippedGemDrag = false;
        gradeEffect?.Clear();
        gameObject.SetActive(false);
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (!DragSlot.IsDragging)
            return;
        if (DragSlot.EquippedComboGemSource != null)
        {
            DragSlot.MarkDropHandled(); // 같은 콤보 내부 이동은 지원하지 않고 원본 상태를 보존한다.
            SetDragPreview(false, false);
            return;
        }
        DragSlot.MarkDropHandled(); // 일반 인벤토리 이동과 월드 드롭 차단
        SetDragPreview(false, false);
        Dropped?.Invoke(new WeaponComboGemSlotDropIntent(CreateTargetIntent(), DragSlot.OriginSlot));
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!DragSlot.IsDragging)
            return;
        SetDragPreview(true, DragSlot.EquippedComboGemSource == null && CanAcceptDraggedGem(DragSlot.DraggedItem));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetDragPreview(false, false);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData == null || !isUnlocked || !isOccupied || string.IsNullOrEmpty(gemRuntimeInstanceId))
            return;

        InstalledDragRequested?.Invoke(new WeaponComboGemInstalledDragIntent(
            this,
            attackId,
            slotIndex,
            gemRuntimeInstanceId,
            eventData.position));
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (ownsEquippedGemDrag && eventData != null)
            DragSlot.MoveExternalDrag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!ownsEquippedGemDrag)
            return;

        DragSlot.CompleteDrag(eventData);
        ownsEquippedGemDrag = false;
    }

    public bool BeginEquippedGemDrag(EquippedComboGemInventoryDropSource source, Vector2 screenPosition)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        ownsEquippedGemDrag = DragSlot.BeginEquippedComboGemDrag(source, canvas, screenPosition);
        return ownsEquippedGemDrag;
    }

    private void OnDisable()
    {
        if (ownsEquippedGemDrag)
            DragSlot.ClearDragState();
        ownsEquippedGemDrag = false;
        SetDragPreview(false, false);
    }

    private WeaponComboGemSlotTargetIntent CreateTargetIntent()
    {
        return new WeaponComboGemSlotTargetIntent(attackId, slotIndex, isUnlocked, isOccupied);
    }

    private bool CanAcceptDraggedGem(ItemData item)
    {
        if (!isUnlocked || !(item?.baseData is ComboGemItemData gemData))
            return false;
        return WeaponComboGemSlotRules.TryGetClassifiedType(gemData, out ComboGemType gemType)
            && WeaponComboGemSlotRules.IsGemTypeAllowed(slotIndex, gemType);
    }

    private void SetDragPreview(bool visible, bool valid)
    {
        if (dragOverlay == null)
            return;
        dragOverlay.gameObject.SetActive(visible);
        dragOverlay.color = valid
            ? new Color(0.08f, 1f, 0.25f, 0.32f)
            : new Color(1f, 0.08f, 0.08f, 0.42f);
    }
}
