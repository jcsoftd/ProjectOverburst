using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Audio/BGM Catalog")]
public sealed class OverburstBgmCatalog : ScriptableObject
{
    public const string ResourcePath = "Audio/OverburstBgmCatalog";
    public AudioClip title, tutorial, town;
    public AudioClip[] outdoor, desert, cave;
    [Range(0, 1)] public float volume = .35f;

    public AudioClip[] Playlist(OverburstBgmContext context)
    {
        switch (context)
        {
            case OverburstBgmContext.Outdoor: return outdoor;
            case OverburstBgmContext.Desert: return desert;
            case OverburstBgmContext.Cave: return cave;
            default: return null;
        }
    }

    public AudioClip Single(OverburstBgmContext context)
    {
        switch (context)
        {
            case OverburstBgmContext.Title: return title;
            case OverburstBgmContext.Tutorial: return tutorial;
            case OverburstBgmContext.Town: return town;
            default: return null;
        }
    }
}

public enum OverburstBgmContext { None, Title, Tutorial, Town, Outdoor, Desert, Cave }
