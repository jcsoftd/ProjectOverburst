using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MerchantStockItemEntry
{
    [SerializeField] private BaseItemData itemData;
    [SerializeField] private int stackCount = 1;
    [SerializeField] private ItemGrade grade = ItemGrade.Common;

    public BaseItemData ItemData { get { return itemData; } }
    public int StackCount { get { return Mathf.Max(1, stackCount); } }
    public ItemGrade Grade { get { return grade; } }
    public bool HasItem { get { return itemData != null; } }
}

[CreateAssetMenu(fileName = "MerchantDefinition", menuName = "OVERBURST/Merchant Definition")]
public class MerchantDefinition : ScriptableObject
{
    [SerializeField] private string merchantName = "상인";
    [SerializeField] private ShopCategory category = ShopCategory.GeneralGoods;
    [TextArea]
    [SerializeField] private string description;
    [SerializeField] private int merchantGold = 1000;
    [SerializeField] private int inventoryCapacity = 20;
    [SerializeField] private List<ShopTab> customTabs = new List<ShopTab>();
    [SerializeField] private List<MerchantStockItemEntry> stockItems = new List<MerchantStockItemEntry>();

    public string MerchantName { get { return string.IsNullOrWhiteSpace(merchantName) ? name : merchantName; } }
    public ShopCategory Category { get { return category; } }
    public string Description { get { return description; } }
    public int MerchantGold { get { return Mathf.Max(0, merchantGold); } }
    public int InventoryCapacity { get { return Mathf.Max(1, inventoryCapacity); } }
    public IReadOnlyList<ShopTab> CustomTabs { get { return customTabs; } }
    public IReadOnlyList<MerchantStockItemEntry> StockItems { get { return stockItems; } }

    public void GetSupportedTabs(List<ShopTab> results)
    {
        if (results == null)
            return;

        results.Clear();
        if (customTabs != null && customTabs.Count > 0)
        {
            for (int i = 0; i < customTabs.Count; i++)
                AddTab(results, customTabs[i]);

            if (!results.Contains(ShopTab.Trade))
                results.Insert(0, ShopTab.Trade);

            return;
        }

        AddDefaultTabs(results);
    }

    private void AddDefaultTabs(List<ShopTab> results)
    {
        AddTab(results, ShopTab.Trade);
        AddTab(results, ShopTab.Quest);

        switch (category)
        {
            case ShopCategory.ComboGem:
                AddTab(results, ShopTab.GemDismantle);
                AddTab(results, ShopTab.GemCombine);
                break;
            case ShopCategory.Weapon:
                AddTab(results, ShopTab.WeaponCombine);
                AddTab(results, ShopTab.WeaponEnhance);
                break;
        }
    }

    private static void AddTab(List<ShopTab> results, ShopTab tab)
    {
        if (!results.Contains(tab))
            results.Add(tab);
    }
}
