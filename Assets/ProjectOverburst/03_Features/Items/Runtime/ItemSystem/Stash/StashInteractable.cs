using TMPro;
using UnityEngine;

public class StashInteractable : MonoBehaviour, IInteractable // 창고 상호작용
{
    [SerializeField] private float interactRadius = 3f;
    [SerializeField] private StashUI stashUI;
    [SerializeField] private Transform player;
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TextMeshPro promptText;
    private string stableInteractionId;

    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 220;
    public string InteractionPrompt => "F : 창고 열기";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.ThreeDimensional;
    public float InteractionRange => interactRadius;
    public string StableInteractionId => stableInteractionId ??= InteractionStableIdUtility.Build(this);
    public bool AllowsInteractionWhileInputBlocked => stashUI != null && stashUI.IsOpen;
    public bool WantsInteractionPrompt => stashUI != null && !stashUI.IsOpen;

    private void Awake()
    {
        ResolveReferences(); // 참조 수집
        SetPromptVisible(false);
    }

    private void OnEnable()
    {
        InteractionRegistry.Register(this);
        SetPromptVisible(false);
    }

    private void OnDisable()
    {
        InteractionRegistry.Unregister(this);
        SetPromptVisible(false);
    }

    public bool IsPlayerInRange()
    {
        if (player == null)
            return false;

        return Vector3.Distance(transform.position, player.position) <= interactRadius; // 상호작용 범위
    }

    private void Update()
    {
        ResolveReferences(); // 참조 최신화

        if (player == null || stashUI == null)
        {
            SetPromptVisible(false);
            return;
        }

        bool inRange = Vector3.Distance(transform.position, player.position) <= interactRadius; // 상호작용 범위
        if (!inRange && stashUI.IsOpen)
        {
            stashUI.Close(); // 범위 이탈 닫기
            SetPromptVisible(false);
            return;
        }

        if (stashUI.IsOpen)
            SetPromptVisible(false);
    }

    private void ResolveReferences()
    {
        if (stashUI == null || !stashUI.HasUsableCanvasRoot)
            stashUI = FindPreferredStashUI(); // 영구 UI

        PlayerContext playerContext = PlayerContext.GetOrCreate();
        PlayerActorRuntime currentActor = playerContext != null ? playerContext.CurrentActor : null;
        if (currentActor != null)
        {
            player = currentActor.transform; // 현재 조작 플레이어
        }
        else if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player"); // 플레이어 태그
            if (playerObject != null)
                player = playerObject.transform; // 플레이어
        }

        if (promptText != null)
            promptText.text = "F : 창고 열기"; // 안내 문구
    }

    private StashUI FindPreferredStashUI()
    {
        StashUI[] candidates = FindObjectsByType<StashUI>(FindObjectsInactive.Include, FindObjectsSortMode.None); // UI 후보

        for (int i = 0; i < candidates.Length; i++)
        {
            StashUI candidate = candidates[i];
            if (candidate == null || !candidate.HasUsableCanvasRoot)
                continue;

            if (candidate.gameObject.scene.name == PersistentSceneFlow.PersistentSceneName)
                return candidate; // Persistent 우선
        }

        return null;
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptRoot != null)
            promptRoot.SetActive(visible); // 안내 표시
    }

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
    {
        ResolveReferences();
        return actor != null && player != null && stashUI != null;
    }

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor) || !IsPlayerInRange())
            return InteractionExecutionResult.Rejected;
        if (stashUI.IsOpen)
        {
            stashUI.Close();
            return InteractionExecutionResult.ClosedSession;
        }
        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return InteractionExecutionResult.Rejected;
        stashUI.Open();
        return InteractionExecutionResult.Succeeded;
    }

    public void SetInteractionPromptVisible(bool visible)
    {
        if (promptText != null)
            promptText.text = InteractionPrompt;
        SetPromptVisible(visible && WantsInteractionPrompt);
    }
}
