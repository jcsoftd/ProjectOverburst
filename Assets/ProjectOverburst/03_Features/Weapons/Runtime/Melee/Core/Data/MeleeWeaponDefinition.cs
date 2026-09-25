using UnityEngine;

[CreateAssetMenu(
    fileName = "MeleeWeaponDefinition",
    menuName = "OVERBURST/Weapons/Melee Weapon Definition")]
public sealed class MeleeWeaponDefinition : WeaponCombatDefinition
{
    public override WeaponCombatFamily Family => WeaponCombatFamily.Melee;

    [Header("근접 기준값")]
    public MeleeWeaponBaseSettings baseSettings;

    [Header("전투 연결")]
    [InspectorName("전투 스타일")]
    public WeaponCombatStyle combatStyle = WeaponCombatStyle.MeleeWeapon;
    [InspectorName("전투 애니메이션 프로필")]
    public WeaponCombatAnimationProfile animationProfile;

    [Header("전투 이동")]
    [InspectorName("일반 전투 이동속도 (0 = 공용 값)")]
    [Min(0f)] public float combatMoveSpeed;

    [Header("공격")]
    [InspectorName("콤보 데이터")]
    public MeleeComboDefinition comboDefinition;

    [Header("가드")]
    [InspectorName("가드 설정")]
    public MeleeGuardSettings guard;

    [Header("무기 그립")]
    [InspectorName("무기 그립 설정")]
    public MeleeGripSettings grip;
}
