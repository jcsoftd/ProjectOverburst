using UnityEngine;
using Overburst.Quests;
using Overburst.Persistence;
using UnityEngine.UI;

/// <summary>Preserves the existing HUD artwork and collapse control; displays the tracked personal quest.</summary>
public sealed class OverburstObjectiveTracker : MonoBehaviour
{
    [SerializeField] private Toggle collapseToggle;
    [SerializeField] private GameObject body;
    [SerializeField] private Text emptyMessage;
    [SerializeField] private GameObject sampleObjectives;
    [SerializeField] private RectTransform backdrop;
    private bool toggleBound;
    private AccountGameplaySession shownAccount;
    private long shownRevision=-1;

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
        var account=AccountGameplaySession.Current;
        if(!ReferenceEquals(account,shownAccount) || (account?.Revision ?? -1)!=shownRevision)
        {
            shownAccount=account;shownRevision=account?.Revision ?? -1;
            var quest=account?.ReadTrackedQuest();
            if(emptyMessage)
            {
                string progress="";
                if(quest!=null)
                {
                    if(AccountQuestCommands.IsReady(quest))progress="상인에게 보고";
                    else {var objective=quest.objectives.Find(o=>o.current<o.required);if(objective!=null)progress=objective.label+"  "+objective.current+" / "+objective.required;}
                }
                emptyMessage.text=quest==null ? "진행 중인 목표 없음" : quest.title+"\n"+progress;
            }
        }
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
