using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class EnemyHpBarView : MonoBehaviour
{
    private const int RoundedSpriteSize = 32;
    private const float RoundedSpriteRadius = 8f;
    private const float RoundedSpritePixelsPerUnit = 100f;

    private static Sprite roundedSprite;

    [Header("Target")]
    [SerializeField] private CombatHealth health;
    [SerializeField] private OverburstEnemyHealthBarView rpgView;

    [Header("UI")]
    [SerializeField] private RectTransform barRoot;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private RectTransform segmentLineRoot;
    [SerializeField] private Image segmentLinePrefab;
    [SerializeField] private ElementalStatusIconStrip elementalStatusIcons;

    [Header("Tier Nameplate")]
    [SerializeField] private CanvasGroup visualCanvasGroup;
    [SerializeField] private Image currentHealthImage;
    [SerializeField] private Image damageTrailImage;
    [SerializeField] private TextMeshProUGUI compactNameText;

    [Header("Visibility")]
    [SerializeField] private bool hideUntilDamaged = true;
    [Min(0.1f)]
    [SerializeField] private float visibleSeconds = 1.35f;
    [Min(0.01f)]
    [SerializeField] private float fadeSeconds = 0.22f;
    [Min(0f)]
    [SerializeField] private float trailHoldSeconds = 0.09f;
    [Min(0.01f)]
    [SerializeField] private float trailCatchupSeconds = 0.42f;

    [Header("Simple Style")]
    [SerializeField] private bool useRoundedStyle = true;
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.95f);
    [SerializeField] private Color fillColor = new Color(0.9f, 0f, 0f, 1f);
    [SerializeField] private Color segmentLineColor = Color.black;

    [Header("Size Setting")]
    [Min(1f)]
    [SerializeField] private float fixedFillWidth = 40f;
    [Min(0.1f)]
    [SerializeField] private float horizontalSizeMultiplier = 1f;
    [Min(1f)]
    [SerializeField] private float barHeight = 6f;

    [Header("Segment Setting")]
    [SerializeField] private bool showSegments;
    [Min(1f)]
    [SerializeField] private float hpPerSegment = 10f;
    [Min(1)]
    [SerializeField] private int maxVisibleSegmentCount = 10;

    [Header("Frame Padding")]
    [Min(0f)]
    [SerializeField] private float fillHorizontalPadding;
    [Min(0f)]
    [SerializeField] private float fillVerticalPadding;

    [Header("AI State Debug")]
    [SerializeField] private bool showAiStateDebug = true;
    [SerializeField] private Color aiStateDebugColor = new Color(1f, 0.82f, 0.12f, 1f); // 미지정 상태 기본색
    [SerializeField] private Color roamStateDebugColor = new Color(0.78f, 0.82f, 0.86f, 1f); // 평시 회색
    [SerializeField] private Color suspiciousStateDebugColor = new Color(1f, 0.83f, 0.15f, 1f); // 경계 노랑
    [SerializeField] private Color chaseStateDebugColor = new Color(1f, 0.45f, 0.08f, 1f); // 추적 주황
    [SerializeField] private Color combatWaitStateDebugColor = new Color(0.2f, 0.85f, 1f, 1f); // 대치 청록
    [SerializeField] private Color attackStateDebugColor = new Color(1f, 0.15f, 0.12f, 1f); // 공격 빨강
    [SerializeField] private Color repositionStateDebugColor = new Color(0.78f, 0.38f, 1f, 1f); // 재배치 보라
    [SerializeField] private Color defendStateDebugColor = new Color(0.28f, 0.55f, 1f, 1f); // 방어 파랑
    [SerializeField] private Color returnStateDebugColor = new Color(0.25f, 1f, 0.45f, 1f); // 복귀 초록
    [SerializeField] private Color deadStateDebugColor = new Color(0.38f, 0.38f, 0.42f, 1f); // 사망 진회색
    [Min(0.05f)]
    [SerializeField] private float aiStateRefreshInterval = 0.1f;

    private CombatHealth subscribedHealth;
    private RectTransform fillRect;
    private CanvasGroup barCanvasGroup;
    private float hideTime;
    private float lastHealthFraction = -1f;
    private float trailStartFill;
    private float trailTargetFill;
    private float trailStartsAt;
    private bool trailAnimationActive;
    private bool barVisible;
    private bool projectionVisible = true;
    private float cachedMaxHp = -1f;
    private float cachedFixedFillWidth = -1f;
    private float cachedHorizontalSizeMultiplier = -1f;
    private float cachedBarHeight = -1f;
    private float cachedHpPerSegment = -1f;
    private int cachedMaxVisibleSegmentCount = -1;
    private float cachedFillHorizontalPadding = -1f;
    private float cachedFillVerticalPadding = -1f;
    private bool cachedShowSegments;
    private EnemyAIController aiController;
    private TextMeshProUGUI aiStateDebugText;
    private string cachedAiStateName;
    private float nextAiStateRefreshTime;

    public RectTransform RectTransform => transform as RectTransform;
    private bool IsAiStateDebugVisible => showAiStateDebug && CombatDebugSettings.ShowEnemyAiStateDebug;

    private void Awake()
    {
        ResolveHealth();
        CacheUiReferences();
        BindElementalStatusIcons();
        ApplySimpleStyle();
        RefreshBarLayoutIfNeeded();
        RefreshFillAmount();
        HideImmediately();
    }

    private void OnEnable()
    {
        CombatDebugSettings.EnemyAiStateDebugChanged += HandleAiStateDebugChanged;
        ResolveHealth();
        BindElementalStatusIcons();
        SubscribeToHealth();
        RefreshBarLayoutIfNeeded();
        RefreshFillAmount();

        if (hideUntilDamaged)
            HideImmediately();
        else
            ShowBar();
    }

    private void OnDisable()
    {
        CombatDebugSettings.EnemyAiStateDebugChanged -= HandleAiStateDebugChanged;
        UnsubscribeFromElementalStatusIcons();
        UnsubscribeFromHealth();
    }

    private void Update()
    {
        if (health == null)
        {
            ResolveHealth();
            SubscribeToHealth();
        }

        if (IsAiStateDebugVisible && Time.unscaledTime >= nextAiStateRefreshTime)
            RefreshAiStateDebugText();

        UpdateDamageTrail();
        if (hideUntilDamaged && barVisible && Time.unscaledTime >= hideTime)
        {
            if (Time.unscaledTime >= hideTime + fadeSeconds)
                HideBar();
            else
                RefreshVisibility();
        }
    }

    public void Bind(CombatHealth targetHealth)
    {
        if (health == targetHealth && subscribedHealth == targetHealth)
            return;

        UnsubscribeFromHealth();
        health = targetHealth;
        lastHealthFraction = -1f;
        trailAnimationActive = false;
        aiController = health != null ? health.GetComponent<EnemyAIController>() : null;
        BindElementalStatusIcons();
        SubscribeToHealth();
        RefreshBarLayoutIfNeeded(true);
        RefreshFillAmount();
        RefreshAiStateDebugText(true);
        hideTime = 0f;
        barVisible = !hideUntilDamaged;
        RefreshVisibility();
    }

    public void Unbind()
    {
        UnsubscribeFromHealth();
        health = null;
        lastHealthFraction = -1f;
        trailAnimationActive = false;
        aiController = null;
        if (elementalStatusIcons != null)
            elementalStatusIcons.Unbind();
        cachedAiStateName = null;
        if (aiStateDebugText != null)
            aiStateDebugText.text = string.Empty;
        barVisible = false;
        projectionVisible = false;
        RefreshVisibility();
    }

    public void SetProjectionVisible(bool visible)
    {
        if (projectionVisible == visible)
            return;

        projectionVisible = visible;
        RefreshVisibility();
    }

    private void ResolveHealth()
    {
        if (health == null)
            health = GetComponentInParent<CombatHealth>();
    }

    private void CacheUiReferences()
    {
        if (barRoot == null)
            barRoot = transform as RectTransform;

        if (backgroundImage == null && barRoot != null)
            backgroundImage = barRoot.GetComponent<Image>();

        fillRect = fillImage != null ? fillImage.rectTransform : null;
        if (barRoot == null)
            return;

        if (elementalStatusIcons == null)
            elementalStatusIcons = GetComponent<ElementalStatusIconStrip>();

        barCanvasGroup = barRoot.GetComponent<CanvasGroup>();
        if (barCanvasGroup == null)
            barCanvasGroup = barRoot.gameObject.AddComponent<CanvasGroup>();

        barCanvasGroup.interactable = false;
        barCanvasGroup.blocksRaycasts = false;
        EnsureAiStateDebugText();
    }

    private void SubscribeToHealth()
    {
        if (!isActiveAndEnabled || health == null || subscribedHealth == health)
            return;

        UnsubscribeFromHealth();
        subscribedHealth = health;
        subscribedHealth.OnDamaged += HandleDamaged;
        subscribedHealth.OnHealthChanged += HandleHealthChanged;
        subscribedHealth.OnDead += HandleDead;
    }

    private void UnsubscribeFromHealth()
    {
        if (subscribedHealth == null)
            return;

        subscribedHealth.OnDamaged -= HandleDamaged;
        subscribedHealth.OnHealthChanged -= HandleHealthChanged;
        subscribedHealth.OnDead -= HandleDead;
        subscribedHealth = null;
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        RefreshFillAmount();
        if (source == null || source.IsDead)
            return;

        hideTime = Time.unscaledTime + visibleSeconds;
        ShowBar();
    }

    private void HandleHealthChanged(CombatHealth source, float currentHp, float maxHp)
    {
        RefreshBarLayoutIfNeeded();
        RefreshFillAmount();
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        barVisible = false;
        RefreshVisibility();
    }

    private void ApplySimpleStyle()
    {
        if (rpgView != null || currentHealthImage != null) return;
        if (backgroundImage != null)
        {
            backgroundImage.color = backgroundColor;
            backgroundImage.raycastTarget = false;
            ApplyRoundedSprite(backgroundImage);
        }

        if (fillImage != null)
        {
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;
            ApplyRoundedSprite(fillImage);
        }

        if (segmentLinePrefab != null)
        {
            segmentLinePrefab.color = segmentLineColor;
            segmentLinePrefab.raycastTarget = false;
            segmentLinePrefab.gameObject.SetActive(false);
        }
    }

    private void ApplyRoundedSprite(Image image)
    {
        if (!useRoundedStyle || image == null)
            return;

        image.sprite = GetRoundedSprite();
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
    }

    private void RefreshBarLayoutIfNeeded(bool force = false)
    {
        if (rpgView != null || currentHealthImage != null) return;
        float maxHp = health != null ? health.MaxHp : 1f;
        if (!force
            && Mathf.Approximately(cachedMaxHp, maxHp)
            && Mathf.Approximately(cachedFixedFillWidth, fixedFillWidth)
            && Mathf.Approximately(cachedHorizontalSizeMultiplier, horizontalSizeMultiplier)
            && Mathf.Approximately(cachedBarHeight, barHeight)
            && Mathf.Approximately(cachedHpPerSegment, hpPerSegment)
            && cachedMaxVisibleSegmentCount == maxVisibleSegmentCount
            && Mathf.Approximately(cachedFillHorizontalPadding, fillHorizontalPadding)
            && Mathf.Approximately(cachedFillVerticalPadding, fillVerticalPadding)
            && cachedShowSegments == showSegments)
        {
            return;
        }

        cachedMaxHp = maxHp;
        cachedFixedFillWidth = fixedFillWidth;
        cachedHorizontalSizeMultiplier = horizontalSizeMultiplier;
        cachedBarHeight = barHeight;
        cachedHpPerSegment = hpPerSegment;
        cachedMaxVisibleSegmentCount = maxVisibleSegmentCount;
        cachedFillHorizontalPadding = fillHorizontalPadding;
        cachedFillVerticalPadding = fillVerticalPadding;
        cachedShowSegments = showSegments;

        float fillWidth = Mathf.Max(1f, fixedFillWidth * horizontalSizeMultiplier);
        float fillHeight = Mathf.Max(1f, barHeight - fillVerticalPadding * 2f);
        float barWidth = fillWidth + fillHorizontalPadding * 2f;

        if (barRoot != null)
            barRoot.sizeDelta = new Vector2(barWidth, barHeight);

        LayoutAiStateDebugText(barWidth);

        if (fillRect != null)
        {
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = new Vector2(fillHorizontalPadding, 0f);
            fillRect.sizeDelta = new Vector2(fillWidth, fillHeight);
            fillRect.localScale = Vector3.one;
        }

        if (segmentLineRoot != null)
        {
            segmentLineRoot.anchorMin = new Vector2(0f, 0.5f);
            segmentLineRoot.anchorMax = new Vector2(0f, 0.5f);
            segmentLineRoot.pivot = new Vector2(0f, 0.5f);
            segmentLineRoot.anchoredPosition = new Vector2(fillHorizontalPadding, 0f);
            segmentLineRoot.sizeDelta = new Vector2(fillWidth, fillHeight);
            segmentLineRoot.localScale = Vector3.one;
            segmentLineRoot.gameObject.SetActive(showSegments);
        }

        RebuildSegmentLines(fillWidth, fillHeight, maxHp);
    }

    private void RefreshFillAmount()
    {
        if (health == null)
            return;

        if (currentHealthImage != null)
        {
            float next = Mathf.Clamp01(health.NormalizedHp);
            float previous = lastHealthFraction;
            if (rpgView != null)
                rpgView.PresentTarget(ResolveDisplayName(), ResolveGradeName(), health.GetComponent<EnemyRank>()?.Level ?? 1, next);
            if (compactNameText != null)
                compactNameText.text = ResolveDisplayName();

            currentHealthImage.fillAmount = next;
            currentHealthImage.enabled = next > 0.001f;
            if (damageTrailImage != null)
            {
                if (previous < 0f || next > previous + 0.001f)
                {
                    damageTrailImage.fillAmount = next;
                    trailAnimationActive = false;
                }
                else if (next < previous - 0.001f)
                {
                    trailStartFill = Mathf.Max(previous, damageTrailImage.fillAmount);
                    trailTargetFill = next;
                    damageTrailImage.fillAmount = trailStartFill;
                    trailStartsAt = Time.unscaledTime + trailHoldSeconds;
                    trailAnimationActive = true;
                }
            }
            lastHealthFraction = next;
            return;
        }

        if (rpgView != null)
        {
            rpgView.PresentTarget(ResolveDisplayName(), ResolveGradeName(), health.GetComponent<EnemyRank>()?.Level ?? 1, health.NormalizedHp);
            return;
        }
        if (fillRect == null || fillImage == null || health == null)
            return;

        float fillWidth = Mathf.Max(1f, fixedFillWidth * horizontalSizeMultiplier);
        float fillHeight = Mathf.Max(1f, barHeight - fillVerticalPadding * 2f);
        float hpRate = health.NormalizedHp;

        fillImage.enabled = hpRate > 0.001f;
        fillImage.fillAmount = 1f;
        fillRect.sizeDelta = new Vector2(fillWidth * hpRate, fillHeight);
    }

    private void UpdateDamageTrail()
    {
        if (!trailAnimationActive || damageTrailImage == null || Time.unscaledTime < trailStartsAt)
            return;

        float t = Mathf.Clamp01((Time.unscaledTime - trailStartsAt) / trailCatchupSeconds);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        damageTrailImage.fillAmount = Mathf.Lerp(trailStartFill, trailTargetFill, eased);
        if (t >= 1f)
            trailAnimationActive = false;
    }

    private string ResolveDisplayName()
    {
        EnemyIdentity identity = health.GetComponent<EnemyIdentity>();
        if (identity != null)
            return identity.DisplayName;
        EnemyRank rank = health.GetComponent<EnemyRank>();
        return rank != null ? rank.DisplayName : health.name;
    }

    private string ResolveGradeName()
    {
        EnemyIdentity identity = health.GetComponent<EnemyIdentity>();
        if (identity != null && identity.GradeType != EnemyGradeType.Normal)
            return identity.GradeType.ToString();
        EnemyRank rank = health.GetComponent<EnemyRank>();
        return rank != null ? rank.GradeType.ToString() : "Normal";
    }

    private void RebuildSegmentLines(float fillWidth, float fillHeight, float maxHp)
    {
        if (segmentLineRoot == null || segmentLinePrefab == null)
            return;

        for (int i = segmentLineRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = segmentLineRoot.GetChild(i);
            if (child.gameObject == segmentLinePrefab.gameObject)
                continue;

            Destroy(child.gameObject);
        }

        segmentLinePrefab.gameObject.SetActive(false);
        if (!showSegments || hpPerSegment <= 0f || maxHp <= hpPerSegment)
            return;

        int rawLineCount = Mathf.FloorToInt((maxHp - 0.001f) / hpPerSegment);
        int maxLineCount = Mathf.Max(0, maxVisibleSegmentCount - 1);
        int lineCount = Mathf.Min(rawLineCount, maxLineCount);
        if (lineCount <= 0)
            return;

        bool capped = lineCount < rawLineCount;
        for (int i = 1; i <= lineCount; i++)
        {
            Image line = Instantiate(segmentLinePrefab, segmentLineRoot);
            line.gameObject.SetActive(true);
            line.color = segmentLineColor;

            RectTransform lineRect = line.rectTransform;
            float ratio = capped ? i / (float)(lineCount + 1) : Mathf.Clamp01(i * hpPerSegment / maxHp);
            lineRect.anchorMin = new Vector2(0f, 0.5f);
            lineRect.anchorMax = new Vector2(0f, 0.5f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.anchoredPosition = new Vector2(fillWidth * ratio, 0f);
            lineRect.sizeDelta = new Vector2(1f, fillHeight);
            lineRect.localScale = Vector3.one;
        }
    }

    private void ShowBar()
    {
        barVisible = true;
        RefreshVisibility();
    }

    private void HideImmediately()
    {
        hideTime = 0f;
        HideBar();
    }

    private void HideBar()
    {
        if (!hideUntilDamaged)
            return;

        barVisible = false;
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        bool debugVisible = IsAiStateDebugVisible && aiController != null && aiStateDebugText != null;
        SetBarAlpha(projectionVisible && (barVisible || debugVisible) ? 1f : 0f);

        float hpAlpha = barVisible && projectionVisible ? 1f : 0f;
        if (hideUntilDamaged && barVisible && Time.unscaledTime > hideTime)
            hpAlpha *= 1f - Mathf.Clamp01((Time.unscaledTime - hideTime) / fadeSeconds);
        if (visualCanvasGroup != null)
            visualCanvasGroup.alpha = hpAlpha;
        if (elementalStatusIcons != null)
            elementalStatusIcons.SetPresentationVisible(hpAlpha > 0f);
        if (backgroundImage != null)
            backgroundImage.canvasRenderer.SetAlpha(hpAlpha);
        if (fillImage != null)
            fillImage.canvasRenderer.SetAlpha(hpAlpha);
        if (segmentLineRoot != null)
            segmentLineRoot.gameObject.SetActive(showSegments && hpAlpha > 0f);
        if (aiStateDebugText != null)
            aiStateDebugText.gameObject.SetActive(debugVisible);
    }

    private void SetBarAlpha(float alpha)
    {
        if (barCanvasGroup == null)
            CacheUiReferences();

        if (barCanvasGroup != null)
            barCanvasGroup.alpha = alpha;
    }

    private void EnsureAiStateDebugText()
    {
        if (!showAiStateDebug || barRoot == null || aiStateDebugText != null)
            return;

        Transform existing = barRoot.Find("StateDebugText");
        if (existing != null)
            aiStateDebugText = existing.GetComponent<TextMeshProUGUI>();

        if (aiStateDebugText == null)
        {
            GameObject labelObject = new GameObject(
                "StateDebugText",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(barRoot, false);
            aiStateDebugText = labelObject.GetComponent<TextMeshProUGUI>();
        }

        aiStateDebugText.raycastTarget = false;
        aiStateDebugText.alignment = TextAlignmentOptions.Center;
        aiStateDebugText.fontSize = 13f;
        aiStateDebugText.fontStyle = FontStyles.Bold;
        aiStateDebugText.color = aiStateDebugColor;
        aiStateDebugText.outlineColor = new Color32(0, 0, 0, 255);
        aiStateDebugText.outlineWidth = 0.2f;
        aiStateDebugText.overflowMode = TextOverflowModes.Overflow;
        aiStateDebugText.text = string.Empty;
        aiStateDebugText.rectTransform.SetAsLastSibling();
    }

    private void LayoutAiStateDebugText(float barWidth)
    {
        EnsureAiStateDebugText();
        if (aiStateDebugText == null)
            return;

        RectTransform rect = aiStateDebugText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, barHeight * 0.5f + 10f);
        rect.sizeDelta = new Vector2(Mathf.Max(140f, barWidth + 40f), 20f);
        rect.localScale = Vector3.one;
    }

    private void RefreshAiStateDebugText(bool force = false)
    {
        EnsureAiStateDebugText();
        nextAiStateRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, aiStateRefreshInterval);
        if (aiStateDebugText == null)
            return;

        if (aiController == null && health != null)
            aiController = health.GetComponent<EnemyAIController>();

        string debugStateName = aiController != null ? aiController.CurrentDebugStateName : "No AI";
        string stateName = aiController != null ? aiController.CurrentStateName : "None";
        if (force || cachedAiStateName != debugStateName)
        {
            cachedAiStateName = debugStateName;
            aiStateDebugText.text = debugStateName;
        }

        aiStateDebugText.color = ResolveAiStateDebugColor(stateName); // 현재 주 상태 색상 반영

        RefreshVisibility();
    }

    private Color ResolveAiStateDebugColor(string stateName)
    {
        switch (stateName)
        {
            case "Roam":
                return roamStateDebugColor;
            case "Suspicious":
                return suspiciousStateDebugColor;
            case "Chase":
                return chaseStateDebugColor;
            case "CombatWait":
                return combatWaitStateDebugColor;
            case "Attack":
                return attackStateDebugColor;
            case "Reposition":
                return repositionStateDebugColor;
            case "Defend":
                return defendStateDebugColor;
            case "Return":
                return returnStateDebugColor;
            case "Dead":
                return deadStateDebugColor;
            default:
                return aiStateDebugColor;
        }
    }

    private void HandleAiStateDebugChanged(bool visible)
    {
        if (visible)
            RefreshAiStateDebugText(true);
        else
            RefreshVisibility();
    }

    private void BindElementalStatusIcons()
    {
        if (elementalStatusIcons == null)
            elementalStatusIcons = GetComponent<ElementalStatusIconStrip>();
        if (elementalStatusIcons == null)
            return;

        elementalStatusIcons.VisibilityChanged -= HandleElementalStatusVisibilityChanged;
        elementalStatusIcons.VisibilityChanged += HandleElementalStatusVisibilityChanged;
        elementalStatusIcons.Bind(health);
    }

    private void UnsubscribeFromElementalStatusIcons()
    {
        if (elementalStatusIcons != null)
            elementalStatusIcons.VisibilityChanged -= HandleElementalStatusVisibilityChanged;
    }

    private void HandleElementalStatusVisibilityChanged(bool visible)
    {
        RefreshVisibility();
    }

    private static Sprite GetRoundedSprite()
    {
        if (roundedSprite != null)
            return roundedSprite;

        Texture2D texture = new Texture2D(RoundedSpriteSize, RoundedSpriteSize, TextureFormat.RGBA32, false)
        {
            name = "Generated_EnemyHpBar_RoundedSprite",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32 solid = new Color32(255, 255, 255, 255);
        Color32 clear = new Color32(255, 255, 255, 0);
        for (int y = 0; y < RoundedSpriteSize; y++)
        {
            for (int x = 0; x < RoundedSpriteSize; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float cx = Mathf.Clamp(px, RoundedSpriteRadius, RoundedSpriteSize - RoundedSpriteRadius);
                float cy = Mathf.Clamp(py, RoundedSpriteRadius, RoundedSpriteSize - RoundedSpriteRadius);
                float dx = px - cx;
                float dy = py - cy;
                texture.SetPixel(x, y, dx * dx + dy * dy <= RoundedSpriteRadius * RoundedSpriteRadius ? solid : clear);
            }
        }

        texture.Apply(false, true);
        roundedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, RoundedSpriteSize, RoundedSpriteSize),
            new Vector2(0.5f, 0.5f),
            RoundedSpritePixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            new Vector4(RoundedSpriteRadius, RoundedSpriteRadius, RoundedSpriteRadius, RoundedSpriteRadius));
        roundedSprite.name = "Generated_EnemyHpBar_RoundedSprite";
        roundedSprite.hideFlags = HideFlags.HideAndDontSave;
        return roundedSprite;
    }
}
