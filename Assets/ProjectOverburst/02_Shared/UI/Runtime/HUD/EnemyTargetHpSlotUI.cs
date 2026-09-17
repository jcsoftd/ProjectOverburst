using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EnemyTargetHpSlotUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI rankText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image fillImage;
    [SerializeField] private RectTransform fillRect;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private ElementalStatusIconStrip elementalStatusIcons;
    [SerializeField] private Color normalColor = new Color(0.3f, 0.95f, 0.42f, 1f);
    [SerializeField] private Color eliteColor = new Color(1f, 0.72f, 0.18f, 1f);

    private void Awake()
    {
        if (root == null)
            root = gameObject;
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (fillRect == null && fillImage != null)
            fillRect = fillImage.rectTransform;
        if (elementalStatusIcons == null)
            elementalStatusIcons = GetComponent<ElementalStatusIconStrip>();
    }

    public void Show(CombatHealth health, EnemyRank rank)
    {
        if (root == null)
            root = gameObject;

        if (health == null)
        {
            Hide();
            return;
        }

        SetVisible(true);
        if (elementalStatusIcons != null)
            elementalStatusIcons.Bind(health);

        EnemyRankType rankType = rank != null ? rank.Rank : EnemyRankType.Normal;
        string displayName = rank != null ? rank.DisplayName : health.gameObject.name.Replace("(Clone)", string.Empty).Trim();

        if (rankText != null)
            rankText.text = rankType == EnemyRankType.Elite ? "엘리트" : "일반";
        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(displayName) ? "Enemy" : displayName;
        if (fillImage != null)
            fillImage.color = rankType == EnemyRankType.Elite ? eliteColor : normalColor;

        Refresh(health);
    }

    public void Refresh(CombatHealth health)
    {
        if (health == null)
            return;

        float maxHp = Mathf.Max(0f, health.MaxHp);
        float currentHp = Mathf.Clamp(health.CurrentHp, 0f, maxHp);
        float normalized = maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;

        if (fillImage != null)
            fillImage.fillAmount = normalized;
        UpdateFillRect(normalized);
        if (hpText != null)
            hpText.text = string.Format("{0:0} / {1:0}", currentHp, maxHp);
    }

    public void Hide()
    {
        if (root == null)
            root = gameObject;

        if (elementalStatusIcons != null)
            elementalStatusIcons.Unbind();
        SetVisible(false);
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

    private void UpdateFillRect(float normalized)
    {
        if (fillRect == null)
            return;

        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(normalized, 1f);
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = normalized > 0f ? new Vector2(-2f, -2f) : new Vector2(0f, -2f); // 막대 너비
    }
}
