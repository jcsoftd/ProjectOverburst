using UnityEngine;

[CreateAssetMenu(
    fileName = "MeleeElementHitVfxCatalog",
    menuName = "OVERBURST/Weapons/Melee Element Hit VFX Catalog")]
public sealed class MeleeElementHitVfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/VFX/MeleeElementHitVfxCatalog";

    [InspectorName("공용 원소 적중 프리팹")]
    public GameObject sharedHitPrefab;
    [Header("원본 최대 재생시간(초)")]
    [Min(0f)] public float fireLifetime = 5.15f;
    [Min(0f)] public float waterLifetime = 10f;
    [Min(0f)] public float iceLifetime = 10f;
    [Min(0f)] public float electricLifetime = 7.5f;
    [Min(0f)] public float earthLifetime = 5f;
    [InspectorName("풀 최대 보관 수")]
    [Min(1)] public int poolCapacity = 32;

    public bool TryResolve(WeaponElement element, out GameObject prefab)
    {
        prefab = Supports(element) ? sharedHitPrefab : null;
        return prefab != null;
    }

    public float ResolveLifetime(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire:
                return Mathf.Max(0f, fireLifetime);
            case WeaponElement.Water:
                return Mathf.Max(0f, waterLifetime);
            case WeaponElement.Ice:
                return Mathf.Max(0f, iceLifetime);
            case WeaponElement.Electric:
                return Mathf.Max(0f, electricLifetime);
            case WeaponElement.Earth:
                return Mathf.Max(0f, earthLifetime);
            default:
                return 0f;
        }
    }

    public static bool Supports(WeaponElement element)
    {
        return element == WeaponElement.Fire
            || element == WeaponElement.Water
            || element == WeaponElement.Ice
            || element == WeaponElement.Electric
            || element == WeaponElement.Earth;
    }
}
