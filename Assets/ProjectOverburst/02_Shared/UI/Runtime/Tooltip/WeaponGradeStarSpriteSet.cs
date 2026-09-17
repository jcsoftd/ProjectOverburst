using UnityEngine;

[CreateAssetMenu(fileName = "WeaponGradeStarSpriteSet", menuName = "UI/Tooltip/Weapon Grade Star Sprite Set")]
public class WeaponGradeStarSpriteSet : ScriptableObject // 교체용 별 이미지 묶음
{
    [SerializeField] private Sprite whiteStar;
    [SerializeField] private Sprite greenStar;
    [SerializeField] private Sprite yellowStar;
    [SerializeField] private Sprite redStar;

    public Sprite GetSprite(WeaponGradeStarType starType)
    {
        switch (starType)
        {
            case WeaponGradeStarType.White: return whiteStar;
            case WeaponGradeStarType.Green: return greenStar;
            case WeaponGradeStarType.Yellow: return yellowStar;
            case WeaponGradeStarType.Red: return redStar;
            default: return null;
        }
    }
}
