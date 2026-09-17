using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class EarthZoneVfxCatalogEntry
{
    [SerializeField] private EarthZoneVfxKind kind;
    [SerializeField] private GameObject prefab;
    public EarthZoneVfxKind Kind => kind;
    public GameObject Prefab => prefab;

    public EarthZoneVfxCatalogEntry(EarthZoneVfxKind value, GameObject valuePrefab)
    {
        kind = value;
        prefab = valuePrefab;
    }
}

[CreateAssetMenu(fileName = "EarthZoneVfxCatalog", menuName = "OVERBURST/Combat/Earth Zone VFX Catalog")]
public sealed class EarthZoneVfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/VFX/EarthZoneVfxCatalog";
    [SerializeField] private List<EarthZoneVfxCatalogEntry> entries = new List<EarthZoneVfxCatalogEntry>();
    public IReadOnlyList<EarthZoneVfxCatalogEntry> Entries => entries;

    public void ReplaceEntries(List<EarthZoneVfxCatalogEntry> replacement)
    {
        entries = replacement ?? new List<EarthZoneVfxCatalogEntry>();
    }
}
