using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// TooltipManager partial: 표시·숨김과 타입별 정식 뷰 연결·검증. 필드와 Unity 수명주기는 TooltipManager.cs에 있다.
public partial class TooltipManager
{
    public void ShowTooltip(ItemData item)
    {
        ShowTooltipInternal(item, false, null, false);
    }

    public void ShowTooltip(ItemData item, MerchantDefinition merchant, bool merchantSelling)
    {
        ShowTooltipInternal(item, true, merchant, merchantSelling);
    }

    private void ShowTooltipInternal(ItemData item, bool shopPriceContextActive, MerchantDefinition merchant, bool merchantSelling)
    {
        if (suppressed)
        {
            HideTooltip();
            return;
        }

        if (item == null || !item.HasValidBaseData)
            return;

        if(rpgTooltip){
            currentItem=item;currentShopPriceContextActive=shopPriceContextActive;currentShopMerchant=merchant;currentShopMerchantSelling=merchantSelling;
            rpgTooltip.Show(item,shopPriceContextActive?GetCurrentShopPrice(item).ToString("N0")+"G":null);return;
        }

        ActivateAuthoredView(ResolveViewKind(item)); // 타입별 실제 뷰 선택
        EnsureRuntimeView();
        if (!authoredViewReady || tooltipPanel == null)
            return;

        BringTooltipToFront(); // 최상단
        ConfigureRaycastBlocking(); // 클릭 통과

        bool contextChanged = currentShopPriceContextActive != shopPriceContextActive
            || currentShopMerchant != merchant
            || currentShopMerchantSelling != merchantSelling;
        if (currentItem != item || contextChanged)
        {
            currentShopPriceContextActive = shopPriceContextActive;
            currentShopMerchant = merchant;
            currentShopMerchantSelling = merchantSelling;
            SetTooltipContent(item); // 내용 갱신
            currentItem = item;
        }

        if (!tooltipPanel.activeSelf)
            tooltipPanel.SetActive(true);

        RebuildTooltipLayout();
        UpdateTooltipPosition(ReadPointerScreenPosition()); // 첫 프레임부터 화면 안쪽
    }

    public void HideTooltip()
    {
        if(rpgTooltip)rpgTooltip.Hide();
        if (HasAuthoredViewGallery())
        {
            for (int i = 0; i < authoredViews.Length; i++)
            {
                if (authoredViews[i] != null)
                    authoredViews[i].Panel.SetActive(false);
            }
        }
        else if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(false);
        }

        currentItem = null;
        currentShopPriceContextActive = false;
        currentShopMerchant = null;
        currentShopMerchantSelling = false;
    }

    public void HideNow()
    {
        HideTooltip();
    }

    public void SetSuppressed(bool value)
    {
        suppressed = value;
        if (suppressed)
            HideTooltip();
    }

    private void BindInitialAuthoredView()
    {
        if (!HasAuthoredViewGallery())
            return;

        TooltipAuthoredView weaponView = FindAuthoredView(TooltipAuthoredViewKind.Weapon);
        if (weaponView != null)
            BindAuthoredView(weaponView);
    }

    private void ActivateAuthoredView(TooltipAuthoredViewKind kind)
    {
        if (!HasAuthoredViewGallery())
            return; // 구형 단일 뷰는 Objectizer 실행 전까지만 유지

        TooltipAuthoredView nextView = FindAuthoredView(kind);
        if (nextView == null)
            return;

        for (int i = 0; i < authoredViews.Length; i++)
        {
            TooltipAuthoredView view = authoredViews[i];
            if (view != null && view != nextView)
                view.Panel.SetActive(false);
        }

        BindAuthoredView(nextView);
    }

    private void BindAuthoredView(TooltipAuthoredView view)
    {
        activeAuthoredView = view;
        tooltipPanel = view.Panel;
        tooltipRect = view.RectTransform;
        nameText = view.NameText;
        weaponHeaderRoot = view.WeaponHeaderRoot;
        weaponNameText = view.WeaponNameText;
        weaponSubtitleText = view.WeaponSubtitleText;
        weaponGradeTagImage = view.WeaponGradeTagImage;
        weaponGradeTagText = view.WeaponGradeTagText;
        basicStatsText = view.BasicStatsText;
        meleeStatListView = view.MeleeStatListView;
        weaponStatsText = view.WeaponStatsText;
        priceText = view.PriceText;
        dividerBasic = view.DividerBasic;
        dividerWeapon = view.DividerWeapon;
        dividerPrice = view.DividerPrice;
        authoredViewReady = view.HasRequiredReferences;
    }

    private bool HasAuthoredViewGallery()
    {
        TooltipAuthoredViewKind[] expectedKinds =
            (TooltipAuthoredViewKind[])System.Enum.GetValues(typeof(TooltipAuthoredViewKind));
        int expectedCount = expectedKinds.Length;
        if (authoredViews == null || authoredViews.Length != expectedCount)
            return false;

        HashSet<TooltipAuthoredViewKind> found = new HashSet<TooltipAuthoredViewKind>();
        for (int i = 0; i < authoredViews.Length; i++)
        {
            TooltipAuthoredView view = authoredViews[i];
            if (view == null
                || System.Array.IndexOf(expectedKinds, view.Kind) < 0
                || !found.Add(view.Kind)
                || !view.HasRequiredReferences)
                return false;
        }

        return found.Count == expectedCount;
    }

    private TooltipAuthoredView FindAuthoredView(TooltipAuthoredViewKind kind)
    {
        if (authoredViews == null)
            return null;

        for (int i = 0; i < authoredViews.Length; i++)
        {
            if (authoredViews[i] != null && authoredViews[i].Kind == kind)
                return authoredViews[i];
        }

        return null;
    }

    private static TooltipAuthoredViewKind ResolveViewKind(ItemData item)
    {
        if (item?.baseData is WeaponItemData)
            return TooltipAuthoredViewKind.Weapon;
        if (item?.baseData is BagItemData)
            return TooltipAuthoredViewKind.Bag;
        if (item?.baseData is ConsumableItemData)
            return TooltipAuthoredViewKind.Consumable;
        if (item?.baseData is CurrencyItemData)
            return TooltipAuthoredViewKind.Currency;
        return TooltipAuthoredViewKind.Default;
    }

    private void EnsureRuntimeView()
    {
        if (tooltipPanel == null)
            return;

        tooltipRect = tooltipPanel.GetComponent<RectTransform>(); // 패널 Rect
        authoredViewReady = ValidateAuthoredView(); // 정식 참조 확인
        if (!authoredViewReady)
        {
            LogMissingAuthoredView();
            return;
        }

        if (tooltipText != null)
            tooltipText.gameObject.SetActive(false); // 기존 폰트 원본
        if (weaponGradeStarSprites == null)
            weaponGradeStarSprites = Resources.Load<WeaponGradeStarSpriteSet>("UI/Tooltip/WeaponGradeStarSpriteSet");
        if (meleeStatListView != null)
            meleeStatListView.Initialize(weaponGradeStarSprites); // Sprite 데이터만 연결
        ConfigureRaycastBlocking(); // 입력 통과
    }

    private void BringTooltipToFront()
    {
        if (tooltipPanel == null)
            return;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(true);

        if (canvas != null && tooltipPanel.transform.parent != canvas.transform)
            tooltipPanel.transform.SetParent(canvas.transform, true);

        tooltipPanel.transform.SetAsLastSibling(); // UI 최상단
    }

    private bool ValidateAuthoredView()
    {
        if (activeAuthoredView != null)
            return activeAuthoredView.Panel == tooltipPanel && activeAuthoredView.HasRequiredReferences;

        if (tooltipPanel == null
            || tooltipPanel.GetComponent<RectTransform>() == null
            || tooltipPanel.GetComponent<Image>() == null
            || tooltipPanel.GetComponent<VerticalLayoutGroup>() == null
            || tooltipPanel.GetComponent<ContentSizeFitter>() == null
            || tooltipPanel.GetComponent<CanvasGroup>() == null
            || nameText == null
            || weaponHeaderRoot == null
            || weaponNameText == null
            || weaponSubtitleText == null
            || weaponGradeTagImage == null
            || weaponGradeTagText == null
            || basicStatsText == null
            || meleeStatListView == null
            || !meleeStatListView.HasAuthoredView
            || weaponStatsText == null
            || priceText == null
            || dividerBasic == null
            || dividerWeapon == null
            || dividerPrice == null)
        {
            return false;
        }

        return true;
    }

    private void LogMissingAuthoredView()
    {
        if (missingAuthoredViewLogged)
            return;

        missingAuthoredViewLogged = true;
        Debug.LogError(
            "[TooltipManager] 정식 툴팁 오브젝트 참조가 없습니다. Tooltip View Objectizer를 실행하세요.",
            this);
    }
}
