using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ShopMerchantTopPanelView
{
    public TextMeshProUGUI HeaderTitleText;
    public TextMeshProUGUI MerchantNameText;
    public TextMeshProUGUI MerchantGoldText;
    public TextMeshProUGUI MerchantDescriptionText;
    public GameObject Root;
    public GameObject FallbackSearchRoot;
    public TextMeshProUGUI MerchantPortraitPlaceholderText;
    public TextMeshProUGUI MerchantPortraitCategoryText;
    public TextMeshProUGUI ReputationLevelText;
    public Image ReputationExpBarFill;
    public TextMeshProUGUI ReputationExpPercentText;
    public TextMeshProUGUI ReputationGradeText;
    public TextMeshProUGUI ReputationEffectsTitleText;
    public TextMeshProUGUI ReputationDiscountText;
    public TextMeshProUGUI ReputationStockGradeText;
    public TextMeshProUGUI MerchantGoldInfoText;
}

public sealed class ShopMerchantTopPanelPresenter
{
    public bool HasDetailedView(ShopMerchantTopPanelView view)
    {
        return view != null
            && (view.ReputationLevelText != null
                || view.ReputationExpBarFill != null
                || view.ReputationGradeText != null
                || view.ReputationDiscountText != null
                || view.ReputationStockGradeText != null
                || view.MerchantGoldInfoText != null);
    }

    public void Refresh(
        ShopMerchantTopPanelView view,
        ShopTab activeTab,
        MerchantDefinition merchant,
        MerchantTradeService tradeService)
    {
        if (view == null)
            return;

        int level = MerchantReputationService.GetLevel(merchant);
        float discountRate = tradeService != null
            ? tradeService.GetCurrentMerchantDiscountRate()
            : MerchantReputationService.GetDiscountRate(merchant);
        int discountPercent = Mathf.RoundToInt(discountRate * 100f);
        int merchantGold = tradeService != null
            ? tradeService.GetMerchantGoldAmount()
            : merchant != null ? merchant.MerchantGold : 0;

        if (view.HeaderTitleText != null)
            view.HeaderTitleText.text = ShopTabDisplayPolicy.GetHeaderTitle(activeTab);

        if (view.MerchantNameText != null)
            view.MerchantNameText.text = merchant != null ? merchant.MerchantName : "상인 없음";

        if (view.MerchantGoldText != null)
            view.MerchantGoldText.text = GoldSummaryTextFormatter.FormatMerchantGold(merchantGold);

        if (view.Root != null)
            view.Root.SetActive(true);

        float progress01 = MerchantReputationService.GetLevelProgress01(merchant);
        int progressPercent = Mathf.RoundToInt(progress01 * 100f);
        int experience = MerchantReputationService.GetExperience(merchant);
        int requiredExperience = MerchantReputationService.GetRequiredExperience();

        if (view.MerchantPortraitPlaceholderText != null)
        {
            string merchantName = merchant != null ? merchant.MerchantName : "상인";
            view.MerchantPortraitPlaceholderText.overflowMode = TextOverflowModes.Overflow;
            view.MerchantPortraitPlaceholderText.text = merchantName + "\n초상화 준비중";
        }

        if (view.MerchantPortraitCategoryText != null)
            view.MerchantPortraitCategoryText.text = MerchantDisplayPolicy.GetCategoryLabel(merchant);

        if (view.ReputationLevelText != null)
        {
            view.ReputationLevelText.overflowMode = TextOverflowModes.Overflow;
            view.ReputationLevelText.text = "우호도 Lv." + level;
        }

        UpdateReputationExpGauge(view, progress01);

        if (view.ReputationExpPercentText != null)
        {
            view.ReputationExpPercentText.overflowMode = TextOverflowModes.Overflow;
            view.ReputationExpPercentText.text = "경험치 " + experience + "/" + requiredExperience + " (" + progressPercent + "%)";
        }

        if (view.ReputationGradeText != null)
        {
            view.ReputationGradeText.overflowMode = TextOverflowModes.Overflow;
            view.ReputationGradeText.text = "등급 : " + MerchantDisplayPolicy.GetReputationLabel(level);
        }

        if (view.ReputationEffectsTitleText != null)
            view.ReputationEffectsTitleText.text = "우호도 효과";

        if (view.ReputationDiscountText != null)
        {
            view.ReputationDiscountText.overflowMode = TextOverflowModes.Overflow;
            view.ReputationDiscountText.text = "할인 " + discountPercent + "%";
        }

        if (view.ReputationStockGradeText != null)
        {
            view.ReputationStockGradeText.richText = true;
            view.ReputationStockGradeText.overflowMode = TextOverflowModes.Overflow;
            view.ReputationStockGradeText.text = "판매아이템등급 : " + MerchantDisplayPolicy.GetSaleGradeSummary(merchant);
        }

        if (view.MerchantGoldInfoText != null)
        {
            view.MerchantGoldInfoText.overflowMode = TextOverflowModes.Overflow;
            view.MerchantGoldInfoText.text = "보유 골드 : " + merchantGold + "G";
        }

        if (view.MerchantDescriptionText != null)
        {
            view.MerchantDescriptionText.overflowMode = TextOverflowModes.Truncate;
            view.MerchantDescriptionText.text = HasDetailedView(view)
                ? BuildMerchantDescriptionText(merchant)
                : MerchantDisplayPolicy.BuildFallbackInfoText(merchant, level, discountPercent + "%");
        }
    }

    private static string BuildMerchantDescriptionText(MerchantDefinition merchant)
    {
        if (merchant == null)
            return "상인 정보가 없습니다.";

        if (!string.IsNullOrWhiteSpace(merchant.Description))
            return merchant.Description;

        return MerchantDisplayPolicy.GetStockSummary(merchant);
    }

    private static void UpdateReputationExpGauge(ShopMerchantTopPanelView view, float progress01)
    {
        Image fillImage = ResolveReputationExpGaugeFillImage(view);
        if (fillImage == null)
            return;

        float clamped = Mathf.Clamp01(progress01);
        fillImage.raycastTarget = false;

        if (IsDedicatedReputationGaugeFill(fillImage))
        {
            RectTransform fillRect = fillImage.rectTransform;
            fillImage.type = Image.Type.Simple;
            fillImage.fillAmount = 1f;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(clamped, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            return;
        }

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = clamped;
    }

    private static Image ResolveReputationExpGaugeFillImage(ShopMerchantTopPanelView view)
    {
        Image fill = FindReputationExpGaugeFillInRoot(view);
        if (fill != null)
        {
            view.ReputationExpBarFill = fill;
            return fill;
        }

        return view.ReputationExpBarFill;
    }

    private static Image FindReputationExpGaugeFillInRoot(ShopMerchantTopPanelView view)
    {
        Transform root = view.Root != null
            ? view.Root.transform
            : view.FallbackSearchRoot != null ? view.FallbackSearchRoot.transform : null;
        if (root == null)
            return null;

        Transform expBar = FindChildRecursive(root, "ReputationExpBar");
        if (expBar == null)
            return null;

        Transform fill = FindChildRecursive(expBar, "Fill");
        return fill != null ? fill.GetComponent<Image>() : null;
    }

    private static bool IsDedicatedReputationGaugeFill(Image image)
    {
        if (image == null)
            return false;

        if (image.transform.parent != null && image.transform.parent.name == "ReputationExpBar")
            return true;

        return image.name == "Fill" || image.name == "ReputationExpFill" || image.name == "ReputationExpBarFill";
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }
}
