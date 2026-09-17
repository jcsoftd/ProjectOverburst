using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerCameraBinder : MonoBehaviour
{
    [SerializeField] private PlayerContext playerContext;
    [SerializeField] private QuarterViewCamera quarterViewCamera;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (playerContext != null)
            playerContext.CurrentActorChanged += HandleActorChanged;

        HandleActorChanged(playerContext != null ? playerContext.CurrentActor : null);
    }

    private void OnDisable()
    {
        if (playerContext != null)
            playerContext.CurrentActorChanged -= HandleActorChanged;
    }

    public void Bind(PlayerContext runtime)
    {
        if (playerContext != null)
            playerContext.CurrentActorChanged -= HandleActorChanged;

        playerContext = runtime;

        if (isActiveAndEnabled && playerContext != null)
            playerContext.CurrentActorChanged += HandleActorChanged;

        HandleActorChanged(playerContext != null ? playerContext.CurrentActor : null);
    }

    private void HandleActorChanged(PlayerActorRuntime actor)
    {
        ResolveReferences();
        if (quarterViewCamera == null || actor == null)
            return;

        quarterViewCamera.SetTarget(actor.transform);
    }

    private void ResolveReferences()
    {
        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        if (quarterViewCamera == null)
            quarterViewCamera = FindFirstObjectByType<QuarterViewCamera>(FindObjectsInactive.Include);
    }
}

