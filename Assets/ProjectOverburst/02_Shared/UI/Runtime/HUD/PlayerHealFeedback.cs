using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(10001)]
public class PlayerHealFeedback : MonoBehaviour
{
    private static PlayerHealFeedback instance;
    private static Sprite vignetteSprite;

    [SerializeField] private Image vignetteImage;
    [SerializeField] private CanvasGroup vignetteGroup;
    [SerializeField] private Color vignetteColor = new Color(0.12f, 1f, 0.28f, 1f);
    [SerializeField] private float peakAlpha = 0.25f;
    [SerializeField] private float fadeDuration = 1f;

    private float startTime;
    private float endTime;

    private void Awake()
    {
        instance = this;
        BindSceneUI();
        SetAlpha(0f);
    }

    private void OnEnable()
    {
        if (instance == null)
            instance = this;
    }

    private void Update()
    {
        if (vignetteGroup == null)
            return;

        if (Time.time >= endTime)
        {
            SetAlpha(0f);
            return;
        }

        float progress = Mathf.Clamp01((Time.time - startTime) / Mathf.Max(0.01f, fadeDuration));
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        SetAlpha(Mathf.Lerp(Mathf.Clamp01(peakAlpha), 0f, eased));
    }

    public static void TriggerGlobal()
    {
        ResolveInstance();
        if (instance != null)
            instance.Trigger();
    }

    public static void ApplyHealPercent(CombatHealth health, float percent)
    {
        if (health == null)
            return;

        ApplyHeal(health, health.MaxHp * Mathf.Max(0f, percent));
    }

    public static void ApplyHeal(CombatHealth health, float amount)
    {
        if (health == null || health.IsDead || amount <= 0f)
            return;

        float hpBeforeHeal = health.CurrentHp;
        health.Heal(amount);
        float actualHeal = Mathf.Max(0f, health.CurrentHp - hpBeforeHeal);
        if (actualHeal <= 0f)
            return;

        DamageNumberSpawner.SpawnHeal(health.transform.position, actualHeal);
        TriggerGlobal();
    }

    private static PlayerHealFeedback ResolveInstance()
    {
        if (instance != null)
            return instance;

        PlayerHealFeedback existing = FindFirstObjectByType<PlayerHealFeedback>(FindObjectsInactive.Include);
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        return null;
    }

    public void Trigger()
    {
        startTime = Time.time;
        endTime = startTime + Mathf.Max(0.01f, fadeDuration);
        SetAlpha(peakAlpha);
    }

    private void BindSceneUI()
    {
        if (vignetteImage == null)
            vignetteImage = GetComponent<Image>();

        if (vignetteGroup == null)
            vignetteGroup = GetComponent<CanvasGroup>();

        RectTransform rect = transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        if (vignetteGroup != null)
        {
            vignetteGroup.blocksRaycasts = false;
            vignetteGroup.interactable = false;
        }

        if (vignetteImage != null)
        {
            vignetteImage.sprite = GetVignetteSprite();
            vignetteImage.color = vignetteColor;
            vignetteImage.raycastTarget = false;
            vignetteImage.type = Image.Type.Simple;
        }
    }

    private void SetAlpha(float alpha)
    {
        if (vignetteGroup != null)
            vignetteGroup.alpha = Mathf.Clamp01(alpha);
    }

    private static Sprite GetVignetteSprite()
    {
        if (vignetteSprite != null)
            return vignetteSprite;

        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PlayerHealVignetteSprite"
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
        vignetteSprite.name = "PlayerHealVignetteSprite";
        return vignetteSprite;
    }
}
