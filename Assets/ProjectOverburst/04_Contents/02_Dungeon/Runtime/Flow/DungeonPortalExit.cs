using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonPortalExit : MonoBehaviour, IInteractable
{
    [SerializeField, Min(0.5f)] private float interactionRadius = 2.8f;
    [SerializeField] private string promptLabel = "F : 하이드아웃으로 복귀";
    [SerializeField] private Transform playerOverride;
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private Transform promptFacingRoot;

    private DungeonRunFlow runFlow;
    private Transform player;
    private bool isLoading;
    private bool interactionEnabled = true;
    private string stableInteractionId;

    public DungeonRunFlow RunFlow => runFlow;
    public float InteractionRadius => interactionRadius;
    public GameObject PromptRoot => promptRoot;
    public bool InteractionEnabled => interactionEnabled;
    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 340;
    public string InteractionPrompt => promptLabel;
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => interactionRadius;
    public string StableInteractionId => stableInteractionId ??= InteractionStableIdUtility.Build(this);
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => interactionEnabled && !isLoading;

    public void ConfigureAuthoring(
        float configuredInteractionRadius,
        string configuredPromptLabel,
        GameObject configuredPromptRoot,
        TMP_Text configuredPromptText,
        Transform configuredPromptFacingRoot)
    {
        interactionRadius = Mathf.Max(
            0.5f,
            configuredInteractionRadius);
        promptLabel = string.IsNullOrWhiteSpace(configuredPromptLabel)
            ? "F : 하이드아웃으로 복귀"
            : configuredPromptLabel;
        promptRoot = configuredPromptRoot;
        promptText = configuredPromptText;
        promptFacingRoot = configuredPromptFacingRoot;
        RefreshPromptText();
    }

    public void Bind(DungeonRunFlow owner)
    {
        runFlow = owner;
        isLoading = false;
        interactionEnabled = true;
        ResolvePlayer();
        RefreshPromptText();
        SetPromptVisible(false);
    }

    public void SetInteractionEnabled(bool enabled)
    {
        interactionEnabled = enabled;
        if (!interactionEnabled)
            SetPromptVisible(false);
    }

    private void Awake()
    {
        ResolvePlayer();
        RefreshPromptText();
        SetPromptVisible(false);
    }

    private void OnEnable()
    {
        isLoading = false;
        RefreshPromptText();
        SetPromptVisible(false);
        InteractionRegistry.Register(this);
    }

    private void OnDisable()
    {
        InteractionRegistry.Unregister(this);
        SetPromptVisible(false);
    }

    private void Update()
    {
        ResolvePlayer();
        if (isLoading || !interactionEnabled)
            SetPromptVisible(false);
    }

    private void LateUpdate()
    {
        if (promptFacingRoot == null
            || !promptFacingRoot.gameObject.activeInHierarchy)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        Vector3 direction =
            promptFacingRoot.position - mainCamera.transform.position;
        if (direction.sqrMagnitude > 0.0001f)
        {
            promptFacingRoot.rotation = Quaternion.LookRotation(
                direction.normalized,
                Vector3.up);
        }
    }

    public bool IsPlayerInRange()
    {
        if (player == null)
            ResolvePlayer();
        if (player == null)
            return false;

        Vector3 delta = player.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude
            <= interactionRadius * interactionRadius;
    }

    public bool RequestExtract()
    {
        if (!interactionEnabled || isLoading || runFlow == null)
            return false;

        isLoading = runFlow.RequestReturnToSourceHub(true);
        if (isLoading)
            SetPromptVisible(false);
        return isLoading;
    }

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        return interactionEnabled
            && !isLoading
            && actor != null
            && runFlow != null
            && flow != null
            && !flow.IsSwitching;
    }

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        return IsInteractionAvailable(actor) && RequestExtract()
            ? InteractionExecutionResult.StartedTransition
            : InteractionExecutionResult.Rejected;
    }

    public void SetInteractionPromptVisible(bool visible) => SetPromptVisible(visible);

    private void ResolvePlayer()
    {
        if (playerOverride != null)
        {
            player = playerOverride;
            return;
        }

        PlayerContext playerContext = PlayerContext.GetOrCreate();
        PlayerActorRuntime actor = playerContext != null ? playerContext.CurrentActor : null;
        if (actor != null)
        {
            player = actor.transform;
            return;
        }

        GameObject playerObject =
            GameObject.FindGameObjectWithTag("Player");
        player = playerObject != null
            ? playerObject.transform
            : null;
    }

    private void RefreshPromptText()
    {
        if (promptText != null)
            promptText.text = promptLabel;
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptRoot != null && promptRoot.activeSelf != visible)
            promptRoot.SetActive(visible);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        interactionRadius = Mathf.Max(0.5f, interactionRadius);
        if (string.IsNullOrWhiteSpace(promptLabel))
            promptLabel = "F : 하이드아웃으로 복귀";
        RefreshPromptText();
    }
#endif
}
