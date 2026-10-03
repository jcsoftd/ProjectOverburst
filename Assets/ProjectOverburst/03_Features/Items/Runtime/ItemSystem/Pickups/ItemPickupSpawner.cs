using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ItemPickupSpawner : MonoBehaviour
{
    public const float AuthoredWeaponGridSpacing = 1.2f;
    public const float AuthoredWeaponGroupGap = 3.5f;
    public const float DefaultPickupScatterRadius = 0.16f;
    private const int WeaponBlockColumnCount = 4;

    private const int MinimumWeaponCopiesPerClass = 2;
    public const int DefaultWeaponCopiesPerGroup = 10;
    private static readonly ItemGrade[] GuaranteedWeaponGrades = ItemGradeAvailabilityPolicy.GetEnabledGrades();
    private bool configuredSpawnCompleted;

    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private Transform player;
    [Tooltip("하이드아웃 전체 아이템 전시 격자의 중심. 비워 두면 캠프 동쪽 기본 전시 위치를 사용한다.")]
    [SerializeField] private Transform authoredSpawnOrigin;
    public Transform AuthoredSpawnOrigin => authoredSpawnOrigin;
    [SerializeField] private WeaponItemData testWeaponItem;
    [SerializeField] private WeaponItemData[] weaponItemAssets;
    [SerializeField] private BaseItemData moveSpeedPotionItem;
    [SerializeField] private BaseItemData smallHealPotionItem;

    [Header("Hideout Catalog")]
    [Tooltip("전체 아이템 전시 격자의 한 행에 놓을 개수")]
    [SerializeField, Min(1)] private int hideoutCatalogColumns = 16;
    [Tooltip("아이템 사이 간격(m)")]
    [SerializeField, Min(1.5f)] private float hideoutCatalogSpacing = 1.8f;
    private static readonly Vector3 HideoutCatalogFallbackCenter = new Vector3(42f, 0f, 20f);
    public int HideoutCatalogSpawnCount { get; private set; }

    [Header("VFX")]
    [SerializeField] private PickupGradeVfxSet pickupGradeVfxSet;

    [Header("Spawn")]
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private bool spawnTestWeaponOnStart = true;
    [SerializeField] private bool spawnRandomWeaponsOnStart;
    [SerializeField] private bool spawnMoveSpeedPotionOnStart = true;
    [SerializeField] private bool spawnSmallHealPotionOnStart = true;
    [SerializeField] private int spawnWeaponCount = 1;
    [SerializeField] private int moveSpeedPotionPickupCount = 15;
    [SerializeField] private int smallHealPotionPickupCount = 10;
    [SerializeField] private float spawnRadius = 3f;
    [SerializeField] private Vector3 weaponSpawnOffset = new Vector3(-0.8f, 0.25f, 1.9f);
    [SerializeField] private Vector3 moveSpeedPotionSpawnOffset = new Vector3(1.6f, 0.25f, -2.2f);
    [SerializeField] private Vector3 moveSpeedPotionSpawnSpacing = new Vector3(0.36f, 0f, 0.36f);
    [SerializeField] private Vector3 smallHealPotionSpawnOffset = new Vector3(2.8f, 0.25f, -2.2f);
    [SerializeField] private Vector3 smallHealPotionSpawnSpacing = new Vector3(0.36f, 0f, 0.36f);

    private void Start()
    {
        if (!spawnOnStart)
            return;

        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow != null && gameObject.scene.name == PersistentSceneFlow.HideoutSceneName)
            return; // 허브 배치 완료 뒤 SceneFlow가 명시적으로 호출

        SpawnConfiguredPickups();
    }

    public void SpawnConfiguredPickups()
    {
        if (configuredSpawnCompleted || !spawnOnStart)
            return;

        ResolveReferences();
        if (gameObject.scene.name == PersistentSceneFlow.HideoutSceneName)
        {
            configuredSpawnCompleted = SpawnHideoutCatalogPickups();
            return;
        }

        configuredSpawnCompleted = true;
        if (spawnTestWeaponOnStart) SpawnTestWeaponPickup();
        if (spawnRandomWeaponsOnStart) SpawnRandomWeaponPickups();
        if (spawnMoveSpeedPotionOnStart) SpawnMoveSpeedPotionPickup();
        if (spawnSmallHealPotionOnStart) SpawnSmallHealPotionPickup();
    }

    /// <summary>저장 가능한 정식 카탈로그 전체를 정의별 한 번만 전시한다.</summary>
    public static List<BaseItemData> CollectHideoutCatalog()
    {
        var result = new List<BaseItemData>();
        var seen = new HashSet<BaseItemData>();
        AddCatalog(Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath), result, seen);
        foreach (var catalog in Resources.LoadAll<AccountContentRegistry>("Persistence/Supplemental"))
            AddCatalog(catalog, result, seen);
        result.Sort(CompareCatalogItems);
        return result;
    }

    private static void AddCatalog(AccountContentRegistry registry, List<BaseItemData> items, HashSet<BaseItemData> seen)
    {
        if (registry == null) return;
        foreach (var entry in registry.Entries)
        {
            if (entry?.asset is not BaseItemData data || !WeaponContentPolicy.IsAllowedItemData(data)) continue;
            if (data is WeaponItemData weapon && weapon.weaponRootPrefab == null) continue;
            if (seen.Add(data)) items.Add(data);
        }
    }

    private static int CatalogCategory(BaseItemData data)
    {
        if (data is WeaponItemData) return 0;
        if (data is GearItemData) return 1;
        if (data is ElementGemItemData) return 2;
        if (data is FlaskItemData) return 3;
        if (data is BagItemData) return 3;
        if (data is ConsumableItemData) return 4;
        if (data is MapItemData) return 5;
        if (data is CurrencyItemData) return 6;
        return 7;
    }

    private static int CompareCatalogItems(BaseItemData left, BaseItemData right)
    {
        int category = CatalogCategory(left).CompareTo(CatalogCategory(right));
        if (category != 0) return category;
        return StringComparer.Ordinal.Compare(left.name, right.name);
    }

    public static Vector3 CalculateCatalogGridOffset(int index, int count, int columns, float spacing)
    {
        int width = Mathf.Min(Mathf.Max(1, columns), Mathf.Max(1, count));
        int rows = Mathf.CeilToInt(Mathf.Max(1, count) / (float)width);
        float step = Mathf.Max(1.5f, spacing);
        return new Vector3((index % width - (width - 1) * .5f) * step, 0f,
            ((rows - 1) * .5f - index / width) * step);
    }

    private bool SpawnHideoutCatalogPickups()
    {
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var catalog = CollectHideoutCatalog();
        if (registry == null || catalog.Count == 0 || inventory == null || player == null)
        {
            Debug.LogError("[ItemPickupSpawner] 전체 아이템 전시에 필요한 카탈로그/플레이어/인벤토리가 없습니다.", this);
            return false;
        }

        Vector3 center = authoredSpawnOrigin != null ? authoredSpawnOrigin.position : HideoutCatalogFallbackCenter;
        int itemLevel = PlayerProgression.CurrentLevel;
        for (int i = 0; i < catalog.Count; i++)
        {
            BaseItemData definition = catalog[i];
            ItemGrade grade = definition is ElementGemItemData gem ? gem.fixedGrade : ItemGradeAvailabilityPolicy.RollWeightedGrade();
            int level = definition is ElementGemItemData || definition is WeaponItemData || definition is GearItemData || definition is FlaskItemData
                || definition is BagItemData || definition is MapItemData ? itemLevel : 1;
            var item = new ItemData(definition, level, grade, 1);
            if (definition is MapItemData)
                item.mapState = new MapInstanceState
                {
                    mapContentId = registry.IdFor(definition), monsterThemeId = MapThemeCatalog.RollThemeId(),
                    level = level, grade = grade, options = MapOptionPolicy.Roll(grade)
                };
            Vector3 position = center + CalculateCatalogGridOffset(i, catalog.Count, hideoutCatalogColumns, hideoutCatalogSpacing);
            position = SnapCatalogPositionToGround(position);
            WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickupFromExistingItem(item, position, inventory, player, pickupGradeVfxSet);
            if (pickup == null)
            {
                Debug.LogError("[ItemPickupSpawner] 전체 아이템 전시 생성 실패: " + definition.name, this);
                continue;
            }
            pickup.PlaceAuthored(position, gameObject.scene);
            HideoutCatalogSpawnCount++;
        }
        return true;
    }

    private Vector3 SnapCatalogPositionToGround(Vector3 position)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int mask = groundLayer >= 0 ? 1 << groundLayer : Physics.DefaultRaycastLayers;
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 20f, Vector3.down, 40f, mask, QueryTriggerInteraction.Ignore);
        float height = float.NegativeInfinity;
        foreach (var hit in hits)
            if (hit.collider.gameObject.scene == gameObject.scene && hit.normal.y > .35f)
                height = Mathf.Max(height, hit.point.y);
        if (!float.IsNegativeInfinity(height)) position.y = height + .15f;
        return position;
    }

    public static void SpawnConfiguredPickupsInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        ItemPickupSpawner[] spawners = FindObjectsByType<ItemPickupSpawner>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < spawners.Length; i++)
        {
            ItemPickupSpawner spawner = spawners[i];
            if (spawner != null && spawner.gameObject.scene == scene)
                spawner.SpawnConfiguredPickups();
        }
    }

    [ContextMenu("Spawn/Test Weapon Pickup")]
    public void SpawnTestWeaponPickup()
    {
        ResolveReferences();

        WeaponItemData weaponData = CreateSpawnWeaponData(ResolvePrimaryWeaponAsset());
        if (weaponData == null)
            return;

        int count = Mathf.Max(MinimumWeaponCopiesPerClass, spawnWeaponCount);
        for (int i = 0; i < count; i++)
        {
            ItemData item = CreateRandomWeaponItem(weaponData);
            CreateWeaponPickup(item, GetPrimaryWeaponSpawnPosition(i, count));
        }
    }

    [ContextMenu("Spawn Random Weapon Pickups")]
    public void SpawnRandomWeaponPickups()
    {
        ResolveReferences();

        List<WeaponSpawnGroup> groups = CollectValidWeaponSpawnGroups();
        if (groups.Count == 0)
        {
            Debug.LogError("[ItemPickupSpawner] 유효한 무기 그룹이 없습니다.", this);
            return;
        }

        int copiesPerClass = DefaultWeaponCopiesPerGroup;
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            SpawnWeaponGroupPickups(groups[groupIndex].weaponData, groupIndex, groups.Count, copiesPerClass);
    }

    private void SpawnWeaponGroupPickups(WeaponItemData weaponData, int groupIndex, int groupCount, int count)
    {
        List<ItemGrade> grades = BuildGuaranteedGradeSequence(count);
        for (int i = 0; i < grades.Count; i++)
        {
            ItemData item = new ItemData(weaponData, 1, grades[i]);
            Vector3 position = GetWeaponSpawnPosition(groupIndex, groupCount, i, grades.Count);
            CreateWeaponPickup(item, position);
        }
    }

    private List<ItemGrade> BuildGuaranteedGradeSequence(int count)
    {
        int targetCount = Mathf.Max(GuaranteedWeaponGrades.Length, count);
        List<ItemGrade> grades = new List<ItemGrade>(targetCount);
        grades.AddRange(GuaranteedWeaponGrades);

        while (grades.Count < targetCount)
            grades.Add(RollVtpGrade());

        for (int i = grades.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            (grades[i], grades[swapIndex]) = (grades[swapIndex], grades[i]);
        }

        return grades;
    }

    [ContextMenu("Spawn/Test Move Speed Potion Pickup")]
    public void SpawnMoveSpeedPotionPickup()
    {
        ResolveReferences();

        if (!(moveSpeedPotionItem is ConsumableItemData potionData))
            return;

        int count = Mathf.Max(1, moveSpeedPotionPickupCount);
        Color color = potionData.color;

        for (int i = 0; i < count; i++)
        {
            ItemData item = new ItemData(potionData, 1, potionData.defaultGrade, 1);
            Vector3 position = GetMoveSpeedPotionSpawnPosition(i, count);
            CreatePickupObject("WorldPickup_MoveSpeedPotion_" + (i + 1).ToString("00"), item, position, new Vector3(0.22f, 0.34f, 0.22f), color, PrimitiveType.Capsule);
        }
    }

    [ContextMenu("Spawn/Test Small Heal Potion Pickup")]
    public void SpawnSmallHealPotionPickup()
    {
        ResolveReferences();

        ConsumableItemData potionData = ResolveSmallHealPotionData();
        if (potionData == null)
            return;

        int count = Mathf.Max(1, smallHealPotionPickupCount);
        Color color = potionData.color;

        for (int i = 0; i < count; i++)
        {
            ItemData item = new ItemData(potionData, 1, potionData.defaultGrade, 1);
            Vector3 position = GetSmallHealPotionSpawnPosition(i, count);
            CreatePickupObject("WorldPickup_SmallHealPotion_" + (i + 1).ToString("00"), item, position, new Vector3(0.2f, 0.3f, 0.2f), color, PrimitiveType.Capsule);
        }
    }

    private WorldItemPickup CreatePickupObject(string objectName, ItemData item, Vector3 position, Vector3 scale, Color color, PrimitiveType primitiveType)
    {
        GameObject pickupObject = GameObject.CreatePrimitive(primitiveType);
        pickupObject.name = objectName;
        pickupObject.transform.position = position;
        pickupObject.transform.localScale = scale;

        Renderer renderer = pickupObject.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = color;

        WorldItemPickup pickup = pickupObject.AddComponent<WorldItemPickup>();
        pickup.Initialize(item, inventory, player, pickupGradeVfxSet);
        PlaceAuthoredPickup(pickup, position);
        return pickup;
    }

    private WeaponItemData ResolvePrimaryWeaponAsset()
    {
        if (IsUsableWeaponAsset(testWeaponItem))
            return testWeaponItem;

        if (weaponItemAssets == null)
            return null;

        for (int i = 0; i < weaponItemAssets.Length; i++)
        {
            if (IsUsableWeaponAsset(weaponItemAssets[i]))
                return weaponItemAssets[i];
        }

        return null;
    }

    private List<WeaponSpawnGroup> CollectValidWeaponSpawnGroups()
    {
        List<WeaponSpawnGroup> result = new List<WeaponSpawnGroup>();
        AddWeaponSpawnGroup(result, testWeaponItem);

        if (weaponItemAssets != null)
        {
            for (int i = 0; i < weaponItemAssets.Length; i++)
                AddWeaponSpawnGroup(result, weaponItemAssets[i]);
        }

        result.Sort(CompareWeaponSpawnGroups); // 실제 무기 분류 기준 정렬
        return result;
    }

    private static void AddWeaponSpawnGroup(List<WeaponSpawnGroup> groups, WeaponItemData weaponData)
    {
        if (groups == null || !IsUsableWeaponAssetForGrouping(weaponData))
            return;

        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].weaponClass == weaponData.weaponClass
                && groups[i].combatFamily == weaponData.CombatFamily)
            {
                return;
            }
        }

        groups.Add(new WeaponSpawnGroup(weaponData));
    }

    private static int CompareWeaponSpawnGroups(WeaponSpawnGroup left, WeaponSpawnGroup right)
    {
        int classCompare = ((int)left.weaponClass).CompareTo((int)right.weaponClass);
        if (classCompare != 0)
            return classCompare;

        return ((int)left.combatFamily).CompareTo((int)right.combatFamily);
    }

    private static bool IsUsableWeaponAssetForGrouping(WeaponItemData weaponData)
    {
        return weaponData != null
            && weaponData.weaponRootPrefab != null
            && WeaponContentPolicy.IsActiveWeapon(weaponData);
    }

    private static bool IsActiveHideoutElement(WeaponElement element)
    {
        return OverburstElementRules.IsActive(element);
    }

    private WeaponItemData CreateSpawnWeaponData(WeaponItemData sourceWeapon)
    {
        return IsUsableWeaponAsset(sourceWeapon) ? sourceWeapon : null;
    }

    private bool IsUsableWeaponAsset(WeaponItemData weaponData)
    {
        return weaponData != null
            && weaponData.weaponRootPrefab != null
            && WeaponContentPolicy.IsActiveWeapon(weaponData);
    }

    private ItemData CreateRandomWeaponItem(WeaponItemData weaponData)
    {
        ItemGrade grade = RollVtpGrade();
        return new ItemData(weaponData, 1, grade);
    }

    private ItemGrade RollVtpGrade()
    {
        return ItemGradeAvailabilityPolicy.RollWeightedGrade(); // 비활성 등급 제외
    }

    private Vector3 GetWeaponSpawnPosition(int groupIndex, int groupCount, int itemIndex, int itemCount)
    {
        return GetSpawnPosition(weaponSpawnOffset + CalculateWeaponGroupBlockOffset(
            groupIndex,
            groupCount,
            itemIndex,
            itemCount,
            new Vector3(AuthoredWeaponGridSpacing, 0f, AuthoredWeaponGridSpacing)));
    }

    private Vector3 GetPrimaryWeaponSpawnPosition(int index, int count)
    {
        float centeredIndex = index - (count - 1) * 0.5f;
        return GetSpawnPosition(weaponSpawnOffset + Vector3.right * (centeredIndex * 0.8f));
    }

    public static Vector3 CalculateWeaponGroupBlockOffset(
        int groupIndex,
        int groupCount,
        int itemIndex,
        int itemCount,
        Vector3 spacing)
    {
        float spacingX = Mathf.Max(AuthoredWeaponGridSpacing, Mathf.Abs(spacing.x));
        float spacingZ = Mathf.Max(AuthoredWeaponGridSpacing, Mathf.Abs(spacing.z));
        int safeGroupCount = Mathf.Max(1, groupCount);
        int safeItemCount = Mathf.Max(1, itemCount);
        int groupsPerRow = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(safeGroupCount)));
        int groupRows = Mathf.CeilToInt(safeGroupCount / (float)groupsPerRow);
        int safeGroupIndex = Mathf.Clamp(groupIndex, 0, safeGroupCount - 1);
        int groupColumn = safeGroupIndex % groupsPerRow;
        int groupRow = safeGroupIndex / groupsPerRow;
        int blockColumns = Mathf.Min(WeaponBlockColumnCount, safeItemCount);
        int blockRows = Mathf.CeilToInt(safeItemCount / (float)blockColumns);
        float blockSpanX = (blockColumns - 1) * spacingX;
        float blockSpanZ = (blockRows - 1) * spacingZ;
        float groupStepX = blockSpanX + AuthoredWeaponGroupGap;
        float groupStepZ = blockSpanZ + AuthoredWeaponGroupGap;
        float centeredGroupColumn = groupColumn - (groupsPerRow - 1) * 0.5f;
        float centeredGroupRow = groupRow - (groupRows - 1) * 0.5f;
        int safeItemIndex = Mathf.Clamp(itemIndex, 0, safeItemCount - 1);
        int itemColumn = safeItemIndex % blockColumns;
        int itemRow = safeItemIndex / blockColumns;
        float centeredItemColumn = itemColumn - (blockColumns - 1) * 0.5f;
        float centeredItemRow = itemRow - (blockRows - 1) * 0.5f;
        return new Vector3(
            centeredGroupColumn * groupStepX + centeredItemColumn * spacingX,
            0f,
            centeredGroupRow * groupStepZ + centeredItemRow * spacingZ);
    }

    private Vector3 GetMoveSpeedPotionSpawnPosition(int index, int count)
    {
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
        int row = index / columns;
        int column = index % columns;
        float centeredX = column - (columns - 1) * 0.5f;
        int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
        float centeredZ = row - (rows - 1) * 0.5f;
        Vector3 gridOffset = new Vector3(centeredX * moveSpeedPotionSpawnSpacing.x, 0f, centeredZ * moveSpeedPotionSpawnSpacing.z);
        return GetSpawnPosition(moveSpeedPotionSpawnOffset + gridOffset);
    }

    private Vector3 GetSmallHealPotionSpawnPosition(int index, int count)
    {
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
        int row = index / columns;
        int column = index % columns;
        float centeredX = column - (columns - 1) * 0.5f;
        int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
        float centeredZ = row - (rows - 1) * 0.5f;
        Vector3 gridOffset = new Vector3(centeredX * smallHealPotionSpawnSpacing.x, 0f, centeredZ * smallHealPotionSpawnSpacing.z);
        return GetSpawnPosition(smallHealPotionSpawnOffset + gridOffset);
    }

    private void CreateWeaponPickup(ItemData item, Vector3 position)
    {
        WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickupFromExistingItem(
            item,
            position,
            inventory,
            player,
            pickupGradeVfxSet);
        PlaceAuthoredPickup(pickup, position);
        if (pickup == null)
            Debug.LogWarning("[ItemPickupSpawner] Weapon world pickup creation failed.", this);
    }

    private void PlaceAuthoredPickup(WorldItemPickup pickup, Vector3 position)
    {
        if (pickup != null)
            pickup.PlaceAuthored(position, gameObject.scene);
    }

    private Vector3 GetSpawnPosition(Vector3 offset)
    {
        Vector3 origin = authoredSpawnOrigin != null ? authoredSpawnOrigin.position
            : player != null ? player.position : transform.position;
        return origin + offset;
    }

    private void ResolveReferences()
    {
        if (inventory == null)
            inventory = FindFirstObjectByType<PlayerInventory>();

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            player = playerObject != null ? playerObject.transform : null;
        }
    }

    private ConsumableItemData ResolveSmallHealPotionData()
    {
        if (smallHealPotionItem is ConsumableItemData potionData)
            return potionData;

#if UNITY_EDITOR
        potionData = UnityEditor.AssetDatabase.LoadAssetAtPath<ConsumableItemData>("Assets/ProjectOverburst/03_Features/Items/Data/Items/Consumables/SmallHealPotion.asset");
        smallHealPotionItem = potionData;
        return potionData;
#else
        return null;
#endif
    }

    private readonly struct WeaponSpawnGroup
    {
        public readonly WeaponItemData weaponData;
        public readonly WeaponClass weaponClass;
        public readonly WeaponCombatFamily combatFamily;

        public WeaponSpawnGroup(WeaponItemData weaponData)
        {
            this.weaponData = weaponData;
            weaponClass = weaponData.weaponClass;
            combatFamily = weaponData.CombatFamily;
        }
    }
}
