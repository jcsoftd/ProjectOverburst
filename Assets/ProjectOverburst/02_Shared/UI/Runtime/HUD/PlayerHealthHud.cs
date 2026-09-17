using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerHealthHud : MonoBehaviour
{
    [SerializeField] private PlayerContext playerContext;
    [SerializeField] private PlayerHealthView view;
    private PlayerContext subscribedContext;
    private CombatHealth subscribedHealth;

    private void OnEnable()
    {
        ResolveView();
        Bind(playerContext != null ? playerContext : PlayerContext.GetOrCreate());
    }

    private void OnDisable()
    {
        if (subscribedContext != null)
            subscribedContext.CurrentActorChanged -= HandleActorChanged;
        subscribedContext = null;
        BindHealth(null);
    }

    private void Update()
    {
        if (playerContext == null)
            Bind(PlayerContext.GetOrCreate());
        // Actor creation can follow the persistent UI's OnEnable.
        CombatHealth current = playerContext != null ? playerContext.CurrentActorHealth : null;
        if (current != subscribedHealth)
            HandleActorChanged(playerContext != null ? playerContext.CurrentActor : null);
    }

    public void Bind(PlayerContext context)
    {
        if (subscribedContext != null)
            subscribedContext.CurrentActorChanged -= HandleActorChanged;
        playerContext = context;
        subscribedContext = isActiveAndEnabled ? context : null;
        if (subscribedContext != null)
            subscribedContext.CurrentActorChanged += HandleActorChanged;
        ResolveView();
        HandleActorChanged(context != null ? context.CurrentActor : null);
    }

    private void ResolveView()
    {
        if (view == null)
            view = GetComponentInChildren<PlayerHealthView>(true);
    }

    private void HandleActorChanged(PlayerActorRuntime actor)
    {
        BindHealth(actor != null ? actor.Health : null);
        if (view != null)
        {
            view.Initialize(actor != null ? actor.DisplayName : null);
            view.Refresh(subscribedHealth);
        }
    }

    private void BindHealth(CombatHealth health)
    {
        if (subscribedHealth == health)
            return;
        if (subscribedHealth != null)
            subscribedHealth.OnHealthChanged -= HandleHealthChanged;
        subscribedHealth = health;
        if (subscribedHealth != null)
            subscribedHealth.OnHealthChanged += HandleHealthChanged;
    }

    private void HandleHealthChanged(CombatHealth source, float currentHp, float maxHp)
    {
        if (view != null)
            view.Refresh(source);
    }
}
