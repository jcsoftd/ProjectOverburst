using UnityEngine;

public static class GradeConfig // 등급색
{
    public static Color GetGradeColor(ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon:
                return new Color(0.35f, 1f, 0.35f);
            case ItemGrade.Rare:
                return new Color(0.35f, 0.65f, 1f);
            case ItemGrade.Epic:
                return new Color(0.75f, 0.35f, 1f);
            case ItemGrade.Legendary:
                return new Color32(0xFF, 0xB1, 0x3B, 0xFF);
            case ItemGrade.Artifact:
                return new Color32(0x36, 0xF2, 0xD0, 0xFF);
            case ItemGrade.Mythic:
                return new Color32(0xFF, 0x05, 0x05, 0xFF);
            case ItemGrade.Cursed:
                return new Color32(0x8B, 0x00, 0x3A, 0xFF);
            default:
                return new Color(0.75f, 0.75f, 0.75f); // 기본색
        }
    }

    public static string GetGradeColorHex(ItemGrade grade)
    {
        Color color = GetGradeColor(grade); // TMP 색상
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }
}
