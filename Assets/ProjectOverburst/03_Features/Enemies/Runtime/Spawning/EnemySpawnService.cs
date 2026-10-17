using UnityEngine;

[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public sealed class EnemySpawnService : MonoBehaviour
{
    private static readonly Unity.Profiling.ProfilerMarker AcquireMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Acquire");
    private static readonly Unity.Profiling.ProfilerMarker PrepareMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Prepare");
    private static readonly Unity.Profiling.ProfilerMarker ActivateMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Activate");
    private static readonly Unity.Profiling.ProfilerMarker FinalizeMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Finalize");
    private static readonly Unity.Profiling.ProfilerMarker PreparePlacementMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Placement");
    private static readonly Unity.Profiling.ProfilerMarker PrepareStatsMarker = new Unity.Profiling.ProfilerMarker("OVERBURST.MapSpawn.Detail.Stats");
    [SerializeField] private EnemyCatalog catalog;
    [SerializeField] private EnemyPoolService pool;
    private readonly System.Collections.Generic.Dictionary<string, EnemyDefinition> additionalDefinitions =
        new System.Collections.Generic.Dictionary<string, EnemyDefinition>(System.StringComparer.Ordinal);

    private static readonly System.Collections.Generic.List<EnemySpawnService> services = new System.Collections.Generic.List<EnemySpawnService>();
    public static EnemySpawnService Current
    {
        get
        {
            // Hub and a preparing run coexist during additive loading; each keeps its own pool.
            foreach (var candidate in services)
                if (candidate && candidate.isActiveAndEnabled && candidate.gameObject.scene == WorldSessionState.ContentScene) return candidate;
            foreach (var candidate in services) if (candidate && candidate.isActiveAndEnabled) return candidate;
            return null;
        }
    }
    public EnemyCatalog Catalog => catalog;
    public EnemyPoolService Pool => pool;
    public bool IsAuthoringValid => catalog != null && pool != null && pool.IsAuthoringValid;

    private void Awake()
    {
        foreach (var candidate in services)
        {
            if (!candidate || candidate == this || candidate.gameObject.scene != gameObject.scene) continue;
            Debug.LogWarning(
                "[EnemySpawnService] 중복 서비스는 등록하지 않고 비활성화합니다.",
                this);
            enabled = false;
            return;
        }

        services.Add(this);
    }

    private void OnDestroy()
    {
        services.Remove(this);
    }

    public void Configure(EnemyCatalog enemyCatalog, EnemyPoolService poolService)
    {
        additionalDefinitions.Clear();
        catalog = enemyCatalog;
        pool = poolService;
    }

    public bool Validate(out string message)
    {
        if (catalog == null)
        {
            message = "EnemyCatalog 참조가 없습니다.";
            return false;
        }

        if (!catalog.Validate(out message))
            return false;
        if (pool == null)
        {
            message = "EnemyPoolService 참조가 없습니다.";
            return false;
        }

        return pool.Validate(out message);
    }

    public EnemyActor Spawn(EnemySpawnRequest request)
    {
        if (TrySpawn(request, out EnemyActor actor))
            return actor;

        Debug.LogError(
            $"[EnemySpawnService] 스폰 실패: {request.DefinitionId}",
            this);
        return null;
    }

    public bool TrySpawn(EnemySpawnRequest request, out EnemyActor actor)
    {
        actor = null;
        if (!TryResolveDefinition(request, out EnemyDefinition definition))
            return false;
        if (pool == null)
            return false;

        EnemyActor rented;
        using (AcquireMarker.Auto()) rented = pool.Acquire(definition);
        if (rented == null)
            return false;

        bool prepared;
        using (PrepareMarker.Auto())
        {
            using (PreparePlacementMarker.Auto())
            {
                Transform actorTransform = rented.transform;
                actorTransform.SetParent(request.Parent, false);
                actorTransform.SetPositionAndRotation(request.Position, request.Rotation);
                actorTransform.localScale = Vector3.one;
            }
            EnemyRuntimeStats stats;
            using (PrepareStatsMarker.Auto())
            {
                stats = definition.ResolveRuntimeStats(
                    request.DifficultyMultiplier,
                    request.EncounterMultiplier);
                stats = MapOptionPolicy.ApplyEnemyStats(stats, request.Encounter.Map);
            }
            prepared = rented.PrepareForLease(definition, stats, request);
        }
        if (!prepared)
        {
            pool.Release(rented);
            return false;
        }

        using (ActivateMarker.Auto())
        {
            rented.ConfigureRunFallGuard(request.Position);
            rented.gameObject.SetActive(true);
        }
        bool finalized;
        using (FinalizeMarker.Auto()) finalized = rented.FinalizeLeaseAfterActivation();
        if (!finalized)
        {
            pool.Release(rented);
            return false;
        }

        actor = rented;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Overburst.DebugTools.Performance.CombatSpawnQualityTrace.Emit(20, actor: actor, definition: definition, point: request.Position);
#endif
        return true;
    }

    public void Release(EnemyActor actor)
    {
        if (actor == null)
            return;
        if (pool == null)
        {
            Debug.LogWarning("[EnemySpawnService] Pool 참조가 없어 Actor를 반환하지 못했습니다.", actor);
            return;
        }

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Overburst.DebugTools.Performance.CombatSpawnQualityTrace.Emit(21, actor: actor);
#endif
        pool.Release(actor);
    }

    public bool ReleaseDeferred(EnemyActor actor, uint expectedLeaseVersion)
    {
        return pool != null && pool.ReleaseDeferred(actor, expectedLeaseVersion);
    }

    public int Prewarm(EnemyDefinition definition, int count = -1)
    {
        return pool != null ? pool.Prewarm(definition, count) : 0;
    }

    // Add content for a scene without mutating its authored catalog or replacing the shared pool.
    public bool RegisterAdditionalCatalog(EnemyCatalog additional, out string message)
    {
        if (catalog == null || additional == null) { message = "카탈로그가 없습니다."; return false; }
        if (!additional.Validate(out message)) return false;
        for (int i = 0; i < additional.Count; i++)
        {
            var entry = additional.GetDefinition(i);
            if ((catalog.TryGet(entry.EnemyId, out var existing) && existing != entry)
                || (additionalDefinitions.TryGetValue(entry.EnemyId, out existing) && existing != entry))
            { message = "기존 몬스터 ID와 충돌합니다: " + entry.EnemyId; return false; }
        }
        for (int i = 0; i < additional.Count; i++)
        { var entry = additional.GetDefinition(i); additionalDefinitions[entry.EnemyId] = entry; }
        message = string.Empty; return true;
    }

    private bool TryResolveDefinition(
        EnemySpawnRequest request,
        out EnemyDefinition definition)
    {
        definition = null;
        if (catalog == null || !request.HasDefinitionId)
            return false;

        if (!catalog.TryGet(request.DefinitionId, out EnemyDefinition registered)
            && !additionalDefinitions.TryGetValue(request.DefinitionId, out registered))
            return false;

        if (request.Definition != null && request.Definition != registered)
        {
            Debug.LogError(
                $"[EnemySpawnService] Catalog과 다른 Definition 참조입니다: {request.DefinitionId}",
                this);
            return false;
        }

        definition = registered;
        if (definition == null || !definition.IsValid)
        {
            Debug.LogError(
                $"[EnemySpawnService] 유효한 Definition을 찾지 못했습니다: {request.DefinitionId}",
                this);
            return false;
        }

        return true;
    }
}
