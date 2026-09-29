using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum TooltipAuthoredViewKind
{
    Weapon = 0,
    // Value 1 retired. Preserve authored asset values.
    Bag = 2,
    Consumable = 3,
    Currency = 4,
    Default = 5
}

public partial class TooltipManager : MonoBehaviour // 툴팁 표시
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
    private static readonly List<TooltipManager> EnabledInstances = new List<TooltipManager>(); // 켜진 인스턴스. 재선출 때 씬 전체 탐색 대신 쓴다.
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
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private GameObject dividerBasic;
    [SerializeField] private GameObject dividerWeapon;
    [SerializeField] private GameObject dividerPrice;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEnabledInstances() => EnabledInstances.Clear();

    private void OnEnable()
    {
        if (!EnabledInstances.Contains(this))
            EnabledInstances.Add(this);
        RegisterInstanceCandidate(this);
    }

    private void OnDisable()
    {
        EnabledInstances.Remove(this);
        if (Instance == this)
        {
            Instance = null;
            RefreshActiveInstance(this);
        }
    }

    private void OnDestroy()
    {
        EnabledInstances.Remove(this);
        if (Instance == this)
        {
            Instance = null;
            RefreshActiveInstance(this);
        }
    }

    public static void RefreshActiveInstance()
    {
        RefreshActiveInstance(null);
    }

    private static void RefreshActiveInstance(TooltipManager excluded)
    {
        List<TooltipManager> managers = EnabledInstances;
        TooltipManager best = null;

        for (int i = 0; i < managers.Count; i++)
        {
            TooltipManager manager = managers[i];
            if (manager == excluded || !IsUsableInstance(manager))
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
        if(rpgTooltip){
            if(currentItem?.baseData is FlaskItemData && rpgTooltip.view.gameObject.activeSelf && Time.unscaledTime >= nextFlaskRefresh){
                nextFlaskRefresh=Time.unscaledTime+.2f;
                rpgTooltip.view.Present(currentItem,currentShopPriceContextActive?GetCurrentShopPrice(currentItem).ToString("N0")+"G":null);
            }
            rpgTooltip.Place();return;
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

    private const char GradeStarMarker = '◆'; // 장비 공통 품질 각인

}

