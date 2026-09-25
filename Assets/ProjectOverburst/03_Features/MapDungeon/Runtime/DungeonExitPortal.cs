using Overburst.Persistence;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonExitPortal : MonoBehaviour, IInteractable
{
    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 320;
    public string InteractionPrompt => "F : 하이드아웃 귀환";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => 3.2f;
    public string StableInteractionId => InteractionStableIdUtility.Build(this);
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => isActiveAndEnabled;

    private void OnEnable() => InteractionRegistry.Register(this);
    private void OnDisable() => InteractionRegistry.Unregister(this);

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
        => actor != null && isActiveAndEnabled && WorldSessionState.Phase == WorldPhase.Run
            && AccountGameplaySession.Current?.ReadRun()?.phase == RunPhase.BossCleared;

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor)) return InteractionExecutionResult.Rejected;
        var driver = PersistentSceneFlow.Instance?.GetComponent<RunLifetimeDriver>();
        return driver != null && driver.RequestPortalExit()
            ? InteractionExecutionResult.StartedTransition : InteractionExecutionResult.Rejected;
    }

    public void SetInteractionPromptVisible(bool visible) { }

    public void BuildVisual(Material material)
    {
        var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObject.name = "ExitPortalBase";
        baseObject.transform.SetParent(transform, false);
        baseObject.transform.localPosition = Vector3.up * .09f;
        baseObject.transform.localScale = new Vector3(3f, .1f, 3f);
        baseObject.GetComponent<Renderer>().sharedMaterial = material;
        Destroy(baseObject.GetComponent<Collider>());
        var core = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        core.name = "ExitPortalCore";
        core.transform.SetParent(transform, false);
        core.transform.localPosition = Vector3.up * 1.5f;
        core.transform.localScale = new Vector3(.7f, 1.2f, .7f);
        core.GetComponent<Renderer>().sharedMaterial = material;
        Destroy(core.GetComponent<Collider>());
    }
}
