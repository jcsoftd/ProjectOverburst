using UnityEngine;

[System.Serializable]
public struct DirectionalAnimationSet8
{
    [InspectorName("앞")] public AnimationClip forward;
    [InspectorName("뒤")] public AnimationClip backward;
    [InspectorName("왼쪽")] public AnimationClip left;
    [InspectorName("오른쪽")] public AnimationClip right;
    [InspectorName("왼쪽 앞")] public AnimationClip forwardLeft;
    [InspectorName("오른쪽 앞")] public AnimationClip forwardRight;
    [InspectorName("왼쪽 뒤")] public AnimationClip backwardLeft;
    [InspectorName("오른쪽 뒤")] public AnimationClip backwardRight;

    public AnimationClip[] ToArray()
    {
        return new[] { forward, backward, left, right, forwardLeft, forwardRight, backwardLeft, backwardRight };
    }
}

[System.Serializable]
public struct DirectionalAnimationSet4
{
    [InspectorName("앞")] public AnimationClip forward;
    [InspectorName("뒤")] public AnimationClip backward;
    [InspectorName("왼쪽")] public AnimationClip left;
    [InspectorName("오른쪽")] public AnimationClip right;

    public AnimationClip[] ToArray()
    {
        return new[] { forward, backward, left, right };
    }
}

[System.Serializable]
public struct DirectionalLocomotionSpeedSet8
{
    [InspectorName("앞")] public float forward;
    [InspectorName("뒤")] public float backward;
    [InspectorName("왼쪽")] public float left;
    [InspectorName("오른쪽")] public float right;
    [InspectorName("왼쪽 앞")] public float forwardLeft;
    [InspectorName("오른쪽 앞")] public float forwardRight;
    [InspectorName("왼쪽 뒤")] public float backwardLeft;
    [InspectorName("오른쪽 뒤")] public float backwardRight;

    public float GetSpeed(Vector3 localDirection)
    {
        float sector = Mathf.Repeat(Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg, 360f) / 45f;
        int first = Mathf.FloorToInt(sector);
        float firstSpeed = GetSpeedAtSector(first);
        float secondSpeed = GetSpeedAtSector((first + 1) % 8);
        return firstSpeed > 0f && secondSpeed > 0f
            ? Mathf.Lerp(firstSpeed, secondSpeed, sector - first)
            : 0f;
    }

    private float GetSpeedAtSector(int sector)
    {
        switch (sector)
        {
            case 0: return forward;
            case 1: return forwardRight;
            case 2: return right;
            case 3: return backwardRight;
            case 4: return backward;
            case 5: return backwardLeft;
            case 6: return left;
            default: return forwardLeft;
        }
    }
}

public enum WeaponCombatStyle
{
    None = 0,
    MeleeWeapon = 10
}

[CreateAssetMenu(fileName = "WeaponCombatAnimationProfile", menuName = "OVERBURST/Weapons/Combat Animation Profile")]
public class WeaponCombatAnimationProfile : ScriptableObject
{
    [Header("전투 연결")]
    [InspectorName("전투 스타일")]
    public WeaponCombatStyle combatStyle = WeaponCombatStyle.MeleeWeapon;
    [InspectorName("애니메이터 오버라이드 컨트롤러")]
    public AnimatorOverrideController animatorOverrideController;
    [InspectorName("전투 레이어 이름")]
    public string animatorLayerName = "Combat_MeleeWeapon";
    [InspectorName("전환 하체 레이어 이름")]
    public string transitionLowerLayerName = "Combat_MeleeWeapon_TransitionLower";

    [Header("애니메이터 상태")]
    [InspectorName("빈 상태 이름")]
    public string emptyStateName = "Melee_Empty";
    [InspectorName("무기 장착 상태 이름")]
    public string equipStateName = "Melee_Equip";
    [InspectorName("전투 이동 상태 이름")]
    public string locomotionStateName = "Melee_Locomotion";
    [InspectorName("가드 이동 상태 이름")]
    public string guardLocomotionStateName = "Melee_GuardLocomotion";
    [InspectorName("무기 해제 상태 이름")]
    public string unequipStateName = "Melee_Unequip";
    [InspectorName("막기 상태 이름")]
    public string blockStateName = "Melee_Block";
    [InspectorName("이동 막기 상태 이름")]
    public string movingBlockStateName = "Melee_MovingBlock";
    [InspectorName("피격 상태 접두사")]
    public string hitStateNamePrefix = "Melee_Hit";
    [InspectorName("점프 상태 이름")]
    public string jumpStateName = "Melee_Jump";
    [InspectorName("구르기 상태 이름")]
    public string rollStateName = "Melee_Roll";
    [InspectorName("공격 상태 이름")]
    public string attackStateName = "Melee_Attack";
    [InspectorName("공격 교체 기준 클립")]
    public AnimationClip attackTemplateClip;
    [InspectorName("전환 하체 빈 상태 이름")]
    public string transitionLowerEmptyStateName = "Melee_TransitionLower_Empty";
    [InspectorName("전환 하체 이동 상태 이름")]
    public string transitionLowerLocomotionStateName = "Melee_TransitionLower_Locomotion";
    [InspectorName("행동 속도 파라미터 이름")]
    public string actionSpeedParameterName = "Melee_ActionSpeed";

    [Header("전투 애니메이션")]
    [InspectorName("전투 진입")]
    public AnimationClip equipClip;
    [InspectorName("전투 해제")]
    public AnimationClip unequipClip;
    [InspectorName("전투 대기")]
    public AnimationClip combatIdleClip;
    [InspectorName("8방향 이동")]
    public DirectionalAnimationSet8 locomotion;
    [InspectorName("가드 대기")]
    public AnimationClip guardIdleClip;
    [InspectorName("가드 4방향 이동")]
    public DirectionalAnimationSet4 guardLocomotion;
    [InspectorName("가드 중 8방향 하체 이동 합성")]
    public bool useTransitionLowerBodyWhileGuarding;
    [InspectorName("정지 가드 성공")]
    public AnimationClip blockClip;
    [InspectorName("이동 가드 성공")]
    public AnimationClip movingBlockClip;
    [InspectorName("피격 목록")]
    public AnimationClip[] hitClips = new AnimationClip[3];
    [InspectorName("점프")]
    public AnimationClip jumpClip;
    [InspectorName("구르기 / 회피")]
    public AnimationClip rollClip;
    [InspectorName("구르기 이동 방향 기준 몸 회전각")]
    public float rollFacingYawOffset;

    [Header("전환 시간")]
    [InspectorName("레이어 진입 페이드")]
    public float layerFadeInDuration = 0.08f;
    [InspectorName("레이어 해제 페이드")]
    public float layerFadeOutDuration = 0.12f;
    [InspectorName("장착 후 이동 전환 시간")]
    public float equipToLocomotionTransitionDuration = 0.12f;
    [InspectorName("이동에서 가드 전환 시간")]
    public float locomotionToGuardTransitionDuration = 0.08f;
    [InspectorName("가드 이동 진입 전환 시간")]
    public float guardLocomotionEnterTransitionDuration = 0.16f;
    [InspectorName("가드 이동 해제 전환 시간")]
    public float guardLocomotionExitTransitionDuration = 0.12f;
    [InspectorName("가드 이동 시작 오프셋")]
    public float guardLocomotionStartOffsetSeconds = 0.05f;
    [InspectorName("행동 전환 시간")]
    public float actionTransitionDuration = 0.06f;
    [InspectorName("이동 중 전환 종료 블렌딩")]
    public float movingTransitionEndBlendDuration = 0.18f;
    [InspectorName("해제 시 등 이동 선행시간")]
    public float unequipWeaponBackLeadTime = 0.3f;
    [InspectorName("레거시 행동 억제 페이드")]
    public float legacyActionSuppressionFadeDuration = 0.04f;
    [InspectorName("전환 하체 레이어 페이드")]
    public float transitionLowerLayerFadeDuration = 0.05f;
    [InspectorName("전환 하체 이동 입력 임계값")]
    public float transitionLowerMoveInputThreshold = 0.05f;

    [Header("재생 속도")]
    [InspectorName("전투 이동 애니메이션 속도 배율")]
    public float locomotionAnimationSpeedMultiplier = 1f;
    [InspectorName("8방향 달리기 원본 속도에 실제 이동속도 맞춤")]
    public bool matchMovementToLocomotionSpeed;
    [InspectorName("8방향 원본 이동속도 (m/s)")]
    public DirectionalLocomotionSpeedSet8 locomotionReferenceSpeeds;
    [InspectorName("장착 애니메이션 속도 배율")]
    public float equipAnimationSpeedMultiplier = 1.3f;
    [InspectorName("해제 애니메이션 속도 배율")]
    public float unequipAnimationSpeedMultiplier = 1.5f;
    [InspectorName("해제 애니메이션 시작 오프셋")]
    public float unequipAnimationStartOffsetSeconds = 0.1f;
}
