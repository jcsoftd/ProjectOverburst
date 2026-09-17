using UnityEngine;

public enum WeaponClass // 무기 클래스
{
    Sword = 8,
    Greatsword = 9,
    Orb = 14
}

public enum WeaponAimType // 조준 방식
{
    None = 0,
    CasterLine = 2,
    MeleeFacing = 3
}

public enum WeaponAimMode // 우클릭 조준 해석
{
    Auto = 0,
    None = 1,
    Magic = 3,
    MeleeStance = 5,
    MeleeGuard = 6
}

public enum WeaponAimPoseBodyMode // 조준 포즈 적용 범위
{
    None = 0,
    UpperBody = 1,
    FullBody = 2
}

public enum WeaponUpperBodyAimChannel // 상체 조준 채널
{
    None = 0,
    Magic = 2
}

public enum WeaponAttackType // 공격 방식
{
    None = 0,
    Chain = 6,
    MeleeSlash = 8
}

public enum WeaponFireMode // 발사 방식
{
    None = 0,
    Cooldown = 5
}

public enum AttackRateDisplayType // 공격속도 표시
{
    Cooldown,
    RPM
}

public enum ProjectileRangeBehavior // 사거리 정책
{
    DestroyAtRange,
    ContinueWithDamageFalloff
}

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Items/Weapon")]
public class WeaponItemData : BaseItemData // 무기 데이터
{
    [Header("무기 기본 정보")]
    [InspectorName("무기 클래스")]
    public WeaponClass weaponClass = WeaponClass.Sword;
    [InspectorName("기본 속성")]
    public WeaponElement defaultElement = WeaponElement.None;

    [Header("무기 공통 능력치")]
    public WeaponBaseStats baseStats;

    [Header("무기 프리팹")]
    [InspectorName("장착 무기 프리팹")]
    public GameObject weaponRootPrefab;

    [Header("전투 정의")]
    [InspectorName("전투 정의")]
    public WeaponCombatDefinition combatDefinition;

    public WeaponCombatFamily CombatFamily => combatDefinition != null
        ? combatDefinition.Family
        : WeaponCombatFamily.None;

    public AnimationClip alternateAimPoseClip => combatDefinition != null
        ? combatDefinition.aim.alternatePoseClip
        : null;
    public AnimationClip fireAnimationClip => combatDefinition != null
        ? combatDefinition.animation.primaryAttackClip
        : null;
    public AnimationClip reloadAnimationClip => combatDefinition != null
        ? combatDefinition.animation.reloadClip
        : null;
    public AnimationClip quickFireAimPoseClip => combatDefinition != null
        ? combatDefinition.animation.quickFireAimPoseClip
        : null;
    public AnimationClip quickFireAnimationClip => combatDefinition != null
        ? combatDefinition.animation.quickFireClip
        : null;

    public MeleeWeaponDefinition GetMeleeDefinition()
    {
        return combatDefinition as MeleeWeaponDefinition;
    }

    public MagicWeaponDefinition GetMagicDefinition()
    {
        return combatDefinition as MagicWeaponDefinition;
    }

    public AnimationClip GetAimPoseClip()
    {
        return combatDefinition != null ? combatDefinition.aim.ResolvePoseClip() : null;
    }

    public WeaponAimPoseBodyMode GetResolvedAimPoseBodyMode()
    {
        if (combatDefinition == null || !combatDefinition.aim.usesUpperBodyPose)
            return WeaponAimPoseBodyMode.None;

        return combatDefinition.aim.poseBodyMode;
    }

    public WeaponUpperBodyAimChannel GetResolvedUpperBodyAimChannel()
    {
        if (GetResolvedAimPoseBodyMode() != WeaponAimPoseBodyMode.UpperBody)
            return WeaponUpperBodyAimChannel.None;

        if (combatDefinition != null && combatDefinition.aim.upperBodyChannel != WeaponUpperBodyAimChannel.None)
            return combatDefinition.aim.upperBodyChannel;

        if (CombatFamily == WeaponCombatFamily.Magic)
            return WeaponUpperBodyAimChannel.Magic;

        return WeaponUpperBodyAimChannel.None;
    }

    public WeaponAimMode GetResolvedAimMode()
    {
        if (combatDefinition == null)
            return WeaponAimMode.None;

        if (combatDefinition.aim.mode != WeaponAimMode.Auto)
            return combatDefinition.aim.mode;

        if (CombatFamily == WeaponCombatFamily.Magic && combatDefinition.usage.aimType == WeaponAimType.CasterLine)
            return WeaponAimMode.Magic;

        if (CombatFamily == WeaponCombatFamily.Melee
            && combatDefinition.usage.attackType == WeaponAttackType.MeleeSlash
            && GetMeleeDefinition() != null)
        {
            return WeaponAimMode.MeleeGuard;
        }

        return WeaponAimMode.None;
    }

    public WeaponCombatStyle GetResolvedCombatStyle()
    {
        MeleeWeaponDefinition meleeDefinition = GetMeleeDefinition();
        if (meleeDefinition != null && meleeDefinition.combatStyle != WeaponCombatStyle.None)
            return meleeDefinition.combatStyle;

        return WeaponCombatStyle.None;
    }

    public MeleeComboDefinition GetMeleeComboDefinition()
    {
        MeleeWeaponDefinition meleeDefinition = GetMeleeDefinition();
        return meleeDefinition != null ? meleeDefinition.comboDefinition : null;
    }

    public WeaponCombatAnimationProfile GetCombatAnimationProfile()
    {
        MeleeWeaponDefinition meleeDefinition = GetMeleeDefinition();
        return meleeDefinition != null ? meleeDefinition.animationProfile : null;
    }

    public MeleeGuardSettings GetMeleeGuardSettings()
    {
        MeleeWeaponDefinition meleeDefinition = GetMeleeDefinition();
        return meleeDefinition != null ? meleeDefinition.guard : default;
    }

    public MeleeGripSettings GetMeleeGripSettings()
    {
        MeleeWeaponDefinition meleeDefinition = GetMeleeDefinition();
        return meleeDefinition != null ? meleeDefinition.grip : default;
    }

    public WeaponItemData CreateRuntimeCopy(GameObject overrideWeaponRootPrefab)
    {
        WeaponItemData copy = CreateInstance<WeaponItemData>(); // 실행 중 복사본
        CopyTo(copy);

        if (overrideWeaponRootPrefab != null)
            copy.weaponRootPrefab = overrideWeaponRootPrefab;

        return copy;
    }

    private void CopyTo(WeaponItemData target)
    {
        target.itemName = itemName;
        target.icon = icon;
        target.color = color;
        target.description = description;
        target.weight = weight;
        target.sellPrice = sellPrice;
        target.worldPickupPrefab = worldPickupPrefab;
        target.possibleStats = possibleStats;
        target.weaponClass = weaponClass;
        target.defaultElement = defaultElement;
        target.baseStats = baseStats;
        target.weaponRootPrefab = weaponRootPrefab;
        target.combatDefinition = combatDefinition;
    }
}
