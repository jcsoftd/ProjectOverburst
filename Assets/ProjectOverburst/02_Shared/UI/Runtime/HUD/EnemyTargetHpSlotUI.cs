using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 2026-10-01: 상단 대상 HUD(일반·정예). 머리 위 정예 HP바(PF_EnemyHpBar_Elite_Tier)와 같은 네임플레이트 재료로 다시 짰다.
// 구성은 Assets/Editor/Builders/UI/EnemyTargetHudRpg11Builder.cs가 만든다.
// 마름모 안 레벨, 막대 위 '이름 + 등급', 막대 안 체력(왼쪽)·퍼센트(오른쪽), 주황 잔상, 막대 아래 원소 상태 칸.
public sealed class EnemyTargetHpSlotUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image trailImage;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI percentText;
    [SerializeField] private EnemyTargetStatusRow statusRow;
    [Tooltip("원소 아이콘 스프라이트 원본. 표시는 statusRow가 한다.")]
    [SerializeField] private ElementalStatusIconStrip elementalStatusIcons;

    [Header("등급별 모양")]
    [SerializeField] private RectTransform frameRoot;
    [SerializeField] private RectTransform barRoot;
    [Tooltip("막대 폭마다 끝 사선을 원래 크기로 둔 채움 그림(일반 640·정예 820). 100%일 때 끝이 테두리와 맞는다.")]
    [SerializeField] private Sprite normalFillSprite;
    [SerializeField] private Sprite eliteFillSprite;
    [SerializeField] private float normalBarWidth = 640f;
    [SerializeField] private float eliteBarWidth = 820f;
    [SerializeField] private float frameExtraWidth = 110f; // 마름모(왼쪽 98) + 테두리 끝(오른쪽 12)
    [SerializeField] private Color normalLevelColor = new Color(0.93f, 0.9f, 0.83f, 1f);
    [SerializeField] private Color eliteLevelColor = new Color(0.965f, 0.84f, 0.59f, 1f);
    [SerializeField] private string eliteTagColor = "#EC843A";

    private readonly HudDamageTrail trail = new HudDamageTrail();
    private float shownCurrent = -1f;
    private float shownMax = -1f;

    private void Awake()
    {
        if (root == null)
            root = gameObject;
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (elementalStatusIcons == null)
            elementalStatusIcons = GetComponent<ElementalStatusIconStrip>();
        trail.Bind(trailImage);
    }

    private void LateUpdate()
    {
        trail.Tick();
    }

    public void Show(CombatHealth health, EnemyRank rank, float preHitHp = -1f)
    {
        if (root == null)
            root = gameObject;

        if (health == null)
        {
            Hide();
            return;
        }

        SetVisible(true);
        statusRow?.Bind(health);

        EnemyRankType rankType = rank != null ? rank.Rank : EnemyRankType.Normal;
        string displayName = rank != null ? rank.DisplayName : health.gameObject.name.Replace("(Clone)", string.Empty).Trim();
        ApplyRank(rankType, string.IsNullOrWhiteSpace(displayName) ? "Enemy" : displayName, rank != null ? rank.Level : 1);

        float maxHp = Mathf.Max(0f, health.MaxHp);
        float normalized = maxHp > 0f ? Mathf.Clamp01(health.CurrentHp / maxHp) : 0f;
        // 새 대상의 첫 타격도 잔상이 보이게, 맞기 전 체력(알 때)에서 시작해 지금 체력으로 따라잡는다.
        float start = preHitHp >= 0f && maxHp > 0f ? Mathf.Max(normalized, Mathf.Clamp01(preHitHp / maxHp)) : normalized;
        trail.Bind(trailImage);
        trail.Snap(start);
        shownCurrent = shownMax = -1f;
        Refresh(health);
    }

    public void Refresh(CombatHealth health)
    {
        if (health == null)
            return;

        float maxHp = Mathf.Max(0f, health.MaxHp);
        ApplyHealth(Mathf.Clamp(health.CurrentHp, 0f, maxHp), maxHp);
    }

    public void ShowPreview(string displayName, EnemyRankType rankType, float currentHp, float maxHp, int level = 1)
    {
        if (root == null)
            root = gameObject;

        statusRow?.Unbind();
        SetVisible(true);
        ApplyRank(rankType, displayName, level);
        float safeMax = Mathf.Max(0f, maxHp);
        trail.Bind(trailImage);
        trail.Snap(safeMax > 0f ? Mathf.Clamp01(currentHp / safeMax) : 0f);
        shownCurrent = shownMax = -1f;
        ApplyHealth(Mathf.Clamp(currentHp, 0f, safeMax), safeMax);
    }

    // 편집 모드 캡처용: 잔상 칸(주황)과 원소 상태를 정해진 값으로 보인다.
    public void ShowPreviewDetail(float trailHp, float maxHp, WeaponElement[] elements, float[] remaining01, int[] stacks)
    {
        if (trailImage != null && maxHp > 0f)
            trailImage.fillAmount = Mathf.Clamp01(trailHp / maxHp);
        statusRow?.ShowPreview(elements, remaining01, stacks);
    }

    public void Hide()
    {
        if (root == null)
            root = gameObject;

        statusRow?.Unbind();
        elementalStatusIcons?.Unbind();
        SetVisible(false);
    }

    private void ApplyRank(EnemyRankType rankType, string displayName, int level)
    {
        bool elite = rankType == EnemyRankType.Elite;
        float width = elite ? eliteBarWidth : normalBarWidth;
        if (barRoot != null)
            barRoot.sizeDelta = new Vector2(width, barRoot.sizeDelta.y);
        if (frameRoot != null)
            frameRoot.sizeDelta = new Vector2(width + frameExtraWidth, frameRoot.sizeDelta.y);
        Sprite fillSprite = elite ? eliteFillSprite : normalFillSprite;
        if (fillSprite != null)
        {
            if (fillImage != null) fillImage.sprite = fillSprite;
            if (trailImage != null) trailImage.sprite = fillSprite;
        }

        if (nameText != null)
        {
            nameText.fontSize = elite ? 32f : 30f;
            nameText.text = elite
                ? displayName + "<space=0.4em><size=75%><color=" + eliteTagColor + ">엘리트</color></size>"
                : displayName;
        }

        if (levelText != null)
        {
            levelText.text = Mathf.Max(1, level).ToString(CultureInfo.InvariantCulture);
            levelText.color = elite ? eliteLevelColor : normalLevelColor;
        }
    }

    private void ApplyHealth(float current, float max)
    {
        float normalized = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        if (fillImage != null)
            fillImage.fillAmount = normalized;
        trail.Set(normalized);

        if (Mathf.Approximately(current, shownCurrent) && Mathf.Approximately(max, shownMax))
            return;

        shownCurrent = current;
        shownMax = max;
        if (hpText != null)
            hpText.text = FormatHealth(current, max);
        if (percentText != null)
            percentText.text = FormatPercent(normalized);
    }

    internal static string FormatHealth(float current, float max)
    {
        return Mathf.CeilToInt(current).ToString("N0", CultureInfo.InvariantCulture) + " / "
            + Mathf.CeilToInt(max).ToString("N0", CultureInfo.InvariantCulture);
    }

    // 살아 있는 대상은 0%로, 다 차지 않은 대상은 100%로 보이지 않게 한다.
    internal static string FormatPercent(float normalized)
    {
        int percent = normalized >= 0.9999f ? 100 : normalized <= 0f ? 0 : Mathf.Clamp(Mathf.FloorToInt(normalized * 100f), 1, 99);
        return percent.ToString(CultureInfo.InvariantCulture) + "%";
    }

    private void SetVisible(bool isVisible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = isVisible ? 1f : 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            return;
        }

        if (root != null)
            root.SetActive(isVisible);
    }
}
