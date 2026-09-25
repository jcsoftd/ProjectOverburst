using System;
using Overburst.Persistence;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapRunTransferObject : MonoBehaviour, IInteractable
{
    private string objectId;
    private string runId;
    private bool used;
    public string ObjectId => objectId;
    public bool Used => used;
    public event Action<MapRunTransferObject> OpenRequested;

    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 245;
    public string InteractionPrompt => "F : 아이템 1스택 창고 전송";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => 3.2f;
    public string StableInteractionId => objectId;
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => !used && !string.IsNullOrEmpty(objectId);

    public void Configure(string id, string activeRunId, Material material)
    {
        objectId = id;
        runId = activeRunId;
        var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObject.name = "TransferBase";
        baseObject.transform.SetParent(transform, false);
        baseObject.transform.localPosition = Vector3.up * .16f;
        baseObject.transform.localScale = new Vector3(2.4f, .15f, 2.4f);
        baseObject.GetComponent<Renderer>().sharedMaterial = material;
        Destroy(baseObject.GetComponent<Collider>());
        var core = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        core.name = "TransferCore";
        core.transform.SetParent(transform, false);
        core.transform.localPosition = Vector3.up * 1.15f;
        core.transform.localScale = new Vector3(.75f, 1f, .75f);
        core.GetComponent<Renderer>().sharedMaterial = material;
        Destroy(core.GetComponent<Collider>());
        InteractionRegistry.Register(this);
    }

    public void MarkUsed()
    {
        used = true;
        InteractionRegistry.Unregister(this);
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(objectId) && !used) InteractionRegistry.Register(this);
    }
    private void OnDisable() => InteractionRegistry.Unregister(this);

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
        => actor != null && !used && !string.IsNullOrEmpty(objectId)
            && AccountGameplaySession.Current != null && WorldSessionState.Phase == WorldPhase.Run;

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor)) return InteractionExecutionResult.Rejected;
        var run = AccountGameplaySession.Current.ReadRun();
        if (run == null || run.runId != runId || !AccountInvariants.IsRunning(run.phase)
            || run.transferredObjects.Contains(objectId)) return InteractionExecutionResult.Rejected;
        OpenRequested?.Invoke(this);
        return InteractionExecutionResult.Succeeded;
    }

    public void SetInteractionPromptVisible(bool visible) { }
}
