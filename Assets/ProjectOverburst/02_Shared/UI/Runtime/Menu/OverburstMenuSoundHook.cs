using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 2026-10-01 ESC 메뉴 버튼·슬라이더·키 칸에 붙는 공통 반응: 마우스를 올리거나 방향키로 고르면 작은 소리를 내고,
/// 스크롤 목록 안에서 방향키로 고른 칸이 가려져 있으면 보이는 위치까지 목록을 옮긴다.
/// </summary>
public sealed class OverburstMenuSoundHook : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    private Selectable selectable;

    private void Awake() => selectable = GetComponent<Selectable>();

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (selectable == null || selectable.IsInteractable()) OverburstGameMenu.Instance?.PlayHover();
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (eventData is PointerEventData) return;
        OverburstGameMenu.Instance?.PlayHover();
        ScrollIntoView();
    }

    private void ScrollIntoView()
    {
        var scroll = GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        var item = (RectTransform)transform;
        var viewport = scroll.viewport;
        Vector3[] corners = new Vector3[4];
        item.GetWorldCorners(corners);
        Vector3 top = viewport.InverseTransformPoint(corners[1]);
        Vector3 bottom = viewport.InverseTransformPoint(corners[0]);
        Rect view = viewport.rect;
        float shift = 0f;
        if (top.y > view.yMax) shift = top.y - view.yMax;
        else if (bottom.y < view.yMin) shift = bottom.y - view.yMin;
        if (Mathf.Approximately(shift, 0f)) return;
        // viewport 로컬 단위를 content 로컬 단위로 바꿔 옮긴다.
        float scale = scroll.content.lossyScale.y / Mathf.Max(.0001f, viewport.lossyScale.y);
        var position = scroll.content.anchoredPosition;
        position.y -= shift / Mathf.Max(.0001f, scale) - (shift > 0f ? -24f : 24f);
        scroll.content.anchoredPosition = position;
        scroll.StopMovement();
    }
}
