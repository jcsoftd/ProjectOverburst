using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

[RequireComponent(typeof(CombatHealth))]
public class EnemyLootDropper : MonoBehaviour // 적 드랍
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private DropTable dropTable;
    [SerializeField] private PlayerInventory targetInventory;
    [SerializeField] private Transform player;
    [SerializeField] private PickupGradeVfxSet pickupGradeVfxSet;
    [SerializeField] private Vector3 dropOffset = new Vector3(0f, 0.2f, 0f);
    [SerializeField] private float scatterRadius = 0.45f;
    [SerializeField] private int maxDroppedItemCount = -1;

    [Header("Weighted Drop Count")]
    [SerializeField] private bool useWeightedDropCount;
    [SerializeField, Range(0f, 1f)] private float zeroDropChance = 0.5f;
    [SerializeField, Range(0f, 1f)] private float oneDropChance = 0.4f;
    [SerializeField, Range(0f, 1f)] private float twoDropChance = 0.1f;
    [SerializeField] private int maxWeightedDropRollAttempts = 12;

    private static readonly ProfilerMarker ResolveMarker = new ProfilerMarker("Overburst.Loot.Resolve");
    private static readonly ProfilerMarker OriginMarker = new ProfilerMarker("Overburst.Loot.Origin");
    private static readonly ProfilerMarker ItemsMarker = new ProfilerMarker("Overburst.Loot.Items");
    private bool dropped; // 중복 드랍 방지
    private EncounterContext encounter = EncounterContext.Test;
    public void ConfigureEncounter(EncounterContext context) => encounter = context ?? EncounterContext.Test;

    private DropTable authoredDropTable;
    private PlayerInventory authoredTargetInventory;
    private Transform authoredPlayer;
    private PickupGradeVfxSet authoredPickupGradeVfxSet;
    private bool authoredPresentationCaptured;

    private void Awake()
    {
        CaptureAuthoredPresentation();
        if (health == null)
            health = GetComponent<CombatHealth>();

        ResolveReferences();
    }

    private void OnEnable()
    {
        dropped = false;
        if (health != null)
            health.OnDead += HandleDead;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDead -= HandleDead;
    }

    public void Configure(DropTable table, PlayerInventory inventory, Transform playerTransform, PickupGradeVfxSet gradeVfxSet)
    {
        CaptureAuthoredPresentation();
        dropTable = table;
        targetInventory = inventory;
        player = playerTransform;
        pickupGradeVfxSet = gradeVfxSet;
    }

    public void ResetForPool()
    {
        CaptureAuthoredPresentation();
        encounter = EncounterContext.Test;
        dropTable = authoredDropTable;
        targetInventory = authoredTargetInventory;
        player = authoredPlayer;
        pickupGradeVfxSet = authoredPickupGradeVfxSet;
        dropped = false;
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (dropped || !encounter.CanGrantRewards)
            return;

        dropped = true;

        using (ResolveMarker.Auto()) ResolveReferences();

        Vector3 dropOrigin;
        using (OriginMarker.Auto()) dropOrigin = GetDropOriginPosition(); // 드랍 기준점
        if (encounter.IsOutdoor)
        {
            var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
            var registry = Resources.Load<Overburst.Persistence.AccountContentRegistry>(Overburst.Persistence.AccountContentRegistry.ResourcePath);
            var table = Resources.Load<EnemyThemeTable>("Enemies/Themes/Tables/GraveHunt");
            var enemy = GetComponent<EnemyActor>();
            var tier = EnemyThemeTier.Small;
            if (table && enemy)
                foreach (var entry in table.Entries) if (entry.definition == enemy.Definition) { tier = entry.tier; break; }
            DropFarmingItem(MapDropPolicy.Roll(definition, registry, encounter.MapLevel, tier, null, outdoor: true), dropOrigin + dropOffset + Vector3.forward * .7f);
        }
        if (MapFarmingLoot.Applies(encounter.Map))
        {
            var rank = GetComponent<EnemyRank>();
            var grade = rank != null ? rank.GradeType : EnemyGradeType.Normal;
            float rarePercent = BagQuality.EquippedBonus(BagStat.RareGradeWeight);
            for (int kind = 0; kind < 5; kind++)
                DropFarmingItem(MapFarmingLoot.Roll((MapFarmingKind)kind, grade, encounter.Map, rarePercent),
                    dropOrigin + dropOffset + GetScatterOffset(kind, 5));
        }
        else if (encounter.IsRun || encounter.IsOutdoor)
        {
            EnemyRank rank = GetComponent<EnemyRank>();
            float rarePercent = BagQuality.EquippedBonus(BagStat.RareGradeWeight);
            var maximum = encounter.IsOutdoor ? ItemGrade.Legendary : ItemGrade.Cursed;
            DropFarmingItem(FlaskLootPolicy.Roll(rank, encounter.MapLevel, encounter.MapGrade, rarePercent, maximum), dropOrigin + dropOffset);
            DropFarmingItem(ElementGemLootPolicy.Roll(rank, encounter.MapLevel, encounter.MapGrade, rarePercent, maximum), dropOrigin + dropOffset + Vector3.forward * .35f);
            DropFarmingItem(GearLootPolicy.Roll(rank, encounter.MapLevel, encounter.MapGrade, rarePercent, maximum), dropOrigin + dropOffset + Vector3.right * .35f);
            DropFarmingItem(BagFarmingLoot.RollBag(rank, encounter.MapLevel, encounter.MapGrade, rarePercent, maximum), dropOrigin + dropOffset + Vector3.left * .35f);
            DropFarmingItem(WeaponLootPolicy.Roll(rank, encounter.MapLevel, encounter.MapGrade, rarePercent, maximum), dropOrigin + dropOffset + Vector3.back * .35f);
        }

        if (dropTable == null)
            return;

        using var itemsScope = ItemsMarker.Auto();
        List<ItemData> drops = CreateDropList(); // 드랍 목록
        if (drops == null || drops.Count == 0)
            return;

        for (int i = 0; i < drops.Count; i++)
        {
            Vector3 offset = dropOffset + GetScatterOffset(i, drops.Count); // 흩뿌림
            DropFarmingItem(drops[i], dropOrigin + offset);
        }
    }

    private void DropFarmingItem(ItemData item, Vector3 position)
    {
        if (item == null) return;
        if (item.baseData is CurrencyItemData currency && currency.currencyType == CurrencyType.Gold) return;
        // Authored tables can contain a fixed-grade definition. Never relabel its roll as a lower grade.
        if (encounter.IsOutdoor && item.grade > ItemGrade.Legendary) return;
        position = ConstrainCaveDrop(position);
        WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickup(StampLoot(item), position, targetInventory, player, pickupGradeVfxSet);
        if (pickup == null || !encounter.IsRun || !BagFarmingLoot.Eligible(item.baseData)) return;
        float chance = BagQuality.EquippedBonus(BagStat.ExtraItemDrop) * .01f;
        if (chance <= 0 || Random.value >= chance) return;
        var rank = GetComponent<EnemyRank>();
        float rarePercent = BagQuality.EquippedBonus(BagStat.RareGradeWeight);
        ItemData extra = MapFarmingLoot.Applies(encounter.Map)
            ? MapFarmingLoot.Extra(item, rank != null ? rank.GradeType : EnemyGradeType.Normal, encounter.Map, rarePercent)
            : BagFarmingLoot.Extra(item, rank, encounter.MapLevel, encounter.MapGrade, rarePercent);
        // Direct factory call is intentional: an extra drop never rolls another extra.
        WorldItemDropFactory.CreateWorldPickup(StampLoot(extra), ConstrainCaveDrop(position + Vector3.forward * .35f), targetInventory, player, pickupGradeVfxSet);
    }

    private ItemData StampLoot(ItemData item)
    {
        if (item == null) return null;
        item.level = encounter.IsOutdoor && item.mapState != null ? item.mapState.level : encounter.MapLevel;
        item.originRunId = encounter.RunId;
        return item;
    }

    private List<ItemData> CreateDropList()
    {
        if (!useWeightedDropCount)
        {
            List<ItemData> drops = dropTable.RollDrops(encounter.IsRun ? BagQuality.EquippedBonus(BagStat.RareGradeWeight) : 0);
            ApplyDropLimit(drops);
            return drops;
        }

        int targetCount = RollWeightedDropCount(); // 목표 개수
        if (maxDroppedItemCount >= 0)
            targetCount = Mathf.Min(targetCount, maxDroppedItemCount);

        if (targetCount <= 0)
            return new List<ItemData>();

        List<ItemData> weightedDrops = new List<ItemData>(targetCount); // 누적 드랍
        int attempts = Mathf.Max(1, maxWeightedDropRollAttempts); // 재시도 수

        for (int i = 0; i < attempts && weightedDrops.Count < targetCount; i++)
        {
            List<ItemData> rolledDrops = dropTable.RollDrops(encounter.IsRun ? BagQuality.EquippedBonus(BagStat.RareGradeWeight) : 0);
            if (rolledDrops == null || rolledDrops.Count == 0)
                continue;

            weightedDrops.AddRange(rolledDrops);
        }

        TrimDropList(weightedDrops, targetCount);
        return weightedDrops;
    }

    private int RollWeightedDropCount()
    {
        float zeroWeight = Mathf.Max(0f, zeroDropChance);
        float oneWeight = Mathf.Max(0f, oneDropChance);
        float twoWeight = Mathf.Max(0f, twoDropChance);
        float totalWeight = zeroWeight + oneWeight + twoWeight;

        if (totalWeight <= 0f)
            return 0;

        float roll = Random.value * totalWeight; // 가중치 roll
        if (roll < zeroWeight)
            return 0;

        roll -= zeroWeight;
        return roll < oneWeight ? 1 : 2;
    }

    private Vector3 GetDropOriginPosition()
    {
        Bounds bounds; // 적 영역

        if (TryGetObjectBounds(out bounds))
            return new Vector3(transform.position.x, bounds.center.y, transform.position.z);

        return transform.position + Vector3.up * 0.75f;
    }

    private void ApplyDropLimit(List<ItemData> drops)
    {
        if (drops == null || maxDroppedItemCount < 0 || drops.Count <= maxDroppedItemCount)
            return;

        TrimDropList(drops, maxDroppedItemCount);
    }

    private void TrimDropList(List<ItemData> drops, int targetCount)
    {
        if (drops == null || targetCount < 0 || drops.Count <= targetCount)
            return;

        ShuffleDrops(drops);
        drops.RemoveRange(targetCount, drops.Count - targetCount);
    }

    private void ShuffleDrops(List<ItemData> drops)
    {
        if (drops == null)
            return;

        for (int i = 0; i < drops.Count; i++)
        {
            int swapIndex = Random.Range(i, drops.Count);
            ItemData temp = drops[i];
            drops[i] = drops[swapIndex];
            drops[swapIndex] = temp;
        }
    }

    private bool TryGetObjectBounds(out Bounds bounds)
    {
        bounds = new Bounds(transform.position, Vector3.zero);
        bool hasBounds = false; // bounds 보유

        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider enemyCollider = colliders[i];
            if (enemyCollider == null)
                continue;

            if (!hasBounds)
            {
                bounds = enemyCollider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(enemyCollider.bounds);
            }
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer enemyRenderer = renderers[i];
            if (enemyRenderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = enemyRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(enemyRenderer.bounds);
            }
        }

        return hasBounds;
    }

    private Vector3 GetScatterOffset(int index, int count)
    {
        if (scatterRadius <= 0f || count <= 1)
            return Vector3.zero;

        float angle = 360f * index / count; // 분산 각도
        return new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * scatterRadius;
    }

    // Missing references are resolved once per frame across the whole death batch.
    // Authored/Configure references remain authoritative; destroyed Unity objects are retried.
    private static PlayerInventory sharedInventory;
    private static Transform sharedPlayer;
    private static int inventoryLookupFrame = -1, playerLookupFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedReferences()
    {
        sharedInventory = null; sharedPlayer = null;
        inventoryLookupFrame = playerLookupFrame = -1;
    }

    private void ResolveReferences()
    {
        if (targetInventory == null)
        {
            if (sharedInventory == null && inventoryLookupFrame != Time.frameCount)
            {
                inventoryLookupFrame = Time.frameCount;
                sharedInventory = PlayerAccountInventoryService.FindSharedInventory();
            }
            targetInventory = sharedInventory;
        }
        if (player != null) return;
        if ((sharedPlayer == null || !sharedPlayer.gameObject.activeInHierarchy)
            && playerLookupFrame != Time.frameCount)
        {
            playerLookupFrame = Time.frameCount;
            var actor = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null;
            var playerObject = actor != null ? actor.gameObject : GameObject.FindGameObjectWithTag("Player");
            sharedPlayer = playerObject != null ? playerObject.transform : null;
        }
        player = sharedPlayer;
    }

    private Vector3 ConstrainCaveDrop(Vector3 position)
    {
        // Scatter starts from the monster's feet, even when a tall model emits loot high above them.
        return Overburst.Caves.CaveFallProtection.TryDropLanding(transform.position, position,
            Mathf.Max(.05f, position.y - transform.position.y), out var safe) ? safe : position;
    }

    private void CaptureAuthoredPresentation()
    {
        if (authoredPresentationCaptured)
            return;

        authoredDropTable = dropTable;
        authoredTargetInventory = targetInventory;
        authoredPlayer = player;
        authoredPickupGradeVfxSet = pickupGradeVfxSet;
        authoredPresentationCaptured = true;
    }
}
