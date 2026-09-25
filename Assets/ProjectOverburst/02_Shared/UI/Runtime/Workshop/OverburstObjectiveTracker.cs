using UnityEngine;
using UnityEngine.UI;

/// <summary>Vendor objective tracker view. The game currently has no quest state, so it shows an honest empty state.</summary>
public sealed class OverburstObjectiveTracker : MonoBehaviour
{
    [SerializeField] private Toggle collapseToggle;
    [SerializeField] private GameObject body;
    [SerializeField] private Text emptyMessage;
    [SerializeField] private GameObject sampleObjectives;
    [SerializeField] private RectTransform backdrop;
    private bool toggleBound;

    public void Configure(Toggle toggle, GameObject bodyRoot, Text message, GameObject sampleRows, RectTransform backdropRect)
    {
        if (toggleBound && collapseToggle != null)
            collapseToggle.onValueChanged.RemoveListener(SetExpanded);
        toggleBound = false;
        collapseToggle = toggle;
        body = bodyRoot;
        emptyMessage = message;
        sampleObjectives = sampleRows;
        backdrop = backdropRect;
        if (!isActiveAndEnabled)
            return;
        if (emptyMessage != null)
            emptyMessage.text = "진행 중인 목표 없음";
        if (sampleObjectives != null)
            sampleObjectives.SetActive(false);
        BindToggle();
        SetExpanded(collapseToggle == null || collapseToggle.isOn);
    }

    private void OnEnable()
    {
        if (emptyMessage != null)
            emptyMessage.text = "진행 중인 목표 없음";
        if (sampleObjectives != null)
            sampleObjectives.SetActive(false);
        BindToggle();
        SetExpanded(collapseToggle == null || collapseToggle.isOn);
    }

    private void OnDisable()
    {
        if (toggleBound && collapseToggle != null)
            collapseToggle.onValueChanged.RemoveListener(SetExpanded);
        toggleBound = false;
    }

    private void Update()
    {
        // Domain-reload-free Play can keep this scene object alive without OnEnable.
        BindToggle();
        if (collapseToggle != null && body != null && body.activeSelf != collapseToggle.isOn)
            SetExpanded(collapseToggle.isOn);
    }

    private void BindToggle()
    {
        if (toggleBound || collapseToggle == null)
            return;
        collapseToggle.onValueChanged.AddListener(SetExpanded);
        toggleBound = true;
    }

    private void SetExpanded(bool expanded)
    {
        if (body != null)
            body.SetActive(expanded);
        if (backdrop != null)
            backdrop.sizeDelta = new Vector2(backdrop.sizeDelta.x, expanded ? 180f : 86f);
    }
}
