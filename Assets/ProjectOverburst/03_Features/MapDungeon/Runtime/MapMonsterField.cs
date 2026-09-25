using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapMonsterField : MonoBehaviour
{
    private const float TriggerDistance = 17f;
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
        int[] small = { 5, 6, 7 };
        int[] medium = { 1, 2, 3 };
        int[] elite = { 0, 1, 2 };
        int mediumCount = medium[band];
        int eliteCount = elite[band];
        if (MapOptionPolicy.Value(map, MapOptionPolicy.MoreMediumElite) > 0f)
        {
            mediumCount++;
            if (band > 0) eliteCount++;
        }
        var roster = theme.BuildRoster(small[band], mediumCount, eliteCount, seed);
        var random = new System.Random(seed);
        for (int i = 0; i < roster.Count; i++)
        {
            if (!TryPosition(random, out Vector3 position)) continue;
            var direction = player.position - position;
            direction.y = 0f;
            var rotation = direction.sqrMagnitude > .01f
                ? Quaternion.LookRotation(direction.normalized) : Quaternion.identity;
            var request = new EnemySpawnRequest(roster[i], position, rotation, player,
                gameObject, transform, transform, 1f, 1f, seed + i, context);
            if (!service.TrySpawn(request, out var actor)) continue;
            var tier = i < small[band] ? EnemyThemeTier.Small
                : i < small[band] + mediumCount ? EnemyThemeTier.Medium : EnemyThemeTier.Elite;
            living.Add(actor.Health, tier);
            actor.Health.OnDead += HandleDead;
            SpawnedCount++;
        }
        if (SpawnedCount != roster.Count)
            Debug.LogWarning($"[MapMonsterField] {name}: {SpawnedCount}/{roster.Count} 소환", this);
        triggered = SpawnedCount > 0;
        if (!triggered) nextCheck = Time.time + 1f;
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
        foreach (var pair in living)
            if (pair.Key != null) pair.Key.OnDead -= HandleDead;
        living.Clear();
    }
}
