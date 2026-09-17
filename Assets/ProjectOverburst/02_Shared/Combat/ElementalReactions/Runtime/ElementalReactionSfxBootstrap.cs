using UnityEngine;

public static class ElementalReactionSfxBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        ElementalReactionSfxCatalog catalog =
            Resources.Load<ElementalReactionSfxCatalog>(ElementalReactionSfxCatalog.ResourcePath);
        ElementalReactionSfxService.Configure(catalog);
    }
}
