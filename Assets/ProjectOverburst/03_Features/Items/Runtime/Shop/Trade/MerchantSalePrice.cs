using System;

public static class MerchantSalePrice
{
    public const int PolicyVersion = 1;
    public static int Unit(int referenceValue, int discountBasisPoints)
    {
        if (referenceValue < 0 || discountBasisPoints < 0 || discountBasisPoints > 1000)
            throw new ArgumentOutOfRangeException();
        long numerator = checked((long)referenceValue * 2 * (10000 - discountBasisPoints));
        return checked((int)((numerator + 9999) / 10000));
    }
}
