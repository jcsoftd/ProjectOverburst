using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LoadingScreenUI : MonoBehaviour // 로딩 화면
{
    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI hintText;
    [SerializeField] private Image progressFill;
    [SerializeField] private RectTransform themedProgressMask;
    [SerializeField] private Text themedPercentageText;
    [SerializeField] private float themedFillWidth = 3194f;

    private bool warnedMissingReferences;
    private Image artworkBackground;
    private LoadingScreenArtworkCatalog artworkCatalog;
    private Material artworkMaterial;
    private readonly System.Random artworkRandom = new System.Random();
    private float viewportAspect;

    private void Awake()
    {
        ResolveReferences();
        ForceHide(); // 시작 숨김
    }

    private void OnDisable()
    {
        GameplayInputBlocker.Unblock(this); // 잠금 해제
    }

    public void Show(string title, string status,
        LoadingScreenArtworkContext artworkContext = LoadingScreenArtworkContext.Random)
    {
        ResolveReferences();
        ShowArtwork(artworkContext);
        if (titleText != null)
            titleText.text = title;

        SetStatus(status);
        SetProgress(0f);
        SetVisible(true);
        SetInventoryToggleLocked(true);
        GameplayInputBlocker.Block(this); // 입력 차단
    }

    private void LateUpdate()
    {
        if (artworkMaterial != null && canvasGroup != null && canvasGroup.alpha > 0f)
            UpdateArtworkAspect();
    }

    private void OnDestroy()
    {
        GameplayInputBlocker.Unblock(this);
        if (artworkMaterial != null)
        {
            if (Application.isPlaying) Destroy(artworkMaterial);
            else DestroyImmediate(artworkMaterial);
        }
    }

    private void ShowArtwork(LoadingScreenArtworkContext context)
    {
        if (artworkBackground == null)
            artworkBackground = transform.Find("Background")?.GetComponent<Image>();
        if (artworkCatalog == null)
            artworkCatalog = Resources.Load<LoadingScreenArtworkCatalog>(LoadingScreenArtworkCatalog.ResourcePath);
        if (artworkBackground == null || artworkCatalog == null || artworkCatalog.ArtworkMaterial == null)
            return;

        int count = artworkCatalog.RandomArtworkCount;
        Texture2D artwork = artworkCatalog.Select(context, count > 0 ? artworkRandom.Next(count) : 0);
        if (artwork == null)
            return;

        if (artworkMaterial == null)
        {
            artworkMaterial = new Material(artworkCatalog.ArtworkMaterial) { hideFlags = HideFlags.DontSave };
            artworkBackground.material = artworkMaterial;
            artworkBackground.color = Color.white;
        }
        artworkMaterial.SetTexture("_ArtworkTex", artwork);
        artworkMaterial.SetFloat("_ArtworkAspect", (float)artwork.width / artwork.height);
        UpdateArtworkAspect();
    }

    private void UpdateArtworkAspect()
    {
        Rect rect = artworkBackground.rectTransform.rect;
        float aspect = rect.height > 0f ? rect.width / rect.height : 16f / 9f;
        if (!Mathf.Approximately(viewportAspect, aspect))
        {
            viewportAspect = aspect;
            artworkMaterial.SetFloat("_ViewportAspect", aspect);
        }
    }

    public void SetStatus(string status)
    {
        if (statusText != null)
            statusText.text = status;
    }

    public void SetProgress(float value)
    {
        float progress = Mathf.Clamp01(value);
        if (progressFill != null)
            progressFill.fillAmount = progress;
        if (themedProgressMask != null)
            themedProgressMask.sizeDelta = new Vector2(themedFillWidth * progress, themedProgressMask.sizeDelta.y);
        if (themedPercentageText != null)
            themedPercentageText.text = Mathf.RoundToInt(progress * 100f) + "%";
    }

    public void Hide()
    {
        SetProgress(1f);
        SetVisible(false);
        SetInventoryToggleLocked(false);
        GameplayInputBlocker.Unblock(this); // 잠금 해제
    }

    public void ForceHide()
    {
        SetVisible(false);
        SetInventoryToggleLocked(false);
        GameplayInputBlocker.Unblock(this);
    }

    private void ResolveReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>(); // 루트 캔버스 그룹

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (canvasGroup == null || titleText == null || statusText == null || progressFill == null)
            WarnMissingReferences();
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible; // 입력 가림
        canvasGroup.interactable = visible;
    }

    private void WarnMissingReferences()
    {
        if (warnedMissingReferences)
            return;

        Debug.LogWarning("[LoadingScreenUI] LoadingScreen 참조가 일부 비어 있습니다. PersistentScene HUDCanvas 하위 LoadingScreen 연결을 확인하세요.");
        warnedMissingReferences = true;
    }

    private void SetInventoryToggleLocked(bool locked)
    {
        InventoryUI[] inventoryUis = FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < inventoryUis.Length; i++)
        {
            if (inventoryUis[i] != null)
                inventoryUis[i].InputToggleLocked = locked;
        }
    }
}
