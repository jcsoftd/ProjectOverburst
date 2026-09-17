using System.Collections.Generic;
using UnityEngine;

public static class CurrencyItemRegistry
{
    private const string ResourcePath = "Data/Items/Currency";
    private static readonly Dictionary<CurrencyType, CurrencyItemData> cache = new Dictionary<CurrencyType, CurrencyItemData>();
    private static bool loaded;

    public static CurrencyItemData Get(CurrencyType type)
    {
        EnsureLoaded();
        CurrencyItemData data;
        return cache.TryGetValue(type, out data) ? data : null;
    }

    public static void Reset()
    {
        cache.Clear();
        loaded = false;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;
        cache.Clear();

        CurrencyItemData[] assets = Resources.LoadAll<CurrencyItemData>(ResourcePath);
        if (assets == null)
            return;

        for (int i = 0; i < assets.Length; i++)
        {
            CurrencyItemData asset = assets[i];
            if (asset == null)
                continue;

            cache[asset.currencyType] = asset;
        }
    }
}
