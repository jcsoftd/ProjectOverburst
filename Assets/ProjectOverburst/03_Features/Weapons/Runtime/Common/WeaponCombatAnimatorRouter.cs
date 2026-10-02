using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(370)]
[DisallowMultipleComponent]
public class WeaponCombatAnimatorRouter : MonoBehaviour
{
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerEquipment playerEquipment;
    [FormerlySerializedAs("oneHandSwordDriver")]
    [SerializeField] private MeleeWeaponCombatAnimatorDriver meleeWeaponDriver;

    private PlayerCombatModeController combatModeController;
    private IWeaponCombatAnimatorDriver activeDriver;
    private bool knockdownSuspended;

    public bool HasActiveCombatDriver => activeDriver != null && activeDriver.IsAvailable;
    public bool CanApplyStationaryFootIk => activeDriver != null && activeDriver.CanApplyStationaryFootIk;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeCombatModeController();
    }

    private void OnDisable()
    {
        UnsubscribeCombatModeController();
    }

    private void Update()
    {
        if (knockdownSuspended) return;
        ResolveReferences();
        RefreshActiveDriverForCurrentWeapon();
        activeDriver?.Tick(Time.deltaTime);
    }

    public bool TryPlayCombatJump()
    {
        if (knockdownSuspended) return false;
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayJump();
    }

    public bool TryPlayCombatHit()
    {
        if (knockdownSuspended) return false;
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayHit();
    }

    public bool TryPlayCombatRoll(float actionDuration)
    {
        if (knockdownSuspended) return false;
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayRoll(actionDuration);
    }

    public bool TryPlayMeleeGuardBlock()
    {
        if (knockdownSuspended) return false;
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayGuardBlock();
    }

    public bool TryPlayCombatAttack(
        int stepIndex,
        AnimationClip expectedClip,
        float actionDuration,
        float transitionDuration,
        bool allowCombatEntry,
        float normalizedStartTime = 0f, MeleePlaybackAcceleration playbackAcceleration = default)
    {
        if (knockdownSuspended) return false;
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null
            && activeDriver.TryPlayAttack(
                stepIndex,
                expectedClip,
                actionDuration,
                transitionDuration,
                allowCombatEntry,
                normalizedStartTime, playbackAcceleration);
    }

    public bool TryBlendDodgeLightRecoveryToLocomotion(float transitionDuration)
    {
        if (knockdownSuspended) return false;
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver == meleeWeaponDriver && meleeWeaponDriver != null
            && meleeWeaponDriver.BlendDodgeLightRecoveryToLocomotion(transitionDuration);
    }

    public void CancelCombatAttack()
    {
        RefreshActiveDriverForCurrentWeapon();
        activeDriver?.CancelAttack();
    }

    public bool TryPlayCombatDodge(AnimationClip clip, string stateName, float duration, float entryBlend, float exitBlend)
    {
        if (knockdownSuspended) return false;
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayDodge(clip, stateName, duration, entryBlend, exitBlend);
    }

    public void FinishCombatEvade() => activeDriver?.FinishEvade();

    public bool IsHeavyParryClipComplete => activeDriver != null && activeDriver.IsHeavyParryClipComplete;

    public bool TryPlayHeavyParry(out float duration, out float bridgeDuration, out float contactDelay, out float heavyStartSeconds)
    {
        RefreshActiveDriverForCurrentWeapon();
        duration = bridgeDuration = contactDelay = heavyStartSeconds = 0f;
        return activeDriver != null && activeDriver.TryPlayHeavyParry(out duration, out bridgeDuration, out contactDelay, out heavyStartSeconds);
    }

    public bool TryBlendHeavyAfterParry(AnimationClip clip, float duration, float normalizedStart,
        MeleePlaybackAcceleration acceleration)
        => activeDriver != null && activeDriver.TryBlendHeavyAfterParry(clip, duration, normalizedStart, acceleration);

    public void CompleteHeavyParryBridge() => activeDriver?.CompleteHeavyParryBridge();

    public void SuppressCombatLayerForLegacyAction(float duration)
    {
        RefreshActiveDriverForCurrentWeapon();
        activeDriver?.SuppressForLegacyFullBodyAction(duration);
    }

    public void ForceResetCombatLayers()
    {
        meleeWeaponDriver?.ForceResetLayer();
        activeDriver = null;
    }

    public void SuspendForKnockdown(bool suspend)
    {
        knockdownSuspended = suspend;
        if (suspend)
        {
            activeDriver?.CancelAttack();
            activeDriver?.SuppressForLegacyFullBodyAction(.01f);
            if (targetAnimator != null)
            {
                int combat = targetAnimator.GetLayerIndex("Combat_MeleeWeapon");
                int lower = targetAnimator.GetLayerIndex("Combat_MeleeWeapon_TransitionLower");
                if (combat >= 0) targetAnimator.SetLayerWeight(combat, 0);
                if (lower >= 0) targetAnimator.SetLayerWeight(lower, 0);
            }
        }
        else
        {
            RefreshActiveDriverForCurrentWeapon();
            meleeWeaponDriver?.ResumeAfterKnockdown(PlayerCombatModeController.IsSharedCombatModeActive());
        }
    }

    private void ResolveReferences()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>(true);

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        if (meleeWeaponDriver == null)
            meleeWeaponDriver = GetComponent<MeleeWeaponCombatAnimatorDriver>();

        meleeWeaponDriver?.Initialize(targetAnimator, playerMovement, playerEquipment);
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
        if (knockdownSuspended) return;
        RefreshActiveDriverForCurrentWeapon();

        if (state == PlayerCombatModeState.Combat)
            activeDriver?.EnterCombat(reason);
        else
            activeDriver?.ExitCombat(reason);
    }

    private void RefreshActiveDriverForCurrentWeapon()
    {
        WeaponCombatStyle style = ResolveCurrentCombatStyle();
        IWeaponCombatAnimatorDriver nextDriver = ResolveDriver(style);

        if (nextDriver == activeDriver)
            return;

        if (activeDriver != null && PlayerCombatModeController.IsSharedCombatModeActive())
            activeDriver.ExitCombat(PlayerCombatModeReason.System);

        activeDriver = nextDriver;

        if (activeDriver != null && PlayerCombatModeController.IsSharedCombatModeActive())
            activeDriver.EnterCombat(PlayerCombatModeReason.System);
    }

    private WeaponCombatStyle ResolveCurrentCombatStyle()
    {
        WeaponItemData weaponData = playerEquipment != null ? playerEquipment.CurrentWeaponData : null;
        if (weaponData == null)
            return WeaponCombatStyle.None;

        return weaponData.GetResolvedCombatStyle();
    }

    private IWeaponCombatAnimatorDriver ResolveDriver(WeaponCombatStyle style)
    {
        switch (style)
        {
            case WeaponCombatStyle.MeleeWeapon:
                return meleeWeaponDriver;
            default:
                return null;
        }
    }
}

public interface IWeaponCombatAnimatorDriver
{
    bool IsAvailable { get; }
    bool CanApplyStationaryFootIk { get; }
    void EnterCombat(PlayerCombatModeReason reason);
    void ExitCombat(PlayerCombatModeReason reason);
    void Tick(float deltaTime);
    bool TryPlayJump();
    bool TryPlayHit();
    bool TryPlayRoll(float actionDuration);
    bool TryPlayDodge(AnimationClip clip, string stateName, float actionDuration, float entryBlend, float exitBlend);
    void FinishEvade();
    bool TryPlayGuardBlock();
    bool TryPlayAttack(
        int stepIndex,
        AnimationClip expectedClip,
        float actionDuration,
        float transitionDuration,
        bool allowCombatEntry,
        float normalizedStartTime = 0f, MeleePlaybackAcceleration playbackAcceleration = default);
    void CancelAttack();
    bool IsHeavyParryClipComplete { get; }
    bool TryPlayHeavyParry(out float duration, out float bridgeDuration, out float contactDelay, out float heavyStartSeconds);
    bool TryBlendHeavyAfterParry(AnimationClip clip, float duration, float normalizedStart, MeleePlaybackAcceleration acceleration);
    void CompleteHeavyParryBridge();
    void SuppressForLegacyFullBodyAction(float duration);
}
