using UnityEngine;

[CreateAssetMenu(fileName = "Map", menuName = "OVERBURST/Items/Map")]
public sealed class MapItemData : BaseItemData
{
    [Tooltip("Stable dungeon theme key. Scene names are resolved by the world adapter.")]
    public string dungeonThemeId;

    [Tooltip("Level bands 1-10, 11-20, ... 91-100. Grade visuals are handled by item UI.")]
    public Sprite[] levelBandIcons = new Sprite[10];

    public Sprite ResolveIconForLevel(int level)
    {
        int index = (Mathf.Clamp(level, 1, 100) - 1) / 10;
        return levelBandIcons != null && index < levelBandIcons.Length && levelBandIcons[index] != null
            ? levelBandIcons[index]
            : icon;
    }
}
