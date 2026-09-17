using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(10000)]
public class PlayerDamageFeedback : MonoBehaviour // 플레이어 피격 피드백
{
    private static Sprite vignetteSprite; // 공유 sprite

    [Header("References")]
    [SerializeField] private PlayerContext playerContext;
    [SerializeField] private CombatHealth playerHealth;
    [SerializeField] private Image vignetteImage;
    [SerializeField] private CanvasGroup vignetteGroup;
    [SerializeField] private bool autoResolveReferences = true;

    [Header("Vignette")]
    [SerializeField] private Color vignetteColor = new Color(1f, 0.04f, 0.02f, 1f);
    [SerializeField] private float vignettePeakAlpha = 0.28f;
    [SerializeField] private float vignetteFadeDuration = 0.38f;

    [Header("Camera Shake")]
    [SerializeField] private float shakeDuration = 0.11f;
    [SerializeField] private float shakeAmplitude = 0.045f;
    [SerializeField] private bool ignoreDamageOverTime = true;

    private float feedbackStartTime; // 시작 시각
    private float vignetteEndTime; // vignette 종료
    private float activeVignetteDuration; // 현재 vignette 길이
    private bool missingReferenceWarned; // 경고 중복 방지
    private PlayerContext subscribedPlayerContext;
    private CombatHealth subscribedHealth;

    private void Awake()
    {
        BindSceneUI();
        ResolveReferences();
        SetVignetteAlpha(0f);
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeLeaderContext();
        SubscribeHealth();
    }

    private void OnDisable()
    {
        UnsubscribeHealth();
        UnsubscribeLeaderContext();
        SetVignetteAlpha(0f);
    }

    private void LateUpdate()
    {
        if (autoResolveReferences)
        {
            ResolveReferences();
            SubscribeLeaderContext();
            SubscribeHealth();
        }

        UpdateVignette();
    }

    public void TriggerFeedback()
    {
        StartFeedback(Time.time, Vector3.zero); // 테스트 호출
    }

    public static void TriggerGlobal()
    {
        PlayerDamageFeedback feedback = FindFirstObjectByType<PlayerDamageFeedback>(FindObjectsInactive.Include);
        if (feedback == null || !feedback.isActiveAndEnabled)
            return;

        feedback.StartFeedback(Time.time, Vector3.zero);
    }

    private void HandleDamaged(CombatHealth health, DamageInfo info)
    {
        if (health == null || health.IsDead)
            return; // 사망 후 무시

        if (ignoreDamageOverTime && info.isDamageOverTime)
            return; // DoT 제외

        if (!info.triggersOnHitEffects)
            return; // 피격 효과 제외

        StartFeedback(Time.time, info.direction);
    }

    private void StartFeedback(float now, Vector3 damageDirection)
    {
        feedbackStartTime = now;
        activeVignetteDuration = Mathf.Max(0.01f, vignetteFadeDuration);
        vignetteEndTime = now + activeVignetteDuration;
        SetVignetteAlpha(vignettePeakAlpha);
        CombatHitFeedbackService.RequestPlayerDamage(damageDirection, shakeDuration, shakeAmplitude);
    }

    private void UpdateVignette()
    {
        if (vignetteGroup == null)
            return;

        if (Time.time >= vignetteEndTime)
        {
            SetVignetteAlpha(0f);
            return;
        }

        float progress = Mathf.Clamp01((Time.time - feedbackStartTime) / activeVignetteDuration);
        float eased = Mathf.SmoothStep(0f, 1f, progress); // 자연 감쇠
        SetVignetteAlpha(Mathf.Lerp(Mathf.Clamp01(vignettePeakAlpha), 0f, eased));
    }


    private void BindSceneUI()
    {
        if (vignetteImage == null)
            vignetteImage = GetComponent<Image>();

        if (vignetteGroup == null)
            vignetteGroup = GetComponent<CanvasGroup>();

        if (vignetteGroup != null)
        {
            vignetteGroup.blocksRaycasts = false;
            vignetteGroup.interactable = false;
        }

        RectTransform rect = transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        if (vignetteImage != null)
        {
            vignetteImage.sprite = GetVignetteSprite();
            vignetteImage.color = vignetteColor;
            vignetteImage.raycastTarget = false;
            vignetteImage.type = Image.Type.Simple;
        }

        if (vignetteImage == null || vignetteGroup == null || rect == null)
            WarnMissingConfiguration();
    }

    private void ResolveReferences()
    {
        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        CombatHealth leaderHealth = playerContext != null
            ? playerContext.CurrentActorHealth
            : null;
        if (leaderHealth != null)
            playerHealth = leaderHealth;
   

    }

    private void SubscribeHealth()
    {
        if (subscribedHealth == playerHealth)
            return;

        UnsubscribeHealth();
        subscribedHealth = playerHealth;
        if (subscribedHealth != null)
            subscribedHealth.OnDamaged += HandleDamaged;
    }

    private void UnsubscribeHealth()
    {
        if (subscribedHealth != null)
            subscribedHealth.OnDamaged -= HandleDamaged;

        subscribedHealth = null;
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
        playerHealth = leader != null ? leader.Health : null;
        SubscribeHealth();
    }

    private void SetVignetteAlpha(float alpha)
    {
        if (vignetteGroup != null)
            vignetteGroup.alpha = Mathf.Clamp01(alpha);
    }

    private static CombatHealth FindPlayerHealth()
    {
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        if (player != null)
            return player.GetComponentInParent<CombatHealth>();

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        return playerObject != null ? playerObject.GetComponentInParent<CombatHealth>() : null;
    }

    private static Sprite GetVignetteSprite()
    {
        if (vignetteSprite != null)
            return vignetteSprite;

        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PlayerDamageVignetteSprite"
        };

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxDistance = center.magnitude;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance01 = Vector2.Distance(new Vector2(x, y), center) / maxDistance;
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 1f, distance01));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        vignetteSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        vignetteSprite.name = "PlayerDamageVignetteSprite";
        return vignetteSprite;
    }

    private void WarnMissingConfiguration()
    {
        if (missingReferenceWarned)
            return;

        Debug.LogWarning("[PlayerDamageFeedback] HUDCanvas 하위 PlayerDamageFeedbackUI의 Image, CanvasGroup 또는 RectTransform 참조를 확인하세요.");
        missingReferenceWarned = true;
    }
}
