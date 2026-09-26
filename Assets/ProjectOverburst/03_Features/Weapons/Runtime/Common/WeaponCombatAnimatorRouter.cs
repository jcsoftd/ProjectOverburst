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
        ResolveReferences();
        RefreshActiveDriverForCurrentWeapon();
        activeDriver?.Tick(Time.deltaTime);
    }

    public bool TryPlayCombatJump()
    {
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayJump();
    }

    public bool TryPlayCombatHit()
    {
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayHit();
    }

    public bool TryPlayCombatRoll(float actionDuration)
    {
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayRoll(actionDuration);
    }

    public bool TryPlayMeleeGuardBlock()
    {
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null && activeDriver.TryPlayGuardBlock();
    }

    public bool TryPlayCombatAttack(
        int stepIndex,
        AnimationClip expectedClip,
        float actionDuration,
        float transitionDuration,
        bool allowCombatEntry,
        float normalizedStartTime = 0f)
    {
        RefreshActiveDriverForCurrentWeapon();
        return activeDriver != null
            && activeDriver.TryPlayAttack(
                stepIndex,
                expectedClip,
                actionDuration,
                transitionDuration,
                allowCombatEntry,
                normalizedStartTime);
    }

    public void CancelCombatAttack()
    {
        RefreshActiveDriverForCurrentWeapon();
        activeDriver?.CancelAttack();
    }

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
    bool TryPlayGuardBlock();
    bool TryPlayAttack(
        int stepIndex,
        AnimationClip expectedClip,
        float actionDuration,
        float transitionDuration,
        bool allowCombatEntry,
        float normalizedStartTime = 0f);
    void CancelAttack();
    void SuppressForLegacyFullBodyAction(float duration);
}
