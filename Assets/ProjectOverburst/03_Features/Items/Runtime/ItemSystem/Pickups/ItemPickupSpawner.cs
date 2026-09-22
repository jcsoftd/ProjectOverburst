using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ItemPickupSpawner : MonoBehaviour
{
    public const float MinimumComboGemCenterSpacing = 0.8f;
    public const float AuthoredComboGemGridSpacing = 1.2f;
    public const float AuthoredComboGemGroupGap = 3.5f;
    public const float AuthoredWeaponGridSpacing = 1.2f;
    public const float AuthoredWeaponGroupGap = 3.5f;
    public const float DefaultPickupScatterRadius = 0.16f;
    private const int FlaskColumns = 4;
    private const float FlaskSpacing = 1.25f;
    private static readonly Vector3 HideoutFlaskOffset = new Vector3(0f, 0.25f, -2.5f);

    private const int ComboGemBlockColumnCount = 2;
    private const int WeaponBlockColumnCount = 4;

    private const int MinimumWeaponCopiesPerClass = 2;
    public const int DefaultWeaponCopiesPerGroup = 10;
    private static readonly ItemGrade[] GuaranteedWeaponGrades = ItemGradeAvailabilityPolicy.GetEnabledGrades();
    private static readonly ItemGrade[] GuaranteedComboGemGrades = ElementComboGemGradePolicy.GetGenerationGrades();
    private bool configuredSpawnCompleted;

    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private Transform player;
    [SerializeField] private WeaponItemData testWeaponItem;
    [SerializeField] private WeaponItemData[] weaponItemAssets;
    [SerializeField] private ComboGemItemData[] testComboGemItems;
    [SerializeField] private BaseItemData moveSpeedPotionItem;
    [SerializeField] private BaseItemData smallHealPotionItem;

    [Header("VFX")]
    [SerializeField] private PickupGradeVfxSet pickupGradeVfxSet;

    [Header("Spawn")]
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private bool spawnTestWeaponOnStart = true;
    [SerializeField] private bool spawnRandomWeaponsOnStart;
    [SerializeField] private bool spawnMoveSpeedPotionOnStart = true;
    [SerializeField] private bool spawnSmallHealPotionOnStart = true;
    [SerializeField] private bool spawnComboGemsOnStart = true;
    [SerializeField] private int spawnWeaponCount = 1;
    [SerializeField] private int spawnComboGemCount = ElementComboGemGradePolicy.GenerationGradeCount;
    [SerializeField] private int moveSpeedPotionPickupCount = 15;
    [SerializeField] private int smallHealPotionPickupCount = 10;
    [SerializeField] private float spawnRadius = 3f;
    [SerializeField] private Vector3 weaponSpawnOffset = new Vector3(-0.8f, 0.25f, 1.9f);
    [SerializeField] private Vector3 moveSpeedPotionSpawnOffset = new Vector3(1.6f, 0.25f, -2.2f);
    [SerializeField] private Vector3 moveSpeedPotionSpawnSpacing = new Vector3(0.36f, 0f, 0.36f);
    [SerializeField] private Vector3 smallHealPotionSpawnOffset = new Vector3(2.8f, 0.25f, -2.2f);
    [SerializeField] private Vector3 smallHealPotionSpawnSpacing = new Vector3(0.36f, 0f, 0.36f);
    [SerializeField] private Vector3 comboGemSpawnOffset = new Vector3(0f, 0.25f, 2.4f);
    [SerializeField] private Vector3 comboGemSpawnSpacing = new Vector3(AuthoredComboGemGridSpacing, 0f, AuthoredComboGemGridSpacing);

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

        configuredSpawnCompleted = true;
        ResolveReferences();

        if (spawnTestWeaponOnStart)
            SpawnTestWeaponPickup();

        if (spawnRandomWeaponsOnStart)
            SpawnRandomWeaponPickups();

        if (spawnMoveSpeedPotionOnStart)
            SpawnMoveSpeedPotionPickup();

        if (spawnSmallHealPotionOnStart)
            SpawnSmallHealPotionPickup();

        if (gameObject.scene.name == PersistentSceneFlow.HideoutSceneName)
            SpawnHideoutFlaskPickups();

        // Element gems are retired; retain serialized fixture fields for legacy asset compatibility.
    }

    public void SpawnHideoutFlaskPickups()
    {
        if (gameObject.scene.name != PersistentSceneFlow.HideoutSceneName)
            return;

        ResolveReferences();
        FlaskItemData[] catalog = FlaskLootPolicy.Catalog;
        foreach (FlaskKind kind in System.Enum.GetValues(typeof(FlaskKind)))
        {
            FlaskItemData data = null;
            for (int i = 0; i < catalog.Length; i++)
                if (catalog[i] != null && catalog[i].kind == kind)
                {
                    data = catalog[i];
                    break;
                }

            if (data == null)
            {
                Debug.LogError($"[ItemPickupSpawner] 하이드아웃 물약 자산 누락: {kind}", this);
                continue;
            }

            int index = (int)kind;
            int column = index % FlaskColumns;
            int row = index / FlaskColumns;
            Vector3 offset = HideoutFlaskOffset + new Vector3(
                (column - (FlaskColumns - 1) * 0.5f) * FlaskSpacing,
                0f,
                -row * FlaskSpacing);
            Vector3 position = GetSpawnPosition(offset);
            ItemData item = new ItemData(data, 1, RollVtpGrade());
            WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickupFromExistingItem(
                item, position, inventory, player, pickupGradeVfxSet);
            PlaceAuthoredPickup(pickup, position);
            if (pickup == null)
                Debug.LogError($"[ItemPickupSpawner] 하이드아웃 물약 생성 실패: {kind}", this);
        }
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
            int swapIndex = Random.Range(0, i + 1);
            (grades[i], grades[swapIndex]) = (grades[swapIndex], grades[i]);
        }

        return grades;
    }

    [ContextMenu("Spawn/Test Combo Gem Pickups")]
    public void SpawnTestComboGemPickups()
    {
        ResolveReferences();

        if (testComboGemItems == null)
            return;

        for (int i = 0; i < testComboGemItems.Length; i++)
        {
            ComboGemItemData comboGemData = testComboGemItems[i];
            if (comboGemData == null)
                continue;

            ItemData item = new ItemData(comboGemData, 1, ItemGrade.Rare);
            Vector3 position = GetComboGemSpawnPosition(i, 0, testComboGemItems.Length, 1);
            CreatePickupObject("WorldPickup_TestComboGem_" + comboGemData.GemType, item, position, new Vector3(0.24f, 0.24f, 0.24f), comboGemData.color, PrimitiveType.Sphere);
        }
    }

    [ContextMenu("Spawn Random Combo Gem Pickups")]
    public void SpawnRandomComboGemPickups()
    {
        ResolveReferences();

        if (testComboGemItems == null || testComboGemItems.Length == 0)
            return;

        List<ItemGrade> grades = BuildComboGemGradeSequence(spawnComboGemCount);
        List<ElementComboGemItemData> comboGemAssets = CollectValidElementComboGemAssets();
        if (comboGemAssets.Count == 0)
        {
            Debug.LogError("[ItemPickupSpawner] 유효한 원소 콤보 보석이 없습니다.", this);
            return;
        }

        HashSet<string> runtimeInstanceIds = new HashSet<string>();
        int spawnedCount = 0;
        for (int elementIndex = 0; elementIndex < comboGemAssets.Count; elementIndex++)
        {
            ElementComboGemItemData comboGemData = comboGemAssets[elementIndex];
            for (int gradeIndex = 0; gradeIndex < grades.Count; gradeIndex++)
            {
                ItemGrade grade = grades[gradeIndex];
                ItemData item = new ItemData(comboGemData, 1, grade);
                if (!ValidateComboGemSpawnItem(comboGemData, item))
                    continue;
                if (!runtimeInstanceIds.Add(item.runtimeInstanceId))
                {
                    Debug.LogError("[ItemPickupSpawner] 콤보 보석 runtimeInstanceId가 중복됐습니다.", this);
                    continue;
                }

                Vector3 position = GetComboGemSpawnPosition(elementIndex, gradeIndex, comboGemAssets.Count, grades.Count);
                WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickupFromExistingItem(
                    item,
                    position,
                    inventory,
                    player,
                    pickupGradeVfxSet);
                PlaceAuthoredPickup(pickup, position);
                if (pickup == null || !ReferenceEquals(pickup.RuntimeItem, item))
                {
                    Debug.LogError("[ItemPickupSpawner] 콤보 보석 ItemData 전달이 보존되지 않았습니다.", this);
                    continue;
                }

                spawnedCount++;
            }
        }

        int expectedCount = comboGemAssets.Count * grades.Count;
        if (spawnedCount != expectedCount)
            Debug.LogError($"[ItemPickupSpawner] 콤보 보석 생성 수가 올바르지 않습니다. {spawnedCount}/{expectedCount}", this);

        if (spawnedCount == 0)
            Debug.LogError("[ItemPickupSpawner] 유효한 콤보 보석을 생성하지 못했습니다.", this);
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

    [ContextMenu("Test/Add Combo Gems To Inventory")]
    public void AddTestComboGemsToInventory()
    {
        ResolveReferences();

        if (inventory == null || testComboGemItems == null)
            return;

        for (int i = 0; i < testComboGemItems.Length; i++)
        {
            if (testComboGemItems[i] != null)
                inventory.AddItem(new ItemData(testComboGemItems[i], 1, ItemGrade.Rare));
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

    private List<ElementComboGemItemData> CollectValidElementComboGemAssets()
    {
        List<ElementComboGemItemData> result = new List<ElementComboGemItemData>();
        HashSet<WeaponElement> elements = new HashSet<WeaponElement>();
        if (testComboGemItems == null)
            return result;

        for (int i = 0; i < testComboGemItems.Length; i++)
        {
            if (!(testComboGemItems[i] is ElementComboGemItemData elementGemData)
                || !WeaponContentPolicy.IsAllowedItemData(elementGemData)
                || !elementGemData.TryGetElementDefinition(out WeaponElement element)
                || !IsActiveHideoutElement(element)
                || !elements.Add(element))
            {
                continue;
            }

            result.Add(elementGemData);
        }

        result.Sort(CompareElementComboGemAssets); // 실제 원소 ID 기준 정렬
        return result;
    }

    private static bool IsActiveHideoutElement(WeaponElement element)
    {
        return element == WeaponElement.Fire
            || element == WeaponElement.Water
            || element == WeaponElement.Ice
            || element == WeaponElement.Electric
            || element == WeaponElement.Earth; // 하이드아웃 신규 생성 원소를 명시한다
    }

    private static int CompareElementComboGemAssets(ElementComboGemItemData left, ElementComboGemItemData right)
    {
        left.TryGetElementDefinition(out WeaponElement leftElement);
        right.TryGetElementDefinition(out WeaponElement rightElement);
        return ((int)leftElement).CompareTo((int)rightElement);
    }

    private List<ItemGrade> BuildComboGemGradeSequence(int count)
    {
        int targetCount = Mathf.Clamp(count, 1, GuaranteedComboGemGrades.Length);
        List<ItemGrade> grades = new List<ItemGrade>(targetCount);

        for (int i = 0; i < targetCount && i < GuaranteedComboGemGrades.Length; i++)
            grades.Add(GuaranteedComboGemGrades[i]); // 속성 보석 생성 정책 사용

        return grades;
    }

    private bool ValidateComboGemSpawnItem(ComboGemItemData comboGemData, ItemData item)
    {
        if (comboGemData == null || item == null)
            return false;

        if (!(comboGemData is ElementComboGemItemData elementGemData)
            || !elementGemData.TryGetElementDefinition(out _)
            || !ReferenceEquals(item.baseData, comboGemData))
            return false;

        ItemGrade grade = item.grade;
        if (!TryGetComboGemOptionValue(item, ComboGemRandomOptionType.ElementDamageIncrease, out float rolledElementDamage))
        {
            Debug.LogError("[ItemPickupSpawner] 원소 콤보 보석에 원소 피해 옵션이 없습니다.", this);
            return false;
        }

        string runtimeInstanceId = item.runtimeInstanceId;
        item.EnsureRuntimeState(); // 생성 롤 보존 확인
        item.EnsureAcquisitionOrder();
        if (string.IsNullOrEmpty(runtimeInstanceId)
            || item.runtimeInstanceId != runtimeInstanceId
            || item.grade != grade
            || !ReferenceEquals(item.baseData, comboGemData))
            return false;

        return TryGetComboGemOptionValue(item, ComboGemRandomOptionType.ElementDamageIncrease, out float preservedElementDamage)
            && Mathf.Approximately(rolledElementDamage, preservedElementDamage);
    }

    private bool TryGetComboGemOptionValue(ItemData item, ComboGemRandomOptionType optionType, out float value)
    {
        value = 0f;
        if (item == null || item.comboGemOptions == null)
            return false;

        for (int i = 0; i < item.comboGemOptions.Count; i++)
        {
            ComboGemRolledOption option = item.comboGemOptions[i];
            if (option != null && option.optionType == optionType)
            {
                value = option.value;
                return true;
            }
        }

        return false;
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

    private Vector3 GetComboGemSpawnPosition(int elementIndex, int gradeIndex, int elementCount, int gradeCount)
    {
        Vector3 gridOffset = CalculateComboGemGridOffset(
            elementIndex,
            gradeIndex,
            elementCount,
            gradeCount,
            comboGemSpawnSpacing);
        return GetSpawnPosition(comboGemSpawnOffset + gridOffset);
    }

    public static Vector3 CalculateComboGemGridOffset(
        int elementIndex,
        int gradeIndex,
        int elementCount,
        int gradeCount,
        Vector3 spacing)
    {
        float spacingX = Mathf.Max(AuthoredComboGemGridSpacing, Mathf.Abs(spacing.x));
        float spacingZ = Mathf.Max(AuthoredComboGemGridSpacing, Mathf.Abs(spacing.z));
        int safeElementCount = Mathf.Max(1, elementCount);
        int safeGradeCount = Mathf.Max(1, gradeCount);
        int groupsPerRow = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(safeElementCount)));
        int groupRows = Mathf.CeilToInt(safeElementCount / (float)groupsPerRow);
        int safeElementIndex = Mathf.Clamp(elementIndex, 0, safeElementCount - 1);
        int groupColumn = safeElementIndex % groupsPerRow;
        int groupRow = safeElementIndex / groupsPerRow;
        int blockColumns = Mathf.Min(ComboGemBlockColumnCount, safeGradeCount);
        int blockRows = Mathf.CeilToInt(safeGradeCount / (float)blockColumns);
        float blockSpanX = (blockColumns - 1) * spacingX;
        float blockSpanZ = (blockRows - 1) * spacingZ;
        float groupStepX = blockSpanX + AuthoredComboGemGroupGap;
        float groupStepZ = blockSpanZ + AuthoredComboGemGroupGap;
        float centeredGroupColumn = groupColumn - (groupsPerRow - 1) * 0.5f;
        float centeredGroupRow = groupRow - (groupRows - 1) * 0.5f;
        int safeGradeIndex = Mathf.Clamp(gradeIndex, 0, safeGradeCount - 1);
        int gradeColumn = safeGradeIndex % blockColumns;
        int gradeRow = safeGradeIndex / blockColumns;
        float centeredGradeColumn = gradeColumn - (blockColumns - 1) * 0.5f;
        float centeredGradeRow = gradeRow - (blockRows - 1) * 0.5f;
        return new Vector3(
            centeredGroupColumn * groupStepX + centeredGradeColumn * spacingX,
            0f,
            centeredGroupRow * groupStepZ + centeredGradeRow * spacingZ);
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
        Vector3 origin = player != null ? player.position : transform.position;
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
