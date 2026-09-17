using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WeaponComboGemDetailView : MonoBehaviour
{
    [SerializeField] private RectTransform nativeSlotRoot;
    [SerializeField] private Image iconImage;
    [SerializeField] private SlotGradeEffect gradeEffect;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI typeText;
    [SerializeField] private TextMeshProUGUI optionText;
    [SerializeField] private TextMeshProUGUI descriptionText;

    public RectTransform NativeSlotRoot => nativeSlotRoot;

    public void Bind(WeaponComboGemSlotViewData data)
    {
        if (data == null || !data.IsUnlocked || !data.IsOccupied)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        if (iconImage != null)
        {
            iconImage.sprite = data.GemIcon;
            iconImage.color = data.GemIcon != null ? data.GemIconColor : Color.clear;
            iconImage.preserveAspect = true;
        }
        gradeEffect?.SetGrade(data.GemGrade, GradeConfig.GetGradeColor(data.GemGrade));
        if (nameText != null) nameText.text = data.GemName;
        if (typeText != null) typeText.text = data.TypeText;
        if (optionText != null)
        {
            optionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(data.OptionText));
            optionText.text = data.OptionText;
        }
        if (descriptionText != null) descriptionText.text = data.EffectDescription;
    }
}
