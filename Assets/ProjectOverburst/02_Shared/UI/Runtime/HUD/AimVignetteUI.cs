using UnityEngine;
using UnityEngine.UI;

public class AimVignetteUI : MonoBehaviour // 조준 vignette
{
    private static Sprite vignetteSprite; // 비네트 이미지

    [Header("References")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private Image vignetteImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private PlayerContext playerContext;
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private bool autoResolveReferences = true;

    [Header("Vignette")]
    [SerializeField] private Color vignetteColor = Color.black;
    [SerializeField] private float vignetteAlphaMax = 0.3f;
    [SerializeField] private float fadeSpeed = 10f;
    [SerializeField] private bool hideWhenGameplayInputBlocked = true;

    private float currentAlpha; // 현재 alpha
    private bool missingReferenceWarned; // 경고 중복 방지

    private void Awake()
    {
        BindSceneUI();
        ResolveReferences();
        SetAlpha(0f);
    }

    private void Update()
    {
        if (autoResolveReferences)
            ResolveReferences();

        float targetAlpha = ShouldShowVignette() ? Mathf.Clamp01(vignetteAlphaMax) : 0f; // 목표 alpha
        float t = 1f - Mathf.Exp(-Mathf.Max(0f, fadeSpeed) * Time.deltaTime); // 보간값
        currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, t);
        SetAlpha(currentAlpha);
    }

    private void BindSceneUI()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        if (vignetteImage == null)
            vignetteImage = GetComponent<Image>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        RectTransform rect = transform as RectTransform; // 전체 화면 Rect
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

        if (canvas == null || vignetteImage == null || canvasGroup == null || rect == null)
            WarnMissingConfiguration();
    }

    private void ResolveReferences()
    {
        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        PlayerMovement leaderMovement = playerContext != null
            ? playerContext.CurrentActorMovement
            : null;
        if (leaderMovement != null)
            playerController = leaderMovement;
   
    }

    private bool ShouldShowVignette()
    {
        if (hideWhenGameplayInputBlocked && GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        return playerController != null && playerController.IsWeaponAimInputActive;
    }

    private void SetAlpha(float alpha)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = alpha;
    }

    private static Sprite GetVignetteSprite()
    {
        if (vignetteSprite != null)
            return vignetteSprite;

        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) // vignette 텍스처
        {
            name = "AimVignetteSprite"
        };

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f); // 중심점
        float maxDistance = center.magnitude; // 최대 거리
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance01 = Vector2.Distance(new Vector2(x, y), center) / maxDistance;
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, distance01));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        vignetteSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        vignetteSprite.name = "AimVignetteSprite";
        return vignetteSprite;
    }

    private void WarnMissingConfiguration()
    {
        if (missingReferenceWarned)
            return;

        Debug.LogWarning("[AimVignetteUI] Canvas, Image, CanvasGroup 또는 RectTransform 참조가 비어 있습니다. PersistentScene HUDCanvas 하위 AimVignetteUI를 확인하세요.");
        missingReferenceWarned = true;
    }
}
