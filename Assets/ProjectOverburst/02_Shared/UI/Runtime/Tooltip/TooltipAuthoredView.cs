using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private GameObject dividerBasic;
    [SerializeField] private GameObject dividerWeapon;
    [SerializeField] private GameObject dividerPrice;

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
    public TextMeshProUGUI PriceText => priceText;
    public GameObject DividerBasic => dividerBasic;
    public GameObject DividerWeapon => dividerWeapon;
    public GameObject DividerPrice => dividerPrice;

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
        TextMeshProUGUI authoredPriceText,
        GameObject authoredDividerBasic,
        GameObject authoredDividerWeapon,
        GameObject authoredDividerPrice)
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
        priceText = authoredPriceText;
        dividerBasic = authoredDividerBasic;
        dividerWeapon = authoredDividerWeapon;
        dividerPrice = authoredDividerPrice;
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
                        && priceText != null
                        && dividerBasic != null
                        && dividerWeapon != null
                        && dividerPrice != null;

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
