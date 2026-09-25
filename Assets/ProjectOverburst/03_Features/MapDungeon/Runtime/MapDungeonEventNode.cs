using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

public enum MapEventKind { Hunt, Guard }
public enum MapEventPhase { Dormant, Active, Complete, Failed, Claimed }

[DisallowMultipleComponent]
public sealed class MapDungeonEventNode : MonoBehaviour, IInteractable
{
    private const float ActivationRadius = 8f;
    private const float GuardSeconds = 45f;
    private readonly Dictionary<CombatHealth, EnemyThemeTier> living =
        new Dictionary<CombatHealth, EnemyThemeTier>();
    private EnemyThemeTable theme;
    private EnemySpawnService spawnService;
    private EncounterContext encounter;
    private MapItemData mapDefinition;
    private AccountContentRegistry registry;
    private System.Random random;
    private string eventId;
    private int band;
    private int wave;
    private float elapsed;
    private float nextCheck;
    private float nextGuardPressure;
    private float nextRunCheck;
    private bool mayRun;
    private CombatHealth guardHealth;
    private GameObject activationBoundary;
    private GameObject rewardChestVisual;
    private MapCardOffer offer;

    public MapEventKind Kind { get; private set; }
    public MapEventPhase Phase { get; private set; } = MapEventPhase.Dormant;
    public string EventId => eventId;
    public int Band => band;
    public int Wave => wave + 1;
    public int LivingCount => living.Count;
    public float GuardHealthFraction => guardHealth != null ? guardHealth.NormalizedHp : 0f;
    public float GuardRemaining => Kind == MapEventKind.Guard && Phase == MapEventPhase.Active
        ? Mathf.Max(0f, GuardSeconds - elapsed) : 0f;
    public MapCardOffer Offer => offer;
    public event Action<MapDungeonEventNode> ChestRequested;
    public event Action<MapDungeonEventNode> StateChanged;

    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 240;
    public string InteractionPrompt => "F : 이벤트 상자 열기";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => 3.1f;
    public string StableInteractionId => eventId;
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => Phase == MapEventPhase.Complete;

    public void Configure(string id, MapEventKind kind, int progressBand, int seed,
        EnemyThemeTable selectedTheme, EnemySpawnService service, EncounterContext context,
        MapItemData definition, AccountContentRegistry contentRegistry,
        int mapLevel, int playerLevel, Material accent)
    {
        eventId = id;
        Kind = kind;
        band = Mathf.Clamp(progressBand, 0, 2);
        theme = selectedTheme;
        spawnService = service;
        encounter = context;
        mapDefinition = definition;
        registry = contentRegistry;
        random = new System.Random(seed);
        offer = MapRunCardPolicy.Roll(random, mapLevel, playerLevel);
        BuildVisual(accent, mapLevel);
        InteractionRegistry.Register(this);
        StateChanged?.Invoke(this);
    }

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(eventId)) InteractionRegistry.Register(this);
        if (guardHealth != null) guardHealth.OnDead += HandleGuardDead;
    }
    private void OnDisable()
    {
        InteractionRegistry.Unregister(this);
        foreach (var pair in living)
            if (pair.Key != null) pair.Key.OnDead -= HandleEnemyDead;
        living.Clear();
        if (guardHealth != null) guardHealth.OnDead -= HandleGuardDead;
    }

    private void Update()
    {
        if (WorldSessionState.Phase != WorldPhase.Run) return;
        if (Time.time >= nextRunCheck)
        {
            nextRunCheck = Time.time + .25f;
            mayRun = encounter != null && encounter.CanGrantRewards;
        }
        if (!mayRun) return;
        if (Phase == MapEventPhase.Dormant && Time.time >= nextCheck)
        {
            nextCheck = Time.time + .2f;
            Transform player = PlayerContext.Instance?.CurrentActor?.transform;
            if (player == null) return;
            Vector3 delta = player.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= ActivationRadius * ActivationRadius) Activate(player);
        }
        else if (Phase == MapEventPhase.Active && Kind == MapEventKind.Hunt
            && wave == 0 && living.Count == 0 && Time.time >= nextCheck)
        {
            nextCheck = Time.time + 1f;
            Transform player = PlayerContext.Instance?.CurrentActor?.transform;
            if (player != null && SpawnWave(player)) wave = 1;
        }
        else if (Phase == MapEventPhase.Active && Kind == MapEventKind.Guard)
        {
            if (guardHealth == null || guardHealth.IsDead) { Fail(); return; }
            elapsed += Time.deltaTime;
            if (wave < 2 && elapsed >= (wave + 1) * 15f && Time.time >= nextCheck)
            {
                nextCheck = Time.time + 1f;
                if (SpawnWave(guardHealth.transform)) wave++;
            }
            if (Time.time >= nextGuardPressure)
            {
                nextGuardPressure = Time.time + 2.5f;
                ApplyGuardPressure();
            }
            if (elapsed >= GuardSeconds) Complete();
        }
    }

    public bool Activate(Transform player)
    {
        if (Phase != MapEventPhase.Dormant || player == null || spawnService == null || theme == null)
            return false;
        if (Kind == MapEventKind.Guard && guardHealth == null) return false;
        Transform target = Kind == MapEventKind.Guard ? guardHealth.transform : player;
        if (!SpawnWave(target)) return false;
        nextGuardPressure = Time.time + 2.5f;
        Phase = MapEventPhase.Active;
        if (activationBoundary != null) activationBoundary.SetActive(false);
        StateChanged?.Invoke(this);
        return true;
    }

    private bool SpawnWave(Transform target)
    {
        int small = Kind == MapEventKind.Hunt ? 4 + band + wave : 3 + band;
        int medium = Kind == MapEventKind.Hunt ? 1 + (band > 0 || wave > 0 ? 1 : 0)
            : band == 0 ? 0 : band;
        int elite = band == 2 && wave > 0 ? 1 : 0;
        if (Kind == MapEventKind.Guard && band == 2) elite = 1;
        var roster = theme.BuildRoster(small, medium, elite, random.Next());
        int spawned = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            if (!TrySpawnPoint(out Vector3 position)) continue;
            Vector3 direction = target.position - position;
            direction.y = 0f;
            Quaternion rotation = direction.sqrMagnitude > .001f
                ? Quaternion.LookRotation(direction.normalized) : Quaternion.identity;
            var request = new EnemySpawnRequest(roster[i], position, rotation, target,
                gameObject, transform, transform, 1f, 1f, random.Next(), encounter);
            if (!spawnService.TrySpawn(request, out EnemyActor actor)) continue;
            EnemyThemeTier tier = i < small ? EnemyThemeTier.Small
                : i < small + medium ? EnemyThemeTier.Medium : EnemyThemeTier.Elite;
            living.Add(actor.Health, tier);
            actor.Health.OnDead += HandleEnemyDead;
            if (Kind == MapEventKind.Guard) actor.AI?.RequestAggro(target);
            spawned++;
        }
        if (spawned == 0) Debug.LogWarning("[MapEvent] 소환 실패, 다음 근접 검사에서 재시도: " + eventId, this);
        return spawned > 0;
    }

    private bool TrySpawnPoint(out Vector3 position)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int mask = groundLayer >= 0 ? 1 << groundLayer : Physics.DefaultRaycastLayers;
        for (int attempt = 0; attempt < 35; attempt++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = 6f + (float)random.NextDouble() * 7f;
            Vector3 candidate = transform.position + new Vector3(
                Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            if (!DiamondDungeonLayout.Contains(candidate, 5f)) continue;
            if (!Physics.Raycast(candidate + Vector3.up * 6f, Vector3.down, out RaycastHit hit,
                12f, mask, QueryTriggerInteraction.Ignore)) continue;
            position = hit.point + Vector3.up * .06f;
            return true;
        }
        position = default;
        return false;
    }

    private void ApplyGuardPressure()
    {
        if (living.Count == 0 || guardHealth == null || guardHealth.IsDead) return;
        // The shared enemy AI prioritizes the player. Remaining enemies still pressure the relic,
        // so leaving the area cannot turn the timed objective into a free reward.
        GameObject source = null;
        foreach (var pair in living)
            if (pair.Key != null && !pair.Key.IsDead) { source = pair.Key.gameObject; break; }
        if (source == null) return;
        guardHealth.TakeDamage(new DamageInfo(2f * living.Count,
            guardHealth.transform.position, source, suppressDefaultHitVfx: true));
    }

    private void HandleEnemyDead(CombatHealth health, DamageInfo info)
    {
        if (!living.TryGetValue(health, out EnemyThemeTier tier)) return;
        living.Remove(health);
        health.OnDead -= HandleEnemyDead;
        if (encounter.CanGrantRewards && info.source != null
            && info.source.GetComponentInParent<PlayerActorRuntime>() != null)
        {
            ItemData mapItem = MapDropPolicy.Roll(mapDefinition, registry,
                encounter.MapLevel, tier, encounter.RunId);
            if (mapItem != null)
                WorldItemDropFactory.CreateWorldPickup(mapItem,
                    health.transform.position + Vector3.up * .4f,
                    PlayerAccountInventoryService.SharedInventory,
                    PlayerContext.Instance?.CurrentActor?.transform);
        }
        if (Phase != MapEventPhase.Active || Kind != MapEventKind.Hunt || living.Count > 0) return;
        if (wave == 0)
        {
            Transform player = PlayerContext.Instance?.CurrentActor?.transform;
            if (player != null && SpawnWave(player)) { wave = 1; return; }
            nextCheck = Time.time + 1f;
        }
        else Complete();
    }

    private void Complete()
    {
        if (Phase != MapEventPhase.Active) return;
        Phase = MapEventPhase.Complete;
        if (rewardChestVisual != null) rewardChestVisual.SetActive(true);
        StateChanged?.Invoke(this);
    }

    private void Fail()
    {
        if (Phase != MapEventPhase.Active) return;
        Phase = MapEventPhase.Failed;
        StateChanged?.Invoke(this);
    }

    private void HandleGuardDead(CombatHealth health, DamageInfo info) => Fail();

    public void MarkClaimed()
    {
        if (Phase != MapEventPhase.Complete) return;
        Phase = MapEventPhase.Claimed;
        StateChanged?.Invoke(this);
    }

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
        => actor != null && Phase == MapEventPhase.Complete && mayRun
            && WorldSessionState.Phase == WorldPhase.Run;

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor)) return InteractionExecutionResult.Rejected;
        ChestRequested?.Invoke(this);
        return InteractionExecutionResult.Succeeded;
    }

    public void SetInteractionPromptVisible(bool visible) { }

    private void BuildVisual(Material accent, int mapLevel)
    {
        activationBoundary = new GameObject("ActivationBoundary");
        activationBoundary.transform.SetParent(transform, false);
        var line = activationBoundary.AddComponent<LineRenderer>();
        line.sharedMaterial = accent;
        line.useWorldSpace = false;
        line.loop = true;
        line.widthMultiplier = .09f;
        line.positionCount = 64;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * ActivationRadius,
                .10f, Mathf.Sin(angle) * ActivationRadius));
        }
        var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pedestal.name = "EventPedestal";
        pedestal.transform.SetParent(transform, false);
        pedestal.transform.localPosition = new Vector3(0f, .22f, 0f);
        pedestal.transform.localScale = new Vector3(2.7f, .20f, 2.7f);
        pedestal.GetComponent<Renderer>().sharedMaterial = accent;
        Destroy(pedestal.GetComponent<Collider>());
        var focus = GameObject.CreatePrimitive(Kind == MapEventKind.Guard
            ? PrimitiveType.Capsule : PrimitiveType.Cube);
        focus.name = Kind == MapEventKind.Guard ? "GuardedRelic" : "EventChest";
        focus.transform.SetParent(transform, false);
        focus.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        focus.transform.localScale = Kind == MapEventKind.Guard
            ? new Vector3(1.45f, 1.1f, 1.45f) : new Vector3(1.6f, .85f, 1.1f);
        focus.GetComponent<Renderer>().sharedMaterial = accent;
        if (Kind == MapEventKind.Guard)
        {
            guardHealth = focus.AddComponent<CombatHealth>();
            guardHealth.SetMaxHp(100f + mapLevel * 18f, true);
            CombatTarget.EnsureConfigured(focus, CombatTeam.PlayerParty);
            guardHealth.OnDead += HandleGuardDead;
            var chest = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chest.name = "RewardChest";
            chest.transform.SetParent(transform, false);
            chest.transform.localPosition = new Vector3(1.2f, .75f, 0f);
            chest.transform.localScale = new Vector3(1.15f, .75f, .9f);
            chest.GetComponent<Renderer>().sharedMaterial = accent;
            Destroy(chest.GetComponent<Collider>());
            rewardChestVisual = chest;
            chest.SetActive(false);
        }
        else Destroy(focus.GetComponent<Collider>());
    }
}
