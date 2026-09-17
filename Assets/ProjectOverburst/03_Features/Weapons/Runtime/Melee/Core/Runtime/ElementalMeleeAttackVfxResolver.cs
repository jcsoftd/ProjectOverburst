using UnityEngine;

public sealed class ElementalMeleeAttackVfxResolver : IMeleeAttackVfxResolver
{
    private readonly MeleeElementAttackVfxCatalog catalog;

    public ElementalMeleeAttackVfxResolver(MeleeElementAttackVfxCatalog catalog)
    {
        this.catalog = catalog;
    }

    public GameObject ResolvePrefab(
        MeleeAttackVfxDefinition definition,
        string overrideKey)
    {
        if (MeleeElementAttackVfxCatalog.IsSharedSlashKey(overrideKey))
        {
            return catalog != null
                && catalog.TryResolveSharedSlash(overrideKey, out GameObject sharedPrefab)
                    ? sharedPrefab
                    : null; // 공용 자산 누락은 조용히 미재생
        }

        return definition != null ? definition.neutralPrefab : null;
    }
}
