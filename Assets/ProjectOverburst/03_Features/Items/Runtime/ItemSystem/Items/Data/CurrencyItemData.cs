using UnityEngine;

[CreateAssetMenu(fileName = "NewCurrency", menuName = "Items/Currency")]
public class CurrencyItemData : BaseItemData
{
    [Header("Currency")]
    public CurrencyType currencyType;
    public int maxStack = 1000;

    [Header("Stack Icon")]
    public Sprite smallStackIcon;
    public Sprite mediumStackIcon;
    public Sprite largeStackIcon;
    public int smallStackIconMax = 500;
    public int mediumStackIconMax = 999;

    public Sprite GetDisplayIcon(int stackCount)
    {
        if (currencyType != CurrencyType.Gold)
            return icon;

        int count = Mathf.Max(1, stackCount);
        if (count <= Mathf.Max(1, smallStackIconMax))
            return smallStackIcon != null ? smallStackIcon : icon;

        if (count <= Mathf.Max(smallStackIconMax + 1, mediumStackIconMax))
            return mediumStackIcon != null ? mediumStackIcon : icon;

        return largeStackIcon != null ? largeStackIcon : icon;
    }
}
