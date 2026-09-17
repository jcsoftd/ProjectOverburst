using UnityEngine;

public static class MeleeElementSfxBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        MeleeElementSfxCatalog catalog =
            Resources.Load<MeleeElementSfxCatalog>(MeleeElementSfxCatalog.ResourcePath);
        MeleeElementSfxService.Configure(catalog);
    }
}
