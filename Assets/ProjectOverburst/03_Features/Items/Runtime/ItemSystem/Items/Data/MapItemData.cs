using UnityEngine;

[CreateAssetMenu(fileName = "Map", menuName = "OVERBURST/Items/Map")]
public sealed class MapItemData : BaseItemData
{
    [Tooltip("Stable dungeon theme key. Scene names are resolved by the world adapter.")]
    public string dungeonThemeId;
}
