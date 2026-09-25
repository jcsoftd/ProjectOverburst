using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(10002)]
public class PlayerStaminaBarUI : MonoBehaviour // 플레이어 스태미너 HUD
{
    [SerializeField] private PlayerContext playerContext;
    [SerializeField] private PlayerStaminaController staminaController;
    [SerializeField] private Image staminaFill;
    [SerializeField] private TextMeshProUGUI staminaText;
    [SerializeField] private Color fillColor = new Color(0.35f, 0.85f, 1f, 1f);
    [SerializeField] private bool autoResolveReferences = true;

    private PlayerStaminaController subscribedController;
    private PlayerContext subscribedPlayerContext;

    private void OnEnable()
    {
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        Unsubscribe();
        UnsubscribeLeaderContext();
    }

    private void Update()
    {
        if (!autoResolveReferences)
            return;

        PlayerStaminaController previousController = staminaController;
        ResolveReferences();
        SubscribeLeaderContext();
        if (previousController != staminaController || staminaFill == null)
        {
            Subscribe();
            Refresh();
        }
    }

    private void ResolveReferences()
    {
        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        PlayerStaminaController leaderStamina = playerContext != null
            ? playerContext.CurrentActorStaminaController
            : null;
        if (leaderStamina != null)
            staminaController = leaderStamina;
   

        if (staminaFill == null)
            staminaFill = transform.Find("StaminaBarBackground/StaminaBarFill")?.GetComponent<Image>();

        if (staminaText == null)
            staminaText = transform.Find("StaminaText")?.GetComponent<TextMeshProUGUI>();
    }

    private void SubscribeLeaderContext()
    {
        if (subscribedPlayerContext == playerContext)
            return;

        UnsubscribeLeaderContext();
        subscribedPlayerContext = playerContext;
        if (subscribedPlayerContext != null)
            subscribedPlayerContext.CurrentActorChanged += HandleLeaderChanged;
    }

    private void UnsubscribeLeaderContext()
    {
        if (subscribedPlayerContext != null)
            subscribedPlayerContext.CurrentActorChanged -= HandleLeaderChanged;

        subscribedPlayerContext = null;
    }

    private void HandleLeaderChanged(PlayerActorRuntime leader)
    {
        staminaController = leader != null && leader.PlayerKit != null
            ? leader.PlayerKit.StaminaController
            : null;
        Subscribe();
        Refresh();
    }

    private void Subscribe()
    {
        if (subscribedController == staminaController)
            return;

        Unsubscribe();

        if (staminaController == null)
            return;

        subscribedController = staminaController;
        subscribedController.OnStaminaChanged += Refresh;
    }

    private void Unsubscribe()
    {
        if (subscribedController != null)
            subscribedController.OnStaminaChanged -= Refresh;

        subscribedController = null;
    }

    private void Refresh(float current, float max)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (staminaController == null)
            return;

        float max = Mathf.Max(1f, staminaController.MaxStamina);
        float current = Mathf.Clamp(staminaController.CurrentStamina, 0f, max);
        float normalized = current / max;

        if (staminaFill != null)
        {
            staminaFill.color = fillColor;
            staminaFill.type = Image.Type.Simple;
            staminaFill.fillAmount = normalized;
            RefreshFillRect(staminaFill.rectTransform, normalized);
        }

        if (staminaText != null)
            staminaText.text = Mathf.CeilToInt(current) + " / " + Mathf.CeilToInt(max);
    }

    private void RefreshFillRect(RectTransform fillRect, float normalized)
    {
        if (fillRect == null)
            return;

        fillRect.localScale = new Vector3(Mathf.Clamp01(normalized), 1f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
    }

    private PlayerStaminaController FindStaminaController()
    {
        PlayerMovement player = Object.FindFirstObjectByType<PlayerMovement>(FindObjectsInactive.Include);
        if (player != null)
        {
            PlayerStaminaController controller = player.GetComponentInParent<PlayerStaminaController>();
            if (controller != null)
                return controller;

            controller = player.GetComponentInChildren<PlayerStaminaController>(true);
            if (controller != null)
                return controller;
        }

        return Object.FindFirstObjectByType<PlayerStaminaController>(FindObjectsInactive.Include);
    }
}
