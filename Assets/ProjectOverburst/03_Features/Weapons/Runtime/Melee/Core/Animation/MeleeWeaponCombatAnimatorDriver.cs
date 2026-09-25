using UnityEngine;

[DefaultExecutionOrder(371)]
[DisallowMultipleComponent]
public class MeleeWeaponCombatAnimatorDriver : MonoBehaviour, IWeaponCombatAnimatorDriver
{
    private const float DefaultGuardLocomotionEnterTransitionDuration = 0.16f;
    private const float DefaultGuardLocomotionExitTransitionDuration = 0.12f;
    private const float DefaultGuardLocomotionStartOffsetSeconds = 0.05f;
    private static readonly int LocomotionSpeedParameterHash = Animator.StringToHash("Melee_LocomotionSpeed");

    private enum DriverAction
    {
        None,
        Equip,
        Unequip,
        Jump,
        Hit,
        Block,
        Roll,
        Attack
    }

    [Header("References")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private WeaponCombatAnimationProfile fallbackProfile;

    [Header("States")]
    [SerializeField] private string layerName = "Combat_MeleeWeapon";
    [SerializeField] private string transitionLowerLayerName = "Combat_MeleeWeapon_TransitionLower";
    [SerializeField] private string emptyStateName = "Melee_Empty";
    [SerializeField] private string equipStateName = "Melee_Equip";
    [SerializeField] private string locomotionStateName = "Melee_Locomotion";
    [SerializeField] private string guardLocomotionStateName = "Melee_GuardLocomotion";
    [SerializeField] private string unequipStateName = "Melee_Unequip";
    [SerializeField] private string blockStateName = "Melee_Block";
    [SerializeField] private string movingBlockStateName = "Melee_MovingBlock";
    [SerializeField] private string hitStateNamePrefix = "Melee_Hit";
    [SerializeField] private string jumpStateName = "Melee_Jump";
    [SerializeField] private string rollStateName = "Melee_Roll";
    [SerializeField] private string attackStateName = "Melee_Attack";
    [SerializeField] private string transitionLowerEmptyStateName = "Melee_TransitionLower_Empty";
    [SerializeField] private string transitionLowerLocomotionStateName = "Melee_TransitionLower_Locomotion";
    [SerializeField] private string actionSpeedParameterName = "Melee_ActionSpeed";

    private AnimationClip equipClip;
    private AnimationClip unequipClip;
    private AnimationClip blockClip;
    private AnimationClip movingBlockClip;
    private AnimationClip jumpClip;
    private AnimationClip rollClip;
    private AnimationClip[] hitClips = System.Array.Empty<AnimationClip>();
    private AnimationClip attackTemplateClip;

    [Header("Timings")]
    [SerializeField] private float layerFadeInDuration = 0.08f;
    [SerializeField] private float layerFadeOutDuration = 0.12f;
    [SerializeField] private float equipToLocomotionTransitionDuration = 0.12f;
    [SerializeField] private float locomotionToGuardTransitionDuration = 0.08f;
    [SerializeField] private float guardLocomotionEnterTransitionDuration = DefaultGuardLocomotionEnterTransitionDuration;
    [SerializeField] private float guardLocomotionExitTransitionDuration = DefaultGuardLocomotionExitTransitionDuration;
    [SerializeField] private float guardLocomotionStartOffsetSeconds = DefaultGuardLocomotionStartOffsetSeconds;
    [SerializeField] private float actionTransitionDuration = 0.06f;
    [SerializeField] private float movingTransitionEndBlendDuration = 0.18f;
    [SerializeField] private float unequipWeaponBackLeadTime = 0.3f;
    [SerializeField] private float legacyActionSuppressionFadeDuration = 0.04f;
    [SerializeField] private float transitionLowerLayerFadeDuration = 0.05f;
    [SerializeField] private float transitionLowerMoveInputThreshold = 0.05f;
    [SerializeField] private bool useTransitionLowerBodyWhileGuarding;

    [Header("Playback")]
    [SerializeField] private float equipAnimationSpeedMultiplier = 1.3f;
    [SerializeField] private float unequipAnimationSpeedMultiplier = 1.5f;
    [SerializeField] private float unequipAnimationStartOffsetSeconds = 0.1f;

    private int layerIndex = -1;
    private int transitionLowerLayerIndex = -1;
    private float targetLayerWeight;
    private float targetTransitionLowerLayerWeight;
    private DriverAction activeAction;
    private float activeActionEndTime;
    private bool combatRequested;
    private bool legacySuppressed;
    private float legacySuppressedUntil;
    private bool emptyStateAppliedAfterFade;
    private bool transitionLowerEmptyStateAppliedAfterFade = true;
    private float layerFadeOutDurationOverride = -1f;
    private float transitionLowerFadeOutDurationOverride = -1f;
    private bool profileApplied;
    private WeaponCombatAnimationProfile activeProfile;
    private RuntimeAnimatorController baseAnimatorController;
    private AnimatorOverrideController runtimeOverrideController;
    private WeaponCombatAnimationProfile runtimeOverrideProfile;

    public bool IsAvailable
    {
        get
        {
            ResolveLayerIndex();
            return targetAnimator != null && layerIndex >= 0;
        }
    }

    public bool CanApplyStationaryFootIk
    {
        get
        {
            ResolveLayerIndex();
            if (targetAnimator == null
                || layerIndex < 0
                || !combatRequested
                || legacySuppressed
                || activeAction != DriverAction.None
                || targetAnimator.IsInTransition(layerIndex))
            {
                return false;
            }

            return true;
        }
    }

    public void Initialize(Animator animator, PlayerMovement movement, PlayerEquipment equipment)
    {
        if (targetAnimator == null)
            targetAnimator = animator;

        if (playerMovement == null)
            playerMovement = movement;

        if (playerEquipment == null)
            playerEquipment = equipment;

        CaptureBaseAnimatorController();
        ApplyCurrentProfile();
        ResolveLayerIndex();
    }

    private void Awake()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>(true);

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        CaptureBaseAnimatorController();
        ApplyCurrentProfile();
        ResolveLayerIndex();
    }

    public void EnterCombat(PlayerCombatModeReason reason)
    {
        if (!IsAvailable)
            return;

        ApplyCurrentProfile();
        bool keepStartedAttack = activeAction == DriverAction.Attack;
        combatRequested = true;
        legacySuppressed = false;
        emptyStateAppliedAfterFade = false;
        layerFadeOutDurationOverride = -1f;
        transitionLowerFadeOutDurationOverride = -1f;
        targetLayerWeight = 1f;
        if (keepStartedAttack)
            return; // 공격 진입이 이미 성공했으면 Equip으로 덮어쓰지 않음

        float equipLength = GetScaledClipLength(equipClip, equipAnimationSpeedMultiplier);
        PlayActionState(equipStateName, DriverAction.Equip, equipLength, actionTransitionDuration);
        playerEquipment?.CurrentWeaponPose?.BeginActivePose(equipLength + 0.1f);
    }

    public void ExitCombat(PlayerCombatModeReason reason)
    {
        if (!IsAvailable)
            return;

        combatRequested = false;
        legacySuppressed = false;
        emptyStateAppliedAfterFade = false;
        layerFadeOutDurationOverride = -1f;
        transitionLowerFadeOutDurationOverride = -1f;
        targetLayerWeight = 1f;

        float unequipLength = GetScaledClipLength(
            unequipClip,
            unequipAnimationSpeedMultiplier,
            unequipAnimationStartOffsetSeconds);
        float weaponHoldTime = Mathf.Max(0f, unequipLength - Mathf.Max(0f, unequipWeaponBackLeadTime));
        playerEquipment?.CurrentWeaponPose?.BeginActivePose(weaponHoldTime);
        PlayActionState(
            unequipStateName,
            DriverAction.Unequip,
            unequipLength,
            actionTransitionDuration,
            GetClipStartOffsetNormalized(unequipClip, unequipAnimationStartOffsetSeconds));
    }

    public void Tick(float deltaTime)
    {
        if (!IsAvailable)
            return;

        ApplyCurrentProfile();
        UpdateLegacySuppression();
        UpdateActionState();
        UpdateLocomotionState();
        UpdateTransitionLowerBodyLayer(deltaTime);
        UpdateLayerWeight(deltaTime);
        ApplyEmptyStateAfterFadeOut();
    }

    public bool TryPlayJump()
    {
        if (!CanPlayInterruptibleCombatAction())
            return false;

        PlayActionState(jumpStateName, DriverAction.Jump, GetClipLength(jumpClip), actionTransitionDuration);
        return true;
    }

    public bool TryPlayHit()
    {
        if (!CanPlayInterruptibleCombatAction())
            return false;

        int hitIndex = PickHitIndex();
        if (hitIndex < 0)
            return false;

        string stateName = hitStateNamePrefix + (hitIndex + 1).ToString("00");
        PlayActionState(stateName, DriverAction.Hit, GetClipLength(hitClips[hitIndex]), actionTransitionDuration);
        return true;
    }

    public bool TryPlayRoll(float actionDuration)
    {
        if (!CanPlayCombatAction())
            return false;

        if (!HasState(rollStateName))
            return false;

        float duration = Mathf.Max(0.01f, actionDuration);
        SetActionSpeedForClip(rollClip, duration);
        PlayActionState(rollStateName, DriverAction.Roll, duration, actionTransitionDuration);
        return true;
    }

    public bool TryPlayGuardBlock()
    {
        if (!combatRequested
            || legacySuppressed
            || activeAction == DriverAction.Unequip
            || activeAction == DriverAction.Roll
            || activeAction == DriverAction.Attack)
            return false;

        bool moving = playerMovement != null && playerMovement.MoveInput.sqrMagnitude > 0.001f;
        string stateName = moving && HasState(movingBlockStateName) ? movingBlockStateName : blockStateName;
        AnimationClip clip = moving && movingBlockClip != null ? movingBlockClip : blockClip;
        if (!HasState(stateName))
            return false;

        PlayActionState(stateName, DriverAction.Block, GetClipLength(clip), actionTransitionDuration);
        return true;
    }

    public bool TryPlayAttack(
        int stepIndex,
        AnimationClip expectedClip,
        float actionDuration,
        float transitionDuration,
        bool allowCombatEntry)
    {
        ApplyCurrentProfile();
        if (!CanPlayAttack(allowCombatEntry) || stepIndex < 0 || expectedClip == null)
        {
            return false;
        }

        if (!HasState(attackStateName) || !ApplyAttackClip(expectedClip))
            return false;

        if (!combatRequested)
            PrepareCombatLayerForAttackEntry();

        float duration = Mathf.Max(0.01f, actionDuration);
        SetActionSpeedForClip(expectedClip, duration);
        PlayActionState(
            attackStateName,
            DriverAction.Attack,
            duration,
            Mathf.Max(0f, transitionDuration));
        return true;
    }

    public void CancelAttack()
    {
        if (activeAction != DriverAction.Attack)
            return;

        activeAction = DriverAction.None;
        activeActionEndTime = 0f;
        targetTransitionLowerLayerWeight = 0f;
        if (combatRequested && !legacySuppressed)
            PlayLocomotionByGuardState(actionTransitionDuration);
    }

    public void SuppressForLegacyFullBodyAction(float duration)
    {
        if (!IsAvailable)
            return;

        legacySuppressed = true;
        legacySuppressedUntil = Time.time + Mathf.Max(0.01f, duration);
        targetLayerWeight = 0f;
        targetTransitionLowerLayerWeight = 0f;
    }

    public void ForceResetLayer()
    {
        ResolveLayerIndex();
        combatRequested = false;
        legacySuppressed = false;
        activeAction = DriverAction.None;
        activeActionEndTime = 0f;
        targetLayerWeight = 0f;
        targetTransitionLowerLayerWeight = 0f;
        layerFadeOutDurationOverride = -1f;
        transitionLowerFadeOutDurationOverride = -1f;

        if (targetAnimator != null && layerIndex >= 0)
            targetAnimator.SetLayerWeight(layerIndex, 0f);

        if (targetAnimator != null && transitionLowerLayerIndex >= 0)
            targetAnimator.SetLayerWeight(transitionLowerLayerIndex, 0f);
    }

    private void ApplyCurrentProfile()
    {
        WeaponCombatAnimationProfile profile = ResolveProfile();
        if (profile == null)
            return;

        ApplyAnimatorOverride(profile);
        if (profileApplied && activeProfile == profile)
            return;

        activeProfile = profile;
        profileApplied = true;
        layerName = profile.animatorLayerName;
        transitionLowerLayerName = profile.transitionLowerLayerName;
        emptyStateName = profile.emptyStateName;
        equipStateName = profile.equipStateName;
        locomotionStateName = profile.locomotionStateName;
        guardLocomotionStateName = profile.guardLocomotionStateName;
        unequipStateName = profile.unequipStateName;
        blockStateName = profile.blockStateName;
        movingBlockStateName = profile.movingBlockStateName;
        hitStateNamePrefix = profile.hitStateNamePrefix;
        jumpStateName = profile.jumpStateName;
        rollStateName = profile.rollStateName;
        attackStateName = profile.attackStateName;
        attackTemplateClip = profile.attackTemplateClip;
        transitionLowerEmptyStateName = profile.transitionLowerEmptyStateName;
        transitionLowerLocomotionStateName = profile.transitionLowerLocomotionStateName;
        actionSpeedParameterName = profile.actionSpeedParameterName;
        equipClip = profile.equipClip;
        unequipClip = profile.unequipClip;
        blockClip = profile.blockClip;
        movingBlockClip = profile.movingBlockClip;
        hitClips = profile.hitClips;
        jumpClip = profile.jumpClip;
        rollClip = profile.rollClip;
        layerFadeInDuration = profile.layerFadeInDuration;
        layerFadeOutDuration = profile.layerFadeOutDuration;
        equipToLocomotionTransitionDuration = profile.equipToLocomotionTransitionDuration;
        locomotionToGuardTransitionDuration = profile.locomotionToGuardTransitionDuration;
        guardLocomotionEnterTransitionDuration = ResolvePositiveOrDefault(
            profile.guardLocomotionEnterTransitionDuration,
            DefaultGuardLocomotionEnterTransitionDuration);
        guardLocomotionExitTransitionDuration = ResolvePositiveOrDefault(
            profile.guardLocomotionExitTransitionDuration,
            DefaultGuardLocomotionExitTransitionDuration);
        guardLocomotionStartOffsetSeconds = ResolvePositiveOrDefault(
            profile.guardLocomotionStartOffsetSeconds,
            DefaultGuardLocomotionStartOffsetSeconds);
        actionTransitionDuration = profile.actionTransitionDuration;
        movingTransitionEndBlendDuration = profile.movingTransitionEndBlendDuration;
        unequipWeaponBackLeadTime = profile.unequipWeaponBackLeadTime;
        legacyActionSuppressionFadeDuration = profile.legacyActionSuppressionFadeDuration;
        transitionLowerLayerFadeDuration = profile.transitionLowerLayerFadeDuration;
        transitionLowerMoveInputThreshold = profile.transitionLowerMoveInputThreshold;
        useTransitionLowerBodyWhileGuarding = profile.useTransitionLowerBodyWhileGuarding; // 대검 가드 하체 합성
        equipAnimationSpeedMultiplier = profile.equipAnimationSpeedMultiplier;
        unequipAnimationSpeedMultiplier = profile.unequipAnimationSpeedMultiplier;
        unequipAnimationStartOffsetSeconds = profile.unequipAnimationStartOffsetSeconds;
        SetLocomotionSpeedForProfile(profile);
        layerIndex = -1;
        transitionLowerLayerIndex = -1;
    }

    private void SetLocomotionSpeedForProfile(WeaponCombatAnimationProfile profile)
    {
        if (targetAnimator == null)
            return;

        foreach (AnimatorControllerParameter parameter in targetAnimator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Float
                || parameter.nameHash != LocomotionSpeedParameterHash)
                continue;

            targetAnimator.SetFloat(
                LocomotionSpeedParameterHash,
                ResolvePositiveOrDefault(profile.locomotionAnimationSpeedMultiplier, 1f));
            return;
        }
    }

    private void CaptureBaseAnimatorController()
    {
        if (baseAnimatorController != null || targetAnimator == null)
            return;

        RuntimeAnimatorController current = targetAnimator.runtimeAnimatorController;
        baseAnimatorController = current is AnimatorOverrideController overrideController
            ? overrideController.runtimeAnimatorController
            : current;
    }

    private void ApplyAnimatorOverride(WeaponCombatAnimationProfile profile)
    {
        if (targetAnimator == null)
            return;

        CaptureBaseAnimatorController();
        if (baseAnimatorController == null || runtimeOverrideProfile == profile)
            return;

        AnimatorOverrideController previousController = runtimeOverrideController;

        runtimeOverrideController = new AnimatorOverrideController(baseAnimatorController)
        {
            name = profile.name + "_Runtime"
        };
        if (profile.animatorOverrideController != null)
        {
            var overrides = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
            profile.animatorOverrideController.GetOverrides(overrides);
            runtimeOverrideController.ApplyOverrides(overrides);
        }

        runtimeOverrideProfile = profile;
        targetAnimator.runtimeAnimatorController = runtimeOverrideController;
        if (previousController != null)
        {
            if (Application.isPlaying)
                Destroy(previousController);
            else
                DestroyImmediate(previousController);
        }
        layerIndex = -1;
        transitionLowerLayerIndex = -1;
    }

    private bool ApplyAttackClip(AnimationClip clip)
    {
        if (runtimeOverrideController == null || attackTemplateClip == null || clip == null)
            return false;

        runtimeOverrideController[attackTemplateClip] = clip;
        return true;
    }

    private void OnDestroy()
    {
        if (runtimeOverrideController == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeOverrideController);
        else
            DestroyImmediate(runtimeOverrideController);
    }

    private WeaponCombatAnimationProfile ResolveProfile()
    {
        WeaponItemData weaponData = playerEquipment != null ? playerEquipment.CurrentWeaponData : null;
        WeaponCombatAnimationProfile weaponProfile = weaponData != null
            ? weaponData.GetCombatAnimationProfile()
            : null;
        if (weaponProfile != null)
            return weaponProfile;

        return fallbackProfile;
    }

    private void ResolveLayerIndex()
    {
        if (targetAnimator == null)
            return;

        bool mainLayerValid = layerIndex >= 0
            && layerIndex < targetAnimator.layerCount
            && targetAnimator.GetLayerName(layerIndex) == layerName;

        if (!mainLayerValid)
            layerIndex = targetAnimator.GetLayerIndex(layerName);

        bool transitionLowerLayerValid = transitionLowerLayerIndex >= 0
            && transitionLowerLayerIndex < targetAnimator.layerCount
            && targetAnimator.GetLayerName(transitionLowerLayerIndex) == transitionLowerLayerName;

        if (!transitionLowerLayerValid)
        {
            transitionLowerLayerIndex = string.IsNullOrEmpty(transitionLowerLayerName)
                ? -1
                : targetAnimator.GetLayerIndex(transitionLowerLayerName);
        }
    }

    private void UpdateLegacySuppression()
    {
        if (!legacySuppressed)
            return;

        if (Time.time < legacySuppressedUntil)
            return;

        legacySuppressed = false;
        if (combatRequested && activeAction != DriverAction.Unequip)
            targetLayerWeight = 1f;
    }

    private void UpdateActionState()
    {
        if (activeAction == DriverAction.None)
            return;

        if (Time.time < activeActionEndTime)
            return;

        DriverAction endedAction = activeAction;
        bool movingAtActionEnd = IsMovingInputActive();
        activeAction = DriverAction.None;

        if (endedAction == DriverAction.Unequip)
        {
            if (movingAtActionEnd)
            {
                layerFadeOutDurationOverride = Mathf.Max(layerFadeOutDuration, movingTransitionEndBlendDuration);
                transitionLowerFadeOutDurationOverride = Mathf.Max(transitionLowerLayerFadeDuration, movingTransitionEndBlendDuration);
            }

            targetLayerWeight = 0f;
            targetTransitionLowerLayerWeight = 0f;
            return;
        }

        if (movingAtActionEnd)
            transitionLowerFadeOutDurationOverride = Mathf.Max(transitionLowerLayerFadeDuration, movingTransitionEndBlendDuration);

        targetTransitionLowerLayerWeight = 0f;
        if (combatRequested && !legacySuppressed)
            PlayLocomotionByGuardState(ResolveActionEndTransitionDuration(movingAtActionEnd));
    }

    private void UpdateLocomotionState()
    {
        if (!combatRequested || legacySuppressed || activeAction != DriverAction.None)
            return;

        PlayLocomotionByGuardState(locomotionToGuardTransitionDuration);
    }

    private void PlayLocomotionByGuardState(float transitionDuration)
    {
        bool targetGuard = playerMovement != null && playerMovement.IsMeleeGuarding;
        string stateName = targetGuard
            ? guardLocomotionStateName
            : locomotionStateName;

        if (IsCurrentOrNextState(stateName))
            return;

        float resolvedTransitionDuration = ResolveGuardLocomotionTransitionDuration(targetGuard, transitionDuration);
        float startOffsetSeconds = targetGuard
            ? ResolvePositiveOrDefault(guardLocomotionStartOffsetSeconds, DefaultGuardLocomotionStartOffsetSeconds)
            : 0f;
        PlayState(stateName, resolvedTransitionDuration, startOffsetSeconds);
    }

    private float ResolveGuardLocomotionTransitionDuration(bool targetGuard, float fallbackDuration)
    {
        float profileDuration = targetGuard
            ? guardLocomotionEnterTransitionDuration
            : guardLocomotionExitTransitionDuration;

        return Mathf.Max(0f, Mathf.Max(fallbackDuration, profileDuration));
    }

    private static float ResolvePositiveOrDefault(float value, float fallback)
    {
        return value > 0f ? value : fallback;
    }

    private float ResolveActionEndTransitionDuration(bool movingAtActionEnd)
    {
        if (!movingAtActionEnd)
            return equipToLocomotionTransitionDuration;

        return Mathf.Max(equipToLocomotionTransitionDuration, movingTransitionEndBlendDuration);
    }

    private bool CanPlayCombatAction()
    {
        return combatRequested
            && !legacySuppressed
            && activeAction != DriverAction.Unequip
            && IsAvailable;
    }

    private bool CanPlayAttack(bool allowCombatEntry)
    {
        return (combatRequested || allowCombatEntry)
            && !legacySuppressed
            && activeAction != DriverAction.Unequip
            && IsAvailable;
    }

    private void PrepareCombatLayerForAttackEntry()
    {
        combatRequested = true;
        emptyStateAppliedAfterFade = false;
        layerFadeOutDurationOverride = -1f;
        transitionLowerFadeOutDurationOverride = -1f;
        targetLayerWeight = 1f;
    }

    private bool CanPlayInterruptibleCombatAction()
    {
        return CanPlayCombatAction()
            && activeAction != DriverAction.Roll;
    }

    private void SetActionSpeedForClip(AnimationClip clip, float targetDuration)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(actionSpeedParameterName))
            return;

        float speed = clip != null && targetDuration > 0f
            ? Mathf.Max(0.01f, clip.length / targetDuration)
            : 1f;

        AnimatorControllerParameter[] parameters = targetAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type != AnimatorControllerParameterType.Float || parameter.name != actionSpeedParameterName)
                continue;

            targetAnimator.SetFloat(actionSpeedParameterName, speed);
            return;
        }
    }

    private void PlayActionState(string stateName, DriverAction action, float duration, float transitionDuration)
    {
        PlayActionState(stateName, action, duration, transitionDuration, 0f);
    }

    private void PlayActionState(
        string stateName,
        DriverAction action,
        float duration,
        float transitionDuration,
        float normalizedStartTime)
    {
        if (!PlayState(stateName, Mathf.Max(0f, transitionDuration), normalizedStartTime))
            return;

        activeAction = action;
        activeActionEndTime = Time.time + Mathf.Max(0.01f, duration);
    }

    private bool PlayState(string stateName, float transitionDuration, float normalizedTime)
    {
        return PlayState(layerIndex, layerName, stateName, transitionDuration, normalizedTime);
    }

    private bool HasState(string stateName)
    {
        return HasState(layerIndex, layerName, stateName);
    }

    private bool IsCurrentState(string stateName)
    {
        return IsCurrentState(layerIndex, layerName, stateName);
    }

    private bool IsCurrentOrNextState(string stateName)
    {
        return IsCurrentOrNextState(layerIndex, layerName, stateName);
    }

    private bool PlayState(int animatorLayerIndex, string animatorLayerName, string stateName, float transitionDuration, float normalizedTime)
    {
        if (targetAnimator == null || animatorLayerIndex < 0 || string.IsNullOrEmpty(stateName))
            return false;

        int stateHash = Animator.StringToHash(animatorLayerName + "." + stateName);
        if (!targetAnimator.HasState(animatorLayerIndex, stateHash))
        {
            stateHash = Animator.StringToHash(stateName);
            if (!targetAnimator.HasState(animatorLayerIndex, stateHash))
                return false;
        }

        if (transitionDuration > 0f)
            targetAnimator.CrossFadeInFixedTime(stateHash, transitionDuration, animatorLayerIndex, Mathf.Clamp01(normalizedTime));
        else
            targetAnimator.Play(stateHash, animatorLayerIndex, Mathf.Clamp01(normalizedTime));

        return true;
    }

    private bool HasState(int animatorLayerIndex, string animatorLayerName, string stateName)
    {
        if (targetAnimator == null || animatorLayerIndex < 0 || string.IsNullOrEmpty(stateName))
            return false;

        int stateHash = Animator.StringToHash(animatorLayerName + "." + stateName);
        if (targetAnimator.HasState(animatorLayerIndex, stateHash))
            return true;

        return targetAnimator.HasState(animatorLayerIndex, Animator.StringToHash(stateName));
    }

    private bool IsCurrentState(int animatorLayerIndex, string animatorLayerName, string stateName)
    {
        if (targetAnimator == null || animatorLayerIndex < 0 || string.IsNullOrEmpty(stateName))
            return false;

        AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(animatorLayerIndex);
        return stateInfo.shortNameHash == Animator.StringToHash(stateName)
            || stateInfo.fullPathHash == Animator.StringToHash(animatorLayerName + "." + stateName);
    }

    private bool IsCurrentOrNextState(int animatorLayerIndex, string animatorLayerName, string stateName)
    {
        if (IsCurrentState(animatorLayerIndex, animatorLayerName, stateName))
            return true;

        if (targetAnimator == null || animatorLayerIndex < 0 || string.IsNullOrEmpty(stateName))
            return false;

        if (!targetAnimator.IsInTransition(animatorLayerIndex))
            return false;

        AnimatorStateInfo nextStateInfo = targetAnimator.GetNextAnimatorStateInfo(animatorLayerIndex);
        return nextStateInfo.shortNameHash == Animator.StringToHash(stateName)
            || nextStateInfo.fullPathHash == Animator.StringToHash(animatorLayerName + "." + stateName);
    }

    private void UpdateTransitionLowerBodyLayer(float deltaTime)
    {
        if (targetAnimator == null || transitionLowerLayerIndex < 0)
            return;

        bool shouldUseLowerBody = ShouldUseTransitionLowerBody();
        targetTransitionLowerLayerWeight = shouldUseLowerBody ? 1f : 0f;

        if (shouldUseLowerBody)
        {
            transitionLowerEmptyStateAppliedAfterFade = false;
            PlayTransitionLowerLocomotion();
        }

        float currentWeight = targetAnimator.GetLayerWeight(transitionLowerLayerIndex);
        float fadeDuration = ResolveTransitionLowerFadeDuration(currentWeight);
        float speed = fadeDuration <= 0f
            ? 1000f
            : 1f / Mathf.Max(0.0001f, fadeDuration);
        float nextWeight = Mathf.MoveTowards(
            currentWeight,
            Mathf.Clamp01(targetTransitionLowerLayerWeight),
            speed * Mathf.Max(0f, deltaTime));

        targetAnimator.SetLayerWeight(transitionLowerLayerIndex, nextWeight);
        if (nextWeight <= 0.001f)
            transitionLowerFadeOutDurationOverride = -1f;

        ApplyTransitionLowerEmptyStateAfterFadeOut();
    }

    private bool ShouldUseTransitionLowerBody()
    {
        if (legacySuppressed)
            return false;

        bool transitionAction = activeAction == DriverAction.Equip || activeAction == DriverAction.Unequip;
        bool synthesizedGuardLocomotion = activeAction == DriverAction.None
            && useTransitionLowerBodyWhileGuarding
            && playerMovement != null
            && playerMovement.IsMeleeGuarding;
        if (!transitionAction && !synthesizedGuardLocomotion)
            return false;

        if (!HasState(transitionLowerLayerIndex, transitionLowerLayerName, transitionLowerLocomotionStateName))
            return false;

        return IsMovingForTransitionLowerBody();
    }

    private bool IsMovingForTransitionLowerBody()
    {
        return IsMovingInputActive();
    }

    private bool IsMovingInputActive()
    {
        if (playerMovement == null)
            return false;

        float threshold = Mathf.Max(0f, transitionLowerMoveInputThreshold);
        return playerMovement.MoveInput.sqrMagnitude > threshold * threshold;
    }

    private void PlayTransitionLowerLocomotion()
    {
        if (IsCurrentOrNextState(transitionLowerLayerIndex, transitionLowerLayerName, transitionLowerLocomotionStateName))
            return;

        PlayState(transitionLowerLayerIndex, transitionLowerLayerName, transitionLowerLocomotionStateName, 0.04f, 0f);
    }

    private void ApplyTransitionLowerEmptyStateAfterFadeOut()
    {
        if (targetTransitionLowerLayerWeight > 0f || transitionLowerEmptyStateAppliedAfterFade)
            return;

        if (targetAnimator == null || transitionLowerLayerIndex < 0)
            return;

        if (targetAnimator.GetLayerWeight(transitionLowerLayerIndex) > 0.001f)
            return;

        PlayState(transitionLowerLayerIndex, transitionLowerLayerName, transitionLowerEmptyStateName, 0f, 0f);
        transitionLowerEmptyStateAppliedAfterFade = true;
    }

    private void UpdateLayerWeight(float deltaTime)
    {
        if (targetAnimator == null || layerIndex < 0)
            return;

        float currentWeight = targetAnimator.GetLayerWeight(layerIndex);
        float duration = targetLayerWeight > currentWeight ? layerFadeInDuration : ResolveFadeOutDuration();
        float speed = duration <= 0f ? 1000f : 1f / Mathf.Max(0.0001f, duration);
        float nextWeight = Mathf.MoveTowards(currentWeight, Mathf.Clamp01(targetLayerWeight), speed * Mathf.Max(0f, deltaTime));
        targetAnimator.SetLayerWeight(layerIndex, nextWeight);
        if (nextWeight <= 0.001f)
            layerFadeOutDurationOverride = -1f;
    }

    private float ResolveFadeOutDuration()
    {
        if (legacySuppressed)
            return legacyActionSuppressionFadeDuration;

        if (layerFadeOutDurationOverride >= 0f)
            return layerFadeOutDurationOverride;

        return layerFadeOutDuration;
    }

    private float ResolveTransitionLowerFadeDuration(float currentWeight)
    {
        if (targetTransitionLowerLayerWeight < currentWeight && transitionLowerFadeOutDurationOverride >= 0f)
            return transitionLowerFadeOutDurationOverride;

        return transitionLowerLayerFadeDuration;
    }

    private void ApplyEmptyStateAfterFadeOut()
    {
        if (targetLayerWeight > 0f || emptyStateAppliedAfterFade)
            return;

        if (targetAnimator == null || layerIndex < 0)
            return;

        if (targetAnimator.GetLayerWeight(layerIndex) > 0.001f)
            return;

        PlayState(emptyStateName, 0f, 0f);
        emptyStateAppliedAfterFade = true;
    }

    private int PickHitIndex()
    {
        if (hitClips == null || hitClips.Length == 0)
            return -1;

        int availableCount = 0;
        for (int i = 0; i < hitClips.Length; i++)
        {
            if (hitClips[i] != null && HasState(hitStateNamePrefix + (i + 1).ToString("00")))
                availableCount++;
        }

        if (availableCount <= 0)
            return -1;

        int selectedIndex = Random.Range(0, availableCount);
        for (int i = 0; i < hitClips.Length; i++)
        {
            if (hitClips[i] == null || !HasState(hitStateNamePrefix + (i + 1).ToString("00")))
                continue;

            if (selectedIndex == 0)
                return i;

            selectedIndex--;
        }

        return -1;
    }

    private float GetClipLength(AnimationClip clip)
    {
        return clip != null ? Mathf.Max(0.01f, clip.length) : 0.25f;
    }

    private float GetScaledClipLength(AnimationClip clip, float speedMultiplier)
    {
        return GetClipLength(clip) / Mathf.Max(0.01f, speedMultiplier);
    }

    private float GetScaledClipLength(AnimationClip clip, float speedMultiplier, float startOffsetSeconds)
    {
        float clipLength = GetClipLength(clip);
        float remainingLength = Mathf.Max(0.01f, clipLength - ClampClipStartOffset(clipLength, startOffsetSeconds));
        return remainingLength / Mathf.Max(0.01f, speedMultiplier);
    }

    private float GetClipStartOffsetNormalized(AnimationClip clip, float startOffsetSeconds)
    {
        float clipLength = GetClipLength(clip);
        return ClampClipStartOffset(clipLength, startOffsetSeconds) / clipLength;
    }

    private float ClampClipStartOffset(float clipLength, float startOffsetSeconds)
    {
        return Mathf.Clamp(Mathf.Max(0f, startOffsetSeconds), 0f, Mathf.Max(0f, clipLength - 0.01f));
    }
}
