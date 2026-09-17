public sealed class MeleeElementSfxResolver
{
    private readonly MeleeElementSfxCatalog catalog;

    public MeleeElementSfxResolver(MeleeElementSfxCatalog catalog)
    {
        this.catalog = catalog;
    }

    public bool TryResolve(
        WeaponElement element,
        MeleeElementSfxCueType cueType,
        out MeleeElementSfxCueSettings settings)
    {
        settings = null;
        return catalog != null && catalog.TryResolve(element, cueType, out settings);
    }
}
