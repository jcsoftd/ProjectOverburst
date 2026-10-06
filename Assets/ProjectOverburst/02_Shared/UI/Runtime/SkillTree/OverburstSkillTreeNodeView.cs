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
    public void Present(bool learned, bool selected, bool available, bool committed = false)
    {
        frame.color = learned ? committed ? new Color(.87f, .73f, .47f) : new Color(.93f, .89f, .77f) : committed ? new Color(.65f, .37f, .27f) : new Color(.59f, .54f, .45f);
        icon.color = learned ? new Color(.98f, .88f, .66f) : available ? new Color(.86f, .80f, .68f) : new Color(.68f, .64f, .56f);
        selection.gameObject.SetActive(selected); if (caption) caption.color = selected ? new Color(.96f, .80f, .49f) : new Color(.73f, .69f, .60f);
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
