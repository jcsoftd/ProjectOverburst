using UnityEngine;

public enum LoadingScreenArtworkContext
{
    Random,
    ReturnToHideout
}

/// <summary>Authored loading art; scene flow chooses the transition context.</summary>
[CreateAssetMenu(menuName = "OVERBURST/UI/Loading Artwork Catalog")]
public sealed class LoadingScreenArtworkCatalog : ScriptableObject
{
    public const string ResourcePath = "UI/Loading/LoadingScreenArtworkCatalog";

    [SerializeField] private Texture2D[] randomArtworks;
    [SerializeField] private Texture2D hideoutReturnArtwork;
    [SerializeField] private Material artworkMaterial;

    public Material ArtworkMaterial => artworkMaterial;
    public int RandomArtworkCount => randomArtworks == null ? 0 : randomArtworks.Length;

    public Texture2D Select(LoadingScreenArtworkContext context, int randomIndex)
    {
        if (context == LoadingScreenArtworkContext.ReturnToHideout && hideoutReturnArtwork != null)
            return hideoutReturnArtwork;

        if (RandomArtworkCount == 0)
            return hideoutReturnArtwork;

        return randomArtworks[Mathf.Clamp(randomIndex, 0, RandomArtworkCount - 1)];
    }
}
