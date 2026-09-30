using UnityEngine;
using UnityEngine.EventSystems;

// 경험치 바 마우스 오버 설명창. HUD 프리팹이 원본 Demo_XPTooltip·UIProgressBar를 뺀 뒤(채움은 OverburstGameUI가 직접 계산)
// 설명창을 켜는 부분만 없어졌다. 원본과 같은 방식으로 바에 올리면 채움 끝 위에 "Tooltip" 자식을 띄운다.
// 글자(경험치 / 필요량, 퍼센트)는 OverburstGameUI.Refresh가 채운다.
[DisallowMultipleComponent]
public sealed class ExperienceBarTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float ShowDelay = .3f;
    private const float OffsetY = -20f; // 원본 Demo_XPTooltip 값

    private RectTransform tooltip;
    private RectTransform track;
    private CanvasGroup tooltipGroup;
    private bool hovering, shown;
    private float showAt;

    public bool IsShown => shown;

    public void Configure(RectTransform tooltipRect, RectTransform trackRect)
    {
        tooltip = tooltipRect; track = trackRect;
        if (tooltip == null) return;
        tooltip.anchorMin = tooltip.anchorMax = new Vector2(0f, 1f);
        tooltip.pivot = new Vector2(.5f, 0f);
        // 설명창이 바 위에 겹쳐도 마우스 판정을 가로채지 않게 해서 깜빡임을 막는다.
        tooltipGroup = tooltip.GetComponent<CanvasGroup>();
        if (tooltipGroup == null) tooltipGroup = tooltip.gameObject.AddComponent<CanvasGroup>();
        tooltipGroup.blocksRaycasts = false; tooltipGroup.interactable = false;
        tooltip.gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData) { hovering = true; showAt = Time.unscaledTime + ShowDelay; }
    public void OnPointerExit(PointerEventData eventData) { hovering = false; Hide(); }
    private void OnDisable() { hovering = false; Hide(); }

    private void LateUpdate()
    {
        if (!hovering || tooltip == null) return;
        if (!shown && Time.unscaledTime >= showAt) { shown = true; tooltip.gameObject.SetActive(true); }
        if (shown) Position();
    }

    private void Hide()
    {
        shown = false;
        if (tooltip != null) tooltip.gameObject.SetActive(false);
    }

    // 채움 끝을 따라가되 바 밖으로 나가지 않게 한다.
    private void Position()
    {
        if (track == null) return;
        var progression = PlayerProgression.Current;
        float fill = progression != null ? progression.ExperienceProgress : 0f;
        Transform parent = tooltip.parent;
        tooltip.SetParent(track, true);
        float half = tooltip.rect.width * .5f;
        float x = Mathf.Clamp(track.rect.width * fill, half, Mathf.Max(half, track.rect.width - half));
        tooltip.anchoredPosition = new Vector2(x, OffsetY);
        tooltip.SetParent(parent, true);
        tooltip.SetAsLastSibling();
    }
}
