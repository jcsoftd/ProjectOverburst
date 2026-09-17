using System;

[Serializable]
public struct CurrencyAmount
{
    public CurrencyType type;
    public int amount;

    public CurrencyAmount(CurrencyType type, int amount)
    {
        this.type = type;
        this.amount = amount;
    }

    public bool IsValid => amount > 0;
}
