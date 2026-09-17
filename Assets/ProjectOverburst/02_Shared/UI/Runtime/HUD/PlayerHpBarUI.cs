using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(700)]
public sealed class PlayerHpBarUI : MonoBehaviour
{
    [SerializeField] private CombatHealth playerHealth;
    [SerializeField] private Transform followTarget;
    [SerializeField] private PlayerContext playerContext;
    [SerializeField] private Canvas canvas;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private CanvasGroup visibilityGroup;
    [SerializeField] private Image hpFill;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.02f, 0f);
    [SerializeField] private bool autoResolveReferences = true;
    [SerializeField] private bool hideWhenUnavailable = true;
    [SerializeField] private bool hideWhenBehindCamera = true;
    [UnityEngine.Serialization.FormerlySerializedAs("disableWhenPartyHudExists"), SerializeField] private bool disableWhenStatusHudExists = true;

    private RectTransform rectTransform;
    private CombatHealth subscribedHealth;
    private PlayerContext subscribedPlayerContext;

    private void OnEnable()
    {
        CacheReferences();
        if (ShouldDisableForStatusHud())
        {
            gameObject.SetActive(false);
            return;
        }

        ResolveReferences();
        SubscribeLeaderContext();
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
        UnsubscribeLeaderContext();
    }

    private void Update()
    {
        if (autoResolveReferences && (playerHealth == null || followTarget == null))
        {
            ResolveReferences();
            Subscribe();
            Refresh();
        }
    }

    private void LateUpdate()
    {
        UpdateFollowPosition();
    }

    private void HandleHealthChanged(CombatHealth source, float currentHp, float maxHp)
    {
        Refresh(currentHp, maxHp);
    }

    private void HandleLeaderChanged(PlayerActorRuntime leader)
    {
        ResolveReferences();
        Subscribe();
        Refresh();
    }

    private void Refresh()
    {
        if (playerHealth == null)
        {
            Refresh(0f, 1f);
            return;
        }

        Refresh(playerHealth.CurrentHp, playerHealth.MaxHp);
    }

    private void Refresh(float currentHp, float maxHp)
    {
        float safeMaxHp = Mathf.Max(1f, maxHp);
        float normalized = Mathf.Clamp01(currentHp / safeMaxHp);

        if (hpFill != null)
        {
            hpFill.type = Image.Type.Simple;
            hpFill.fillAmount = normalized;
            RefreshFillRect(hpFill.rectTransform, normalized);
        }

        if (hpText != null)
            hpText.text = Mathf.RoundToInt(Mathf.Max(0f, currentHp)) + " / " + Mathf.RoundToInt(safeMaxHp);
    }

    private static void RefreshFillRect(RectTransform fillRect, float normalized)
    {
        if (fillRect == null)
            return;

        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.localScale = new Vector3(normalized, 1f, 1f);
    }

    private void Subscribe()
    {
        if (playerHealth == null || subscribedHealth == playerHealth)
            return;

        Unsubscribe();
        playerHealth.OnHealthChanged += HandleHealthChanged;
        subscribedHealth = playerHealth;
    }

    private void Unsubscribe()
    {
        if (subscribedHealth == null)
            return;

        subscribedHealth.OnHealthChanged -= HandleHealthChanged;
        subscribedHealth = null;
    }

    private void SubscribeLeaderContext()
    {
        if (playerContext == null || subscribedPlayerContext == playerContext)
            return;

        UnsubscribeLeaderContext();
        playerContext.CurrentActorChanged += HandleLeaderChanged;
        subscribedPlayerContext = playerContext;
    }

    private void UnsubscribeLeaderContext()
    {
        if (subscribedPlayerContext == null)
            return;

        subscribedPlayerContext.CurrentActorChanged -= HandleLeaderChanged;
        subscribedPlayerContext = null;
    }

    private void ResolveReferences()
    {
        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        SubscribeLeaderContext();

        CombatHealth leaderHealth = playerContext != null ? playerContext.CurrentActorHealth : null;
        if (leaderHealth != null && leaderHealth != playerHealth)
        {
            Unsubscribe();
            playerHealth = leaderHealth;
            followTarget = playerHealth.transform;
        }

        if (playerHealth == null)
            playerHealth = FindPlayerHealth();

        if (followTarget == null && playerHealth != null)
            followTarget = playerHealth.transform;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        if (visibilityGroup == null)
            visibilityGroup = GetComponent<CanvasGroup>();

        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void CacheReferences()
    {
        if (rectTransform == null)
            rectTransform = transform as RectTransform;

        if (visibilityGroup == null)
            visibilityGroup = GetComponent<CanvasGroup>();
    }

    private void UpdateFollowPosition()
    {
        CacheReferences();

        if (rectTransform == null || canvas == null || followTarget == null)
        {
            SetVisible(!hideWhenUnavailable);
            return;
        }

        Camera cameraForWorld = targetCamera != null ? targetCamera : Camera.main;
        if (cameraForWorld == null)
        {
            SetVisible(!hideWhenUnavailable);
            return;
        }

        Vector3 screenPosition = cameraForWorld.WorldToScreenPoint(followTarget.position + worldOffset);
        if (hideWhenBehindCamera && screenPosition.z < 0f)
        {
            SetVisible(false);
            return;
        }

        RectTransform parentRect = rectTransform.parent as RectTransform;
        if (parentRect == null)
        {
            SetVisible(!hideWhenUnavailable);
            return;
        }

        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cameraForWorld;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPosition, eventCamera, out Vector2 localPoint))
        {
            rectTransform.anchoredPosition = localPoint;
            SetVisible(true);
        }
        else
        {
            SetVisible(!hideWhenUnavailable);
        }
    }

    private void SetVisible(bool visible)
    {
        if (visibilityGroup != null)
        {
            visibilityGroup.alpha = visible ? 1f : 0f;
            return;
        }

        if (hpFill != null)
            hpFill.enabled = visible;

        if (hpText != null)
            hpText.enabled = visible;
    }

    private bool ShouldDisableForStatusHud()
    {
        return disableWhenStatusHudExists
            && FindFirstObjectByType<PlayerHealthHud>(FindObjectsInactive.Include) != null;
    }

    private static CombatHealth FindPlayerHealth()
    {
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>(FindObjectsInactive.Include);
        if (player != null)
            return player.GetComponentInParent<CombatHealth>();

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        return playerObject != null ? playerObject.GetComponentInParent<CombatHealth>() : null;
    }
}
