using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GeneralGoodsMerchantInteractable : MonoBehaviour, IInteractable
{
    private const string DefaultMerchantDefinitionPath = "Assets/ProjectOverburst/03_Features/Items/Data/Merchants/GeneralGoodsMerchant.asset";

    [SerializeField] private float interactRadius = 3f;
    [SerializeField] private MerchantDefinition merchantDefinition;
    [SerializeField] private ShopUI shopUI;
    [SerializeField] private Transform player;
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TextMeshPro promptText;
    private string stableInteractionId;

    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 200;
    public string InteractionPrompt => "F : " + (merchantDefinition != null ? merchantDefinition.MerchantName : "상인");
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.ThreeDimensional;
    public float InteractionRange => interactRadius;
    public string StableInteractionId => stableInteractionId ??= InteractionStableIdUtility.Build(this);
    public bool AllowsInteractionWhileInputBlocked => shopUI != null && merchantDefinition != null && shopUI.IsOpenFor(merchantDefinition);
    public bool WantsInteractionPrompt => shopUI != null && merchantDefinition != null && !shopUI.IsOpenFor(merchantDefinition);

    private void Awake()
    {
        ResolveReferences();
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

    private void Update()
    {
        ResolveReferences();

        if (player == null || shopUI == null || merchantDefinition == null)
        {
            SetPromptVisible(false);
            return;
        }

        bool inRange = Vector3.Distance(transform.position, player.position) <= interactRadius;
        if (!inRange && shopUI.IsOpenFor(merchantDefinition))
        {
            shopUI.Close();
            SetPromptVisible(false);
            return;
        }

        if (shopUI.IsOpenFor(merchantDefinition))
            SetPromptVisible(false);
    }

    private void ResolveReferences()
    {
        if (merchantDefinition == null)
            merchantDefinition = LoadDefaultMerchantDefinition();

        if (shopUI == null || !shopUI.HasUsableCanvasRoot)
            shopUI = FindPreferredShopUI();

        PlayerContext playerContext = PlayerContext.GetOrCreate();
        PlayerActorRuntime currentActor = playerContext != null ? playerContext.CurrentActor : null;
        if (currentActor != null)
        {
            player = currentActor.transform;
        }
        else if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
        }

        if (promptText != null)
            promptText.text = "F : " + (merchantDefinition != null ? merchantDefinition.MerchantName : "상인");
    }

    private ShopUI FindPreferredShopUI()
    {
        ShopUI[] candidates = FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < candidates.Length; i++)
        {
            ShopUI candidate = candidates[i];
            if (candidate == null || !candidate.HasUsableCanvasRoot)
                continue;

            if (candidate.gameObject.scene.name == PersistentSceneFlow.PersistentSceneName)
                return candidate;
        }

        return candidates.Length > 0 ? candidates[0] : null;
    }

    private MerchantDefinition LoadDefaultMerchantDefinition()
    {
#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<MerchantDefinition>(DefaultMerchantDefinitionPath);
#else
        return null;
#endif
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptRoot != null)
            promptRoot.SetActive(visible);
    }

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
    {
        ResolveReferences();
        return actor != null && player != null && shopUI != null && merchantDefinition != null;
    }

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor))
            return InteractionExecutionResult.Rejected;
        if (Vector3.Distance(transform.position, player.position) > interactRadius)
            return InteractionExecutionResult.Rejected;
        if (shopUI.IsOpenFor(merchantDefinition))
        {
            shopUI.Close();
            return InteractionExecutionResult.ClosedSession;
        }
        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return InteractionExecutionResult.Rejected;
        shopUI.Open(merchantDefinition);
        return InteractionExecutionResult.Succeeded;
    }

    public void SetInteractionPromptVisible(bool visible)
    {
        if (promptText != null)
            promptText.text = InteractionPrompt;
        SetPromptVisible(visible && WantsInteractionPrompt);
    }
}
