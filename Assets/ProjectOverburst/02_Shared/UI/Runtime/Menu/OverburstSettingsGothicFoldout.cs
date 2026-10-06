using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class OverburstSettingsGothicFoldout : MonoBehaviour
{
    public OverburstSettingsGothicView owner;
    public Button button;
    public TMP_Text arrow;
    public RectTransform body;
    public LayoutElement element;
    public float headerHeight = 72f;
    public bool expanded;

    void Awake() { button.onClick.AddListener(() => { SetExpanded(!expanded); owner.PlayClick(); }); }
    public void SetExpanded(bool value)
    {
        bool returnFocus = !value && EventSystem.current && EventSystem.current.currentSelectedGameObject
            && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(body);
        expanded = value; body.gameObject.SetActive(value); arrow.text = value ? "-" : "+";
        Recalculate(); owner.RequestLayout();
        if (returnFocus) EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
    public void Recalculate()
    {
        if (expanded) LayoutRebuilder.ForceRebuildLayoutImmediate(body);
        element.preferredHeight = headerHeight + (expanded ? LayoutUtility.GetPreferredHeight(body) : 0f);
        element.minHeight = element.preferredHeight;
    }
}
