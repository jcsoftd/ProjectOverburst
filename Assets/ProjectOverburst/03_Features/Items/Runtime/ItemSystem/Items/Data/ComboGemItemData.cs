using System.Collections.Generic;
using UnityEngine;

public enum ComboGemRandomOptionType // 콤보 보석 옵션
{
    ElementDamageIncrease = 0
}

[System.Serializable]
public sealed class ComboGemRandomOptionRange // 등급별 옵션 범위
{
    public ComboGemRandomOptionType optionType;
    public float[] minValuePerGrade = new float[8];
    public float[] maxValuePerGrade = new float[8];

    public bool TryGetRange(ItemGrade grade, out float minValue, out float maxValue)
    {
        minValue = 0f;
        maxValue = 0f;
        if (minValuePerGrade == null || minValuePerGrade.Length == 0
            || maxValuePerGrade == null || maxValuePerGrade.Length == 0)
        {
            return false;
        }

        int gradeIndex = Mathf.Clamp((int)grade, 0, Mathf.Min(minValuePerGrade.Length, maxValuePerGrade.Length) - 1);
        minValue = Mathf.Max(0f, minValuePerGrade[gradeIndex]);
        maxValue = Mathf.Max(0f, maxValuePerGrade[gradeIndex]);
        return maxValue >= minValue && maxValue > 0f;
    }
}

public abstract class ComboGemItemData : BaseItemData // 콤보 보석 원본
{
    [Header("Combo Gem")]
    public ItemGrade minGrade = ItemGrade.Common;
    public Sprite[] gradeIcons = new Sprite[8];
    [Header("Random Options")]
    public int[] optionCountPerGrade = { 1, 1, 2, 2, 3, 3, 4, 3 };
    public ComboGemRandomOptionRange[] randomOptionRanges;

    public abstract ComboGemType GemType { get; }
    public abstract bool HasValidDefinition { get; }

    public virtual Sprite GetIcon(ItemGrade grade)
    {
        int index = Mathf.Clamp((int)grade, 0, gradeIcons != null ? gradeIcons.Length - 1 : 0);
        if (gradeIcons != null && gradeIcons.Length > 0 && gradeIcons[index] != null)
            return gradeIcons[index];

        return icon;
    }

    public int GetOptionCount(ItemGrade grade)
    {
        if (optionCountPerGrade == null || optionCountPerGrade.Length == 0)
            return 0;

        int index = Mathf.Clamp((int)grade, 0, optionCountPerGrade.Length - 1);
        return Mathf.Max(0, optionCountPerGrade[index]);
    }

    public List<ComboGemRolledOption> RollOptions(ItemGrade grade)
    {
        List<ComboGemRolledOption> results = new List<ComboGemRolledOption>();
        int desiredCount = GetOptionCount(grade);
        if (desiredCount <= 0 || randomOptionRanges == null)
            return results;

        List<ComboGemRandomOptionRange> candidates = new List<ComboGemRandomOptionRange>();
        HashSet<ComboGemRandomOptionType> uniqueTypes = new HashSet<ComboGemRandomOptionType>();
        for (int i = 0; i < randomOptionRanges.Length; i++)
        {
            ComboGemRandomOptionRange range = randomOptionRanges[i];
            if (range == null
                || !IsOptionAllowed(range.optionType)
                || !range.TryGetRange(grade, out _, out _)
                || !uniqueTypes.Add(range.optionType))
            {
                continue;
            }

            candidates.Add(range); // 유효한 고유 옵션만 후보
        }

        int rollCount = Mathf.Min(desiredCount, candidates.Count);
        for (int i = 0; i < rollCount; i++)
        {
            int candidateIndex = Random.Range(0, candidates.Count);
            ComboGemRandomOptionRange range = candidates[candidateIndex];
            candidates.RemoveAt(candidateIndex); // 옵션 타입 중복 방지

            range.TryGetRange(grade, out float minValue, out float maxValue);
            float value = maxValue > minValue ? Random.Range(minValue, maxValue) : minValue;
            results.Add(new ComboGemRolledOption(range.optionType, value));
        }

        return results;
    }

    protected abstract bool IsOptionAllowed(ComboGemRandomOptionType optionType);
}

public static partial class WeaponComboGemSlotRules
{
    public static bool TryGetClassifiedType(ComboGemItemData gemData, out ComboGemType gemType)
    {
        gemType = gemData != null ? gemData.GemType : ComboGemType.Unspecified;
        if (gemData == null || !gemData.HasValidDefinition)
        {
            gemType = ComboGemType.Unspecified;
            return false;
        }

        return IsGemTypeAllowedForAnySlot(gemType);
    }

    public static WeaponComboGemSlotValidationResult ValidateGemData(
        int slotIndex,
        int unlockedSlotCount,
        ComboGemItemData gemData)
    {
        TryGetClassifiedType(gemData, out ComboGemType gemType);
        return Validate(slotIndex, unlockedSlotCount, gemType);
    }

    private static bool IsGemTypeAllowedForAnySlot(ComboGemType gemType)
    {
        return gemType == ComboGemType.Element
            || gemType == ComboGemType.Link
            || gemType == ComboGemType.Enhancement;
    }
}
