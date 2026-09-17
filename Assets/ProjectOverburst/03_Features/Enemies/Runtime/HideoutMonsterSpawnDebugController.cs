using UnityEngine;

public sealed class HideoutMonsterSpawnDebugController : MonoBehaviour // 하이드아웃 전용 무제한 테스트 스폰
{
    private const float GoldenAngle = 137.50776f;

    [SerializeField, HideInInspector] private EnemySpawnPackConfig spawnConfig =
        new EnemySpawnPackConfig();
    [Header("Protofactor Debug Roster")]
    [SerializeField, Min(1)] private int monstersPerPack = 4;
    [SerializeField, Min(0.01f)] private float difficultyMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float encounterMultiplier = 1f;
    [SerializeField] private float spawnInterval = 5f;
    [SerializeField] private float spawnRadius = 8f;
    [SerializeField] private float ringSpacing = 1.8f;
    [SerializeField] private float groundProbeHeight = 12f;
    [SerializeField] private float groundSpawnOffset = 0.05f;

    private Transform player;
    private PlayerInventory targetInventory;
    private System.Random definitionRandom;
    private float nextSpawnTime;
    private int spawnSerial;

    public int SpawnedCount { get { return spawnSerial; } }
    public bool IsSpawning { get { return CombatDebugSettings.SpawnHideoutMonsters; } }

    private void OnEnable()
    {
        definitionRandom = new System.Random(
            unchecked(System.Environment.TickCount ^ GetInstanceID()));
        CombatDebugSettings.HideoutMonsterSpawnChanged += HandleSpawnSettingChanged;
        ResolveReferences();
        ScheduleFromCurrentSetting();
    }

    private void OnDisable()
    {
        CombatDebugSettings.HideoutMonsterSpawnChanged -= HandleSpawnSettingChanged;
    }

    private void Update()
    {
        if (!CombatDebugSettings.SpawnHideoutMonsters || Time.time < nextSpawnTime)
            return;

        nextSpawnTime = Time.time + Mathf.Max(0.1f, spawnInterval);
        SpawnNextPack();
    }

    public int TrySpawnPackNow()
    {
        if (!CombatDebugSettings.SpawnHideoutMonsters)
            return 0;

        return SpawnNextPack();
    }

    private int SpawnNextPack()
    {
        ResolveReferences();
        if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(
                transform,
                out EnemySpawnService spawnService))
        {
            return 0;
        }

        int spawned = 0;
        int requestedCount = Mathf.Max(1, monstersPerPack);
        for (int i = 0; i < requestedCount; i++)
        {
            int serial = spawnSerial++;
            string definitionId =
                EnemyDebugSpawnRuntimeContext.GetRandomDefinitionId(definitionRandom);
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                Debug.LogWarning(
                    "[HideoutMonsterSpawnDebug] 무작위 선택 가능한 신규 몬스터가 없습니다.",
                    this);
                continue;
            }

            Vector3 position = ResolveSpawnPosition(serial);
            EnemySpawnRequest request = new EnemySpawnRequest(
                definitionId,
                position,
                Quaternion.identity,
                player,
                gameObject,
                player,
                transform,
                difficultyMultiplier,
                encounterMultiplier,
                serial);
            if (!spawnService.TrySpawn(request, out EnemyActor actor))
            {
                Debug.LogWarning(
                    "[HideoutMonsterSpawnDebug] 신규 몬스터 생성 실패: "
                    + definitionId,
                    this);
                continue;
            }

            actor.name = string.Format(
                "HideoutDebugMonster_{0:0000}_{1}",
                serial,
                definitionId);
            ConfigureDebugPresentation(actor);
            spawned++;
        }

        Debug.Log(
            "[HideoutMonsterSpawnDebug] 스폰팩 생성="
            + spawned
            + " 누적="
            + spawnSerial,
            this);
        return spawned;
    }

    private void ConfigureDebugPresentation(EnemyActor actor)
    {
        if (actor == null)
            return;

        EnemyLootDropper dropper = actor.GetComponent<EnemyLootDropper>();
        dropper?.Configure(
            spawnConfig.DropTable,
            targetInventory,
            player,
            spawnConfig.PickupGradeVfxSet);

        if (spawnConfig.HitVfxPrefab != null || spawnConfig.DeathVfxPrefab != null)
        {
            CombatVfx combatVfx = actor.GetComponent<CombatVfx>();
            combatVfx?.Configure(
                spawnConfig.HitVfxPrefab,
                spawnConfig.DeathVfxPrefab);
        }
    }

    private Vector3 ResolveSpawnPosition(int serial)
    {
        Vector3 center = player != null ? player.position : transform.position;
        int ring = serial / 8;
        float radius = Mathf.Max(1f, spawnRadius) + (ring * Mathf.Max(0f, ringSpacing));
        float angle = serial * GoldenAngle * Mathf.Deg2Rad;
        Vector3 position = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

        Vector3 probeOrigin = position + Vector3.up * Mathf.Max(1f, groundProbeHeight);
        if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, groundProbeHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
            position.y = hit.point.y + Mathf.Max(0f, groundSpawnOffset);
        else
            position.y = center.y + spawnConfig.SpawnHeightOffset;

        return position;
    }

    private void HandleSpawnSettingChanged(bool enabled)
    {
        if (enabled)
        {
            ResolveReferences();
            nextSpawnTime = Time.time; // 켜는 즉시 첫 팩 생성
        }
    }

    private void ScheduleFromCurrentSetting()
    {
        nextSpawnTime = CombatDebugSettings.SpawnHideoutMonsters
            ? Time.time
            : float.PositiveInfinity;
    }

    private void ResolveReferences()
    {
        if (targetInventory == null)
            targetInventory = FindFirstObjectByType<PlayerInventory>();

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            player = playerObject != null ? playerObject.transform : null;
        }
    }
}
