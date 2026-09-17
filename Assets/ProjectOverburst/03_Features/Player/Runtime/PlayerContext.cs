using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerContext : MonoBehaviour
{
    private static PlayerContext instance;
    [SerializeField] private PlayerActorRuntime actor;

    public static PlayerContext Instance => instance;
    public PlayerActorRuntime CurrentActor => ResolveActor();
    public PlayerControlKit CurrentActorKit => CurrentActor != null ? CurrentActor.PlayerKit : null;
    public PlayerMovement CurrentActorMovement => CurrentActor != null ? CurrentActor.Movement : null;
    public PlayerEquipment CurrentActorEquipment => CurrentActor != null ? CurrentActor.Equipment : null;
    public CombatHealth CurrentActorHealth => CurrentActor != null ? CurrentActor.Health : null;
    public PlayerStaminaController CurrentActorStaminaController => CurrentActorKit != null ? CurrentActorKit.StaminaController : null;
    public PlayerBuffController CurrentActorBuffController => CurrentActorKit != null ? CurrentActorKit.BuffController : null;
    public PlayerInventory CurrentActorInventory => PlayerAccountInventoryService.SharedInventory != null
        ? PlayerAccountInventoryService.SharedInventory
        : CurrentActor != null ? CurrentActor.Inventory : null;

    public event Action<PlayerActorRuntime> CurrentActorChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
        ResolveActor();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static PlayerContext GetOrCreate()
    {
        if (instance != null)
            return instance;
        PlayerContext existing = FindFirstObjectByType<PlayerContext>(FindObjectsInactive.Include);
        if (existing != null)
        {
            instance = existing;
            return existing;
        }
        if (!Application.isPlaying)
            return null;
        GameObject owner = new GameObject("PlayerContext");
        DontDestroyOnLoad(owner);
        return owner.AddComponent<PlayerContext>();
    }

    public void Bind(PlayerActorRuntime value)
    {
        if (actor == value)
            return;
        actor = value;
        actor?.ResolveReferences();
        CurrentActorChanged?.Invoke(actor);
    }

    private PlayerActorRuntime ResolveActor()
    {
        if (actor != null && actor.isActiveAndEnabled)
            return actor;
        PlayerActorRuntime next = null;
        foreach (PlayerActorRuntime candidate in FindObjectsByType<PlayerActorRuntime>(FindObjectsSortMode.None))
        {
            if (candidate.ActorIndex == 0)
            {
                next = candidate;
                break;
            }
        }
        Bind(next);
        return actor;
    }
}
