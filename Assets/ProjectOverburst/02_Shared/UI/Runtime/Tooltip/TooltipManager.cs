using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum TooltipAuthoredViewKind
{
    Weapon,
    ComboGem,
    Bag,
    Consumable,
    Currency,
    Default
}

[System.Serializable]
public class TooltipAuthoredView // 타입별 정식 툴팁 뷰 참조
{
    [SerializeField] private TooltipAuthoredViewKind kind;
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private RectTransform weaponHeaderRoot;
    [SerializeField] private TextMeshProUGUI weaponNameText;
    [SerializeField] private TextMeshProUGUI weaponSubtitleText;
    [SerializeField] private Image weaponGradeTagImage;
    [SerializeField] private TextMeshProUGUI weaponGradeTagText;
    [SerializeField] private TextMeshProUGUI basicStatsText;
    [SerializeField] private WeaponMeleeStatListView meleeStatListView;
    [SerializeField] private TextMeshProUGUI weaponStatsText;
    [SerializeField] private TextMeshProUGUI gemStatsText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private GameObject dividerBasic;
    [SerializeField] private GameObject dividerWeapon;
    [SerializeField] private GameObject dividerGem;
    [SerializeField] private GameObject dividerPrice;
    [SerializeField] private RectTransform gemSlotRoot;
    [SerializeField] private Image[] gemSlotImages;
    [SerializeField] private TemporaryTooltipGemSlotOutlineEffect[] gemSlotOutlineEffects;

    public TooltipAuthoredViewKind Kind => kind;
    public GameObject Panel => panel;
    public RectTransform RectTransform => panel != null ? panel.transform as RectTransform : null;
    public TextMeshProUGUI NameText => nameText;
    public RectTransform WeaponHeaderRoot => weaponHeaderRoot;
    public TextMeshProUGUI WeaponNameText => weaponNameText;
    public TextMeshProUGUI WeaponSubtitleText => weaponSubtitleText;
    public Image WeaponGradeTagImage => weaponGradeTagImage;
    public TextMeshProUGUI WeaponGradeTagText => weaponGradeTagText;
    public TextMeshProUGUI BasicStatsText => basicStatsText;
    public WeaponMeleeStatListView MeleeStatListView => meleeStatListView;
    public TextMeshProUGUI WeaponStatsText => weaponStatsText;
    public TextMeshProUGUI GemStatsText => gemStatsText;
    public TextMeshProUGUI PriceText => priceText;
    public GameObject DividerBasic => dividerBasic;
    public GameObject DividerWeapon => dividerWeapon;
    public GameObject DividerGem => dividerGem;
    public GameObject DividerPrice => dividerPrice;
    public RectTransform GemSlotRoot => gemSlotRoot;
    public Image[] GemSlotImages => gemSlotImages;
    public TemporaryTooltipGemSlotOutlineEffect[] GemSlotOutlineEffects => gemSlotOutlineEffects;

    public void Configure(
        TooltipAuthoredViewKind viewKind,
        GameObject panelObject,
        TextMeshProUGUI authoredNameText,
        RectTransform authoredWeaponHeaderRoot,
        TextMeshProUGUI authoredWeaponNameText,
        TextMeshProUGUI authoredWeaponSubtitleText,
        Image authoredWeaponGradeTagImage,
        TextMeshProUGUI authoredWeaponGradeTagText,
        TextMeshProUGUI authoredBasicStatsText,
        WeaponMeleeStatListView authoredMeleeStatListView,
        TextMeshProUGUI authoredWeaponStatsText,
        TextMeshProUGUI authoredGemStatsText,
        TextMeshProUGUI authoredPriceText,
        GameObject authoredDividerBasic,
        GameObject authoredDividerWeapon,
        GameObject authoredDividerGem,
        GameObject authoredDividerPrice,
        RectTransform authoredGemSlotRoot,
        Image[] authoredGemSlotImages,
        TemporaryTooltipGemSlotOutlineEffect[] authoredGemSlotOutlineEffects)
    {
        kind = viewKind;
        panel = panelObject;
        nameText = authoredNameText;
        weaponHeaderRoot = authoredWeaponHeaderRoot;
        weaponNameText = authoredWeaponNameText;
        weaponSubtitleText = authoredWeaponSubtitleText;
        weaponGradeTagImage = authoredWeaponGradeTagImage;
        weaponGradeTagText = authoredWeaponGradeTagText;
        basicStatsText = authoredBasicStatsText;
        meleeStatListView = authoredMeleeStatListView;
        weaponStatsText = authoredWeaponStatsText;
        gemStatsText = authoredGemStatsText;
        priceText = authoredPriceText;
        dividerBasic = authoredDividerBasic;
        dividerWeapon = authoredDividerWeapon;
        dividerGem = authoredDividerGem;
        dividerPrice = authoredDividerPrice;
        gemSlotRoot = authoredGemSlotRoot;
        gemSlotImages = authoredGemSlotImages;
        gemSlotOutlineEffects = authoredGemSlotOutlineEffects;
    }

    public bool HasRequiredReferences
    {
        get
        {
            if (RectTransform == null
                || panel.GetComponent<Image>() == null
                || panel.GetComponent<VerticalLayoutGroup>() == null
                || panel.GetComponent<ContentSizeFitter>() == null
                || panel.GetComponent<CanvasGroup>() == null)
            {
                return false;
            }

            if (!HasHeaderReferences())
                return false;

            switch (kind)
            {
                case TooltipAuthoredViewKind.Weapon:
                    return basicStatsText != null
                        && meleeStatListView != null
                        && meleeStatListView.HasAuthoredView
                        && weaponStatsText != null
                        && gemStatsText != null
                        && priceText != null
                        && dividerBasic != null
                        && dividerWeapon != null
                        && dividerGem != null
                        && dividerPrice != null
                        && gemSlotRoot != null
                        && HasFour(gemSlotImages)
                        && HasFour(gemSlotOutlineEffects);
                case TooltipAuthoredViewKind.ComboGem:
                    return gemStatsText != null && priceText != null
                        && dividerGem != null && dividerPrice != null;
                case TooltipAuthoredViewKind.Bag:
                    return basicStatsText != null && priceText != null
                        && dividerBasic != null && dividerPrice != null;
                case TooltipAuthoredViewKind.Consumable:
                    return basicStatsText != null && weaponStatsText != null && priceText != null
                        && dividerBasic != null && dividerWeapon != null && dividerPrice != null;
                case TooltipAuthoredViewKind.Currency:
                    return basicStatsText != null && dividerBasic != null;
                default:
                    return basicStatsText != null && priceText != null
                        && dividerBasic != null && dividerPrice != null;
            }
        }
    }

    private bool HasHeaderReferences()
    {
        return nameText != null
            && weaponHeaderRoot != null
            && weaponNameText != null
            && weaponSubtitleText != null
            && weaponGradeTagImage != null
            && weaponGradeTagText != null;
    }

    private static bool HasFour<T>(T[] values) where T : Object
    {
        if (values == null || values.Length != 4)
            return false;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
                return false;
        }

        return true;
    }
}

public class TooltipManager : MonoBehaviour // 툴팁 표시
{
    private const float VtpPanelWidth = 204.0325f; // 기본 폭
    private const float VtpBodyFontSize = 18f; // 본문 크기
    private const float VtpStatFontSize = 16f; // 스탯 크기
    private const float ResponsiveMaxPanelWidth = 680f; // 별 한 줄 최대 폭
    private const float ScreenEdgePadding = 8f; // 화면 가장자리 여백
    private const string ChangedValueColor = "#FFD75A"; // 변경값
    private const string DisabledOptionColor = "#8A8A8A"; // 비활성 옵션
    private const string SubtitleSizeOpen = "<size=85%>";
    private const string SubtitleSizeClose = "</size>";
    public static TooltipManager Instance { get; private set; }
    [SerializeField] private OverburstGameTooltip rpgTooltip;

#if UNITY_EDITOR
    public TooltipAuthoredView[] EditorAuthoredViews => authoredViews;

    public void EditorAssignAuthoredViews(TooltipAuthoredView[] views)
    {
        authoredViews = views;
    }
#endif

    [SerializeField] private GameObject tooltipPanel; // root 패널
    [SerializeField] private TextMeshProUGUI tooltipText; // 기존 텍스트
    [SerializeField] private Vector2 tooltipPointerOffset = new Vector2(20f, -16f); // 마우스 근접 오프셋
    [SerializeField] private WeaponGradeStarSpriteSet weaponGradeStarSprites;

    [Header("타입별 정식 툴팁 전시관")]
    [SerializeField] private TooltipAuthoredView[] authoredViews;

    [Header("정식 툴팁 뷰")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private RectTransform weaponHeaderRoot;
    [SerializeField] private TextMeshProUGUI weaponNameText;
    [SerializeField] private TextMeshProUGUI weaponSubtitleText;
    [SerializeField] private Image weaponGradeTagImage;
    [SerializeField] private TextMeshProUGUI weaponGradeTagText;
    [SerializeField] private TextMeshProUGUI basicStatsText;
    [SerializeField] private WeaponMeleeStatListView meleeStatListView;
    [SerializeField] private TextMeshProUGUI weaponStatsText;
    [SerializeField] private TextMeshProUGUI gemStatsText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private GameObject dividerBasic;
    [SerializeField] private GameObject dividerWeapon;
    [SerializeField] private GameObject dividerGem;
    [SerializeField] private GameObject dividerPrice;
    [SerializeField] private RectTransform gemSlotRoot;
    [SerializeField] private Image[] gemSlotImages;
    [SerializeField] private TemporaryTooltipGemSlotOutlineEffect[] gemSlotOutlineEffects;

    private RectTransform tooltipRect;
    private Canvas canvas;
    private CanvasGroup tooltipCanvasGroup;
    private float nextFlaskRefresh;
    private ItemData currentItem; // 현재 표시 아이템
    private bool currentShopPriceContextActive;
    private bool currentShopMerchantSelling;
    private MerchantDefinition currentShopMerchant;
    private MerchantTradeValueCalculator priceValueCalculator;
    private TMP_FontAsset regularConsumableStatsFont;
    private TMP_FontAsset flaskStatsFont;

    private TooltipAuthoredView activeAuthoredView;
    private bool suppressed;
    private bool authoredViewReady;
    private bool missingAuthoredViewLogged;
    private readonly Vector3[] tooltipWorldCorners = new Vector3[4];

    private void Awake()
    {
        RegisterInstanceCandidate(this); // Instance 후보
        canvas = GetComponentInParent<Canvas>(); // 부모 Canvas
        BindInitialAuthoredView(); // 전시관 기본 뷰
        EnsureRuntimeView(); // 구조 보장
        HideTooltip();
    }

    private void OnEnable()
    {
        RegisterInstanceCandidate(this);
    }

    private void OnDisable()
    {
        if (Instance == this)
            RefreshActiveInstance();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            RefreshActiveInstance();
        }
    }

    public static void RefreshActiveInstance()
    {
        TooltipManager[] managers = FindObjectsByType<TooltipManager>(FindObjectsSortMode.None);
        TooltipManager best = null;

        for (int i = 0; i < managers.Length; i++)
        {
            TooltipManager manager = managers[i];
            if (!IsUsableInstance(manager))
                continue;

            if (best == null || IsPreferredInstance(manager, best))
                best = manager;
        }

        Instance = best;
        if (Instance != null)
            Instance.EnsureRuntimeView(); // 표시 구조
    }

    private static void RegisterInstanceCandidate(TooltipManager candidate)
    {
        if (!IsUsableInstance(candidate))
            return;

        if (!IsUsableInstance(Instance) || IsPreferredInstance(candidate, Instance))
            Instance = candidate;
    }

    private static bool IsUsableInstance(TooltipManager manager)
    {
        return manager != null
            && manager.isActiveAndEnabled
            && manager.gameObject.activeInHierarchy;
    }

    private static bool IsPreferredInstance(TooltipManager candidate, TooltipManager current)
    {
        if (candidate == null)
            return false;

        if (current == null)
            return true;

        bool candidatePreserved = IsPersistentTooltipScene(candidate.gameObject.scene.name); // Persistent 우선
        bool currentPreserved = IsPersistentTooltipScene(current.gameObject.scene.name); // 현재 기준
        if (candidatePreserved != currentPreserved)
            return candidatePreserved;

        return false;
    }

    private static bool IsPersistentTooltipScene(string sceneName)
    {
        return sceneName == PersistentSceneFlow.PersistentSceneName;
    }

    private void Update()
    {
        if (rpgTooltip)
        {
            if (currentItem?.baseData is FlaskItemData && rpgTooltip.view.gameObject.activeSelf && Time.unscaledTime >= nextFlaskRefresh)
            {
                nextFlaskRefresh = Time.unscaledTime + .2f;
                rpgTooltip.view.Present(currentItem, currentShopPriceContextActive ? GetCurrentShopPrice(currentItem).ToString("N0") + "G" : null);
            }
            rpgTooltip.Place();
            return;
        }
        if (tooltipPanel == null || !tooltipPanel.activeSelf || tooltipRect == null)
            return;

        if (currentItem?.baseData is FlaskItemData && Time.unscaledTime >= nextFlaskRefresh)
        {
            nextFlaskRefresh = Time.unscaledTime + .2f;
            SetConsumableTooltipContent(currentItem, (ConsumableItemData)currentItem.baseData);
        }
        UpdateTooltipPosition(ReadPointerScreenPosition()); // 실제 크기 기준 화면 안쪽
    }

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

        if (rpgTooltip)
        {
            currentItem = item;
            currentShopPriceContextActive = shopPriceContextActive;
            currentShopMerchant = merchant;
            currentShopMerchantSelling = merchantSelling;
            rpgTooltip.Show(item, shopPriceContextActive ? GetCurrentShopPrice(item).ToString("N0") + "G" : null);
            return;
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
        if (rpgTooltip) rpgTooltip.Hide();
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
        gemStatsText = view.GemStatsText;
        priceText = view.PriceText;
        dividerBasic = view.DividerBasic;
        dividerWeapon = view.DividerWeapon;
        dividerGem = view.DividerGem;
        dividerPrice = view.DividerPrice;
        gemSlotRoot = view.GemSlotRoot;
        gemSlotImages = view.GemSlotImages;
        gemSlotOutlineEffects = view.GemSlotOutlineEffects;
        authoredViewReady = view.HasRequiredReferences;
    }

    private bool HasAuthoredViewGallery()
    {
        int expectedCount = System.Enum.GetValues(typeof(TooltipAuthoredViewKind)).Length;
        if (authoredViews == null || authoredViews.Length != expectedCount)
            return false;

        bool[] found = new bool[expectedCount];
        for (int i = 0; i < authoredViews.Length; i++)
        {
            TooltipAuthoredView view = authoredViews[i];
            int kindIndex = view != null ? (int)view.Kind : -1;
            if (kindIndex < 0 || kindIndex >= found.Length || found[kindIndex] || !view.HasRequiredReferences)
                return false;
            found[kindIndex] = true;
        }

        return true;
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
        if (item?.baseData is ComboGemItemData)
            return TooltipAuthoredViewKind.ComboGem;
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
            || gemStatsText == null
            || priceText == null
            || dividerBasic == null
            || dividerWeapon == null
            || dividerGem == null
            || dividerPrice == null
            || gemSlotRoot == null
            || !HasAuthoredArray(gemSlotImages, 4)
            || !HasAuthoredArray(gemSlotOutlineEffects, 4))
        {
            return false;
        }

        return true;
    }

    private static bool HasAuthoredArray<T>(T[] values, int expectedCount) where T : Object
    {
        if (values == null || values.Length != expectedCount)
            return false;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
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

    private void SetTooltipContent(ItemData item)
    {
        SetActive(nameText, false);
        SetActive(weaponHeaderRoot, true);
        SetActive(meleeStatListView, false);

        if (item.baseData is WeaponItemData weaponData)
        {
            SetWeaponTooltipContent(item, weaponData); // 무기 툴팁
            return;
        }

        if (item.baseData is ComboGemItemData comboGemData)
        {
            SetComboGemTooltipContent(item, comboGemData); // 콤보 보석 툴팁
            return;
        }

        if (item.baseData is BagItemData bagData)
        {
            SetBagTooltipContent(item, bagData); // 가방 툴팁
            return;
        }

        if (item.baseData is ConsumableItemData consumableData)
        {
            SetConsumableTooltipContent(item, consumableData); // 소비 툴팁
            return;
        }

        if (item.baseData is CurrencyItemData currencyData)
        {
            SetCurrencyTooltipContent(item, currencyData); // 재화 툴팁
            return;
        }

        SetDefaultTooltipContent(item); // 일반 툴팁
    }

    private void SetWeaponTooltipContent(ItemData item, WeaponItemData weaponData)
    {
        SetActive(dividerBasic, true);
        bool isMeleeSlash = weaponData.combatDefinition.usage.attackType == WeaponAttackType.MeleeSlash; // 근접 분기
        bool isMagicWeapon = weaponData.CombatFamily == WeaponCombatFamily.Magic || weaponData.combatDefinition.usage.attackType == WeaponAttackType.Chain; // 마법 분기
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);
        SetActive(dividerGem, false);
        SetActive(gemStatsText, false);

        SetActive(gemSlotRoot, false);
        SetActive(dividerPrice, true);
        SetActive(priceText, true);

        string categoryName = isMeleeSlash
            ? "근접무기"
            : isMagicWeapon
                ? "마법무기"
                : ItemTooltipFormatter.GetWeaponFamilyName(weaponData.CombatFamily);
        SetWeaponHeader(item, weaponData, categoryName);

        WeaponFinalStats baseStats = WeaponStatCalculator.CalculateWeaponBase(item); // 기본 스탯
        WeaponFinalStats finalStats = WeaponStatCalculator.Calculate(item); // 최종 스탯

        SetActive(basicStatsText, !isMeleeSlash);
        SetActive(meleeStatListView, isMeleeSlash);
        if (isMeleeSlash)
        {
            basicStatsText.text = string.Empty;
            meleeStatListView.Initialize(weaponGradeStarSprites);
            meleeStatListView.SetContent(item, baseStats, finalStats);
        }
        else
        {
            if (flaskStatsFont == null)
                flaskStatsFont = Resources.Load<TMP_FontAsset>("UI/Tooltip/FlaskTooltipFont");
            if (flaskStatsFont != null)
                basicStatsText.font = flaskStatsFont;
            basicStatsText.text = isMagicWeapon
                ? BuildMagicWeaponGradeStatComparisonFixed(item, baseStats, finalStats)
                : BuildWeaponGradeStatComparisonFixed(item, baseStats, finalStats);
        }
        weaponStatsText.text = string.Empty;
        gemStatsText.text = string.Empty;
        priceText.text = BuildPriceText(item);
    }

    private void SetWeaponHeader(ItemData item, WeaponItemData weaponData, string categoryName)
    {
        string displayName = item != null ? item.itemName : string.Empty;
        if (string.IsNullOrWhiteSpace(displayName) && weaponData != null)
            displayName = !string.IsNullOrWhiteSpace(weaponData.itemName) ? weaponData.itemName : weaponData.name;

        string weaponClassName = weaponData != null
            ? ItemTooltipFormatter.GetWeaponClassName(weaponData.weaponClass)
            : string.Empty;
        string elementName = item != null ? GetWeaponElementName(item.ResolvedElement) : "무속성";
        SetItemHeader(item, displayName, categoryName + " / " + weaponClassName + " · " + elementName);
    }

    private void SetItemHeader(ItemData item, string displayName, string subtitle)
    {
        if (string.IsNullOrWhiteSpace(displayName) && item?.baseData != null)
            displayName = !string.IsNullOrWhiteSpace(item.baseData.itemName) ? item.baseData.itemName : item.baseData.name;

        Color gradeColor = item != null ? GradeConfig.GetGradeColor(item.grade) : Color.gray;
        weaponNameText.text = displayName ?? string.Empty;
        weaponNameText.color = gradeColor; // 이름 등급색
        weaponSubtitleText.text = subtitle ?? string.Empty;
        weaponGradeTagText.text = item != null ? ItemTooltipFormatter.GetGradeName(item.grade) : string.Empty;
        weaponGradeTagImage.color = gradeColor;
        weaponGradeTagText.color = GetContrastingTextColor(gradeColor);
    }

    private static Color GetContrastingTextColor(Color background)
    {
        float luminance = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
        return luminance >= 0.62f ? new Color32(24, 20, 16, 255) : Color.white;
    }

    private static string GetWeaponElementName(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return "불";
            case WeaponElement.Ice: return "얼음";
            case WeaponElement.Electric: return "번개";
            case WeaponElement.Water: return "물";
            default: return "무속성";
        }
    }

    private void SetComboGemTooltipContent(ItemData item, ComboGemItemData comboGemData)
    {
        item.EnsureRuntimeState();
        SetActive(dividerBasic, false);
        SetActive(basicStatsText, false);
        SetActive(gemSlotRoot, false);
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);
        SetActive(dividerGem, true);
        SetActive(gemStatsText, true);
        SetActive(dividerPrice, true);
        SetActive(priceText, true);

        SetItemHeader(
            item,
            item.itemName,
            "콤보 보석 / " + ItemTooltipFormatter.GetComboGemTypeName(comboGemData.GemType));

        gemStatsText.text = BuildComboGemTooltipText(item, comboGemData);
        priceText.text = BuildPriceText(item);
    }

    private static string BuildComboGemTooltipText(ItemData item, ComboGemItemData gemData)
    {
        if (gemData == null || gemData.GemType == ComboGemType.Unspecified)
            return string.Empty;

        StringBuilder builder = new StringBuilder();
        builder.Append("역할: ").Append(ItemTooltipFormatter.GetComboGemTypeName(gemData.GemType));
        if (gemData is ElementComboGemItemData elementGemData)
        {
            builder.AppendLine();
            builder.Append("원소: ").Append(ItemTooltipFormatter.GetWeaponElementName(elementGemData.element));
        }

        if (item.comboGemOptions != null)
        {
            for (int i = 0; i < item.comboGemOptions.Count; i++)
            {
                string optionText = ItemTooltipFormatter.FormatComboGemOptionWithRollRange(
                    item.comboGemOptions[i],
                    gemData,
                    item.grade,
                    "#8A8A8A");
                if (string.IsNullOrEmpty(optionText))
                    continue;

                builder.AppendLine();
                builder.Append(optionText);
            }
        }

        return builder.ToString();
    }

    private void SetBagTooltipContent(ItemData item, BagItemData bagData)
    {
        item.EnsureRuntimeState(); // 가방 옵션 보정
        SetActive(dividerBasic, true);
        SetActive(basicStatsText, true);
        SetActive(gemSlotRoot, false);
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);
        SetActive(dividerGem, false);
        SetActive(gemStatsText, false);
        SetActive(dividerPrice, true);
        SetActive(priceText, true);
        SetActive(dividerPrice, true);
        SetActive(priceText, true);

        SetItemHeader(item, item.itemName, "가방 / 수납");

        basicStatsText.text = BuildBagStatsText(item, bagData);
        priceText.text = BuildPriceText(item);
    }

    private string BuildBagStatsText(ItemData item, BagItemData bagData)
    {
        StringBuilder builder = new StringBuilder(); // 가방 표시 줄
        builder.Append("인벤토리 슬롯 +").Append(Mathf.Max(0, bagData.additionalSlots));

        if (item.bagOptions == null || item.bagOptions.Count == 0)
            return builder.ToString();

        builder.AppendLine();
        builder.Append("추가 옵션");
        for (int i = 0; i < item.bagOptions.Count; i++)
        {
            BagRandomOptionRoll option = item.bagOptions[i];
            if (option == null)
                continue;

            builder.AppendLine();
            builder.Append("- ");
            builder.Append(ItemTooltipFormatter.FormatBagOptionWithRollRange(option, item.grade, DisabledOptionColor));
        }

        return builder.ToString();
    }

    private void SetDefaultTooltipContent(ItemData item)
    {
        SetActive(dividerBasic, true);
        SetActive(basicStatsText, true);
        SetActive(gemSlotRoot, false);
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);
        SetActive(dividerGem, false);
        SetActive(gemStatsText, false);

        SetItemHeader(item, item.itemName, "일반 아이템");

        basicStatsText.text = string.IsNullOrEmpty(item.baseData.description) ? string.Empty : item.baseData.description;
        priceText.text = BuildPriceText(item);
    }

    private void SetCurrencyTooltipContent(ItemData item, CurrencyItemData currencyData)
    {
        SetActive(dividerBasic, true);
        SetActive(basicStatsText, true);
        SetActive(gemSlotRoot, false);
        SetActive(dividerWeapon, false);
        SetActive(weaponStatsText, false);
        SetActive(dividerGem, false);
        SetActive(gemStatsText, false);
        SetActive(dividerPrice, false);
        SetActive(priceText, false);

        SetItemHeader(
            item,
            item.itemName + " x" + Mathf.Max(1, item.stackCount),
            "재화 / " + GetCurrencyDisplayName(currencyData.currencyType));

        basicStatsText.text = string.IsNullOrEmpty(currencyData.description) ? "창고 기준으로 자동 정산되는 아이템형 재화" : currencyData.description;
    }

    private void SetConsumableTooltipContent(ItemData item, ConsumableItemData consumableData)
    {
        SetActive(dividerBasic, true);
        SetActive(basicStatsText, true);
        SetActive(gemSlotRoot, false);
        SetActive(dividerWeapon, true);
        SetActive(weaponStatsText, true);
        SetActive(dividerGem, false);
        SetActive(gemStatsText, false);
        SetActive(dividerPrice, true);
        SetActive(priceText, true);

        SetItemHeader(item, item.itemName, "소비아이템 / " + GetConsumableSubtypeName(consumableData));

        if (regularConsumableStatsFont == null)
            regularConsumableStatsFont = weaponStatsText.font;

        if (consumableData is FlaskItemData flaskData)
        {
            if (flaskStatsFont == null)
                flaskStatsFont = Resources.Load<TMP_FontAsset>("UI/Tooltip/FlaskTooltipFont");
            if (flaskStatsFont != null)
                weaponStatsText.font = flaskStatsFont;
            SetItemHeader(item, item.itemName, FlaskTooltip.Subtitle(flaskData));
            basicStatsText.text = FlaskTooltip.Status(item);
            weaponStatsText.text = FlaskTooltip.Details(item);
            SetActive(dividerPrice, currentShopPriceContextActive);
            SetActive(priceText, currentShopPriceContextActive);
        }
        else
        {
            if (regularConsumableStatsFont != null)
                weaponStatsText.font = regularConsumableStatsFont;
            basicStatsText.text = BuildConsumableEffectText(consumableData);
            weaponStatsText.text = BuildConsumableTimingText(consumableData);
        }
        priceText.text = BuildPriceText(item);
    }

    private string BuildConsumableEffectText(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return string.Empty;

        switch (consumableData.consumableType)
        {
            case ConsumableType.SpeedBoost:
                return "이동속도 +" + FormatConsumablePercent(GetMoveSpeedBonusPercent(consumableData)) + "%";
            case ConsumableType.HealHp:
                return "체력 +" + FormatHealValue(consumableData.effectValue);
            default:
                return string.IsNullOrEmpty(consumableData.description) ? "사용 효과" : consumableData.description;
        }
    }

    private string BuildConsumableTimingText(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return string.Empty;

        StringBuilder builder = new StringBuilder();
        if (consumableData.duration > 0f)
            builder.Append("지속시간 ").Append(ItemTooltipFormatter.FormatSeconds(consumableData.duration)).AppendLine();
        else if (consumableData.consumableType == ConsumableType.HealHp)
            builder.AppendLine("즉시 회복");

        if (consumableData.cooldown > 0f)
            builder.Append("쿨타임 ").Append(ItemTooltipFormatter.FormatSeconds(consumableData.cooldown));

        if (consumableData.IsPermanentSingleItem)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.AppendLine("소모 없음");
            builder.Append("단일 아이템");
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildPriceText(ItemData item)
    {
        int baseValue = GetBaseTooltipValue(item);
        if (currentShopPriceContextActive && currentShopMerchant != null)
        {
            int saleValue = GetCurrentShopPrice(item);
            return "가치 : " + baseValue + "G\n판매가 : " + saleValue + "G";
        }

        return "가치 : " + baseValue + "G";
    }

    private int GetBaseTooltipValue(ItemData item)
    {
        ResolvePriceValueCalculator();
        if (priceValueCalculator != null)
            return priceValueCalculator.GetBaseValue(item);

        return GetFallbackStackValue(item);
    }

    private int GetCurrentShopPrice(ItemData item)
    {
        ResolvePriceValueCalculator();
        if (priceValueCalculator != null)
        {
            return currentShopMerchantSelling
                ? priceValueCalculator.GetBuyValue(item, currentShopMerchant)
                : priceValueCalculator.GetSellValue(item, currentShopMerchant);
        }

        return GetFallbackStackValue(item);
    }

    private void ResolvePriceValueCalculator()
    {
        if (priceValueCalculator == null)
            priceValueCalculator = FindFirstObjectByType<MerchantTradeValueCalculator>(FindObjectsInactive.Include);
    }

    private int GetFallbackStackValue(ItemData item)
    {
        if (item == null || item.baseData == null)
            return 0;

        int unitValue = item.baseData is CurrencyItemData currencyData && currencyData.currencyType == CurrencyType.Gold
            ? 1
            : Mathf.Max(0, item.baseData.sellPrice);
        return unitValue * Mathf.Max(1, item.stackCount);
    }

    private string GetConsumableSubtypeName(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return "소비";

        switch (consumableData.consumableType)
        {
            case ConsumableType.SpeedBoost: return "물약";
            case ConsumableType.HealHp: return "물약";
            case ConsumableType.Invincible: return "방어";
            case ConsumableType.TimeStop: return "시간";
            default: return "소비";
        }
    }

    private string GetCurrencyDisplayName(CurrencyType type)
    {
        switch (type)
        {
            case CurrencyType.Gold: return "골드";
            case CurrencyType.GemPowder: return "보석가루";
            case CurrencyType.MapFragment: return "지도조각";
            default: return "재화";
        }
    }

    private float GetMoveSpeedBonusPercent(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return 0f;

        float multiplier = consumableData.moveSpeedMultiplier > 0f ? consumableData.moveSpeedMultiplier : consumableData.effectValue;
        if (multiplier <= 0f)
            return 0f;

        return Mathf.Max(0f, (multiplier - 1f) * 100f);
    }

    private string FormatHealValue(float value)
    {
        if (value > 0f && value <= 1f)
            return FormatConsumablePercent(value * 100f) + "%";

        return ItemTooltipFormatter.FormatNumber(value);
    }

    private string FormatConsumablePercent(float value)
    {
        return value.ToString("0.##");
    }

    private const char GradeStarMarker = '◆'; // 장비 공통 품질 각인

    private string BuildWeaponGradeStatComparisonFixed(ItemData item, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        StringBuilder builder = new StringBuilder();

        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Damage, "데미지", baseStats.damage, finalStats.damage, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Rpm, "RPM", ItemTooltipFormatter.ToRpm(baseStats.attackInterval), ItemTooltipFormatter.ToRpm(finalStats.attackInterval), FormatZeroDecimal);
        AppendWeaponIntFixed(builder, item, WeaponGradeStatType.MagazineSize, "장탄수", baseStats.magazineSize, finalStats.magazineSize);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.ReloadDuration, "재장전 시간", baseStats.reloadDuration, finalStats.reloadDuration, FormatSecondsTwoDecimals);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Range, "사거리", baseStats.range, finalStats.range, FormatMetersOneDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Recoil, "반동", baseStats.recoil, finalStats.recoil, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.RecoilRecovery, "반동 회복", baseStats.recoilRecoverySpeed, finalStats.recoilRecoverySpeed, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritChance, "치명타 확률", baseStats.critChance, finalStats.critChance, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritDamage, "치명타 피해", baseStats.critDamageMultiplier, finalStats.critDamageMultiplier, FormatMultiplierAsPercent);

        return builder.ToString().TrimEnd();
    }

    private string BuildMeleeWeaponGradeStatComparisonFixed(ItemData item, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        StringBuilder builder = new StringBuilder();

        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Damage, "데미지", baseStats.damage, finalStats.damage, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.AttackSpeed, "공격속도", baseStats.meleeAttackSpeedMultiplier * 100f, finalStats.meleeAttackSpeedMultiplier * 100f, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, ResolveMeleeRangeGradeStatType(), "공격 범위", baseStats.range, finalStats.range, FormatMetersOneDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritChance, "치명타확률", baseStats.critChance, finalStats.critChance, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritDamage, "치명타피해", baseStats.critDamageMultiplier, finalStats.critDamageMultiplier, FormatMultiplierAsPercent);
        AppendWeaponFloatNoStarsFixed(builder, "넉백", baseStats.knockback, finalStats.knockback, FormatZeroDecimal);

        return builder.ToString().TrimEnd();
    }

    private static WeaponGradeStatType ResolveMeleeRangeGradeStatType()
    {
        return WeaponGradeStatType.AttackRange;
    }

    private string BuildMagicWeaponGradeStatComparisonFixed(ItemData item, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        StringBuilder builder = new StringBuilder();

        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Damage, "데미지", baseStats.damage, finalStats.damage, FormatZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Rpm, "쿨타임", baseStats.attackInterval, finalStats.attackInterval, FormatSecondsTwoDecimals);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.Range, "사거리", baseStats.range, finalStats.range, FormatMetersOneDecimal);
        AppendWeaponFloatNoStarsFixed(builder, "투사체속도", baseStats.projectileSpeed, finalStats.projectileSpeed, FormatZeroDecimal);
        AppendWeaponFloatNoStarsFixed(builder, "투사체수", baseStats.projectileCount, finalStats.projectileCount, FormatZeroDecimal);
        WeaponItemData weaponData = item != null ? item.baseData as WeaponItemData : null;
        if (weaponData == null || weaponData.combatDefinition.usage.attackType != WeaponAttackType.Chain)
        {
            AppendWeaponFloatNoStarsFixed(builder, "투사체크기", baseStats.projectileSize, finalStats.projectileSize, FormatOneDecimal);
            AppendWeaponFloatNoStarsFixed(builder, "폭발범위", baseStats.explosionRadius, finalStats.explosionRadius, FormatMetersOneDecimal);
        }
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritChance, "치명타확률", baseStats.critChance, finalStats.critChance, FormatPercentZeroDecimal);
        AppendWeaponFloatFixed(builder, item, WeaponGradeStatType.CritDamage, "치명타피해", baseStats.critDamageMultiplier, finalStats.critDamageMultiplier, FormatMultiplierAsPercent);
        AppendWeaponFloatNoStarsFixed(builder, "넉백", baseStats.knockback, finalStats.knockback, FormatZeroDecimal);

        return builder.ToString().TrimEnd();
    }

    private void AppendWeaponFloatNoStarsFixed(StringBuilder builder, string label, float baseValue, float finalValue, System.Func<float, string> formatter)
    {
        builder.Append(label).Append(" ");

        if (!Mathf.Approximately(baseValue, finalValue))
            builder.Append("(").Append(formatter(baseValue)).Append(" -> <color=").Append(ChangedValueColor).Append(">").Append(formatter(finalValue)).Append("</color>)");
        else
            builder.Append(formatter(baseValue));

        builder.AppendLine();
    }

    private void AppendWeaponFloatFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType, string label, float baseValue, float finalValue, System.Func<float, string> formatter)
    {
        builder.Append(label).Append(" ");

        if (!Mathf.Approximately(baseValue, finalValue))
            builder.Append("(").Append(formatter(baseValue)).Append(" -> <color=").Append(ChangedValueColor).Append(">").Append(formatter(finalValue)).Append("</color>)");
        else
            builder.Append(formatter(baseValue));

        AppendStarTextFixed(builder, item, statType);
        builder.AppendLine();
    }

    private void AppendWeaponIntFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType, string label, int baseValue, int finalValue)
    {
        builder.Append(label).Append(" ");

        if (baseValue != finalValue)
            builder.Append("(").Append(baseValue).Append(" -> <color=").Append(ChangedValueColor).Append(">").Append(finalValue).Append("</color>)");
        else
            builder.Append(baseValue);

        AppendStarTextFixed(builder, item, statType);
        builder.AppendLine();
    }

    private void AppendStarTextFixed(StringBuilder builder, ItemData item, WeaponGradeStatType statType)
    {
        WeaponGradeStatRoll roll = WeaponGradeStatRoller.GetRoll(item != null ? item.weaponGradeStats : null, statType); // 별 롤
        if (roll == null || !roll.HasStars)
            return;

        List<WeaponGradeStarRoll> stars = roll.GetDisplayStars();
        if (stars.Count <= 0)
            return;

        builder.Append(" <size=68%>");
        for (int i = 0; i < stars.Count; i++)
        {
            WeaponGradeStarType starType = stars[i] != null ? stars[i].starType : WeaponGradeStarType.White;
            builder.Append("<color=").Append(GetGradeStarTextColor(starType)).Append(">");
            builder.Append(GradeStarMarker);
            builder.Append("</color>");
        }
        builder.Append("</size>");
    }

    private static string GetGradeStarTextColor(WeaponGradeStarType starType)
    {
        switch (starType)
        {
            case WeaponGradeStarType.Green: return "#68AA84";
            case WeaponGradeStarType.Yellow: return "#D2A85D";
            case WeaponGradeStarType.Red: return "#AE5962";
            default: return "#D5D8D8";
        }
    }

    private string FormatZeroDecimal(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
    }

    private string FormatMeleeAttackRange(float value)
    {
        return Mathf.Approximately(value, Mathf.Round(value))
            ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private string FormatOneDecimal(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private string FormatSecondsTwoDecimals(float value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture) + "초";
    }

    private string FormatMetersOneDecimal(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture) + "m";
    }

    private string FormatPercentZeroDecimal(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private string FormatMultiplierAsPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private void AppendComparedAttackRate(StringBuilder builder, WeaponFinalStats baseStats, WeaponFinalStats finalStats)
    {
        AppendComparedFloat(builder, "쿨다운", baseStats.attackInterval, finalStats.attackInterval, ItemTooltipFormatter.FormatSeconds, true);
    }

    private void AppendComparedFloat(StringBuilder builder, string label, float baseValue, float finalValue, System.Func<float, string> formatter, bool showWhenZero)
    {
        if (!showWhenZero && Mathf.Approximately(baseValue, 0f) && Mathf.Approximately(finalValue, 0f))
            return;

        bool changed = !Mathf.Approximately(baseValue, finalValue); // 변경 여부
        builder.Append(label).Append(" : ").Append(formatter(baseValue));

        if (changed)
            builder.Append(" -> <color=").Append(ChangedValueColor).Append(">(").Append(formatter(finalValue)).Append(")</color>");

        builder.AppendLine();
    }

    private void AppendComparedInt(StringBuilder builder, string label, int baseValue, int finalValue, System.Func<int, string> formatter, bool showWhenZero)
    {
        if (!showWhenZero && baseValue == 0 && finalValue == 0)
            return;

        builder.Append(label).Append(" : ").Append(formatter(baseValue));

        if (baseValue != finalValue)
            builder.Append(" -> <color=").Append(ChangedValueColor).Append(">(").Append(formatter(finalValue)).Append(")</color>");

        builder.AppendLine();
    }

    private bool HasValueOrChanged(float baseValue, float finalValue)
    {
        return !Mathf.Approximately(baseValue, 0f) || !Mathf.Approximately(finalValue, 0f) || !Mathf.Approximately(baseValue, finalValue);
    }

    private void SetActive(Component component, bool active)
    {
        if (component != null)
            component.gameObject.SetActive(active);
    }

    private void SetActive(GameObject gameObject, bool active)
    {
        if (gameObject != null)
            gameObject.SetActive(active);
    }

    private void RebuildTooltipLayout()
    {
        if (tooltipRect == null)
            return;

        if (currentItem != null)
            tooltipRect.sizeDelta = new Vector2(CalculateResponsiveWidth(currentItem), tooltipRect.sizeDelta.y);

        if (weaponHeaderRoot != null && weaponHeaderRoot.gameObject.activeSelf)
            LayoutRebuilder.ForceRebuildLayoutImmediate(weaponHeaderRoot);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);
        Canvas.ForceUpdateCanvases(); // ContentSizeFitter 최종 크기 확정
    }

    private float CalculateResponsiveWidth(ItemData item)
    {
        int maxLineLength = 0;
        UpdateMaxLineLength(ref maxLineLength, nameText);
        UpdateMaxLineLength(ref maxLineLength, weaponNameText);
        UpdateMaxLineLength(ref maxLineLength, basicStatsText);
        UpdateMaxLineLength(ref maxLineLength, weaponStatsText);
        UpdateMaxLineLength(ref maxLineLength, gemStatsText);
        UpdateMaxLineLength(ref maxLineLength, priceText);

        float textWidth = VtpPanelWidth + Mathf.Max(0, maxLineLength - 16) * 5.6f; // 글자 폭
        float meleeWidth = meleeStatListView != null && meleeStatListView.gameObject.activeSelf
            ? meleeStatListView.PreferredContentWidth + 20f
            : 0f;
        return Mathf.Clamp(Mathf.Max(VtpPanelWidth, textWidth, meleeWidth), VtpPanelWidth, ResponsiveMaxPanelWidth);
    }

    private void UpdateMaxLineLength(ref int maxLineLength, TextMeshProUGUI text)
    {
        if (text == null || string.IsNullOrEmpty(text.text))
            return;

        string plainText = StripRichText(text.text); // 태그 제거
        string[] lines = plainText.Split('\n');

        for (int i = 0; i < lines.Length; i++)
            maxLineLength = Mathf.Max(maxLineLength, lines[i].Length);
    }

    private string StripRichText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        bool insideTag = false; // rich tag 내부

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '<')
            {
                insideTag = true;
                continue;
            }

            if (c == '>')
            {
                insideTag = false;
                continue;
            }

            if (!insideTag)
                builder.Append(c);
        }

        return builder.ToString();
    }

    private void ConfigureRaycastBlocking()
    {
        if (tooltipPanel == null)
            return;

        tooltipCanvasGroup = tooltipPanel.GetComponent<CanvasGroup>();
        if (tooltipCanvasGroup == null)
            return;

        tooltipCanvasGroup.blocksRaycasts = false;
        tooltipCanvasGroup.interactable = false;

        Graphic[] graphics = tooltipPanel.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].raycastTarget = false;
    }

    private static Vector2 ReadPointerScreenPosition()
    {
        // GOAL A2: Mouse 직접 읽기 대신 UI Point 포인터 위치를 사용한다. 마우스 없으면 zero 폴백 유지.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        return facade != null ? facade.PointerPosition : Vector2.zero;
    }

    private void UpdateTooltipPosition(Vector2 pointerScreenPosition)
    {
        if (tooltipRect == null)
            return;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(true);

        RectTransform parentRect = tooltipRect.parent as RectTransform;
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        Vector2 desiredScreenPosition = pointerScreenPosition + tooltipPointerOffset;

        if (parentRect != null
            && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parentRect,
                desiredScreenPosition,
                eventCamera,
                out Vector3 desiredWorldPosition))
        {
            tooltipRect.position = desiredWorldPosition;
        }
        else
        {
            tooltipRect.position = desiredScreenPosition;
        }

        ClampTooltipToScreen(parentRect, eventCamera);
    }

    private void ClampTooltipToScreen(RectTransform parentRect, Camera eventCamera)
    {
        tooltipRect.GetWorldCorners(tooltipWorldCorners); // 최종 레이아웃 실제 코너
        Vector2 firstCorner = RectTransformUtility.WorldToScreenPoint(eventCamera, tooltipWorldCorners[0]);
        float minX = firstCorner.x;
        float maxX = firstCorner.x;
        float minY = firstCorner.y;
        float maxY = firstCorner.y;

        for (int i = 1; i < tooltipWorldCorners.Length; i++)
        {
            Vector2 screenCorner = RectTransformUtility.WorldToScreenPoint(eventCamera, tooltipWorldCorners[i]);
            minX = Mathf.Min(minX, screenCorner.x);
            maxX = Mathf.Max(maxX, screenCorner.x);
            minY = Mathf.Min(minY, screenCorner.y);
            maxY = Mathf.Max(maxY, screenCorner.y);
        }

        Vector2 correction = new Vector2(
            CalculateScreenCorrection(minX, maxX, Screen.width),
            CalculateScreenCorrection(minY, maxY, Screen.height));
        if (correction.sqrMagnitude <= 0.0001f)
            return;

        Vector2 anchorScreenPosition = RectTransformUtility.WorldToScreenPoint(eventCamera, tooltipRect.position);
        Vector2 correctedScreenPosition = anchorScreenPosition + correction;
        if (parentRect != null
            && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parentRect,
                correctedScreenPosition,
                eventCamera,
                out Vector3 correctedWorldPosition))
        {
            tooltipRect.position = correctedWorldPosition;
        }
        else
        {
            tooltipRect.position += new Vector3(correction.x, correction.y, 0f);
        }
    }

    private static float CalculateScreenCorrection(float minimum, float maximum, float screenSize)
    {
        float availableSize = Mathf.Max(0f, screenSize - ScreenEdgePadding * 2f);
        float contentSize = maximum - minimum;
        if (contentSize >= availableSize)
            return screenSize * 0.5f - (minimum + maximum) * 0.5f;

        if (minimum < ScreenEdgePadding)
            return ScreenEdgePadding - minimum;
        if (maximum > screenSize - ScreenEdgePadding)
            return screenSize - ScreenEdgePadding - maximum;
        return 0f;
    }
}





