public static class ElementComboGemGradePolicy // 속성 보석 신규 생성 등급
{
    private static readonly ItemGrade[] GenerationGrades =
    {
        ItemGrade.Uncommon,
        ItemGrade.Rare,
        ItemGrade.Epic,
        ItemGrade.Legendary,
        ItemGrade.Artifact,
        ItemGrade.Mythic
    };

    public static int GenerationGradeCount => GenerationGrades.Length;

    public static bool IsGenerationEnabled(ItemGrade grade)
    {
        return grade >= ItemGrade.Uncommon && grade <= ItemGrade.Mythic;
    }

    public static ItemGrade[] GetGenerationGrades()
    {
        return (ItemGrade[])GenerationGrades.Clone(); // 외부 배열 변경 차단
    }

    public static bool TryGetGenerationGrade(int index, out ItemGrade grade)
    {
        if (index < 0 || index >= GenerationGrades.Length)
        {
            grade = default;
            return false;
        }

        grade = GenerationGrades[index];
        return true;
    }
}

public static class ElementComboGemGradePolicyFixture
{
    public static bool Validate()
    {
        if (ElementComboGemGradePolicy.GenerationGradeCount != 6
            || ElementComboGemGradePolicy.IsGenerationEnabled(ItemGrade.Common)
            || ElementComboGemGradePolicy.IsGenerationEnabled(ItemGrade.Cursed))
        {
            return false;
        }

        ItemGrade[] grades = ElementComboGemGradePolicy.GetGenerationGrades();
        if (grades.Length != 6)
            return false;

        for (int i = 0; i < grades.Length; i++)
        {
            ItemGrade expected = (ItemGrade)((int)ItemGrade.Uncommon + i);
            if (grades[i] != expected
                || !ElementComboGemGradePolicy.IsGenerationEnabled(grades[i])
                || !ElementComboGemGradePolicy.TryGetGenerationGrade(i, out ItemGrade resolved)
                || resolved != expected)
            {
                return false;
            }
        }

        return !ElementComboGemGradePolicy.TryGetGenerationGrade(-1, out _)
            && !ElementComboGemGradePolicy.TryGetGenerationGrade(grades.Length, out _);
    }
}

public static class ElementComboGemIconContractFixture
{
    public static bool Validate(ElementComboGemItemData asset, UnityEngine.Sprite[] expectedGradeIcons)
    {
        if (asset == null
            || expectedGradeIcons == null
            || expectedGradeIcons.Length != 8
            || asset.GetIcon(ItemGrade.Common) != null
            || asset.GetIcon(ItemGrade.Cursed) != null)
        {
            return false;
        }

        ItemGrade[] grades = ElementComboGemGradePolicy.GetGenerationGrades();
        for (int i = 0; i < grades.Length; i++)
        {
            int gradeIndex = (int)grades[i];
            if (expectedGradeIcons[gradeIndex] == null
                || asset.GetIcon(grades[i]) != expectedGradeIcons[gradeIndex])
            {
                return false;
            }
        }

        return true;
    }
}
