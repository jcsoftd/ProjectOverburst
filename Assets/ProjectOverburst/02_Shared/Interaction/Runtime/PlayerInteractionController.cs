using System;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public sealed class PlayerInteractionController : MonoBehaviour
{
    [SerializeField] private PlayerActorRuntime actor;
    [SerializeField] private PlayerInputFacade inputFacade;
    [SerializeField] private InteractionDirector director;
    [SerializeField] private InteractionPromptPresenter promptPresenter;

    public IInteractable Current => director != null ? director.Current : null;
    public InteractionExecutionResult LastResult { get; private set; }
    public int ExecutionCount { get; private set; }
    public event Action<IInteractable, InteractionExecutionResult> InteractionExecuted;

    public void Configure(
        PlayerActorRuntime configuredActor,
        PlayerInputFacade configuredInputFacade,
        InteractionDirector configuredDirector,
        InteractionPromptPresenter configuredPromptPresenter)
    {
        actor = configuredActor;
        inputFacade = configuredInputFacade;
        director = configuredDirector;
        promptPresenter = configuredPromptPresenter;
    }

    private void Awake() => ResolveReferences();

    private void Update()
    {
        ResolveReferences();
        if (director == null || actor == null)
        {
            promptPresenter?.Clear();
            return;
        }

        IInteractable selected = director.Refresh(actor);
        promptPresenter?.Present(selected);
        QuestMerchantUI.Current?.Present(selected, actor, this);

        if (inputFacade == null || !inputFacade.InteractPressedThisFrame)
            return;

        InteractionExecutionResult result = ExecuteSelected(selected);
        LastResult = result;
        if (result != InteractionExecutionResult.Rejected)
        {
            ExecutionCount++;
            InteractionExecuted?.Invoke(selected, result);
            OverburstFeelFeedbackHub.Request(OverburstFeelCue.Interaction, transform.position);
        }

        promptPresenter?.Present(director.Refresh(actor));
    }

    public InteractionExecutionResult ExecuteSelectedForValidation()
    {
        ResolveReferences();
        if (director == null || actor == null)
            return InteractionExecutionResult.Rejected;

        IInteractable selected = director.Refresh(actor);
        QuestMerchantUI.Current?.Present(selected, actor, this);
        InteractionExecutionResult result = ExecuteSelected(selected);
        LastResult = result;
        if (result != InteractionExecutionResult.Rejected)
        {
            ExecutionCount++;
            InteractionExecuted?.Invoke(selected, result);
            OverburstFeelFeedbackHub.Request(OverburstFeelCue.Interaction, transform.position);
        }
        promptPresenter?.Present(director.Refresh(actor));
        return result;
    }

    private void OnDisable()
    {
        promptPresenter?.Clear();
        director?.ClearSelection();
        QuestMerchantUI.Current?.Present(null, actor, this);
    }

    private InteractionExecutionResult ExecuteSelected(IInteractable selected)
    {
        if (QuestMerchantUI.Current && (selected is GeneralGoodsMerchantInteractable
            || selected is AppearanceStylistInteractable && !Overburst.Appearance.AppearanceCustomizationPanel.IsOpen))
            return QuestMerchantUI.Current.Execute(selected, actor, QuestMerchantUI.Current.SelectedOption);
        return director.ExecuteCurrent(actor);
    }

    public InteractionExecutionResult ExecuteMerchantOption(IInteractable expected, int option)
    {
        ResolveReferences();
        if (expected == null || !expected.InteractionComponent || !actor || !director || !QuestMerchantUI.Current
            || !ReferenceEquals(director.Refresh(actor), expected)) return InteractionExecutionResult.Rejected;
        var result = QuestMerchantUI.Current.Execute(expected, actor, option);
        LastResult = result;
        if (result != InteractionExecutionResult.Rejected)
        {
            ExecutionCount++;
            InteractionExecuted?.Invoke(expected, result);
            OverburstFeelFeedbackHub.Request(OverburstFeelCue.Interaction, transform.position);
        }
        return result;
    }

    private void ResolveReferences()
    {
        if (actor == null)
            actor = GetComponent<PlayerActorRuntime>();
        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();
        if (director == null)
            director = GetComponent<InteractionDirector>();
        if (promptPresenter == null)
            promptPresenter = GetComponent<InteractionPromptPresenter>();
    }
}
