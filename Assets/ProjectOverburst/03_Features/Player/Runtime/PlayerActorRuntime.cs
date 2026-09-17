using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerActorRuntime : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private int actorIndex;
    [SerializeField] private string actorId;
    [SerializeField] private string displayName;

    [Header("Core References")]
    [SerializeField] private PlayerControlKit playerKit;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private CombatHealth health;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerEquipment equipment;

    private CombatHealth subscribedHealth;
    private PlayerStateCoordinator stateCoordinator;

    public ActorControlAuthority Authority => playerKit != null ? playerKit.Authority : ActorControlAuthority.Disabled;
    public int ActorIndex => actorIndex;
    public string ActorId => actorId;
    public string DisplayName => displayName;
    public PlayerControlKit PlayerKit => playerKit;
    public CharacterController CharacterController => characterController;
    public PlayerMovement Movement => movement;
    public CombatHealth Health => health;
    public PlayerInventory Inventory => inventory != null ? inventory : PlayerAccountInventoryService.SharedInventory;
    public PlayerEquipment Equipment => equipment;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeHealth();
        playerKit?.SetIncapacitated(health != null && health.IsDead);
        ResolveStateCoordinator()?.BindActor(this);
        SyncDeathState();
    }

    private void OnDisable()
    {
        UnsubscribeHealth();
        // 비활성화된 액터가 Dead 요청을 남기지 않도록 명시 해제한다. 같은 GO 코디네이터에만 해제한다.
        ReleaseDeathState();
    }

    private void OnDestroy()
    {
        UnsubscribeHealth();
        ReleaseDeathState();
    }

    private void SubscribeHealth()
    {
        if (subscribedHealth == health)
            return;
        UnsubscribeHealth();
        subscribedHealth = health;
        if (subscribedHealth == null)
            return;
        subscribedHealth.OnDead += HandleDead;
        subscribedHealth.OnReset += HandleHealthReset;
    }

    private void UnsubscribeHealth()
    {
        if (subscribedHealth != null)
        {
            subscribedHealth.OnDead -= HandleDead;
            subscribedHealth.OnReset -= HandleHealthReset;
        }
        subscribedHealth = null;
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        playerKit?.SetIncapacitated(true);
        // GOAL A2: 사망을 Condition.Dead로 명시 연결한다.
        ResolveStateCoordinator()?.RequestDeath(this);
    }

    private void HandleHealthReset(CombatHealth source)
    {
        playerKit?.SetIncapacitated(source.IsDead);
        // GOAL A2: 리셋 시 Dead를 명시 해제한다. 차단 상태는 별도 소유로 유지된다.
        PlayerStateCoordinator coordinator = ResolveStateCoordinator();
        if (coordinator == null)
            return;
        if (source != null && source.IsDead)
            coordinator.RequestDeath(this);
        else
            coordinator.ReleaseDeath(this);
    }

    public void Initialize(int index, string id, string name)
    {
        actorIndex = Mathf.Max(0, index);
        actorId = string.IsNullOrWhiteSpace(id) ? "PlayerActor_" + (actorIndex + 1) : id;
        displayName = string.IsNullOrWhiteSpace(name) ? actorId : name;
        gameObject.name = actorId;
        ResolveReferences();
    }

    public void ResolveReferences()
    {
        if (playerKit == null)
            playerKit = GetComponent<PlayerControlKit>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (movement == null)
            movement = GetComponent<PlayerMovement>();

        if (health == null)
            health = GetComponent<CombatHealth>();

        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();

        if (inventory == null)
            inventory = PlayerAccountInventoryService.SharedInventory;

        if (equipment == null)
            equipment = GetComponent<PlayerEquipment>();

        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
    }

    private PlayerStateCoordinator ResolveStateCoordinator()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        return stateCoordinator;
    }

    private void SyncDeathState()
    {
        PlayerStateCoordinator coordinator = ResolveStateCoordinator();
        if (coordinator == null)
            return;
        if (health != null && health.IsDead)
            coordinator.RequestDeath(this);
        else
            coordinator.ReleaseDeath(this);
    }

    private void ReleaseDeathState()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator != null)
            stateCoordinator.ReleaseDeath(this);
    }
}
