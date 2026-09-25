using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerControlKit : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private CombatHealth health;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerEquipment equipment;
    [SerializeField] private WeaponRuntimeHub weaponRuntimeHub;

    [Header("Player Control")]
    [SerializeField] private PlayerMovementInputSource movementInputSource;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerStaminaController staminaController;
    [SerializeField] private PlayerEvadeController evadeController;
    [SerializeField] private PlayerAnimation playerAnimation;
    [SerializeField] private PlayerAimRotation aimRotation;
    [SerializeField] private PlayerLeftHandGrip leftHandGrip;
    [SerializeField] private PlayerBuffController buffController;

    [Header("Interaction")]
    [SerializeField] private PlayerPickupInteractor pickupInteractor;
    [SerializeField] private PlayerCurrencyAutoPickup currencyAutoPickup;
    [SerializeField] private PlayerStarterLoadout starterLoadout;

    [Header("Weapon Runtime")]
    [SerializeField] private MeleeRuntime meleeRuntime;
    [SerializeField] private MagicRuntime magicRuntime;

    [SerializeField] private ActorControlAuthority authority = ActorControlAuthority.Disabled;

    private bool incapacitated;
    private bool waitingForMeleeInputRelease;

    public CharacterController CharacterController => characterController;
    public CombatHealth Health => health;
    public PlayerInventory Inventory => inventory != null ? inventory : PlayerAccountInventoryService.SharedInventory;
    public PlayerEquipment Equipment => equipment;
    public WeaponRuntimeHub WeaponRuntimeHub => weaponRuntimeHub;
    public PlayerMovementInputSource MovementInputSource => movementInputSource;
    public PlayerMovement Movement => movement;
    public PlayerStaminaController StaminaController => staminaController;
    public PlayerEvadeController EvadeController => evadeController;
    public PlayerAnimation PlayerAnimation => playerAnimation;
    public PlayerAimRotation AimRotation => aimRotation;
    public PlayerLeftHandGrip LeftHandGrip => leftHandGrip;
    public PlayerBuffController BuffController => buffController;
    public PlayerPickupInteractor PickupInteractor => pickupInteractor;
    public PlayerCurrencyAutoPickup CurrencyAutoPickup => currencyAutoPickup;
    public PlayerStarterLoadout StarterLoadout => starterLoadout;
    public MeleeRuntime MeleeRuntime => meleeRuntime;
    public MagicRuntime MagicRuntime => magicRuntime;
    public ActorControlAuthority Authority => authority;
    public bool IsIncapacitated => incapacitated;
    public bool IsMeleeManualInputArmed => authority == ActorControlAuthority.Player
        && !incapacitated
        && !waitingForMeleeInputRelease;
    public bool IsMeleeAttackInputHeld
    {
        get
        {
            // GOAL A2: 좌클릭 홀드 직접 읽기 대신 Gameplay Attack 유지를 사용한다.
            // 클릭 release 억제와 melee combo handoff 의미는 유지.
            PlayerInputFacade facade = ResolveFacade();
            return !GameplayInputBlocker.IsGameplayInputBlocked
                && facade != null
                && facade.AttackHeld;
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (!waitingForMeleeInputRelease
            || authority != ActorControlAuthority.Player
            || incapacitated
            || (meleeRuntime != null && meleeRuntime.IsAttackInProgress))
        {
            return;
        }

        // GOAL A2: 권한 진입 시 실제 release까지 대기한다. 홀드 감각 유지.
        PlayerInputFacade facade = ResolveFacade();
        if (facade != null && facade.AttackHeld)
            return;

        waitingForMeleeInputRelease = false;
        SetMeleeManualInputEnabled(true);
    }

    public void ResolveReferences()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (health == null)
            health = GetComponent<CombatHealth>();

        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();

        if (equipment == null)
            equipment = GetComponent<PlayerEquipment>();

        if (weaponRuntimeHub == null)
            weaponRuntimeHub = GetComponent<WeaponRuntimeHub>();

        if (movementInputSource == null)
            movementInputSource = GetComponent<PlayerMovementInputSource>();

        if (movement == null)
            movement = GetComponent<PlayerMovement>();

        movement?.BindInputSource(movementInputSource);

        if (staminaController == null)
            staminaController = GetComponent<PlayerStaminaController>();

        if (evadeController == null)
            evadeController = GetComponent<PlayerEvadeController>();

        if (playerAnimation == null)
            playerAnimation = GetComponent<PlayerAnimation>();

        if (aimRotation == null)
            aimRotation = GetComponent<PlayerAimRotation>();

        if (leftHandGrip == null)
            leftHandGrip = GetComponent<PlayerLeftHandGrip>();

        if (buffController == null)
            buffController = GetComponent<PlayerBuffController>();

        if (pickupInteractor == null)
            pickupInteractor = GetComponent<PlayerPickupInteractor>();

        if (currencyAutoPickup == null)
            currencyAutoPickup = GetComponent<PlayerCurrencyAutoPickup>();

        if (starterLoadout == null)
            starterLoadout = GetComponent<PlayerStarterLoadout>();

        if (meleeRuntime == null)
            meleeRuntime = GetComponent<MeleeRuntime>();

        if (magicRuntime == null)
            magicRuntime = GetComponent<MagicRuntime>();

        if (weaponRuntimeHub != null)
            weaponRuntimeHub.ResolveControllers();
    }

    public void ApplyAuthority(ActorControlAuthority nextAuthority)
    {
        ResolveReferences();
        ActorControlAuthority previousAuthority = authority;
        authority = nextAuthority;

        bool active = authority != ActorControlAuthority.Disabled;
        bool controllable = active && !incapacitated;
        bool player = authority == ActorControlAuthority.Player && !incapacitated;

        movement?.SetControlAuthority(controllable
            ? authority
            : ActorControlAuthority.Disabled);

        SetEnabled(health, active);
        SetEnabled(inventory, active);
        SetEnabled(equipment, active);
        SetEnabled(staminaController, false);
        SetEnabled(buffController, active);
        SetEnabled(movementInputSource, player);
        SetEnabled(movement, controllable);
        SetEnabled(evadeController, player);
        SetEnabled(playerAnimation, active);
        SetEnabled(aimRotation, player);
        SetEnabled(leftHandGrip, active);
        SetEnabled(pickupInteractor, player);
        SetEnabled(currencyAutoPickup, player);
        SetEnabled(starterLoadout, player);

        bool enteringPlayer = player && previousAuthority != ActorControlAuthority.Player;
        bool activeMeleeAction = meleeRuntime != null && meleeRuntime.IsAttackInProgress;
        bool attackInputHeld = IsMeleeAttackInputHeld;
        waitingForMeleeInputRelease = player
            && (enteringPlayer || activeMeleeAction || attackInputHeld);
        SetMeleeManualInputEnabled(player && !waitingForMeleeInputRelease);
        SetMagicManualInputEnabled(player);
        SetEnabled(meleeRuntime, active);
        SetEnabled(magicRuntime, active);
    }

    public bool TryAcceptMeleeComboHandoff()
    {
        ResolveReferences();
        if (authority != ActorControlAuthority.Player
            || incapacitated
            || meleeRuntime == null
            || !meleeRuntime.TryArmPlayerComboHandoff())
        {
            return false;
        }

        waitingForMeleeInputRelease = false;
        SetMeleeManualInputEnabled(true);
        return true;
    }

    public void CancelCurrentActions(WeaponActionCancelReason reason)
    {
        meleeRuntime?.CancelCurrentAction(reason);
        magicRuntime?.CancelCurrentAction();

        if (authority != ActorControlAuthority.Player)
            return;

        waitingForMeleeInputRelease = IsMeleeAttackInputHeld;
        SetMeleeManualInputEnabled(!waitingForMeleeInputRelease && !incapacitated);
    }

    public void RequireMeleeInputRelease()
    {
        if (authority != ActorControlAuthority.Player)
            return;

        waitingForMeleeInputRelease = true;
        SetMeleeManualInputEnabled(false);
    }

    public void SetIncapacitated(bool value)
    {
        if (incapacitated == value)
            return;

        incapacitated = value;
        if (incapacitated)
            CancelCurrentActions(WeaponActionCancelReason.Death);
        ApplyAuthority(authority);
        if (incapacitated)
            movement?.Stop();
    }

    private void SetMeleeManualInputEnabled(bool enabledValue)
    {
        if (meleeRuntime != null)
            meleeRuntime.SetManualInputEnabled(enabledValue);
    }

    private void SetMagicManualInputEnabled(bool enabledValue)
    {
        if (magicRuntime != null)
            magicRuntime.SetManualInputEnabled(enabledValue);
    }

    private static void SetEnabled(Behaviour behaviour, bool enabledValue)
    {
        if (behaviour != null)
            behaviour.enabled = enabledValue;
    }

    private PlayerInputFacade inputFacade;

    private PlayerInputFacade ResolveFacade()
    {
        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();
        if (inputFacade == null)
            inputFacade = PlayerInputFacade.Current;
        return inputFacade;
    }
}
