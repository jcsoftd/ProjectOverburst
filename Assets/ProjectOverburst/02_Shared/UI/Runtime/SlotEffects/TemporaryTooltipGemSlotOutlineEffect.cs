using UnityEngine;
using UnityEngine.UI;

public class TemporaryTooltipGemSlotOutlineEffect : MonoBehaviour // 임시 툴팁 보석슬롯 외곽선
{
    private static readonly bool UseTemporaryTooltipGemSlotOutline = true; // false면 임시 효과 비활성
    private const string OutlineObjectName = "TemporaryTooltipGemSlotOutline";

    private ExperimentalSlotOutlineEffect outlineEffect;
    private Image outlineImage;

    public void SetItem(ItemData item)
    {
        if (!UseTemporaryTooltipGemSlotOutline || item == null || !item.HasValidBaseData || item.grade == ItemGrade.Common)
        {
            Clear();
            return;
        }

        EnsureOutline();

        if (outlineEffect == null)
            return;

        Color gradeColor = GradeConfig.GetGradeColor(item.grade);
        ExperimentalSlotOutlineMode mode = ResolveMode(item.grade);

        outlineEffect.gameObject.SetActive(true);
        outlineEffect.SetGradeColor(gradeColor);
        outlineEffect.SetCircleShape(false);
        outlineEffect.SetMode(mode);
        outlineEffect.SetIntensity(
            GetThickness(item.grade),
            GetGlowIntensity(item.grade),
            GetGlowSize(item.grade),
            GetNoiseStrength(item.grade),
            GetPulseSpeed(item.grade),
            GetAlpha(item.grade));
    }

    public void Clear()
    {
        if (outlineEffect != null)
            outlineEffect.gameObject.SetActive(false);
    }

    private void EnsureOutline()
    {
        if (outlineEffect != null)
            return;

        Transform existing = transform.Find(OutlineObjectName);
        GameObject outlineObject = existing != null ? existing.gameObject : new GameObject(OutlineObjectName, typeof(RectTransform));
        outlineObject.transform.SetParent(transform, false);
        outlineObject.transform.SetAsFirstSibling();

        outlineImage = outlineObject.GetComponent<Image>();
        if (outlineImage == null)
            outlineImage = outlineObject.AddComponent<Image>();

        outlineImage.color = Color.white;
        outlineImage.raycastTarget = false;

        RectTransform rect = outlineObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        outlineEffect = outlineObject.GetComponent<ExperimentalSlotOutlineEffect>();
        if (outlineEffect == null)
            outlineEffect = outlineObject.AddComponent<ExperimentalSlotOutlineEffect>();

        outlineObject.SetActive(false);
    }

    private ExperimentalSlotOutlineMode ResolveMode(ItemGrade grade)
    {
        if (grade == ItemGrade.Uncommon || grade == ItemGrade.Rare)
            return ExperimentalSlotOutlineMode.InnerElectric;

        return ExperimentalSlotOutlineMode.TightFlameOrbit;
    }

    private float GetThickness(ItemGrade grade)
    {
        if (grade == ItemGrade.Uncommon)
            return 0.015f;

        if (grade == ItemGrade.Rare)
            return 0.02f;

        if (grade == ItemGrade.Epic)
            return 0.023f;

        if (grade == ItemGrade.Legendary)
            return 0.027f;

        return 0.032f;
    }

    private float GetGlowIntensity(ItemGrade grade)
    {
        if (grade == ItemGrade.Uncommon)
            return 2.2f;

        if (grade == ItemGrade.Rare)
            return 3.1f;

        if (grade == ItemGrade.Epic)
            return 4.4f;

        if (grade == ItemGrade.Legendary)
            return 5.1f;

        return 5.8f;
    }

    private float GetGlowSize(ItemGrade grade)
    {
        if (grade == ItemGrade.Uncommon)
            return 0.014f;

        if (grade == ItemGrade.Rare)
            return 0.02f;

        if (grade == ItemGrade.Epic)
            return 0.05f;

        if (grade == ItemGrade.Legendary)
            return 0.062f;

        return 0.078f;
    }

    private float GetNoiseStrength(ItemGrade grade)
    {
        if (grade == ItemGrade.Uncommon)
            return 0.65f;

        if (grade == ItemGrade.Rare)
            return 0.92f;

        if (grade == ItemGrade.Epic)
            return 1.08f;

        if (grade == ItemGrade.Legendary)
            return 1.22f;

        return 1.38f;
    }

    private float GetPulseSpeed(ItemGrade grade)
    {
        if (grade == ItemGrade.Uncommon)
            return 0.55f;

        if (grade == ItemGrade.Rare)
            return 0.9f;

        if (grade == ItemGrade.Epic)
            return 1.25f;

        if (grade == ItemGrade.Legendary)
            return 1.5f;

        return 1.7f;
    }

    private float GetAlpha(ItemGrade grade)
    {
        if (grade == ItemGrade.Uncommon)
            return 0.78f;

        if (grade == ItemGrade.Rare)
            return 0.9f;

        return 1f;
    }
}
