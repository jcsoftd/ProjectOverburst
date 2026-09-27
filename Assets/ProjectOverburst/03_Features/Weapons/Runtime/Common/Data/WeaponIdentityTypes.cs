public enum WeaponCombatFamily
{
    None = 0,
    Melee = 1,
    Magic = 2,
    Ranged = 3
}

public enum WeaponElement
{
    None = 0,
    Fire = 1,
    Water = 2, // 저장 호환용 퇴역 식별자. 로드 시 Dark로 전환.
    Ice = 3,
    Electric = 4,
    Wind = 5, // 저장 호환용 퇴역 식별자
    Earth = 6,
    Nature = 7,
    Dark = 8,
    Light = 9
}
