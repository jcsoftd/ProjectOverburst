using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class WorldItemNameplateRowView : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [SerializeField] private RectTransform rootRect;
    [SerializeField] private Image background;
    [SerializeField] private Image hoverHighlight;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image pendingMarker;
    [SerializeField] private Image gradeFrame;
    [SerializeField] private CanvasGroup canvasGroup;

    private WorldItemNameplateBridge bridge;
    private WorldItemPickup pickup;
    private bool clickable;
    private bool pointerHovered;
    private bool modelHovered;
    private Color restingBackgroundColor;

    public RectTransform RootRect => rootRect;
    public Image Background => background;
    public Image HoverHighlight => hoverHighlight;
    public TMP_Text Label => label;
    public Image PendingMarker => pendingMarker;
    public Image GradeFrame => gradeFrame;
    public CanvasGroup CanvasGroup => canvasGroup;
    public bool IsPointerHovered => pointerHovered;
    public bool IsModelHovered => modelHovered;

    public void BindBridge(WorldItemNameplateBridge value)
    {
        bridge = value;
    }

    public void Show(
        WorldItemNameplateLayoutCandidate candidate,
        Vector2 anchoredPosition,
        bool allowPointerInput)
    {
        bool preservePointerHover = pointerHovered
            && pickup != null
            && pickup == candidate.Pickup
            && allowPointerInput;
        if (!preservePointerHover && pointerHovered && pickup != null)
            bridge?.HandlePointerExit(pickup); // pooled row 대상 교체 전 override 해제

        pickup = candidate.Pickup;
        clickable = allowPointerInput && pickup != null;
        pointerHovered = preservePointerHover;
        modelHovered = candidate.IsHoverEmphasized;
        if (rootRect != null)
        {
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = candidate.Size; // 내용 폭만큼 compact 크기 적용
        }

        Color gradeColor = ResolveLabelColor(candidate.Grade);
        if (label != null)
        {
            label.text = candidate.DisplayText;
            label.color = gradeColor;
            label.raycastTarget = false;
        }

        if (background != null)
        {
            restingBackgroundColor = ResolveRestingBackgroundColor(
                candidate.Grade,
                candidate.IsPendingAutoMove,
                candidate.IsSelected,
                candidate.IsTemporaryHover,
                candidate.IsWithinPickupRange);
            ApplyBackgroundColor();
            background.raycastTarget = clickable;
        }

        ApplyGradeVisuals(candidate.Grade);

        if (pendingMarker != null)
        {
            pendingMarker.gameObject.SetActive(candidate.IsPendingAutoMove);
            pendingMarker.raycastTarget = false;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = clickable;
            canvasGroup.blocksRaycasts = clickable;
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (pointerHovered && pickup != null)
            bridge?.HandlePointerExit(pickup); // 비활성화로 PointerExit이 생략돼도 정리

        pickup = null;
        clickable = false;
        pointerHovered = false;
        modelHovered = false;
        if (gradeFrame != null)
            gradeFrame.gameObject.SetActive(false);
        if (background != null)
        {
            ApplyBackgroundColor();
            background.raycastTarget = false;
        }
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!clickable)
            return;

        pointerHovered = true;
        ApplyBackgroundColor();
        bridge?.HandlePointerEnter(pickup);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (pointerHovered && pickup != null)
            bridge?.HandlePointerExit(pickup);

        pointerHovered = false;
        ApplyBackgroundColor();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!clickable
            || pickup == null
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        bridge?.HandlePointerDown(pickup, eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!clickable
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        bridge?.HandlePointerReleased(eventData);
    }

    private void ApplyBackgroundColor()
    {
        if (background != null)
            background.color = restingBackgroundColor;

        if (hoverHighlight != null)
        {
            hoverHighlight.raycastTarget = false;
            hoverHighlight.gameObject.SetActive(pointerHovered || modelHovered);
        }
    }

    private void ApplyGradeVisuals(ItemGrade grade)
    {
        if (gradeFrame != null)
        {
            gradeFrame.color = ResolveGradeFrameColor(grade);
            gradeFrame.raycastTarget = false;
            gradeFrame.gameObject.SetActive(ShouldShowGradeFrame(grade));
        }
    }

    public static Color ResolveReadableTextColor(Color source)
    {
        source.a = 1f;
        float luminance = source.r * 0.2126f + source.g * 0.7152f + source.b * 0.0722f;
        const float minimumLuminance = 0.58f;
        if (luminance >= minimumLuminance)
            return source;

        float blend = Mathf.Clamp01((minimumLuminance - luminance) / Mathf.Max(0.001f, 1f - luminance));
        Color readable = Color.Lerp(source, Color.white, blend);
        readable.a = 1f;
        return readable;
    }

    public static Color ResolveLabelColor(ItemGrade grade)
    {
        return grade == ItemGrade.Artifact || grade == ItemGrade.Mythic
            ? Color.white
            : ResolveReadableTextColor(GradeConfig.GetGradeColor(grade));
    }

    public static Color ResolveRestingBackgroundColor(
        ItemGrade grade,
        bool isPendingAutoMove,
        bool isSelected,
        bool isTemporaryHover,
        bool isWithinPickupRange)
    {
        Color neutral = isPendingAutoMove
            ? new Color(0.11f, 0.14f, 0.15f, 0.85f)
            : isSelected
                ? new Color(0.14f, 0.14f, 0.15f, 0.85f)
                : isTemporaryHover
                    ? new Color(0.12f, 0.12f, 0.13f, 0.85f)
                    : isWithinPickupRange
                        ? new Color(0.075f, 0.08f, 0.09f, 0.85f)
                        : new Color(0.055f, 0.06f, 0.07f, 0.85f);

        if (grade != ItemGrade.Artifact && grade != ItemGrade.Mythic)
            return neutral;

        Color gradeColor = GradeConfig.GetGradeColor(grade);
        float gradeStrength = grade == ItemGrade.Artifact ? 0.48f : 0.56f;
        Color resolved = Color.Lerp(Color.black, gradeColor, gradeStrength);

        Color stateAccent = isPendingAutoMove
            ? new Color(0.20f, 0.82f, 1f, 1f)
            : isSelected || isTemporaryHover
                ? new Color(1f, 0.60f, 0.12f, 1f)
                : resolved;
        float stateBlend = isPendingAutoMove ? 0.08f : isSelected || isTemporaryHover ? 0.06f : 0f;
        resolved = Color.Lerp(resolved, stateAccent, stateBlend);
        resolved.a = isPendingAutoMove
            ? 0.96f
            : isSelected || isTemporaryHover
                ? 0.94f
                : isWithinPickupRange ? 0.92f : 0.90f;
        return resolved;
    }

    public static bool ShouldShowGradeFrame(ItemGrade grade)
    {
        return grade == ItemGrade.Common
            || grade == ItemGrade.Uncommon
            || grade == ItemGrade.Rare
            || grade == ItemGrade.Epic
            || grade == ItemGrade.Legendary
            || grade == ItemGrade.Artifact
            || grade == ItemGrade.Mythic;
    }

    public static Color ResolveGradeFrameColor(ItemGrade grade)
    {
        if (grade == ItemGrade.Artifact || grade == ItemGrade.Mythic)
            return new Color(1f, 1f, 1f, 0.94f);
        if (!ShouldShowGradeFrame(grade))
            return Color.clear;

        Color color = ResolveReadableTextColor(GradeConfig.GetGradeColor(grade));
        color.a = 0.9f;
        return color;
    }
}
