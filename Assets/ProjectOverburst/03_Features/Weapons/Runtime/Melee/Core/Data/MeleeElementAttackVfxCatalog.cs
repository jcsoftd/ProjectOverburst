using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "MeleeElementAttackVfxCatalog",
    menuName = "OVERBURST/Weapons/Melee Element Attack VFX Catalog")]
public sealed class MeleeElementAttackVfxCatalog : ScriptableObject
{
    public const string BasicSlashKey = "BasicSlash";
    public const string CircularSlashKey = "CircularSlash";
    public const string GroundSlamSlashKey = "GroundSlamSlash";

    [InspectorName("공용 원소 슬래시 프리팹")]
    public GameObject sharedSlashPrefab;

    public bool TryResolveSharedSlash(string overrideKey, out GameObject prefab)
    {
        prefab = IsSharedSlashKey(overrideKey) ? sharedSlashPrefab : null;
        return prefab != null;
    }

    public static bool IsSharedSlashKey(string overrideKey)
    {
        return string.Equals(overrideKey, BasicSlashKey, StringComparison.Ordinal)
            || string.Equals(overrideKey, CircularSlashKey, StringComparison.Ordinal)
            || string.Equals(overrideKey, GroundSlamSlashKey, StringComparison.Ordinal);
    }

    public static bool IsCircularSlashKey(string overrideKey)
    {
        return string.Equals(overrideKey, CircularSlashKey, StringComparison.Ordinal);
    }
}
