using System;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum PlayerEvadeType // 회피 종류
{
    Dash,
    Roll
}

[DefaultExecutionOrder(280)]
public class PlayerEvadeController : MonoBehaviour // Dash / Roll 회피
{
#if UNITY_EDITOR
    private const string EditorDefaultRollAnimationClipPath = "Assets/ProjectOverburst/03_Features/Player/Animations/Roll/InPlace/RM_Roll_front_InPlace.anim";
#endif
    [Header("Common")]
    [SerializeField] private float staminaCost = 10f;
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
    private PlayerStaminaController staminaController;
    private bool isEvading;
    private bool perfectEvadeTriggered;
    private PlayerEvadeType activeType;
    private Vector3 activeDirection;
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

    public bool IsInvincible
    {
        get { return Time.unscaledTime < invincibleEndTime; }
    }

    public bool IsPerfectEvadeWindowActive
    {
        get { return Time.unscaledTime < perfectWindowEndTime; }
    }

    public PlayerEvadeType ActiveType
    {
        get { return activeType; }
    }

    public float RotationRecoverySpeedMultiplier
    {
        get
        {
            if (Time.unscaledTime >= rollRotationRecoveryEndTime)
                return 1f;

            float duration = Mathf.Max(0.001f, rollExitRotationBlendDuration);
            float remaining01 = Mathf.Clamp01((rollRotationRecoveryEndTime - Time.unscaledTime) / duration);
            return Mathf.Lerp(1f, Mathf.Clamp01(rollExitRotationSpeedMultiplier), remaining01);
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        ResolveFacade()?.CombatInputs?.Invalidate();
        OverburstTimeEffectArbiter.ClearOwner(this);
        if (isEvading)
            EndEvade();
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
        ReadEvadeInput();
        UpdateEvadeMotion(Time.unscaledDeltaTime);
    }

    private void LateUpdate()
    {
        MaintainEvadeDirection(Time.unscaledDeltaTime);
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

        if (staminaController == null)
            staminaController = GetComponent<PlayerStaminaController>();

        if (playerAnimation == null)
            playerAnimation = GetComponent<PlayerAnimation>();

        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();

        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();

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
        PlayerInputFacade facade = ResolveFacade();
        if (facade == null || facade.CombatInputs == null || !facade.CombatInputs.HasEvade)
            return false;

        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        if (isEvading || Time.unscaledTime < nextEvadeTime)
            return false;

        if (playerMovement != null && playerMovement.IsMeleeAttackMoveLocked && !CanCancelMeleeComboForEvade())
            return false; // 이동 잠금 중에는 MeleeRuntime 회피 취소 계약을 따른다.

        if (!CanStartRollInCurrentMode())
            return false;

        if (!TryStartEvade(PlayerEvadeType.Roll)) return false;
        facade.CombatInputs.ConsumeEvade();
        return true;
    }

    private bool CanStartRollInCurrentMode()
    {
        return playerMovement != null && playerMovement.IsMeleeCombatLocomotionMode;
    }

    private bool TryStartEvade(PlayerEvadeType evadeType)
    {
        if (staminaController == null || !staminaController.TryConsume(staminaCost))
            return false;

        meleeRuntime?.CancelActiveComboForEvade(); // 공격 취소와 콤보 연결 상태 초기화는 10번대 위임

        activeType = evadeType;
        activeDirection = ResolveEvadeDirection();
        activeDirection.y = 0f;
        activeDirection = activeDirection.sqrMagnitude > 0.001f ? activeDirection.normalized : transform.forward;

        if (activeDirection.sqrMagnitude <= 0.001f)
            activeDirection = Vector3.forward;

        if (evadeType == PlayerEvadeType.Roll)
            ConfigureActiveEvade(rollDistance, ResolveRollDuration(), rollInvincibleDuration, rollPerfectWindow);
        else
            ConfigureActiveEvade(dashDistance, dashDuration, dashInvincibleDuration, dashPerfectWindow);

        isEvading = true;
        perfectEvadeTriggered = false;
        activeElapsed = 0f;
        activeMovedDistance = 0f;
        evadeStartTime = Time.unscaledTime;
        evadeEndTime = evadeStartTime + activeDuration;
        nextEvadeTime = evadeStartTime + Mathf.Max(0f, cooldown);
        rollRotationRecoveryEndTime = 0f;
        // GOAL A2: 회피 구간 Locomotion.Evading을 명시 요청한다.
        ResolveStateCoordinator()?.RequestLocomotion(this, PlayerLocomotionState.Evading);
        MaintainEvadeDirection(Time.unscaledDeltaTime);

        playerMovement?.PrepareEvadeMotion();
        PlayEvadeAnimation(activeType, activeDuration);
        OnEvadeStarted?.Invoke(activeType);
        OverburstFeelFeedbackHub.Request(OverburstFeelCue.Evade, transform.position);
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

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        float blend = 1f - Mathf.Exp(-Mathf.Max(0f, rollDirectionBlendSpeed) * Mathf.Max(0f, deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Mathf.Clamp01(blend));
    }

    private void MaintainEvadeDirection(float deltaTime)
    {
        if (!isEvading || activeType != PlayerEvadeType.Roll)
            return;

        RotateToEvadeDirection(deltaTime);
    }

    private void PlayEvadeAnimation(PlayerEvadeType evadeType, float duration)
    {
        if (playerAnimation == null || evadeType != PlayerEvadeType.Roll)
            return;

        AnimationClip clip = rollAnimationClip;
#if UNITY_EDITOR
        if (clip == null)
            clip = TryLoadEditorDefaultRollAnimationClip();
#endif

        playerAnimation.PlayEvadeFullBody(clip, duration, evadeAnimationTransitionDuration);
    }

    private float ResolveRollDuration()
    {
        return rollDuration;
    }

    private void ConfigureActiveEvade(float distance, float duration, float invincibleDuration, float perfectWindow)
    {
        activeDistance = Mathf.Max(0f, distance);
        activeDuration = Mathf.Max(0.01f, duration);
        invincibleEndTime = Time.unscaledTime + Mathf.Max(0f, invincibleDuration);
        perfectWindowEndTime = Time.unscaledTime + Mathf.Max(0f, perfectWindow);
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

        if (combatMotion == null)
        {
            EndEvade();
            return;
        }

        activeElapsed += Mathf.Max(0f, deltaTime);
        float progress = Mathf.Clamp01(activeElapsed / activeDuration);
        float targetDistance = activeDistance * GetDistanceProgress(progress);
        float moveDistance = Mathf.Max(0f, targetDistance - activeMovedDistance);
        activeMovedDistance = targetDistance;

        if (moveDistance > 0f)
        {
            Vector3 displacement = activeDirection * moveDistance;
            combatMotion.ApplyEvadeDisplacement(displacement);
        }

        if (Time.unscaledTime >= evadeEndTime || progress >= 1f)
            EndEvade();
    }

    private float GetDistanceProgress(float progress)
    {
        if (activeType != PlayerEvadeType.Roll)
            return progress;

        float ease = Mathf.Clamp01(rollMoveEase);
        if (ease <= 0f)
            return progress;

        float smoothProgress = progress * progress * (3f - 2f * progress);
        return Mathf.Lerp(progress, smoothProgress, ease);
    }

    private void EndEvade()
    {
        if (!isEvading)
            return;

        PlayerEvadeType endedType = activeType;
        isEvading = false;

        if (endedType == PlayerEvadeType.Roll)
            rollRotationRecoveryEndTime = Time.unscaledTime + Mathf.Max(0f, rollExitRotationBlendDuration);

        // GOAL A2: Evading 요청을 해제한다. 이동 축은 Movement 보고로 복귀.
        if (stateCoordinator == null)
            stateCoordinator = ResolveStateCoordinator();
        if (stateCoordinator != null)
            stateCoordinator.ReleaseLocomotion(this);

        OnEvadeEnded?.Invoke(endedType);
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
