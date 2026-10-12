using Overburst.Persistence;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldLevelNpcInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private float interactRadius = 3f;
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TextMeshPro promptText;
    private string stableId;
    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 200;
    public string InteractionPrompt => "F : 월드레벨";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.ThreeDimensional;
    public float InteractionRange => interactRadius;
    public string StableInteractionId => stableId ??= InteractionStableIdUtility.Build(this);
    public bool OwnsOpenSession => WorldLevelUI.Current && WorldLevelUI.Current.IsOpen && WorldLevelUI.Current.Owner == this;
    public bool AllowsInteractionWhileInputBlocked => OwnsOpenSession;
    public bool WantsInteractionPrompt => !OwnsOpenSession;
    private void OnEnable() { InteractionRegistry.Register(this); SetInteractionPromptVisible(false); }
    private void OnDisable() { InteractionRegistry.Unregister(this); SetInteractionPromptVisible(false); if (OwnsOpenSession) WorldLevelUI.Current.Close(); }
    public bool IsInteractionAvailable(PlayerActorRuntime actor) => actor && actor.Health && !actor.Health.IsDead
        && WorldSessionState.IsHideout && AccountBootstrap.Ready && AccountGameplaySession.Current != null
        && !AccountGameplaySession.Current.NeedsProjectionRecovery && PersistentSceneFlow.Instance?.IsSwitching != true;
    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor) || Vector3.Distance(transform.position, actor.transform.position) > interactRadius)
            return InteractionExecutionResult.Rejected;
        if (OwnsOpenSession) { WorldLevelUI.Current.Close(); return InteractionExecutionResult.ClosedSession; }
        var panel = WorldLevelUI.Install();
        return panel && panel.Open(this, actor) ? InteractionExecutionResult.Succeeded : InteractionExecutionResult.Rejected;
    }
    public void SetInteractionPromptVisible(bool visible)
    {
        if (promptText) promptText.text = InteractionPrompt;
        if (promptRoot) promptRoot.SetActive(visible && WantsInteractionPrompt);
    }
}
