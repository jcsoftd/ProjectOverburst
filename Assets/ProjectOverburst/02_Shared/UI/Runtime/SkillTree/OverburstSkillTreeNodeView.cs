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
        bool learned = state == NodeState.Learned;
        frame.color = learned ? new Color(1,.80f,.35f) : state == NodeState.Guide ? new Color(.55f,.47f,.30f) : state == NodeState.PurchaseDraft ? new Color(.39f,.43f,.46f) : state == NodeState.RefundDraft ? new Color(.44f,.24f,.20f) : state == NodeState.Available ? new Color(.29f,.32f,.35f) : state == NodeState.Reserved ? new Color(.25f,.25f,.30f) : new Color(.21f,.23f,.26f);
        icon.color = learned ? new Color(1,.94f,.68f) : state == NodeState.Guide ? new Color(.83f,.72f,.45f) : state == NodeState.PurchaseDraft ? new Color(.95f,.97f,1) : state == NodeState.Reserved ? new Color(.47f,.47f,.55f) : state == NodeState.Available ? new Color(.78f,.83f,.88f) : state == NodeState.RefundDraft ? new Color(.95f,.52f,.37f) : state == NodeState.Insufficient ? new Color(.54f,.59f,.64f) : new Color(.38f,.42f,.47f);
        if (innerBorder) innerBorder.color = frame.color;
        selection.gameObject.SetActive(false);
        Accent?.Present(state, selected, frame.type == Image.Type.Sliced);
        if (caption) caption.color = StatusColor(state);
        if (stateMark)
        {
            stateMark.text = state == NodeState.Learned ? "✓" : state == NodeState.PurchaseDraft ? "+" : state == NodeState.RefundDraft ? "−" : state == NodeState.Available ? "○" : state == NodeState.Insufficient ? "!" : state == NodeState.Locked ? "×" : state == NodeState.Reserved ? "…" : "";
            stateMark.color = state == NodeState.Insufficient ? new Color(.88f,.69f,.34f) : StatusColor(state); statePlate.color = new Color(.035f,.04f,.05f,.98f); statePlate.gameObject.SetActive(state != NodeState.Guide);
        }
    }
    public static Color StatusColor(NodeState state) => state == NodeState.Learned ? new Color(1,.83f,.40f) : state == NodeState.Guide ? new Color(.80f,.70f,.48f) : state == NodeState.PurchaseDraft ? new Color(.92f,.95f,1) : state == NodeState.RefundDraft ? new Color(.95f,.48f,.34f) : state == NodeState.Available ? new Color(.80f,.84f,.88f) : state == NodeState.Reserved ? new Color(.58f,.58f,.66f) : new Color(.55f,.60f,.65f);
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
