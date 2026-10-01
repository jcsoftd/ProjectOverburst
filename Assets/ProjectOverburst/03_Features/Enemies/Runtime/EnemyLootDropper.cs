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

    [Header("Currency Drop")]
    [SerializeField] private bool dropGoldCurrency = true;
    [SerializeField] private CurrencyItemData goldCurrencyItem;
    [SerializeField] private int minGoldAmount = 1;
    [SerializeField] private int maxGoldAmount = 50;

    [Header("Weighted Drop Count")]
    [SerializeField] private bool useWeightedDropCount;
    [SerializeField, Range(0f, 1f)] private float zeroDropChance = 0.5f;
    [SerializeField, Range(0f, 1f)] private float oneDropChance = 0.4f;
    [SerializeField, Range(0f, 1f)] private float twoDropChance = 0.1f;
    [SerializeField] private int maxWeightedDropRollAttempts = 12;

    private static readonly ProfilerMarker ResolveMarker = new ProfilerMarker("Overburst.Loot.Resolve");
    private static readonly ProfilerMarker OriginMarker = new ProfilerMarker("Overburst.Loot.Origin");
    private static readonly ProfilerMarker GoldMarker = new ProfilerMarker("Overburst.Loot.Gold");
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
        if (encounter.IsRun)
        {
            EnemyRank rank = GetComponent<EnemyRank>();
            float rarePercent = BagQuality.EquippedBonus(BagStat.RareGradeWeight);
            DropFarmingItem(FlaskLootPolicy.Roll(rank, encounter.MapLevel, encounter.MapGrade, rarePercent), dropOrigin + dropOffset);
            DropFarmingItem(GearLootPolicy.Roll(rank, encounter.MapLevel, encounter.MapGrade, rarePercent), dropOrigin + dropOffset + Vector3.right * .35f);
            DropFarmingItem(BagFarmingLoot.RollBag(rank, encounter.MapLevel, encounter.MapGrade, rarePercent), dropOrigin + dropOffset + Vector3.left * .35f);
        }
        using (GoldMarker.Auto()) DropGoldCurrency(dropOrigin); // 테스트용 자동 획득 재화

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
        WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickup(StampLoot(item), position, targetInventory, player, pickupGradeVfxSet);
        if (pickup == null || !encounter.IsRun || !BagFarmingLoot.Eligible(item.baseData)) return;
        float chance = BagQuality.EquippedBonus(BagStat.ExtraItemDrop) * .01f;
        if (chance <= 0 || Random.value >= chance) return;
        ItemData extra = BagFarmingLoot.Extra(item, GetComponent<EnemyRank>(), encounter.MapLevel,
            encounter.MapGrade, BagQuality.EquippedBonus(BagStat.RareGradeWeight));
        // Direct factory call is intentional: an extra drop never rolls another extra.
        WorldItemDropFactory.CreateWorldPickup(StampLoot(extra), position + Vector3.forward * .35f, targetInventory, player, pickupGradeVfxSet);
    }

    private ItemData StampLoot(ItemData item)
    {
        if (item == null) return null;
        item.level = encounter.MapLevel;
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
        if (goldCurrencyItem == null)
            goldCurrencyItem = CurrencyItemRegistry.Get(CurrencyType.Gold);
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

    private void DropGoldCurrency(Vector3 dropOrigin)
    {
        if (!dropGoldCurrency)
            return;

        CurrencyItemData goldItem = goldCurrencyItem != null ? goldCurrencyItem : CurrencyItemRegistry.Get(CurrencyType.Gold);
        if (goldItem == null)
            return;

        int min = Mathf.Max(1, minGoldAmount);
        int max = Mathf.Max(min, maxGoldAmount);
        int amount = Random.Range(min, max + 1);
        if (encounter.IsRun)
            amount = BagFarmingLoot.CombatGoldAmount(amount, BagQuality.EquippedBonus(BagStat.CombatGold));
        Vector3 position = dropOrigin + dropOffset + GetScatterOffset(0, 2);
        WorldItemDropFactory.CreateCurrencyWorldPickupFromExistingItem(
            StampLoot(new ItemData(goldItem, encounter.MapLevel, ItemGrade.Common, amount)), position, targetInventory);
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
