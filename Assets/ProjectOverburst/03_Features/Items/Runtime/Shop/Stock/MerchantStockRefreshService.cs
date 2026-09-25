using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class MerchantStockRefreshService : MonoBehaviour
{
    private const string SmallHealPotionPath = "Assets/ProjectOverburst/03_Features/Items/Data/Items/Consumables/SmallHealPotion.asset";
    private const string MediumHealPotionPath = "Assets/ProjectOverburst/03_Features/Items/Data/Items/Consumables/MediumHealPotion.asset";
    private const string LargeHealPotionPath = "Assets/ProjectOverburst/03_Features/Items/Data/Items/Consumables/LargeHealPotion.asset";
    private const string ExtraLargeHealPotionPath = "Assets/ProjectOverburst/03_Features/Items/Data/Items/Consumables/ExtraLargeHealPotion.asset";
    private const string PermanentHealPotionPath = "Assets/ProjectOverburst/03_Features/Items/Data/Items/Consumables/PermanentHealPotion.asset";
    private const string MoveSpeedPotionPath = "Assets/ProjectOverburst/03_Features/Items/Data/Items/Consumables/MoveSpeedPotion.asset";

    private static MerchantStockRefreshService instance;
    private static readonly Dictionary<MerchantDefinition, MerchantInventory> inventories = new Dictionary<MerchantDefinition, MerchantInventory>();
    internal static IReadOnlyDictionary<MerchantDefinition, MerchantInventory> AccountInventories => inventories;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAccountStocks()
    {
        instance = null;
        inventories.Clear();
        StocksRefreshed = null;
    }

    internal static void ApplyAccountInventories(Dictionary<MerchantDefinition, MerchantInventory> restored)
    {
        foreach (var key in new List<MerchantDefinition>(inventories.Keys))
            if (!restored.ContainsKey(key)) inventories.Remove(key);
        foreach (var pair in restored)
        {
            if (inventories.TryGetValue(pair.Key, out var existing))
                existing.ApplyAccountItems(pair.Value.Capacity, pair.Value.Items, pair.Value.CurrencyItems);
            else inventories.Add(pair.Key, pair.Value);
        }
    }

    internal static void NotifyAccountApplied() => StocksRefreshed?.Invoke();
    private readonly MerchantStockGenerator[] stockGenerators =
    {
        new GeneralGoodsMerchantStockGenerator(),
        new WeaponMerchantStockGenerator()
    };

    public static event System.Action StocksRefreshed;

    [Header("Merchants")]
    [SerializeField] private MerchantDefinition[] merchantDefinitions;

    [Header("General Goods")]
    [SerializeField] private BaseItemData smallHealPotion;
    [SerializeField] private BaseItemData mediumHealPotion;
    [SerializeField] private BaseItemData largeHealPotion;
    [SerializeField] private BaseItemData extraLargeHealPotion;
    [SerializeField] private BaseItemData permanentHealPotion;
    [SerializeField] private BaseItemData moveSpeedPotion;
    [SerializeField] private int generalGoodsMinStackTotal = 15;
    [SerializeField] private int generalGoodsMaxStackTotal = 29;


    [Header("Weapons")]
    [SerializeField] private BaseItemData[] weaponPool;
    [SerializeField] private int weaponMinStockCount = 10;
    [SerializeField] private int weaponMaxStockCount = 15;

    [Header("Refresh")]
    [Range(0f, 1f)]
    [SerializeField] private float successfulRunRefreshChance = 0.5f;
    [SerializeField] private int merchantGoldMin = 1000;
    [SerializeField] private int merchantGoldMax = 2000;

    private void Awake()
    {
        instance = this;
        MerchantReputationService.RegisterMerchants(merchantDefinitions);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static MerchantInventory GetOrCreateInventory(MerchantDefinition definition)
    {
        if (definition == null)
            return null;

        MerchantInventory inventory;
        if (inventories.TryGetValue(definition, out inventory) && inventory != null)
            return inventory;

        inventory = instance != null
            ? instance.CreateInventory(definition)
            : CreateFallbackInventory(definition);
        inventories[definition] = inventory;
        return inventory;
    }

    public static void HandleSuccessfulRunReturn()
    {
        if (instance == null)
            return;

        instance.RefreshMerchantsAfterSuccessfulRun();
    }

    public static bool ForceRefreshAllMerchants()
    {
        if (instance == null)
            return false;

        return instance.RefreshAllMerchants();
    }

    public void RefreshMerchantsAfterSuccessfulRun()
    {
        if (merchantDefinitions == null)
            return;

        bool refreshedAny = false;
        for (int i = 0; i < merchantDefinitions.Length; i++)
        {
            MerchantDefinition definition = merchantDefinitions[i];
            if (definition == null)
                continue;

            if (Random.value > Mathf.Clamp01(successfulRunRefreshChance))
                continue;

            inventories[definition] = CreateInventory(definition);
            Debug.Log("[MerchantStock] Refreshed stock: " + definition.MerchantName, this);
            refreshedAny = true;
        }

        if (refreshedAny)
            StocksRefreshed?.Invoke();
    }

    private bool RefreshAllMerchants()
    {
        if (merchantDefinitions == null)
            return false;

        bool refreshedAny = false;
        for (int i = 0; i < merchantDefinitions.Length; i++)
        {
            MerchantDefinition definition = merchantDefinitions[i];
            if (definition == null)
                continue;

            inventories[definition] = CreateInventory(definition);
            Debug.Log("[MerchantStock] Force refreshed stock: " + definition.MerchantName, this);
            refreshedAny = true;
        }

        if (refreshedAny)
            StocksRefreshed?.Invoke();

        return refreshedAny;
    }

    public MerchantInventory CreateInventory(MerchantDefinition definition)
    {
        if (definition == null)
            return null;

        MerchantStockGenerationContext context = BuildGenerationContext();
        for (int i = 0; i < stockGenerators.Length; i++)
        {
            MerchantStockGenerator generator = stockGenerators[i];
            if (generator != null && generator.CanGenerate(definition))
                return generator.Generate(definition, context);
        }

        return CreateFallbackInventory(definition);
    }

    private MerchantStockGenerationContext BuildGenerationContext()
    {
        return new MerchantStockGenerationContext(
            ResolveGeneralGoodsItem(smallHealPotion, SmallHealPotionPath),
            ResolveGeneralGoodsItem(mediumHealPotion, MediumHealPotionPath),
            ResolveGeneralGoodsItem(largeHealPotion, LargeHealPotionPath),
            ResolveGeneralGoodsItem(extraLargeHealPotion, ExtraLargeHealPotionPath),
            ResolveGeneralGoodsItem(permanentHealPotion, PermanentHealPotionPath),
            ResolveGeneralGoodsItem(moveSpeedPotion, MoveSpeedPotionPath),
            generalGoodsMinStackTotal,
            generalGoodsMaxStackTotal,
            weaponPool,
            weaponMinStockCount,
            weaponMaxStockCount,
            merchantGoldMin,
            merchantGoldMax);
    }

    private static BaseItemData ResolveGeneralGoodsItem(BaseItemData itemData, string assetPath)
    {
        if (itemData != null)
            return itemData;

#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<BaseItemData>(assetPath);
#else
        return null;
#endif
    }

    private static MerchantInventory CreateFallbackInventory(MerchantDefinition definition)
    {
        MerchantInventory inventory = new MerchantInventory();
        inventory.Initialize(definition);
        return inventory;
    }
}
