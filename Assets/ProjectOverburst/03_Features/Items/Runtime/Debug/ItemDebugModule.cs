#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Overburst.DebugTools;
using Overburst.Persistence;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// 아이템·경제 탭(90C 7.5): 아이템 만들기, 드롭 시험(몬스터 드롭 규칙 그대로), 재화, 바닥 정리, 옛 픽업 버튼, 상인.
/// 만든 아이템과 재화는 계정에 저장되므로 실제 계정이면 한 번 더 묻는다.
/// </summary>
internal static class ItemDebugModule
{
    internal enum Category { All, Weapon, Gear, Flask, Consumable, Map, Bag }
    internal enum Destination { Floor, Inventory, Stash, Equip }

    private const int SimulationCount = 1000;
    private static readonly ItemGrade[] GradeOrder =
    {
        ItemGrade.Common, ItemGrade.Uncommon, ItemGrade.Rare, ItemGrade.Epic,
        ItemGrade.Legendary, ItemGrade.Artifact, ItemGrade.Mythic, ItemGrade.Cursed
    };

    private static readonly List<BaseItemData> filtered = new List<BaseItemData>();
    private static List<BaseItemData> catalog;
    private static List<ItemGrade> enabledGrades;
    private static Category category;
    private static Category filteredFor = (Category)(-1);
    private static BaseItemData selected;
    private static int level = 1;
    private static ItemGrade grade = ItemGrade.Common;
    private static int count = 1;
    private static Destination destination = Destination.Floor;

    private static EnemyGradeType dropMonster = EnemyGradeType.Normal;
    private static int dropMapLevel = 10;
    private static ItemGrade dropMapGrade = ItemGrade.Common;
    private static int[] simulated;
    private static int simulatedGear;
    private static int simulatedFlask;
    private static string simulationSummary = "아직 모의하지 않았어요";

    private static CurrencyType currency = CurrencyType.Gold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        filtered.Clear();
        catalog = null;
        enabledGrades = null;
        category = Category.All;
        filteredFor = (Category)(-1);
        selected = null;
        level = 1;
        grade = ItemGrade.Common;
        count = 1;
        destination = Destination.Floor;
        dropMonster = EnemyGradeType.Normal;
        dropMapLevel = 10;
        dropMapGrade = ItemGrade.Common;
        simulated = null;
        simulationSummary = "아직 모의하지 않았어요";
        currency = CurrencyType.Gold;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        RegisterCreate();
        RegisterDropTest();
        RegisterCurrency();
        RegisterFloor();
        RegisterMerchant();
    }

    // ── 아이템 만들기 ─────────────────────────────────────────────

    private static void RegisterCreate()
    {
        DebugSection s = DebugRegistry.Section(DebugTabs.Items, "아이템 만들기", 10, "계정 저장 콘텐츠 목록에서 고른다");
        s.Choice("분류", () => category, value => { category = value; selected = null; }, CategoryLabel)
            .WithId("items.create.category")
            .NoPreset();
        s.Picker("아이템", Filtered, ItemLabel, () => Selected, value => selected = value)
            .WithId("items.create.item")
            .Keywords("아이템", "item", "무기", "장비");
        s.Integer("레벨", () => level, value => level = value, 1, OverburstGrowthRules.MaximumLevel)
            .WithId("items.create.level")
            .NoPreset();
        s.Choice("등급", () => grade, value => grade = value, ItemTooltipFormatter.GetGradeName, () => EnabledGrades)
            .WithId("items.create.grade")
            .NoPreset()
            .Tip("쓰지 않는 등급(저주)은 목록에서 뺐다.");
        s.Integer("개수", () => count, value => count = value, 1, 20)
            .WithId("items.create.count")
            .NoPreset();
        s.Choice("넣을 곳", () => destination, value => destination = value, DestinationLabel)
            .WithId("items.create.destination")
            .NoPreset()
            .Tip("장착은 무기만. 바닥은 플레이어 주변에 원형으로 흩는다.");
        s.Button(() => $"만들기  ·  {ItemLabelShort(Selected)} ×{count}", Create)
            .WithId("items.create.go")
            .EnabledWhen(() => Selected != null && DebugTeleport.HasPlayer, "플레이어가 있는 씬에서 아이템을 고르세요")
            .ConfirmOnRealAccount()
            .Keywords("생성", "create", "spawn");
    }

    private static IReadOnlyList<BaseItemData> Filtered()
    {
        EnsureCatalog();
        if (filteredFor == category)
            return filtered;
        filtered.Clear();
        for (int i = 0; i < catalog.Count; i++)
        {
            if (category == Category.All || CategoryOf(catalog[i]) == category)
                filtered.Add(catalog[i]);
        }
        filteredFor = category;
        return filtered;
    }

    private static BaseItemData Selected
    {
        get
        {
            IReadOnlyList<BaseItemData> list = Filtered();
            if (selected != null && Contains(list, selected))
                return selected;
            return list.Count > 0 ? list[0] : null;
        }
    }

    private static IReadOnlyList<ItemGrade> EnabledGrades
    {
        get
        {
            if (enabledGrades != null)
                return enabledGrades;
            enabledGrades = new List<ItemGrade>();
            foreach (ItemGrade value in GradeOrder)
            {
                if (ItemGradeAvailabilityPolicy.IsEnabled(value))
                    enabledGrades.Add(value);
            }
            return enabledGrades;
        }
    }

    private static void EnsureCatalog()
    {
        if (catalog != null)
            return;
        catalog = new List<BaseItemData>();
        AccountContentRegistry registry = Registry;
        if (registry == null)
            return;
        var seen = new HashSet<BaseItemData>();
        foreach (AccountContentEntry entry in registry.Entries)
        {
            if (entry?.asset is BaseItemData data && !(data is CurrencyItemData)
                && WeaponContentPolicy.IsAllowedItemData(data) && seen.Add(data))
                catalog.Add(data);
        }
        catalog.Sort((a, b) =>
        {
            int compare = CategoryOf(a).CompareTo(CategoryOf(b));
            return compare != 0 ? compare : string.CompareOrdinal(DisplayName(a), DisplayName(b));
        });
    }

    // 계정 세션이 쓰는 것과 같은 Resources 에셋이다(AccountBootstrap도 이 경로로 읽는다).
    private static AccountContentRegistry Registry
        => Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);

    private static DebugResult Create()
    {
        BaseItemData data = Selected;
        if (data == null)
            return DebugResult.Fail("아이템을 고르세요");
        PlayerActorRuntime actor = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null;
        if (actor == null)
            return DebugResult.Fail("플레이어가 없어요");
        if (destination == Destination.Equip && !(data is WeaponItemData))
            return DebugResult.Fail("장착은 무기만 돼요");

        int made = 0;
        for (int i = 0; i < count; i++)
        {
            ItemData item = MakeItem(data, level, grade);
            if (item == null)
                return DebugResult.Fail($"만들지 못했어요({made}/{count}개 완료)");
            if (!Place(item, actor, i, count))
                return made == 0
                    ? DebugResult.Fail(DestinationFailure())
                    : DebugResult.Ok($"{made}/{count}개 · 나머지는 {DestinationFailure()}");
            made++;
        }
        return DebugResult.Ok($"{DestinationLabel(destination)}에 {made}개");
    }

    /// <summary>레벨·등급을 그대로 쓴다(WorldItemDropFactory.CreateRuntimeItem은 레벨을 1로 고정해서 쓰지 않는다).</summary>
    internal static ItemData MakeItem(BaseItemData data, int itemLevel, ItemGrade itemGrade)
    {
        var item = new ItemData(data, itemLevel, itemGrade);
        if (data is MapItemData map)
        {
            AccountContentRegistry registry = Registry;
            if (registry == null)
                return null;
            // MapDropPolicy.Roll과 같은 지도 상태. 런 밖에서 만들어 originRunId는 비운다.
            item.mapState = new MapInstanceState
            {
                mapContentId = registry.IdFor(map),
                monsterThemeId = MapThemeCatalog.RollThemeId(),
                level = item.level,
                grade = itemGrade,
                options = MapOptionPolicy.Roll(itemGrade)
            };
        }
        return item;
    }

    private static bool Place(ItemData item, PlayerActorRuntime actor, int index, int total)
    {
        switch (destination)
        {
            case Destination.Inventory:
                return actor.Inventory != null && actor.Inventory.AddItem(item);
            case Destination.Stash:
            {
                PlayerStash stash = PlayerAccountInventoryService.SharedStash;
                int slot = stash != null ? stash.FindFirstEmptySlot() : -1;
                return slot >= 0 && stash.SetItemAt(slot, item);
            }
            case Destination.Equip:
                return actor.Equipment != null && actor.Equipment.EquipWeaponItem(item);
            default:
                return WorldItemDropFactory.CreateWorldPickup(item, ScatterPosition(actor.transform, index, total),
                    actor.Inventory, actor.transform) != null;
        }
    }

    internal static Vector3 ScatterPosition(Transform player, int index, int total)
    {
        float radius = total <= 1 ? 1.6f : 1.8f + 0.12f * total;
        float angle = total <= 1 ? 0f : index * Mathf.PI * 2f / total;
        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;
        Quaternion turn = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up);
        return player.position + turn * forward.normalized * radius + Vector3.up * 0.45f;
    }

    private static string DestinationFailure()
    {
        switch (destination)
        {
            case Destination.Inventory: return "인벤토리가 가득 찼어요";
            case Destination.Stash: return "창고가 가득 찼어요";
            case Destination.Equip: return "장착하지 못했어요";
            default: return "바닥에 놓지 못했어요";
        }
    }

    // ── 드롭 시험 ────────────────────────────────────────────────

    private static void RegisterDropTest()
    {
        DebugSection s = DebugRegistry.Section(DebugTabs.Items, "드롭 시험", 20, "EnemyLootDropper와 같은 규칙");
        s.Choice("몬스터 등급", () => dropMonster, value => dropMonster = value, MonsterLabel)
            .WithId("items.drop.monster")
            .NoPreset();
        s.Integer("지도 레벨", () => dropMapLevel, value => dropMapLevel = value, 1, OverburstGrowthRules.MaximumLevel)
            .WithId("items.drop.mapLevel")
            .NoPreset();
        s.Choice("지도 등급", () => dropMapGrade, value => dropMapGrade = value, ItemTooltipFormatter.GetGradeName, () => EnabledGrades)
            .WithId("items.drop.mapGrade")
            .NoPreset();
        s.Buttons("굴리기")
            .Add("1회 굴려 바닥에", RollOnce)
            .Add($"{SimulationCount}회 모의", Simulate)
            .WithId("items.drop.roll")
            .EnabledWhen(() => DebugTeleport.HasPlayer, "플레이어가 있는 씬에서만")
            .Tip("모의는 아이템을 만들지 않고 등급 분포만 센다. 난수 상태는 모의 전으로 되돌린다. 지도 버프(MapRunBuffs)가 있으면 확률에 반영된다.");
        s.Bar("모의 등급 분포", SimulationBar)
            .WithId("items.drop.distribution")
            .Pinnable();
        s.Readout("모의 결과", () => simulationSummary)
            .Lines(3)
            .WithId("items.drop.summary");
    }

    private static DebugResult RollOnce()
    {
        PlayerActorRuntime actor = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null;
        if (actor == null)
            return DebugResult.Fail("플레이어가 없어요");
        // EnemyLootDropper.HandleDead와 같은 순서: 물약 → 장비.
        ItemData flask = FlaskLootPolicy.Roll(dropMonster, dropMapLevel, dropMapGrade);
        ItemData gear = GearLootPolicy.Roll(dropMonster, dropMapLevel, dropMapGrade);
        int placed = 0;
        var parts = new List<string>(2);
        foreach (ItemData item in new[] { flask, gear })
        {
            if (item == null)
                continue;
            item.level = dropMapLevel;
            if (WorldItemDropFactory.CreateWorldPickup(item, ScatterPosition(actor.transform, placed, 2),
                    actor.Inventory, actor.transform) != null)
            {
                placed++;
                parts.Add($"{ItemTooltipFormatter.GetGradeName(item.grade)} {DisplayName(item.baseData)}");
            }
        }
        return DebugResult.Ok(placed == 0 ? "드롭 없음" : string.Join(", ", parts));
    }

    private static DebugResult Simulate()
    {
        Random.State saved = Random.state;
        var watch = Stopwatch.StartNew();
        simulated = new int[GradeOrder.Length];
        simulatedGear = 0;
        simulatedFlask = 0;
        try
        {
            for (int i = 0; i < SimulationCount; i++)
            {
                Count(FlaskLootPolicy.Roll(dropMonster, dropMapLevel, dropMapGrade), ref simulatedFlask);
                Count(GearLootPolicy.Roll(dropMonster, dropMapLevel, dropMapGrade), ref simulatedGear);
            }
        }
        finally
        {
            Random.state = saved;
            watch.Stop();
        }

        var builder = new StringBuilder();
        builder.Append($"{SimulationCount}마리 · 물약 {simulatedFlask} · 장비 {simulatedGear} · {watch.Elapsed.TotalMilliseconds:0.0}ms");
        int total = simulatedFlask + simulatedGear;
        bool first = true;
        for (int i = 0; i < GradeOrder.Length; i++)
        {
            if (simulated[i] == 0)
                continue;
            builder.Append(first ? "\n" : "  ");
            first = false;
            builder.Append($"{ItemTooltipFormatter.GetGradeName(GradeOrder[i])} {simulated[i]}({simulated[i] * 100f / Mathf.Max(1, total):0.#}%)");
        }
        simulationSummary = builder.ToString();
        return DebugResult.Ok($"드롭 {total}개");
    }

    private static void Count(ItemData item, ref int counter)
    {
        if (item == null)
            return;
        counter++;
        int index = Array.IndexOf(GradeOrder, item.grade);
        if (index >= 0)
            simulated[index]++;
    }

    private static DebugBarSegment[] SimulationBar()
    {
        if (simulated == null)
            return Array.Empty<DebugBarSegment>();
        var segments = new List<DebugBarSegment>(GradeOrder.Length);
        for (int i = 0; i < GradeOrder.Length; i++)
        {
            if (simulated[i] > 0)
                segments.Add(new DebugBarSegment(ItemTooltipFormatter.GetGradeName(GradeOrder[i]), simulated[i], GradeColor(GradeOrder[i])));
        }
        return segments.ToArray();
    }

    private static Color GradeColor(ItemGrade value)
    {
        switch (value)
        {
            case ItemGrade.Uncommon: return new Color(0.45f, 0.78f, 0.42f);
            case ItemGrade.Rare: return new Color(0.35f, 0.6f, 0.95f);
            case ItemGrade.Epic: return new Color(0.68f, 0.45f, 0.92f);
            case ItemGrade.Legendary: return new Color(0.95f, 0.66f, 0.25f);
            case ItemGrade.Artifact: return new Color(0.88f, 0.4f, 0.35f);
            case ItemGrade.Mythic: return new Color(0.95f, 0.85f, 0.45f);
            case ItemGrade.Cursed: return new Color(0.55f, 0.2f, 0.3f);
            default: return new Color(0.7f, 0.72f, 0.76f);
        }
    }

    // ── 재화 ────────────────────────────────────────────────────

    private static void RegisterCurrency()
    {
        DebugSection s = DebugRegistry.Section(DebugTabs.Items, "재화", 30);
        s.Choice("종류", () => currency, value => currency = value, CurrencyLabel)
            .WithId("items.currency.type")
            .NoPreset();
        s.Readout("보유량", () =>
            {
                StashCurrencyService service = PlayerAccountInventoryService.SharedCurrencyService;
                return service != null ? service.GetAmount(currency).ToString("N0") : "-";
            })
            .WithId("items.currency.amount")
            .Pinnable();
        s.Buttons("더하기")
            .Add("+100", () => AddCurrency(100))
            .Add("+1,000", () => AddCurrency(1000))
            .Add("+10,000", () => AddCurrency(10000))
            .WithId("items.currency.add")
            .EnabledWhen(() => PlayerAccountInventoryService.SharedCurrencyService != null, "재화 서비스가 없어요")
            .ConfirmOnRealAccount();
        s.Button("1,000을 발밑 바닥에 떨어뜨리기", DropCurrency)
            .WithId("items.currency.drop")
            .EnabledWhen(() => DebugTeleport.HasPlayer, "플레이어가 있는 씬에서만");
    }

    private static DebugResult AddCurrency(int amount)
    {
        StashCurrencyService service = PlayerAccountInventoryService.SharedCurrencyService;
        if (service == null)
            return DebugResult.Fail("재화 서비스가 없어요");
        return service.TryAddCurrency(currency, amount)
            ? DebugResult.Ok($"{CurrencyLabel(currency)} +{amount:N0} → {service.GetAmount(currency):N0}")
            : DebugResult.Fail("더하지 못했어요(한도 등)");
    }

    private static DebugResult DropCurrency()
    {
        PlayerActorRuntime actor = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null;
        CurrencyItemData data = CurrencyData(currency);
        if (actor == null)
            return DebugResult.Fail("플레이어가 없어요");
        if (data == null)
            return DebugResult.Fail("이 재화의 아이템 데이터가 없어요");
        CurrencyWorldPickup pickup = WorldItemDropFactory.CreateCurrencyWorldPickup(data, 1000,
            ScatterPosition(actor.transform, 0, 1), actor.Inventory);
        return pickup != null ? DebugResult.Ok() : DebugResult.Fail("떨어뜨리지 못했어요");
    }

    private static CurrencyItemData CurrencyData(CurrencyType type)
    {
        AccountContentRegistry registry = Registry;
        if (registry == null)
            return null;
        foreach (AccountContentEntry entry in registry.Entries)
        {
            if (entry?.asset is CurrencyItemData data && data.currencyType == type)
                return data;
        }
        return null;
    }

    // ── 바닥 정리 ────────────────────────────────────────────────

    private static void RegisterFloor()
    {
        DebugSection s = DebugRegistry.Section(DebugTabs.Items, "바닥 정리", 40);
        s.Readout("바닥 아이템", () => $"{CountWorldPickups()}개")
            .WithId("items.floor.count");
        s.Buttons("바닥 아이템")
            .Add("모두 줍기", PickupAll)
            .Add("모두 지우기", ClearAll)
            .WithId("items.floor.actions")
            .EnabledWhen(() => DebugTeleport.HasPlayer, "플레이어가 있는 씬에서만")
            .Tip("재화 픽업도 함께 처리한다. 지우기는 되돌릴 수 없다.");
        s.Buttons("옛 테스트 픽업")
            .Add("테스트 무기", () => WithSpawner(spawner => spawner.SpawnTestWeaponPickup()))
            .Add("랜덤 무기", () => WithSpawner(spawner => spawner.SpawnRandomWeaponPickups()))
            .Add("이속 물약", () => WithSpawner(spawner => spawner.SpawnMoveSpeedPotionPickup()))
            .Add("회복 물약", () => WithSpawner(spawner => spawner.SpawnSmallHealPotionPickup()))
            .WithId("items.floor.legacySpawner")
            .Tip("ItemPickupSpawner 우클릭 메뉴와 같은 함수다.");
    }

    private static readonly List<WorldItemPickup> pickupScratch = new List<WorldItemPickup>();

    private static int CountWorldPickups()
    {
        WorldItemPickup.CopyActivePickups(pickupScratch);
        return pickupScratch.Count;
    }

    private static DebugResult PickupAll()
    {
        int items = 0;
        int failed = 0;
        WorldItemPickup.CopyActivePickups(pickupScratch);
        for (int i = 0; i < pickupScratch.Count; i++)
        {
            if (pickupScratch[i] == null)
                continue;
            if (pickupScratch[i].TryPickup())
                items++;
            else
                failed++;
        }
        int coins = 0;
        foreach (CurrencyWorldPickup coin in Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None))
        {
            if (coin != null && coin.isActiveAndEnabled && coin.TryPickup())
                coins++;
        }
        string message = $"아이템 {items}개 · 재화 {coins}개";
        return failed > 0 ? DebugResult.Ok(message + $" · {failed}개는 자리가 없어 남김") : DebugResult.Ok(message);
    }

    private static DebugResult ClearAll()
    {
        WorldItemPickup.CopyActivePickups(pickupScratch);
        int items = 0;
        for (int i = 0; i < pickupScratch.Count; i++)
        {
            if (pickupScratch[i] == null)
                continue;
            Object.Destroy(pickupScratch[i].gameObject);
            items++;
        }
        int coins = 0;
        foreach (CurrencyWorldPickup coin in Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None))
        {
            if (coin == null || !coin.isActiveAndEnabled)
                continue;
            if (!CurrencyPickupPool.TryReturn(coin))
                Object.Destroy(coin.gameObject);
            coins++;
        }
        return DebugResult.Ok($"아이템 {items}개 · 재화 {coins}개 지움");
    }

    private static DebugResult WithSpawner(Action<ItemPickupSpawner> action)
    {
        ItemPickupSpawner spawner = Object.FindFirstObjectByType<ItemPickupSpawner>();
        if (spawner == null)
            return DebugResult.Fail("이 씬에 ItemPickupSpawner가 없어요");
        action(spawner);
        return DebugResult.Ok();
    }

    // ── 상인 ────────────────────────────────────────────────────

    private static void RegisterMerchant()
    {
        DebugSection s = DebugRegistry.Section(DebugTabs.Items, "상인", 50);
        s.Buttons("상인")
            .Add("평판 경험치 +20", () =>
            {
                MerchantReputationService.AddReputationExperienceToAll(20);
                return DebugResult.Ok("모든 상인");
            })
            .Add("재고 새로고침", () => MerchantStockRefreshService.ForceRefreshAllMerchants()
                ? DebugResult.Ok("모든 상인")
                : DebugResult.Fail("새로고침할 상인이 없어요"))
            .WithId("items.merchant.actions")
            .ConfirmOnRealAccount()
            .Tip("옛 HUD 버튼(평판·재고)과 같은 함수다.");
    }

    // ── 표시 ────────────────────────────────────────────────────

    private static Category CategoryOf(BaseItemData data)
    {
        if (data is WeaponItemData) return Category.Weapon;
        if (data is GearItemData) return Category.Gear;
        if (data is FlaskItemData) return Category.Flask;
        if (data is MapItemData) return Category.Map;
        if (data is BagItemData) return Category.Bag;
        return Category.Consumable;
    }

    private static string CategoryLabel(Category value)
    {
        switch (value)
        {
            case Category.Weapon: return "무기";
            case Category.Gear: return "장비";
            case Category.Flask: return "물약";
            case Category.Consumable: return "소모품";
            case Category.Map: return "지도";
            case Category.Bag: return "가방";
            default: return "전체";
        }
    }

    private static string DestinationLabel(Destination value)
    {
        switch (value)
        {
            case Destination.Inventory: return "인벤토리";
            case Destination.Stash: return "창고";
            case Destination.Equip: return "장착";
            default: return "바닥";
        }
    }

    private static string MonsterLabel(EnemyGradeType value)
    {
        switch (value)
        {
            case EnemyGradeType.Elite: return "정예";
            case EnemyGradeType.GreaterElite: return "상위 정예";
            case EnemyGradeType.Boss: return "보스";
            default: return "일반";
        }
    }

    private static string CurrencyLabel(CurrencyType value) => value == CurrencyType.MapFragment ? "지도 조각" : "골드";

    private static string DisplayName(BaseItemData data)
        => data == null ? "-" : !string.IsNullOrWhiteSpace(data.itemName) ? data.itemName : data.name;

    private static string ItemLabel(BaseItemData data) => $"{DisplayName(data)}  ·  {CategoryLabel(CategoryOf(data))}";

    private static string ItemLabelShort(BaseItemData data)
    {
        string name = DisplayName(data);
        return name.Length > 14 ? name.Substring(0, 13) + "…" : name;
    }

    private static bool Contains(IReadOnlyList<BaseItemData> list, BaseItemData value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
                return true;
        }
        return false;
    }
}
#endif
