using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(RunWorldGate))]
public sealed class DiamondDungeonWorld : MonoBehaviour
{
    public const string SceneName = "DiamondDungeon01";
    private RunWorldGate gate;
    private RunWalkableArea walkable;
    private EnemyThemeTable theme;
    private MapItemData mapDefinition;
    private EnemySpawnService spawnService;
    private GameObject boss;
    private CombatHealth bossHealth;
    private DungeonExitPortal exitPortal;
    private DiamondCorner startCorner;
    private Vector3 bossPosition;
    private bool prepared;
    private bool bossDeathPending;
    private bool bossSettled;
    private float nextBossRetry;
    private Material floorMaterial;
    private Material accentMaterial;
    private Mesh floorMesh;
    private readonly List<MapMonsterField> fields = new List<MapMonsterField>();
    private MapDungeonEventDirector eventDirector;

    public DiamondCorner StartCorner => startCorner;
    public DiamondCorner BossCorner => DiamondDungeonLayout.Opposite(startCorner);
    public EnemyThemeTable Theme => theme;
    public IReadOnlyList<MapMonsterField> Fields => fields;
    public MapDungeonEventDirector EventDirector => eventDirector;

    private void Awake() => gate = GetComponent<RunWorldGate>();

    private void Update()
    {
        if (!prepared && gate != null && gate.Context != null && gate.Error == null)
        {
            prepared = true;
            try { PrepareWorld(); }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                if (RunWalkableContext.Current == walkable) RunWalkableContext.Clear();
                gate.RejectPreparation(gate.Context.RunId, error.Message);
            }
        }
        if (bossDeathPending && !bossSettled && Time.unscaledTime >= nextBossRetry)
            TrySettleBoss();
    }

    private void PrepareWorld()
    {
        mapDefinition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        if (mapDefinition == null || gate.Map == null || gate.Map.mapContentId == null)
            throw new InvalidOperationException("첫 던전 지도 정의가 없습니다.");
        var account = AccountGameplaySession.Current;
        if (account == null || account.ContentRegistry.IdFor(mapDefinition) != gate.Map.mapContentId)
            throw new InvalidOperationException("입장 지도의 종류가 첫 던전과 다릅니다.");

        string themeId = string.IsNullOrEmpty(gate.Map.monsterThemeId)
            ? MapThemeCatalog.RollThemeId() : gate.Map.monsterThemeId;
        theme = MapThemeCatalog.Resolve(themeId);
        if (theme == null) throw new InvalidOperationException("지도 몬스터 테마를 찾을 수 없습니다: " + themeId);
        var random = new System.Random(StableSeed(gate.Context.RunId));
        startCorner = (DiamondCorner)random.Next(4);
        bossPosition = DiamondDungeonLayout.InsetPoint(BossCorner, 12f);
        BuildMaterials();
        BuildLighting();
        BuildFloor();
        BuildCornerMarkers();
        walkable = DiamondDungeonLayout.CreateWalkableArea();
        RunWalkableContext.SetCurrent(walkable, -.5f);

        var entry = Child("EntryPoint").transform;
        entry.position = DiamondDungeonLayout.InsetPoint(startCorner, 12f) + Vector3.up * .18f;
        entry.rotation = Quaternion.LookRotation(-DiamondDungeonLayout.Direction(startCorner), Vector3.up);
        BuildBoss();
        BuildExitPortal();
        BuildSpawnService();
        BuildFields(random, account);
        var runBuffs = gameObject.AddComponent<MapRunBuffs>();
        runBuffs.Configure(gate.Context.RunId);
        eventDirector = Child("DungeonEvents").AddComponent<MapDungeonEventDirector>();
        eventDirector.Configure(startCorner, theme, spawnService, gate.Context, gate.Map,
            accentMaterial, fields, random.Next());

        if (!gate.CompletePreparation(gate.Context.RunId, entry))
            throw new InvalidOperationException("던전 준비 완료 신호를 전달하지 못했습니다.");
    }

    private void BuildMaterials()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) throw new InvalidOperationException("던전 바닥 셰이더가 없습니다.");
        floorMaterial = new Material(shader) { color = new Color(.30f, .34f, .35f) };
        accentMaterial = new Material(shader)
        { color = Color.Lerp(new Color(.49f, .68f, .73f), theme.Accent, .45f) };
    }

    private void BuildLighting()
    {
        var lightObject = Child("DungeonSun");
        lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, .94f, .84f);
        light.intensity = 1.5f;
        light.shadows = LightShadows.Soft;
    }

    private void BuildFloor()
    {
        var floor = Child("DiamondTerrain");
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0) floor.layer = groundLayer;
        float r = DiamondDungeonLayout.Radius;
        var mesh = new Mesh { name = "DiamondDungeonFloor" };
        mesh.vertices = new[]
        {
            new Vector3(r, 0f, 0f), new Vector3(0f, 0f, r),
            new Vector3(-r, 0f, 0f), new Vector3(0f, 0f, -r)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.uv = new[] { Vector2.right, Vector2.up, Vector2.zero, Vector2.one };
        mesh.RecalculateNormals();
        floorMesh = mesh;
        floor.AddComponent<MeshFilter>().sharedMesh = mesh;
        floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
        floor.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private void BuildCornerMarkers()
    {
        for (int i = 0; i < 4; i++)
        {
            var marker = Primitive("CornerMarker_" + i, PrimitiveType.Cylinder,
                DiamondDungeonLayout.InsetPoint((DiamondCorner)i, 3f) + Vector3.up * .08f,
                new Vector3(2.1f, .08f, 2.1f));
            marker.GetComponent<Renderer>().sharedMaterial = accentMaterial;
            Destroy(marker.GetComponent<Collider>());
        }
    }

    private void BuildBoss()
    {
        boss = Primitive("CapsuleBoss_Temporary", PrimitiveType.Capsule,
            bossPosition + Vector3.up * 2.2f, new Vector3(2.8f, 2.2f, 2.8f));
        boss.GetComponent<Renderer>().sharedMaterial = accentMaterial;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0) boss.layer = enemyLayer;
        bossHealth = boss.AddComponent<CombatHealth>();
        var rank = boss.AddComponent<EnemyRank>();
        rank.ConfigureTemporaryBoss(gate.Context);
        rank.ApplyLevelToHealth(140f);
        boss.AddComponent<EnemyLootDropper>().ConfigureEncounter(gate.Context);
        CombatTarget.EnsureConfigured(boss, CombatTeam.Enemy);
        bossHealth.OnDead += HandleBossDead;
    }

    private void BuildExitPortal()
    {
        var root = Child("ExitPortal_AfterBoss");
        root.transform.position = DiamondDungeonLayout.InsetPoint(BossCorner, 4f);
        exitPortal = root.AddComponent<DungeonExitPortal>();
        exitPortal.BuildVisual(accentMaterial);
        root.SetActive(false);
    }

    private void BuildSpawnService()
    {
        var root = Child("EnemySpawnServices");
        var inactive = Child("InactiveEnemyPool");
        inactive.transform.SetParent(root.transform, false);
        inactive.SetActive(false);
        var pool = root.AddComponent<EnemyPoolService>();
        pool.Configure(inactive.transform, 0);
        spawnService = root.AddComponent<EnemySpawnService>();
        spawnService.Configure(theme.Catalog, pool);
        if (!spawnService.Validate(out string reason)) throw new InvalidOperationException(reason);
    }

    private void BuildFields(System.Random random, AccountGameplaySession account)
    {
        var positions = new List<Vector3>();
        int[] fieldCounts = { 2, 3, 3 };
        for (int band = 0; band < fieldCounts.Length; band++)
            for (int index = 0; index < fieldCounts[band]; index++)
            {
                float min = band == 0 ? .10f : band == 1 ? .35f : .68f;
                float max = band == 0 ? .31f : band == 1 ? .65f : .89f;
                Vector3 position = DiamondDungeonLayout.RollPoint(random, startCorner,
                    min, max, positions.ToArray(), 19f);
                positions.Add(position);
                var node = Child("FieldEncounter_" + band + "_" + index);
                node.transform.position = position;
                var field = node.AddComponent<MapMonsterField>();
                field.Configure(theme, spawnService, gate.Context, gate.Map, mapDefinition,
                    account.ContentRegistry, band, random.Next());
                fields.Add(field);
            }
    }

    private void HandleBossDead(CombatHealth health, DamageInfo info)
    {
        bossDeathPending = true;
        TrySettleBoss();
    }

    private void TrySettleBoss()
    {
        nextBossRetry = Time.unscaledTime + 1f;
        var account = AccountGameplaySession.Current;
        if (account == null || gate?.Context == null) return;
        try
        {
            var run = account.ReadRun();
            if (run == null || run.runId != gate.Context.RunId) return;
            if (run.phase == RunPhase.Active)
                new AccountRunSession(account).ClearBossWithMapReward(run.runId, mapDefinition,
                    bossPosition + Vector3.up * .4f);
            else if (run.phase != RunPhase.BossCleared) return;
            bossSettled = true;
            if (boss != null)
            {
                boss.GetComponent<Collider>().enabled = false;
                boss.GetComponent<Renderer>().enabled = false;
            }
            if (exitPortal != null) exitPortal.gameObject.SetActive(true);
        }
        catch (Exception error) { Debug.LogError("보스 보상 저장 재시도: " + error.Message, this); }
    }

    private GameObject Child(string name)
    {
        var child = new GameObject(name);
        SceneManager.MoveGameObjectToScene(child, gameObject.scene);
        child.transform.SetParent(transform, false);
        return child;
    }

    private GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale)
    {
        var child = GameObject.CreatePrimitive(type);
        child.name = name;
        SceneManager.MoveGameObjectToScene(child, gameObject.scene);
        child.transform.SetParent(transform, false);
        child.transform.position = position;
        child.transform.localScale = scale;
        return child;
    }

    private static int StableSeed(string id)
    {
        unchecked
        {
            int value = 17;
            foreach (char letter in id) value = value * 31 + letter;
            return value;
        }
    }

    private void OnDestroy()
    {
        if (bossHealth != null) bossHealth.OnDead -= HandleBossDead;
        if (RunWalkableContext.Current == walkable) RunWalkableContext.Clear();
        if (floorMaterial != null) Destroy(floorMaterial);
        if (accentMaterial != null) Destroy(accentMaterial);
        if (floorMesh != null) Destroy(floorMesh);
    }
}
