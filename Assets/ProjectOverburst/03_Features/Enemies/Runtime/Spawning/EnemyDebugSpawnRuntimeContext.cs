using UnityEngine;
using UnityEngine.SceneManagement;

public static class EnemyDebugSpawnRuntimeContext // 디버그 소환의 신규 몬스터 공용 진입점
{
    public const string CatalogResourcePath =
        "Enemies/Protofactor/Catalogs/EC_ProtofactorPilot";
    public const string CeratoferoxDefinitionId = "Ceratoferox_Normal";
    public const string RapaxDefinitionId = "Rapax_Normal";
    public const string GobblerDefinitionId = "Gobbler_Normal";

    private static readonly string[] RequiredPilotDefinitionIds =
    {
        CeratoferoxDefinitionId,
        RapaxDefinitionId,
        GobblerDefinitionId
    };

    private static EnemyCatalog debugCatalog;
    private static EnemySpawnService debugSpawnService;

    public static int ActiveDefinitionCount
    {
        get { return CountDebugDefinitions(ResolveCatalog()); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        debugCatalog = null;
        debugSpawnService = null;
    }

    public static string GetDefinitionId(int serial)
    {
        EnemyCatalog catalog = ResolveCatalog();
        int count = CountDebugDefinitions(catalog);
        if (count == 0)
            return string.Empty;

        int index = serial % count;
        if (index < 0)
            index += count;
        EnemyDefinition definition = GetDebugDefinition(catalog, index);
        return definition != null ? definition.EnemyId : string.Empty;
    }

    public static string GetRandomDefinitionId(System.Random random)
    {
        EnemyCatalog catalog = ResolveCatalog();
        int count = CountDebugDefinitions(catalog);
        if (count == 0 || random == null)
            return string.Empty;

        EnemyDefinition definition = GetDebugDefinition(catalog, random.Next(count));
        return definition != null ? definition.EnemyId : string.Empty;
    }

    public static bool TryGetSpawnService(
        Transform sceneAnchor,
        out EnemySpawnService spawnService)
    {
        spawnService = null;
        if (debugSpawnService != null && debugSpawnService.IsAuthoringValid)
        {
            spawnService = debugSpawnService;
            return true;
        }

        EnemyCatalog catalog = ResolveCatalog();
        if (!IsActiveCatalogValid(catalog, out string message))
        {
            Debug.LogError("[EnemyDebugSpawnRuntimeContext] " + message);
            return false;
        }

        EnemySpawnService current = EnemySpawnService.Current;
        if (current != null)
        {
            if (current.Catalog != catalog || !current.IsAuthoringValid)
            {
                Debug.LogError(
                    "[EnemyDebugSpawnRuntimeContext] 현재 씬의 EnemySpawnService가 "
                    + "Protofactor 디버그 Catalog 계약과 다릅니다.",
                    current);
                return false;
            }

            debugSpawnService = current;
            spawnService = current;
            return true;
        }

        GameObject serviceRoot = new GameObject("EnemyDebugSpawnServices");
        MoveToAnchorScene(serviceRoot, sceneAnchor);

        GameObject inactivePool = new GameObject("InactiveEnemyPool");
        inactivePool.transform.SetParent(serviceRoot.transform, false);
        inactivePool.SetActive(false);

        EnemyPoolService pool = serviceRoot.AddComponent<EnemyPoolService>();
        pool.Configure(inactivePool.transform, 0);

        debugSpawnService = serviceRoot.AddComponent<EnemySpawnService>();
        debugSpawnService.Configure(catalog, pool);
        if (!debugSpawnService.Validate(out message))
        {
            Debug.LogError("[EnemyDebugSpawnRuntimeContext] " + message, serviceRoot);
            Object.Destroy(serviceRoot);
            debugSpawnService = null;
            return false;
        }

        spawnService = debugSpawnService;
        return true;
    }

    private static bool IsActiveCatalogValid(
        EnemyCatalog catalog,
        out string message)
    {
        if (catalog == null)
        {
            message = "Protofactor EnemyCatalog을 Resources에서 찾지 못했습니다.";
            return false;
        }

        if (!catalog.Validate(out message))
            return false;

        for (int i = 0; i < RequiredPilotDefinitionIds.Length; i++)
        {
            if (!catalog.TryGet(
                    RequiredPilotDefinitionIds[i],
                    out EnemyDefinition definition)
                || definition == null
                || !definition.IsValid)
            {
                message = "필수 Pilot Definition이 Catalog에 없습니다: "
                    + RequiredPilotDefinitionIds[i];
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private static int CountDebugDefinitions(EnemyCatalog catalog)
    {
        if (catalog == null)
            return 0;

        int count = 0;
        for (int i = 0; i < catalog.Count; i++)
        {
            EnemyDefinition definition = catalog.GetDefinition(i);
            if (definition != null
                && definition.IsValid
                && definition.Grade != null
                && definition.Grade.GradeType == EnemyGradeType.Normal)
            {
                count++;
            }
        }

        return count;
    }

    private static EnemyDefinition GetDebugDefinition(EnemyCatalog catalog, int normalIndex)
    {
        if (catalog == null || normalIndex < 0)
            return null;

        int cursor = 0;
        for (int i = 0; i < catalog.Count; i++)
        {
            EnemyDefinition definition = catalog.GetDefinition(i);
            if (definition == null
                || !definition.IsValid
                || definition.Grade == null
                || definition.Grade.GradeType != EnemyGradeType.Normal)
            {
                continue;
            }

            if (cursor == normalIndex)
                return definition;
            cursor++;
        }

        return null;
    }

    private static EnemyCatalog ResolveCatalog()
    {
        if (debugCatalog == null)
            debugCatalog = Resources.Load<EnemyCatalog>(CatalogResourcePath);
        return debugCatalog;
    }

    private static void MoveToAnchorScene(
        GameObject target,
        Transform sceneAnchor)
    {
        if (target == null || sceneAnchor == null)
            return;

        Scene scene = sceneAnchor.gameObject.scene;
        if (scene.IsValid())
            SceneManager.MoveGameObjectToScene(target, scene);
    }
}
