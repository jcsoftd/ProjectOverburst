using Overburst.Appearance;
using Overburst.Persistence;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AppearanceStylistInteractable : MonoBehaviour,IInteractable
{
    [SerializeField] private float interactRadius=3f;
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TextMeshPro promptText;
    private AppearanceCustomizationPanel panel;
    private string stableId;
    public Component InteractionComponent=>this;
    public Transform InteractionTransform=>transform;
    public int InteractionPriority=>200;
    public string InteractionPrompt=>"F : 외모 변경";
    public InteractionDistanceMode DistanceMode=>InteractionDistanceMode.ThreeDimensional;
    public float InteractionRange=>interactRadius;
    public string StableInteractionId=>stableId??=InteractionStableIdUtility.Build(this);
    private bool OwnsOpenSession=>panel&&AppearanceCustomizationPanel.IsOpen&&panel.Owner==this;
    public bool AllowsInteractionWhileInputBlocked=>OwnsOpenSession;
    public bool WantsInteractionPrompt=>!OwnsOpenSession;
    private void Awake(){panel=AppearanceCustomizationPanel.Instance;SetInteractionPromptVisible(false);}
    private void OnEnable(){InteractionRegistry.Register(this);SetInteractionPromptVisible(false);}
    private void OnDisable()
    {
        InteractionRegistry.Unregister(this);SetInteractionPromptVisible(false);
        if(OwnsOpenSession)panel.Close();
    }
    private void Update()
    {
        if(!panel)panel=AppearanceCustomizationPanel.Instance;
        if(!OwnsOpenSession)return;
        var actor=PlayerContext.Instance?.CurrentActor;
        if(!actor||Vector3.Distance(transform.position,actor.transform.position)>interactRadius)
            panel.Close();
    }
    public bool IsInteractionAvailable(PlayerActorRuntime actor)
    {
        if(!panel)panel=AppearanceCustomizationPanel.Instance;
        return actor&&panel&&WorldSessionState.IsHideout&&AccountBootstrap.Ready
            && AccountGameplaySession.Current!=null&&!AccountGameplaySession.Current.NeedsProjectionRecovery
            && actor.Health&&!actor.Health.IsDead;
    }
    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if(!IsInteractionAvailable(actor)||Vector3.Distance(transform.position,actor.transform.position)>interactRadius)
            return InteractionExecutionResult.Rejected;
        if(OwnsOpenSession){panel.RequestClose();return InteractionExecutionResult.ClosedSession;}
        return panel.Open(this)?InteractionExecutionResult.Succeeded:InteractionExecutionResult.Rejected;
    }
    public void SetInteractionPromptVisible(bool visible)
    {
        if(promptText)promptText.text=InteractionPrompt;
        if(promptRoot)promptRoot.SetActive(visible&&WantsInteractionPrompt);
    }
}

