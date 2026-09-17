using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonPortalEntry : MonoBehaviour, IInteractable
{
    public const string DefaultReturnPointId = "DungeonPortal";

    [Header("Dungeon Entry")]
    [SerializeField] private string returnPointId =
        DefaultReturnPointId;
    [SerializeField] private bool useFixedSeed;
    [SerializeField] private int fixedSeed = 20260726;

    [Header("Interaction")]
    [SerializeField, Min(0.5f)] private float interactionRadius = 2.8f;
    [SerializeField] private string promptLabel = "F : 다층 던전 진입";
    [SerializeField] private Transform playerOverride;
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private Transform promptFacingRoot;

    private Transform player;
    private bool isLoading;
    private string stableInteractionId;

    public string ReturnPointId => string.IsNullOrWhiteSpace(returnPointId)
        ? DefaultReturnPointId
        : returnPointId;
    public float InteractionRadius => interactionRadius;
    public GameObject PromptRoot => promptRoot;
    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 320;
    public string InteractionPrompt => promptLabel;
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => interactionRadius;
    public string StableInteractionId => stableInteractionId ??= InteractionStableIdUtility.Build(this);
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => !isLoading;

    public void ConfigureAuthoring(
        string configuredReturnPointId,
        float configuredInteractionRadius,
        string configuredPromptLabel,
        GameObject configuredPromptRoot,
        TMP_Text configuredPromptText,
        Transform configuredPromptFacingRoot)
    {
        returnPointId = string.IsNullOrWhiteSpace(
            configuredReturnPointId)
                ? DefaultReturnPointId
                : configuredReturnPointId;
        interactionRadius = Mathf.Max(
            0.5f,
            configuredInteractionRadius);
        promptLabel = string.IsNullOrWhiteSpace(configuredPromptLabel)
            ? "F : 다층 던전 진입"
            : configuredPromptLabel;
        promptRoot = configuredPromptRoot;
        promptText = configuredPromptText;
        promptFacingRoot = configuredPromptFacingRoot;
        RefreshPromptText();
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
        if (isLoading)
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

    public void LaunchDungeonRun()
    {
        int seed = useFixedSeed
            ? fixedSeed
            : RunSeedUtility.Create();
        LaunchDungeonRunWithSeed(seed);
    }

    public bool LaunchDungeonRunWithSeed(int seed)
    {
        if (isLoading)
            return false;

        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null || flow.IsSwitching)
        {
            Debug.LogError(
                "[DungeonPortal] PersistentSceneFlow is not ready.",
                this);
            return false;
        }

        string sourceSceneName = gameObject.scene.name;
        if (!PersistentSceneFlow.IsHubSceneName(sourceSceneName))
            sourceSceneName = PersistentSceneFlow.DefaultHubSceneName;

        isLoading = true;
        SetPromptVisible(false);
        flow.EnterDungeon(
            DungeonRunEntryRequest.Create(
                seed,
                sourceSceneName,
                ReturnPointId));
        return true;
    }

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        return !isLoading && actor != null && flow != null && !flow.IsSwitching;
    }

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor))
            return InteractionExecutionResult.Rejected;
        return LaunchDungeonRunWithSeed(useFixedSeed ? fixedSeed : RunSeedUtility.Create())
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
        if (string.IsNullOrWhiteSpace(returnPointId))
            returnPointId = DefaultReturnPointId;
        if (string.IsNullOrWhiteSpace(promptLabel))
            promptLabel = "F : 다층 던전 진입";
        RefreshPromptText();
    }
#endif
}
