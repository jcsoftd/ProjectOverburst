using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class OverburstSkillTreeNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler, IMoveHandler
{
    public string nodeId;
    public Button button;
    public Image frame, icon, selection;
    public Text caption;
    public RectTransform captionRect;
    public Vector2 captionOffset;
    public OverburstSkillTreeNodeAccent Accent { get; private set; }
    OverburstSkillTreeUI owner;
    Image innerBorder;
    public void Bind(OverburstSkillTreeUI view)
    {
        if (owner != null) button.onClick.RemoveListener(Click);
        owner = view; button.onClick.AddListener(Click);
        var inner = transform.Find("Keystone Inner Border"); innerBorder = inner ? inner.GetComponent<Image>() : null;
        if (!Accent)
        {
            var go = new GameObject("State Accent", typeof(RectTransform), typeof(OverburstSkillTreeNodeAccent)); go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform; rect.SetParent(transform, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            Accent = go.GetComponent<OverburstSkillTreeNodeAccent>(); Accent.raycastTarget = false;
            rect.SetSiblingIndex(icon.transform.GetSiblingIndex());
        }
    }
    void Click() { owner.HideTooltip(); owner.SelectNode(nodeId, true); }
    public enum NodeState { Guide, Locked, Insufficient, Available, Learned, PurchaseDraft, RefundDraft, Reserved }
    public NodeState State { get; private set; }
    public Text stateMark;
    public Image statePlate;
    public void Present(NodeState state, bool selected)
    {
        State = state;
        frame.color = OverburstSkillTreePalette.Frame(state);
        icon.color = OverburstSkillTreePalette.Icon(state);
        if (innerBorder) innerBorder.color = frame.color;
        selection.gameObject.SetActive(false);
        Accent?.Present(state, selected, frame.type == Image.Type.Sliced);
        if (caption) caption.color = StatusColor(state);
        if (stateMark)
        {
            stateMark.text = state == NodeState.Learned ? "✓" : state == NodeState.PurchaseDraft ? "+" : state == NodeState.RefundDraft ? "−" : state == NodeState.Available ? "○" : state == NodeState.Insufficient ? "!" : state == NodeState.Locked ? "×" : state == NodeState.Reserved ? "…" : "";
            stateMark.color = state == NodeState.Insufficient ? OverburstSkillTreePalette.Brass : StatusColor(state); statePlate.color = OverburstSkillTreePalette.Badge; statePlate.gameObject.SetActive(state != NodeState.Guide);
        }
    }
    public static Color StatusColor(NodeState state) => OverburstSkillTreePalette.Caption(state);
    public static string StatusName(NodeState state) => state == NodeState.Learned ? "습득 완료" : state == NodeState.Guide ? "기본 경로" : state == NodeState.PurchaseDraft ? "습득 예정 · 적용 필요" : state == NodeState.RefundDraft ? "환급 예정 · 적용 필요" : state == NodeState.Available ? "습득 가능" : state == NodeState.Insufficient ? "포인트 부족" : state == NodeState.Reserved ? "확장 예정" : "미습득 · 연결 잠김";
    public void OnPointerEnter(PointerEventData e) => owner?.ShowTooltip(nodeId);
    public void OnPointerExit(PointerEventData e) => owner?.HideTooltip();
    public void OnSelect(BaseEventData e) { owner?.SelectNode(nodeId, true); owner?.ShowTooltip(nodeId); }
    public void OnDeselect(BaseEventData e) => owner?.HideTooltip();
    public void OnMove(AxisEventData e)
    {
        if (owner == null || e.moveDir == MoveDirection.None) return;
        owner.MoveSelection(nodeId, e.moveVector); e.Use();
    }
}
