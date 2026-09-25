using Overburst.Persistence;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapRewardChest : MonoBehaviour, IInteractable
{
    private ItemData reward;
    private string runId;
    private bool opened;
    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 242;
    public string InteractionPrompt => "F : 특별 전리품 상자 열기";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => 3.1f;
    public string StableInteractionId => name;
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => !opened && reward != null;

    public void Configure(string activeRunId, ItemData item, Material material)
    {
        runId = activeRunId;
        reward = item;
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "RewardChestBody";
        body.transform.SetParent(transform, false);
        body.transform.localPosition = Vector3.up * .58f;
        body.transform.localScale = new Vector3(1.6f, 1.1f, 1.15f);
        body.GetComponent<Renderer>().sharedMaterial = material;
        Destroy(body.GetComponent<Collider>());
        InteractionRegistry.Register(this);
    }

    private void OnEnable()
    {
        if (reward != null && !opened) InteractionRegistry.Register(this);
    }
    private void OnDisable() => InteractionRegistry.Unregister(this);

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
        => actor != null && !opened && reward != null && WorldSessionState.Phase == WorldPhase.Run
            && AccountGameplaySession.Current != null;

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor)) return InteractionExecutionResult.Rejected;
        var run = AccountGameplaySession.Current.ReadRun();
        if (run == null || run.runId != runId || !AccountInvariants.IsRunning(run.phase))
            return InteractionExecutionResult.Rejected;
        WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickup(reward,
            transform.position + Vector3.up * .65f,
            PlayerAccountInventoryService.SharedInventory,
            PlayerContext.Instance?.CurrentActor?.transform);
        if (pickup == null) return InteractionExecutionResult.Rejected;
        opened = true;
        InteractionRegistry.Unregister(this);
        gameObject.SetActive(false);
        return InteractionExecutionResult.Succeeded;
    }

    public void SetInteractionPromptVisible(bool visible) { }
}
