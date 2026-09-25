using UnityEngine;

public static class MeleeElementHitVfxService
{
    private static MeleeElementHitVfxCatalog catalog;
    private static bool loadAttempted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        catalog = null;
        loadAttempted = false;
    }

    public static bool CanPlay(WeaponElement element)
    {
        return TryResolve(element, out GameObject prefab)
            && prefab.TryGetComponent(out MeleeElementHitVfxController controller)
            && controller.HasPlayableContent(element);
    }

    public static bool TryPlay(WeaponElement element, Vector3 hitPoint)
    {
        return TryPlay(element, hitPoint, 1f);
    }

    public static bool TryPlay(WeaponElement element, Vector3 hitPoint, float sizeMultiplier)
    {
        if (!TryResolve(element, out GameObject prefab))
            return false;

        MeleeElementHitVfxController prefabController =
            prefab.GetComponent<MeleeElementHitVfxController>();
        if (prefabController == null || !prefabController.HasPlayableContent(element))
            return false;

        GameObject instance = TransientVfxPool.Spawn(
            prefab,
            hitPoint,
            Quaternion.identity,
            catalog.ResolveLifetime(element),
            Mathf.Max(1, catalog.poolCapacity),
            null,
            spawned =>
            {
                MeleeElementHitVfxController controller =
                    spawned.GetComponent<MeleeElementHitVfxController>();
                if (controller == null)
                    throw new MissingComponentException(nameof(MeleeElementHitVfxController));

                controller.StopAndClearVfx();
                controller.SetElement(element); // 활성 전 원소 주입
                spawned.transform.localScale = prefab.transform.localScale
                    * Mathf.Clamp(sizeMultiplier, 0.55f, 1.5f);
            });
        return instance != null;
    }

    private static bool TryResolve(WeaponElement element, out GameObject prefab)
    {
        prefab = null;
        if (!MeleeElementHitVfxCatalog.Supports(element))
            return false;

        EnsureCatalog();
        return catalog != null && catalog.TryResolve(element, out prefab);
    }

    private static void EnsureCatalog()
    {
        if (loadAttempted)
            return;

        loadAttempted = true;
        catalog = Resources.Load<MeleeElementHitVfxCatalog>(
            MeleeElementHitVfxCatalog.ResourcePath);
        if (catalog == null)
        {
            Debug.LogWarning(
                $"[MeleeElementHitVfx] Catalog load failed: Resources/{MeleeElementHitVfxCatalog.ResourcePath}");
        }
    }
}
