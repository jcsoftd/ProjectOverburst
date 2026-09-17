using UnityEngine;

public static class WorldItemDropFactory // 월드 아이템 생성
{
    private const string GoldCurrencyPickupPrefabPath = "Pickups/PF_CurrencyWorldPickup_Gold";

    public static ItemData CreateRuntimeItem(BaseItemData itemData, int minLevel, int maxLevel, ItemGrade minGrade, ItemGrade maxGrade, bool useVtpGradeRoll, int stackCount)
    {
        if (itemData == null)
            return null;

        if (!WeaponContentPolicy.IsAllowedItemData(itemData))
            return null;

        int level = 1; // 현재는 레벨 제거 기준
        if (!TryResolveRuntimeGrade(minGrade, maxGrade, useVtpGradeRoll, out ItemGrade grade))
            return null; // 비활성 등급 범위는 신규 드랍 제외

        return new ItemData(itemData, level, grade, Mathf.Max(1, stackCount));
    }

    public static WorldItemPickup CreateWorldPickup(ItemData item, Vector3 position, PlayerInventory inventory, Transform player)
    {
        return CreateWorldPickupFromExistingItem(item, position, inventory, player, null);
    }

    public static WorldItemPickup CreateWorldPickup(ItemData item, Vector3 position, PlayerInventory inventory, Transform player, PickupGradeVfxSet pickupGradeVfxSet)
    {
        return CreateWorldPickupFromExistingItem(item, position, inventory, player, pickupGradeVfxSet);
    }

    public static WorldItemPickup CreateWorldPickupFromExistingItem(ItemData item, Vector3 position, PlayerInventory inventory, Transform player)
    {
        return CreateWorldPickupFromExistingItem(item, position, inventory, player, null);
    }

    public static WorldItemPickup CreateWorldPickupFromExistingItem(ItemData item, Vector3 position, PlayerInventory inventory, Transform player, PickupGradeVfxSet pickupGradeVfxSet)
    {
        if (item == null || !item.HasValidBaseData)
            return null;

        if (!WeaponContentPolicy.IsAllowedRuntimeItem(item))
            return null;

        GameObject pickupObject = InstantiateWorldPickupObject(item);
        pickupObject.name = BuildPickupName(item);
        pickupObject.transform.position = position;

        WorldItemPickup pickup = EnsureWorldItemPickupComponent(pickupObject);
        pickup.Initialize(item, inventory, player, pickupGradeVfxSet);
        RunWalkableContext.TryAttachFallGuard(pickupObject);
        return pickup;
    }

    public static CurrencyWorldPickup CreateCurrencyWorldPickup(CurrencyItemData currencyData, int amount, Vector3 position, PlayerInventory inventory)
    {
        if (currencyData == null || amount <= 0)
            return null;

        GameObject pickupObject = InstantiateCurrencyPickupObject(currencyData, position);
        pickupObject.name = string.Format("CurrencyPickup_{0}_{1}", currencyData.itemName, amount);

        CurrencyWorldPickup pickup = EnsureCurrencyPickupComponent(pickupObject);
        pickup.Initialize(currencyData, amount, inventory);
        BeginScriptedDrop(pickupObject);
        RunWalkableContext.TryAttachFallGuard(pickupObject);
        return pickup;
    }

    public static CurrencyWorldPickup CreateCurrencyWorldPickupFromExistingItem(ItemData item, Vector3 position, PlayerInventory inventory)
    {
        if (item == null || item.baseData is not CurrencyItemData currencyData)
            return null;

        GameObject pickupObject = InstantiateCurrencyPickupObject(currencyData, position);
        pickupObject.name = string.Format("CurrencyPickup_{0}_{1}", item.itemName, Mathf.Max(1, item.stackCount));

        CurrencyWorldPickup pickup = EnsureCurrencyPickupComponent(pickupObject);
        pickup.Initialize(item, inventory);
        BeginScriptedDrop(pickupObject);
        RunWalkableContext.TryAttachFallGuard(pickupObject);
        return pickup;
    }

    private static GameObject InstantiateWorldPickupObject(ItemData item)
    {
        GameObject prefab = item != null && item.baseData != null ? item.baseData.worldPickupPrefab : null;
        if (prefab != null)
            return Object.Instantiate(prefab);

        return CreateWorldPickupFallback(item);
    }

    private static WorldItemPickup EnsureWorldItemPickupComponent(GameObject pickupObject)
    {
        WorldItemPickup pickup = pickupObject.GetComponent<WorldItemPickup>();
        if (pickup == null)
            pickup = pickupObject.AddComponent<WorldItemPickup>();

        return pickup;
    }

    private static GameObject CreateWorldPickupFallback(ItemData item)
    {
        PrimitiveType primitiveType = item.baseData is WeaponItemData ? PrimitiveType.Cube : PrimitiveType.Sphere;
        Vector3 scale = item.baseData is WeaponItemData ? new Vector3(0.55f, 0.14f, 0.22f) : new Vector3(0.24f, 0.24f, 0.24f);

        GameObject pickupObject = GameObject.CreatePrimitive(primitiveType);
        pickupObject.transform.localScale = scale;

        Renderer renderer = pickupObject.GetComponent<Renderer>();
        if (renderer != null && item != null)
            renderer.material.color = item.color;

        return pickupObject;
    }

    private static GameObject InstantiateCurrencyPickupObject(CurrencyItemData currencyData, Vector3 position)
    {
        GameObject prefab = ResolveCurrencyPickupPrefab(currencyData);
        GameObject pickupObject = prefab != null
            ? Object.Instantiate(prefab)
            : CreateCurrencyPickupFallback(currencyData);

        pickupObject.transform.position = position + Vector3.up * 0.05f;
        return pickupObject;
    }

    private static GameObject ResolveCurrencyPickupPrefab(CurrencyItemData currencyData)
    {
        if (currencyData != null && currencyData.currencyType == CurrencyType.Gold)
            return Resources.Load<GameObject>(GoldCurrencyPickupPrefabPath);

        return null;
    }

    private static CurrencyWorldPickup EnsureCurrencyPickupComponent(GameObject pickupObject)
    {
        CurrencyWorldPickup pickup = pickupObject.GetComponent<CurrencyWorldPickup>();
        if (pickup == null)
            pickup = pickupObject.AddComponent<CurrencyWorldPickup>();

        return pickup;
    }

    private static GameObject CreateCurrencyPickupFallback(CurrencyItemData currencyData)
    {
        GameObject pickupObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pickupObject.transform.localScale = new Vector3(0.34f, 0.035f, 0.34f);

        Renderer renderer = pickupObject.GetComponent<Renderer>();
        if (renderer != null && currencyData != null)
            renderer.material.color = currencyData.color;

        Collider collider = pickupObject.GetComponent<Collider>();
        if (collider != null)
            collider.isTrigger = true;

        return pickupObject;
    }

    private static void BeginScriptedDrop(GameObject pickupObject)
    {
        if (pickupObject == null)
            return;

        WorldItemDropMotion dropMotion = pickupObject.GetComponent<WorldItemDropMotion>();
        if (dropMotion == null)
            dropMotion = pickupObject.AddComponent<WorldItemDropMotion>();

        if (pickupObject.GetComponent<WorldPickupPresentation>() == null)
            pickupObject.AddComponent<WorldPickupPresentation>();

        dropMotion.Begin();
    }

    private static string BuildPickupName(ItemData item)
    {
        string itemName = !string.IsNullOrEmpty(item.itemName) ? item.itemName : "Item";

        if (item.baseData is WeaponItemData)
            return string.Format("WorldPickup_{0}_{1}", itemName, item.grade);

        return string.Format("WorldPickup_{0}_Lv{1}_{2}", itemName, item.level, item.grade);
    }

    private static bool TryResolveRuntimeGrade(
        ItemGrade minGrade,
        ItemGrade maxGrade,
        bool useVtpGradeRoll,
        out ItemGrade grade)
    {
        grade = default;
        ItemGrade candidate = useVtpGradeRoll
            ? ItemGradeAvailabilityPolicy.RollWeightedGrade()
            : default;
        int min = Mathf.Min((int)minGrade, (int)maxGrade);
        int max = Mathf.Max((int)minGrade, (int)maxGrade);
        if (useVtpGradeRoll
            && ItemGradeAvailabilityPolicy.IsEnabled(candidate)
            && (int)candidate >= min
            && (int)candidate <= max)
        {
            grade = candidate;
            return true;
        }

        return ItemGradeAvailabilityPolicy.TryRollInRange(minGrade, maxGrade, out grade);
    }
}
