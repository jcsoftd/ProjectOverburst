using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HubScenePortal : MonoBehaviour, IInteractable // 허브 간 이동 포탈
{
    [Header("Destination")]
    [SerializeField] private string targetSceneName = PersistentSceneFlow.HideoutSceneName; // 대상 허브
    [SerializeField] private string targetReturnPointId = "Default"; // 대상 복귀 지점

    [Header("Interaction")]
    [SerializeField] private float interactionRadius = 2.6f; // 상호작용 반경
    [SerializeField] private string promptLabel = "F : 이동"; // 안내 문구
    [SerializeField] private Transform playerOverride; // 테스트용 플레이어
    [SerializeField] private GameObject promptRoot; // 안내 루트
    [SerializeField] private TMP_Text promptText; // 안내 텍스트
    [SerializeField] private Transform promptFacingRoot; // 카메라 정면 루트

    private Transform player;
    private bool isLoading;
    private string stableInteractionId;

    public string TargetSceneName => targetSceneName;
    public string TargetReturnPointId => targetReturnPointId;
    public float InteractionRadius => interactionRadius;
    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 300;
    public string InteractionPrompt => promptLabel;
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => interactionRadius;
    public string StableInteractionId => stableInteractionId ??= InteractionStableIdUtility.Build(this);
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => !isLoading;

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
        if (promptFacingRoot == null || !promptFacingRoot.gameObject.activeInHierarchy)
            return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        Vector3 direction = promptFacingRoot.position - mainCamera.transform.position;
        if (direction.sqrMagnitude > 0.0001f)
            promptFacingRoot.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    public bool IsPlayerInRange()
    {
        if (player == null)
            return false;

        Vector3 delta = player.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= interactionRadius * interactionRadius;
    }

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        return !isLoading
            && actor != null
            && flow != null
            && !flow.IsSwitching
            && PersistentSceneFlow.IsHubSceneName(targetSceneName);
    }

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor))
            return InteractionExecutionResult.Rejected;
        if (!PersistentSceneFlow.IsHubSceneName(targetSceneName))
        {
            Debug.LogError("[SceneFlow] HubScenePortal target is invalid: " + targetSceneName, this);
            return InteractionExecutionResult.Rejected;
        }

        isLoading = true;
        SetPromptVisible(false);
        PersistentSceneFlow.Instance.SwitchHubScene(targetSceneName, targetReturnPointId);
        return InteractionExecutionResult.StartedTransition;
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

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        player = playerObject != null ? playerObject.transform : null;
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
        if (string.IsNullOrWhiteSpace(targetReturnPointId))
            targetReturnPointId = "Default";
        if (string.IsNullOrWhiteSpace(promptLabel))
            promptLabel = "F : 이동";
        RefreshPromptText();
    }
#endif
}
