using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum SlotDragOverlayState
{
    None,
    WillUnlock,
    WillLock
}

public class SlotUI : MonoBehaviour, IPointerClickHandler // 공통 슬롯
{
    private static readonly Color DefaultSlotBackgroundColor = new Color(0.24f, 0.25f, 0.27f, 0.96f); // 기본 배경
    private static readonly Color LockedSlotBackgroundColor = new Color(0.045f, 0.048f, 0.055f, 0.9f); // 잠금 배경
    private static readonly Color LockedSlotSpriteTintColor = Color.black; // 잠금 슬롯 배경 tint
    private static readonly Color ActiveWeaponSlotBorderColor = new Color(0.35f, 0.72f, 1f, 0.95f); // 장착 테두리

    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI infoText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image slotBackgroundImage;

    private SlotGradeEffect gradeEffect; // 등급 효과
    private SlotFrameAnimator slotFrameAnimator; // 프레임 효과
    private SlotTooltip slotTooltip; // 툴팁
    private DragSlot dragSlot; // 드래그
    private DropSlot dropSlot; // 드롭
    private ISlotInteractionBridge ownerBridge; // 정책 Bridge
    private float lastClickTime; // 더블클릭 시간
    private const float DoubleClickInterval = 0.3f; // 더블클릭 간격
    private Image lockedOverlay; // 잠금 표시
    private Image dragOverlay; // 드래그 표시
    private GameObject activeWeaponBorder; // 장착 표시
    private Color emptyBackgroundColor = DefaultSlotBackgroundColor; // 빈 배경

    private Image contextSelectionOverlay;
    private TextMeshProUGUI newItemMarker; // 신규 획득 표시
    private TextMeshProUGUI stackCountText; // 스택 수 표시

    public int SlotIndex { get; private set; }
    public bool IsWeaponSlot { get; private set; }
    public bool IsBagSlot { get; private set; }
    public bool IsLocked { get; private set; }
    public bool IsActiveWeaponSlot { get; private set; }
    public ItemData DisplayItem { get; private set; }
    public ISlotInteractionBridge OwnerBridge => ownerBridge;

    private void Awake()
    {
        EnsureInitialized();
    }

    public void Init(ISlotInteractionBridge bridge, int slotIndex, bool isWeaponSlot)
    {
        Init(bridge, slotIndex, isWeaponSlot, false);
    }

    public void Init(ISlotInteractionBridge bridge, int slotIndex, bool isWeaponSlot, bool isBagSlot)
    {
        ownerBridge = bridge; // 정책 연결
        SlotIndex = slotIndex; // 슬롯 index
        IsWeaponSlot = isWeaponSlot; // 무기 슬롯
        IsBagSlot = isBagSlot; // 가방 슬롯
        EnsureInitialized();
    }

    public void SetDisplayItem(ItemData item)
    {
        EnsureInitialized();
        if (item == null || !item.HasValidBaseData)
        {
            DisplayItem = null; // 표시 제거
            Clear();
            return;
        }

        DisplayItem = item; // 표시 아이템
        SetIconActive(true);

        if (iconImage != null)
        {
            iconImage.sprite = item.icon;
            iconImage.color = item.icon != null ? item.iconColor : new Color(item.color.r, item.color.g, item.color.b, 0.45f);
            iconImage.preserveAspect = true; // 비율 유지
        }

        if (infoText != null)
            infoText.text = GetInfoText(item); // 보조 정보

        RefreshStackCount(item);

        if (gradeEffect != null)
            gradeEffect.SetGrade(item.grade, GradeConfig.GetGradeColor(item.grade)); // 등급 색

        RefreshBackgroundVisual();

        RefreshActiveWeaponBorder();
    }

    public void SetNewItemMarker(bool visible)
    {
        EnsureInitialized();
        EnsureNewItemMarker();

        if (newItemMarker == null)
            return;

        newItemMarker.gameObject.SetActive(visible && DisplayItem != null && !IsLocked);
        if (newItemMarker.gameObject.activeSelf)
            newItemMarker.transform.SetAsLastSibling(); // 표시 우선

        if (lockedOverlay != null)
            lockedOverlay.transform.SetAsLastSibling(); // 잠금 우선
    }

    public void SetLocked(bool locked)
    {
        EnsureInitialized();
        IsLocked = locked; // 잠금 상태

        if (lockedOverlay != null)
            lockedOverlay.gameObject.SetActive(locked); // 잠금 표시

        RefreshBackgroundVisual();

        if (iconImage != null && locked)
            iconImage.color = Color.clear; // 아이콘 숨김

        if (infoText != null && locked)
            infoText.text = string.Empty; // 텍스트 숨김

        RefreshStackCount(DisplayItem);

        RefreshActiveWeaponBorder();
    }

    public void SetDragOverlay(SlotDragOverlayState state)
    {
        EnsureInitialized();
        EnsureDragOverlay();

        if (dragOverlay == null)
            return;

        dragOverlay.gameObject.SetActive(state != SlotDragOverlayState.None);
        dragOverlay.color = GetDragOverlayColor(state);
    }

    public void ClearDragOverlay()
    {
        if (dragOverlay != null)
            dragOverlay.gameObject.SetActive(false);
    }

    public void SetContextSelected(bool selected)
    {
        EnsureInitialized();
        EnsureContextSelectionOverlay();

        if (contextSelectionOverlay != null)
            contextSelectionOverlay.gameObject.SetActive(selected);
    }

    public void SetActiveWeaponSlot(bool active)
    {
        EnsureInitialized();
        IsActiveWeaponSlot = active; // 장착 상태

        RefreshBackgroundVisual();

        RefreshActiveWeaponBorder();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
        {
            if (DisplayItem == null || ownerBridge == null || IsLocked)
                return;

            SlotClickContext rightClickContext = SlotClickContext.Create(this, eventData, false);
            if (ownerBridge is ISlotRightClickInteractionBridge rightClickBridge && rightClickBridge.HandleSlotRightClick(rightClickContext))
                return;

            InventoryContextMenuController contextMenu = GetComponentInParent<InventoryContextMenuController>();
            if (contextMenu == null)
                contextMenu = Object.FindFirstObjectByType<InventoryContextMenuController>(FindObjectsInactive.Include);

            contextMenu?.OpenForSlot(this, eventData);
            return;
        }

        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;

        if (DisplayItem == null || ownerBridge == null || IsLocked)
            return;

        bool isDoubleClick = Time.unscaledTime - lastClickTime <= DoubleClickInterval; // 더블클릭
        lastClickTime = Time.unscaledTime; // 클릭 시간
        SlotClickContext context = SlotClickContext.Create(this, eventData, isDoubleClick); // 클릭 context

        if (!isDoubleClick && ownerBridge is ISlotSingleClickInteractionBridge singleClickBridge && singleClickBridge.HandleSlotSingleClick(context))
            return;

        if (isDoubleClick)
        {
            context?.Bridge.HandleSlotClick(context);
            TooltipManager.Instance?.HideTooltip(); // 더블클릭 처리 후 잔여 Tooltip 정리
        }
    }

    private void EnsureInitialized()
    {
        if (iconImage == null)
            iconImage = transform.Find("Icon") != null ? transform.Find("Icon").GetComponent<Image>() : null; // 아이콘

        if (infoText == null)
            infoText = FindInfoText(); // 정보 Text

        if (backgroundImage == null)
            backgroundImage = GetComponent<Image>(); // 배경 Image

        if (backgroundImage != null)
            emptyBackgroundColor = DefaultSlotBackgroundColor; // 빈 배경

        EnsureSlotBackgroundImage();
        RefreshBackgroundVisual();

        if (gradeEffect == null)
            gradeEffect = GetComponent<SlotGradeEffect>() ?? gameObject.AddComponent<SlotGradeEffect>(); // 등급 효과

        gradeEffect.Init(iconImage, GetEffectBackgroundImage());

        if (slotTooltip == null)
            slotTooltip = GetComponent<SlotTooltip>() ?? gameObject.AddComponent<SlotTooltip>(); // 툴팁

        slotTooltip.Init(this); // 슬롯 연결

        if (slotFrameAnimator == null)
            slotFrameAnimator = GetComponent<SlotFrameAnimator>() ?? gameObject.AddComponent<SlotFrameAnimator>(); // 프레임 효과

        slotFrameAnimator.targetImage = iconImage; // 효과 대상

        if (dragSlot == null)
            dragSlot = GetComponent<DragSlot>() ?? gameObject.AddComponent<DragSlot>(); // 드래그

        dragSlot.Init(this); // 슬롯 연결

        if (dropSlot == null)
            dropSlot = GetComponent<DropSlot>() ?? gameObject.AddComponent<DropSlot>(); // 드롭

        dropSlot.Init(this); // 슬롯 연결
        EnsureLockOverlay();
        EnsureDragOverlay();
        EnsureContextSelectionOverlay();
        EnsureNewItemMarker();
        EnsureStackCountText();
        EnsureActiveWeaponBorder();
        RefreshActiveWeaponBorder();
    }

    private TextMeshProUGUI FindInfoText()
    {
        TextMeshProUGUI[] textComponents = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < textComponents.Length; i++)
        {
            TextMeshProUGUI candidate = textComponents[i];
            if (candidate == null || candidate.transform == transform)
                continue;

            if (candidate.gameObject.name == "NewItemMarker")
                continue;

            if (candidate.gameObject.name == "StackCountText")
                continue;

            return candidate;
        }

        return null;
    }

    private void Clear()
    {
        SetIconActive(false);

        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.color = Color.clear;
        }

        if (infoText != null)
            infoText.text = IsWeaponSlot ? GetWeaponSlotLabel() : IsBagSlot ? GetBagSlotLabel() : string.Empty; // 빈 라벨

        RefreshStackCount(null);

        if (slotFrameAnimator != null)
            slotFrameAnimator.Stop(); // 효과 정지

        if (gradeEffect != null)
            gradeEffect.Clear(); // 등급 초기화

        if (newItemMarker != null)
            newItemMarker.gameObject.SetActive(false); // 신규 표시 숨김

        RefreshBackgroundVisual();

        RefreshActiveWeaponBorder();
    }

    private string GetInfoText(ItemData item)
    {
        if (IsWeaponSlot)
            return GetWeaponSlotLabel();

        if (IsBagSlot)
            return GetBagSlotLabel();

        if (item.itemType == "Bag" && item.baseData is BagItemData bagData)
            return "Lv" + bagData.level;

        return string.Empty;
    }

    private string GetWeaponSlotLabel()
    {
        return "무기 " + (SlotIndex + 1);
    }

    private string GetBagSlotLabel()
    {
        return "가방";
    }

    private Color GetCurrentBackgroundColor()
    {
        if (HasSeparatedSlotBackground())
            return Color.clear;

        if (HasSpriteBackground())
            return Color.white;

        if (IsLocked)
            return LockedSlotBackgroundColor;

        return emptyBackgroundColor;
    }

    private bool HasSpriteBackground()
    {
        return backgroundImage != null && backgroundImage.sprite != null;
    }

    private bool HasSeparatedSlotBackground()
    {
        return HasSeparatedSlotBackgroundObject() && slotBackgroundImage.sprite != null;
    }

    private Image GetEffectBackgroundImage()
    {
        return HasSeparatedSlotBackgroundObject() ? slotBackgroundImage : backgroundImage;
    }

    private void RefreshBackgroundVisual()
    {
        if (slotBackgroundImage != null)
        {
            slotBackgroundImage.color = IsLocked ? LockedSlotSpriteTintColor : Color.white;
            slotBackgroundImage.raycastTarget = false;
        }

        if (backgroundImage != null)
            backgroundImage.color = GetCurrentBackgroundColor();
    }

    private void EnsureSlotBackgroundImage()
    {
        if (slotBackgroundImage != null && !HasSeparatedSlotBackgroundObject())
            slotBackgroundImage = null;

        if (slotBackgroundImage == null)
            slotBackgroundImage = FindSlotBackgroundImage();

        if (slotBackgroundImage == null)
            return;

        slotBackgroundImage.raycastTarget = false;
        slotBackgroundImage.color = IsLocked ? LockedSlotSpriteTintColor : Color.white;
        slotBackgroundImage.transform.SetAsFirstSibling();

        if (backgroundImage != null && backgroundImage != slotBackgroundImage)
            backgroundImage.raycastTarget = true;
    }

    private bool HasSeparatedSlotBackgroundObject()
    {
        return slotBackgroundImage != null
            && slotBackgroundImage != backgroundImage
            && slotBackgroundImage != iconImage
            && slotBackgroundImage.transform.parent == transform;
    }

    private Image FindSlotBackgroundImage()
    {
        Image namedImage = FindDirectChildImage("SlotBackground");
        if (namedImage != null)
            return namedImage;

        Image legacyImage = FindDirectChildImage("Image");
        if (legacyImage != null)
            return legacyImage;

        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image candidate = images[i];
            if (!IsSlotBackgroundCandidate(candidate))
                continue;

            return candidate;
        }

        return null;
    }

    private Image FindDirectChildImage(string childName)
    {
        Transform child = transform.Find(childName);
        if (child == null || child.parent != transform)
            return null;

        Image image = child.GetComponent<Image>();
        return IsSlotBackgroundCandidate(image) ? image : null;
    }

    private bool IsSlotBackgroundCandidate(Image candidate)
    {
        if (candidate == null || candidate == backgroundImage || candidate == iconImage)
            return false;

        if (candidate.transform.parent != transform)
            return false;

        string objectName = candidate.gameObject.name;
        if (objectName == "Icon" || objectName == "Info" || objectName == "GradeOverlay" || objectName == "ExperimentalGradeOutline"
            || objectName == "LockOverlay" || objectName == "DragStateOverlay" || objectName == "ContextSelectionOverlay"
            || objectName == "ActiveWeaponBorder" || objectName == "NewItemMarker" || objectName == "StackCountText")
            return false;

        return objectName == "SlotBackground" || objectName == "Image" || candidate.sprite != null;
    }

    private void SetIconActive(bool active)
    {
        if (iconImage != null)
            iconImage.gameObject.SetActive(active);
    }

    private void EnsureLockOverlay()
    {
        if (lockedOverlay != null)
            return;

        Transform existing = transform.Find("LockOverlay");
        GameObject overlayObject = existing != null ? existing.gameObject : new GameObject("LockOverlay"); // 잠금 overlay
        overlayObject.transform.SetParent(transform, false);
        overlayObject.transform.SetAsLastSibling(); // 최상단

        lockedOverlay = overlayObject.GetComponent<Image>();
        if (lockedOverlay == null)
            lockedOverlay = overlayObject.AddComponent<Image>(); // Image 보장

        lockedOverlay.color = new Color(0f, 0f, 0f, 0.58f);
        lockedOverlay.raycastTarget = false; // 입력 통과

        RectTransform rect = overlayObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        overlayObject.SetActive(IsLocked); // 잠금 상태
    }

    private void EnsureDragOverlay()
    {
        if (dragOverlay != null)
            return;

        Transform existing = transform.Find("DragStateOverlay");
        GameObject overlayObject = existing != null ? existing.gameObject : new GameObject("DragStateOverlay"); // 드래그 overlay
        overlayObject.transform.SetParent(transform, false);

        dragOverlay = overlayObject.GetComponent<Image>();
        if (dragOverlay == null)
            dragOverlay = overlayObject.AddComponent<Image>(); // Image 보장

        dragOverlay.color = Color.clear;
        dragOverlay.raycastTarget = false; // 입력 통과

        RectTransform rect = overlayObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        overlayObject.SetActive(false); // 기본 숨김
        overlayObject.transform.SetAsLastSibling(); // 최상단

        if (lockedOverlay != null)
            lockedOverlay.transform.SetAsLastSibling(); // 잠금 우선
    }

    private void EnsureActiveWeaponBorder()
    {
        if (activeWeaponBorder != null)
            return;

        Transform existing = transform.Find("ActiveWeaponBorder");
        activeWeaponBorder = existing != null ? existing.gameObject : new GameObject("ActiveWeaponBorder", typeof(RectTransform)); // 장착 border
        activeWeaponBorder.transform.SetParent(transform, false);

        RectTransform rootRect = activeWeaponBorder.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        CreateBorderSegment("Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), Vector2.zero);
        CreateBorderSegment("Bottom", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f));
        CreateBorderSegment("Left", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f));
        CreateBorderSegment("Right", new Vector2(1f, 0f), Vector2.one, new Vector2(-3f, 0f), Vector2.zero);
        activeWeaponBorder.SetActive(false); // 기본 숨김
        activeWeaponBorder.transform.SetAsLastSibling(); // 최상단

        if (lockedOverlay != null)
            lockedOverlay.transform.SetAsLastSibling(); // 잠금 우선
    }

    private void CreateBorderSegment(string segmentName, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        Transform existing = activeWeaponBorder.transform.Find(segmentName);
        GameObject segmentObject = existing != null ? existing.gameObject : new GameObject(segmentName, typeof(RectTransform)); // 테두리 조각
        segmentObject.transform.SetParent(activeWeaponBorder.transform, false);

        Image segmentImage = segmentObject.GetComponent<Image>();
        if (segmentImage == null)
            segmentImage = segmentObject.AddComponent<Image>(); // Image 보장

        segmentImage.color = ActiveWeaponSlotBorderColor;
        segmentImage.raycastTarget = false; // 입력 통과

        RectTransform rect = segmentObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private void EnsureContextSelectionOverlay()
    {
        if (contextSelectionOverlay != null)
            return;

        Transform existing = transform.Find("ContextSelectionOverlay");
        GameObject overlayObject = existing != null ? existing.gameObject : new GameObject("ContextSelectionOverlay", typeof(RectTransform));
        overlayObject.transform.SetParent(transform, false);

        contextSelectionOverlay = overlayObject.GetComponent<Image>();
        if (contextSelectionOverlay == null)
            contextSelectionOverlay = overlayObject.AddComponent<Image>();

        contextSelectionOverlay.color = new Color(1f, 0.72f, 0.18f, 0.28f);
        contextSelectionOverlay.raycastTarget = false;

        RectTransform rect = overlayObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        overlayObject.SetActive(false);
        overlayObject.transform.SetAsLastSibling();
        if (lockedOverlay != null)
            lockedOverlay.transform.SetAsLastSibling();
    }

    private void EnsureNewItemMarker()
    {
        if (newItemMarker != null)
            return;

        Transform existing = transform.Find("NewItemMarker");
        GameObject markerObject = existing != null ? existing.gameObject : new GameObject("NewItemMarker", typeof(RectTransform));
        markerObject.transform.SetParent(transform, false);

        newItemMarker = markerObject.GetComponent<TextMeshProUGUI>();
        if (newItemMarker == null)
            newItemMarker = markerObject.AddComponent<TextMeshProUGUI>(); // 전용 아이콘 도입 전 임시 텍스트

        newItemMarker.text = "●";
        newItemMarker.color = new Color(0.15f, 1f, 0.28f, 0.95f);
        newItemMarker.fontSize = 16f;
        newItemMarker.alignment = TextAlignmentOptions.Center;
        newItemMarker.raycastTarget = false;

        RectTransform rect = markerObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-4f, 3f);
        rect.sizeDelta = new Vector2(18f, 18f);

        markerObject.SetActive(false);
        markerObject.transform.SetAsLastSibling();
        if (lockedOverlay != null)
            lockedOverlay.transform.SetAsLastSibling();
    }

    private void EnsureStackCountText()
    {
        if (stackCountText != null)
            return;

        Transform existing = transform.Find("StackCountText");
        GameObject stackObject = existing != null ? existing.gameObject : new GameObject("StackCountText", typeof(RectTransform));
        stackObject.transform.SetParent(transform, false);

        stackCountText = stackObject.GetComponent<TextMeshProUGUI>();
        if (stackCountText == null)
            stackCountText = stackObject.AddComponent<TextMeshProUGUI>();

        if (infoText != null && infoText.font != null)
        {
            stackCountText.font = infoText.font;
            stackCountText.fontSharedMaterial = infoText.fontSharedMaterial;
        }
        else if (TMP_Settings.defaultFontAsset != null)
        {
            stackCountText.font = TMP_Settings.defaultFontAsset;
            stackCountText.fontSharedMaterial = TMP_Settings.defaultFontAsset.material;
        }

        stackCountText.text = string.Empty;
        stackCountText.color = Color.white;
        stackCountText.fontSize = 14f;
        stackCountText.fontStyle = FontStyles.Bold;
        stackCountText.alignment = TextAlignmentOptions.BottomLeft;
        stackCountText.raycastTarget = false;

        if (stackCountText.fontSharedMaterial != null)
        {
            stackCountText.outlineColor = new Color(0f, 0f, 0f, 0.85f);
            stackCountText.outlineWidth = 0.22f;
        }

        RectTransform rect = stackObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(4f, 2f);
        rect.sizeDelta = new Vector2(42f, 18f);

        stackObject.SetActive(false);
        stackObject.transform.SetAsLastSibling();
        if (lockedOverlay != null)
            lockedOverlay.transform.SetAsLastSibling();
    }

    private void RefreshStackCount(ItemData item)
    {
        EnsureStackCountText();

        if (stackCountText == null)
            return;

        bool visible = item != null && item.ShouldDisplayStackCount && !IsLocked;
        stackCountText.gameObject.SetActive(visible);
        stackCountText.text = visible ? "x" + item.stackCount : string.Empty;

        if (visible)
            stackCountText.transform.SetAsLastSibling(); // 아이콘 위 표시

        if (newItemMarker != null && newItemMarker.gameObject.activeSelf)
            newItemMarker.transform.SetAsLastSibling(); // 신규 표시 우선

        if (lockedOverlay != null)
            lockedOverlay.transform.SetAsLastSibling(); // 잠금 우선
    }

    private void RefreshActiveWeaponBorder()
    {
        if (activeWeaponBorder == null)
            return;

        activeWeaponBorder.SetActive(IsWeaponSlot && IsActiveWeaponSlot && !IsLocked); // 장착 표시

        if (activeWeaponBorder.activeSelf)
            activeWeaponBorder.transform.SetAsLastSibling(); // 최상단

        if (lockedOverlay != null)
            lockedOverlay.transform.SetAsLastSibling(); // 잠금 우선
    }

    private Color GetDragOverlayColor(SlotDragOverlayState state)
    {
        switch (state)
        {
            case SlotDragOverlayState.WillUnlock:
                return new Color(0.08f, 1f, 0.25f, 0.32f);
            case SlotDragOverlayState.WillLock:
                return new Color(1f, 0.08f, 0.08f, 0.42f);
            default:
                return Color.clear;
        }
    }
}
