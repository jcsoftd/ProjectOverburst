using UnityEngine;

[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public sealed class EnemySpawnService : MonoBehaviour
{
    [SerializeField] private EnemyCatalog catalog;
    [SerializeField] private EnemyPoolService pool;
    private readonly System.Collections.Generic.Dictionary<string, EnemyDefinition> additionalDefinitions =
        new System.Collections.Generic.Dictionary<string, EnemyDefinition>(System.StringComparer.Ordinal);

    public static EnemySpawnService Current { get; private set; }
    public EnemyCatalog Catalog => catalog;
    public EnemyPoolService Pool => pool;
    public bool IsAuthoringValid => catalog != null && pool != null && pool.IsAuthoringValid;

    private void Awake()
    {
        if (Current != null && Current != this)
        {
            Debug.LogWarning(
                "[EnemySpawnService] 중복 서비스는 등록하지 않고 비활성화합니다.",
                this);
            enabled = false;
            return;
        }

        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;
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

        EnemyActor rented = pool.Acquire(definition);
        if (rented == null)
            return false;

        Transform actorTransform = rented.transform;
        actorTransform.SetParent(request.Parent, false);
        actorTransform.SetPositionAndRotation(request.Position, request.Rotation);
        actorTransform.localScale = Vector3.one;

        EnemyRuntimeStats stats = definition.ResolveRuntimeStats(
            request.DifficultyMultiplier,
            request.EncounterMultiplier);
        if (!rented.PrepareForLease(definition, stats, request))
        {
            pool.Release(rented);
            return false;
        }

        rented.ConfigureRunFallGuard(request.Position);
        rented.gameObject.SetActive(true);
        if (!rented.FinalizeLeaseAfterActivation())
        {
            pool.Release(rented);
            return false;
        }

        actor = rented;
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

        pool.Release(actor);
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
