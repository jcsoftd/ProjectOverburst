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

    public int Level { get { return Mathf.Max(0, level); } }
    public float BuyDiscountRate { get { return Mathf.Clamp01(buyDiscountRate); } }
    public int MerchantGoldMin { get { return Mathf.Max(0, merchantGoldMin); } }
    public int MerchantGoldMax { get { return Mathf.Max(MerchantGoldMin, merchantGoldMax); } }
    public int GeneralGoodsMinTotal { get { return Mathf.Max(1, generalGoodsMinTotal); } }
    public int GeneralGoodsMaxTotal { get { return Mathf.Max(GeneralGoodsMinTotal, generalGoodsMaxTotal); } }

    public MerchantReputationLevelSetting(
        int level,
        float buyDiscountRate,
        int merchantGoldMin,
        int merchantGoldMax,
        int generalGoodsMinTotal,
        int generalGoodsMaxTotal)
    {
        this.level = level;
        this.buyDiscountRate = buyDiscountRate;
        this.merchantGoldMin = merchantGoldMin;
        this.merchantGoldMax = merchantGoldMax;
        this.generalGoodsMinTotal = generalGoodsMinTotal;
        this.generalGoodsMaxTotal = generalGoodsMaxTotal;
    }
}
