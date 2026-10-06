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
    OverburstSkillTreeUI owner;
    public void Bind(OverburstSkillTreeUI view)
    {
        if (owner != null) button.onClick.RemoveListener(Click);
        owner = view; button.onClick.AddListener(Click);
    }
    void Click() { owner.HideTooltip(); owner.SelectNode(nodeId, true); }
    public enum NodeState { Guide, Locked, Insufficient, Available, Learned, PurchaseDraft, RefundDraft, Reserved }
    public NodeState State { get; private set; }
    public Text stateMark;
    public Image statePlate;
    public void Present(NodeState state, bool selected)
    {
        State = state;
        bool learned = state == NodeState.Learned || state == NodeState.Guide;
        frame.color = learned ? new Color(.94f,.76f,.40f) : state == NodeState.PurchaseDraft ? new Color(.97f,.93f,.82f) : state == NodeState.RefundDraft ? new Color(.75f,.35f,.24f) : state == NodeState.Available ? new Color(.72f,.67f,.56f) : state == NodeState.Insufficient ? new Color(.57f,.52f,.43f) : state == NodeState.Reserved ? new Color(.51f,.47f,.54f) : new Color(.36f,.34f,.30f);
        icon.color = learned ? new Color(1,.91f,.70f) : state == NodeState.PurchaseDraft ? new Color(1,.97f,.88f) : state == NodeState.Reserved ? new Color(.69f,.64f,.73f) : state == NodeState.Available ? new Color(.87f,.82f,.71f) : state == NodeState.RefundDraft ? new Color(.84f,.53f,.40f) : new Color(.52f,.50f,.44f);
        selection.gameObject.SetActive(selected);
        if (caption) caption.color = selected ? new Color(.96f,.80f,.49f) : learned ? new Color(.88f,.77f,.55f) : state == NodeState.Reserved ? new Color(.68f,.64f,.71f) : new Color(.73f,.69f,.60f);
        if (stateMark)
        {
            stateMark.text = state == NodeState.Learned ? "✓" : state == NodeState.PurchaseDraft ? "+" : state == NodeState.RefundDraft ? "−" : state == NodeState.Available ? "○" : state == NodeState.Insufficient ? "0" : state == NodeState.Locked ? "×" : state == NodeState.Reserved ? "…" : "";
            stateMark.color = icon.color; statePlate.gameObject.SetActive(state != NodeState.Guide);
        }
    }
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
