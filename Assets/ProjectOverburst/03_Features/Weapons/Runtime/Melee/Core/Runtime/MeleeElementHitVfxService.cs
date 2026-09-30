using UnityEngine;
using System.Collections.Generic;
using Unity.Profiling;

public static class MeleeElementHitVfxService
{
    private static MeleeElementHitVfxCatalog catalog;
    private static bool loadAttempted;
    private static readonly ProfilerMarker PlayMarker = new ProfilerMarker("Overburst.ElementHit.Play");
    public const float UniformHitScale = .65f; // 소형(몸 반경 약 0.5m) 기준 크기

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
        float playbackSpeed = catalog.ResolvePlaybackSpeed(element);
        float hitScale = UniformHitScale * catalog.ResolveHitScale(element);
        GameObject instance = TransientVfxPool.Spawn(
            prefab,
            hitPoint,
            Quaternion.identity,
            catalog.ResolveLifetime(element) / playbackSpeed, // 빨리 재생한 만큼 풀 반환도 앞당긴다
            catalog.ResolvePoolCapacity(element),
            null,
            spawned =>
            {
                MeleeElementHitVfxController controller =
                    spawned.GetComponent<MeleeElementHitVfxController>();
                if (controller == null)
                    throw new MissingComponentException(nameof(MeleeElementHitVfxController));

                controller.SetElement(element); // 활성 전 원소 주입
                controller.SetPlaybackSpeed(playbackSpeed);
                // 2026-09-30: 원소 타격 VFX는 몬스터 크기와 상관없이 소형 기준 크기로 통일한다.
                // (몸 반경 0.5m 소형의 √(0.5/1.2)≈0.65배. 혈흔은 BloodHitVfxService가 몸 크기대로 따로 키운다.)
                // 2026-10-01: 원소끼리 크기를 맞추려 카탈로그의 원소별 배율(불 1.1·빛 0.85)을 곱한다.
                spawned.transform.localScale = prefab.transform.localScale * hitScale;
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
