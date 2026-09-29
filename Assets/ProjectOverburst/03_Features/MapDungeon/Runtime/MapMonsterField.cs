using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapMonsterField : MonoBehaviour
{
    private const float TriggerDistance = 25f;
    private MapSpawnBudget budget;
    public bool IsSpawning { get; private set; }
    private readonly Dictionary<CombatHealth, EnemyThemeTier> living =
        new Dictionary<CombatHealth, EnemyThemeTier>();
    private EnemyThemeTable theme;
    private EnemySpawnService service;
    private EncounterContext context;
    private MapInstanceState map;
    private MapItemData mapDefinition;
    private AccountContentRegistry registry;
    private int band;
    private int seed;
    private bool triggered;
    private float nextCheck;

    public bool HasTriggered => triggered;
    public int Band => band;
    public int SpawnedCount { get; private set; }
    public int RemainingCount => living.Count;

    public void Configure(EnemyThemeTable selectedTheme, EnemySpawnService spawnService,
        EncounterContext encounter, MapInstanceState activeMap, MapItemData definition,
        AccountContentRegistry contentRegistry, int progressBand, int randomSeed)
    {
        theme = selectedTheme;
        service = spawnService;
        budget = service.GetComponent<MapSpawnBudget>();
        context = encounter;
        map = activeMap;
        mapDefinition = definition;
        registry = contentRegistry;
        band = Mathf.Clamp(progressBand, 0, 2);
        seed = randomSeed;
    }

    private void Update()
    {
        if (triggered || Time.time < nextCheck || WorldSessionState.Phase != WorldPhase.Run)
            return;
        nextCheck = Time.time + .25f;
        Transform player = PlayerContext.Instance?.CurrentActor?.transform;
        if (player == null) return;
        Vector3 delta = player.position - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude <= TriggerDistance * TriggerDistance) SpawnOnce(player);
    }

    public bool SpawnOnce(Transform player)
    {
        if (triggered || theme == null || service == null || context == null || player == null
            || !context.CanGrantRewards) return false;
        if (budget == null) return false;
        // Two minimum-30 waves establish a real crowd even in the first level band.
        var roster = MapSpawnPolicy.Roster(theme, map, band, seed);
        roster.AddRange(MapSpawnPolicy.Roster(theme, map, band, seed + 1));
        var random = new System.Random(seed);
        var requests = new List<EnemySpawnRequest>(roster.Count);
        foreach (var definition in roster)
        {
            if (!TryPosition(random, out Vector3 position)) return false;
            Vector3 direction = player.position - position; direction.y = 0;
            requests.Add(new EnemySpawnRequest(definition, position,
                direction.sqrMagnitude > .01f ? Quaternion.LookRotation(direction) : Quaternion.identity,
                player, gameObject, transform, transform, 1, 1, random.Next(), context));
        }
        IsSpawning = triggered = budget.Enqueue(this, requests, (actor, index) =>
        {
            living.Add(actor.Health, MapSpawnPolicy.Tier(theme, roster[index]));
            actor.Health.OnDead += HandleDead;
            SpawnedCount++;
        }, count =>
        {
            IsSpawning = false;
            if (count == 0) { triggered = false; nextCheck = Time.time + 1; }
        });
        return triggered;
    }

    private bool TryPosition(System.Random random, out Vector3 position)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int mask = groundLayer >= 0 ? 1 << groundLayer : Physics.DefaultRaycastLayers;
        for (int attempt = 0; attempt < 35; attempt++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = 4f + (float)random.NextDouble() * 9f;
            var candidate = transform.position + new Vector3(
                Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            if (!DiamondDungeonLayout.Contains(candidate, 6f)) continue;
            if (!Physics.Raycast(candidate + Vector3.up * 6f, Vector3.down,
                out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore)) continue;
            position = hit.point + Vector3.up * .06f;
            return true;
        }
        position = default;
        return false;
    }

    private void HandleDead(CombatHealth health, DamageInfo info)
    {
        if (!living.TryGetValue(health, out EnemyThemeTier tier)) return;
        living.Remove(health);
        health.OnDead -= HandleDead;
        if (!context.CanGrantRewards || info.source == null
            || info.source.GetComponentInParent<PlayerActorRuntime>() == null) return;
        var mapItem = MapDropPolicy.Roll(mapDefinition, registry, map.level, tier, context.RunId);
        if (mapItem == null) return;
        WorldItemDropFactory.CreateWorldPickup(mapItem, health.transform.position + Vector3.up * .4f,
            PlayerAccountInventoryService.SharedInventory, PlayerContext.Instance?.CurrentActor?.transform);
    }

    private void OnDisable()
    {
        if (budget != null) budget.Cancel(this);
        IsSpawning = false;
        foreach (var pair in living)
            if (pair.Key != null) pair.Key.OnDead -= HandleDead;
        living.Clear();
    }
}
