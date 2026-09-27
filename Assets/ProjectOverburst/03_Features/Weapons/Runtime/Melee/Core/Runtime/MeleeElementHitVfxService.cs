using UnityEngine;
using System.Collections.Generic;
using Unity.Profiling;

public static class MeleeElementHitVfxService
{
    private static MeleeElementHitVfxCatalog catalog;
    private static bool loadAttempted;
    private static readonly ProfilerMarker PlayMarker = new ProfilerMarker("Overburst.ElementHit.Play");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        catalog = null;
        loadAttempted = false;
    }

    // Explicit loading requests expire; equipment owns persistent demand until disabled.
    public static void PrepareForElement(WeaponElement element)
    {
        if (Application.isPlaying && TryResolve(element, out var prefab))
            MeleeElementPoolMaintenance.Request(prefab, catalog.ResolvePrewarmCount(element));
    }

    public static void SetElementDemand(Object owner, WeaponElement element)
    {
        if (!Application.isPlaying || owner == null) return;
        if (TryResolve(element, out var prefab))
            MeleeElementPoolMaintenance.SetOwner(owner, prefab, catalog.ResolvePrewarmCount(element));
        else MeleeElementPoolMaintenance.ReleaseOwner(owner);
    }

    public static void ReleaseElementDemand(Object owner) => MeleeElementPoolMaintenance.ReleaseOwner(owner);

    public static bool IsPrepared(WeaponElement element) => TryResolve(element, out var prefab)
        && MeleeElementPoolMaintenance.IsPrepared(prefab);

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
        using (PlayMarker.Auto())
            return Play(element, hitPoint, sizeMultiplier);
    }

    private static bool Play(WeaponElement element, Vector3 hitPoint, float sizeMultiplier)
    {
        if (!TryResolve(element, out GameObject prefab))
            return false;

        MeleeElementHitVfxController prefabController =
            prefab.GetComponent<MeleeElementHitVfxController>();
        if (prefabController == null || !prefabController.HasPlayableContent(element))
            return false;

        MeleeElementPoolMaintenance.Touch(prefab);
        GameObject instance = TransientVfxPool.Spawn(
            prefab,
            hitPoint,
            Quaternion.identity,
            catalog.ResolveLifetime(element),
            catalog.ResolvePoolCapacity(element),
            null,
            spawned =>
            {
                MeleeElementHitVfxController controller =
                    spawned.GetComponent<MeleeElementHitVfxController>();
                if (controller == null)
                    throw new MissingComponentException(nameof(MeleeElementHitVfxController));

                controller.SetElement(element); // 활성 전 원소 주입
                spawned.transform.localScale = prefab.transform.localScale
                    * Mathf.Clamp(sizeMultiplier, 0.55f, 1.5f);
            },
            useUnscaledTime: true);
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
