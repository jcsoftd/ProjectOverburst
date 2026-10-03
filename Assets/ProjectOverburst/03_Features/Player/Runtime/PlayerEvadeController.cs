using System;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum PlayerEvadeType // 회피 종류
{
    Dash = 0,
    Roll = 1,
    ExplorationDodge = 2,
    CombatDodge = 3
}

[DefaultExecutionOrder(280)]
public class PlayerEvadeController : MonoBehaviour // Dash / Roll 회피
{
#if UNITY_EDITOR
    private const string EditorDefaultRollAnimationClipPath = "Assets/ProjectOverburst/03_Features/Player/Animations/Roll/InPlace/RM_Roll_front_InPlace.anim";
#endif
    [Header("Common")]
    [SerializeField] private PlayerEvadeProfile evadeProfile;
    [SerializeField] private float cooldown = 0.25f;

    [Header("Dash")]
    [SerializeField] private float dashDistance = 5.5f;
    [SerializeField] private float dashDuration = 0.16f;
    [SerializeField] private float dashInvincibleDuration = 0.18f;
    [SerializeField] private float dashPerfectWindow = 0.1f;

    [Header("Roll")]
    [SerializeField] private float rollDistance = 4f;
    [SerializeField] private float rollDuration = 0.45f;
    [SerializeField] private float rollInvincibleDuration = 0.24f;
    [SerializeField] private float rollPerfectWindow = 0.12f;

    [Header("Roll Smoothing")]
    [SerializeField] private float rollMoveEase = 0.35f;
    [SerializeField] private float rollDirectionBlendSpeed = 32f;
    [SerializeField] private float rollExitRotationBlendDuration = 0.14f;
    [SerializeField] private float rollExitRotationSpeedMultiplier = 0.35f;

    [Header("Perfect Evade")]
    [SerializeField] private float perfectEvadeTimeScale = 0.4f;
    [SerializeField] private float perfectEvadeSlowDuration = 0.1f;

    [Header("Animation")]
    [SerializeField] private PlayerAnimation playerAnimation;
    [SerializeField] private AnimationClip rollAnimationClip;
    [SerializeField] private float evadeAnimationTransitionDuration = 0.04f;

    private PlayerMovement playerMovement;
    private MeleeRuntime meleeRuntime;
    [SerializeField] private CombatMotionDriver combatMotion;
    private bool isEvading;
    private bool perfectEvadeTriggered;
    private PlayerEvadeType activeType;
    private Vector3 activeDirection;
    private float activeRollFacingYawOffset;
    private float activeDistance;
    private float activeDuration;
    private float activeMovedDistance;
    private float activeElapsed;
    private float evadeStartTime;
    private float evadeEndTime;
    private float invincibleEndTime;
    private float perfectWindowEndTime;
    private float nextEvadeTime;
    private float rollRotationRecoveryEndTime;
    private PlayerInputFacade inputFacade;
    private PlayerStateCoordinator stateCoordinator;
    private CombatHealth health;
    private Vector3 activeFacing;
    private float activeMoveEase;
    private bool activeStartedInCombat;
    private int nextExecutionId = 1;
    private AnimationClip activeDodgeClip;
    private string activeDodgeState;
    private bool hasCompletedDodgeFollowUp;
    private PlayerDodgeFollowUpRequest completedDodgeFollowUp;

    public PlayerEvadeProfile Profile => evadeProfile;
    public int ActiveExecutionId { get; private set; }
    public bool LastEndWasCompleted { get; private set; }
    public float ActiveDuration => activeDuration;
    public float ActiveNormalizedTime => Mathf.Clamp01(activeElapsed / Mathf.Max(.01f, activeDuration));
    public Vector3 ActiveDirection => activeDirection;
    public bool IsFollowUpInputWindowOpen => isEvading && activeType == PlayerEvadeType.CombatDodge
        && activeElapsed < activeDuration && OverburstGameClock.UnscaledTime < evadeEndTime;
    public bool CanEvadeInCurrentMode => playerMovement != null
        && (PlayerCombatModeController.IsSharedCombatModeActive()
            ? playerMovement.IsMeleeCombatLocomotionMode : evadeProfile != null);
#if UNITY_EDITOR
    private bool triedEditorDefaultRollAnimationClip;
#endif

    public event Action<PlayerEvadeType> OnEvadeStarted;
    public event Action<PlayerEvadeType> OnEvadeEnded;
    public event Action<PlayerEvadeType, DamageInfo> OnPerfectEvade;

    public bool IsEvading
    {
        get { return isEvading; }
    }

    public bool IsRollExitRecovering => isActiveAndEnabled
        && OverburstGameClock.UnscaledTime < rollRotationRecoveryEndTime;

    public bool IsInvincible
    {
        get { return OverburstGameClock.UnscaledTime < invincibleEndTime; }
    }

    public bool IsPerfectEvadeWindowActive
    {
        get { return OverburstGameClock.UnscaledTime < perfectWindowEndTime; }
    }

    public PlayerEvadeType ActiveType
    {
        get { return activeType; }
    }

    public float RotationRecoverySpeedMultiplier
    {
        get
        {
            if (OverburstGameClock.UnscaledTime >= rollRotationRecoveryEndTime)
                return 1f;

            float duration = Mathf.Max(0.001f, rollExitRotationBlendDuration);
            float remaining01 = Mathf.Clamp01((rollRotationRecoveryEndTime - OverburstGameClock.UnscaledTime) / duration);
            return Mathf.Lerp(1f, Mathf.Clamp01(rollExitRotationSpeedMultiplier), remaining01);
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        hasCompletedDodgeFollowUp = false;
        meleeRuntime?.DiscardDodgeComboContinuation();
        meleeRuntime?.CancelDodgeLightWindup();
        ResolveFacade()?.CombatInputs?.Invalidate();
        OverburstTimeEffectArbiter.ClearOwner(this);
        if (isEvading)
            EndEvade(false);
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        if (stateCoordinator != null)
            stateCoordinator.ReleaseLocomotion(this);
    }

    private void Update()
    {
        ResolveReferences();
        if (isEvading && ((health != null && health.IsDead)
            || (stateCoordinator != null && stateCoordinator.CurrentCondition != PlayerConditionState.Normal
                && !(stateCoordinator.CurrentCondition == PlayerConditionState.InputBlocked && OverburstTimeEffectArbiter.IsPaused))
            || activeStartedInCombat != PlayerCombatModeController.IsSharedCombatModeActive()))
            EndEvade(false);
        ReadEvadeInput();
        if (isEvading && activeType == PlayerEvadeType.CombatDodge)
            ResolveFacade()?.CombatInputs?.SampleDodgeFollowUp();
        if (isEvading && activeType == PlayerEvadeType.CombatDodge)
        {
            var inputs = ResolveFacade()?.CombatInputs;
            if (inputs != null && inputs.PeekDodgeFollowUp(ActiveExecutionId, out var request) && meleeRuntime != null)
            {
                if (request.Kind == PlayerDodgeFollowUpKind.Heavy)
                    meleeRuntime.PreviewDashHeavy(request, Mathf.Max(0f, OverburstGameClock.UnscaledTime - evadeStartTime),
                        activeDistance, activeDuration, activeMoveEase);
                else meleeRuntime.PreviewDodgeLight(request, evadeEndTime - meleeRuntime.DodgeLightWindupLead);
            }
            else if (meleeRuntime != null && meleeRuntime.CancelDodgeLightWindup())
                playerAnimation?.ResumeDodgeVisual(activeDodgeClip, activeDodgeState, activeDuration, ActiveNormalizedTime,
                    evadeProfile.entryBlend, evadeProfile.exitBlend);
        }
        // 회피는 히트스톱을 무시하려고 unscaled 시간을 쓰되, ESC 메뉴 멈춤 동안에는 흐르지 않는 시계를 쓴다(2026-10-01).
        UpdateEvadeMotion(OverburstGameClock.UnscaledDeltaTime);
    }

    private void LateUpdate()
    {
        MaintainEvadeDirection(OverburstGameClock.UnscaledDeltaTime);
        if (!hasCompletedDodgeFollowUp) return;
        var request = completedDodgeFollowUp;
        hasCompletedDodgeFollowUp = false;
        // MoveDirect can temporarily clear grounding. PlayerMovement has now probed and stepped the motor.
        if (isEvading || GameplayInputBlocker.IsGameplayInputBlocked
            || !PlayerCombatModeController.IsSharedCombatModeActive()
            || ResolveFacade() == null || !ResolveFacade().IsGameplayEnabled
            || ResolveFacade().CombatInputs == null || !ResolveFacade().CombatInputs.IsDodgeFollowUpRequestValid(request)
            || (health != null && health.IsDead)
            || (stateCoordinator != null && stateCoordinator.CurrentCondition != PlayerConditionState.Normal))
        { meleeRuntime?.CancelDodgeLightWindup(); return; }
        if (meleeRuntime == null || !meleeRuntime.TryStartDodgeFollowUp(request)) meleeRuntime?.CancelDodgeLightWindup();
    }

    public bool TryCancelDamageByEvade(DamageInfo damageInfo)
    {
        if (damageInfo.isDamageOverTime || !damageInfo.triggersOnHitEffects)
            return false;

        if (!IsInvincible)
            return false;

        if (IsPerfectEvadeWindowActive && !perfectEvadeTriggered)
            TriggerPerfectEvade(damageInfo);

        return true;
    }

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (meleeRuntime == null)
            meleeRuntime = GetComponent<MeleeRuntime>();

        if (combatMotion == null && playerMovement != null)
            combatMotion = playerMovement.CombatMotion;
        if (combatMotion == null)
            combatMotion = GetComponent<CombatMotionDriver>();

        if (playerAnimation == null)
            playerAnimation = GetComponent<PlayerAnimation>();

        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();

        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (health == null) health = GetComponent<CombatHealth>();

#if UNITY_EDITOR
        if (rollAnimationClip == null)
            rollAnimationClip = TryLoadEditorDefaultRollAnimationClip();
#endif
    }

    private void ReadEvadeInput()
    {
        TryExecuteBufferedEvade();
    }

    public bool TryExecuteBufferedEvade()
    {
        if (!isActiveAndEnabled) return false;
        ResolveReferences();
        if (ResolveStateCoordinator() != null
            && ResolveStateCoordinator().CurrentCondition != PlayerConditionState.Normal) return false;
        PlayerInputFacade facade = ResolveFacade();
        if (facade == null || facade.CombatInputs == null || !facade.CombatInputs.HasEvade)
            return false;

        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        if (isEvading || OverburstGameClock.UnscaledTime < nextEvadeTime)
            return false;

        if (playerMovement != null && playerMovement.IsMeleeAttackMoveLocked && !CanCancelMeleeComboForEvade())
            return false; // 이동 잠금 중에는 MeleeRuntime 회피 취소 계약을 따른다.

        if (!CanEvadeInCurrentMode)
            return false;

        PlayerEvadeType type = PlayerCombatModeController.IsSharedCombatModeActive()
            ? (evadeProfile != null ? SelectCombatEvade(transform.forward, ResolveEvadeDirection()) : PlayerEvadeType.Roll)
            : PlayerEvadeType.ExplorationDodge;
        if (!TryStartEvade(type)) return false;
        facade.CombatInputs.ConsumeEvade();
        if (type == PlayerEvadeType.CombatDodge)
            facade.CombatInputs.BeginDodgeFollowUpWindow(ActiveExecutionId);
        else facade.CombatInputs.ClearDodgeFollowUp();
        return true;
    }

    public static PlayerEvadeType SelectCombatEvade(Vector3 facing, Vector3 direction)
    {
        facing.y = direction.y = 0f;
        if (facing.sqrMagnitude < .001f) facing = Vector3.forward;
        if (direction.sqrMagnitude < .001f) direction = facing;
        return Vector3.Dot(facing.normalized, direction.normalized) >= -.000001f
            ? PlayerEvadeType.CombatDodge : PlayerEvadeType.Roll;
    }

    private bool TryStartEvade(PlayerEvadeType evadeType)
    {
        hasCompletedDodgeFollowUp = false;
        activeFacing = transform.forward; activeFacing.y = 0;
        activeFacing = activeFacing.sqrMagnitude > .001f ? activeFacing.normalized : Vector3.forward;
        activeDirection = ResolveEvadeDirection();
        meleeRuntime?.CaptureDodgeComboContinuation(nextExecutionId, evadeType == PlayerEvadeType.CombatDodge);
        meleeRuntime?.CancelActiveComboForEvade(); // 공격 취소와 콤보 연결 상태 초기화는 10번대 위임

        activeType = evadeType;
        var weaponProfile = GetComponent<PlayerEquipment>()?.CurrentWeaponData?.GetCombatAnimationProfile();
        activeRollFacingYawOffset = evadeType == PlayerEvadeType.Roll && weaponProfile != null ? weaponProfile.rollFacingYawOffset : 0f;
        activeDirection.y = 0f;
        activeDirection = activeDirection.sqrMagnitude > 0.001f ? activeDirection.normalized : transform.forward;

        if (activeDirection.sqrMagnitude <= 0.001f)
            activeDirection = Vector3.forward;

        if (evadeType == PlayerEvadeType.Roll)
        {
            ConfigureActiveEvade(rollDistance, ResolveRollDuration(), rollInvincibleDuration, rollPerfectWindow);
            activeMoveEase = rollMoveEase;
        }
        else if (evadeType == PlayerEvadeType.ExplorationDodge || evadeType == PlayerEvadeType.CombatDodge)
        {
            var settings = evadeType == PlayerEvadeType.ExplorationDodge ? evadeProfile.exploration : evadeProfile.combatDodge;
            ConfigureActiveEvade(settings.distance, settings.duration, settings.invincibleDuration, settings.perfectWindow);
            activeMoveEase = settings.moveEase;
            if (evadeType == PlayerEvadeType.ExplorationDodge)
            {
                bool moving = ReadRawMoveInput().sqrMagnitude > .001f;
                activeDodgeClip = moving ? evadeProfile.explorationDodgeToRun : evadeProfile.explorationDodge;
                activeDodgeState = moving ? PlayerEvadeProfile.ExplorationRunState : PlayerEvadeProfile.ExplorationStandState;
                transform.rotation = Quaternion.LookRotation(activeDirection, Vector3.up);
            }
            else activeDodgeClip = evadeProfile.ResolveCombatClip(
                Quaternion.Inverse(Quaternion.LookRotation(activeFacing, Vector3.up)) * activeDirection, out activeDodgeState);
        }
        else
        {
            ConfigureActiveEvade(dashDistance, dashDuration, dashInvincibleDuration, dashPerfectWindow);
            activeMoveEase = 0f;
        }

        ActiveExecutionId = nextExecutionId++;
        if (nextExecutionId <= 0) nextExecutionId = 1;
        activeStartedInCombat = PlayerCombatModeController.IsSharedCombatModeActive();
        isEvading = true;
        perfectEvadeTriggered = false;
        activeElapsed = 0f;
        activeMovedDistance = 0f;
        evadeStartTime = OverburstGameClock.UnscaledTime;
        evadeEndTime = evadeStartTime + activeDuration;
        float reuse = activeType == PlayerEvadeType.ExplorationDodge ? evadeProfile.exploration.cooldown
            : activeType == PlayerEvadeType.CombatDodge ? evadeProfile.combatDodge.cooldown : cooldown;
        nextEvadeTime = evadeStartTime + Mathf.Max(0f, reuse);
        rollRotationRecoveryEndTime = 0f;
        // GOAL A2: 회피 구간 Locomotion.Evading을 명시 요청한다.
        ResolveStateCoordinator()?.RequestLocomotion(this, PlayerLocomotionState.Evading);
        if (activeType == PlayerEvadeType.Roll && Mathf.Abs(activeRollFacingYawOffset) > .01f)
            transform.rotation = Quaternion.LookRotation(activeDirection, Vector3.up)
                * Quaternion.Euler(0f, activeRollFacingYawOffset, 0f);
        MaintainEvadeDirection(OverburstGameClock.UnscaledDeltaTime);

        playerMovement?.PrepareEvadeMotion();
        if (!PlayEvadeAnimation(activeType, activeDuration)) { EndEvade(false); return false; }
        OnEvadeStarted?.Invoke(activeType);
        OverburstFeelFeedbackHub.Request(OverburstFeelCue.Evade, transform.position);
        CombatActionSfxService.PlayPlayerEvade(transform.position);
        return true;
    }

    private bool CanCancelMeleeComboForEvade()
    {
        return meleeRuntime != null && meleeRuntime.CanCancelActiveComboForEvade;
    }

    private void RotateToEvadeDirection(float deltaTime)
    {
        Vector3 direction = activeDirection;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up)
            * Quaternion.Euler(0f, activeRollFacingYawOffset, 0f);
        float blend = 1f - Mathf.Exp(-Mathf.Max(0f, rollDirectionBlendSpeed) * Mathf.Max(0f, deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Mathf.Clamp01(blend));
    }

    private void MaintainEvadeDirection(float deltaTime)
    {
        if (!isEvading) return;
        if (activeType == PlayerEvadeType.Roll) RotateToEvadeDirection(deltaTime);
        else if (activeType == PlayerEvadeType.CombatDodge)
        {
            var target = Quaternion.LookRotation(meleeRuntime != null && (meleeRuntime.IsDodgeLightWindupActive || meleeRuntime.IsDashHeavyWindupActive) ? activeDirection : activeFacing, Vector3.up);
            transform.rotation = meleeRuntime != null && (meleeRuntime.IsDodgeLightWindupActive || meleeRuntime.IsDashHeavyWindupActive)
                ? Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-24f * Mathf.Max(0f, deltaTime))) : target;
        }
    }

    private bool PlayEvadeAnimation(PlayerEvadeType evadeType, float duration)
    {
        if (playerAnimation == null) return false;
        if (evadeType == PlayerEvadeType.ExplorationDodge || evadeType == PlayerEvadeType.CombatDodge)
            return playerAnimation.TryPlayConfiguredDodge(evadeType, activeDodgeClip, activeDodgeState,
                duration, evadeProfile.entryBlend, evadeProfile.exitBlend);
        if (evadeType != PlayerEvadeType.Roll) return false;

        AnimationClip clip = rollAnimationClip;
#if UNITY_EDITOR
        if (clip == null)
            clip = TryLoadEditorDefaultRollAnimationClip();
#endif

        playerAnimation.PlayEvadeFullBody(clip, duration, evadeAnimationTransitionDuration);
        return true;
    }

    private float ResolveRollDuration()
    {
        return rollDuration;
    }

    private void ConfigureActiveEvade(float distance, float duration, float invincibleDuration, float perfectWindow)
    {
        activeDistance = Mathf.Max(0f, distance);
        activeDuration = Mathf.Max(0.01f, duration);
        invincibleEndTime = OverburstGameClock.UnscaledTime + Mathf.Max(0f, invincibleDuration);
        perfectWindowEndTime = OverburstGameClock.UnscaledTime + Mathf.Max(0f, perfectWindow);
    }

    private Vector3 ResolveEvadeDirection()
    {
        Vector2 input = ReadRawMoveInput();
        if (input.sqrMagnitude > 0.001f && playerMovement != null)
            return playerMovement.ResolveMoveDirection(input);

        return transform.forward;
    }

    private Vector2 ReadRawMoveInput()
    {
        // GOAL A2: WASD 직접 읽기 대신 Gameplay Move 벡터를 사용한다.
        PlayerInputFacade facade = ResolveFacade();
        if (facade == null)
            return Vector2.zero;

        return Vector2.ClampMagnitude(facade.MoveValue, 1f);
    }

    private void UpdateEvadeMotion(float deltaTime)
    {
        if (!isEvading)
            return;
        if (OverburstTimeEffectArbiter.IsPaused) return;

        if (combatMotion == null)
        {
            EndEvade(false);
            return;
        }

        // The attack windup and combat dodge must end on the same clock, without counting the start frame twice.
        activeElapsed = activeType == PlayerEvadeType.CombatDodge
            ? Mathf.Max(0f, OverburstGameClock.UnscaledTime - evadeStartTime)
            : activeElapsed + Mathf.Max(0f, deltaTime);
        float progress = Mathf.Clamp01(activeElapsed / activeDuration);
        float targetDistance = meleeRuntime != null && meleeRuntime.IsDashHeavyWindupActive
            ? meleeRuntime.DashHeavyTravelDistance(activeElapsed) : activeDistance * GetDistanceProgress(progress);
        float moveDistance = Mathf.Max(0f, targetDistance - activeMovedDistance);
        activeMovedDistance = targetDistance;

        if (moveDistance > 0f)
        {
            Vector3 displacement = activeDirection * moveDistance;
            combatMotion.ApplyEvadeDisplacement(displacement);
        }

        if (OverburstGameClock.UnscaledTime >= evadeEndTime || progress >= 1f)
            EndEvade();
    }

    private float GetDistanceProgress(float progress)
    {
        float ease = Mathf.Clamp01(activeMoveEase);
        if (ease <= 0f)
            return progress;

        float smoothProgress = progress * progress * (3f - 2f * progress);
        return Mathf.Lerp(progress, smoothProgress, ease);
    }

    public void CancelForKnockdown()
    {
        hasCompletedDodgeFollowUp = false;
        meleeRuntime?.DiscardDodgeComboContinuation();
        meleeRuntime?.CancelDodgeLightWindup();
        EndEvade(false);
        rollRotationRecoveryEndTime = 0;
        OverburstTimeEffectArbiter.ClearOwner(this);
        ResolveFacade()?.CombatInputs?.Invalidate();
    }

    private void EndEvade(bool completed = true)
    {
        if (!isEvading)
            return;

        PlayerEvadeType endedType = activeType;
        int endedExecutionId = ActiveExecutionId;
        var inputs = ResolveFacade()?.CombatInputs;
        // Take once, before events can change the actor or weapon. The request carries its weapon identity.
        PlayerDodgeFollowUpRequest request = default;
        bool followUp = inputs != null && inputs.TakeDodgeFollowUp(endedExecutionId, completed, out request);
        isEvading = false;
        LastEndWasCompleted = completed;
        invincibleEndTime = perfectWindowEndTime = OverburstGameClock.UnscaledTime;

        if (completed && endedType == PlayerEvadeType.Roll)
            rollRotationRecoveryEndTime = OverburstGameClock.UnscaledTime + Mathf.Max(0f, rollExitRotationBlendDuration);

        // GOAL A2: Evading 요청을 해제한다. 이동 축은 Movement 보고로 복귀.
        if (stateCoordinator == null)
            stateCoordinator = ResolveStateCoordinator();
        if (stateCoordinator != null)
            stateCoordinator.ReleaseLocomotion(this);

        if (!completed || !followUp) { meleeRuntime?.CancelDodgeLightWindup(); meleeRuntime?.DiscardDodgeComboContinuation(); }
        playerAnimation?.FinishEvadeAnimation(endedType, completed);
        if (completed && endedType == PlayerEvadeType.ExplorationDodge)
            playerMovement?.CompleteExplorationEvade(ReadRawMoveInput());
        OnEvadeEnded?.Invoke(endedType);
        if (!completed || isEvading) return;
        if (TryExecuteBufferedEvade()) { meleeRuntime?.CancelDodgeLightWindup(); return; }
        if (followUp) { completedDodgeFollowUp = request; hasCompletedDodgeFollowUp = true; }
    }

    private void TriggerPerfectEvade(DamageInfo damageInfo)
    {
        perfectEvadeTriggered = true;
        OnPerfectEvade?.Invoke(activeType, damageInfo);
        OverburstFeelFeedbackHub.Request(OverburstFeelCue.Evade, transform.position, 1.5f);
        OverburstTimeEffectArbiter.Request(
            this,
            OverburstTimeEffectKind.PerfectEvade,
            Mathf.Clamp(perfectEvadeTimeScale, 0.05f, 1f),
            Mathf.Max(0f, perfectEvadeSlowDuration));
    }

#if UNITY_EDITOR
    private AnimationClip TryLoadEditorDefaultRollAnimationClip()
    {
        if (triedEditorDefaultRollAnimationClip)
            return null;

        triedEditorDefaultRollAnimationClip = true;
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(EditorDefaultRollAnimationClipPath);
    }
#endif

    private PlayerInputFacade ResolveFacade()
    {
        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();
        if (inputFacade == null)
            inputFacade = PlayerInputFacade.Current;
        return inputFacade;
    }

    private PlayerStateCoordinator ResolveStateCoordinator()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        return stateCoordinator;
    }
}
