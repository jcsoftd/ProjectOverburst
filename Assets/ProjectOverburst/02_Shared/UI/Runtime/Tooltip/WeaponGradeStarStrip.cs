using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WeaponGradeStarStrip : MonoBehaviour // 능력치 한 줄의 별 표시
{
    public const int AuthoredStarCapacity = 25; // 긍정 20 + 저주 5 최대치

    private const float TextStarWidth = 12f;
    private const float TextStarHeight = 18f;
    private const float ImageStarSize = 13f;

    [SerializeField] private HorizontalLayoutGroup layoutGroup; // 정식 레이아웃
    [SerializeField] private GameObject[] starRoots; // 정식 별 루트
    [SerializeField] private LayoutElement[] starLayouts; // 별 크기
    [SerializeField] private TextMeshProUGUI[] starTexts; // 이미지 누락 대체
    [SerializeField] private Image[] starImages; // 정식 별 이미지

    private bool missingViewLogged;

    public bool HasAuthoredView
    {
        get
        {
            return layoutGroup != null
                && HasCapacity(starRoots)
                && HasCapacity(starLayouts)
                && HasCapacity(starTexts)
                && HasCapacity(starImages);
        }
    }

    public void SetStars(
        IReadOnlyList<WeaponGradeStarRoll> stars,
        WeaponGradeStarSpriteSet spriteSet)
    {
        if (!HasAuthoredView)
        {
            LogMissingAuthoredView();
            gameObject.SetActive(false);
            return;
        }

        int requestedCount = stars != null ? stars.Count : 0;
        int visibleCount = Mathf.Min(requestedCount, AuthoredStarCapacity);
        if (requestedCount > AuthoredStarCapacity)
        {
            Debug.LogError(
                "[WeaponGradeStarStrip] 정식 별 슬롯 상한을 초과했습니다. requested="
                + requestedCount + " capacity=" + AuthoredStarCapacity,
                this);
        }

        for (int i = 0; i < visibleCount; i++)
        {
            WeaponGradeStarType starType = stars[i] != null
                ? stars[i].starType
                : WeaponGradeStarType.White;
            SetStar(i, starType, spriteSet);
            starRoots[i].SetActive(true);
        }

        for (int i = visibleCount; i < AuthoredStarCapacity; i++)
            starRoots[i].SetActive(false);

        gameObject.SetActive(visibleCount > 0);
    }

    public void Clear()
    {
        if (!HasAuthoredView)
        {
            LogMissingAuthoredView();
            gameObject.SetActive(false);
            return;
        }

        for (int i = 0; i < AuthoredStarCapacity; i++)
            starRoots[i].SetActive(false);

        gameObject.SetActive(false);
    }

    private void SetStar(
        int index,
        WeaponGradeStarType starType,
        WeaponGradeStarSpriteSet spriteSet)
    {
        Sprite sprite = spriteSet != null ? spriteSet.GetSprite(starType) : null;
        bool useImage = sprite != null;

        TextMeshProUGUI text = starTexts[index];
        Image image = starImages[index];
        LayoutElement layout = starLayouts[index];
        text.gameObject.SetActive(!useImage);
        image.gameObject.SetActive(useImage);
        layout.preferredWidth = useImage ? ImageStarSize : TextStarWidth;
        layout.preferredHeight = useImage ? ImageStarSize : TextStarHeight;

        if (useImage)
        {
            image.sprite = sprite;
            image.color = Color.white;
            return;
        }

        text.text = "◆";
        text.color = GetTextColor(starType);
    }

    private static bool HasCapacity<T>(T[] values) where T : Object
    {
        if (values == null || values.Length != AuthoredStarCapacity)
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
        if (missingViewLogged)
            return;

        missingViewLogged = true;
        Debug.LogError(
            "[WeaponGradeStarStrip] 정식 별 오브젝트 참조가 없습니다. Tooltip View Objectizer를 실행하세요.",
            this);
    }

    private static Color GetTextColor(WeaponGradeStarType starType)
    {
        switch (starType)
        {
            case WeaponGradeStarType.Green: return new Color32(104, 170, 132, 255);
            case WeaponGradeStarType.Yellow: return new Color32(210, 168, 93, 255);
            case WeaponGradeStarType.Red: return new Color32(174, 89, 98, 255);
            default: return new Color32(213, 216, 216, 255);
        }
    }
}
