using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Weapons/Level Catalog")]
public sealed class WeaponLevelCatalog : ScriptableObject
{
    [Serializable] public struct Entry
    {
        public WeaponItemData weapon;
        [Range(1, 100)] public int minimumLevel;
        [Range(1, 100)] public int maximumLevel;
    }
    public const string ResourcePath = "Items/Weapons/WeaponLevelCatalog";
    public Entry[] entries = Array.Empty<Entry>();
    public GameObject unfinishedGreatswordVisual;
    static WeaponLevelCatalog current;
    public static WeaponLevelCatalog Current => current != null ? current : current = Resources.Load<WeaponLevelCatalog>(ResourcePath);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => current = null;
    public List<WeaponItemData> Candidates(int level)
    {
        var result = new List<WeaponItemData>();
        foreach (var entry in entries)
            if (entry.weapon != null && level >= entry.minimumLevel && level <= entry.maximumLevel
                && WeaponContentPolicy.IsActiveWeapon(entry.weapon)) result.Add(entry.weapon);
        return result;
    }
    public static GameObject ResolveVisual(WeaponItemData weapon)
    {
        if (weapon == null) return null;
        if (weapon.weaponRootPrefab != null) return weapon.weaponRootPrefab;
        var catalog = Current;
        if (catalog != null && weapon.weaponClass == WeaponClass.Greatsword)
            foreach (var entry in catalog.entries)
                if (entry.weapon == weapon) return catalog.unfinishedGreatswordVisual;
        return null;
    }
}
