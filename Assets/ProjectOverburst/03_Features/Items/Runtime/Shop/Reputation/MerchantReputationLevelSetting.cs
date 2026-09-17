using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public sealed class MerchantReputationLevelSetting
{
    [SerializeField] private int level;
    [SerializeField] private float buyDiscountRate;
    [SerializeField] private int merchantGoldMin;
    [SerializeField] private int merchantGoldMax;
    [SerializeField] private int generalGoodsMinTotal;
    [SerializeField] private int generalGoodsMaxTotal;
    [FormerlySerializedAs("modPartMinStockCount")]
    [FormerlySerializedAs("enchantGemMinStockCount")]
    [SerializeField] private int comboGemMinStockCount;
    [FormerlySerializedAs("modPartMaxStockCount")]
    [FormerlySerializedAs("enchantGemMaxStockCount")]
    [SerializeField] private int comboGemMaxStockCount;
    [FormerlySerializedAs("modPartUncommonChance")]
    [FormerlySerializedAs("enchantGemUncommonChance")]
    [SerializeField] private float comboGemUncommonChance;

    public int Level { get { return Mathf.Max(0, level); } }
    public float BuyDiscountRate { get { return Mathf.Clamp01(buyDiscountRate); } }
    public int MerchantGoldMin { get { return Mathf.Max(0, merchantGoldMin); } }
    public int MerchantGoldMax { get { return Mathf.Max(MerchantGoldMin, merchantGoldMax); } }
    public int GeneralGoodsMinTotal { get { return Mathf.Max(1, generalGoodsMinTotal); } }
    public int GeneralGoodsMaxTotal { get { return Mathf.Max(GeneralGoodsMinTotal, generalGoodsMaxTotal); } }
    public int ComboGemMinStockCount { get { return Mathf.Max(0, comboGemMinStockCount); } }
    public int ComboGemMaxStockCount { get { return Mathf.Max(ComboGemMinStockCount, comboGemMaxStockCount); } }
    public float ComboGemUncommonChance { get { return Mathf.Clamp01(comboGemUncommonChance); } }

    public MerchantReputationLevelSetting(
        int level,
        float buyDiscountRate,
        int merchantGoldMin,
        int merchantGoldMax,
        int generalGoodsMinTotal,
        int generalGoodsMaxTotal,
        int comboGemMinStockCount,
        int comboGemMaxStockCount,
        float comboGemUncommonChance)
    {
        this.level = level;
        this.buyDiscountRate = buyDiscountRate;
        this.merchantGoldMin = merchantGoldMin;
        this.merchantGoldMax = merchantGoldMax;
        this.generalGoodsMinTotal = generalGoodsMinTotal;
        this.generalGoodsMaxTotal = generalGoodsMaxTotal;
        this.comboGemMinStockCount = comboGemMinStockCount;
        this.comboGemMaxStockCount = comboGemMaxStockCount;
        this.comboGemUncommonChance = comboGemUncommonChance;
    }
}
