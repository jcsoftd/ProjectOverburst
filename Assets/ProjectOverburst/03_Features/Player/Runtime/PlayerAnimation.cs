using UnityEngine;
using System;
using System.Collections.Generic;

[DefaultExecutionOrder(360)]
[RequireComponent(typeof(PlayerMovement))]
public partial class PlayerAnimation : MonoBehaviour // 플레이어 애니
{
    private const string BaseLayerName = "Base Layer";
    private const string FullBodyAimLayerName = "FullBody_Aim";
    private const string FullBodyAimStateName = "FullBodyAim";
    private static readonly int ExplorationLocomotionSpeedParameterHash = Animator.StringToHash("ExplorationLocomotionSpeed");

    [Header("References")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private Rigidbody targetRigidbody;
    [SerializeField] private PlayerEquipment playerEquipment;

    [Header("Animator Parameter Names")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string moveXParameter = "MoveX";
    [SerializeField] private string moveYParameter = "MoveY";
    [SerializeField] private string groundedParameter = "IsGrounded";
    [SerializeField] private string verticalVelocityParameter = "VerticalVelocity";
    [SerializeField] private string aimingParameter = "IsAiming";
    [SerializeField] private string jumpTriggerParameter = "JumpTrigger";
    [SerializeField] private string fireTriggerParameter = "FireTrigger";
    [SerializeField] private string recoverTriggerParameter = "RecoverTrigger";
    [SerializeField] private string fireAnimationSpeedParameter = "FireAnimationSpeed";
    [SerializeField] private string combatModeParameter = "IsCombatMode";
    [SerializeField] private string guardingParameter = "IsGuarding";

    [Header("Blend")]
    [SerializeField] private float moveSmoothTime = 0.12f;
    [SerializeField] private float speedDampTime = 0.08f;

    [Header("Weapon Aim Pose Override")]
    [SerializeField] private bool useWeaponAimPoseOverride = true;
    [SerializeField] private AnimationClip baseAimClip;
    [SerializeField] private string baseAimClipName = "MagicAim";

    [Header("Weapon Action Clip Override")]
    [SerializeField] private bool useWeaponActionClipOverride = true;
    [SerializeField] private AnimationClip baseFireClip;
    [SerializeField] private string baseFireClipName = "MagicCast";

    [Header("Weapon Action States")]
    [SerializeField] private bool restartWeaponActionStates = true;
    [SerializeField] private string weaponActionLayerName = "UpperBody_Magic"; // 옛 마법 기본값. 프리팹 저장값이라 남긴다.
    [SerializeField] private string aimStateName = "MagicAim";
    [SerializeField] private string fireStateName = "MagicCast";
    [SerializeField] private string recoverStateName = "MagicRecover";

    [Header("Melee Full Body Action")]
    [SerializeField] private string meleeFullBodyStateName = "NormalIdle";
    [SerializeField] private AnimationClip meleeFullBodyBaseClip;
    [SerializeField] private string meleeFullBodyBaseClipName = "Idle_JawFixed";
    [SerializeField] private AnimationClip combatEquipClip;
    [SerializeField] private AnimationClip combatEquipExitClip;
    [SerializeField] private AnimationClip combatBlockClip;
    [SerializeField] private AnimationClip combatMovingBlockClip;
    [SerializeField] private AnimationClip combatJumpClip;
    [SerializeField] private AnimationClip[] combatHitClips = Array.Empty<AnimationClip>();
    [SerializeField] private float combatEquipTransitionDuration = 0.08f;
    [SerializeField] private float combatEquipEndTrimDuration;
    [SerializeField] private float combatEquipExitTransitionDuration = 0.08f;
    [SerializeField] private float combatEquipExitWeaponBackLeadTime = 0.3f;
    [SerializeField] private float combatBlockTransitionDuration = 0.04f;
    [SerializeField] private float combatJumpTransitionDuration = 0.06f;
    [SerializeField] private float combatHitTransitionDuration = 0.04f;

    [Header("Combat Locomotion States")]
    [SerializeField] private bool useCombatLocomotionStates = true;
    [SerializeField] private string normalLocomotionStateName = "NormalIdle";
    [SerializeField] private string combatLocomotionStateName = "CombatLocomotion";
    [SerializeField] private string combatGuardLocomotionStateName = "CombatGuardLocomotion";
    [SerializeField] private float combatLocomotionTransitionDuration = 0.1f;
    [SerializeField] private float combatLocomotionEntryFixedTimeOffset;
    [SerializeField] private float combatEquipToLocomotionTransitionDuration = 0.22f;

    [Header("Melee Aim Upper Body Offset")]
    [SerializeField] private bool useMeleeAimUpperBodyYawOffset = true;
    [SerializeField] private float meleeAimUpperBodyYawOffset = 60f;

    [Header("Combat Animator Routing")]
    [SerializeField] private bool useWeaponCombatAnimatorRouter = true;
    [SerializeField] private bool useLegacyCombatBaseLayerStates;
    [SerializeField] private bool useLegacyWeaponAimLayers;
    [SerializeField] private WeaponCombatAnimatorRouter weaponCombatAnimatorRouter;

    private PlayerMovement playerController; // 이동 상태
    private Vector2 currentMoveBlend; // 이동 blend
    private Vector2 moveBlendVelocity; // 부드러운 보간
    private RuntimeAnimatorController baseAnimatorController; // 원본 controller
    private AnimatorOverrideController weaponOverrideController; // 무기 override
    private AnimationClip activeAimPoseClip; // 조준 포즈
    private AnimationClip activeFireClip; // 발사 clip
    private AnimationClip forcedAimPoseClip; // 강제 포즈
    private float forcedAimPoseNormalizedTime; // 강제 시간
    private bool useForcedAimPoseTime; // 시간 고정
    private AnimationClip quickFireAimPoseClip; // QuickFire 포즈
    private float quickFireAimPoseUntil; // QuickFire 시간
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> weaponOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); // override 목록
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> meleeFullBodyRestoreOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); // 복구 목록
    private int weaponActionLayerIndex = -1; // 액션 layer
    private int fullBodyAimLayerIndex = -1; // 전신 조준 layer
    private int fireStateHash; // 발사 hash
    private int recoverStateHash; // 회복 액션 hash
    private int fullBodyAimStateHash; // 전신 조준 hash
    private bool actionStateHashesInitialized; // hash 초기화
    private WeaponUpperBodyAimChannel activeWeaponActionChannel = WeaponUpperBodyAimChannel.None; // 현재 상체 채널
    private bool isMeleeFullBodyActionActive; // 근접 전신
    private float meleeFullBodyActionEndTime; // 전신 종료
    private float animatorSpeedBeforeMeleeFullBody = 1f; // 속도 복구
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> fullBodyAimPoseRestoreOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); // 전신 조준 복구
    private bool isFullBodyAimPoseActive; // 전신 조준 포즈
    private AnimationClip activeFullBodyAimPoseClip; // 전신 조준 clip
    private float weaponActionLayerWeight = 1f; // 상체 layer weight
    private float fullBodyAimLayerWeight; // 전신 layer weight
    private float weaponActionLayerHoldUntil; // 액션 layer 유지
    private string activeWeaponActionLayerName; // 실제 사용 layer
    private bool useCombatLocomotionEntryOffsetOnNextEnter;
    private bool useCombatEquipToLocomotionBlendOnNextEnter;
    private bool deferMeleeFullBodyRestoreToCombatLocomotion;
    private bool isMeleeFullBodyRestorePending;
    private float meleeFullBodyRestoreTime;

    private PlayerCombatModeController combatModeController;

    private void Awake()
    {
        playerController = GetComponent<PlayerMovement>(); // 이동 컨트롤러

        RefreshAnimatorReference();

        if (targetRigidbody == null)
            targetRigidbody = GetComponent<Rigidbody>(); // 물리체

        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>(); // 장비

        ResolveWeaponCombatAnimatorRouter();
    }

    private void OnEnable()
    {
        RefreshAnimatorReference();
        ResolveWeaponCombatAnimatorRouter();
        SubscribeCombatModeController();
    }

    private void OnDisable()
    {
        UnsubscribeCombatModeController();
    }

    private void SubscribeCombatModeController()
    {
        UnsubscribeCombatModeController();
        combatModeController = PlayerCombatModeController.GetOrCreate();
        if (combatModeController != null)
            combatModeController.ModeChanged += HandleCombatModeChanged;
    }

    private void UnsubscribeCombatModeController()
    {
        if (combatModeController != null)
            combatModeController.ModeChanged -= HandleCombatModeChanged;

        combatModeController = null;
    }

    private void HandleCombatModeChanged(PlayerCombatModeState state, PlayerCombatModeReason reason)
    {
        if (IsWeaponCombatAnimatorRouterActive())
            return;

        if (state == PlayerCombatModeState.Combat)
            PlayCombatEquip(false);
        else
            PlayCombatEquip(true);
    }

    public void RefreshAnimatorReference()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>(true); // 애니메이터
    }

    private void ResolveWeaponCombatAnimatorRouter()
    {
        if (weaponCombatAnimatorRouter == null)
            weaponCombatAnimatorRouter = GetComponent<WeaponCombatAnimatorRouter>();
    }

    private bool IsWeaponCombatAnimatorRouterActive()
    {
        return useWeaponCombatAnimatorRouter && weaponCombatAnimatorRouter != null;
    }

    private void Update()
    {
        if (targetAnimator == null)
            return;

        if (playerController == null)
            return;

        ResolveWeaponCombatAnimatorRouter();
        ProcessPendingMeleeFullBodyRestore();

        if (playerController.IsKnockedDown) return;

        if (UpdateMeleeFullBodyAction())
            return;

        Vector2 targetMove = GetAnimatorMoveInput(); // 애니 이동값

        currentMoveBlend = Vector2.SmoothDamp(
            currentMoveBlend,
            targetMove,
            ref moveBlendVelocity,
            moveSmoothTime);

        targetAnimator.SetFloat(speedParameter, playerController.AnimationMoveAmount, speedDampTime, Time.deltaTime);
        targetAnimator.SetFloat(ExplorationLocomotionSpeedParameterHash,
            playerController.HasGreatswordEquipped ? PlayerMovement.GreatswordLocomotionSpeedMultiplier : 1f);
        targetAnimator.SetFloat(moveXParameter, currentMoveBlend.x);
        targetAnimator.SetFloat(moveYParameter, currentMoveBlend.y);
        targetAnimator.SetBool(groundedParameter, playerController.IsGrounded);
        targetAnimator.SetFloat(verticalVelocityParameter, playerController.VerticalVelocity);
        targetAnimator.SetBool(aimingParameter, playerController.IsCombatMoveMode || playerController.IsWeaponAimPoseActive);
        SetBoolIfPresent(combatModeParameter, playerController.IsMeleeCombatLocomotionMode);
        SetBoolIfPresent(guardingParameter, playerController.IsMeleeGuarding);
        if (useLegacyCombatBaseLayerStates)
            RefreshCombatLocomotionState();

        if (playerController.ConsumeJumpAnimationRequest())
        {
            if (playerController.IsMeleeCombatLocomotionMode
                && IsWeaponCombatAnimatorRouterActive()
                && weaponCombatAnimatorRouter.TryPlayCombatJump())
            {
                return;
            }

            if (playerController.IsMeleeCombatLocomotionMode && useLegacyCombatBaseLayerStates && combatJumpClip != null)
                PlayCombatJump();
            else
                targetAnimator.SetTrigger(jumpTriggerParameter); // 점프 trigger
        }

        if (useLegacyWeaponAimLayers)
        {
            RefreshFullBodyAimPose();
            RefreshWeaponAimPoseOverride();
            RefreshWeaponActionLayerWeight();
        }
    }

    private void LateUpdate()
    {
        if (useLegacyWeaponAimLayers)
            ApplyMeleeAimUpperBodyYawOffset();
    }

}
