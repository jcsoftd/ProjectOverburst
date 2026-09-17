using UnityEngine;
using System;
using System.Collections.Generic;

[DefaultExecutionOrder(360)]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerAnimation : MonoBehaviour // 플레이어 애니
{
    private const string BaseLayerName = "Base Layer";
    private const string MagicActionLayerName = "UpperBody_Magic";
    private const string FullBodyAimLayerName = "FullBody_Aim";
    private const string FullBodyAimStateName = "FullBodyAim";

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
    [SerializeField] private string recoverAnimationSpeedParameter = "RecoverAnimationSpeed";
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
    [SerializeField] private AnimationClip baseRecoverClip;
    [SerializeField] private string baseRecoverClipName = "MagicRecover";

    [Header("Weapon Action States")]
    [SerializeField] private bool restartWeaponActionStates = true;
    [SerializeField] private string weaponActionLayerName = MagicActionLayerName;
    [SerializeField] private string aimStateName = "MagicAim";
    [SerializeField] private string fireStateName = "MagicCast";
    [SerializeField] private string recoverStateName = "MagicRecover";
    [SerializeField] private float weaponActionLayerBlendSpeed = 18f;

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
    private AnimationClip activeRecoverClip; // 회복 액션 clip
    private AnimationClip forcedAimPoseClip; // 강제 포즈
    private float forcedAimPoseNormalizedTime; // 강제 시간
    private bool useForcedAimPoseTime; // 시간 고정
    private AnimationClip quickFireAimPoseClip; // QuickFire 포즈
    private float quickFireAimPoseUntil; // QuickFire 시간
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> weaponOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); // override 목록
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> meleeFullBodyRestoreOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); // 복구 목록
    private int weaponActionLayerIndex = -1; // 액션 layer
    private int magicActionLayerIndex = -1; // 마법 상체 layer
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
    private float magicActionLayerWeight; // 마법 layer weight
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

        if (UpdateMeleeFullBodyAction())
            return;

        Vector2 targetMove = GetAnimatorMoveInput(); // 애니 이동값

        currentMoveBlend = Vector2.SmoothDamp(
            currentMoveBlend,
            targetMove,
            ref moveBlendVelocity,
            moveSmoothTime);

        targetAnimator.SetFloat(speedParameter, playerController.AnimationMoveAmount, speedDampTime, Time.deltaTime);
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

    private Vector2 GetAnimatorMoveInput()
    {
        if (playerController.MoveInput.sqrMagnitude <= 0.001f)
            return Vector2.zero;

        if (!playerController.IsCombatMoveMode)
            return new Vector2(0f, Mathf.Clamp01(playerController.MoveInput.magnitude));

        Vector3 localMove = transform.InverseTransformDirection(playerController.MoveDirection); // 로컬 이동

        return Vector2.ClampMagnitude(new Vector2(localMove.x, localMove.z), 1f);
    }

    private void RefreshCombatLocomotionState()
    {
        if (!useCombatLocomotionStates || targetAnimator == null || playerController == null)
            return;

        if (!playerController.IsGrounded || targetAnimator.IsInTransition(0))
            return;

        string targetStateName = null;
        if (playerController.IsMeleeCombatLocomotionMode)
            targetStateName = playerController.IsMeleeGuarding ? combatGuardLocomotionStateName : combatLocomotionStateName;
        else if (IsCurrentBaseState(combatLocomotionStateName) || IsCurrentBaseState(combatGuardLocomotionStateName))
            targetStateName = normalLocomotionStateName;

        if (string.IsNullOrEmpty(targetStateName) || IsCurrentBaseState(targetStateName))
            return;

        if (!TryResolveBaseLayerStateHash(targetStateName, out int stateHash))
            return;

        float transitionDuration = ResolveCombatLocomotionTransitionDuration(targetStateName);
        float fixedTimeOffset = ResolveCombatLocomotionFixedTimeOffset(targetStateName);
        targetAnimator.CrossFadeInFixedTime(
            stateHash,
            transitionDuration,
            0,
            fixedTimeOffset);
    }

    private float ResolveCombatLocomotionTransitionDuration(string targetStateName)
    {
        if (!useCombatEquipToLocomotionBlendOnNextEnter)
            return Mathf.Max(0f, combatLocomotionTransitionDuration);

        if (!IsCombatLocomotionTargetState(targetStateName))
            return Mathf.Max(0f, combatLocomotionTransitionDuration);

        useCombatEquipToLocomotionBlendOnNextEnter = false;
        float transitionDuration = Mathf.Max(0f, combatEquipToLocomotionTransitionDuration);
        SchedulePendingMeleeFullBodyRestore(transitionDuration);
        return transitionDuration;
    }

    private float ResolveCombatLocomotionFixedTimeOffset(string targetStateName)
    {
        if (!useCombatLocomotionEntryOffsetOnNextEnter)
            return 0f;

        if (!IsCombatLocomotionTargetState(targetStateName))
            return 0f;

        useCombatLocomotionEntryOffsetOnNextEnter = false;
        return Mathf.Max(0f, combatLocomotionEntryFixedTimeOffset);
    }

    private bool IsCombatLocomotionTargetState(string stateName)
    {
        return string.Equals(stateName, combatLocomotionStateName, StringComparison.Ordinal)
            || string.Equals(stateName, combatGuardLocomotionStateName, StringComparison.Ordinal);
    }

    private bool IsCurrentBaseState(string stateName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(stateName))
            return false;

        AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.shortNameHash == Animator.StringToHash(stateName)
            || stateInfo.fullPathHash == Animator.StringToHash(BuildBaseLayerStatePath(stateName));
    }

    private string BuildBaseLayerStatePath(string stateName)
    {
        return BaseLayerName + "." + stateName;
    }

    private bool TryResolveBaseLayerStateHash(string stateName, out int stateHash)
    {
        stateHash = 0;
        if (targetAnimator == null || string.IsNullOrEmpty(stateName))
            return false;

        int fullPathHash = Animator.StringToHash(BuildBaseLayerStatePath(stateName));
        if (targetAnimator.HasState(0, fullPathHash))
        {
            stateHash = fullPathHash;
            return true;
        }

        int shortNameHash = Animator.StringToHash(stateName);
        if (targetAnimator.HasState(0, shortNameHash))
        {
            stateHash = shortNameHash;
            return true;
        }

        return false;
    }

    public void PlayWeaponFire(AnimationClip fireClip)
    {
        PlayWeaponFire(fireClip, 1f);
    }

    public void PlayWeaponFire(AnimationClip fireClip, float animationSpeed)
    {
        if (!useLegacyWeaponAimLayers)
            return;

        AnimationClip desiredClip = fireClip != null ? fireClip : GetBaseFireClip(); // 발사 clip
        PlayWeaponAction(
            desiredClip,
            IsBaseFireClip,
            ref activeFireClip,
            fireTriggerParameter,
            fireAnimationSpeedParameter,
            Mathf.Max(0.01f, animationSpeed),
            fireStateName);
    }

    public void PlayMeleeFullBodyFire(AnimationClip fireClip, float animationSpeed, float actionDuration)
    {
        PlayMeleeFullBodyFire(fireClip, animationSpeed, actionDuration, 0f);
    }

    public void PlayMeleeFullBodyFire(AnimationClip fireClip, float animationSpeed, float actionDuration, float fixedTransitionDuration)
    {
        AnimationClip desiredClip = fireClip != null ? fireClip : GetBaseFireClip(); // 근접 clip
        if (useLegacyWeaponAimLayers)
        {
            PlayWeaponAction(
                desiredClip,
                IsBaseFireClip,
                ref activeFireClip,
                fireTriggerParameter,
                fireAnimationSpeedParameter,
                1f,
                fireStateName);
        }

        if (IsWeaponCombatAnimatorRouterActive())
            weaponCombatAnimatorRouter.SuppressCombatLayerForLegacyAction(Mathf.Max(0.01f, actionDuration + fixedTransitionDuration));

        BeginMeleeFullBodyAction(desiredClip, animationSpeed, actionDuration, fixedTransitionDuration);
    }

    public bool PlayMeleeCombatAttack(
        int comboStepIndex,
        AnimationClip attackClip,
        float animationSpeed,
        float actionDuration,
        float transitionDuration,
        bool allowCombatEntry)
    {
        if (IsWeaponCombatAnimatorRouterActive())
        {
            return weaponCombatAnimatorRouter.TryPlayCombatAttack(
                comboStepIndex,
                attackClip,
                actionDuration,
                transitionDuration,
                allowCombatEntry);
        }

        PlayMeleeFullBodyFire(attackClip, animationSpeed, actionDuration, transitionDuration);
        return attackClip != null;
    }

    public void PlayEvadeFullBody(AnimationClip evadeClip, float actionDuration, float fixedTransitionDuration)
    {
        float duration = Mathf.Max(0.01f, actionDuration);
        if (playerController != null
            && playerController.IsMeleeCombatLocomotionMode
            && IsWeaponCombatAnimatorRouterActive()
            && weaponCombatAnimatorRouter.TryPlayCombatRoll(duration))
        {
            return;
        }

        if (evadeClip == null)
            return;

        float animationSpeed = Mathf.Max(0.01f, evadeClip.length / duration);
        BeginMeleeFullBodyAction(evadeClip, animationSpeed, duration, fixedTransitionDuration);
    }

    public void NotifyMeleeGuardBlockedHit()
    {
        if (IsWeaponCombatAnimatorRouterActive() && weaponCombatAnimatorRouter.TryPlayMeleeGuardBlock())
            return;

        AnimationClip clip = ShouldUseMovingGuardBlockClip() ? combatMovingBlockClip : combatBlockClip;
        if (clip == null)
            clip = combatBlockClip != null ? combatBlockClip : combatMovingBlockClip;

        if (clip == null)
            return;

        BeginMeleeFullBodyAction(clip, 1f, clip.length, combatBlockTransitionDuration);
    }

    public void NotifyCombatDamagedHit()
    {
        if (IsWeaponCombatAnimatorRouterActive() && weaponCombatAnimatorRouter.TryPlayCombatHit())
            return;

        AnimationClip clip = PickCombatHitClip();
        if (clip == null)
            return;

        BeginMeleeFullBodyAction(clip, 1f, clip.length, combatHitTransitionDuration);
    }

    private AnimationClip PickCombatHitClip()
    {
        if (combatHitClips == null || combatHitClips.Length == 0)
            return null;

        int availableCount = 0;
        for (int i = 0; i < combatHitClips.Length; i++)
        {
            if (combatHitClips[i] != null)
                availableCount++;
        }

        if (availableCount <= 0)
            return null;

        int selectedIndex = UnityEngine.Random.Range(0, availableCount);
        for (int i = 0; i < combatHitClips.Length; i++)
        {
            AnimationClip clip = combatHitClips[i];
            if (clip == null)
                continue;

            if (selectedIndex == 0)
                return clip;

            selectedIndex--;
        }

        return null;
    }

    private bool ShouldUseMovingGuardBlockClip()
    {
        return combatMovingBlockClip != null
            && playerController != null
            && playerController.MoveInput.sqrMagnitude > 0.001f;
    }

    private void PlayCombatJump()
    {
        if (combatJumpClip == null)
            return;

        BeginMeleeFullBodyAction(combatJumpClip, 1f, combatJumpClip.length, combatJumpTransitionDuration);
    }

    private void PlayCombatEquip(bool reverse)
    {
        if (combatEquipClip == null)
            return;

        AnimationClip clip = reverse && combatEquipExitClip != null ? combatEquipExitClip : combatEquipClip;
        float transitionDuration = reverse ? combatEquipExitTransitionDuration : combatEquipTransitionDuration;
        float actionDuration = reverse ? clip.length : Mathf.Max(0.01f, clip.length - Mathf.Max(0f, combatEquipEndTrimDuration));
        useCombatEquipToLocomotionBlendOnNextEnter = !reverse;
        useCombatLocomotionEntryOffsetOnNextEnter = !reverse && combatLocomotionEntryFixedTimeOffset > 0f;
        if (reverse)
        {
            float weaponHoldTime = Mathf.Max(0f, clip.length - Mathf.Max(0f, combatEquipExitWeaponBackLeadTime));
            playerEquipment?.CurrentWeaponPose?.BeginActivePose(weaponHoldTime);
        }

        BeginMeleeFullBodyAction(clip, 1f, actionDuration, transitionDuration, 0f, !reverse);
    }

    public void PlayWeaponRecover(AnimationClip recoverClip)
    {
        PlayWeaponRecover(recoverClip, 0f);
    }

    public void PlayWeaponRecover(AnimationClip recoverClip, float targetDuration)
    {
        if (!useLegacyWeaponAimLayers)
            return;

        AnimationClip desiredClip = recoverClip != null ? recoverClip : GetBaseRecoverClip(); // 회복 액션 clip
        float animationSpeed = CalculateRecoverAnimationSpeed(desiredClip, targetDuration); // 속도 보정
        PlayWeaponAction(
            desiredClip,
            IsBaseRecoverClip,
            ref activeRecoverClip,
            recoverTriggerParameter,
            recoverAnimationSpeedParameter,
            animationSpeed,
            recoverStateName);
    }

    public void BeginQuickFireAimPose(AnimationClip aimPoseClip, float holdTime)
    {
        if (!useLegacyWeaponAimLayers)
            return;

        quickFireAimPoseClip = aimPoseClip; // QuickFire 포즈
        quickFireAimPoseUntil = Mathf.Max(quickFireAimPoseUntil, Time.time + Mathf.Max(0f, holdTime)); // 유지 시간
        SetWeaponActionLayerWeight(1f);
    }

    public void SetForcedWeaponAimPose(AnimationClip aimPoseClip)
    {
        if (!useLegacyWeaponAimLayers)
            return;

        forcedAimPoseClip = aimPoseClip; // 강제 포즈
        useForcedAimPoseTime = false; // 시간 해제
        SetWeaponActionLayerWeight(1f);
    }

    public void SetForcedWeaponAimPose(AnimationClip aimPoseClip, float normalizedTime)
    {
        if (!useLegacyWeaponAimLayers)
            return;

        forcedAimPoseClip = aimPoseClip; // 강제 포즈
        forcedAimPoseNormalizedTime = Mathf.Clamp01(normalizedTime); // 고정 시간
        useForcedAimPoseTime = true; // 시간 사용
        SetWeaponActionLayerWeight(1f);
    }

    public void ClearForcedWeaponAimPose()
    {
        forcedAimPoseClip = null; // 강제 해제
        useForcedAimPoseTime = false; // 시간 해제
    }

    public void CancelWeaponRuntimeState()
    {
        weaponCombatAnimatorRouter?.CancelCombatAttack();
        forcedAimPoseClip = null; // 강제 해제
        useForcedAimPoseTime = false; // 시간 해제
        quickFireAimPoseClip = null; // QuickFire 해제
        quickFireAimPoseUntil = 0f; // 시간 해제
        weaponActionLayerHoldUntil = 0f;
        RestoreFullBodyAimPose();
        RestoreMeleeFullBodyOverrides();
        SetWeaponActionLayerWeight(0f);
        SetFullBodyAimLayerWeight(0f);
        ResetTriggerIfPresent(fireTriggerParameter);
        ResetTriggerIfPresent(recoverTriggerParameter);
    }

    private void RefreshWeaponAimPoseOverride()
    {
        if (!useWeaponAimPoseOverride || targetAnimator == null)
            return;

        if (ShouldUseCurrentAimPoseFullBody())
            return;

        EnsureWeaponOverrideController();

        if (weaponOverrideController == null)
            return;

        AnimationClip desiredClip = GetDesiredAimPoseClip(); // 목표 포즈

        if (desiredClip == null)
            return;

        ApplyClipOverride(IsBaseAimPoseClip, desiredClip, ref activeAimPoseClip);

        if (useForcedAimPoseTime && desiredClip == forcedAimPoseClip)
            PlayWeaponAimStateAt(forcedAimPoseNormalizedTime); // 시간 고정
    }

    private void PlayWeaponAction(
        AnimationClip clip,
        Func<AnimationClip, bool> baseClipMatcher,
        ref AnimationClip activeClip,
        string triggerParameter,
        string speedParameter,
        float animationSpeed,
        string stateName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(triggerParameter))
            return;

        if (useWeaponActionClipOverride)
        {
            EnsureWeaponOverrideController(); // override 준비

            if (clip != null)
                ApplyClipOverride(baseClipMatcher, clip, ref activeClip); // clip 교체
        }

        SetFloatIfPresent(speedParameter, Mathf.Max(0.01f, animationSpeed)); // 속도 파라미터
        HoldWeaponActionLayer(clip, animationSpeed);
        if (!RestartWeaponActionState(stateName))
            SetTriggerIfPresent(triggerParameter);
    }

    private void BeginMeleeFullBodyAction(AnimationClip actionClip, float animationSpeed, float actionDuration)
    {
        BeginMeleeFullBodyAction(actionClip, animationSpeed, actionDuration, 0f);
    }

    private void BeginMeleeFullBodyAction(AnimationClip actionClip, float animationSpeed, float actionDuration, float fixedTransitionDuration)
    {
        BeginMeleeFullBodyAction(actionClip, animationSpeed, actionDuration, fixedTransitionDuration, animationSpeed < 0f ? 1f : 0f);
    }

    private void BeginMeleeFullBodyAction(AnimationClip actionClip, float animationSpeed, float actionDuration, float fixedTransitionDuration, float normalizedStartTime)
    {
        BeginMeleeFullBodyAction(actionClip, animationSpeed, actionDuration, fixedTransitionDuration, normalizedStartTime, false);
    }

    private void BeginMeleeFullBodyAction(AnimationClip actionClip, float animationSpeed, float actionDuration, float fixedTransitionDuration, float normalizedStartTime, bool deferRestoreForCombatLocomotion)
    {
        if (targetAnimator == null || actionClip == null)
            return;

        RestoreFullBodyAimPose();
        RestoreMeleeFullBodyOverrides(); // 이전 복구
        EnsureWeaponOverrideController(); // override 준비

        if (weaponOverrideController == null)
            return;

        if (!ApplyMeleeFullBodyClipOverride(actionClip))
            return;

        bool playFullBodyActionInReverse = animationSpeed < -0.001f;
        animatorSpeedBeforeMeleeFullBody = targetAnimator.speed; // 속도 백업
        targetAnimator.speed = Mathf.Max(0.01f, animationSpeed); // 전신 속도
        isMeleeFullBodyActionActive = true; // 전신 활성
        deferMeleeFullBodyRestoreToCombatLocomotion = deferRestoreForCombatLocomotion;
        isMeleeFullBodyRestorePending = false;
        meleeFullBodyRestoreTime = 0f;
        meleeFullBodyActionEndTime = Time.time + Mathf.Max(0.01f, actionDuration); // 종료 시간
        SetWeaponActionLayerWeight(0f);
        SetFullBodyAimLayerWeight(0f);
        if (playFullBodyActionInReverse)
            targetAnimator.speed = animationSpeed;

        ResetMeleeAnimatorParametersOnStart();
        PlayBaseLayerState(meleeFullBodyStateName, fixedTransitionDuration);
        if (normalizedStartTime > 0.001f && TryResolveBaseLayerStateHash(meleeFullBodyStateName, out int meleeFullBodyStateHash))
            targetAnimator.Play(meleeFullBodyStateHash, 0, Mathf.Clamp01(normalizedStartTime));
    }

    private bool UpdateMeleeFullBodyAction()
    {
        if (!isMeleeFullBodyActionActive)
            return false;

        if (Time.time >= meleeFullBodyActionEndTime)
        {
            if (ShouldDeferMeleeFullBodyRestoreToCombatLocomotion())
            {
                BeginDeferredMeleeFullBodyRestore();
                return false;
            }

            RestoreMeleeFullBodyOverrides(); // 전신 복구
            return false;
        }

        SetMeleeFullBodyAnimatorParameters();
        return true;
    }

    private void SetMeleeFullBodyAnimatorParameters()
    {
        targetAnimator.SetFloat(speedParameter, 0f);
        targetAnimator.SetFloat(moveXParameter, 0f);
        targetAnimator.SetFloat(moveYParameter, 0f);
        targetAnimator.SetBool(groundedParameter, playerController.IsGrounded);
        targetAnimator.SetFloat(verticalVelocityParameter, playerController.VerticalVelocity);
        targetAnimator.SetBool(aimingParameter, false);
        SetWeaponActionLayerWeight(0f);
        SetFullBodyAimLayerWeight(0f);
    }

    private void ResetMeleeAnimatorParametersOnStart()
    {
        if (targetAnimator == null)
            return;

        currentMoveBlend = Vector2.zero; // 근접 1타 시작 안정화
        moveBlendVelocity = Vector2.zero;
        targetAnimator.SetFloat(speedParameter, 0f);
        targetAnimator.SetFloat(moveXParameter, 0f);
        targetAnimator.SetFloat(moveYParameter, 0f);
        targetAnimator.SetBool(aimingParameter, false);
        targetAnimator.Update(0f);
    }

    private bool ApplyMeleeFullBodyClipOverride(AnimationClip actionClip)
    {
        meleeFullBodyRestoreOverrides.Clear(); // 복구 목록 초기화
        weaponOverrideController.GetOverrides(meleeFullBodyRestoreOverrides); // 원본 저장
        weaponOverrides.Clear(); // 적용 목록 초기화

        bool replaced = false;
        for (int i = 0; i < meleeFullBodyRestoreOverrides.Count; i++)
        {
            AnimationClip sourceClip = meleeFullBodyRestoreOverrides[i].Key; // 원본 clip
            AnimationClip replacementClip = meleeFullBodyRestoreOverrides[i].Value; // 기존 override 유지
            if (IsMeleeFullBodyBaseClip(sourceClip))
            {
                replacementClip = actionClip; // 전신 clip
                replaced = true;
            }

            weaponOverrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(sourceClip, replacementClip));
        }

        if (!replaced)
        {
            meleeFullBodyRestoreOverrides.Clear();
            Debug.LogWarning($"PlayerAnimation could not find melee full-body base clip '{meleeFullBodyBaseClipName}'.", this);
            return false;
        }

        weaponOverrideController.ApplyOverrides(weaponOverrides);
        return true;
    }

    private bool ShouldDeferMeleeFullBodyRestoreToCombatLocomotion()
    {
        return deferMeleeFullBodyRestoreToCombatLocomotion
            && playerController != null
            && playerController.IsMeleeCombatLocomotionMode
            && meleeFullBodyRestoreOverrides.Count > 0;
    }

    private void BeginDeferredMeleeFullBodyRestore()
    {
        if (targetAnimator != null)
            targetAnimator.speed = animatorSpeedBeforeMeleeFullBody;

        isMeleeFullBodyActionActive = false;
        deferMeleeFullBodyRestoreToCombatLocomotion = false;
        isMeleeFullBodyRestorePending = true;
        meleeFullBodyRestoreTime = Time.time + Mathf.Max(0.05f, combatEquipToLocomotionTransitionDuration + 0.05f);
    }

    private void SchedulePendingMeleeFullBodyRestore(float transitionDuration)
    {
        if (!isMeleeFullBodyRestorePending)
            return;

        meleeFullBodyRestoreTime = Time.time + Mathf.Max(0f, transitionDuration) + 0.02f;
    }

    private void ProcessPendingMeleeFullBodyRestore()
    {
        if (!isMeleeFullBodyRestorePending)
            return;

        if (Time.time < meleeFullBodyRestoreTime)
            return;

        if (!CanCompletePendingMeleeFullBodyRestore())
            return;

        RestoreMeleeFullBodyOverrides();
    }

    private bool CanCompletePendingMeleeFullBodyRestore()
    {
        if (targetAnimator != null && targetAnimator.IsInTransition(0))
            return false;

        if (playerController == null || !playerController.IsMeleeCombatLocomotionMode)
            return true;

        return IsCurrentBaseState(combatLocomotionStateName)
            || IsCurrentBaseState(combatGuardLocomotionStateName);
    }

    private void RestoreMeleeFullBodyOverrides()
    {
        if (!isMeleeFullBodyActionActive && meleeFullBodyRestoreOverrides.Count == 0)
            return;

        if (weaponOverrideController != null && meleeFullBodyRestoreOverrides.Count > 0)
            weaponOverrideController.ApplyOverrides(meleeFullBodyRestoreOverrides); // clip 복구

        if (targetAnimator != null)
            targetAnimator.speed = animatorSpeedBeforeMeleeFullBody; // 속도 복구

        isMeleeFullBodyActionActive = false; // 전신 종료
        deferMeleeFullBodyRestoreToCombatLocomotion = false;
        isMeleeFullBodyRestorePending = false;
        meleeFullBodyRestoreTime = 0f;
        meleeFullBodyRestoreOverrides.Clear(); // 복구 목록 해제
    }

    private void RefreshFullBodyAimPose()
    {
        AnimationClip aimClip = GetFullBodyAimPoseClip();
        if (aimClip == null)
        {
            RestoreFullBodyAimPose();
            return;
        }

        if (isFullBodyAimPoseActive && activeFullBodyAimPoseClip == aimClip)
            return;

        ApplyFullBodyAimPose(aimClip);
    }

    private AnimationClip GetFullBodyAimPoseClip()
    {
        if (isMeleeFullBodyActionActive || targetAnimator == null || playerController == null || playerEquipment == null)
            return null;

        if (!ShouldUseCurrentAimPoseFullBody())
            return null;

        return playerEquipment.CurrentWeaponData.GetAimPoseClip();
    }

    private bool ShouldUseCurrentAimPoseFullBody()
    {
        if (playerController == null || playerEquipment == null || !playerController.IsWeaponAimPoseActive)
            return false;

        WeaponItemData weaponData = playerEquipment.CurrentWeaponData;
        return weaponData != null
            && weaponData.GetResolvedAimPoseBodyMode() == WeaponAimPoseBodyMode.FullBody
            && weaponData.GetAimPoseClip() != null;
    }

    private void ApplyFullBodyAimPose(AnimationClip aimClip)
    {
        if (aimClip == null)
            return;

        RestoreMeleeFullBodyOverrides();
        EnsureWeaponOverrideController();

        if (weaponOverrideController == null)
            return;

        ApplyClipOverride(IsBaseAimPoseClip, aimClip, ref activeAimPoseClip);
        activeFullBodyAimPoseClip = aimClip;
        isFullBodyAimPoseActive = true;
        PlayFullBodyAimStateAt(0f);
        SetFullBodyAimLayerWeight(1f);
    }

    private void RestoreFullBodyAimPose()
    {
        if (!isFullBodyAimPoseActive)
            return;

        isFullBodyAimPoseActive = false;
        activeFullBodyAimPoseClip = null;
        fullBodyAimPoseRestoreOverrides.Clear();
        SetFullBodyAimLayerWeight(0f);
        targetAnimator?.Update(0f);
    }

    private void PlayFullBodyAimStateAt(float normalizedTime)
    {
        if (targetAnimator == null)
            return;

        EnsureActionStateHashes();
        if (fullBodyAimLayerIndex < 0 || fullBodyAimStateHash == 0)
            return;

        int stateHash = fullBodyAimStateHash;
        if (!targetAnimator.HasState(fullBodyAimLayerIndex, stateHash))
        {
            stateHash = Animator.StringToHash(FullBodyAimStateName);
            if (!targetAnimator.HasState(fullBodyAimLayerIndex, stateHash))
                return;
        }

        targetAnimator.CrossFadeInFixedTime(stateHash, 0.1f, fullBodyAimLayerIndex, Mathf.Clamp01(normalizedTime));
        targetAnimator.Update(0f);
    }

    private void PlayBaseLayerState(string stateName)
    {
        PlayBaseLayerState(stateName, 0f);
    }

    private void PlayBaseLayerState(string stateName, float fixedTransitionDuration)
    {
        if (!TryResolveBaseLayerStateHash(stateName, out int stateHash))
            return;

        if (fixedTransitionDuration > 0f)
            targetAnimator.CrossFadeInFixedTime(stateHash, fixedTransitionDuration, 0, 0f);
        else
            targetAnimator.Play(stateHash, 0, 0f);

        targetAnimator.Update(0f);
    }

    private void PlayWeaponAimStateAt(float normalizedTime)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(aimStateName))
            return;

        EnsureActionStateHashes();
        int layerIndex = weaponActionLayerIndex; // 액션 layer
        if (layerIndex < 0)
            return;

        int stateHash = Animator.StringToHash(BuildActionStatePath(aimStateName)); // 전체 경로
        if (!targetAnimator.HasState(layerIndex, stateHash))
        {
            stateHash = Animator.StringToHash(aimStateName); // 짧은 이름
            if (!targetAnimator.HasState(layerIndex, stateHash))
                return;
        }

        targetAnimator.Play(stateHash, layerIndex, Mathf.Clamp01(normalizedTime));
        targetAnimator.Update(0f);
    }

    private void RefreshWeaponActionLayerWeight()
    {
        EnsureActionStateHashes();

        WeaponUpperBodyAimChannel targetChannel = ShouldUseWeaponActionLayer()
            ? GetCurrentUpperBodyAimChannel()
            : WeaponUpperBodyAimChannel.None;
        float step = Mathf.Max(0f, weaponActionLayerBlendSpeed) * Time.deltaTime;

        magicActionLayerWeight = MoveLayerWeight(magicActionLayerIndex, magicActionLayerWeight, targetChannel == WeaponUpperBodyAimChannel.Magic ? 1f : 0f, step);

        weaponActionLayerWeight = GetCurrentActionLayerWeight(targetChannel);
    }

    private bool ShouldUseWeaponActionLayer()
    {
        if (isMeleeFullBodyActionActive)
            return false;

        if (isFullBodyAimPoseActive)
            return false;

        if (GetCurrentUpperBodyAimChannel() == WeaponUpperBodyAimChannel.None)
            return false;

        if (playerController != null && playerController.IsWeaponAimPoseActive)
            return true;

        if (forcedAimPoseClip != null)
            return true;

        if (Time.time < quickFireAimPoseUntil)
            return true;

        return Time.time < weaponActionLayerHoldUntil;
    }

    private void ApplyMeleeAimUpperBodyYawOffset()
    {
        float yawOffset = GetCurrentMeleeAimUpperBodyYawOffset();
        if (!useMeleeAimUpperBodyYawOffset || Mathf.Approximately(yawOffset, 0f))
            return;

        if (targetAnimator == null || !targetAnimator.isHuman || playerController == null)
            return;

        if (!playerController.IsMeleeCombatStance || playerController.IsMeleeGuarding || isMeleeFullBodyActionActive)
            return;

        if (playerEquipment != null && !playerEquipment.CanCurrentWeaponUseMeleeCombatStance)
            return;

        Transform upperBody = targetAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (upperBody == null)
            upperBody = targetAnimator.GetBoneTransform(HumanBodyBones.Chest);

        if (upperBody == null)
            upperBody = targetAnimator.GetBoneTransform(HumanBodyBones.Spine);

        if (upperBody == null)
            return;

        upperBody.rotation = Quaternion.AngleAxis(yawOffset, transform.up) * upperBody.rotation;
    }

    private float GetCurrentMeleeAimUpperBodyYawOffset()
    {
        if (playerEquipment == null || !playerEquipment.HasCurrentWeapon)
            return meleeAimUpperBodyYawOffset;

        return playerEquipment.CurrentMeleeAimUpperBodyYawOffset;
    }

    private void HoldWeaponActionLayer(AnimationClip clip, float animationSpeed)
    {
        float duration = clip != null ? clip.length / Mathf.Max(0.01f, animationSpeed) : 0.25f;
        weaponActionLayerHoldUntil = Mathf.Max(weaponActionLayerHoldUntil, Time.time + Mathf.Max(0.05f, duration));
        SetWeaponActionLayerWeight(1f);
    }

    private void SetWeaponActionLayerWeight(float weight)
    {
        EnsureActionStateHashes();

        if (targetAnimator == null)
            return;

        weaponActionLayerWeight = Mathf.Clamp01(weight);
        WeaponUpperBodyAimChannel targetChannel = weaponActionLayerWeight > 0f
            ? GetCurrentUpperBodyAimChannel()
            : WeaponUpperBodyAimChannel.None;

        magicActionLayerWeight = SetLayerWeightImmediate(magicActionLayerIndex, targetChannel == WeaponUpperBodyAimChannel.Magic ? weaponActionLayerWeight : 0f);
    }

    private void SetFullBodyAimLayerWeight(float weight)
    {
        EnsureActionStateHashes();

        fullBodyAimLayerWeight = Mathf.Clamp01(weight);
        if (targetAnimator != null && fullBodyAimLayerIndex >= 0)
            targetAnimator.SetLayerWeight(fullBodyAimLayerIndex, fullBodyAimLayerWeight);
    }

    private float MoveLayerWeight(int layerIndex, float currentWeight, float targetWeight, float step)
    {
        float nextWeight = Mathf.MoveTowards(currentWeight, Mathf.Clamp01(targetWeight), step);
        if (targetAnimator != null && layerIndex >= 0)
            targetAnimator.SetLayerWeight(layerIndex, nextWeight);

        return nextWeight;
    }

    private float SetLayerWeightImmediate(int layerIndex, float weight)
    {
        float clampedWeight = Mathf.Clamp01(weight);
        if (targetAnimator != null && layerIndex >= 0)
            targetAnimator.SetLayerWeight(layerIndex, clampedWeight);

        return clampedWeight;
    }

    private float GetCurrentActionLayerWeight(WeaponUpperBodyAimChannel channel)
    {
        switch (channel)
        {
            case WeaponUpperBodyAimChannel.Magic:
                return magicActionLayerWeight;
            default:
                return 0f;
        }
    }

    private void ApplyClipOverride(Func<AnimationClip, bool> clipMatcher, AnimationClip desiredClip, ref AnimationClip activeClip)
    {
        if (weaponOverrideController == null || clipMatcher == null || desiredClip == null)
            return;

        if (desiredClip == activeClip)
            return;

        weaponOverrideController.GetOverrides(weaponOverrides); // 현재 목록

        bool replaced = false; // 교체 여부
        for (int i = 0; i < weaponOverrides.Count; i++)
        {
            AnimationClip sourceClip = weaponOverrides[i].Key; // 원본 clip
            if (!clipMatcher(sourceClip))
                continue;

            weaponOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(sourceClip, desiredClip);
            replaced = true; // 교체 완료
        }

        if (!replaced)
            return;

        weaponOverrideController.ApplyOverrides(weaponOverrides);
        activeClip = desiredClip; // 활성 clip
        targetAnimator.Update(0f);
    }

    private float CalculateRecoverAnimationSpeed(AnimationClip recoverClip, float targetDuration)
    {
        if (recoverClip == null || targetDuration <= 0f)
            return 1f;

        return Mathf.Max(0.01f, recoverClip.length / targetDuration);
    }

    private bool RestartWeaponActionState(string stateName)
    {
        if (!restartWeaponActionStates || string.IsNullOrEmpty(stateName) || targetAnimator == null)
            return false;

        EnsureActionStateHashes();

        if (weaponActionLayerIndex < 0)
            return false;

        int stateHash = stateName == fireStateName ? fireStateHash : recoverStateHash; // 캐시 hash
        if (stateHash == 0)
            return false;

        if (!targetAnimator.HasState(weaponActionLayerIndex, stateHash))
        {
            stateHash = Animator.StringToHash(stateName); // 짧은 이름
            if (!targetAnimator.HasState(weaponActionLayerIndex, stateHash))
                return false;
        }

        targetAnimator.Play(stateHash, weaponActionLayerIndex, 0f);
        targetAnimator.Update(0f);
        return true;
    }

    private void EnsureActionStateHashes()
    {
        ResolveActionLayerIndices();
        WeaponUpperBodyAimChannel resolvedChannel = GetCurrentUpperBodyAimChannel();
        int resolvedLayerIndex = ResolveWeaponActionLayerIndex(resolvedChannel);
        string resolvedLayerName = GetActionLayerName(resolvedChannel);

        if (actionStateHashesInitialized
            && weaponActionLayerIndex == resolvedLayerIndex
            && activeWeaponActionChannel == resolvedChannel
            && activeWeaponActionLayerName == resolvedLayerName)
            return;

        weaponActionLayerIndex = resolvedLayerIndex;
        activeWeaponActionChannel = resolvedChannel;
        activeWeaponActionLayerName = resolvedLayerIndex >= 0 ? resolvedLayerName : null;

        fireStateHash = !string.IsNullOrEmpty(fireStateName) ? Animator.StringToHash(BuildActionStatePath(fireStateName)) : 0;
        recoverStateHash = !string.IsNullOrEmpty(recoverStateName) ? Animator.StringToHash(BuildActionStatePath(recoverStateName)) : 0;
        fullBodyAimStateHash = Animator.StringToHash(FullBodyAimLayerName + "." + FullBodyAimStateName);
        actionStateHashesInitialized = true; // hash 완료
    }

    private void ResolveActionLayerIndices()
    {
        if (targetAnimator == null)
            return;

        magicActionLayerIndex = ResolveLayerIndexWithFallback(MagicActionLayerName, weaponActionLayerName);
        fullBodyAimLayerIndex = targetAnimator.GetLayerIndex(FullBodyAimLayerName);
    }

    private int ResolveWeaponActionLayerIndex(WeaponUpperBodyAimChannel channel)
    {
        switch (channel)
        {
            case WeaponUpperBodyAimChannel.Magic:
                return magicActionLayerIndex;
            default:
                return -1;
        }
    }

    private int ResolveLayerIndexWithFallback(params string[] layerNames)
    {
        if (targetAnimator == null || layerNames == null)
            return -1;

        for (int i = 0; i < layerNames.Length; i++)
        {
            string layerName = layerNames[i];
            if (string.IsNullOrEmpty(layerName))
                continue;

            int layerIndex = targetAnimator.GetLayerIndex(layerName);
            if (layerIndex >= 0)
                return layerIndex;
        }

        return -1;
    }

    private WeaponUpperBodyAimChannel GetCurrentUpperBodyAimChannel()
    {
        WeaponItemData weaponData = playerEquipment != null ? playerEquipment.CurrentWeaponData : null;
        if (weaponData == null)
            return WeaponUpperBodyAimChannel.None;

        return weaponData.GetResolvedUpperBodyAimChannel();
    }

    private string GetActionLayerName(WeaponUpperBodyAimChannel channel)
    {
        switch (channel)
        {
            case WeaponUpperBodyAimChannel.Magic:
                return magicActionLayerIndex >= 0 ? MagicActionLayerName : weaponActionLayerName;
            default:
                return null;
        }
    }

    private string BuildActionStatePath(string stateName)
    {
        string layerName = string.IsNullOrEmpty(activeWeaponActionLayerName)
            ? weaponActionLayerName
            : activeWeaponActionLayerName;

        if (string.IsNullOrEmpty(layerName))
            return stateName;

        return layerName + "." + stateName;
    }

    private void SetTriggerIfPresent(string triggerParameter)
    {
        if (!HasParameter(triggerParameter, AnimatorControllerParameterType.Trigger))
            return;

        targetAnimator.ResetTrigger(triggerParameter);
        targetAnimator.SetTrigger(triggerParameter);
    }

    private void ResetTriggerIfPresent(string triggerParameter)
    {
        if (!HasParameter(triggerParameter, AnimatorControllerParameterType.Trigger))
            return;

        targetAnimator.ResetTrigger(triggerParameter);
    }

    private void SetFloatIfPresent(string floatParameter, float value)
    {
        if (!HasParameter(floatParameter, AnimatorControllerParameterType.Float))
            return;

        targetAnimator.SetFloat(floatParameter, value);
    }

    private void SetBoolIfPresent(string boolParameter, bool value)
    {
        if (!HasParameter(boolParameter, AnimatorControllerParameterType.Bool))
            return;

        targetAnimator.SetBool(boolParameter, value);
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(parameterName))
            return false;

        AnimatorControllerParameter[] parameters = targetAnimator.parameters; // 파라미터 목록
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == parameterType && parameter.name == parameterName)
                return true;
        }

        return false;
    }

    private void EnsureWeaponOverrideController()
    {
        if (weaponOverrideController != null)
        {
            if (targetAnimator.runtimeAnimatorController != weaponOverrideController)
                targetAnimator.runtimeAnimatorController = weaponOverrideController;

            return;
        }

        RuntimeAnimatorController currentController = targetAnimator.runtimeAnimatorController; // 현재 controller
        if (currentController == null)
            return;

        AnimatorOverrideController currentOverride = currentController as AnimatorOverrideController; // 기존 override
        baseAnimatorController = currentOverride != null ? currentOverride.runtimeAnimatorController : currentController; // 원본 controller

        if (baseAnimatorController == null)
            return;

        weaponOverrideController = new AnimatorOverrideController(baseAnimatorController); // 무기 override
        weaponOverrideController.name = baseAnimatorController.name + "_WeaponClipOverride"; // 식별 이름
        targetAnimator.runtimeAnimatorController = weaponOverrideController; // 적용
    }

    private AnimationClip GetDesiredAimPoseClip()
    {
        if (forcedAimPoseClip != null)
            return forcedAimPoseClip;

        if (Time.time < quickFireAimPoseUntil && quickFireAimPoseClip != null)
            return quickFireAimPoseClip;

        if (playerController != null && !playerController.IsWeaponAimPoseActive)
            return GetBaseAimPoseClip();

        WeaponItemData weaponData = playerEquipment != null ? playerEquipment.CurrentWeaponData : null; // 현재 무기
        AnimationClip weaponClip = weaponData != null ? weaponData.GetAimPoseClip() : null; // 무기 포즈

        if (weaponClip != null)
            return weaponClip;

        return GetBaseAimPoseClip();
    }

    private AnimationClip GetBaseAimPoseClip()
    {
        if (baseAimClip != null)
            return baseAimClip;

        return FindControllerClip(baseAimClipName);
    }

    private AnimationClip GetBaseFireClip()
    {
        if (baseFireClip != null)
            return baseFireClip;

        return FindControllerClip(baseFireClipName);
    }

    private AnimationClip GetBaseRecoverClip()
    {
        if (baseRecoverClip != null)
            return baseRecoverClip;

        return FindControllerClip(baseRecoverClipName);
    }

    private AnimationClip FindControllerClip(string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
            return null;

        RuntimeAnimatorController controller = baseAnimatorController != null ? baseAnimatorController : targetAnimator.runtimeAnimatorController; // 검색 대상

        if (controller == null)
            return null;

        AnimationClip[] clips = controller.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null && clip.name == clipName)
                return clip;
        }

        return null;
    }

    private bool IsBaseAimPoseClip(AnimationClip clip)
    {
        if (clip == null)
            return false;

        if (baseAimClip != null && clip == baseAimClip)
            return true;

        return !string.IsNullOrEmpty(baseAimClipName) && clip.name == baseAimClipName;
    }

    private bool IsMeleeFullBodyBaseClip(AnimationClip clip)
    {
        if (clip == null)
            return false;

        if (meleeFullBodyBaseClip != null && clip == meleeFullBodyBaseClip)
            return true;

        if (!string.IsNullOrEmpty(meleeFullBodyBaseClipName)
            && string.Equals(clip.name, meleeFullBodyBaseClipName, StringComparison.Ordinal))
            return true;

        return !string.IsNullOrEmpty(meleeFullBodyStateName)
            && string.Equals(clip.name, meleeFullBodyStateName, StringComparison.Ordinal);
    }

    private bool IsBaseFireClip(AnimationClip clip)
    {
        if (clip == null)
            return false;

        if (baseFireClip != null && clip == baseFireClip)
            return true;

        return !string.IsNullOrEmpty(baseFireClipName) && clip.name == baseFireClipName;
    }

    private bool IsBaseRecoverClip(AnimationClip clip)
    {
        if (clip == null)
            return false;

        if (baseRecoverClip != null && clip == baseRecoverClip)
            return true;

        return !string.IsNullOrEmpty(baseRecoverClipName) && clip.name == baseRecoverClipName;
    }
}
